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
    [Tooltip("오디오 출력 장치 선택 드롭다운")]
    public TMP_Dropdown outputDeviceDropdown;
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

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        GameplayInputSuppressed = false; // 메뉴가 꺼지면 입력 차단도 해제(스턱 방지)
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureEventSystem();
        InitializeUIEvents();
    }

    public static void EnsureEventSystem()
    {
        var es = EventSystem.current;
        if (es == null)
        {
            es = FindFirstObjectByType<EventSystem>();
        }

        if (es == null)
        {
            var esGo = new GameObject("EventSystem", typeof(EventSystem));
            es = esGo.GetComponent<EventSystem>();
            esGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            DontDestroyOnLoad(esGo);
            Debug.Log("<color=lime>[SettingsMenuUI]</color> EventSystem 자동 생성 및 DontDestroyOnLoad 설정 완료");
        }
        else
        {
            var inputSystemModule = es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            var standaloneModule = es.GetComponent<StandaloneInputModule>();

            if (inputSystemModule == null && standaloneModule == null)
            {
                es.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }
            else if (inputSystemModule != null)
            {
                if (!inputSystemModule.enabled) inputSystemModule.enabled = true;
                if (standaloneModule != null && standaloneModule.enabled) standaloneModule.enabled = false;
            }
            else if (standaloneModule != null && !standaloneModule.enabled)
            {
                standaloneModule.enabled = true;
            }
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var existing = FindFirstObjectByType<SettingsMenuUI>();
        if (existing != null)
        {
            Instance = existing;
            return;
        }

        var prefab = Resources.Load<GameObject>("SettingsMenuCanvas");
        if (prefab != null)
        {
            var go = Instantiate(prefab);
            go.name = "SettingsMenuCanvas";
            DontDestroyOnLoad(go);
            Debug.Log("<color=lime>[SettingsMenuUI]</color> SettingsMenuCanvas 자동 로드 및 DontDestroyOnLoad 완료");
        }
        else
        {
            Debug.LogWarning("[SettingsMenuUI] Resources/SettingsMenuCanvas 프리팹을 찾을 수 없습니다.");
        }
    }

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
        if (_canvas != null)
        {
            _canvas.sortingOrder = 25000;
        }
        _canvasGroup = GetComponent<CanvasGroup>();

        var gr = GetComponent<GraphicRaycaster>();
        if (gr == null) gr = gameObject.AddComponent<GraphicRaycaster>();
        gr.enabled = true;

        EnsureEventSystem();

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
        RefreshOutputDeviceList();
        RefreshMicrophoneList();

        EnsureGearButton();

        // 시작 시 다시 한 번 확실하게 닫기
        CloseMenu();
    }

    private void Update()
    {
        // 톱니바퀴: 로비(비인게임)에서 메뉴가 닫혀 있고 '로그인/게스트 상태'일 때만 표시
        if (_gearButton != null)
        {
            bool authed = AuthManager.Instance != null &&
                          (AuthManager.Instance.IsLoggedIn || AuthManager.Instance.IsGuest);
            _gearButton.SetActive(!_isOpen && !IsGameStage() && authed);
        }

        // ESC 키 입력으로 메뉴 토글 (레거시 Input 및 New Input System 모두 완벽 지원)
        bool escPressed = false;
#if ENABLE_INPUT_SYSTEM
        if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            escPressed = true;
        }
#endif
        if (!escPressed && Input.GetKeyDown(KeyCode.Escape))
        {
            escPressed = true;
        }

        // 키 재설정 대기 중이면: ESC=취소, 그 외 키=해당 바인딩에 배정 (메뉴 토글보다 우선)
        if (_awaitingBind >= 0)
        {
            if (escPressed)
            {
                _awaitingBind = -1;
                RefreshBinds();
            }
            else
            {
                CaptureBindKey();
            }
            return;
        }

        if (escPressed)
        {
            if (BasicLauncher.Instance != null && BasicLauncher.Instance.IsDisconnectModalOpen)
            {
                return;
            }
            if (RankingBoardUI.Instance != null &&
                (RankingBoardUI.Instance.IsOpen || RankingBoardUI.LastEscapeClosedFrame == Time.frameCount))
            {
                if (RankingBoardUI.Instance.IsOpen)
                {
                    RankingBoardUI.Instance.Hide();
                }
                return;
            }
            ToggleMenu();
        }

        // 메뉴가 열려 있을 때 마우스 및 EventSystem 상태 강제 유지
        if (_isOpen)
        {
            if (Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
            }
            if (!Cursor.visible)
            {
                Cursor.visible = true;
            }

            if (EventSystem.current == null)
            {
                EnsureEventSystem();
            }
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

        if (outputDeviceDropdown != null)
        {
            outputDeviceDropdown.onValueChanged.RemoveAllListeners();
            outputDeviceDropdown.onValueChanged.AddListener(OnOutputDeviceSelected);
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

    // 옵션창이나 연결 끊김 모달이 열려 있는 동안 켜짐. Spawner 가 이 값을 보고 게임플레이 입력(이동/점프/스킬)을 막는다.
    // (키 재설정 중 실제 스킬/이동이 발동되는 것을 방지)
    public static bool GameplayInputSuppressed { get; set; }

    public void OpenMenu()
    {
        if (!_isOpen) UiSfx.PopupOpen();
        _isOpen = true;
        GameplayInputSuppressed = true;

        EnsureEventSystem();
        InitializeUIEvents();

        if (_canvas != null)
        {
            _canvas.enabled = true;
            _canvas.sortingOrder = 25000;
        }

        var gr = GetComponent<GraphicRaycaster>();
        if (gr == null) gr = gameObject.AddComponent<GraphicRaycaster>();
        gr.enabled = true;

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

        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 1f;
            _canvasGroup.interactable = true;
            _canvasGroup.blocksRaycasts = true;
        }

        // 메뉴가 열리면 항상 마우스 커서를 표시하고 잠금 해제
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 최신 출력 장치 및 마이크 목록 갱신
        RefreshOutputDeviceList();
        RefreshMicrophoneList();

        // 옵션 UI(탭) 생성 및 상태 반영
        BuildOptionsUI();
        if (_dimOverlay != null) _dimOverlay.SetActive(true);
        _awaitingBind = -1;

        bool inGame = IsGameStage();
        if (_respawnRow != null) _respawnRow.SetActive(inGame); // 다시하기: 인게임에서만
        ConfigureExitButton(inGame);                            // 나가기(인게임) / 닫기(로비)
        SelectTab(_currentTab);
    }

    // 하단 버튼: 인게임이면 '로비 이동', 로비면 '게임 종료' (빨강)
    private void ConfigureExitButton(bool inGame)
    {
        if (_exitBtn2 == null) return;
        _exitBtn2.onClick.RemoveAllListeners();
        if (inGame)
        {
            StyleFlatButton(_exitBtn2, OptExit, "로비 이동", 28);
            _exitBtn2.onClick.AddListener(OnExitButtonClicked);
        }
        else
        {
            StyleFlatButton(_exitBtn2, OptQuit, "게임 종료", 28);
            _exitBtn2.onClick.AddListener(OnQuitButtonClicked);
        }
        // 인게임: 다시하기(좌) 옆 우측 배치 / 로비: 다시하기 숨김 → 단독 중앙 정렬
        var ert = _exitBtn2.GetComponent<RectTransform>();
        if (ert != null) ert.anchoredPosition = new Vector2(inGame ? BTN_HALF : 0f, BOTTOM_Y);
    }

    public void CloseMenu()
    {
        if (_isOpen) UiSfx.PopupClose(); // 시작 시 초기화용 호출(이미 닫힘)에서는 무음
        _isOpen = false;
        GameplayInputSuppressed = false;
        if (_dimOverlay != null) _dimOverlay.SetActive(false);

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
        bool isInGameStage = currentScene == "TutorialStage" ||
                             currentScene == "Main Stage1" ||
                             currentScene == "Main Stage2" ||
                             currentScene == "Main Stage3" ||
                             currentScene == "TestStage" ||
                             currentScene == "GameStage";
        if (isInGameStage)
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

    #region 오디오 출력 장치 제어

#if (UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN)
    [System.Runtime.InteropServices.DllImport("winmm.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern int waveOutGetNumDevs();

    [System.Runtime.InteropServices.DllImport("winmm.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern int waveOutGetDevCaps(System.IntPtr uDeviceID, out WAVEOUTCAPS pwoc, int cbwoc);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private struct WAVEOUTCAPS
    {
        public ushort wMid;
        public ushort wPid;
        public uint vDriverVersion;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;
        public uint dwFormats;
        public ushort wChannels;
        public ushort wReserved1;
        public uint dwSupport;
    }
#endif

    public void RefreshOutputDeviceList()
    {
        if (outputDeviceDropdown == null) return;

        outputDeviceDropdown.ClearOptions();
        List<string> options = new List<string>();
        options.Add("시스템 기본 출력 장치 (Default)");

#if (UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN)
        try
        {
            int count = waveOutGetNumDevs();
            for (int i = 0; i < count; i++)
            {
                WAVEOUTCAPS caps = new WAVEOUTCAPS();
                if (waveOutGetDevCaps((System.IntPtr)i, out caps, System.Runtime.InteropServices.Marshal.SizeOf(typeof(WAVEOUTCAPS))) == 0)
                {
                    if (!string.IsNullOrEmpty(caps.szPname) && !options.Contains(caps.szPname))
                    {
                        options.Add(caps.szPname);
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SettingsMenuUI] 출력 장치 목록 조회 실패: {e.Message}");
        }
#endif

        string savedDevice = PlayerPrefs.GetString("SelectedOutputDevice", options[0]);
        int selectedIndex = 0;
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i] == savedDevice)
            {
                selectedIndex = i;
                break;
            }
        }

        outputDeviceDropdown.AddOptions(options);
        // 목록 갱신은 '선택'이 아니므로 이벤트 없이 값만 맞춘다.
        // (.value 로 넣으면 OnOutputDeviceSelected → AudioSettings.Reset 이 불려 메뉴를 열 때마다 배경음·효과음이 모두 꺼졌다)
        outputDeviceDropdown.SetValueWithoutNotify(selectedIndex);
        outputDeviceDropdown.RefreshShownValue();
    }

    public void OnOutputDeviceSelected(int index)
    {
        if (outputDeviceDropdown == null || index < 0 || index >= outputDeviceDropdown.options.Count) return;

        string selectedDevice = outputDeviceDropdown.options[index].text;
        PlayerPrefs.SetString("SelectedOutputDevice", selectedDevice);
        PlayerPrefs.Save();
        Debug.Log($"<color=cyan>[Settings]</color> 출력 장치 선택: {selectedDevice}");

        // 유니티 오디오 설정 재적용
        AudioSettings.Reset(AudioSettings.GetConfiguration());
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

            // Vosk 음성 스킬 입력 장치 즉시 갱신
            if (VoiceSkillInput.Instance != null)
            {
                VoiceSkillInput.Instance.ChangeMicrophone(_currentMicDevice);
            }

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

        // 인게임 음성 인식이 같은 마이크를 사용 중이면 충돌 방지를 위해 일시 정지
        if (VoiceSkillInput.Instance != null)
        {
            VoiceSkillInput.Instance.PauseListening();
        }

        _isTestingMic = true;
        // 버튼 문구는 항상 "테스트"로 고정하고, 실행 상태는 색상으로만 표시
        if (micTestButtonText != null) micTestButtonText.text = "테스트";
        if (micTestButton != null && micTestButton.image != null) micTestButton.image.color = OptBtnSel;

        _micTestClip = Microphone.Start(_currentMicDevice, true, 1, 44100);
    }

    private void StopMicTest()
    {
        _isTestingMic = false;
        if (micTestButtonText != null) micTestButtonText.text = "테스트";
        if (micTestButton != null && micTestButton.image != null) micTestButton.image.color = OptBtnFace;

        if (!string.IsNullOrEmpty(_currentMicDevice) && Microphone.IsRecording(_currentMicDevice))
        {
            Microphone.End(_currentMicDevice);
        }

        _micTestClip = null;
        if (micVolumeMeter != null) micVolumeMeter.fillAmount = 0f;

        // 마이크 테스트 종료 후 음성 스킬 청취 재개
        if (VoiceSkillInput.Instance != null)
        {
            VoiceSkillInput.Instance.ResumeListening();
        }
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
        BasicLauncher.MarkVoluntaryExit();

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

    private void OnQuitButtonClicked()
    {
        UiSfx.Click();
        PlayerPrefs.Save();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    #endregion

    #region 게임플레이 설정 (스킬 단축키 재설정 + 다시하기)

    private bool _gameplayUIBuilt;
    private int _awaitingBind = -1;       // _binds 인덱스, -1 = 대기 아님
    private TMPro.TMP_Text _rebindHintText;
    private GameObject _respawnRow;       // 하단 다시하기 버튼 (인게임에서만 표시)

    private TMPro.TMP_FontAsset _uiFont;              // 두 탭 공통 TMP 폰트(글씨체 통일)
    private UnityEngine.UI.ScrollRect _contentScroll; // 콘텐츠 스크롤
    private Transform _viewport;                      // 스크롤 뷰포트(마스크)
    private GameObject _resetConfirm;                 // 기본값 복원 확인창
    private const float CONTROLS_H = 660f;            // 조작 탭 콘텐츠 높이(뷰포트보다 커서 스크롤됨)

    private readonly System.Collections.Generic.List<BindEntry> _binds = new System.Collections.Generic.List<BindEntry>();

    // 하나의 재설정 가능한 키 바인딩(이동/점프/스킬 공용)
    private class BindEntry
    {
        public string name;                 // 중복 경고 표시용 이름
        public System.Func<KeyCode> get;
        public System.Action<KeyCode> set;
        public TMPro.TMP_Text label;        // 키캡 텍스트
    }

    // 탭 방식 옵션 UI
    private GameObject _controlsPanel;    // '조작' 탭 내용
    private GameObject _audioPanel;       // '음향' 탭 내용 (= 기존 SettingsPanel 재사용)
    private Button _tabBtnControls, _tabBtnAudio;
    private Button _exitBtn2;             // 하단 나가기/게임종료 버튼 (상황에 따라 라벨·동작 변경)
    private Button _closeBtn;             // 우측 상단 X 닫기 버튼
    private GameObject _gearButton;       // 로비용 톱니바퀴 버튼
    private GameObject _dimOverlay;       // 모달 딤 오버레이
    private int _currentTab = 0;          // 0=조작, 1=음향

    // === 레트로 픽셀아트 팔레트 (GPT 스펙) ===
    private static Color Hex(int r, int g, int b) => new Color32((byte)r, (byte)g, (byte)b, 0xFF);
    private static readonly Color OptPanelBg   = Hex(0x30, 0x31, 0x3C);   // 배경
    private static readonly Color OptOutline   = Hex(0x2F, 0x1C, 0x16);   // 어두운 외곽선(검정 대신)
    private static readonly Color OptBorder    = Hex(0x6A, 0x47, 0x2A);   // 갈색 테두리
    private static readonly Color OptBtnFace   = Hex(0x48, 0x4C, 0x5C);   // 기본 버튼
    private static readonly Color OptBtnSel    = Hex(0x4F, 0x91, 0xAD);   // 선택(틸)
    private static readonly Color OptGold      = Hex(0xFA, 0xD1, 0x6A);   // 메인 골드(강조)
    private static readonly Color OptGoldHi    = Hex(0xFF, 0xE6, 0xA0);   // 하이라이트 골드
    private static readonly Color OptText      = Hex(0xE8, 0xE4, 0xD8);   // 메인 텍스트
    private static readonly Color OptHeader    = Hex(0xE8, 0xE4, 0xD8);
    private static readonly Color OptHint      = Hex(0xA9, 0xAA, 0xB5);   // 보조 텍스트
    private static readonly Color OptDivider   = Hex(0x55, 0x55, 0x63);   // 구분선
    private static readonly Color OptKeycapBg  = Hex(0x48, 0x4C, 0x5C);   // 키캡 배경
    private static readonly Color OptKeycapBd  = Hex(0x6A, 0x6B, 0x78);   // 키캡 밝은 테두리
    private static readonly Color OptRespawn   = Hex(0x48, 0x4C, 0x5C);   // 통일(기본 버튼 톤)
    private static readonly Color OptExit      = Hex(0x48, 0x4C, 0x5C);   // 통일(기본 버튼 톤)
    private static readonly Color OptQuit      = LobbyUITheme.RedBtn;     // 로비 게임종료 버튼 (레드)

    // === 공통 레이아웃 그리드 (모든 요소를 하나의 그리드에 정렬) ===
    // 창을 약 12% 축소하고, 탭↔콘텐츠 간격을 줄여 하나의 그룹처럼 보이게 함.
    // 두 탭이 완전히 동일한 콘텐츠 영역(위치·폭·시작 X)을 공유하는 상단 정렬 2열 레이아웃.
    private const float FRAME_W = 850f;   // 옵션창(바깥 외곽선) 너비
    private const float FRAME_H = 610f;   // 옵션창(바깥 외곽선) 높이 (세로 확장)
    private const float COL_X   = 84f;    // 오른쪽 콘텐츠 영역 중심 X (프레임 기준) — 두 탭 공통
    private const float COL_Y   = 8f;     // 콘텐츠(뷰포트) 중심 Y — 두 탭 공통
    private const float COL_W   = 560f;   // 콘텐츠(뷰포트) 너비 — 두 탭 공통
    private const float COL_H   = 400f;   // 콘텐츠(뷰포트) 높이 — 두 탭 공통
    private const float IN_L    = -256f;  // 콘텐츠 내부 좌측 X(= 모든 섹션 제목·라벨 시작) — 두 탭 공통
    private const float IN_R    = 256f;    // 콘텐츠 내부 우측 X
    private const float IN_W    = 512f;    // 콘텐츠 내부 너비
    private const float CTRL_X  = -132f;   // 컨트롤 열 시작 X(키캡/드롭다운/슬라이더) — 두 탭 공통
    private const float COLDIV_X = -230f;  // 탭↔콘텐츠 세로 구분선 X
    private const float TAB_X   = -305f;   // 탭 열 중심 X (사이드 메뉴)
    private const float TAB_W   = 132f;    // 탭 너비
    private const float TAB_H   = 56f;     // 탭 높이
    private const float TAB_GAP = 12f;     // 탭 간 간격
    private const float TAB_TOP_Y = 180f;  // 위 탭 중심 Y (콘텐츠 상단과 정렬)
    private const float TAB_BOT_Y = 112f;  // 아래 탭 중심 Y
    private const float BDIV_Y  = -200f;   // 하단 버튼 구분선 Y
    private const float BOTTOM_Y = -244f;  // 하단 버튼 중심 Y
    private const float BTN_W   = 236f;    // 하단 버튼 너비
    private const float BTN_H   = 56f;     // 하단 버튼 높이
    private const float BTN_HALF = 128f;   // 하단 버튼 좌우 배치 오프셋(=너비/2+18)

    // 그래픽에 어두운 픽셀 외곽선(#2F1C16)을 입힌다. 검정 대신 사용.
    private UnityEngine.UI.Outline AddPixelOutline(Graphic g, float dist)
    {
        if (g == null) return null;
        var o = g.GetComponent<UnityEngine.UI.Outline>();
        if (o == null) o = g.gameObject.AddComponent<UnityEngine.UI.Outline>();
        o.effectColor = OptOutline;
        o.effectDistance = new Vector2(dist, -dist);
        o.useGraphicAlpha = false;
        return o;
    }

    // 평면 픽셀 버튼 스타일 (각진 모서리 + 어두운 외곽선, 둥근모서리/그림자 없음)
    private void StyleFlatButton(Button btn, Color face, string label, int fontSize)
    {
        if (btn == null) return;
        var img = btn.GetComponent<Image>();
        if (img != null) { img.sprite = null; img.type = Image.Type.Simple; img.color = face; AddPixelOutline(img, 3f); }

        var t = btn.GetComponentInChildren<Text>();
        if (t == null)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(btn.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            t = go.GetComponent<Text>();
        }
        t.font = LobbyUITheme.PlainFont;
        t.fontSize = fontSize;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = OptText;
        t.text = label;
    }

    private static bool IsGameStage()
    {
        string s = SceneManager.GetActiveScene().name;
        return s == "TutorialStage" || s == "Main Stage1" || s == "Main Stage2" ||
               s == "Main Stage3" || s == "TestStage" || s == "GameStage";
    }

    // ESC/톱니바퀴로 여는 옵션 창을 처음 한 번 구성한다 (탭 방식: 조작 / 음향).
    private void BuildOptionsUI()
    {
        if (_gameplayUIBuilt) return;
        Transform parent = menuPanel != null ? menuPanel.transform : transform;

        // 기존 오디오 패널(SettingsPanel)을 '음향' 탭 내용으로 재사용
        var ap = parent.Find("SettingsPanel");
        if (ap != null) _audioPanel = ap.gameObject;

        // 기존 계속하기/나가기 버튼은 숨김 (하단 바로 대체)
        if (resumeButton != null) resumeButton.gameObject.SetActive(false);
        if (exitButton != null) exitButton.gameObject.SetActive(false);

        // === 모달 딤 오버레이: 옵션창 뒤 게임 화면을 약하게 어둡게(픽셀아트는 보이게) ===
        _dimOverlay = new GameObject("DimOverlay", typeof(RectTransform), typeof(Image));
        _dimOverlay.transform.SetParent(transform, false); // 캔버스 루트 = 화면 전체
        var doRt = _dimOverlay.GetComponent<RectTransform>();
        doRt.anchorMin = Vector2.zero; doRt.anchorMax = Vector2.one;
        doRt.offsetMin = Vector2.zero; doRt.offsetMax = Vector2.zero;
        var doImg = _dimOverlay.GetComponent<Image>();
        doImg.color = new Color(0f, 0f, 0f, 0.45f); // 약한 반투명 (배경 여전히 보임)
        doImg.raycastTarget = true;                 // 뒤쪽 클릭 차단(모달)
        _dimOverlay.transform.SetAsFirstSibling();  // 옵션창보다 뒤

        // === 3중 픽셀 프레임: 어두운 외곽선 → 갈색 테두리 → 배경 ===
        var border = new GameObject("OptionsFrameBorder", typeof(RectTransform), typeof(Image));
        border.transform.SetParent(parent, false);
        var bRt = border.GetComponent<RectTransform>();
        bRt.anchorMin = bRt.anchorMax = new Vector2(0.5f, 0.5f);
        bRt.pivot = new Vector2(0.5f, 0.5f);
        bRt.anchoredPosition = Vector2.zero;
        bRt.sizeDelta = new Vector2(FRAME_W, FRAME_H);
        border.GetComponent<Image>().color = OptOutline;       // 바깥 어두운 외곽선
        border.transform.SetAsFirstSibling(); // 오디오 컨트롤/기타 요소보다 뒤

        var brown = new GameObject("OptionsFrameBrown", typeof(RectTransform), typeof(Image));
        brown.transform.SetParent(border.transform, false);
        var brRt = brown.GetComponent<RectTransform>();
        brRt.anchorMin = Vector2.zero; brRt.anchorMax = Vector2.one;
        brRt.offsetMin = new Vector2(5f, 5f); brRt.offsetMax = new Vector2(-5f, -5f);
        brown.GetComponent<Image>().color = OptBorder;         // 갈색 테두리

        var frame = new GameObject("OptionsFrame", typeof(RectTransform), typeof(Image));
        frame.transform.SetParent(brown.transform, false);
        var fRt = frame.GetComponent<RectTransform>();
        fRt.anchorMin = Vector2.zero; fRt.anchorMax = Vector2.one;
        fRt.offsetMin = new Vector2(6f, 6f); fRt.offsetMax = new Vector2(-6f, -6f);
        frame.GetComponent<Image>().color = OptPanelBg;        // 안쪽 배경
        Transform F = frame.transform;

        // 타이틀 (골드 강조 + 어두운 외곽선) — 중앙 상단
        var titleT = AddFixedLabel(F, "옵션", 40, OptGold, new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(200f, 50f));
        AddPixelOutline(titleT, 2f);
        MakeTitleDivider(F, -235f, 220f, -42f); // 왼쪽 세그먼트
        MakeTitleDivider(F,  235f, 220f, -42f); // 오른쪽 세그먼트 (X 버튼과의 간격 확보)

        // 오른쪽 상단 X(닫기) 버튼
        _closeBtn = MakeCloseButton(F);

        // 두 탭 공통 TMP 폰트 확보(글씨체 통일)
        if (_audioPanel != null)
        {
            var fsrc = _audioPanel.transform.Find("MicSensLabel")?.GetComponent<TMPro.TMP_Text>();
            if (fsrc != null) _uiFont = fsrc.font;
        }

        // 탭↔콘텐츠 세로 구분선
        MakeFrameDivider(F, COLDIV_X, COL_Y, 2f, COL_H);

        // 좌측 탭 버튼 (사이드 메뉴)
        _tabBtnControls = MakeTabButton(F, "조작", TAB_TOP_Y);
        _tabBtnControls.onClick.AddListener(() => SelectTab(0));
        _tabBtnAudio = MakeTabButton(F, "음향", TAB_BOT_Y);
        _tabBtnAudio.onClick.AddListener(() => SelectTab(1));

        // === 스크롤 뷰포트(콘텐츠가 많아도 창 크기는 고정) ===
        var scrollGO = new GameObject("ContentScroll", typeof(RectTransform), typeof(ScrollRect));
        scrollGO.transform.SetParent(F, false);
        var sRt = scrollGO.GetComponent<RectTransform>();
        sRt.anchorMin = sRt.anchorMax = new Vector2(0.5f, 0.5f); sRt.pivot = new Vector2(0.5f, 0.5f);
        sRt.anchoredPosition = new Vector2(COL_X, COL_Y); sRt.sizeDelta = new Vector2(COL_W, COL_H);
        _contentScroll = scrollGO.GetComponent<ScrollRect>();
        _contentScroll.horizontal = false; _contentScroll.vertical = true;
        _contentScroll.movementType = ScrollRect.MovementType.Clamped;
        _contentScroll.scrollSensitivity = 28f;

        var vpGO = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        vpGO.transform.SetParent(scrollGO.transform, false);
        var vpRt = vpGO.GetComponent<RectTransform>();
        vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one; vpRt.offsetMin = Vector2.zero; vpRt.offsetMax = Vector2.zero;
        var vpImg = vpGO.GetComponent<Image>(); vpImg.color = new Color(0f, 0f, 0f, 0f); vpImg.raycastTarget = true;
        _contentScroll.viewport = vpRt;
        _viewport = vpGO.transform;

        // 세로 스크롤바 (콘텐츠가 넘칠 때만 자동 표시)
        var sbGO = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        sbGO.transform.SetParent(scrollGO.transform, false);
        var sbRt = sbGO.GetComponent<RectTransform>();
        sbRt.anchorMin = new Vector2(1f, 0f); sbRt.anchorMax = new Vector2(1f, 1f); sbRt.pivot = new Vector2(1f, 1f);
        sbRt.sizeDelta = new Vector2(8f, 0f); sbRt.anchoredPosition = Vector2.zero;
        var sbImg = sbGO.GetComponent<Image>(); sbImg.sprite = null; sbImg.color = OptDropBg;
        var sb = sbGO.GetComponent<Scrollbar>(); sb.direction = Scrollbar.Direction.BottomToTop;
        var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handle.transform.SetParent(sbGO.transform, false);
        var hRt = handle.GetComponent<RectTransform>();
        hRt.anchorMin = Vector2.zero; hRt.anchorMax = Vector2.one; hRt.offsetMin = Vector2.zero; hRt.offsetMax = Vector2.zero;
        var hImg = handle.GetComponent<Image>(); hImg.sprite = null; hImg.color = OptBtnFace;
        sb.handleRect = hRt; sb.targetGraphic = hImg;
        _contentScroll.verticalScrollbar = sb;
        _contentScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        // 음향 탭 내용 = 기존 SettingsPanel: 뷰포트 안으로 이동(콘텐츠가 뷰포트에 맞아 스크롤 없음)
        if (_audioPanel != null)
        {
            _audioPanel.transform.SetParent(_viewport, false);
            var apRt = _audioPanel.GetComponent<RectTransform>();
            apRt.anchorMin = apRt.anchorMax = new Vector2(0.5f, 1f);
            apRt.pivot = new Vector2(0.5f, 1f);
            apRt.anchoredPosition = Vector2.zero;
            apRt.sizeDelta = new Vector2(COL_W, COL_H);
            var apImg = _audioPanel.GetComponent<Image>();
            if (apImg != null) apImg.enabled = false;
            foreach (var tmp in _audioPanel.GetComponentsInChildren<TMPro.TMP_Text>(true))
                if (tmp.text != null && tmp.text.Contains("Settings")) { tmp.gameObject.SetActive(false); break; }

            StyleAudioControls();
            LayoutAudioControls();
        }

        // 조작 탭 내용: 뷰포트 안, 콘텐츠 높이만큼(뷰포트보다 커서 스크롤됨)
        _controlsPanel = new GameObject("ControlsPanel", typeof(RectTransform), typeof(Image));
        _controlsPanel.transform.SetParent(_viewport, false);
        var cRt = _controlsPanel.GetComponent<RectTransform>();
        cRt.anchorMin = cRt.anchorMax = new Vector2(0.5f, 1f);
        cRt.pivot = new Vector2(0.5f, 1f);
        cRt.anchoredPosition = Vector2.zero;
        cRt.sizeDelta = new Vector2(COL_W, CONTROLS_H);
        var cImg = _controlsPanel.GetComponent<Image>();
        cImg.color = new Color(0f, 0f, 0f, 0f); cImg.raycastTarget = false;
        BuildControlsContent(_controlsPanel.transform);

        // 하단 구분선 + 액션 바
        MakeFrameDivider(F, 0f, BDIV_Y, 780f, 2f);
        var respawnBtn = MakeBottomButton(F, "다시하기", -BTN_HALF, OptRespawn);
        respawnBtn.onClick.AddListener(() => { if (PlayerMovement.RequestLocalRespawn()) CloseMenu(); });
        _respawnRow = respawnBtn.gameObject;
        _exitBtn2 = MakeBottomButton(F, "나가기", BTN_HALF, OptExit);

        BuildResetConfirm(F); // 기본값 복원 확인창(숨김)

        _gameplayUIBuilt = true;
        SelectTab(0);
    }

    // 중앙 기준 구분선(세로/가로 공용).
    private void MakeFrameDivider(Transform parent, float x, float y, float w, float h)
    {
        var go = new GameObject("Divider", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
        var img = go.GetComponent<Image>(); img.color = OptDivider; img.raycastTarget = false;
    }

    private void SelectTab(int tab)
    {
        _currentTab = tab;
        _awaitingBind = -1;
        if (_controlsPanel != null) _controlsPanel.SetActive(tab == 0);
        if (_audioPanel != null) _audioPanel.SetActive(tab == 1);
        if (_contentScroll != null)
        {
            var rt = (tab == 0 ? _controlsPanel : _audioPanel)?.GetComponent<RectTransform>();
            _contentScroll.content = rt;
            if (rt != null) { Canvas.ForceUpdateCanvases(); _contentScroll.verticalNormalizedPosition = 1f; }
        }
        if (_resetConfirm != null) _resetConfirm.SetActive(false);
        StyleTab(_tabBtnControls, tab == 0, "조작");
        StyleTab(_tabBtnAudio, tab == 1, "음향");
        RefreshBinds();
    }

    // 선택 탭은 틸 배경 + 골드 하이라이트 텍스트 + 왼쪽 골드 픽셀 마커.
    private void StyleTab(Button btn, bool selected, string label)
    {
        if (btn == null) return;
        StyleFlatButton(btn, selected ? OptBtnSel : OptBtnFace, label, 28);
        var t = btn.GetComponentInChildren<Text>();
        if (t != null) t.color = selected ? OptGoldHi : OptText;
        var m = btn.transform.Find("SelMarker");
        if (m != null) m.gameObject.SetActive(selected);
    }

    private Button MakeTabButton(Transform parent, string label, float y)
    {
        var go = new GameObject("Tab_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(TAB_X, y);
        rt.sizeDelta = new Vector2(TAB_W, TAB_H);
        var btn = go.GetComponent<Button>();
        StyleFlatButton(btn, OptBtnFace, label, 28);

        // 선택 표시용 골드 픽셀 마커(왼쪽 세로 바) — 평소엔 숨김
        var marker = new GameObject("SelMarker", typeof(RectTransform), typeof(Image));
        marker.transform.SetParent(go.transform, false);
        var mrt = marker.GetComponent<RectTransform>();
        mrt.anchorMin = new Vector2(0f, 0.5f); mrt.anchorMax = new Vector2(0f, 0.5f);
        mrt.pivot = new Vector2(0f, 0.5f);
        mrt.anchoredPosition = new Vector2(8f, 0f);
        mrt.sizeDelta = new Vector2(10f, 34f);
        var mimg = marker.GetComponent<Image>();
        mimg.color = OptGold; mimg.raycastTarget = false;
        marker.SetActive(false);
        return btn;
    }

    private Button MakeBottomButton(Transform parent, string label, float x, Color face)
    {
        var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, BOTTOM_Y);
        rt.sizeDelta = new Vector2(BTN_W, BTN_H);
        var btn = go.GetComponent<Button>();
        StyleFlatButton(btn, face, label, 28);
        return btn;
    }

    // 옵션 창 오른쪽 상단 X(닫기) 버튼
    private Button MakeCloseButton(Transform parent)
    {
        var go = new GameObject("Btn_Close", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-12f, -12f);
        rt.sizeDelta = new Vector2(36f, 36f);

        var img = go.GetComponent<Image>();
        img.sprite = null;
        img.type = Image.Type.Simple;
        img.color = OptBtnFace;
        AddPixelOutline(img, 2f);

        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        var cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
        cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        cb.fadeDuration = 0.05f;
        btn.colors = cb;

        var textGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
        textGo.transform.SetParent(go.transform, false);
        var trt = textGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;

        var t = textGo.GetComponent<Text>();
        t.font = LobbyUITheme.PlainFont;
        t.fontSize = 22;
        t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = OptText;
        t.text = "X";
        AddPixelOutline(t, 1.5f);

        btn.onClick.AddListener(CloseMenu);
        return btn;
    }

    // 콘텐츠 그리드용 라벨 (anchor는 콘텐츠 상단 중앙 고정, pivotX로 좌/중/우 정렬)
    private Text AddGridLabel(Transform parent, string text, int fontSize, Color color,
                             float pivotX, float x, float y, float w, float h, TextAnchor align)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(pivotX, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
        var t = go.GetComponent<Text>();
        t.font = LobbyUITheme.PlainFont; t.fontSize = fontSize;
        t.alignment = align; t.color = color; t.text = text; t.raycastTarget = false;
        return t;
    }

    // 콘텐츠 그리드용 버튼 (anchor는 콘텐츠 상단 중앙 고정)
    private Button MakeGridButton(Transform parent, string label, float x, float y, float w, float h, Color face, int fontSize)
    {
        var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
        var btn = go.GetComponent<Button>();
        StyleFlatButton(btn, face, label, fontSize);
        return btn;
    }

    private Text AddFixedLabel(Transform parent, string text, int fontSize, Color color, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var t = go.GetComponent<Text>();
        t.font = LobbyUITheme.PlainFont; t.fontSize = fontSize;
        t.alignment = TextAnchor.MiddleCenter; t.color = color; t.text = text;
        return t;
    }

    // 로비에서 설정을 여는 톱니바퀴 버튼 (메뉴 닫혀있고 인게임이 아닐 때만 표시)
    private void EnsureGearButton()
    {
        if (_gearButton != null) return;
        var go = new GameObject("GearButton", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(1f, 1f);
        // 트로피(랭킹) 버튼과 같은 높이(y-40)·크기(92)로, 그 왼쪽에 배치
        rt.anchoredPosition = new Vector2(-150f, -40f); rt.sizeDelta = new Vector2(92f, 92f);

        // 갈색 버튼 배경(#6A472A) + 어두운 외곽선(#2F1C16)
        var bg = go.GetComponent<Image>();
        bg.sprite = null;
        bg.color = new Color32(0x6A, 0x47, 0x2A, 0xFF);
        var bgOutline = go.AddComponent<UnityEngine.UI.Outline>();
        bgOutline.effectColor = new Color32(0x2F, 0x1C, 0x16, 0xFF);
        bgOutline.effectDistance = new Vector2(3f, -3f);

        // 골드 아이콘(#FAD16A) + 외곽선(#2F1C16)
        var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGo.transform.SetParent(go.transform, false);
        var irt = iconGo.GetComponent<RectTransform>();
        irt.anchorMin = new Vector2(0.14f, 0.14f); irt.anchorMax = new Vector2(0.86f, 0.86f);
        irt.offsetMin = Vector2.zero; irt.offsetMax = Vector2.zero;
        var iimg = iconGo.GetComponent<Image>();
        var gearSp = Resources.Load<Sprite>("Title/skymon-settings");
        iimg.sprite = gearSp != null ? gearSp : GetGearSprite();
        iimg.preserveAspect = true;
        iimg.color = new Color32(0xFA, 0xD1, 0x6A, 0xFF);
        var iconOutline = iconGo.AddComponent<UnityEngine.UI.Outline>();
        iconOutline.effectColor = new Color32(0x2F, 0x1C, 0x16, 0xFF);
        iconOutline.effectDistance = new Vector2(2f, -2f);

        var btn = go.GetComponent<Button>();
        btn.targetGraphic = bg;
        var cb = btn.colors;
        cb.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
        cb.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        cb.fadeDuration = 0.05f;
        btn.colors = cb;
        btn.onClick.AddListener(OpenMenu);
        _gearButton = go;
    }

    // 톱니바퀴 아이콘을 절차적으로 그려서 스프라이트로 만든다 (프로젝트에 기어 에셋이 없음).
    private static Sprite _gearSprite;
    private static Sprite GetGearSprite()
    {
        if (_gearSprite != null) return _gearSprite;

        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var px = new Color[S * S];
        var clear = new Color(1f, 1f, 1f, 0f);
        for (int i = 0; i < px.Length; i++) px[i] = clear;

        float c = (S - 1) / 2f;
        float rBody = S * 0.30f;   // 본체 원
        float rTooth = S * 0.46f;  // 톱니 끝
        float rHole = S * 0.13f;   // 가운데 구멍
        const int teeth = 8;
        float sector = Mathf.PI * 2f / teeth;

        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = x - c, dy = y - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx);
                float frac = Mathf.Repeat(ang, sector) / sector; // 0..1
                bool inTooth = d <= rTooth && frac > 0.28f && frac < 0.72f; // 톱니 사다리꼴
                bool inBody = d <= rBody;
                bool solid = (inBody || inTooth) && d >= rHole;

                // 가장자리 부드럽게 (1px 페더)
                float a = solid ? 1f : 0f;
                if (solid)
                {
                    float edge = Mathf.Min(rTooth - d, d - rHole);
                    if (inBody && !inTooth) edge = Mathf.Min(rBody - d, d - rHole);
                    a = Mathf.Clamp01(edge); // 경계 1px 알파 감쇠
                }
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        _gearSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        return _gearSprite;
    }

    private Text AddLabelRow(Transform parent, string text, int fontSize, Color color, ref float y, float height)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(380f, height);
        var t = go.GetComponent<Text>();
        t.font = LobbyUITheme.PlainFont; // 한글 표기 가능한 폰트
        t.fontSize = fontSize;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = color;
        t.text = text;
        y -= (height + 6f);
        return t;
    }

    private Button AddButtonRow(Transform parent, string label, ref float y, Color face, float height)
    {
        var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(470f, height);
        var btn = go.GetComponent<Button>();
        StyleFlatButton(btn, face, label, 22);
        y -= (height + 10f);
        return btn;
    }

    // 얇은 구분선 (조작 패널 섹션용). 중앙 기준 x, 상단 기준 y.
    private void MakeDivider(Transform parent, float x, float y, float w)
    {
        var go = new GameObject("Divider", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, 2f);
        var img = go.GetComponent<Image>();
        img.color = OptDivider; img.raycastTarget = false;
    }

    // 픽셀 키캡 1개. info=true → 눌리지 않는 안내용(어두운 톤+보조색). interactive=true → Button 부착.
    private GameObject BuildKeycap(Transform parent, float pivotX, float x, float y, float w, float h,
                                   string text, int fontSize, bool info, bool interactive)
    {
        var comps = interactive
            ? new System.Type[] { typeof(RectTransform), typeof(Image), typeof(Button) }
            : new System.Type[] { typeof(RectTransform), typeof(Image) };
        var go = new GameObject("Keycap", comps);
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(pivotX, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
        var capImg = go.GetComponent<Image>();
        capImg.sprite = null; capImg.type = Image.Type.Simple;
        capImg.color = info ? OptPanelBg : OptKeycapBg;
        AddPixelOutline(capImg, 3f);

        var edge = new GameObject("Edge", typeof(RectTransform), typeof(Image));
        edge.transform.SetParent(go.transform, false);
        var ert = edge.GetComponent<RectTransform>();
        ert.anchorMin = Vector2.zero; ert.anchorMax = Vector2.one;
        ert.offsetMin = new Vector2(2f, 2f); ert.offsetMax = new Vector2(-2f, -2f);
        var ei = edge.GetComponent<Image>();
        ei.color = info ? OptPanelBg : OptKeycapBg; ei.raycastTarget = false;
        var eo = edge.AddComponent<UnityEngine.UI.Outline>();
        eo.effectColor = info ? OptDivider : OptKeycapBd; eo.effectDistance = new Vector2(1f, -1f); eo.useGraphicAlpha = false;

        var key = new GameObject("Key", typeof(RectTransform));
        key.transform.SetParent(go.transform, false);
        var krt = key.GetComponent<RectTransform>();
        krt.anchorMin = Vector2.zero; krt.anchorMax = Vector2.one;
        krt.offsetMin = Vector2.zero; krt.offsetMax = Vector2.zero;
        var kt = key.AddComponent<TMPro.TextMeshProUGUI>();
        if (_uiFont != null) kt.font = _uiFont;
        kt.fontSize = fontSize;
        kt.alignment = TMPro.TextAlignmentOptions.Center; kt.color = info ? OptHint : OptText;
        kt.raycastTarget = false; kt.enableWordWrapping = false; kt.overflowMode = TMPro.TextOverflowModes.Overflow;
        kt.text = text;

        if (interactive) go.GetComponent<Button>().targetGraphic = capImg;
        return go;
    }

    // TMP 라벨 (음향 탭과 동일한 글씨체/정렬 기준). anchor는 콘텐츠 상단 중앙 고정, pivotX로 좌/중/우.
    private TMPro.TMP_Text AddTMPLabel(Transform parent, string text, float size, Color color,
                                       float pivotX, float x, float y, float w, float h, TMPro.TextAlignmentOptions align)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(pivotX, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
        var t = go.AddComponent<TMPro.TextMeshProUGUI>();
        if (_uiFont != null) t.font = _uiFont;
        t.fontSize = size; t.color = color; t.alignment = align; t.text = text;
        t.raycastTarget = false; t.enableWordWrapping = false; t.overflowMode = TMPro.TextOverflowModes.Overflow;
        return t;
    }

    // 키캡 버튼 하나 + 재설정 바인딩 등록(이동/점프/스킬 공용).
    private void AddKeycapBind(Transform parent, float x, float y, float w, float h,
                              string name, System.Func<KeyCode> get, System.Action<KeyCode> set)
    {
        var cap = BuildKeycap(parent, 0f, x, y, w, h, SkillKeybinds.KeyLabel(get()), 20, false, true);
        var entry = new BindEntry { name = name, get = get, set = set, label = cap.GetComponentInChildren<TMPro.TMP_Text>() };
        int idx = _binds.Count;
        _binds.Add(entry);
        cap.GetComponent<Button>().onClick.AddListener(() => { _awaitingBind = idx; RefreshBinds(); });
    }

    // 이동 행: 라벨 + [기본][보조] 두 키캡
    private void AddMoveRow(Transform CP, string label, MovementKeybinds.Act act, float rowY)
    {
        const float rowH = 36f, capW = 72f, gap = 12f, capH = 34f;
        AddTMPLabel(CP, label, 19, OptText, 0f, IN_L, rowY, 100f, rowH, TMPro.TextAlignmentOptions.MidlineLeft);
        float cy = rowY - (rowH - capH) / 2f;
        var a = act;
        AddKeycapBind(CP, CTRL_X, cy, capW, capH, label + " 기본", () => MovementKeybinds.Get(a, 0), kc => MovementKeybinds.Set(a, 0, kc));
        AddKeycapBind(CP, CTRL_X + capW + gap, cy, capW, capH, label + " 보조", () => MovementKeybinds.Get(a, 1), kc => MovementKeybinds.Set(a, 1, kc));
    }

    // '조작' 탭 콘텐츠: 기본 조작(이동/점프) + 스킬 단축키 — 모두 재설정 가능. 음향 탭과 동일 기준.
    private void BuildControlsContent(Transform CP)
    {
        _binds.Clear();

        // ===== 기본 조작 =====
        AddTMPLabel(CP, "기본 조작", 22, OptHeader, 0f, IN_L, -12f, 240f, 26f, TMPro.TextAlignmentOptions.MidlineLeft);
        MakeDivider(CP, 0f, -42f, IN_W);
        AddMoveRow(CP, "위",   MovementKeybinds.Act.Up,    -54f);
        AddMoveRow(CP, "아래", MovementKeybinds.Act.Down,  -96f);
        AddMoveRow(CP, "왼쪽", MovementKeybinds.Act.Left,  -138f);
        AddMoveRow(CP, "오른쪽", MovementKeybinds.Act.Right, -180f);
        // 점프 (단일 바인딩)
        AddTMPLabel(CP, "점프", 19, OptText, 0f, IN_L, -222f, 100f, 36f, TMPro.TextAlignmentOptions.MidlineLeft);
        AddKeycapBind(CP, CTRL_X, -223f, 156f, 34f, "점프",
                      () => MovementKeybinds.Get(MovementKeybinds.Act.Jump, 0),
                      kc => MovementKeybinds.Set(MovementKeybinds.Act.Jump, 0, kc));

        // ===== 스킬 단축키 =====
        AddTMPLabel(CP, "스킬 단축키", 22, OptHeader, 0f, IN_L, -268f, 240f, 26f, TMPro.TextAlignmentOptions.MidlineLeft);
        MakeDivider(CP, 0f, -298f, IN_W);
        for (int slot = 1; slot <= SkillKeybinds.MaxSlot; slot++)
        {
            int s = slot;
            float ry = -310f - (s - 1) * 42f;
            AddTMPLabel(CP, $"스킬 {s}", 19, OptText, 0f, IN_L, ry, 100f, 36f, TMPro.TextAlignmentOptions.MidlineLeft);
            AddKeycapBind(CP, CTRL_X, ry - 1f, 156f, 34f, $"스킬 {s}",
                          () => SkillKeybinds.GetSlotKey(s), kc => SkillKeybinds.SetSlotKey(s, kc));
        }

        // ===== 음성 대화 단축키 =====
        float vy = -436f;
        AddTMPLabel(CP, "음성 대화", 22, OptHeader, 0f, IN_L, vy, 240f, 26f, TMPro.TextAlignmentOptions.MidlineLeft);
        MakeDivider(CP, 0f, vy - 30f, IN_W);
        AddTMPLabel(CP, "푸시투톡 키", 19, OptText, 0f, IN_L, vy - 42f, 120f, 36f, TMPro.TextAlignmentOptions.MidlineLeft);
        AddKeycapBind(CP, CTRL_X, vy - 43f, 156f, 34f, "푸시투톡",
                      () => VoiceChatSettings.PushToTalkKey, kc => { VoiceChatSettings.PushToTalkKey = kc; RefreshVoiceModeUI(); });

        // 기본값으로 복원 (확인창 띄움) + 안내문
        var resetBtn = MakeTMPButton(CP, "기본값으로 복원", 0f, vy - 100f, 248f, 44f, OptBtnFace, 22f);
        resetBtn.onClick.AddListener(() => { if (_resetConfirm != null) _resetConfirm.SetActive(true); });
        _rebindHintText = AddTMPLabel(CP, "설정할 키를 눌러 변경하세요", 16, OptHint, 0.5f, 0f, vy - 154f, IN_W, 22f, TMPro.TextAlignmentOptions.Center);
    }

    // TMP 텍스트를 쓰는 픽셀 버튼 (음향 탭 컨트롤과 글씨체 통일).
    private Button MakeTMPButton(Transform parent, string label, float x, float y, float w, float h, Color face, float fontSize)
    {
        var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
        var img = go.GetComponent<Image>();
        img.sprite = null; img.type = Image.Type.Simple; img.color = face; AddPixelOutline(img, 3f);
        var btn = go.GetComponent<Button>(); btn.targetGraphic = img;
        var t = AddTMPLabel(go.transform, label, fontSize, OptText, 0.5f, 0f, 0f, w, h, TMPro.TextAlignmentOptions.Center);
        var trt = t.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
        return btn;
    }

    // 제목 양옆 구분선 세그먼트 (──── 옵션 ────). x=중심, w=너비, y=제목 세로 중심.
    private void MakeTitleDivider(Transform parent, float x, float w, float y)
    {
        var go = new GameObject("TitleDivider", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, 2f);
        var img = go.GetComponent<Image>();
        img.color = OptDivider; img.raycastTarget = false;
    }

    private void RefreshBinds()
    {
        for (int i = 0; i < _binds.Count; i++)
        {
            var e = _binds[i];
            if (e.label == null) continue;
            e.label.text = (_awaitingBind == i) ? "…" : SkillKeybinds.KeyLabel(e.get());
            e.label.color = (_awaitingBind == i) ? OptGoldHi : OptText;
        }
        if (_rebindHintText != null)
        {
            _rebindHintText.text = _awaitingBind >= 0
                ? "새 키를 누르세요  (ESC 취소)"
                : "설정할 키를 눌러 변경하세요";
            _rebindHintText.color = OptHint;
        }
    }

    // 모든 키를 기본값으로 되돌리기 전 경고 확인창.
    private void BuildResetConfirm(Transform F)
    {
        _resetConfirm = new GameObject("ResetConfirm", typeof(RectTransform), typeof(Image));
        _resetConfirm.transform.SetParent(F, false);
        var rc = _resetConfirm.GetComponent<RectTransform>();
        rc.anchorMin = Vector2.zero; rc.anchorMax = Vector2.one; rc.offsetMin = Vector2.zero; rc.offsetMax = Vector2.zero;
        _resetConfirm.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f); // 내부 딤 + 클릭 차단

        // 중앙 패널 (어두운 외곽선 + 배경)
        var border = new GameObject("Box", typeof(RectTransform), typeof(Image));
        border.transform.SetParent(_resetConfirm.transform, false);
        var bRt = border.GetComponent<RectTransform>();
        bRt.anchorMin = bRt.anchorMax = new Vector2(0.5f, 0.5f); bRt.pivot = new Vector2(0.5f, 0.5f);
        bRt.anchoredPosition = Vector2.zero; bRt.sizeDelta = new Vector2(480f, 200f);
        border.GetComponent<Image>().color = OptOutline;
        var bg = new GameObject("Bg", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(border.transform, false);
        var bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one; bgRt.offsetMin = new Vector2(4f, 4f); bgRt.offsetMax = new Vector2(-4f, -4f);
        bg.GetComponent<Image>().color = OptPanelBg;
        Transform B = bg.transform;

        AddTMPLabel(B, "모든 조작/스킬 키를", 21, OptHeader, 0.5f, 0f, -34f, 440f, 28f, TMPro.TextAlignmentOptions.Center);
        AddTMPLabel(B, "기본값으로 되돌릴까요?", 21, OptHeader, 0.5f, 0f, -66f, 440f, 28f, TMPro.TextAlignmentOptions.Center);

        var yes = MakeTMPButton(B, "복원", -110f, -150f, 180f, 46f, OptBtnSel, 22f);
        yes.onClick.AddListener(() =>
        {
            MovementKeybinds.ResetToDefaults();
            SkillKeybinds.ResetToDefaults();
            VoiceChatSettings.ResetToDefaults();
            RefreshVoiceModeUI();
            _awaitingBind = -1;
            RefreshBinds();
            _resetConfirm.SetActive(false);
        });
        var no = MakeTMPButton(B, "취소", 110f, -150f, 180f, 46f, OptBtnFace, 22f);
        no.onClick.AddListener(() => _resetConfirm.SetActive(false));

        _resetConfirm.SetActive(false);
    }

    // === 음향 탭 컨트롤(프리팹 요소) 픽셀 팔레트로 통일 ===
    private static readonly Color OptDropBg = Hex(0x20, 0x21, 0x2A); // 드롭다운 배경

    private void PixelImage(Image img, Color c, float outline)
    {
        if (img == null) return;
        img.sprite = null; img.type = Image.Type.Simple; img.color = c;
        if (outline > 0f) AddPixelOutline(img, outline);
    }

    private void StyleAudioControls()
    {
        if (_audioPanel == null) return;

        EnsureBgmControls();
        StyleSlider(masterVolumeSlider);
        StyleSlider(_bgmSlider);
        StyleSlider(_sfxSlider);
        StyleSlider(micSensitivitySlider);
        StyleDropdown(outputDeviceDropdown);
        StyleDropdown(micDropdown);

        // 마이크 입력 레벨 미터: 배경 + 틸(#4F91AD) 채움
        var meterBg = _audioPanel.transform.Find("MicMeterBg");
        if (meterBg != null) PixelImage(meterBg.GetComponent<Image>(), OptBtnFace, 2f);
        if (micVolumeMeter != null) { micVolumeMeter.sprite = null; micVolumeMeter.color = OptBtnSel; }

        // 구분선
        var sep = _audioPanel.transform.Find("Separator");
        if (sep != null) { var si = sep.GetComponent<Image>(); if (si != null) { si.sprite = null; si.color = OptDivider; } }

        // 테스트 버튼 (TMP 텍스트라 StyleFlatButton 대신 직접 처리) — 문구는 항상 "테스트"
        if (micTestButton != null)
        {
            PixelImage(micTestButton.GetComponent<Image>(), _isTestingMic ? OptBtnSel : OptBtnFace, 3f);
            var tt = micTestButton.GetComponentInChildren<TMPro.TMP_Text>();
            if (tt != null) { tt.color = OptText; tt.text = "테스트"; tt.fontSize = 18f; tt.enableAutoSizing = false; }
            if (micTestButtonText != null) micTestButtonText.text = "테스트";
        }
    }

    private void StyleSlider(Slider s)
    {
        if (s == null) return;
        var bgTr = s.transform.Find("Background");
        if (bgTr != null) PixelImage(bgTr.GetComponent<Image>(), OptBtnFace, 2f); // 비활성 트랙
        var fill = s.fillRect != null ? s.fillRect.GetComponent<Image>() : null;
        if (fill != null) { fill.sprite = null; fill.color = OptGold; }            // 활성 채움(골드)
        var handle = s.handleRect != null ? s.handleRect.GetComponent<Image>() : null;
        if (handle != null) { handle.sprite = null; handle.color = OptGoldHi; AddPixelOutline(handle, 2f); }
    }

    private void StyleDropdown(TMP_Dropdown d)
    {
        if (d == null) return;
        UnityEngine.UI.Outline outline = null;
        var img = d.GetComponent<Image>();
        if (img != null) { img.sprite = null; img.type = Image.Type.Simple; img.color = OptDropBg; outline = AddPixelOutline(img, 2f); }
        // 긴 장치 이름: 한 줄 유지 + 말줄임(...) (컨트롤 높이 증가 방지)
        if (d.captionText != null)
        {
            d.captionText.color = OptText;
            d.captionText.enableWordWrapping = false;
            d.captionText.overflowMode = TMPro.TextOverflowModes.Ellipsis;
        }
        TMPro.TMP_Text arrow = null;
        var arrowT = d.transform.Find("Arrow");
        if (arrowT != null) { arrow = arrowT.GetComponent<TMPro.TMP_Text>(); if (arrow != null) arrow.color = OptGold; }
        var tmpl = d.template != null ? d.template.GetComponent<Image>() : null;
        if (tmpl != null) { tmpl.sprite = null; tmpl.color = OptDropBg; AddPixelOutline(tmpl, 2f); }
        if (d.itemText != null) { d.itemText.color = OptText; d.itemText.enableWordWrapping = false; d.itemText.overflowMode = TMPro.TextOverflowModes.Ellipsis; }

        // 목록 항목별 실제 호버/선택 상태 구현 (커스텀)
        StyleDropdownItems(d);

        // 호버: 배경 살짝 밝게(ColorBlock) + 외곽선/화살표 미세 강조 (네온/글로우 없음)
        var cb = d.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
        cb.pressedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        cb.selectedColor = Color.white;
        cb.fadeDuration = 0.05f;
        d.colors = cb;
        AddDropdownHover(d.gameObject, outline, arrow);
    }

    // 드롭다운 목록 항목(Template Item)에 호버/선택 상태를 커스텀 구현.
    // - 호버: 틸 배경(ColorBlock) + 좌측 골드 바 + 글씨 밝게 (DropdownItemHover, 복제돼도 동작)
    // - 선택(현재 값): 우측 골드 점(Toggle.graphic) — 호버와 분명히 구분
    private void StyleDropdownItems(TMP_Dropdown d)
    {
        if (d == null || d.template == null) return;
        var item = d.template.Find("Viewport/Content/Item");
        if (item == null) return;
        var toggle = item.GetComponent<Toggle>();

        // 배경 이미지 확보 (없으면 추가) + ColorBlock 호버(틸)
        var bgT = item.Find("ItemBg");
        Image bg;
        if (bgT == null)
        {
            var bgo = new GameObject("ItemBg", typeof(RectTransform), typeof(Image));
            bgo.transform.SetParent(item, false);
            bgo.transform.SetAsFirstSibling();
            var brt = bgo.GetComponent<RectTransform>();
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
            bg = bgo.GetComponent<Image>();
        }
        else bg = bgT.GetComponent<Image>();
        bg.sprite = null; bg.color = Color.white; bg.raycastTarget = true;

        if (toggle != null)
        {
            toggle.targetGraphic = bg;
            toggle.transition = Selectable.Transition.ColorTint;
            var cbi = toggle.colors;
            cbi.normalColor = OptDropBg;                 // 기본: 어두운 회색
            cbi.highlightedColor = OptBtnSel;            // 호버: 틸
            cbi.pressedColor = Hex(0x3E, 0x76, 0x8C);    // 눌림: 어두운 틸
            cbi.selectedColor = OptDropBg;               // 포커스만으로는 호버와 겹치지 않게
            cbi.fadeDuration = 0.04f;
            toggle.colors = cbi;
        }

        // 좌측 골드 호버 바 (평소 숨김)
        var barT = item.Find("HoverBar");
        GameObject bar = barT != null ? barT.gameObject : new GameObject("HoverBar", typeof(RectTransform), typeof(Image));
        if (barT == null) bar.transform.SetParent(item, false);
        var barRt = bar.GetComponent<RectTransform>();
        barRt.anchorMin = new Vector2(0f, 0f); barRt.anchorMax = new Vector2(0f, 1f); barRt.pivot = new Vector2(0f, 0.5f);
        barRt.sizeDelta = new Vector2(5f, 0f); barRt.anchoredPosition = new Vector2(2f, 0f);
        var barImg = bar.GetComponent<Image>(); barImg.sprite = null; barImg.color = OptGold; barImg.raycastTarget = false;
        bar.SetActive(false);

        // 선택(현재 값) 표시: 우측 골드 점 → Toggle.graphic (isOn 일 때만 표시)
        var selT = item.Find("SelDot");
        GameObject sel = selT != null ? selT.gameObject : new GameObject("SelDot", typeof(RectTransform), typeof(Image));
        if (selT == null) sel.transform.SetParent(item, false);
        var selRt = sel.GetComponent<RectTransform>();
        selRt.anchorMin = new Vector2(1f, 0.5f); selRt.anchorMax = new Vector2(1f, 0.5f); selRt.pivot = new Vector2(1f, 0.5f);
        selRt.anchoredPosition = new Vector2(-10f, 0f); selRt.sizeDelta = new Vector2(12f, 12f);
        var selImg = sel.GetComponent<Image>(); selImg.sprite = null; selImg.color = OptGold; selImg.raycastTarget = false;
        if (toggle != null) toggle.graphic = selImg;

        // 라벨: 한 줄 + 말줄임, 화살표/점 영역 침범 방지
        var label = item.Find("Item Label");
        TMPro.TMP_Text lt = label != null ? label.GetComponent<TMPro.TMP_Text>() : null;
        if (lt != null)
        {
            lt.color = OptText; lt.enableWordWrapping = false; lt.overflowMode = TMPro.TextOverflowModes.Ellipsis;
            var lrt = lt.rectTransform;
            lrt.offsetMin = new Vector2(16f, lrt.offsetMin.y);   // 좌측 바/여백
            lrt.offsetMax = new Vector2(-26f, lrt.offsetMax.y);  // 우측 선택점 여백
        }

        // 복제돼도 각 항목이 자기 자식만 제어하는 호버 컴포넌트
        var hov = item.GetComponent<DropdownItemHover>();
        if (hov == null) hov = item.gameObject.AddComponent<DropdownItemHover>();
        hov.hoverBar = bar;
        hov.label = lt;
    }

    // 드롭다운 호버 시 외곽선(밝은 회색)과 화살표(하이라이트 골드) 강조 / 해제
    private void AddDropdownHover(GameObject go, UnityEngine.UI.Outline outline, TMPro.TMP_Text arrow)
    {
        var trig = go.GetComponent<UnityEngine.EventSystems.EventTrigger>();
        if (trig == null) trig = go.AddComponent<UnityEngine.EventSystems.EventTrigger>();
        trig.triggers.Clear();
        var enter = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => { if (outline != null) outline.effectColor = OptKeycapBd; if (arrow != null) arrow.color = OptGoldHi; });
        var exit = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => { if (outline != null) outline.effectColor = OptOutline; if (arrow != null) arrow.color = OptGold; });
        trig.triggers.Add(enter); trig.triggers.Add(exit);
    }

    // === 음향 탭 컨트롤 위치·간격을 공통 그리드로 정렬 ===
    // 그리드: 라벨열[IN_L..] / 컨트롤열[AUD_CTRL_X] / 값열[AUD_VAL_X] / 테스트[IN_R]
    private const float AUD_LBL_W  = 110f;    // 라벨열 고정 폭
    private const float AUD_CTRL_X = -132f;   // 모든 컨트롤(드롭다운/슬라이더/미터) 시작 X (동일)
    private const float AUD_SW     = 238f;    // 슬라이더/미터 너비 (동일)
    private const float AUD_DROPW  = 388f;    // 드롭다운 너비 (CTRL_X → IN_R)
    private const float AUD_SLH    = 24f;     // 슬라이더/미터 높이 (동일)
    private const float AUD_VAL_X  = 116f;    // 수치(100%/79%) 시작 X (동일)
    private const float AUD_VAL_W  = 54f;
    private const float AUD_TESTW  = 76f;     // 테스트 버튼 너비
    private const float AUD_RLH    = 34f;     // 음향 행 라벨 높이 (컨트롤 수직 중앙 기준)
    private TMPro.TMP_Text _volumeLabel, _outDevLabel, _inDevLabel;

    // 라벨 기준 세로 중앙에 컨트롤을 두기 위한 y (pivot top, anchoredPosition)
    private static float CtrlY(float labelDepth, float ctrlH) => -(labelDepth + (AUD_RLH - ctrlH) / 2f);

    private void LayoutAudioControls()
    {
        if (_audioPanel == null) return;
        Transform A = _audioPanel.transform;

        // 그룹 순서: 사운드 설정(배경음·효과음) → 출력 설정 → 입력 설정. 각 그룹 = 헤더 + 하단 밑줄 + 행들.
        // 기존 프리팹 구분선은 그룹 밑줄로 대체
        var sep = A.Find("Separator"); if (sep != null) sep.gameObject.SetActive(false);

        // === 사운드 설정 === (프리팹에 없는 그룹 → "출력 설정" 헤더를 복제해 헤더 생성)
        var outHead = A.Find("OutputLabel");
        var soundHead = A.Find("SoundLabel");
        if (soundHead == null && outHead != null)
        {
            soundHead = Instantiate(outHead.gameObject, outHead.parent).transform;
            soundHead.name = "SoundLabel";
        }
        SetRT(soundHead, 0f, IN_L, -16f, IN_W, 26f); AlignTMP(soundHead, TMPro.TextAlignmentOptions.MidlineLeft); SetTMP(soundHead, "사운드 설정", 22f, OptHeader);
        SetAudioDivider("SoundGroupLine", -48f, IN_W);

        EnsureBgmControls(); // 배경음/효과음 슬라이더·창 비활성 버튼 (기존 컨트롤 복제)
        LayoutVolumeRow("BgmLabel", "배경음", _bgmSlider, _bgmValueText, 62f);
        LayoutVolumeRow("SfxLabel", "효과음", _sfxSlider, _sfxValueText, 100f);

        // === 출력 설정 ===
        SetRT(outHead, 0f, IN_L, -146f, IN_W, 26f); AlignTMP(outHead, TMPro.TextAlignmentOptions.MidlineLeft); SetTMP(outHead, "출력 설정", 22f, OptHeader);
        SetAudioDivider("OutGroupLine", -178f, IN_W);

        _outDevLabel = MakeAudioLabel("OutDevLabel", "출력 장치");
        SetRT(_outDevLabel.transform, 0f, IN_L, -192f, AUD_LBL_W, AUD_RLH); SetTMP(_outDevLabel.transform, null, 19f, OptText);
        SetRT(A.Find("OutputDeviceDropdown"), 0f, AUD_CTRL_X, CtrlY(192f, 40f), AUD_DROPW, 40f);

        _volumeLabel = MakeAudioLabel("VolumeLabel", "전체 음량");
        SetRT(_volumeLabel.transform, 0f, IN_L, -238f, AUD_LBL_W, AUD_RLH); SetTMP(_volumeLabel.transform, null, 19f, OptText);
        SetRT(masterVolumeSlider != null ? masterVolumeSlider.transform : null, 0f, AUD_CTRL_X, CtrlY(238f, AUD_SLH), AUD_SW, AUD_SLH);
        SetRT(A.Find("VolumeValueText"), 0f, AUD_VAL_X, -238f, AUD_VAL_W, AUD_RLH); AlignTMP(A.Find("VolumeValueText"), TMPro.TextAlignmentOptions.MidlineLeft); SetTMP(A.Find("VolumeValueText"), null, 19f, OptText);

        var bgMuteLabel = MakeAudioLabel("BgMuteLabel", "창 비활성");
        SetRT(bgMuteLabel != null ? bgMuteLabel.transform : null, 0f, IN_L, -276f, AUD_LBL_W, AUD_RLH); SetTMP(bgMuteLabel != null ? bgMuteLabel.transform : null, null, 19f, OptText);
        if (_bgMuteButton != null) SetRT(_bgMuteButton.transform, 0f, AUD_CTRL_X, -276f, 120f, AUD_RLH);

        // === 입력 설정 ===
        var inHead  = A.Find("InputLabel");  SetRT(inHead,  0f, IN_L, -322f, IN_W, 26f); AlignTMP(inHead, TMPro.TextAlignmentOptions.MidlineLeft); SetTMP(inHead, "입력 설정", 22f, OptHeader);
        SetAudioDivider("InGroupLine", -354f, IN_W);

        _inDevLabel = MakeAudioLabel("InDevLabel", "입력 장치");
        SetRT(_inDevLabel.transform, 0f, IN_L, -368f, AUD_LBL_W, AUD_RLH); SetTMP(_inDevLabel.transform, null, 19f, OptText);
        SetRT(A.Find("MicDropdown"), 0f, AUD_CTRL_X, CtrlY(368f, 40f), AUD_DROPW, 40f);

        var micSens = A.Find("MicSensLabel");
        SetRT(micSens, 0f, IN_L, -414f, AUD_LBL_W, AUD_RLH); AlignTMP(micSens, TMPro.TextAlignmentOptions.MidlineLeft); SetTMP(micSens, "마이크 감도", 19f, OptText);
        SetRT(micSensitivitySlider != null ? micSensitivitySlider.transform : null, 0f, AUD_CTRL_X, CtrlY(414f, AUD_SLH), AUD_SW, AUD_SLH);
        SetRT(A.Find("MicSensValueText"), 0f, AUD_VAL_X, -414f, AUD_VAL_W, AUD_RLH); AlignTMP(A.Find("MicSensValueText"), TMPro.TextAlignmentOptions.MidlineLeft); SetTMP(A.Find("MicSensValueText"), null, 19f, OptText);
        // 테스트 버튼: 마이크 감도 행 오른쪽 끝(IN_R)
        if (micTestButton != null) SetRT(micTestButton.transform, 1f, IN_R, -414f, AUD_TESTW, AUD_RLH);

        var meterLabel = A.Find("MeterLabel");
        SetRT(meterLabel, 0f, IN_L, -452f, AUD_LBL_W, AUD_RLH); AlignTMP(meterLabel, TMPro.TextAlignmentOptions.MidlineLeft); SetTMP(meterLabel, "입력 레벨", 19f, OptText);
        var meterBg = A.Find("MicMeterBg");
        SetRT(meterBg, 0f, AUD_CTRL_X, CtrlY(452f, AUD_SLH), AUD_SW, AUD_SLH);

        // ===== 음성 채팅 (Photon Voice) 설정 =====
        SetupVoiceModeSection(A, -490f);

        // 그룹이 늘어 뷰포트(COL_H)보다 길어졌으므로 내용 높이를 늘려 스크롤되게 한다
        var apRt = _audioPanel.GetComponent<RectTransform>();
        if (apRt != null) apRt.sizeDelta = new Vector2(COL_W, 640f);
        if (meterBg != null)
        {
            var fill = meterBg.Find("MicMeterFill");
            if (fill != null)
            {
                var fr = fill.GetComponent<RectTransform>();
                fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one;
                fr.offsetMin = Vector2.zero; fr.offsetMax = Vector2.zero;
            }
        }
    }

    private void SetTMP(Transform t, string text, float size) => SetTMP(t, text, size, null);
    private void SetTMP(Transform t, string text, float size, Color? color)
    {
        if (t == null) return;
        var tm = t.GetComponent<TMPro.TMP_Text>();
        if (tm == null) return;
        if (_uiFont != null) tm.font = _uiFont;           // 글씨체 통일
        if (text != null) tm.text = text;
        if (size > 0f) { tm.enableAutoSizing = false; tm.fontSize = size; }
        if (color.HasValue) tm.color = color.Value;
    }

    // RectTransform을 콘텐츠 상단 중앙(anchor) 기준으로 배치. pivotX로 좌(0)/중(0.5)/우(1) 정렬.
    private void SetRT(Transform t, float pivotX, float x, float y, float w, float h)
    {
        if (t == null) return;
        var rt = t as RectTransform ?? t.GetComponent<RectTransform>();
        if (rt == null) return;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(pivotX, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
    }

    private void AlignTMP(Transform t, TMPro.TextAlignmentOptions a)
    {
        if (t == null) return;
        var tm = t.GetComponent<TMPro.TMP_Text>();
        if (tm != null) tm.alignment = a;
    }

    // 음향 그룹 밑줄(구분선)을 생성/재사용하여 배치한다.
    private void SetAudioDivider(string name, float y, float w)
    {
        if (_audioPanel == null) return;
        var t = _audioPanel.transform.Find(name);
        GameObject go;
        if (t != null) go = t.gameObject;
        else { go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(_audioPanel.transform, false); }
        var img = go.GetComponent<Image>();
        if (img == null) img = go.AddComponent<Image>();
        img.sprite = null; img.color = OptDivider; img.raycastTarget = false;
        SetRT(go.transform, 0.5f, 0f, y, w, 2f);
    }

    // 배경음/효과음 슬라이더 + 수치, 백그라운드 음소거 버튼: 프리팹에 없으므로 기존 컨트롤을 복제해 만든다 (한 번만).
    private Slider _bgmSlider, _sfxSlider;
    private TMPro.TMP_Text _bgmValueText, _sfxValueText;
    private Button _bgMuteButton;

    private Button _voiceModeAlwaysBtn;
    private Button _voiceModePttBtn;
    private TMPro.TMP_Text _voiceModeHintText;

    private void SetupVoiceModeSection(Transform A, float startY)
    {
        SetAudioDivider("VoiceGroupLine", startY, IN_W);

        var voiceHead = MakeAudioLabel("VoiceGroupHeader", "음성 채팅");
        SetRT(voiceHead.transform, 0f, IN_L, startY - 14f, IN_W, 26f);
        AlignTMP(voiceHead.transform, TMPro.TextAlignmentOptions.MidlineLeft);
        SetTMP(voiceHead.transform, "음성 채팅 (Photon Voice)", 22f, OptHeader);
        SetAudioDivider("VoiceGroupSubLine", startY - 44f, IN_W);

        var modeLabel = MakeAudioLabel("VoiceModeLabel", "송출 방식");
        SetRT(modeLabel.transform, 0f, IN_L, startY - 60f, AUD_LBL_W, AUD_RLH);
        SetTMP(modeLabel.transform, "송출 방식", 19f, OptText);

        const float btnW = 120f, btnH = AUD_RLH, gap = 10f;
        float btnY = startY - 60f;

        if (_voiceModeAlwaysBtn == null)
        {
            _voiceModeAlwaysBtn = MakeTMPButton(A, "상시 송출", AUD_CTRL_X, btnY, btnW, btnH, OptBtnFace, 18f);
            _voiceModeAlwaysBtn.onClick.AddListener(() =>
            {
                VoiceChatSettings.Mode = VoiceChatSettings.TransmitMode.Always;
                RefreshVoiceModeUI();
            });
        }
        SetRT(_voiceModeAlwaysBtn.transform, 0f, AUD_CTRL_X, btnY, btnW, btnH);

        if (_voiceModePttBtn == null)
        {
            _voiceModePttBtn = MakeTMPButton(A, "푸시투톡", AUD_CTRL_X + btnW + gap, btnY, btnW, btnH, OptBtnFace, 18f);
            _voiceModePttBtn.onClick.AddListener(() =>
            {
                VoiceChatSettings.Mode = VoiceChatSettings.TransmitMode.PushToTalk;
                RefreshVoiceModeUI();
            });
        }
        SetRT(_voiceModePttBtn.transform, 0f, AUD_CTRL_X + btnW + gap, btnY, btnW, btnH);

        if (_voiceModeHintText == null)
        {
            _voiceModeHintText = MakeAudioLabel("VoiceModeHint", "");
        }
        SetRT(_voiceModeHintText.transform, 0f, IN_L, startY - 104f, IN_W, 24f);
        SetTMP(_voiceModeHintText.transform, null, 15f, OptHint);

        RefreshVoiceModeUI();
    }

    private void RefreshVoiceModeUI()
    {
        bool isAlways = (VoiceChatSettings.Mode == VoiceChatSettings.TransmitMode.Always);
        if (_voiceModeAlwaysBtn != null)
        {
            var img = _voiceModeAlwaysBtn.GetComponent<Image>();
            if (img != null) img.color = isAlways ? OptBtnSel : OptBtnFace;
        }
        if (_voiceModePttBtn != null)
        {
            var img = _voiceModePttBtn.GetComponent<Image>();
            if (img != null) img.color = isAlways ? OptBtnFace : OptBtnSel;
        }
        if (_voiceModeHintText != null)
        {
            _voiceModeHintText.text = isAlways
                ? "마이크로 말하면 음성을 자동 감지하여 항상 팀원에게 전달됩니다. (기본)"
                : $"'{SkillKeybinds.KeyLabel(VoiceChatSettings.PushToTalkKey)}' 키를 누르고 있는 동안만 마이크가 송출됩니다. (조작 탭에서 변경)";
        }
    }

    private void EnsureBgmControls()
    {
        if (_audioPanel == null || masterVolumeSlider == null) return;

        if (_bgmSlider == null)
            _bgmSlider = CloneVolumeSlider("BgmVolumeSlider",
                BgmManager.Instance != null ? BgmManager.Instance.Volume : BgmManager.DefaultVolume, OnBgmVolumeChanged);
        if (_sfxSlider == null)
            _sfxSlider = CloneVolumeSlider("SfxVolumeSlider", SfxPlayer.Volume, OnSfxVolumeChanged);
        if (_bgmValueText == null) _bgmValueText = CloneValueText("BgmValueText");
        if (_sfxValueText == null) _sfxValueText = CloneValueText("SfxValueText");
        UpdatePercentText(_bgmValueText, _bgmSlider.value);
        UpdatePercentText(_sfxValueText, _sfxSlider.value);

        if (_bgMuteButton == null && micTestButton != null)
        {
            var existing = _audioPanel.transform.Find("BgMuteButton");
            var go = existing != null ? existing.gameObject : Instantiate(micTestButton.gameObject, micTestButton.transform.parent);
            go.name = "BgMuteButton";
            _bgMuteButton = go.GetComponent<Button>();
            for (int i = 0; i < _bgMuteButton.onClick.GetPersistentEventCount(); i++)
                _bgMuteButton.onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
            _bgMuteButton.onClick.RemoveAllListeners();
            _bgMuteButton.onClick.AddListener(() =>
            {
                if (BgmManager.Instance == null) return;
                BgmManager.Instance.MuteInBackground = !BgmManager.Instance.MuteInBackground;
                PlayerPrefs.Save();
                RefreshBgMuteButton();
            });
        }
        RefreshBgMuteButton();
    }

    // 음량 슬라이더 한 행(라벨 · 슬라이더 · 수치) 배치. depth = 라벨 상단까지의 깊이.
    private void LayoutVolumeRow(string labelName, string labelText, Slider slider, TMPro.TMP_Text valueText, float depth)
    {
        var label = MakeAudioLabel(labelName, labelText);
        SetRT(label != null ? label.transform : null, 0f, IN_L, -depth, AUD_LBL_W, AUD_RLH);
        SetTMP(label != null ? label.transform : null, null, 19f, OptText);
        SetRT(slider != null ? slider.transform : null, 0f, AUD_CTRL_X, CtrlY(depth, AUD_SLH), AUD_SW, AUD_SLH);
        var vt = valueText != null ? valueText.transform : null;
        SetRT(vt, 0f, AUD_VAL_X, -depth, AUD_VAL_W, AUD_RLH);
        AlignTMP(vt, TMPro.TextAlignmentOptions.MidlineLeft);
        SetTMP(vt, null, 19f, OptText);
    }

    private Slider CloneVolumeSlider(string name, float value, UnityEngine.Events.UnityAction<float> onChanged)
    {
        var existing = _audioPanel.transform.Find(name);
        var go = existing != null ? existing.gameObject : Instantiate(masterVolumeSlider.gameObject, masterVolumeSlider.transform.parent);
        go.name = name;
        var s = go.GetComponent<Slider>();
        // 복제된 슬라이더에 인스펙터로 연결된 '마스터 음량' 이벤트가 딸려오지 않도록 끈다
        for (int i = 0; i < s.onValueChanged.GetPersistentEventCount(); i++)
            s.onValueChanged.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
        s.onValueChanged.RemoveAllListeners();
        s.minValue = 0f;
        s.maxValue = 1f;
        s.wholeNumbers = false;
        s.SetValueWithoutNotify(value);
        s.onValueChanged.AddListener(onChanged);
        return s;
    }

    private TMPro.TMP_Text CloneValueText(string name)
    {
        var A = _audioPanel.transform;
        var existing = A.Find(name);
        if (existing != null) return existing.GetComponent<TMPro.TMP_Text>();
        var src = A.Find("VolumeValueText");
        if (src == null) return null;
        var go = Instantiate(src.gameObject, src.parent);
        go.name = name;
        return go.GetComponent<TMPro.TMP_Text>();
    }

    public void OnBgmVolumeChanged(float value)
    {
        if (BgmManager.Instance != null) BgmManager.Instance.Volume = value; // PlayerPrefs 저장 포함
        PlayerPrefs.Save();
        UpdatePercentText(_bgmValueText, value);
    }

    public void OnSfxVolumeChanged(float value)
    {
        SfxPlayer.Volume = value; // PlayerPrefs 저장 포함
        PlayerPrefs.Save();
        UpdatePercentText(_sfxValueText, value);
    }

    private static void UpdatePercentText(TMPro.TMP_Text t, float value)
    {
        if (t != null) t.text = $"{Mathf.RoundToInt(value * 100f)}%";
    }

    private void RefreshBgMuteButton()
    {
        if (_bgMuteButton == null) return;
        bool on = BgmManager.Instance != null && BgmManager.Instance.MuteInBackground;
        PixelImage(_bgMuteButton.GetComponent<Image>(), on ? OptBtnSel : OptBtnFace, 3f);
        var tt = _bgMuteButton.GetComponentInChildren<TMPro.TMP_Text>();
        if (tt != null) { tt.color = OptText; tt.text = on ? "음소거 켬" : "음소거 끔"; tt.fontSize = 18f; tt.enableAutoSizing = false; }
    }

    // 프리팹에 없는 음향 라벨(음량/출력 장치/입력 장치)을 그리드 정렬용으로 생성/재사용한다.
    private TMPro.TMP_Text MakeAudioLabel(string objName, string text)
    {
        if (_audioPanel == null) return null;
        var existing = _audioPanel.transform.Find(objName);
        if (existing != null)
        {
            var et = existing.GetComponent<TMPro.TMP_Text>();
            if (et != null && text != null) et.text = text;
            return et;
        }
        var src = _audioPanel.transform.Find("MicSensLabel")?.GetComponent<TMPro.TMP_Text>();
        var go = new GameObject(objName, typeof(RectTransform));
        go.transform.SetParent(_audioPanel.transform, false);
        var t = go.AddComponent<TMPro.TextMeshProUGUI>();
        if (src != null) { t.font = src.font; t.fontSize = src.fontSize; t.fontSharedMaterial = src.fontSharedMaterial; }
        t.text = text;
        t.color = OptText;
        t.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
        t.raycastTarget = false;
        return t;
    }

    // 재설정 대기 중 눌린 키를 잡아 해당 바인딩에 배정 (중복 차단).
    private void CaptureBindKey()
    {
        if (_awaitingBind < 0 || _awaitingBind >= _binds.Count) return;
        var cur = _binds[_awaitingBind];
        foreach (KeyCode kc in System.Enum.GetValues(typeof(KeyCode)))
        {
            if (kc == KeyCode.None || kc == KeyCode.Escape) continue;
            if (kc >= KeyCode.Mouse0 && kc <= KeyCode.Mouse6) continue;
            if (Input.GetKeyDown(kc))
            {
                // 같은 키 → 그대로 확정
                if (cur.get() == kc) { _awaitingBind = -1; RefreshBinds(); return; }
                // 다른 바인딩에서 이미 쓰고 있으면, 그 바인딩을 해제하고 이 키를 새로 적용(키 뺏기)
                int dup = -1;
                for (int j = 0; j < _binds.Count; j++)
                {
                    if (j == _awaitingBind) continue;
                    if (_binds[j].get() == kc) { dup = j; break; }
                }
                string releasedName = dup >= 0 ? _binds[dup].name : null;
                if (dup >= 0) _binds[dup].set(KeyCode.None); // 기존에 쓰던 키 초기화
                cur.set(kc);
                _awaitingBind = -1;
                RefreshBinds();
                if (releasedName != null && _rebindHintText != null)
                {
                    _rebindHintText.text = $"'{releasedName}' 키를 해제하고 적용했습니다";
                    _rebindHintText.color = OptGoldHi;
                }
                return;
            }
        }
    }

    #endregion
}
