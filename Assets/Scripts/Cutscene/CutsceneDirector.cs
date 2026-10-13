using System.Collections;
using Fusion;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

// 컷씬 씬(OpeningCutscene / EndingCutscene)에 하나 배치하는 재생기.
//  - 영상 준비 → (멀티) CutsceneSync 로 두 플레이어 준비 확인 → 같은 tick 에 페이드 인 + 재생
//  - 재생 skipButtonDelay 초 뒤 오른쪽 아래에 [건너뛰기] 표시. 멀티에서는 전원이 눌러야 넘어간다.
//  - 끝나면 페이드 아웃 후 nextSceneName 으로(오프닝 → 튜토리얼) 또는 로비로(엔딩).
//  - 러너가 없으면(솔로 테스트) 동기화 없이 바로 재생한다.
public class CutsceneDirector : MonoBehaviour
{
    public static CutsceneDirector Instance { get; private set; }

    [Header("영상")]
    public VideoClip clip;

    [Header("종료 후 이동")]
    [Tooltip("체크하면 영상이 끝난 뒤 네트워크 세션을 종료하고 로비로 돌아간다 (엔딩)")]
    public bool returnToLobby = false;
    public string nextSceneName = "TutorialStage";
    public string lobbySceneName = "Lobby";

    [Header("타이밍")]
    public float skipButtonDelay = 5f;   // 재생 시작 후 건너뛰기 버튼이 나타날 때까지 (초)
    public float readyTimeout = 10f;     // 상대 피어 준비를 기다리는 최대 시간 (초)

    public float ClipLength => clip != null ? (float)clip.length : 0f;

    // ----- 테마 팔레트 (TutorialFlowController 와 동일 톤) -----
    private static readonly Color WoodFill  = new Color(0.42f, 0.28f, 0.16f, 0.92f);
    private static readonly Color WoodDark  = new Color(0.19f, 0.11f, 0.05f, 0.92f);
    private static readonly Color Parch     = new Color(0.98f, 0.95f, 0.89f, 1f);

    private VideoPlayer _video;
    private RenderTexture _rt;
    private GameObject _skipBtn;
    private Button _skipButton;
    private Text _skipLabel;

    private NetworkRunner _runner;
    private bool _networked;
    private bool _syncSpawnRequested;
    private bool _prepared;
    private bool _readySent;
    private bool _playing;
    private bool _videoEnded;
    private bool _skipRequested;
    private bool _finishing;
    private float _startTime;
    private float _playStartTime;

    private void Awake()
    {
        Instance = this;
        if (ScreenFader.Instance != null) ScreenFader.Instance.SetBlack(); // 재생 시작 전까지 검은 화면 유지
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_rt != null) { _rt.Release(); Destroy(_rt); }
    }

    private void Start()
    {
        _startTime = Time.unscaledTime;
        SetupVideo(); // RenderTexture 를 먼저 만들어야 UI 의 RawImage 에 연결된다
        BuildUI();

        _runner = FindFirstObjectByType<NetworkRunner>();
        _networked = _runner != null && _runner.IsRunning;

        if (clip == null)
        {
            Debug.LogError("[Cutscene] VideoClip 이 비어 있습니다. 바로 다음으로 넘어갑니다.");
            _prepared = true;
            _videoEnded = true;
        }
        else
        {
            _video.Prepare();
        }
    }

    // 호스트: CutsceneSync 생성. Start() 는 Fusion 이 씬 로드를 끝내기 전에 불리므로
    // 그 시점의 동기 Spawn 은 실패한다("Failed to load prefab synchronously") → 씬 로드가 끝난 뒤 비동기 Spawn.
    private void TrySpawnSync()
    {
        if (_syncSpawnRequested || !_networked || _runner == null || !_runner.IsRunning || !_runner.IsServer) return;
        if (CutsceneSync.Instance != null) { _syncSpawnRequested = true; return; }
        if (_runner.SceneManager != null && _runner.SceneManager.IsBusy) return;

        _syncSpawnRequested = true;
        var prefab = Resources.Load<NetworkObject>("CutsceneSync");
        if (prefab != null) _runner.SpawnAsync(prefab);
        else Debug.LogError("[Cutscene] Resources/CutsceneSync 프리팹이 없습니다. 동기화 없이 재생합니다.");
    }

    private void Update()
    {
        if (_finishing) return;
        if (BasicLauncher.Instance != null && BasicLauncher.Instance.IsDisconnectModalOpen)
        {
            if (_video != null && _video.isPlaying) _video.Pause();
            return;
        }
        TrySpawnSync();
        var sync = CutsceneSync.Instance;

        if (!_networked)
        {
            if (_prepared && !_playing) StartPlayback();
            if (_videoEnded || _skipRequested) Finish();
        }
        else if (sync != null)
        {
            if (_prepared && !_readySent) { sync.SendReady(); _readySent = true; }
            if (sync.HasStarted && !_playing) StartPlayback();
            if (sync.State == CutsceneSync.StateDone) { Finish(); return; }
            UpdateSkipLabel(sync);
        }
        else if (_prepared && !_playing && Time.unscaledTime - _startTime > readyTimeout + 5f)
        {
            // 동기화 오브젝트가 끝내 오지 않으면 혼자라도 재생 (멈춘 화면 방지)
            Debug.LogWarning("[Cutscene] CutsceneSync 를 찾지 못해 로컬로 재생합니다.");
            StartPlayback();
        }

        if (_playing && !_skipBtn.activeSelf && !_skipRequested && !_videoEnded
            && Time.unscaledTime - _playStartTime >= skipButtonDelay)
        {
            _skipBtn.SetActive(true);
        }

        // 동기화 없이 재생 중인 경우의 종료 처리
        if (_networked && sync == null && _playing && (_videoEnded || _skipRequested)) Finish();
    }

    // ---------- 재생 ----------
    private void SetupVideo()
    {
        _rt = new RenderTexture(1920, 1080, 0);
        _rt.Create();

        _video = gameObject.AddComponent<VideoPlayer>();
        _video.playOnAwake = false;
        _video.isLooping = false;
        _video.source = VideoSource.VideoClip;
        _video.clip = clip;
        _video.renderMode = VideoRenderMode.RenderTexture;
        _video.targetTexture = _rt;
        _video.skipOnDrop = true;

        var audio = gameObject.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        _video.audioOutputMode = VideoAudioOutputMode.AudioSource;
        _video.controlledAudioTrackCount = 1;
        _video.EnableAudioTrack(0, true);
        _video.SetTargetAudioSource(0, audio);

        _video.prepareCompleted += _ => _prepared = true;
        _video.loopPointReached += _ => OnVideoEnded();
        _video.errorReceived += (_, msg) =>
        {
            Debug.LogError("[Cutscene] 영상 오류: " + msg);
            _prepared = true;
            OnVideoEnded();
        };
    }

    private void StartPlayback()
    {
        _playing = true;
        _playStartTime = Time.unscaledTime;
        if (clip != null && _video.isPrepared) _video.Play();
        if (ScreenFader.Instance != null) ScreenFader.Instance.FadeIn();
    }

    private void OnVideoEnded()
    {
        if (_videoEnded) return;
        _videoEnded = true;
        if (_skipBtn != null) _skipBtn.SetActive(false);
        if (_networked && CutsceneSync.Instance != null) CutsceneSync.Instance.SendFinished();
    }

    private void OnClickSkip()
    {
        if (_skipRequested) return;
        _skipRequested = true;
        _skipButton.interactable = false;
        if (_networked && CutsceneSync.Instance != null) CutsceneSync.Instance.SendSkip();
        else _skipBtn.SetActive(false);
    }

    private void UpdateSkipLabel(CutsceneSync sync)
    {
        if (_skipLabel == null || sync.PlayerCount <= 1) return;
        _skipLabel.text = _skipRequested || sync.SkipCount > 0
            ? $"건너뛰기 대기 {sync.SkipCount}/{sync.PlayerCount}"
            : "건너뛰기 ▶";
        // 상대가 먼저 눌렀으면 아직 안 누른 쪽에도 버튼을 바로 보여준다
        if (sync.SkipCount > 0 && !_skipRequested && _playing && !_videoEnded) _skipBtn.SetActive(true);
    }

    // ---------- 종료 ----------
    private void Finish()
    {
        if (_finishing) return;
        _finishing = true;
        if (_skipBtn != null) _skipBtn.SetActive(false);
        StartCoroutine(FinishRoutine());
    }

    private IEnumerator FinishRoutine()
    {
        if (ScreenFader.Instance != null) ScreenFader.Instance.FadeOut();
        yield return new WaitForSecondsRealtime(ScreenFader.DefaultDuration + 0.1f);
        if (_video != null) _video.Stop();

        var runner = FindFirstObjectByType<NetworkRunner>();
        bool networked = runner != null && runner.IsRunning;

        if (returnToLobby)
        {
            // 클라이언트가 먼저 정상 종료(ShutdownReason.Ok)하도록 호스트는 잠깐 기다린다.
            // (호스트가 먼저 끊으면 클라이언트에 '연결 종료' 안내가 뜬다)
            if (networked && runner.IsServer) yield return new WaitForSecondsRealtime(1f);
            ReturnToLobby();
            yield break;
        }

        if (!networked)
        {
            SceneManager.LoadScene(nextSceneName);
        }
        else if (runner.IsServer)
        {
            int idx = SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{nextSceneName}.unity");
            if (idx >= 0) runner.LoadScene(SceneRef.FromIndex(idx));
            else runner.LoadScene(nextSceneName, LoadSceneMode.Single);
        }
        // 클라이언트는 호스트의 씬 로드를 따라간다 (검은 화면 유지 → 새 씬에서 ScreenFader 가 페이드 인)
    }

    private async void ReturnToLobby()
    {
        BasicLauncher.MarkVoluntaryExit();
        RunManager.ResetRun();
        CheckpointManager.ResetCheckpoint(); // 리스폰 지점(정적) 잔존 방지 (GameEndController.OnLobby 와 동일)
        foreach (var r in FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None))
        {
            if (r == null) continue;
            await r.Shutdown();
            if (r != null) Destroy(r.gameObject);
        }
        SceneManager.LoadScene(lobbySceneName);
    }

    // ---------- 런타임 UI (legacy uGUI) ----------
    private void BuildUI()
    {
        if (FindFirstObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        Font font = Resources.Load<Font>("Sunflower-Medium");
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var root = new GameObject("CutsceneCanvas");
        root.transform.SetParent(transform, false);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32600; // ScreenFader(32700) 아래
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();

        // 검은 배경 + 16:9 영상 (화면 비율이 달라도 레터박스로 맞춤)
        var bg = NewChild("BG", root.transform);
        bg.AddComponent<Image>().color = Color.black;
        Stretch(bg.GetComponent<RectTransform>());

        var videoGo = NewChild("Video", bg.transform);
        var raw = videoGo.AddComponent<RawImage>();
        raw.texture = _rt;
        raw.raycastTarget = false;
        Stretch(videoGo.GetComponent<RectTransform>());
        var fitter = videoGo.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = 16f / 9f;

        // 오른쪽 아래 [건너뛰기]
        _skipBtn = NewChild("SkipButton", root.transform);
        var brt = _skipBtn.GetComponent<RectTransform>();
        brt.anchorMin = brt.anchorMax = new Vector2(1f, 0f);
        brt.pivot = new Vector2(1f, 0f);
        brt.sizeDelta = new Vector2(300f, 76f);
        brt.anchoredPosition = new Vector2(-48f, 48f);
        var border = _skipBtn.AddComponent<Image>();
        border.color = WoodDark;
        var face = NewChild("Face", _skipBtn.transform);
        var frt = face.GetComponent<RectTransform>();
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(5f, 5f); frt.offsetMax = new Vector2(-5f, -5f);
        var faceImg = face.AddComponent<Image>();
        faceImg.color = WoodFill;
        faceImg.raycastTarget = false;
        _skipButton = _skipBtn.AddComponent<Button>();
        _skipButton.targetGraphic = border;
        _skipButton.onClick.AddListener(OnClickSkip);

        var labelGo = NewChild("Label", _skipBtn.transform);
        _skipLabel = labelGo.AddComponent<Text>();
        _skipLabel.font = font;
        _skipLabel.fontSize = 32;
        _skipLabel.alignment = TextAnchor.MiddleCenter;
        _skipLabel.color = Parch;
        _skipLabel.raycastTarget = false;
        _skipLabel.text = "건너뛰기 ▶";
        Stretch(labelGo.GetComponent<RectTransform>());

        _skipBtn.SetActive(false);
    }

    private static GameObject NewChild(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
