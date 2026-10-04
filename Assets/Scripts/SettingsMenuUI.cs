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

        // 스킬 키 재설정 대기 중이면: ESC=취소, 그 외 키=해당 슬롯에 배정 (메뉴 토글보다 우선)
        if (_awaitingRebindSlot > 0)
        {
            if (escPressed)
            {
                _awaitingRebindSlot = 0;
                RefreshRebindLabels();
            }
            else
            {
                CaptureRebindKey();
            }
            return;
        }

        if (escPressed)
        {
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

    public void OpenMenu()
    {
        _isOpen = true;

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
        _awaitingRebindSlot = 0;

        bool inGame = IsGameStage();
        if (_respawnRow != null) _respawnRow.SetActive(inGame); // 다시하기: 인게임에서만
        ConfigureExitButton(inGame);                            // 나가기(인게임) / 닫기(로비)
        SelectTab(_currentTab);
    }

    // 하단 버튼: 인게임이면 '나가기'(로비로), 로비면 '닫기'(창만 닫기)
    private void ConfigureExitButton(bool inGame)
    {
        if (_exitBtn2 == null) return;
        _exitBtn2.onClick.RemoveAllListeners();
        if (inGame)
        {
            StyleFlatButton(_exitBtn2, OptExit, "나가기", 24);
            _exitBtn2.onClick.AddListener(OnExitButtonClicked);
        }
        else
        {
            StyleFlatButton(_exitBtn2, OptBtnFace, "닫기", 24);
            _exitBtn2.onClick.AddListener(CloseMenu);
        }
        // 인게임: 다시하기(좌) 옆 우측 배치 / 로비: 다시하기 숨김 → 단독 중앙 정렬
        var ert = _exitBtn2.GetComponent<RectTransform>();
        if (ert != null) ert.anchoredPosition = new Vector2(inGame ? BTN_HALF : 0f, BOTTOM_Y);
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
        outputDeviceDropdown.value = selectedIndex;
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

    #region 게임플레이 설정 (스킬 단축키 재설정 + 다시하기)

    private bool _gameplayUIBuilt;
    private int _awaitingRebindSlot = 0; // 0=대기 아님, 1~3=해당 슬롯 키 입력 대기
    private readonly Text[] _rebindKeyLabels = new Text[SkillKeybinds.MaxSlot + 1];
    private Text _rebindHintText;
    private GameObject _respawnRow;       // 하단 다시하기 버튼 (인게임에서만 표시)

    // 탭 방식 옵션 UI
    private GameObject _controlsPanel;    // '조작' 탭 내용
    private GameObject _audioPanel;       // '음향' 탭 내용 (= 기존 SettingsPanel 재사용)
    private Button _tabBtnControls, _tabBtnAudio;
    private Button _exitBtn2;             // 하단 나가기/닫기 버튼 (상황에 따라 라벨·동작 변경)
    private GameObject _gearButton;       // 로비용 톱니바퀴 버튼
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

    // === 공통 레이아웃 그리드 (모든 요소를 하나의 그리드에 정렬) ===
    // 창을 약 12% 축소하고, 탭↔콘텐츠 간격을 줄여 하나의 그룹처럼 보이게 함.
    private const float FRAME_W = 852f;   // 옵션창(바깥 외곽선) 너비  (968 → -12%)
    private const float FRAME_H = 588f;   // 옵션창(바깥 외곽선) 높이  (668 → -12%)
    private const float COL_X   = 126f;   // 오른쪽 콘텐츠 영역 중심 X (프레임 기준)
    private const float COL_Y   = 5f;     // 콘텐츠 수직 중심 Y
    private const float COL_W   = 500f;   // 콘텐츠 영역 너비
    private const float COL_H   = 420f;   // 콘텐츠 영역 높이
    private const float IN_L    = -228f;  // 콘텐츠 내부 좌측 X (패널 중심 기준)
    private const float IN_R    = 228f;    // 콘텐츠 내부 우측 X
    private const float IN_W    = 456f;    // 콘텐츠 내부 너비
    private const float TAB_X   = -290f;   // 탭 열 중심 X (콘텐츠에 더 가깝게)
    private const float TAB_W   = 130f;    // 탭 너비
    private const float TAB_H   = 48f;     // 탭 높이
    private const float TAB_GAP = 12f;     // 탭 간 간격
    private const float BOTTOM_Y = -237f;  // 하단 버튼 중심 Y
    private const float BTN_W   = 220f;    // 하단 버튼 너비
    private const float BTN_H   = 52f;     // 하단 버튼 높이
    private const float BTN_HALF = 120f;   // 하단 버튼 좌우 배치 오프셋(=너비/2+10)

    // 그래픽에 어두운 픽셀 외곽선(#2F1C16)을 입힌다. 검정 대신 사용.
    private void AddPixelOutline(Graphic g, float dist)
    {
        if (g == null) return;
        var o = g.GetComponent<UnityEngine.UI.Outline>();
        if (o == null) o = g.gameObject.AddComponent<UnityEngine.UI.Outline>();
        o.effectColor = OptOutline;
        o.effectDistance = new Vector2(dist, -dist);
        o.useGraphicAlpha = false;
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
        var titleT = AddFixedLabel(F, "옵션", 32, OptGold, new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(160f, 42f));
        AddPixelOutline(titleT, 2f);
        // 제목 양옆 구분선: 글자 뒤를 가로지르지 않도록 좌/우로 끊어서 배치 (──── 옵션 ────)
        MakeTitleDivider(F, -229f, 262f, -41f); // 왼쪽 세그먼트
        MakeTitleDivider(F,  229f, 262f, -41f); // 오른쪽 세그먼트

        // 좌측 탭 버튼: 동일 X·너비·높이, 콘텐츠 수직 중심(COL_Y)에 맞춰 세로 중앙 정렬
        float tabTopY = COL_Y + (TAB_H + TAB_GAP) / 2f;
        float tabBotY = COL_Y - (TAB_H + TAB_GAP) / 2f;
        _tabBtnControls = MakeTabButton(F, "조작", tabTopY);
        _tabBtnControls.onClick.AddListener(() => SelectTab(0));
        _tabBtnAudio = MakeTabButton(F, "음향", tabBotY);
        _tabBtnAudio.onClick.AddListener(() => SelectTab(1));

        // 음향 탭 내용 = 기존 SettingsPanel: 콘텐츠 영역(COL)과 동일한 위치·크기로 정렬
        if (_audioPanel != null)
        {
            var apRt = _audioPanel.GetComponent<RectTransform>();
            apRt.anchorMin = apRt.anchorMax = new Vector2(0.5f, 0.5f);
            apRt.pivot = new Vector2(0.5f, 0.5f);
            apRt.anchoredPosition = new Vector2(COL_X, COL_Y);
            apRt.sizeDelta = new Vector2(COL_W, COL_H);
            var apImg = _audioPanel.GetComponent<Image>();
            if (apImg != null) apImg.enabled = false; // 통합 프레임 배경이 대신 보이도록
            foreach (var tmp in _audioPanel.GetComponentsInChildren<TMPro.TMP_Text>(true))
                if (tmp.text != null && tmp.text.Contains("Settings")) { tmp.gameObject.SetActive(false); break; }

            StyleAudioControls();  // 색/스타일 픽셀 팔레트 통일
            LayoutAudioControls(); // 위치·간격을 공통 그리드로 정렬
        }

        // 조작 탭 내용 패널: 음향 패널과 완전히 동일한 위치·크기
        _controlsPanel = new GameObject("ControlsPanel", typeof(RectTransform), typeof(Image));
        _controlsPanel.transform.SetParent(F, false);
        var cRt = _controlsPanel.GetComponent<RectTransform>();
        cRt.anchorMin = cRt.anchorMax = new Vector2(0.5f, 0.5f);
        cRt.pivot = new Vector2(0.5f, 0.5f);
        cRt.anchoredPosition = new Vector2(COL_X, COL_Y);
        cRt.sizeDelta = new Vector2(COL_W, COL_H);
        var cImg = _controlsPanel.GetComponent<Image>();
        cImg.color = new Color(0f, 0f, 0f, 0f); cImg.raycastTarget = false;
        Transform CP = _controlsPanel.transform;

        // "스킬 단축키" 제목 — 콘텐츠 영역 정중앙 (콘텐츠를 세로 중앙에 모아 빈 공간 최소화)
        AddGridLabel(CP, "스킬 단축키", 22, OptHeader, 0.5f, 0f, -62f, IN_W, 32f, TextAnchor.MiddleCenter);

        // 스킬 1~3: 라벨(좌측 동일 X) + 키캡(우측 동일 X·동일 크기) + 균일 간격 + 구분선
        const float rowH = 44f, pitch = 52f, firstRowY = -108f;
        for (int slot = 1; slot <= SkillKeybinds.MaxSlot; slot++)
        {
            int s = slot;
            float ry = firstRowY - (s - 1) * pitch;
            var cap = AddKeybindRow(CP, s, ry, rowH, s < SkillKeybinds.MaxSlot);
            _rebindKeyLabels[s] = cap.GetComponentInChildren<Text>();
            cap.onClick.AddListener(() => { _awaitingRebindSlot = s; RefreshRebindLabels(); });
        }

        // 기본값 버튼: 축소 + 중앙 정렬
        var resetBtn = MakeGridButton(CP, "기본값으로", 0f, -276f, 300f, 42f, OptBtnFace, 20);
        resetBtn.onClick.AddListener(() => { SkillKeybinds.ResetToDefaults(); _awaitingRebindSlot = 0; RefreshRebindLabels(); });

        _rebindHintText = AddGridLabel(CP, "키캡을 누른 뒤 새 키를 입력하세요", 15, OptHint, 0.5f, 0f, -332f, IN_W, 26f, TextAnchor.MiddleCenter);

        // 하단 액션 바: 다시하기(인게임 전용) + 나가기/닫기 (두 탭에서 동일 위치·크기)
        var respawnBtn = MakeBottomButton(F, "다시하기", -BTN_HALF, OptRespawn);
        respawnBtn.onClick.AddListener(() => { if (PlayerMovement.RequestLocalRespawn()) CloseMenu(); });
        _respawnRow = respawnBtn.gameObject;
        _exitBtn2 = MakeBottomButton(F, "나가기", BTN_HALF, OptExit);

        _gameplayUIBuilt = true;
        SelectTab(0);
    }

    private void SelectTab(int tab)
    {
        _currentTab = tab;
        _awaitingRebindSlot = 0;
        if (_controlsPanel != null) _controlsPanel.SetActive(tab == 0);
        if (_audioPanel != null) _audioPanel.SetActive(tab == 1);
        StyleTab(_tabBtnControls, tab == 0, "조작");
        StyleTab(_tabBtnAudio, tab == 1, "음향");
        RefreshRebindLabels();
    }

    // 선택 탭은 틸 배경 + 골드 하이라이트 텍스트 + 왼쪽 골드 픽셀 마커.
    private void StyleTab(Button btn, bool selected, string label)
    {
        if (btn == null) return;
        StyleFlatButton(btn, selected ? OptBtnSel : OptBtnFace, label, 24);
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
        StyleFlatButton(btn, OptBtnFace, label, 20);

        // 선택 표시용 골드 픽셀 마커(왼쪽 세로 바) — 평소엔 숨김
        var marker = new GameObject("SelMarker", typeof(RectTransform), typeof(Image));
        marker.transform.SetParent(go.transform, false);
        var mrt = marker.GetComponent<RectTransform>();
        mrt.anchorMin = new Vector2(0f, 0.5f); mrt.anchorMax = new Vector2(0f, 0.5f);
        mrt.pivot = new Vector2(0f, 0.5f);
        mrt.anchoredPosition = new Vector2(7f, 0f);
        mrt.sizeDelta = new Vector2(8f, 26f);
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
        StyleFlatButton(btn, face, label, 24);
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

    // "스킬 N" 라벨 + 오른쪽 픽셀 키캡(클릭 시 리바인드) 한 줄. 반환 = 키캡 버튼.
    // 한 줄(절대 y 기준): "스킬 N" 라벨(좌측 동일 X) + 픽셀 키캡(우측 동일 X·동일 크기) + (옵션) 전체폭 구분선
    private Button AddKeybindRow(Transform parent, int slot, float rowY, float rowH, bool divider)
    {
        const float capW = 160f, capH = 38f;

        // 왼쪽: 스킬 이름 — 콘텐츠 좌측(IN_L)에서 시작, 모든 행 동일 X
        AddGridLabel(parent, $"스킬 {slot}", 20, OptText, 0f, IN_L, rowY, 150f, rowH, TextAnchor.MiddleLeft);

        // 오른쪽: 픽셀 키캡 버튼 — 콘텐츠 우측(IN_R)에 맞춰 정렬, 행 안에서 수직 중앙
        var cap = new GameObject("Keycap", typeof(RectTransform), typeof(Image), typeof(Button));
        cap.transform.SetParent(parent, false);
        var crt = cap.GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 1f);
        crt.pivot = new Vector2(1f, 1f);
        crt.anchoredPosition = new Vector2(IN_R, rowY - (rowH - capH) / 2f);
        crt.sizeDelta = new Vector2(capW, capH);
        var capImg = cap.GetComponent<Image>();
        capImg.sprite = null; capImg.type = Image.Type.Simple; capImg.color = OptKeycapBg;
        AddPixelOutline(capImg, 3f);

        var edge = new GameObject("Edge", typeof(RectTransform), typeof(Image));
        edge.transform.SetParent(cap.transform, false);
        var ert = edge.GetComponent<RectTransform>();
        ert.anchorMin = Vector2.zero; ert.anchorMax = Vector2.one;
        ert.offsetMin = new Vector2(2f, 2f); ert.offsetMax = new Vector2(-2f, -2f);
        var ei = edge.GetComponent<Image>();
        ei.color = OptKeycapBg; ei.raycastTarget = false;
        var eiOut = edge.AddComponent<UnityEngine.UI.Outline>();
        eiOut.effectColor = OptKeycapBd; eiOut.effectDistance = new Vector2(1f, -1f); eiOut.useGraphicAlpha = false;

        var key = new GameObject("Key", typeof(RectTransform), typeof(Text));
        key.transform.SetParent(cap.transform, false);
        var krt = key.GetComponent<RectTransform>();
        krt.anchorMin = Vector2.zero; krt.anchorMax = Vector2.one;
        krt.offsetMin = Vector2.zero; krt.offsetMax = Vector2.zero;
        var kt = key.GetComponent<Text>();
        kt.font = LobbyUITheme.PlainFont; kt.fontSize = 20;
        kt.alignment = TextAnchor.MiddleCenter; kt.color = OptText;
        kt.raycastTarget = false;
        kt.text = SkillKeybinds.GetLabel(slot);

        var btn = cap.GetComponent<Button>();
        btn.targetGraphic = capImg;

        // 구분선: 모든 항목에서 동일 X·동일 폭(콘텐츠 전체 폭), 행 사이 정중앙
        if (divider)
        {
            var dv = new GameObject("Divider", typeof(RectTransform), typeof(Image));
            dv.transform.SetParent(parent, false);
            var drt = dv.GetComponent<RectTransform>();
            drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 1f);
            drt.pivot = new Vector2(0.5f, 1f);
            drt.anchoredPosition = new Vector2(0f, rowY - rowH - 4f);
            drt.sizeDelta = new Vector2(IN_W, 2f);
            var dvi = dv.GetComponent<Image>();
            dvi.color = OptDivider; dvi.raycastTarget = false;
        }

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

    private void RefreshRebindLabels()
    {
        for (int slot = 1; slot <= SkillKeybinds.MaxSlot; slot++)
        {
            if (_rebindKeyLabels[slot] == null) continue;
            _rebindKeyLabels[slot].text = (_awaitingRebindSlot == slot)
                ? "[ ... ]"
                : SkillKeybinds.GetLabel(slot);
            _rebindKeyLabels[slot].color = (_awaitingRebindSlot == slot) ? OptGoldHi : OptText;
        }
        if (_rebindHintText != null)
            _rebindHintText.text = _awaitingRebindSlot > 0
                ? "새 키를 누르세요 (ESC 취소)"
                : "키캡을 누른 뒤 새 키를 입력하세요";
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

        StyleSlider(masterVolumeSlider);
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

        // 테스트 버튼 (TMP 텍스트라 StyleFlatButton 대신 직접 처리)
        if (micTestButton != null)
        {
            PixelImage(micTestButton.GetComponent<Image>(), OptBtnFace, 3f);
            var tt = micTestButton.GetComponentInChildren<TMPro.TMP_Text>();
            if (tt != null) tt.color = OptText;
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
        var img = d.GetComponent<Image>();
        if (img != null) { img.sprite = null; img.type = Image.Type.Simple; img.color = OptDropBg; AddPixelOutline(img, 2f); }
        if (d.captionText != null) d.captionText.color = OptText;
        var arrow = d.transform.Find("Arrow");
        if (arrow != null) { var at = arrow.GetComponent<TMPro.TMP_Text>(); if (at != null) at.color = OptGold; }
        var tmpl = d.template != null ? d.template.GetComponent<Image>() : null;
        if (tmpl != null) { tmpl.sprite = null; tmpl.color = OptDropBg; }
        if (d.itemText != null) d.itemText.color = OptText;
    }

    // === 음향 탭 컨트롤 위치·간격을 공통 그리드로 정렬 ===
    // 그리드: 라벨열[IN_L..] / 컨트롤열[AUD_CTRL_X, 너비 AUD_CW] / 값열[AUD_VAL_X]
    private const float AUD_LBL_W  = 118f;
    private const float AUD_CTRL_X = -96f;    // 모든 슬라이더/미터 시작 X (동일)
    private const float AUD_CW     = 246f;    // 슬라이더/미터 너비 (동일)
    private const float AUD_SLH    = 20f;     // 슬라이더/미터 높이 (동일)
    private const float AUD_VAL_X  = 164f;    // 수치(100%/79%) 및 테스트 버튼 시작 X (동일)
    private const float AUD_VAL_W  = 64f;
    private const float AUD_RLH    = 32f;     // 음향 행 라벨 높이 (슬라이더 수직 중앙용 기준)
    private TMPro.TMP_Text _volumeLabel;

    private void LayoutAudioControls()
    {
        if (_audioPanel == null) return;
        Transform A = _audioPanel.transform;
        float slOff = (AUD_RLH - AUD_SLH) / 2f; // 라벨 기준 슬라이더 수직 중앙 보정

        // 섹션 헤더(체크박스 라벨): 동일 X(IN_L), 좌측 정렬 (세로 간격 압축)
        SetRT(A.Find("OutputLabel"), 0f, IN_L, -61f, IN_W, 28f); AlignTMP(A.Find("OutputLabel"), TMPro.TextAlignmentOptions.MidlineLeft);
        SetRT(A.Find("InputLabel"),  0f, IN_L, -201f, IN_W, 28f); AlignTMP(A.Find("InputLabel"), TMPro.TextAlignmentOptions.MidlineLeft);

        // 드롭다운: 동일 X·동일 폭(콘텐츠 전체 폭)
        SetRT(A.Find("OutputDeviceDropdown"), 0.5f, 0f, -97f, IN_W, 40f);
        SetRT(A.Find("MicDropdown"),          0.5f, 0f, -237f, IN_W, 40f);

        // 섹션 구분선: 드롭다운 좌우 범위와 정확히 일치
        SetRT(A.Find("Separator"), 0.5f, 0f, -189f, IN_W, 2f);

        // 음량 행: (음량 라벨) + 슬라이더(AUD_CTRL_X) + 값(AUD_VAL_X)
        EnsureVolumeLabel();
        SetRT(_volumeLabel != null ? _volumeLabel.transform : null, 0f, IN_L, -147f, AUD_LBL_W, AUD_RLH);
        SetRT(masterVolumeSlider != null ? masterVolumeSlider.transform : null, 0f, AUD_CTRL_X, -147f - slOff, AUD_CW, AUD_SLH);
        SetRT(A.Find("VolumeValueText"), 0f, AUD_VAL_X, -147f, AUD_VAL_W, AUD_RLH); AlignTMP(A.Find("VolumeValueText"), TMPro.TextAlignmentOptions.MidlineLeft);

        // 마이크 감도 행
        SetRT(A.Find("MicSensLabel"), 0f, IN_L, -287f, AUD_LBL_W, AUD_RLH); AlignTMP(A.Find("MicSensLabel"), TMPro.TextAlignmentOptions.MidlineLeft);
        SetRT(micSensitivitySlider != null ? micSensitivitySlider.transform : null, 0f, AUD_CTRL_X, -287f - slOff, AUD_CW, AUD_SLH);
        SetRT(A.Find("MicSensValueText"), 0f, AUD_VAL_X, -287f, AUD_VAL_W, AUD_RLH); AlignTMP(A.Find("MicSensValueText"), TMPro.TextAlignmentOptions.MidlineLeft);

        // 입력 레벨 행: 라벨 + 미터(동일 X·너비) + 테스트 버튼(값열과 동일 X)
        SetRT(A.Find("MeterLabel"), 0f, IN_L, -327f, AUD_LBL_W, AUD_RLH); AlignTMP(A.Find("MeterLabel"), TMPro.TextAlignmentOptions.MidlineLeft);
        var meterBg = A.Find("MicMeterBg");
        SetRT(meterBg, 0f, AUD_CTRL_X, -327f - slOff, AUD_CW, AUD_SLH);
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
        if (micTestButton != null) SetRT(micTestButton.transform, 0f, AUD_VAL_X, -327f - (AUD_RLH - 28f) / 2f, AUD_VAL_W, 28f);
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

    // 음량 행 라벨("음량:")이 프리팹에 없으므로 그리드 정렬용으로 한 번 생성한다.
    private void EnsureVolumeLabel()
    {
        if (_volumeLabel != null || _audioPanel == null) return;
        var src = _audioPanel.transform.Find("MicSensLabel")?.GetComponent<TMPro.TMP_Text>();
        var go = new GameObject("VolumeLabel", typeof(RectTransform));
        go.transform.SetParent(_audioPanel.transform, false);
        var t = go.AddComponent<TMPro.TextMeshProUGUI>();
        if (src != null) { t.font = src.font; t.fontSize = src.fontSize; t.fontSharedMaterial = src.fontSharedMaterial; }
        t.text = "음량:";
        t.color = OptText;
        t.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
        t.raycastTarget = false;
        _volumeLabel = t;
    }

    // 리바인딩 대기 중 눌린 키를 잡아 해당 슬롯에 배정한다.
    private void CaptureRebindKey()
    {
        foreach (KeyCode kc in System.Enum.GetValues(typeof(KeyCode)))
        {
            if (kc == KeyCode.None || kc == KeyCode.Escape) continue;
            if (kc >= KeyCode.Mouse0 && kc <= KeyCode.Mouse6) continue;
            if (Input.GetKeyDown(kc))
            {
                SkillKeybinds.SetSlotKey(_awaitingRebindSlot, kc);
                _awaitingRebindSlot = 0;
                RefreshRebindLabels();
                return;
            }
        }
    }

    #endregion
}
