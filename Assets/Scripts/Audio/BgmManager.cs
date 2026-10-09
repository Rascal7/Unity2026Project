using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// 씬별 배경음(BGM) 재생기. 씬에 배치할 필요 없이 스스로 부트스트랩되고 씬이 바뀌어도 유지된다.
//  - 곡은 Resources/BGM/<이름> (반복용으로 이음매를 미리 맞춘 WAV). AudioSource.loop 로 무한 반복.
//  - 씬이 바뀌어 곡이 달라지면 크로스페이드, 같은 곡이면(로비 → 캐릭터 선택 → 세이브) 끊김 없이 이어 재생.
//  - 컷씬 씬에서는 영상 소리와 겹치지 않도록 페이드 아웃.
//  - 상황별 자동 조절(Update):
//      · 음성 스킬: 마이크에 목소리가 들어오는 동안 배경음을 낮춘다(덕킹) → 스피커 소리가 다시 마이크로 들어가 오인식되는 것 방지
//      · 설정창 열림: 배경음을 낮추고 먹먹하게(로우패스)
//      · 스테이지 클리어 결과 화면: 배경음을 낮추고 클리어 징글(Resources/SFX/Jingle_StageClear, Jingle_AllClear) 재생
//  - 창이 비활성(Alt+Tab 등)이면 전체 소리를 끈다 (설정창 '백그라운드 음소거', 기본 켜짐).
//  - 최종 음량 = 마스터(AudioListener.volume) × 배경음 설정(Volume) × BaseGain × 상황 배율.
public class BgmManager : MonoBehaviour
{
    public static BgmManager Instance { get; private set; }

    public const string VolumePrefKey = "BgmVolume";
    public const string MuteInBackgroundPrefKey = "MuteInBackground";
    public const float DefaultVolume = 0.7f;   // 설정창 배경음 슬라이더 기본값
    private const float BaseGain = 0.5f;       // 배경음은 효과음보다 작게: 슬라이더 100% 여도 이 배율까지만
    private const float FadeOutTime = 1.0f;
    private const float FadeInTime = 1.5f;

    // 상황별 배율
    private const float VoiceDuckLevel = 0.5f;    // 말하는 동안
    private const float VoiceDuckMinOpen = 0.15f; // 이보다 짧은 소리(키보드·잡음 순간 피크)에는 반응하지 않음
    private const float VoiceDuckHold = 0.6f;     // 말이 끝난 뒤 이만큼 유지한 다음 복귀
    private const float VoiceDuckAttack = 0.12f;  // 내려가는 시간 (빠르게)
    private const float VoiceDuckRelease = 0.8f;  // 올라오는 시간 (천천히)
    private const float MenuLevel = 0.5f;         // 설정창 열림
    private const float MenuLowPassHz = 1200f;
    private const float ResultLevel = 0.3f;       // 클리어 결과 화면
    private const float ModSmoothTime = 0.35f;

    // 씬 이름 → 곡 이름 (null = 배경음 끄기). 표에 없는 씬(테스트 씬 등)은 지금 곡을 그대로 유지한다.
    private static readonly Dictionary<string, string> SceneTracks = new Dictionary<string, string>
    {
        { "Lobby", "Lobby" },
        { "CharacterSelect", "Lobby" },
        { "savefile", "Lobby" },
        { "TutorialStage", "Tutorial" },
        { "Main Stage1", "Stage1" },
        { "Main Stage2", "Stage2" },
        { "Main Stage3", "Stage3" },
        { "OpeningCutscene", null },
        { "EndingCutscene", null },
    };

    private AudioSource[] _sources;
    private float[] _fade;          // 소스별 페이드 배율 (0..1)
    private int _active;            // 지금 곡을 재생 중인 소스 인덱스
    private string _currentTrack;
    private Coroutine _co;
    private float _volume;

    private AudioLowPassFilter _lowPass;
    private float _duck = 1f;       // 음성 덕킹 배율
    private float _lastSpeakTime = -999f;
    private float _speakStart = -1f;
    private float _situation = 1f;  // 설정창/결과 화면 배율 (부드럽게 보간)
    private float _situationVel;
    private int _lastResultPhase = -1;
    private bool _muteInBackground;
    private bool _hasFocus = true;

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(VolumePrefKey, _volume);
            ApplyVolumes();
        }
    }

    public bool MuteInBackground
    {
        get => _muteInBackground;
        set
        {
            _muteInBackground = value;
            PlayerPrefs.SetInt(MuteInBackgroundPrefKey, value ? 1 : 0);
            ApplyFocusMute();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("BgmManager");
        go.AddComponent<BgmManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        _volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumePrefKey, DefaultVolume));
        _muteInBackground = PlayerPrefs.GetInt(MuteInBackgroundPrefKey, 1) == 1;
        _sources = new AudioSource[2];
        _fade = new float[2];
        for (int i = 0; i < 2; i++)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = true;
            s.spatialBlend = 0f;
            s.priority = 0;            // 효과음이 많아도 배경음이 끊기지 않도록 최우선
            s.ignoreListenerPause = true;
            s.enabled = false;         // 곡이 없는 대기 소스는 꺼 둔다 (오디오 리셋 때 빈 소스를 재생하려는 경고 방지)
            _sources[i] = s;
        }
        _lowPass = gameObject.AddComponent<AudioLowPassFilter>(); // 같은 오브젝트의 두 소스 모두에 적용
        _lowPass.cutoffFrequency = 22000f;
        ApplyVolumes();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        // 부트스트랩 시점에 이미 활성화된 첫 씬은 sceneLoaded 가 불리지 않으므로 여기서 한 번 처리.
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    // 출력 장치 변경 등으로 AudioSettings.Reset 이 불리면 재생 중이던 모든 소리가 멈춘다 → 같은 곡을 같은 위치부터 다시 재생.
    private float _resumeTime;
    private void OnEnable() => AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
    private void OnDisable() => AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
    private void OnAudioConfigurationChanged(bool deviceWasChanged)
    {
        if (isActiveAndEnabled) StartCoroutine(ResumeAfterReset());
    }

    private IEnumerator ResumeAfterReset()
    {
        yield return null;
        // 리셋 후 Unity 가 비어 있는 대기 소스까지 재생하려 드는 경우가 있어 먼저 정리
        foreach (var s in _sources) if (s.clip == null) s.Stop();
        var src = _sources[_active];
        if (src.clip == null || src.isPlaying || _currentTrack == null) yield break;
        if (src.clip.loadState != AudioDataLoadState.Loaded) src.clip.LoadAudioData();
        float waitUntil = Time.unscaledTime + 10f;
        while (src.clip.loadState != AudioDataLoadState.Loaded && Time.unscaledTime < waitUntil) yield return null;
        src.Play();
        src.time = Mathf.Repeat(_resumeTime, src.clip.length);
        Debug.Log($"[BGM] 오디오 장치 재설정 후 {_currentTrack} 이어서 재생 ({_resumeTime:0.0}s)");
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        _lastResultPhase = -1;
        if (SceneTracks.TryGetValue(scene.name, out var track)) Play(track);
    }

    // ---------- 창 포커스: 백그라운드 음소거 ----------
    private void OnApplicationFocus(bool focus)
    {
        _hasFocus = focus;
        ApplyFocusMute();
    }

    private void ApplyFocusMute()
    {
        // 전체 소리(효과음 포함)를 끄기 위해 마스터 음량을 0 으로. 돌아오면 저장된 마스터 음량으로 복원.
        float master = PlayerPrefs.GetFloat("MasterVolume", 1f);
        AudioListener.volume = (_muteInBackground && !_hasFocus) ? 0f : master;
    }

    // ---------- 상황별 자동 조절 ----------
    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        var activeSrc = _sources[_active];
        if (activeSrc.clip != null && activeSrc.isPlaying) _resumeTime = activeSrc.time;

        // 1) 음성 덕킹 (게이트가 VoiceDuckMinOpen 이상 연속으로 열려 있을 때만)
        if (VoiceSkillInput.IsSpeaking)
        {
            if (_speakStart < 0f) _speakStart = Time.unscaledTime;
            if (Time.unscaledTime - _speakStart >= VoiceDuckMinOpen) _lastSpeakTime = Time.unscaledTime;
        }
        else _speakStart = -1f;
        bool duck = Time.unscaledTime - _lastSpeakTime < VoiceDuckHold;
        float duckTarget = duck ? VoiceDuckLevel : 1f;
        float duckTime = duck ? VoiceDuckAttack : VoiceDuckRelease;
        _duck = Mathf.MoveTowards(_duck, duckTarget, dt * (1f - VoiceDuckLevel) / duckTime);

        // 2) 설정창 / 3) 클리어 결과 화면
        bool menuOpen = SettingsMenuUI.Instance != null && SettingsMenuUI.Instance.IsOpen;
        int phase = NetworkTimer.Instance != null ? NetworkTimer.Instance.Phase : -1;
        bool result = phase == NetworkTimer.PhaseAwaitingChoice || phase == NetworkTimer.PhaseFinished;
        if (result && phase != _lastResultPhase) PlayClearJingle(phase == NetworkTimer.PhaseFinished);
        _lastResultPhase = result ? phase : -1;

        float target = 1f;
        if (result) target = Mathf.Min(target, ResultLevel);
        if (menuOpen) target = Mathf.Min(target, MenuLevel);
        _situation = Mathf.SmoothDamp(_situation, target, ref _situationVel, ModSmoothTime, Mathf.Infinity, dt);

        float cutoff = menuOpen ? MenuLowPassHz : 22000f;
        _lowPass.cutoffFrequency = Mathf.Lerp(_lowPass.cutoffFrequency, cutoff, 1f - Mathf.Exp(-dt * 8f));
        _lowPass.enabled = _lowPass.cutoffFrequency < 21000f; // 평소에는 필터를 꺼서 음질 손실 없음

        ApplyVolumes();
    }

    private static void PlayClearJingle(bool allClear)
    {
        // 징글 음원이 아직 없으면 조용히 넘어간다 (Resources/SFX 에 파일만 넣으면 재생됨)
        if (allClear && SfxPlayer.Play("Jingle_AllClear")) return;
        SfxPlayer.Play("Jingle_StageClear");
    }

    // ---------- 곡 전환 ----------
    // 곡 전환. null 이면 페이드 아웃. 이미 재생 중인 곡이면 아무것도 하지 않는다(이어 재생).
    public void Play(string track)
    {
        if (track == _currentTrack) return;

        AudioClip clip = null;
        if (!string.IsNullOrEmpty(track))
        {
            clip = Resources.Load<AudioClip>("BGM/" + track);
            if (clip == null) Debug.LogWarning($"[BGM] Resources/BGM/{track} 을 찾을 수 없습니다.");
        }
        _currentTrack = clip != null ? track : null;

        if (_co != null) StopCoroutine(_co);
        _co = StartCoroutine(Crossfade(clip));
    }

    private IEnumerator Crossfade(AudioClip next)
    {
        int from = _active;
        int to = 1 - _active;

        if (next != null)
        {
            // 스트리밍 클립은 데이터를 먼저 불러와야 재생된다 (그동안 이전 곡은 그대로 재생)
            if (next.loadState != AudioDataLoadState.Loaded) next.LoadAudioData();
            float waitUntil = Time.unscaledTime + 10f; // 에디터가 백그라운드일 때 첫 로드가 수 초 걸릴 수 있다
            while (next.loadState == AudioDataLoadState.Loading || next.loadState == AudioDataLoadState.Unloaded)
            {
                if (Time.unscaledTime > waitUntil) { Debug.LogWarning($"[BGM] {next.name} 로드 대기 시간 초과"); break; }
                yield return null;
            }

            var src = _sources[to];
            src.enabled = true;
            src.Stop();
            src.clip = next;
            _fade[to] = 0f;
            ApplyVolumes();
            src.Play();
            _active = to;
        }

        float t0 = Time.unscaledTime;
        float fromStart = _fade[from];
        float dur = Mathf.Max(FadeOutTime, next != null ? FadeInTime : 0f);
        while (true)
        {
            float e = Time.unscaledTime - t0;
            _fade[from] = fromStart * (1f - Mathf.Clamp01(e / FadeOutTime));
            if (next != null) _fade[to] = Mathf.Clamp01(e / FadeInTime);
            ApplyVolumes();
            if (e >= dur) break;
            yield return null;
        }

        // 페이드 아웃이 끝난 이전 곡 정지 (next 가 null 이면 from == _active 이므로 지금 곡도 함께 정지)
        _sources[from].Stop();
        _sources[from].clip = null;
        _sources[from].enabled = false;
        _co = null;
    }

    private void ApplyVolumes()
    {
        if (_sources == null) return;
        float mod = _duck * _situation;
        for (int i = 0; i < _sources.Length; i++)
            _sources[i].volume = _fade[i] * _volume * BaseGain * mod;
    }
}
