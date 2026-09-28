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

    // === 음향 설정 패널과 통일한 다크 톤 팔레트 ===
    private static readonly Color OptPanelBg   = new Color(0.18f, 0.18f, 0.22f, 0.98f); // 기존 SettingsPanel 색
    private static readonly Color OptBorder    = new Color(0.42f, 0.45f, 0.54f, 1f);
    private static readonly Color OptBtnFace   = new Color(0.26f, 0.28f, 0.34f, 1f);
    private static readonly Color OptBtnSel    = new Color(0.30f, 0.55f, 0.66f, 1f);   // 선택 탭 강조(틸)
    private static readonly Color OptText      = new Color(0.92f, 0.94f, 0.98f, 1f);
    private static readonly Color OptHeader    = new Color(0.80f, 0.86f, 0.96f, 1f);
    private static readonly Color OptHint      = new Color(0.62f, 0.66f, 0.74f, 1f);
    private static readonly Color OptRespawn   = new Color(0.26f, 0.46f, 0.40f, 1f);   // 다시하기(뮤트 그린)
    private static readonly Color OptExit      = new Color(0.50f, 0.28f, 0.30f, 1f);   // 나가기(뮤트 레드)

    // 평면 다크 버튼 스타일 (우드 대신 음향 패널 톤에 맞춤)
    private void StyleFlatButton(Button btn, Color face, string label, int fontSize)
    {
        if (btn == null) return;
        var img = btn.GetComponent<Image>();
        if (img != null) { img.sprite = null; img.color = face; }

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

        // === 통합 프레임 (테두리 + 다크 배경, 음향 패널 톤) ===
        var border = new GameObject("OptionsFrameBorder", typeof(RectTransform), typeof(Image));
        border.transform.SetParent(parent, false);
        var bRt = border.GetComponent<RectTransform>();
        bRt.anchorMin = bRt.anchorMax = new Vector2(0.5f, 0.5f);
        bRt.pivot = new Vector2(0.5f, 0.5f);
        bRt.anchoredPosition = Vector2.zero;
        bRt.sizeDelta = new Vector2(968f, 668f);
        border.GetComponent<Image>().color = OptBorder;
        border.transform.SetAsFirstSibling(); // 오디오 컨트롤/기타 요소보다 뒤

        var frame = new GameObject("OptionsFrame", typeof(RectTransform), typeof(Image));
        frame.transform.SetParent(border.transform, false);
        var fRt = frame.GetComponent<RectTransform>();
        fRt.anchorMin = Vector2.zero; fRt.anchorMax = Vector2.one;
        fRt.offsetMin = new Vector2(4f, 4f); fRt.offsetMax = new Vector2(-4f, -4f);
        frame.GetComponent<Image>().color = OptPanelBg;
        Transform F = frame.transform;

        // 타이틀
        AddFixedLabel(F, "옵션", 34, OptHeader, new Vector2(0.5f, 1f), new Vector2(0f, -26f), new Vector2(500f, 50f));

        // 좌측 탭 버튼
        _tabBtnControls = MakeTabButton(F, "조작", 32f);
        _tabBtnControls.onClick.AddListener(() => SelectTab(0));
        _tabBtnAudio = MakeTabButton(F, "음향", -30f);
        _tabBtnAudio.onClick.AddListener(() => SelectTab(1));

        // 음향 탭 내용 = 기존 SettingsPanel: 프레임 안으로 위치 이동 + 자체 배경/제목 숨김
        if (_audioPanel != null)
        {
            var apRt = _audioPanel.GetComponent<RectTransform>();
            apRt.anchoredPosition = new Vector2(110f, -4f);
            var apImg = _audioPanel.GetComponent<Image>();
            if (apImg != null) apImg.enabled = false; // 통합 프레임 배경이 대신 보이도록
            foreach (var tmp in _audioPanel.GetComponentsInChildren<TMPro.TMP_Text>(true))
                if (tmp.text != null && tmp.text.Contains("Settings")) { tmp.gameObject.SetActive(false); break; }
        }

        // 조작 탭 내용 패널 (음향 패널과 같은 영역, 배경 투명 → 프레임 배경 사용)
        _controlsPanel = new GameObject("ControlsPanel", typeof(RectTransform), typeof(Image));
        _controlsPanel.transform.SetParent(F, false);
        var cRt = _controlsPanel.GetComponent<RectTransform>();
        cRt.anchorMin = cRt.anchorMax = new Vector2(0.5f, 0.5f);
        cRt.pivot = new Vector2(0.5f, 0.5f);
        cRt.anchoredPosition = new Vector2(110f, 0f);
        cRt.sizeDelta = new Vector2(560f, 340f);
        _controlsPanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

        float y = -8f;
        AddLabelRow(_controlsPanel.transform, "스킬 단축키", 22, OptHeader, ref y, 36f);
        for (int slot = 1; slot <= SkillKeybinds.MaxSlot; slot++)
        {
            int s = slot;
            var btn = AddButtonRow(_controlsPanel.transform, $"스킬 {s}:  {SkillKeybinds.GetLabel(s)}", ref y, OptBtnFace, 54f);
            _rebindKeyLabels[s] = btn.GetComponentInChildren<Text>();
            btn.onClick.AddListener(() => { _awaitingRebindSlot = s; RefreshRebindLabels(); });
        }
        var resetBtn = AddButtonRow(_controlsPanel.transform, "기본값으로", ref y, OptBtnFace, 46f);
        resetBtn.onClick.AddListener(() => { SkillKeybinds.ResetToDefaults(); _awaitingRebindSlot = 0; RefreshRebindLabels(); });
        _rebindHintText = AddLabelRow(_controlsPanel.transform, "버튼을 누른 뒤 새 키를 입력하세요", 15, OptHint, ref y, 40f);

        // 하단 액션 바: 다시하기(인게임 전용) + 나가기/닫기
        var respawnBtn = MakeBottomButton(F, "다시하기", -140f, OptRespawn);
        respawnBtn.onClick.AddListener(() => { if (PlayerMovement.RequestLocalRespawn()) CloseMenu(); });
        _respawnRow = respawnBtn.gameObject;
        _exitBtn2 = MakeBottomButton(F, "나가기", 140f, OptExit);

        _gameplayUIBuilt = true;
        SelectTab(0);
    }

    private void SelectTab(int tab)
    {
        _currentTab = tab;
        _awaitingRebindSlot = 0;
        if (_controlsPanel != null) _controlsPanel.SetActive(tab == 0);
        if (_audioPanel != null) _audioPanel.SetActive(tab == 1);
        if (_tabBtnControls != null)
            StyleFlatButton(_tabBtnControls, tab == 0 ? OptBtnSel : OptBtnFace, "조작", 24);
        if (_tabBtnAudio != null)
            StyleFlatButton(_tabBtnAudio, tab == 1 ? OptBtnSel : OptBtnFace, "음향", 24);
        RefreshRebindLabels();
    }

    private Button MakeTabButton(Transform parent, string label, float y)
    {
        var go = new GameObject("Tab_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(-360f, y);
        rt.sizeDelta = new Vector2(140f, 50f);
        var btn = go.GetComponent<Button>();
        StyleFlatButton(btn, OptBtnFace, label, 20);
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
        rt.anchoredPosition = new Vector2(x, -292f);
        rt.sizeDelta = new Vector2(240f, 60f);
        var btn = go.GetComponent<Button>();
        StyleFlatButton(btn, face, label, 24);
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

        var img = go.GetComponent<Image>();
        img.sprite = GetGearSprite();
        img.preserveAspect = true;
        img.color = LobbyUITheme.Gold; // 트로피와 같은 골드 톤 아이콘

        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        var cb = btn.colors;
        cb.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
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

    private void RefreshRebindLabels()
    {
        for (int slot = 1; slot <= SkillKeybinds.MaxSlot; slot++)
        {
            if (_rebindKeyLabels[slot] == null) continue;
            _rebindKeyLabels[slot].text = (_awaitingRebindSlot == slot)
                ? $"스킬 {slot}:  [ 키 입력... ]"
                : $"스킬 {slot}:  {SkillKeybinds.GetLabel(slot)}";
        }
        if (_rebindHintText != null)
            _rebindHintText.text = _awaitingRebindSlot > 0
                ? "새 키를 누르세요 (ESC 취소)"
                : "버튼을 누른 뒤 새 키를 입력하세요";
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
