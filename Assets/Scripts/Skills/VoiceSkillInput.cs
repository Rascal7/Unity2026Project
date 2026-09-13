using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Vosk;

/// <summary>
/// Vosk 오프라인 음성 인식 엔진을 사용하여 마이크 입력을 처리하는 로컬 전용 컴포넌트.
/// - 설정 메뉴(SettingsMenuUI)에서 선택한 특정 마이크 장치로부터 PCM 오디오를 스트리밍합니다.
/// - 백그라운드 스레드에서 Vosk 문법(Grammar) 기반으로 "점프", "대시" 등의 키워드를 실시간 감지합니다.
/// - 키워드가 인식되면 activeWindow(0.2초) 동안 IsSkillRequested 가 true 가 되어 Spawner.OnInput 으로 전달됩니다.
/// - 씬 배선이 필요 없도록 런타임에 스스로 부트스트랩합니다.
/// </summary>
public class VoiceSkillInput : MonoBehaviour
{
    public static VoiceSkillInput Instance { get; private set; }

    [Header("스킬 키워드 설정")]
    [Tooltip("인식할 스킬 키워드 목록.")]
    public List<string> keywords = new List<string> { "dash", "jump", "대시", "점프" };

    [Tooltip("키워드 인식 후 스킬 입력이 유지되는 시간(초).")]
    public float activeWindow = 0.2f;

    // 이 시각까지 스킬 입력이 활성 상태로 간주됨
    private float _activeUntil = -1f;
    private string _lastKeyword = "";
    private int _requestedSlot = 0;

    /// <summary>현재 음성으로 스킬이 요청된 상태인가.</summary>
    public static bool IsSkillRequested =>
        Instance != null && Time.time <= Instance._activeUntil;

    /// <summary>현재 음성으로 요청된 스킬 슬롯(1/2/3), 없으면 0.</summary>
    public static int RequestedSlot =>
        (Instance != null && Time.time <= Instance._activeUntil) ? Instance._requestedSlot : 0;

    /// <summary>마지막으로 인식된 키워드(디버그/캐릭터별 분기용).</summary>
    public static string LastKeyword => Instance != null ? Instance._lastKeyword : "";

    // 마이크 및 Vosk 관련 변수
    private const int SAMPLE_RATE = 16000; // Vosk 표준 16kHz
    private string _currentDeviceName = "";
    private AudioClip _micClip = null;
    private int _lastReadPos = 0;
    private bool _isListening = false;
    private bool _isPaused = false;

    [Header("음성 감지 임계값 (VAD)")]
    [Tooltip("말소리로 인정할 최소 음량 (배경 소음/잡음 필터링).")]
    public float minVoiceThreshold = 0.025f;

    // 음성 발화 상태 추적
    private bool _isSpeaking = false;
    private float _silenceDuration = 0f;

    // Vosk 엔진
    private Model _model;
    private VoskRecognizer _recognizer;
    private bool _isVoskReady = false;

    // 멀티스레딩
    private readonly ConcurrentQueue<short[]> _pcmQueue = new ConcurrentQueue<short[]>();
    private CancellationTokenSource _cts;
    private readonly object _keywordLock = new object();
    private string _pendingKeyword = null;

    /// <summary>현재 녹음 중인 마이크 장치명</summary>
    public string CurrentMicrophone => _currentDeviceName;

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
        InitializeVoskAndMicrophone();
    }

    private void InitializeVoskAndMicrophone()
    {
        string modelPath = Path.Combine(Application.streamingAssetsPath, "vosk-model-small-ko-0.22");
        if (!Directory.Exists(modelPath))
        {
            Debug.LogWarning($"[VoiceSkillInput] Vosk 모델 폴더를 찾을 수 없습니다: {modelPath}. 키보드(LeftShift)로 대체 입력 가능합니다.");
            return;
        }

        // 마이크 장치 결정 (PlayerPrefs 저장값 우선)
        string[] devices = Microphone.devices;
        if (devices != null && devices.Length > 0)
        {
            _currentDeviceName = PlayerPrefs.GetString("SelectedMicrophone", devices[0]);
            bool valid = false;
            foreach (var d in devices)
            {
                if (d == _currentDeviceName) { valid = true; break; }
            }
            if (!valid) _currentDeviceName = devices[0];
        }

        // 백그라운드 스레드에서 Vosk 모델 로드
        _cts = new CancellationTokenSource();
        Task.Run(() => InitVoskWorker(modelPath, _cts.Token));
    }

    private void InitVoskWorker(string modelPath, CancellationToken token)
    {
        try
        {
            Vosk.Vosk.SetLogLevel(0);
            _model = new Model(modelPath);

            // 단어 제한 문법 (타겟 단어와 [unk]만 등록하여 잡음 오인식 방지)
            string grammar = "[\"점프\", \"대시\", \"킁킁\", \"당근\", \"[unk]\"]";
            _recognizer = new VoskRecognizer(_model, SAMPLE_RATE, grammar);
            _recognizer.SetWords(false);

            _isVoskReady = true;
            Debug.Log("<color=cyan>[VoiceSkillInput]</color> Vosk 한국어 음성 모델 초기화 완료!");

            // 워커 루프 시작
            VoskProcessingLoop(token);
        }
        catch (Exception e)
        {
            Debug.LogError($"[VoiceSkillInput] Vosk 초기화 실패: {e.Message}");
        }
    }

    private void VoskProcessingLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (_pcmQueue.TryDequeue(out short[] chunk))
            {
                if (_recognizer == null) continue;

                bool isFinal = _recognizer.AcceptWaveform(chunk, chunk.Length);
                string json = isFinal ? _recognizer.Result() : _recognizer.PartialResult();

                string detected = ParseKeywordFromJson(json);
                if (!string.IsNullOrEmpty(detected))
                {
                    _recognizer.Reset();

                    // 큐에 남은 잔여 오디오 비우기 (연속 오발동 방지)
                    while (_pcmQueue.TryDequeue(out _)) { }

                    lock (_keywordLock)
                    {
                        _pendingKeyword = detected;
                    }
                }
            }
            else
            {
                Thread.Sleep(10);
            }
        }
    }

    private string ParseKeywordFromJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;

        // [unk] 로 판정된 알 수 없는 소리는 즉시 제외
        if (json.Contains("[unk]")) return null;

        // 정확한 키워드 매칭 (JSON 내 정확한 단어 표현 확인)
        if (json.Contains("\"점프\"") || json.Contains(": \"점프"))
        {
            return "점프";
        }
        if (json.Contains("\"대시\"") || json.Contains(": \"대시"))
        {
            return "대시";
        }
        if (json.Contains("\"킁킁\"") || json.Contains(": \"킁킁"))
        {
            return "킁킁";
        }
        if (json.Contains("\"당근\"") || json.Contains(": \"당근"))
        {
            return "당근";
        }

        return null;
    }

    /// <summary>키워드 → 스킬 슬롯 매핑. (여우 대시/토끼 당근=1, 여우 킁킁/토끼 점프=2)</summary>
    private static int KeywordToSlot(string kw)
    {
        switch (kw)
        {
            case "대시": return 1; // 여우 ch1
            case "당근": return 1; // 토끼 ch1
            case "킁킁": return 2; // 여우 ch2
            case "점프": return 2; // 토끼 ch2
            default: return 0;
        }
    }

    private void Update()
    {
        // 1. 인식된 키워드 메인 스레드 적용
        lock (_keywordLock)
        {
            if (!string.IsNullOrEmpty(_pendingKeyword))
            {
                _lastKeyword = _pendingKeyword;
                _pendingKeyword = null;
                _activeUntil = Time.time + activeWindow;
                _requestedSlot = KeywordToSlot(_lastKeyword);
                Debug.Log($"<color=lime>[VoiceSkillInput]</color> Vosk 키워드 인식 성공: '{_lastKeyword}' (슬롯 {_requestedSlot})");
            }
        }

        // 2. Vosk 준비 완료 후 마이크 녹음 시작
        if (_isVoskReady && !_isListening && !_isPaused && !string.IsNullOrEmpty(_currentDeviceName))
        {
            StartMicrophoneRecording();
        }

        // 3. 실시간 마이크 오디오 샘플링
        if (_isListening && !_isPaused && _micClip != null)
        {
            SampleMicrophoneAudio();
        }
    }

    private void StartMicrophoneRecording()
    {
        if (string.IsNullOrEmpty(_currentDeviceName)) return;

        try
        {
            if (Microphone.IsRecording(_currentDeviceName))
            {
                Microphone.End(_currentDeviceName);
            }

            _micClip = Microphone.Start(_currentDeviceName, true, 2, SAMPLE_RATE);
            _lastReadPos = Microphone.GetPosition(_currentDeviceName);
            _isListening = true;
            Debug.Log($"<color=cyan>[VoiceSkillInput]</color> 마이크 녹음 시작: {_currentDeviceName} (16kHz)");
        }
        catch (Exception e)
        {
            Debug.LogError($"[VoiceSkillInput] 마이크 시작 오류: {e.Message}");
            _isListening = false;
        }
    }

    private void StopMicrophoneRecording()
    {
        _isListening = false;
        if (!string.IsNullOrEmpty(_currentDeviceName) && Microphone.IsRecording(_currentDeviceName))
        {
            Microphone.End(_currentDeviceName);
        }
        _micClip = null;
        _lastReadPos = 0;
    }

    private void SampleMicrophoneAudio()
    {
        int curPos = Microphone.GetPosition(_currentDeviceName);
        if (curPos < 0 || curPos == _lastReadPos || _micClip == null) return;

        int samplesToRead;
        if (curPos > _lastReadPos)
        {
            samplesToRead = curPos - _lastReadPos;
        }
        else
        {
            samplesToRead = (_micClip.samples - _lastReadPos) + curPos;
        }

        if (samplesToRead <= 0) return;

        float[] floatSamples = new float[samplesToRead];
        if (curPos > _lastReadPos)
        {
            _micClip.GetData(floatSamples, _lastReadPos);
        }
        else
        {
            int firstPart = _micClip.samples - _lastReadPos;
            float[] part1 = new float[firstPart];
            _micClip.GetData(part1, _lastReadPos);
            Array.Copy(part1, 0, floatSamples, 0, firstPart);

            if (curPos > 0)
            {
                float[] part2 = new float[curPos];
                _micClip.GetData(part2, 0);
                Array.Copy(part2, 0, floatSamples, firstPart, curPos);
            }
        }
        _lastReadPos = curPos;

        // 1. 실시간 RMS 음량 측정 (소음 vs 실제 말소리 구분)
        float sum = 0f;
        for (int i = 0; i < samplesToRead; i++)
        {
            sum += floatSamples[i] * floatSamples[i];
        }
        float rms = Mathf.Sqrt(sum / samplesToRead);

        // 2. 마이크 감도 설정 반영
        float sens = PlayerPrefs.GetFloat("MicSensitivity", 1.0f);
        float effectiveVolume = rms * sens;

        // 3. 음성 감지 게이트 (VAD): 실제 말소리 음량 이상일 때만 발화 상태로 인정
        if (effectiveVolume >= minVoiceThreshold)
        {
            _isSpeaking = true;
            _silenceDuration = 0f;
        }
        else if (_isSpeaking)
        {
            _silenceDuration += (float)samplesToRead / SAMPLE_RATE;
            // 말소리가 끝난 후 0.3초간 유지하여 단어 끝부분이 잘리지 않도록 보호
            if (_silenceDuration > 0.3f)
            {
                _isSpeaking = false;
                _silenceDuration = 0f;
            }
        }

        // 아무 말도 하지 않는 정적/배경 소음 상태일 때는 오디오를 Vosk로 보내지 않음 (오인식 원천 차단)
        if (!_isSpeaking)
        {
            return;
        }

        // 4. 16-bit PCM 변환 및 백그라운드 Vosk 큐로 전송
        short[] pcmSamples = new short[samplesToRead];
        for (int i = 0; i < samplesToRead; i++)
        {
            float val = floatSamples[i] * sens;
            val = Mathf.Clamp(val, -1.0f, 1.0f);
            pcmSamples[i] = (short)(val * 32767f);
        }

        _pcmQueue.Enqueue(pcmSamples);
    }

    /// <summary>
    /// 설정 메뉴에서 마이크를 변경했을 때 호출됩니다.
    /// </summary>
    public void ChangeMicrophone(string deviceName)
    {
        if (string.IsNullOrEmpty(deviceName)) return;
        if (_currentDeviceName == deviceName && _isListening) return;

        Debug.Log($"<color=cyan>[VoiceSkillInput]</color> 마이크 장치 변경: {_currentDeviceName} -> {deviceName}");
        StopMicrophoneRecording();
        _currentDeviceName = deviceName;

        if (_isVoskReady && !_isPaused)
        {
            StartMicrophoneRecording();
        }
    }

    /// <summary>
    /// 설정창 마이크 테스트 중일 때 마이크 점유 충돌 방지용 일시정지
    /// </summary>
    public void PauseListening()
    {
        _isPaused = true;
        StopMicrophoneRecording();
    }

    /// <summary>
    /// 마이크 테스트 종료 후 청취 재개
    /// </summary>
    public void ResumeListening()
    {
        _isPaused = false;
        if (_isVoskReady)
        {
            StartMicrophoneRecording();
        }
    }

    private void OnDestroy()
    {
        StopMicrophoneRecording();

        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        if (_recognizer != null)
        {
            _recognizer.Dispose();
            _recognizer = null;
        }

        if (_model != null)
        {
            _model.Dispose();
            _model = null;
        }

        if (Instance == this) Instance = null;
    }
}
