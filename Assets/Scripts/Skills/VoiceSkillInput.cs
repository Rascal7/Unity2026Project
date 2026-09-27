using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using Vosk;

/// <summary>
/// Vosk 오프라인 음성 인식 엔진을 사용하여 마이크 입력을 처리하는 로컬 전용 컴포넌트.
/// - 설정 메뉴(SettingsMenuUI)에서 선택한 마이크로부터 PCM 오디오를 스트리밍합니다.
/// - 전용 백그라운드 스레드에서 문법(Grammar) 기반으로 "점프", "대시" 등의 키워드를 감지합니다.
/// - 키워드가 인식되면 activeWindow 동안 IsSkillRequested 가 true 가 되어 Spawner.OnInput 으로 전달됩니다.
/// - 씬 배선이 필요 없도록 런타임에 스스로 부트스트랩합니다.
///
/// [PC 마다 다르게 동작하던 문제에 대한 대응]
/// 1) 발화 감지 임계값을 고정하지 않고, 마이크 시작 시 주변 소음을 직접 측정해서 잡습니다.
///    내장 마이크나 AGC 가 켜진 장치는 무음 상태 음량이 고정 임계값을 그냥 넘어서, 게이트가 영구히
///    열린 채로 오디오가 끊임없이 흘러들어가 무한 스킬 발동 + CPU 점유의 원인이 되었습니다.
/// 2) 부분 결과(PartialResult)로는 발동하지 않습니다. 문법 인식기는 어떤 소리든 등록된 단어로
///    스냅시키기 때문에 부분 가설을 믿으면 잡음이 그대로 스킬이 됩니다. 최종 결과 + 단어 신뢰도로만 판정합니다.
/// 3) 처리 대기 큐에 상한을 둡니다. 디코딩이 실시간을 못 따라가는 PC 에서 큐가 무한정 커지며 메모리가 터졌습니다.
/// </summary>
public class VoiceSkillInput : MonoBehaviour
{
    public static VoiceSkillInput Instance { get; private set; }

    [Serializable]
    public struct KeywordSlot
    {
        public string keyword;
        [Range(0, 3)] public int slot;
    }

    [Header("키워드 → 스킬 슬롯")]
    [Tooltip("여기 적힌 단어로 Vosk 문법이 만들어지고, 인식 결과도 이 목록으로만 매칭됩니다.")]
    public List<KeywordSlot> keywordSlots = new List<KeywordSlot>
    {
        new KeywordSlot { keyword = "대시", slot = 1 }, // 여우 ch1
        new KeywordSlot { keyword = "당근", slot = 1 }, // 토끼 ch1
        new KeywordSlot { keyword = "킁킁", slot = 2 }, // 여우 ch2
        new KeywordSlot { keyword = "점프", slot = 2 }, // 토끼 ch2
        new KeywordSlot { keyword = "체인지", slot = 3 }, // 여우 ch3 (change: 위치 바꾸기)
        new KeywordSlot { keyword = "파워업", slot = 3 }, // 토끼 ch3 (power up: 다음 스킬 강화)
    };

    [Header("인식 판정")]
    [Tooltip("키워드 인식 후 스킬 입력이 유지되는 시간(초).")]
    public float activeWindow = 0.2f;

    [Tooltip("한 번 발동한 뒤 다시 발동되기까지의 최소 간격(초). 같은 발화가 두 번 먹는 것을 막습니다.")]
    public float retriggerGuard = 0.35f;

    [Tooltip("Vosk 단어 신뢰도 하한(0~1). 낮추면 잘 인식되지만 오인식이 늘어납니다.")]
    [Range(0f, 1f)] public float minConfidence = 0.75f;

    [Header("음성 감지(VAD) - 주변 소음 자동 캘리브레이션")]
    [Tooltip("마이크 시작 후 이 시간 동안 주변 소음 크기를 측정해서 기준을 잡습니다.")]
    public float calibrationSeconds = 0.7f;

    [Tooltip("소음 바닥 대비 이 배수를 넘으면 발화 시작으로 봅니다.")]
    public float openMultiplier = 3.5f;

    [Tooltip("소음 바닥 대비 이 배수 아래로 떨어지면 발화 종료로 봅니다(히스테리시스).")]
    public float closeMultiplier = 2.0f;

    [Tooltip("소음 바닥이 아무리 낮게 측정돼도 이 음량 아래 소리는 발화로 치지 않습니다.")]
    public float absoluteFloor = 0.012f;

    [Tooltip("발화가 끝난 뒤 단어 끝이 잘리지 않도록 더 듣는 시간(초).")]
    public float hangoverSeconds = 0.35f;

    [Tooltip("게이트가 이 시간 이상 연속으로 열려 있으면 말이 아니라 지속 소음으로 보고 강제로 닫습니다(폭주 방지).")]
    public float maxOpenSeconds = 5f;

    [Header("버퍼")]
    [Tooltip("Vosk 처리 대기 큐의 최대 길이(초). 초과분은 오래된 것부터 버립니다.")]
    public float maxQueueSeconds = 1.0f;

    // ---------------- 공개 상태 ----------------

    /// <summary>현재 음성으로 스킬이 요청된 상태인가.</summary>
    public static bool IsSkillRequested =>
        Instance != null && Time.time <= Instance._activeUntil;

    /// <summary>현재 음성으로 요청된 스킬 슬롯(1/2/3), 없으면 0.</summary>
    public static int RequestedSlot =>
        (Instance != null && Time.time <= Instance._activeUntil) ? Instance._requestedSlot : 0;

    /// <summary>마지막으로 인식된 키워드(디버그/캐릭터별 분기용).</summary>
    public static string LastKeyword => Instance != null ? Instance._lastKeyword : "";

    /// <summary>현재 녹음 중인 마이크 장치명</summary>
    public string CurrentMicrophone => _currentDeviceName;

    private float _activeUntil = -1f;
    private string _lastKeyword = "";
    private int _requestedSlot;
    private float _lastAcceptTime = -999f;

    // ---------------- 마이크 ----------------

    private const int VOSK_RATE = 16000;       // Vosk 표준 16kHz
    private const int MIC_BUFFER_SECONDS = 1;  // 마이크 링버퍼 길이

    private string _currentDeviceName = "";
    private string _requestedDeviceName = "";  // 설정창에서 고른 장치
    private AudioClip _micClip;
    private int _captureRate = VOSK_RATE;      // 장치가 실제로 지원하는 캡처 레이트
    private int _lastReadPos = -1;             // -1 = 아직 첫 데이터가 들어오지 않음
    private float _micStartedAt;
    private bool _isListening;
    private bool _isPaused;

    // 마이크 열기 실패 시 지수 백오프. 프레임마다 재시도하면 그 자체로 렉이 됩니다.
    private float _nextMicAttemptAt;
    private int _micFailCount;

    // ---------------- VAD ----------------

    private bool _calibrating;
    private float _calibrationT;
    private float _calibrationPeak;
    private float _noiseFloor = 0.01f;
    private bool _isSpeaking;
    private float _silenceT;
    private float _openT;
    private float _recentPeak;

    private float OpenThreshold => Mathf.Max(absoluteFloor, _noiseFloor * openMultiplier);
    private float CloseThreshold => Mathf.Max(absoluteFloor * 0.7f, _noiseFloor * closeMultiplier);

    // ---------------- 리샘플러 (캡처 레이트 → 16kHz) ----------------

    private float _resamplePos;
    private float _resampleSum;
    private int _resampleCount;

    // ---------------- 스레드 ----------------

    // buffer == null 이면 "발화가 끝났으니 최종 결과를 확정하라"는 플러시 신호입니다.
    private struct PcmChunk { public short[] buffer; public int length; }

    private readonly ConcurrentQueue<PcmChunk> _pcmQueue = new ConcurrentQueue<PcmChunk>();
    private readonly ConcurrentBag<short[]> _bufferPool = new ConcurrentBag<short[]>();
    private int _queuedSamples;

    private volatile bool _running;
    private volatile bool _isVoskReady;
    private Thread _worker;

    private readonly object _keywordLock = new object();
    private string _pendingKeyword;

    private Model _model;
    private VoskRecognizer _recognizer;

    // 워커 스레드에서만 읽는 읽기 전용 스냅샷 (메인 스레드에서 미리 만들어 둡니다)
    private Dictionary<string, int> _slotByKeyword;

    // 메인 스레드 재사용 버퍼 (프레임마다 새 배열을 할당하지 않습니다)
    private float[] _floatScratch = new float[VOSK_RATE];

    private float _micSens = 1f;
    private float _micSensRefreshAt;

    private bool _shutdown;

    // ---------------- 부트스트랩 ----------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("VoiceSkillInput");
        DontDestroyOnLoad(go);
        go.AddComponent<VoiceSkillInput>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        _micSens = PlayerPrefs.GetFloat("MicSensitivity", 1.0f);

        string modelPath = Path.Combine(Application.streamingAssetsPath, "vosk-model-small-ko-0.22");
        if (!Directory.Exists(modelPath))
        {
            Debug.LogWarning($"[VoiceSkillInput] Vosk 모델 폴더를 찾을 수 없습니다: {modelPath}. 키보드(LeftShift/LeftControl/LeftAlt)로 대체 입력 가능합니다.");
            return;
        }

        // 키워드 테이블과 문법은 메인 스레드에서 미리 확정합니다 (워커가 컴포넌트 필드를 건드리지 않도록).
        _slotByKeyword = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var ks in keywordSlots)
        {
            if (string.IsNullOrWhiteSpace(ks.keyword)) continue;
            _slotByKeyword[ks.keyword.Trim()] = Mathf.Clamp(ks.slot, 0, 3);
        }
        if (_slotByKeyword.Count == 0)
        {
            Debug.LogWarning("[VoiceSkillInput] 등록된 키워드가 없습니다. 음성 입력을 비활성화합니다.");
            return;
        }

        string grammar = BuildGrammar();
        float threshold = minConfidence;

        _running = true;
        _worker = new Thread(() => WorkerMain(modelPath, grammar, threshold))
        {
            IsBackground = true,
            Name = "VoskWorker"
        };
        _worker.Start();
    }

    private string BuildGrammar()
    {
        var sb = new StringBuilder("[");
        foreach (var kw in _slotByKeyword.Keys)
        {
            sb.Append('"').Append(kw).Append("\", ");
        }
        sb.Append("\"[unk]\"]");
        return sb.ToString();
    }

    private int SlotForKeyword(string kw)
    {
        if (_slotByKeyword != null && kw != null && _slotByKeyword.TryGetValue(kw, out int s)) return s;
        return 0;
    }

    // ---------------- 워커 스레드 ----------------

    private void WorkerMain(string modelPath, string grammar, float confThreshold)
    {
        try
        {
            Vosk.Vosk.SetLogLevel(-1);
            _model = new Model(modelPath);
            _recognizer = new VoskRecognizer(_model, VOSK_RATE, grammar);
            _recognizer.SetWords(true); // 단어별 conf 를 받기 위해 필요합니다
            _isVoskReady = true;
            Debug.Log("<color=cyan>[VoiceSkillInput]</color> Vosk 한국어 음성 모델 초기화 완료!");
        }
        catch (Exception e)
        {
            Debug.LogError($"[VoiceSkillInput] Vosk 초기화 실패: {e.Message}");
            return;
        }

        while (_running)
        {
            if (!_pcmQueue.TryDequeue(out PcmChunk chunk))
            {
                Thread.Sleep(8);
                continue;
            }

            try
            {
                if (chunk.buffer == null)
                {
                    // 발화 종료 → 여기서 한 번만 최종 판정합니다.
                    Evaluate(_recognizer.FinalResult(), confThreshold);
                    continue;
                }

                Interlocked.Add(ref _queuedSamples, -chunk.length);

                // PartialResult 는 쓰지 않습니다. 문법 인식기의 부분 가설은 잡음을 그대로 키워드로 만들어냅니다.
                if (_recognizer.AcceptWaveform(chunk.buffer, chunk.length))
                {
                    Evaluate(_recognizer.Result(), confThreshold);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[VoiceSkillInput] 음성 인식 처리 중 오류: {e.Message}");
            }
            finally
            {
                if (chunk.buffer != null) _bufferPool.Add(chunk.buffer);
            }
        }
    }

    private void Evaluate(string json, float confThreshold)
    {
        string kw = ExtractKeyword(json, confThreshold);
        if (string.IsNullOrEmpty(kw)) return;

        // _recognizer.Reset() 제거: 이 json 은 Result()/FinalResult() 에서 온 것이고,
        // 두 호출 모두 Vosk 내부에서 디코더를 이미 리셋한다. 그 직후 Reset() 을 또 부르면
        // libvosk 의 FinalizeDecoding 에서 네이티브 크래시(SIGSEGV)가 발생했다.
        // 중복 발동은 아래 DrainQueue + Update() 의 retriggerGuard(0.35s) 로 충분히 막힌다.
        DrainQueue(); // 남은 잔여 오디오를 비워 연속 오발동을 막습니다

        lock (_keywordLock)
        {
            _pendingKeyword = kw;
        }
    }

    /// <summary>
    /// 최종 결과 JSON 에서 유효한 키워드를 뽑습니다. 하나라도 조건을 못 맞추면 null.
    /// 조건: [unk] 아님 / 등록된 키워드 / 단어 하나뿐 / 신뢰도 임계 이상.
    /// </summary>
    private string ExtractKeyword(string json, float confThreshold)
    {
        if (string.IsNullOrEmpty(json)) return null;

        string text = ReadJsonString(json, "text");
        if (string.IsNullOrEmpty(text)) return null;

        text = text.Trim();
        if (text.IndexOf("[unk]", StringComparison.Ordinal) >= 0) return null;

        // 한 번의 발화 = 키워드 하나. "대시 대시 대시" 같은 잡음 연쇄는 버립니다.
        if (text.IndexOf(' ') >= 0) return null;

        if (SlotForKeyword(text) == 0) return null;

        float conf = ReadMinConfidence(json);
        if (conf < confThreshold) return null;

        return text;
    }

    private static string ReadJsonString(string json, string key)
    {
        string needle = "\"" + key + "\"";
        int i = json.IndexOf(needle, StringComparison.Ordinal);
        if (i < 0) return null;
        i = json.IndexOf(':', i + needle.Length);
        if (i < 0) return null;
        int q1 = json.IndexOf('"', i + 1);
        if (q1 < 0) return null;
        int q2 = json.IndexOf('"', q1 + 1);
        if (q2 < 0) return null;
        return json.Substring(q1 + 1, q2 - q1 - 1);
    }

    /// <summary>"result":[{"conf":..}] 안의 최저 신뢰도. conf 정보가 없으면 1(통과)을 돌려줍니다.</summary>
    private static float ReadMinConfidence(string json)
    {
        const string needle = "\"conf\"";
        float min = 1f;
        bool found = false;
        int i = 0;

        while ((i = json.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            i += needle.Length;
            int c = json.IndexOf(':', i);
            if (c < 0) break;

            int s = c + 1;
            while (s < json.Length && (json[s] == ' ' || json[s] == '\t')) s++;
            int e = s;
            while (e < json.Length &&
                   (char.IsDigit(json[e]) || json[e] == '.' || json[e] == '-' || json[e] == '+' || json[e] == 'e' || json[e] == 'E')) e++;

            if (e > s && float.TryParse(json.Substring(s, e - s), NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
            {
                min = Mathf.Min(min, v);
                found = true;
            }
            i = Mathf.Max(e, i);
        }

        return found ? min : 1f;
    }

    // ---------------- 메인 루프 ----------------

    private void Update()
    {
        // 1. 인식된 키워드를 메인 스레드로 옮깁니다
        string kw = null;
        lock (_keywordLock)
        {
            if (!string.IsNullOrEmpty(_pendingKeyword))
            {
                kw = _pendingKeyword;
                _pendingKeyword = null;
            }
        }

        if (kw != null && Time.time - _lastAcceptTime >= retriggerGuard)
        {
            _lastAcceptTime = Time.time;
            _lastKeyword = kw;
            _requestedSlot = SlotForKeyword(kw);
            _activeUntil = Time.time + activeWindow;
            Debug.Log($"<color=lime>[VoiceSkillInput]</color> Vosk 키워드 인식 성공: '{kw}' (슬롯 {_requestedSlot})");
        }

        // 2. 마이크 상태 관리
        if (_isVoskReady && !_isPaused)
        {
            if (!_isListening)
            {
                if (Time.unscaledTime >= _nextMicAttemptAt) TryStartMicrophone();
            }
            else if (_lastReadPos >= 0 && !IsDeviceRecording())
            {
                // 장치가 빠졌거나 캡처가 멈췄습니다 → 정리 후 백오프 재시도
                Debug.LogWarning($"[VoiceSkillInput] 마이크 캡처가 중단되었습니다: {_currentDeviceName}");
                StopMicrophoneRecording();
                ScheduleMicRetry();
            }
        }

        // 3. 실시간 마이크 오디오 샘플링
        if (_isListening && !_isPaused && _micClip != null)
        {
            SampleMicrophoneAudio();
        }
    }

    private bool IsDeviceRecording()
    {
        if (string.IsNullOrEmpty(_currentDeviceName)) return false;
        try { return Microphone.IsRecording(_currentDeviceName); }
        catch { return false; }
    }

    // ---------------- 마이크 시작/정지 ----------------

    private string ResolveDevice()
    {
        string[] devices;
        try { devices = Microphone.devices; }
        catch { return ""; }

        if (devices == null || devices.Length == 0) return "";

        string preferred = !string.IsNullOrEmpty(_requestedDeviceName)
            ? _requestedDeviceName
            : PlayerPrefs.GetString("SelectedMicrophone", "");

        foreach (var d in devices)
        {
            if (d == preferred) return d;
        }
        return devices[0];
    }

    private void TryStartMicrophone()
    {
        string device = ResolveDevice();
        if (string.IsNullOrEmpty(device))
        {
            // 아직 마이크가 없습니다. 핫플러그를 감안해 천천히 다시 확인합니다.
            _nextMicAttemptAt = Time.unscaledTime + 3f;
            return;
        }

        // 16kHz 를 지원하지 않는 장치가 있습니다. 지원 범위 안으로 맞추고 우리가 직접 16kHz 로 리샘플합니다.
        int rate = VOSK_RATE;
        try
        {
            Microphone.GetDeviceCaps(device, out int minFreq, out int maxFreq);
            if (minFreq != 0 || maxFreq != 0) rate = Mathf.Clamp(VOSK_RATE, minFreq, maxFreq);
        }
        catch { rate = VOSK_RATE; }
        if (rate <= 0) rate = VOSK_RATE;

        AudioClip clip = null;
        try
        {
            if (Microphone.IsRecording(device)) Microphone.End(device);
            clip = Microphone.Start(device, true, MIC_BUFFER_SECONDS, rate);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[VoiceSkillInput] 마이크 시작 실패({device}): {e.Message}");
            clip = null;
        }

        if (clip == null || clip.samples <= 0)
        {
            ScheduleMicRetry();
            return;
        }

        _currentDeviceName = device;
        _micClip = clip;
        _captureRate = rate;
        _lastReadPos = -1;
        _micStartedAt = Time.unscaledTime;
        _isListening = true;
        _micFailCount = 0;

        // 링버퍼 전체를 한 번에(회전된 형태로) 읽습니다. 프레임마다 새 배열을 만들지 않기 위함입니다.
        if (_floatScratch == null || _floatScratch.Length != clip.samples)
            _floatScratch = new float[clip.samples];

        ResetResampler();
        BeginCalibration();

        Debug.Log($"<color=cyan>[VoiceSkillInput]</color> 마이크 녹음 시작: {device} ({rate}Hz → Vosk {VOSK_RATE}Hz)");
    }

    private void ScheduleMicRetry()
    {
        _micFailCount = Mathf.Min(_micFailCount + 1, 8);
        float delay = Mathf.Min(0.5f * Mathf.Pow(2f, _micFailCount - 1), 30f);
        _nextMicAttemptAt = Time.unscaledTime + delay;

        if (_micFailCount == 3)
        {
            Debug.LogWarning($"[VoiceSkillInput] 마이크를 열 수 없습니다. {delay:0.#}초 뒤 다시 시도합니다. " +
                             "(Windows 설정 > 개인 정보 > 마이크 접근 권한을 확인하세요. 키보드 입력은 계속 동작합니다.)");
        }
    }

    private void StopMicrophoneRecording()
    {
        _isListening = false;

        if (!string.IsNullOrEmpty(_currentDeviceName))
        {
            try
            {
                if (Microphone.IsRecording(_currentDeviceName)) Microphone.End(_currentDeviceName);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VoiceSkillInput] 마이크 정지 실패: {e.Message}");
            }
        }

        _micClip = null;
        _lastReadPos = -1;
        _isSpeaking = false;
        _silenceT = 0f;
        _openT = 0f;
        _calibrating = false;

        DrainQueue(); // 정지 후 남은 오디오가 뒤늦게 스킬을 발동시키지 않도록
    }

    private void BeginCalibration()
    {
        _calibrating = true;
        _calibrationT = 0f;
        _calibrationPeak = 0f;
        _isSpeaking = false;
        _silenceT = 0f;
        _openT = 0f;
        _recentPeak = 0f;
    }

    private void ResetResampler()
    {
        _resamplePos = 0f;
        _resampleSum = 0f;
        _resampleCount = 0;
    }

    // ---------------- 샘플링 ----------------

    private void SampleMicrophoneAudio()
    {
        int curPos;
        try { curPos = Microphone.GetPosition(_currentDeviceName); }
        catch
        {
            StopMicrophoneRecording();
            ScheduleMicRetry();
            return;
        }

        if (curPos < 0) return;

        int total = _micClip.samples;

        if (_lastReadPos < 0)
        {
            // 첫 데이터가 들어오기 전의 0으로 채워진 구간은 캘리브레이션을 망치므로 건너뜁니다.
            if (curPos == 0)
            {
                if (Time.unscaledTime - _micStartedAt > 3f)
                {
                    Debug.LogWarning($"[VoiceSkillInput] 마이크에서 데이터가 들어오지 않습니다: {_currentDeviceName}");
                    StopMicrophoneRecording();
                    ScheduleMicRetry();
                }
                return;
            }
            _lastReadPos = curPos;
            return;
        }

        if (curPos == _lastReadPos) return;

        int count = curPos >= _lastReadPos ? curPos - _lastReadPos : (total - _lastReadPos) + curPos;
        if (count <= 0) return;
        if (count > total) count = total; // 한 프레임에 버퍼 한 바퀴 이상 밀린 경우

        int start = curPos - count;
        while (start < 0) start += total;

        try
        {
            // 배열 길이 == 클립 길이, 오프셋 0 → 되감기 없이 링버퍼 전체를 그대로 읽습니다.
            // (새 구간만 읽으려면 오프셋 되감기 동작에 의존해야 해서 장치/버전에 따라 위험합니다)
            _micClip.GetData(_floatScratch, 0);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[VoiceSkillInput] 마이크 버퍼 읽기 실패: {e.Message}");
            StopMicrophoneRecording();
            ScheduleMicRetry();
            return;
        }

        _lastReadPos = curPos;
        ProcessSamples(start, count, total);
    }

    private float GetMicSensitivity()
    {
        // PlayerPrefs 조회를 프레임마다 하지 않습니다.
        if (Time.unscaledTime >= _micSensRefreshAt)
        {
            _micSensRefreshAt = Time.unscaledTime + 0.5f;
            _micSens = PlayerPrefs.GetFloat("MicSensitivity", 1.0f);
        }
        return _micSens;
    }

    private void ProcessSamples(int start, int count, int total)
    {
        float sens = GetMicSensitivity();

        double sumSq = 0.0;
        int idx = start;
        for (int i = 0; i < count; i++)
        {
            float v = _floatScratch[idx];
            if (++idx >= total) idx = 0;
            sumSq += v * v;
        }
        float rms = Mathf.Sqrt((float)(sumSq / count)) * sens;
        float dt = (float)count / _captureRate;

        // --- 캘리브레이션: PC/마이크마다 다른 주변 소음 바닥을 직접 측정합니다 ---
        if (_calibrating)
        {
            _calibrationPeak = Mathf.Max(_calibrationPeak, rms);
            _calibrationT += dt;
            if (_calibrationT >= calibrationSeconds)
            {
                _calibrating = false;
                _noiseFloor = Mathf.Max(_calibrationPeak, 0.0005f);
                Debug.Log($"<color=cyan>[VoiceSkillInput]</color> 주변 소음 바닥 {_noiseFloor:F4} 측정 완료 → 발화 임계 {OpenThreshold:F4}");
            }
            return; // 캘리브레이션 중에는 Vosk 로 보내지 않습니다
        }

        bool wasSpeaking = _isSpeaking;
        bool forcedClose = false;

        if (!_isSpeaking)
        {
            // 조용한 동안 소음 바닥을 천천히 따라갑니다 (선풍기/에어컨 같은 지속 소음 흡수)
            _noiseFloor = Mathf.Lerp(_noiseFloor, Mathf.Max(rms, 0.0005f), 0.05f);

            if (rms >= OpenThreshold)
            {
                _isSpeaking = true;
                _openT = 0f;
                _silenceT = 0f;
                _recentPeak = rms;
            }
        }
        else
        {
            _openT += dt;
            _recentPeak = Mathf.Max(_recentPeak, rms);

            if (rms >= CloseThreshold)
            {
                _silenceT = 0f;
            }
            else
            {
                _silenceT += dt;
                if (_silenceT >= hangoverSeconds) _isSpeaking = false;
            }

            // 폭주 방지: 계속 열려 있다면 그건 말이 아니라 지속 소음입니다. 기준을 올려 다시는 안 걸리게 합니다.
            if (_isSpeaking && _openT >= maxOpenSeconds)
            {
                _isSpeaking = false;
                forcedClose = true;
                _noiseFloor = Mathf.Max(_noiseFloor, _recentPeak * 0.9f);
                Debug.LogWarning($"[VoiceSkillInput] {maxOpenSeconds:0.#}초 넘게 입력이 끊기지 않아 지속 소음으로 판단합니다. " +
                                 $"소음 기준을 {_noiseFloor:F4} 로 올립니다.");
            }
        }

        // 이번 청크가 발화 구간에 걸쳐 있으면 오디오를 보냅니다 (끝부분이 잘리지 않도록).
        if (wasSpeaking || _isSpeaking) EnqueueResampled(start, count, total, sens);

        // 발화가 끝났으면 최종 결과를 확정하라고 알립니다. 반드시 오디오 뒤에 와야 합니다.
        if (wasSpeaking && !_isSpeaking)
        {
            if (forcedClose) DrainQueue(); // 소음 구간은 통째로 버립니다
            else _pcmQueue.Enqueue(new PcmChunk { buffer = null, length = 0 });
        }
    }

    // ---------------- 큐 ----------------

    private void EnqueueResampled(int start, int count, int total, float sens)
    {
        int cap = Mathf.Max(VOSK_RATE / 4, Mathf.RoundToInt(maxQueueSeconds * VOSK_RATE));
        if (Volatile.Read(ref _queuedSamples) > cap)
        {
            // 디코딩이 실시간을 못 따라가는 상황입니다. 큐를 무한정 키우면 메모리가 터집니다.
            DropOldest(cap / 2);
        }

        float step = (float)VOSK_RATE / _captureRate; // 입력 1샘플당 출력 샘플 수
        int maxOut = Mathf.CeilToInt(count * step) + 2;
        short[] outBuf = RentBuffer(maxOut);
        int outLen = 0;

        int idx = start;
        for (int i = 0; i < count; i++)
        {
            float v = _floatScratch[idx] * sens;
            if (++idx >= total) idx = 0;
            _resampleSum += v;
            _resampleCount++;
            _resamplePos += step;

            while (_resamplePos >= 1f && outLen < outBuf.Length)
            {
                _resamplePos -= 1f;
                float avg = _resampleCount > 0 ? _resampleSum / _resampleCount : v;
                _resampleSum = 0f;
                _resampleCount = 0;
                outBuf[outLen++] = (short)(Mathf.Clamp(avg, -1f, 1f) * 32767f);
            }
        }

        if (outLen == 0)
        {
            _bufferPool.Add(outBuf);
            return;
        }

        Interlocked.Add(ref _queuedSamples, outLen);
        _pcmQueue.Enqueue(new PcmChunk { buffer = outBuf, length = outLen });
    }

    private short[] RentBuffer(int minSize)
    {
        while (_bufferPool.TryTake(out short[] b))
        {
            if (b.Length >= minSize) return b;
            // 너무 작은 버퍼는 되돌리지 않고 버립니다 (크기는 금방 안정됩니다)
        }
        return new short[Mathf.NextPowerOfTwo(Mathf.Max(minSize, 512))];
    }

    private void DropOldest(int targetSamples)
    {
        while (Volatile.Read(ref _queuedSamples) > targetSamples && _pcmQueue.TryDequeue(out PcmChunk c))
        {
            if (c.buffer == null) continue;
            Interlocked.Add(ref _queuedSamples, -c.length);
            _bufferPool.Add(c.buffer);
        }
    }

    private void DrainQueue()
    {
        while (_pcmQueue.TryDequeue(out PcmChunk c))
        {
            if (c.buffer == null) continue;
            Interlocked.Add(ref _queuedSamples, -c.length);
            _bufferPool.Add(c.buffer);
        }
    }

    // ---------------- 외부 제어 ----------------

    /// <summary>설정 메뉴에서 마이크를 변경했을 때 호출됩니다.</summary>
    public void ChangeMicrophone(string deviceName)
    {
        if (string.IsNullOrEmpty(deviceName)) return;

        _requestedDeviceName = deviceName;
        if (_currentDeviceName == deviceName && _isListening) return;

        Debug.Log($"<color=cyan>[VoiceSkillInput]</color> 마이크 장치 변경: {_currentDeviceName} -> {deviceName}");
        StopMicrophoneRecording();

        _micFailCount = 0;
        _nextMicAttemptAt = 0f; // 다음 Update 에서 즉시 재시도
    }

    /// <summary>설정창 마이크 테스트 중일 때 마이크 점유 충돌 방지용 일시정지</summary>
    public void PauseListening()
    {
        _isPaused = true;
        StopMicrophoneRecording();
    }

    /// <summary>마이크 테스트 종료 후 청취 재개</summary>
    public void ResumeListening()
    {
        _isPaused = false;
        _micFailCount = 0;
        _nextMicAttemptAt = 0f;
    }

    // ---------------- 종료 ----------------

    private void OnDestroy() => Shutdown();
    private void OnApplicationQuit() => Shutdown();

    private void Shutdown()
    {
        if (_shutdown) return;
        _shutdown = true;

        StopMicrophoneRecording();
        _running = false;

        if (_worker != null)
        {
            // 워커가 AcceptWaveform 안에 있는 동안 네이티브 핸들을 Dispose 하면 크래시합니다. 반드시 먼저 기다립니다.
            bool joined = _worker.Join(3000);
            _worker = null;

            if (!joined)
            {
                Debug.LogWarning("[VoiceSkillInput] Vosk 워커 종료 대기 시간 초과. 크래시를 피하기 위해 네이티브 핸들 해제를 건너뜁니다.");
                if (Instance == this) Instance = null;
                return;
            }
        }

        if (_recognizer != null) { _recognizer.Dispose(); _recognizer = null; }
        if (_model != null) { _model.Dispose(); _model = null; }

        if (Instance == this) Instance = null;
    }
}
