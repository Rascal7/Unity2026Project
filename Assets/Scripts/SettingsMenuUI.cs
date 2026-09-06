using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using TMPro;
using Fusion;

/// <summary>
/// ESC 키를 누르면 나타나는 설정/일시정지 메뉴 UI (UGUI 기반).
/// 스프라이트 에셋(패널, 버튼, 슬라이더, 드롭다운 등)을 인스펙터에서 자유롭게 교체하여 커스텀할 수 있습니다.
/// </summary>
public class SettingsMenuUI : MonoBehaviour
{
    public static SettingsMenuUI Instance { get; private set; }

    [Header("UI 패널")]
    [Tooltip("ESC를 눌렀을 때 켜지고 꺼질 최상위 패널 (다운받은 배경 스프라이트를 적용할 Image 오브젝트)")]
    public GameObject menuPanel;

    [Header("오디오 출력 설정 (스피커/볼륨)")]
    [Tooltip("마스터 볼륨 슬라이더 (다운받은 슬라이더 스프라이트 적용 가능)")]
    public Slider masterVolumeSlider;
    public TMP_Text masterVolumeText;
    public TMP_Text outputDeviceInfoText;

    [Header("오디오 입력 설정 (마이크)")]
    [Tooltip("마이크 장치 선택 드롭다운")]
    public TMP_Dropdown micDropdown;
    [Tooltip("마이크 감도/볼륨 슬라이더")]
    public Slider micSensitivitySlider;
    public TMP_Text micSensitivityText;
    [Tooltip("마이크 입력 레벨 표시 바 (Fill 타입 Image에 게이지 스프라이트 적용)")]
    public Image micVolumeMeter;
    public Button micTestButton;
    public TMP_Text micTestButtonText;

    [Header("제어 버튼")]
    public Button resumeButton;
    public Button exitButton;

    // 내부 상태 변수
    private bool _isOpen = false;
    public bool IsOpen => _isOpen;

    private Canvas _canvas;
    private CanvasGroup _canvasGroup;

    private string _currentMicDevice = "";
    private AudioClip _micTestClip = null;
    private bool _isTestingMic = false;
    private float[] _sampleBuffer = new float[128];

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _canvas = GetComponent<Canvas>();
        _canvasGroup = GetComponent<CanvasGroup>();

        // 1. UI 클릭 처리를 위한 EventSystem 안전 점검 (없으면 자동 생성)
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(es);
        }

        // 2. menuPanel 자동 탐색 안전장치
        if (menuPanel == null)
        {
            var dim = transform.Find("DimBackground");
            if (dim != null)
            {
                menuPanel = dim.gameObject;
            }
            else
            {
                var panel = transform.Find("SettingsPanel");
                if (panel != null) menuPanel = panel.gameObject;
            }
        }

        InitializeUIEvents();

        // 3. 게임 시작 시 무조건 닫힌 상태로 초기화 (중요!)
        CloseMenu();
    }

    private void Start()
    {
        LoadSettings();
        RefreshMicrophoneList();

        // 시작 시 다시 한 번 확실하게 닫기
        CloseMenu();
    }

    private void Update()
    {
        // ESC 키 입력으로 메뉴 토글
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            ToggleMenu();
        }

        // 마이크 테스트 중일 때 음량 게이지 갱신
        if (_isTestingMic && _micTestClip != null && micVolumeMeter != null)
        {
            UpdateMicMeter();
        }
    }

    private void InitializeUIEvents()
    {
        // 1. 볼륨 슬라이더 이벤트
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.onValueChanged.RemoveAllListeners();
            masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
        }

        // 2. 마이크 슬라이더 및 드롭다운 이벤트
        if (micSensitivitySlider != null)
        {
            micSensitivitySlider.onValueChanged.RemoveAllListeners();
            micSensitivitySlider.onValueChanged.AddListener(OnMicSensitivityChanged);
        }

        if (micDropdown != null)
        {
            micDropdown.onValueChanged.RemoveAllListeners();
            micDropdown.onValueChanged.AddListener(OnMicDeviceSelected);
        }

        if (micTestButton != null)
        {
            micTestButton.onClick.RemoveAllListeners();
            micTestButton.onClick.AddListener(ToggleMicTest);
        }

        // 3. 계속하기 & 로비 나가기 버튼 (이벤트 재등록)
        if (resumeButton != null)
        {
            resumeButton.onClick.RemoveAllListeners();
            resumeButton.onClick.AddListener(CloseMenu);
        }

        if (exitButton != null)
        {
            exitButton.onClick.RemoveAllListeners();
            exitButton.onClick.AddListener(OnExitButtonClicked);
        }
    }

    /// <summary>
    /// 메뉴 열기/닫기 토글
    /// </summary>
    public void ToggleMenu()
    {
        if (_isOpen)
        {
            CloseMenu();
        }
        else
        {
            OpenMenu();
        }
    }

    public void OpenMenu()
    {
        _isOpen = true;

        if (menuPanel != null)
        {
            menuPanel.SetActive(true);
        }
        else
        {
            var dim = transform.Find("DimBackground");
            if (dim != null) dim.gameObject.SetActive(true);
            var panel = transform.Find("SettingsPanel");
            if (panel != null) panel.gameObject.SetActive(true);
        }

        if (_canvas != null)
        {
            _canvas.enabled = true;
        }

        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 1f;
            _canvasGroup.interactable = true;
            _canvasGroup.blocksRaycasts = true;
        }

        // 메뉴가 열리면 항상 마우스 커서를 표시하고 잠금 해제
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 최신 마이크 목록 갱신
        RefreshMicrophoneList();
    }

    public void CloseMenu()
    {
        _isOpen = false;

        // 1. menuPanel 비활성화
        if (menuPanel != null)
        {
            menuPanel.SetActive(false);
        }
        else
        {
            var dim = transform.Find("DimBackground");
            if (dim != null) dim.gameObject.SetActive(false);
            var panel = transform.Find("SettingsPanel");
            if (panel != null) panel.gameObject.SetActive(false);
        }

        // 2. CanvasGroup이 있다면 비활성화
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
        }

        // 3. 마이크 테스트 중이었다면 중지
        if (_isTestingMic)
        {
            StopMicTest();
        }

        // 4. 인게임 플레이 씬인 경우 커서 잠금 복원
        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene == "TutorialStage" || currentScene == "GameStage" || currentScene == "TestStage")
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    #region 설정 로드 및 적용

    private void LoadSettings()
    {
        float savedVolume = PlayerPrefs.GetFloat("MasterVolume", 1.0f);
        AudioListener.volume = savedVolume;
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.value = savedVolume;
        }
        UpdateMasterVolumeText(savedVolume);

        float savedMicSens = PlayerPrefs.GetFloat("MicSensitivity", 1.0f);
        if (micSensitivitySlider != null)
        {
            micSensitivitySlider.value = savedMicSens;
        }
        UpdateMicSensitivityText(savedMicSens);

        if (outputDeviceInfoText != null)
        {
            outputDeviceInfoText.text = "시스템 기본 출력 장치 (OS Default)";
        }
    }

    public void OnMasterVolumeChanged(float value)
    {
        AudioListener.volume = value;
        PlayerPrefs.SetFloat("MasterVolume", value);
        PlayerPrefs.Save();
        UpdateMasterVolumeText(value);
    }

    private void UpdateMasterVolumeText(float value)
    {
        if (masterVolumeText != null)
        {
            masterVolumeText.text = $"{Mathf.RoundToInt(value * 100f)}%";
        }
    }

    public void OnMicSensitivityChanged(float value)
    {
        PlayerPrefs.SetFloat("MicSensitivity", value);
        PlayerPrefs.Save();
        UpdateMicSensitivityText(value);
    }

    private void UpdateMicSensitivityText(float value)
    {
        if (micSensitivityText != null)
        {
            micSensitivityText.text = $"{Mathf.RoundToInt(value * 100f)}%";
        }
    }

    #endregion

    #region 마이크 입력 장치 제어

    public void RefreshMicrophoneList()
    {
        if (micDropdown == null) return;

        micDropdown.ClearOptions();
        string[] devices = Microphone.devices;
        List<string> options = new List<string>();

        if (devices == null || devices.Length == 0)
        {
            options.Add("연결된 마이크 없음");
            micDropdown.AddOptions(options);
            micDropdown.interactable = false;
            return;
        }

        micDropdown.interactable = true;
        string savedMic = PlayerPrefs.GetString("SelectedMicrophone", devices[0]);
        int selectedIndex = 0;

        for (int i = 0; i < devices.Length; i++)
        {
            options.Add(devices[i]);
            if (devices[i] == savedMic)
            {
                selectedIndex = i;
            }
        }

        micDropdown.AddOptions(options);
        micDropdown.value = selectedIndex;
        micDropdown.RefreshShownValue();

        _currentMicDevice = devices[selectedIndex];
    }

    public void OnMicDeviceSelected(int index)
    {
        string[] devices = Microphone.devices;
        if (devices != null && index >= 0 && index < devices.Length)
        {
            _currentMicDevice = devices[index];
            PlayerPrefs.SetString("SelectedMicrophone", _currentMicDevice);
            PlayerPrefs.Save();
            Debug.Log($"<color=cyan>[Settings]</color> 마이크 장치 변경: {_currentMicDevice}");

            if (_isTestingMic)
            {
                StopMicTest();
                StartMicTest();
            }
        }
    }

    public void ToggleMicTest()
    {
        if (_isTestingMic)
        {
            StopMicTest();
        }
        else
        {
            StartMicTest();
        }
    }

    private void StartMicTest()
    {
        if (string.IsNullOrEmpty(_currentMicDevice) || Microphone.devices.Length == 0)
        {
            Debug.LogWarning("[Settings] 테스트할 수 있는 마이크가 없습니다.");
            return;
        }

        _isTestingMic = true;
        if (micTestButtonText != null) micTestButtonText.text = "테스트 중지";

        _micTestClip = Microphone.Start(_currentMicDevice, true, 1, 44100);
    }

    private void StopMicTest()
    {
        _isTestingMic = false;
        if (micTestButtonText != null) micTestButtonText.text = "마이크 테스트";

        if (!string.IsNullOrEmpty(_currentMicDevice) && Microphone.IsRecording(_currentMicDevice))
        {
            Microphone.End(_currentMicDevice);
        }

        _micTestClip = null;
        if (micVolumeMeter != null) micVolumeMeter.fillAmount = 0f;
    }

    private void UpdateMicMeter()
    {
        int micPos = Microphone.GetPosition(_currentMicDevice);
        if (micPos < 0 || _micTestClip == null) return;

        int startPos = micPos - _sampleBuffer.Length;
        if (startPos < 0) return;

        _micTestClip.GetData(_sampleBuffer, startPos);

        float sum = 0f;
        for (int i = 0; i < _sampleBuffer.Length; i++)
        {
            sum += _sampleBuffer[i] * _sampleBuffer[i];
        }

        float rms = Mathf.Sqrt(sum / _sampleBuffer.Length);
        float sens = PlayerPrefs.GetFloat("MicSensitivity", 1.0f);
        float level = Mathf.Clamp01(rms * 10f * sens);

        micVolumeMeter.fillAmount = Mathf.Lerp(micVolumeMeter.fillAmount, level, Time.deltaTime * 15f);
    }

    #endregion

    #region 게임/로비 나가기

    private async void OnExitButtonClicked()
    {
        CloseMenu();

        var runner = FindFirstObjectByType<NetworkRunner>();
        if (runner != null)
        {
            Debug.Log("<color=orange>[Settings]</color> 네트워크 세션 종료 중...");
            await runner.Shutdown();
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        SceneManager.LoadScene("Lobby");
    }

    #endregion
}
