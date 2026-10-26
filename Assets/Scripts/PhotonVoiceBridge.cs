using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Photon Voice 2와 인게임 음성 설정(VoiceChatSettings)을 연동하는 브릿지 컨트롤러.
/// - 상시 음성(Always / Open Mic) vs 푸시투톡(Push-to-Talk) 모드 실시간 제어
/// - 푸시투톡 키(기본 F) 입력을 감지하여 음성 송출(TransmitEnabled)을 전환
/// - 마이크 음소거(Mute) 상태 즉시 반영
/// - Vosk 오프라인 STT(VoiceSkillInput)와 마이크 충돌을 방지할 수 있도록 네이티브 오디오 입력 지원
/// - Photon Voice SDK 임포트 전후 모두 컴파일 에러 없이 안전하게 동작하도록 설계
/// </summary>
public class PhotonVoiceBridge : MonoBehaviour
{
    public static PhotonVoiceBridge Instance { get; private set; }

    [Header("런타임 상태")]
    [SerializeField] private bool _isVoiceSdkAvailable;
    [SerializeField] private bool _isTransmittingNow;

    public bool IsVoiceSdkAvailable => _isVoiceSdkAvailable;
    public bool IsTransmittingNow => _isTransmittingNow;

    private Component _recorder;
    private PropertyInfo _propTransmitEnabled;
    private PropertyInfo _propVoiceDetection;
    private PropertyInfo _propIsTransmitting;
    private PropertyInfo _propRecording;

    private float _nextScanTime = 0f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("PhotonVoiceBridge");
        go.AddComponent<PhotonVoiceBridge>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        CheckSdkAvailability();
        SceneManager.sceneLoaded += OnSceneLoaded;
        VoiceChatSettings.OnSettingsChanged += ApplyVoiceSettings;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        VoiceChatSettings.OnSettingsChanged -= ApplyVoiceSettings;
        if (Instance == this) Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _recorder = null;
        _nextScanTime = 0f;
    }

    private void CheckSdkAvailability()
    {
        Type recorderType = FindRecorderType();
        _isVoiceSdkAvailable = (recorderType != null);
        if (_isVoiceSdkAvailable)
        {
            _propTransmitEnabled = recorderType.GetProperty("TransmitEnabled");
            _propVoiceDetection = recorderType.GetProperty("VoiceDetection");
            _propIsTransmitting = recorderType.GetProperty("IsCurrentlyTransmitting");
            _propRecording = recorderType.GetProperty("Recording");
            Debug.Log("<color=cyan>[PhotonVoiceBridge]</color> Photon Voice SDK가 감지되었습니다.");
        }
    }

    private static Type FindRecorderType()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("Photon.Voice.Unity.Recorder");
            if (t != null) return t;
        }
        return null;
    }

    private void Update()
    {
        if (!_isVoiceSdkAvailable)
        {
            // 아직 SDK가 로드되지 않은 경우 주기적으로 확인
            if (Time.unscaledTime > _nextScanTime)
            {
                _nextScanTime = Time.unscaledTime + 5f;
                CheckSdkAvailability();
            }
            return;
        }

        // Recorder 인스턴스 탐색
        if (_recorder == null && Time.unscaledTime > _nextScanTime)
        {
            _nextScanTime = Time.unscaledTime + 1f;
            FindActiveRecorder();
        }

        if (_recorder == null)
        {
            _isTransmittingNow = false;
            return;
        }

        // 송출 가능 여부 판정 (음소거, 상시 송출, 푸시투톡 키 입력)
        bool allowTransmit = VoiceChatSettings.IsTransmitAllowed();

        // Recorder.TransmitEnabled 실시간 갱신
        if (_propTransmitEnabled != null)
        {
            bool current = (bool)(_propTransmitEnabled.GetValue(_recorder) ?? false);
            if (current != allowTransmit)
            {
                _propTransmitEnabled.SetValue(_recorder, allowTransmit);
            }
        }

        // 실제 음성 송출 중인지 확인 (인디케이터용)
        if (_propIsTransmitting != null)
        {
            _isTransmittingNow = (bool)(_propIsTransmitting.GetValue(_recorder) ?? false);
        }
        else
        {
            _isTransmittingNow = allowTransmit;
        }
    }

    private void FindActiveRecorder()
    {
        Type recType = FindRecorderType();
        if (recType == null) return;

        var rec = FindFirstObjectByType(recType) as Component;
        if (rec != null)
        {
            _recorder = rec;
            ApplyVoiceSettings();
            Debug.Log($"<color=cyan>[PhotonVoiceBridge]</color> 활성 Recorder({rec.gameObject.name})를 연결했습니다.");
        }
    }

    public void ApplyVoiceSettings()
    {
        // 1. 송출 관련 설정 반영
        if (_recorder != null)
        {
            bool allowTransmit = VoiceChatSettings.IsTransmitAllowed();
            if (_propTransmitEnabled != null)
            {
                _propTransmitEnabled.SetValue(_recorder, allowTransmit);
            }

            // 상시 모드일 때는 보이스 감지(VAD) 활성화
            if (_propVoiceDetection != null)
            {
                bool useVad = (VoiceChatSettings.Mode == VoiceChatSettings.TransmitMode.Always);
                _propVoiceDetection.SetValue(_recorder, useVad);
            }
        }

        // 2. 수신 음량(Voice Volume) 반영
        UpdateSpeakerVolumes();
    }

    private void UpdateSpeakerVolumes()
    {
        Type spkType = FindSpeakerType();
        if (spkType == null) return;

        var speakers = FindObjectsByType(spkType, FindObjectsSortMode.None);
        float volume = VoiceChatSettings.VoiceVolume;
        foreach (var obj in speakers)
        {
            if (obj is Component comp)
            {
                var aud = comp.GetComponent<AudioSource>();
                if (aud != null)
                {
                    aud.volume = volume;
                }
            }
        }
    }

    private static Type FindSpeakerType()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("Photon.Voice.Unity.Speaker");
            if (t != null) return t;
        }
        return null;
    }
}
