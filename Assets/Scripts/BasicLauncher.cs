using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Fusion.Addons.Physics;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class BasicLauncher : MonoBehaviour, INetworkRunnerCallbacks
{
    public static BasicLauncher Instance { get; private set; }

    // 한 방의 최대 인원. 이 게임은 2인 협동이라 2명 초과 입장을 막는다.
    private const int MAX_PLAYERS = 2;

    private NetworkRunner _runner;
    private string _roomCode = ""; // 사용자가 입력할 방 코드
    private float _roomCodeCopiedUntil = 0f;
    private bool _isStarting = false;
    private bool _isReturningToLobby = false;
    private static bool _isVoluntaryExit = false;
    private string _playerName = "";
    [SerializeField] private bool _useIMGUIMenu = false; // 새 TitleScreen 도입으로 구 IMGUI 로비 메뉴는 기본 비활성화

    // 연결 끊김 안내 팝업 UI
    private GameObject _disconnectModalRoot;
    private Text _disconnectTitleText;
    private Text _disconnectBodyText;
    private GameObject _keepWaitBtn;
    private GameObject _returnLobbyBtn;
    private Font _pixelFont;
    private Font _plainFont;

    // 연결 상태 이벤트
    public event Action OnConnectionStarted;
    public event Action<string> OnConnectionFailed;
    public event Action OnConnectionSuccess;

    public bool IsStarting => _isStarting;
    public NetworkRunner Runner => _runner;
    public static bool IsVoluntaryExit => _isVoluntaryExit;
    public bool IsDisconnectModalOpen => _disconnectModalRoot != null && _disconnectModalRoot.activeSelf;

    public static void MarkVoluntaryExit()
    {
        _isVoluntaryExit = true;
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

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        _playerName = PlayerPrefs.GetString("PlayerName", "Player" + UnityEngine.Random.Range(100, 999));
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!IsDisconnectModalOpen)
        {
            Time.timeScale = 1f;
            SettingsMenuUI.GameplayInputSuppressed = false;
        }

        if (scene.name == "Lobby")
        {
            _isVoluntaryExit = false;
            _isReturningToLobby = false;
        }
    }

    /// <summary>
    /// 방 참가가 허용되는 씬인지 확인합니다.
    /// 로비 또는 캐릭터 선택(CharacterSelect) 씬까지만 참가를 허용하며,
    /// 세이브 파일 선택(savefile) 이후(컷씬, 튜토리얼, 본 스테이지 등)에는 방 참가를 차단합니다.
    /// </summary>
    public static bool CanJoinInScene(string sceneName)
    {
        return sceneName == "Lobby" || sceneName == "CharacterSelect";
    }

    /// <summary>
    /// 네트워크 러너가 실행 중이면 호스트 권한으로 네트워크 씬을 로드하고,
    /// 로컬/솔로 상태이면 SceneManager.LoadScene으로 씬을 로드하는 공용 헬퍼입니다.
    /// </summary>
    public static void LoadNetworkOrLocalScene(string sceneName)
    {
        var runner = Instance != null && Instance._runner != null
            ? Instance._runner
            : FindFirstObjectByType<NetworkRunner>();

        if (runner != null && runner.IsRunning)
        {
            if (runner.IsServer)
            {
                int sceneIndex = SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{sceneName}.unity");
                if (sceneIndex >= 0)
                {
                    runner.LoadScene(SceneRef.FromIndex(sceneIndex));
                }
                else
                {
                    runner.LoadScene(sceneName, LoadSceneMode.Single);
                }
            }
            else
            {
                Debug.LogWarning($"[BasicLauncher] 클라이언트는 직접 씬({sceneName})을 전환할 수 없습니다.");
            }
        }
        else
        {
            SceneManager.LoadScene(sceneName);
        }
    }

    public string RoomCode => _roomCode;

    private static readonly string[] InGameStageNames = { "TutorialStage", "Main Stage1", "Main Stage2", "Main Stage3", "TestStage", "GameStage" };

    public static bool IsInGameStage()
    {
        string currentScene = SceneManager.GetActiveScene().name;
        foreach (var name in InGameStageNames)
        {
            if (currentScene == name) return true;
        }
        return false;
    }

    private GUIStyle _imguiLabelStyle;
    private GUIStyle _imguiRoomCodeStyle;

    private void EnsureIMGUIStyles()
    {
        if (_imguiLabelStyle == null)
        {
            _imguiLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };
            _imguiLabelStyle.normal.textColor = Color.white;
        }

        if (_imguiRoomCodeStyle == null)
        {
            _imguiRoomCodeStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
        }
    }

    private void OnGUI()
    {
        // 랭킹 보드나 ESC 설정 메뉴가 열려있으면 레거시 GUI를 일체 그리지 않음
        if (RankingManager.Instance != null && RankingManager.Instance.IsBoardOpen) return;
        if (SettingsMenuUI.Instance != null && SettingsMenuUI.Instance.IsOpen) return;

        // 아직 룸에 접속하지 않은 상태 (로비 화면) - _useIMGUIMenu가 true일 때만 레거시 IMGUI 표시
        if (_useIMGUIMenu && (_runner == null || !_runner.IsRunning))
        {
            EnsureIMGUIStyles();
            float buttonWidth = 260f;
            float buttonHeight = 44f;
            float spacing = 12f;
            float centerX = (Screen.width - buttonWidth) / 2f;
            
            // 타이틀 "보이스 히어로" 아래쪽의 패널 안으로 들어가도록 Y축 위치를 아래로 이동
            float startY = Screen.height * 0.38f;

            // 1. 닉네임 입력창
            GUI.Label(new Rect(centerX, startY, buttonWidth, 20), "닉네임 (Nickname):", _imguiLabelStyle);
            _playerName = GUI.TextField(new Rect(centerX, startY + 22, buttonWidth, 32), _playerName);
            PlayerPrefs.SetString("PlayerName", _playerName);

            // 2. 방 코드 입력창
            GUI.Label(new Rect(centerX, startY + 62, buttonWidth, 20), "방 코드 입력 (Join 시 필요):", _imguiLabelStyle);
            _roomCode = GUI.TextField(new Rect(centerX, startY + 84, buttonWidth, 32), _roomCode);

            // 3. Host 버튼 (방 만들기)
            if (GUI.Button(new Rect(centerX, startY + 128, buttonWidth, buttonHeight), "Host (방 만들기)"))
            {
                bool isAuto = string.IsNullOrEmpty(_roomCode);
                if (isAuto)
                    _roomCode = GenerateRandomRoomCode();
                else
                    _roomCode = _roomCode.Trim().ToUpperInvariant();

                StartGame(GameMode.Host, _roomCode, isAuto);
            }

            // 4. Join 버튼 (참가하기)
            if (GUI.Button(new Rect(centerX, startY + 128 + buttonHeight + spacing, buttonWidth, buttonHeight), "Join (참가하기)"))
            {
                if (!string.IsNullOrEmpty(_roomCode))
                {
                    _roomCode = _roomCode.Trim().ToUpperInvariant();
                    StartGame(GameMode.Client, _roomCode, false);
                }
                else
                {
                    Debug.LogWarning("참가하려면 방 코드를 입력하세요!");
                }
            }
        }
        else if (_runner != null && (_runner.IsServer || _runner.IsClient))
        {
            // 게임 시작(인게임 플레이) 시 방 코드 안 보이게 숨김
            if (IsInGameStage()) return;

            EnsureIMGUIStyles();

            // 대기실 / 캐릭터 선택 / 세이브파일 화면: 화면 상단에 방 코드 안내 (기존 UI 외형 유지 + 클릭 시 복사)
            bool isCopied = Time.realtimeSinceStartup < _roomCodeCopiedUntil;
            _imguiRoomCodeStyle.normal.textColor = isCopied ? new Color(0.45f, 1f, 0.45f) : Color.white;

            // 타이틀(중앙)과 겹치지 않도록 좌상단 구석으로 배치 (기존 크기 230x42 그대로 유지)
            float labelWidth = 230f;
            float labelHeight = 42f;
            float xPos = 20f;
            float yPos = 20f;
            Rect rect = new Rect(xPos, yPos, labelWidth, labelHeight);

            // 기존 Box와 Label 외형 100% 동일하게 렌더링
            GUI.Box(rect, "");
            string labelText = isCopied ? $"복사됨! ({_roomCode})" : $"방 코드: {_roomCode}";
            GUI.Label(rect, labelText, _imguiRoomCodeStyle);

            // 해당 영역 클릭 시 클립보드 복사
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            {
                if (!string.IsNullOrEmpty(_roomCode))
                {
                    GUIUtility.systemCopyBuffer = _roomCode;
                    _roomCodeCopiedUntil = Time.realtimeSinceStartup + 1.5f;
                    UiSfx.Click();
                    Debug.Log($"<color=lime>[Launcher]</color> 방 코드 '{_roomCode}' 클립보드 복사 완료!");
                }
            }

            GUI.Label(new Rect(10, Screen.height - 30, 200, 25), $"Status: {(_runner.IsServer ? "Host" : "Client")}");
        }
    }

    // 혼동하기 쉬운 문자(0, O, 1, I)를 제외한 가독성 높은 32개 영숫자 문자셋
    private const string RoomCodeAlphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    public const int RoomCodeLength = 6;
    private const int MaxHostRetries = 5;

    /// <summary>
    /// 충돌 확률을 획기적으로 낮춘 6자리 대문자/숫자 방 코드를 생성합니다 (약 10억 7천만 가지 조합).
    /// </summary>
    public static string GenerateRandomRoomCode()
    {
        var chars = new char[RoomCodeLength];
        for (int i = 0; i < RoomCodeLength; i++)
        {
            chars[i] = RoomCodeAlphabet[UnityEngine.Random.Range(0, RoomCodeAlphabet.Length)];
        }
        return new string(chars);
    }

    // TitleScreen 등 외부 UI에서 호출하는 진입점
    public void RequestHost(string roomCode, string nick)
    {
        ApplyNick(nick);
        bool isAuto = string.IsNullOrWhiteSpace(roomCode);
        string code = isAuto ? GenerateRandomRoomCode() : roomCode.Trim().ToUpperInvariant();
        _roomCode = code;
        StartGame(GameMode.Host, code, isAuto);
    }

    public void RequestJoin(string roomCode, string nick)
    {
        ApplyNick(nick);
        if (string.IsNullOrWhiteSpace(roomCode))
        {
            Debug.LogError("[Launcher] 들어갈 방 코드를 입력하세요!");
            OnConnectionFailed?.Invoke("방 코드를 입력하세요.");
            return;
        }
        _roomCode = roomCode.Trim().ToUpperInvariant();
        StartGame(GameMode.Client, _roomCode, false);
    }

    private void ApplyNick(string nick)
    {
        if (!string.IsNullOrWhiteSpace(nick))
        {
            _playerName = nick.Trim();
            PlayerPrefs.SetString("PlayerName", _playerName);
        }
    }

    /// <summary>
    /// 러너 및 네트워크 연결을 완전히 초기화하고 파괴합니다.
    /// 연결 실패, 재시도, 게임 종료 시 호출됩니다.
    /// </summary>
    public async Task CleanupRunnerAsync()
    {
        _isStarting = false;

        if (_runner != null)
        {
            try
            {
                if (_runner.IsRunning)
                {
                    await _runner.Shutdown();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Launcher] Runner Shutdown 예외 (정상 정리 진행): {ex.Message}");
            }

            if (_runner != null && _runner.gameObject != null)
            {
                if (_runner.gameObject == gameObject)
                {
                    Destroy(_runner);
                }
                else
                {
                    Destroy(_runner.gameObject);
                }
            }
            _runner = null;
        }

        // 씬 내 잔류 중인 고아 러너가 있다면 일괄 정리 (단, 본체 GameObject는 파괴하지 않음)
        var runners = FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None);
        foreach (var r in runners)
        {
            if (r != null)
            {
                try
                {
                    if (r.IsRunning) r.Shutdown();
                }
                catch { }

                if (r.gameObject != null)
                {
                    if (r.gameObject == gameObject)
                    {
                        Destroy(r);
                    }
                    else
                    {
                        Destroy(r.gameObject);
                    }
                }
            }
        }

        CleanupLingeringSessionItems();
    }

    private void CleanupLingeringSessionItems()
    {
        var items = FindObjectsByType<SessionPlayerItem>(FindObjectsSortMode.None);
        foreach (var item in items)
        {
            if (item != null && item.gameObject != null)
            {
                Destroy(item.gameObject);
            }
        }
    }

    async void StartGame(GameMode mode, string roomName, bool isAutoGenerated = false)
    {
        if (_isStarting)
        {
            Debug.LogWarning("[Launcher] 이미 연결 시도 중입니다. 잠시 기다려주세요.");
            return;
        }

        _isStarting = true;
        _isVoluntaryExit = false;
        HideDisconnectModal();
        OnConnectionStarted?.Invoke();

        try
        {
            int maxAttempts = (mode == GameMode.Host && isAutoGenerated) ? MaxHostRetries : 1;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                // 1. 이전 시도로 남아있을 수 있는 러너 완전 정리 (초기화)
                await CleanupRunnerAsync();

                // 2. NetworkRunner 전용 GameObject 동적 생성 (BasicLauncher 본체 오염 방지)
                var runnerGO = new GameObject("FusionNetworkRunner");
                _runner = runnerGO.AddComponent<NetworkRunner>();
                var sceneManager = runnerGO.AddComponent<NetworkSceneManagerDefault>();

                // 움직임 예측(Client Prediction) 기반 물리 시뮬레이션 활성화
                var physicsSim2D = runnerGO.AddComponent<RunnerSimulatePhysics2D>();
                physicsSim2D.ClientPhysicsSimulation = ClientPhysicsSimulation.SimulateAlways;

                // Photon Voice 음성 채팅 클라이언트 연동
                var voiceClient = runnerGO.AddComponent<Photon.Voice.Fusion.FusionVoiceClient>();
                voiceClient.UseFusionAppSettings = true;
                voiceClient.UseFusionAuthValues = true;

                _runner.ProvideInput = true;
                _runner.RemoveCallbacks(this);
                _runner.AddCallbacks(this);
                _runner.AddCallbacks(voiceClient);

                var spawner = FindFirstObjectByType<Spawner>();
                if (spawner != null)
                {
                    _runner.RemoveCallbacks(spawner);
                    _runner.AddCallbacks(spawner);
                }

                Debug.Log($"<color=white>[Launcher]</color> {mode} 모드로 시작 시도 중... 방 코드: {roomName}" + (maxAttempts > 1 ? $" (시도 {attempt}/{maxAttempts})" : ""));

                var result = await _runner.StartGame(new StartGameArgs()
                {
                    GameMode = mode,
                    SessionName = roomName,
                    // 방 정원 2명. 호스트가 정한 정원을 초과해 들어오려는 클라이언트는
                    // Fusion 이 자동으로 거부한다(ShutdownReason.GameIsFull → "방이 꽉 찼습니다.").
                    PlayerCount = MAX_PLAYERS,
                    Scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex),
                    SceneManager = sceneManager
                });

                if (result.Ok)
                {
                    _roomCode = roomName;
                    Debug.Log($"<color=green>[Launcher]</color> {mode} 세션 연결 성공! (방 코드: {roomName})");
                    DontDestroyOnLoad(runnerGO);
                    OnConnectionSuccess?.Invoke();

                    if (mode == GameMode.Host)
                    {
                        Debug.Log("호스트: CharacterSelect 씬으로 이동");
                        _runner.LoadScene("CharacterSelect", LoadSceneMode.Single);
                    }
                    return; // 성공 시 완료
                }

                // 호스트 자동 생성 코드 중복 발생 시 자동 재추첨 및 재시도
                if (mode == GameMode.Host && isAutoGenerated && result.ShutdownReason == ShutdownReason.GameIdAlreadyExists && attempt < maxAttempts)
                {
                    string oldCode = roomName;
                    roomName = GenerateRandomRoomCode();
                    _roomCode = roomName;
                    Debug.LogWarning($"<color=yellow>[Launcher]</color> 방 코드 '{oldCode}' 중복 감지. 새 방 코드 '{roomName}'(으)로 자동 재시도 중... ({attempt}/{maxAttempts})");
                    continue;
                }

                // 재시도 대상이 아니거나 최대 시도 초과 시 실패 처리
                string reasonMsg = FormatShutdownReason(result.ShutdownReason);
                Debug.LogError($"[Launcher] 시작 실패: {result.ShutdownReason} ({reasonMsg})");

                await CleanupRunnerAsync();
                OnConnectionFailed?.Invoke(reasonMsg);
                return;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Launcher] 시작 중 예외 발생: {ex.Message}\n{ex.StackTrace}");
            await CleanupRunnerAsync();
            OnConnectionFailed?.Invoke("네트워크 오류가 발생했습니다. 다시 시도하세요.");
        }
        finally
        {
            _isStarting = false;
        }
    }

    private string FormatShutdownReason(ShutdownReason reason)
    {
        switch (reason)
        {
            case ShutdownReason.GameNotFound:
                return "방을 찾을 수 없습니다. 코드를 확인하세요.";
            case ShutdownReason.GameIsFull:
                return "방이 꽉 찼습니다.";
            case ShutdownReason.GameClosed:
            case ShutdownReason.ConnectionRefused:
                return "이미 시작된 게임(세이브 선택 이후)이거나 닫힌 방입니다.";
            case ShutdownReason.GameIdAlreadyExists:
                return "이미 존재하는 방 코드입니다.";
            case ShutdownReason.ConnectionTimeout:
            case ShutdownReason.PhotonCloudTimeout:
            case ShutdownReason.OperationTimeout:
                return "연결 시간이 초과되었습니다.";
            case ShutdownReason.HostMigration:
                return "호스트 이전 중입니다.";
            default:
                return $"접속 실패 ({reason})";
        }
    }

    public async void ExitGame()
    {
        MarkVoluntaryExit();
        HideDisconnectModal();
        await CleanupRunnerAsync();

        CheckpointManager.ResetCheckpoint();
        RunManager.ResetRun();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        SceneManager.LoadScene("Lobby");
    }

    /// <summary>
    /// 호스트(서버)에서 참가자(파트너) 퇴장을 감지했을 때 호출됩니다.
    /// 인게임 중이면 현재 체크포인트 진행도를 안전하게 보존하고 안내 팝업을 띄웁니다.
    /// </summary>
    public void NotifyPartnerLeftOnServer(NetworkRunner runner, PlayerRef leftPlayer)
    {
        if (_isVoluntaryExit || _isReturningToLobby) return;
        if (runner == null || !runner.IsServer) return;
        if (leftPlayer == runner.LocalPlayer) return;

        // 나간 플레이어의 SessionPlayerItem 정리 및 남은 호스트 레디 해제
        var items = FindObjectsByType<SessionPlayerItem>(FindObjectsSortMode.None);
        foreach (var item in items)
        {
            if (item == null || item.Object == null) continue;
            if (item.Object.InputAuthority == leftPlayer)
            {
                runner.Despawn(item.Object);
            }
            else if (SceneManager.GetActiveScene().name == "CharacterSelect")
            {
                item.IsReady = false;
            }
        }
        CharacterSelectUI.Instance?.NotifySessionChanged();

        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene == "Lobby") return;

        if (currentScene == "CharacterSelect")
        {
            ShowDisconnectModal(
                "PARTNER LEFT",
                "파트너와의 연결이 끊어졌습니다.\n계속 대기하거나 로비로 돌아갈 수 있습니다.",
                allowKeepWaiting: true);
            return;
        }

        bool savedCheckpoint = EnsureProgressSavedOnHostDisconnect(currentScene);
        if (NetworkTimer.Instance != null && NetworkTimer.Instance.Phase == NetworkTimer.PhaseRunning)
        {
            NetworkTimer.Instance.PauseForChoice();
        }
        Time.timeScale = 0f;

        string body = savedCheckpoint
            ? "파트너와의 연결이 끊어졌습니다.\n(현재 체크포인트까지 자동 저장됨)"
            : "파트너와의 연결이 끊어졌습니다.\n안전하게 로비로 돌아갑니다.";

        ShowDisconnectModal("DISCONNECTED", body, allowKeepWaiting: false);
    }

    private bool EnsureProgressSavedOnHostDisconnect(string currentScene)
    {
        if (SaveDataManager.CurrentSlotIndex < 0) return false;

        if (currentScene == "Main Stage1" || currentScene == "Main Stage2" || currentScene == "Main Stage3")
        {
            var nt = NetworkTimer.Instance;
            if (CheckpointManager.hasCheckpoint && nt != null)
            {
                SaveDataManager.SaveCheckpoint(currentScene, RunManager.AccumulatedTime, nt.ElapsedTime, CheckpointManager.lastCheckpointPosition);
                return true;
            }
            return true; // 스테이지 진입 시 이미 SaveProgressWithTime 으로 자동 저장됨
        }
        return false;
    }

    /// <summary>
    /// 클라이언트 측에서 호스트 연결 끊김을 감지했을 때 팝업을 띄우거나 로비로 복귀합니다.
    /// </summary>
    public void HandleClientDisconnected(string reason = "호스트와의 연결이 끊어졌습니다.")
    {
        if (_isVoluntaryExit || _isReturningToLobby || IsDisconnectModalOpen) return;

        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene == "Lobby")
        {
            FastCleanupRunner();
            return;
        }

        Debug.LogWarning($"<color=yellow>[Launcher]</color> 클라이언트 연결 끊김 처리: {reason} (씬: {currentScene})");
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 죽은 러너를 먼저 정리해 네트워크 예외/블로킹 방지
        FastCleanupRunner();
        Time.timeScale = 0f;

        bool isMainStage = currentScene == "Main Stage1" || currentScene == "Main Stage2" || currentScene == "Main Stage3";
        string body = isMainStage
            ? "파트너(호스트)와의 연결이 끊어졌습니다.\n(현재 체크포인트까지 호스트에 자동 저장됨)"
            : $"{reason}\n안전하게 로비로 돌아갑니다.";

        ShowDisconnectModal("DISCONNECTED", body, allowKeepWaiting: false);
    }

    /// <summary>
    /// 호스트 접속 끊김 등의 사유로 블로킹/데드락 없이 안전하게 처리합니다.
    /// 인게임/대기실 중이면 안내 팝업을 먼저 띄우고, 확인 시 로비로 복귀합니다.
    /// </summary>
    public void ForceReturnToLobby(string reason = "호스트와의 연결이 끊어졌습니다.")
    {
        if (_isVoluntaryExit) return;

        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene != "Lobby")
        {
            HandleClientDisconnected(reason);
            return;
        }

        if (_isReturningToLobby) return;
        _isReturningToLobby = true;

        Debug.Log($"<color=yellow>[Launcher]</color> ForceReturnToLobby 실행: {reason}");

        if (!string.IsNullOrEmpty(reason))
        {
            PlayerPrefs.SetString("DisconnectNotice", reason);
        }

        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        FastCleanupRunner();
        _isReturningToLobby = false;
    }

    private void FastCleanupRunner()
    {
        _isStarting = false;

        if (_runner != null)
        {
            try
            {
                _runner.RemoveCallbacks(this);
                if (_runner.IsRunning)
                {
                    _runner.Shutdown();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Launcher] FastCleanupRunner Shutdown 예외 (정상 무시): {ex.Message}");
            }

            if (_runner != null && _runner.gameObject != null)
            {
                if (_runner.gameObject == gameObject)
                {
                    Destroy(_runner);
                }
                else
                {
                    Destroy(_runner.gameObject);
                }
            }
            _runner = null;
        }

        // 잔여 러너들 정리
        var runners = FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None);
        foreach (var r in runners)
        {
            if (r != null)
            {
                try { if (r.IsRunning) r.Shutdown(); } catch { }
                if (r.gameObject != null && r.gameObject != gameObject)
                {
                    Destroy(r.gameObject);
                }
            }
        }

        CleanupLingeringSessionItems();
    }

    #region Disconnect Notice Modal UI

    private void EnsureDisconnectModalUI()
    {
        if (_disconnectModalRoot != null) return;

        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(es);
        }

        _pixelFont = Resources.Load<Font>("BoldPixels");
        _plainFont = Resources.Load<Font>("Sunflower-Medium");
        if (_plainFont == null) _plainFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_pixelFont == null) _pixelFont = _plainFont;

        var root = new GameObject("DisconnectNoticeCanvas");
        DontDestroyOnLoad(root);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760; // ScreenFader(32700) 및 CutsceneCanvas(32600)보다 위에 표시
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();
        _disconnectModalRoot = root;

        // 반투명 딤 배경
        var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image));
        dim.transform.SetParent(root.transform, false);
        var dimRt = dim.GetComponent<RectTransform>();
        dimRt.anchorMin = Vector2.zero;
        dimRt.anchorMax = Vector2.one;
        dimRt.offsetMin = Vector2.zero;
        dimRt.offsetMax = Vector2.zero;
        dim.GetComponent<Image>().color = new Color(0.04f, 0.03f, 0.02f, 0.84f);

        // 중앙 우드 카드 패널
        var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
        card.transform.SetParent(root.transform, false);
        var cardRt = card.GetComponent<RectTransform>();
        cardRt.anchorMin = cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.pivot = new Vector2(0.5f, 0.5f);
        cardRt.sizeDelta = new Vector2(820f, 430f);
        card.GetComponent<Image>().color = new Color(0.19f, 0.11f, 0.05f, 1f);

        var bevel = CreateModalInset(card.transform, "Bevel", 6f, new Color(0.60f, 0.43f, 0.24f, 1f));
        var face = CreateModalInset(card.transform, "Face", 12f, new Color(0.42f, 0.28f, 0.16f, 1f));

        _disconnectTitleText = CreateModalLabel(face.transform, "Title", new Vector2(0.5f, 0.78f),
            new Vector2(740f, 90f), "DISCONNECTED", _pixelFont, 64, new Color(0.98f, 0.82f, 0.42f, 1f));
        AddModalOutline(_disconnectTitleText.gameObject, new Color(0.13f, 0.08f, 0.02f, 1f), 4f);

        _disconnectBodyText = CreateModalLabel(face.transform, "Body", new Vector2(0.5f, 0.50f),
            new Vector2(740f, 140f), "파트너와의 연결이 끊어졌습니다.", _plainFont, 32, new Color(0.98f, 0.95f, 0.89f, 1f));
        AddModalOutline(_disconnectBodyText.gameObject, new Color(0.12f, 0.07f, 0.02f, 0.95f), 2f);

        _keepWaitBtn = CreateModalButton(face.transform, "KeepWaitingBtn", new Vector2(0.5f, 0.18f),
            new Vector2(-175f, 0f), new Vector2(310f, 84f),
            new Color(0.55f, 0.60f, 0.66f, 1f), "계속 대기", new Color(0.98f, 0.95f, 0.89f, 1f), OnDisconnectKeepWaitingClicked);

        _returnLobbyBtn = CreateModalButton(face.transform, "ReturnLobbyBtn", new Vector2(0.5f, 0.18f),
            new Vector2(0f, 0f), new Vector2(340f, 84f),
            new Color(0.98f, 0.82f, 0.42f, 1f), "로비로 돌아가기", new Color(0.12f, 0.07f, 0.02f, 1f), OnDisconnectReturnLobbyClicked);

        _disconnectModalRoot.SetActive(false);
    }

    private void ShowDisconnectModal(string title, string body, bool allowKeepWaiting)
    {
        EnsureDisconnectModalUI();

        if (SettingsMenuUI.Instance != null && SettingsMenuUI.Instance.IsOpen)
        {
            SettingsMenuUI.Instance.CloseMenu();
        }
        SettingsMenuUI.GameplayInputSuppressed = true;

        _disconnectTitleText.text = title;
        _disconnectBodyText.text = body;

        if (_keepWaitBtn != null) _keepWaitBtn.SetActive(allowKeepWaiting);
        if (_returnLobbyBtn != null)
        {
            var rt = _returnLobbyBtn.GetComponent<RectTransform>();
            rt.anchoredPosition = allowKeepWaiting ? new Vector2(175f, 0f) : Vector2.zero;
        }

        _disconnectModalRoot.SetActive(true);
        UiSfx.PopupOpen();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void HideDisconnectModal()
    {
        if (_disconnectModalRoot != null && _disconnectModalRoot.activeSelf)
        {
            _disconnectModalRoot.SetActive(false);
            SettingsMenuUI.GameplayInputSuppressed = false;
            Time.timeScale = 1f;
        }
    }

    private void OnDisconnectKeepWaitingClicked()
    {
        HideDisconnectModal();
    }

    private void OnDisconnectReturnLobbyClicked()
    {
        HideDisconnectModal();
        _isReturningToLobby = true;
        _isVoluntaryExit = true;

        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        CheckpointManager.ResetCheckpoint();
        RunManager.ResetRun();
        FastCleanupRunner();

        if (SceneManager.GetActiveScene().name != "Lobby")
        {
            SceneManager.LoadScene("Lobby");
        }
        else
        {
            _isReturningToLobby = false;
            _isVoluntaryExit = false;
        }
    }

    private static Image CreateModalInset(Transform parent, string name, float pad, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(pad, pad);
        rt.offsetMax = new Vector2(-pad, -pad);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static Text CreateModalLabel(Transform parent, string name, Vector2 anchor, Vector2 size, string content, Font font, int fontSize, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;
        var t = go.GetComponent<Text>();
        t.font = font;
        t.fontSize = fontSize;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.color = color;
        t.text = content;
        t.raycastTarget = false;
        return t;
    }

    private GameObject CreateModalButton(Transform parent, string name, Vector2 anchor, Vector2 offset, Vector2 size, Color faceColor, string label, Color labelColor, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = offset;

        var hit = go.GetComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        var btn = go.GetComponent<Button>();

        CreateModalInset(go.transform, "Border", 0f, new Color(0.19f, 0.11f, 0.05f, 1f));
        CreateModalInset(go.transform, "Bevel", 4f, new Color(0.60f, 0.43f, 0.24f, 1f));
        var face = CreateModalInset(go.transform, "Face", 8f, faceColor);
        btn.targetGraphic = face;
        btn.onClick.AddListener(onClick);

        var lbl = CreateModalLabel(face.transform, "Label", new Vector2(0.5f, 0.5f), size, label, _plainFont, 30, labelColor);
        AddModalOutline(lbl.gameObject, new Color(0.13f, 0.08f, 0.02f, 0.9f), 2f);
        return go;
    }

    private static void AddModalOutline(GameObject go, Color color, float dist)
    {
        var o = go.AddComponent<Outline>();
        o.effectColor = color;
        o.effectDistance = new Vector2(dist, -dist);
    }

    #endregion

    #region INetworkRunnerCallbacks Implementation

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        if (_isVoluntaryExit || _isStarting) return;
        Debug.LogWarning($"<color=red>[Launcher]</color> 서버 연결이 끊겼습니다: {reason}.");
        if (runner == null || !runner.IsServer)
        {
            HandleClientDisconnected($"호스트와의 연결이 끊어졌습니다 ({reason}).");
        }
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        if (_isVoluntaryExit || _isStarting) return;
        if (SceneManager.GetActiveScene().name == "Lobby") return;

        Debug.LogWarning($"<color=red>[Launcher]</color> NetworkRunner 비자발적 Shutdown 감지: {shutdownReason}.");
        if (runner == null || !runner.IsServer)
        {
            string msg = shutdownReason == ShutdownReason.Ok
                ? "호스트가 세션을 종료했습니다."
                : $"호스트와의 연결이 종료되었습니다 ({shutdownReason}).";
            HandleClientDisconnected(msg);
        }
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (_isVoluntaryExit || runner == null) return;

        if (runner.IsServer)
        {
            Debug.LogWarning($"<color=red>[Launcher]</color> 호스트에서 파트너({player}) 퇴장 감지.");
            NotifyPartnerLeftOnServer(runner, player);
        }
        else
        {
            Debug.LogWarning($"<color=red>[Launcher]</color> 상대 플레이어 {player} 퇴장 감지.");
            HandleClientDisconnected("호스트가 접속을 종료했습니다.");
        }
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {
        if (runner != null && runner.IsServer)
        {
            string currentScene = SceneManager.GetActiveScene().name;
            if (!CanJoinInScene(currentScene))
            {
                Debug.LogWarning($"[Launcher] 세이브 파일 선택 이후({currentScene})에는 새로운 참가 요청을 거부합니다.");
                request.Refuse();
                return;
            }
            if (System.Linq.Enumerable.Count(runner.ActivePlayers) >= MAX_PLAYERS)
            {
                Debug.LogWarning($"[Launcher] 방 정원({MAX_PLAYERS}명)이 초과되어 새로운 참가 요청을 거부합니다.");
                request.Refuse();
                return;
            }
        }
        request.Accept();
    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        if (_isVoluntaryExit) return;
        Debug.LogWarning($"<color=red>[Launcher]</color> 서버 연결 거부/실패: {reason}");
        if (reason == NetConnectFailedReason.ServerRefused)
        {
            OnConnectionFailed?.Invoke("이미 시작된 게임(세이브 선택 이후)이거나 참가할 수 없는 방입니다.");
        }
        else if (reason == NetConnectFailedReason.ServerFull)
        {
            OnConnectionFailed?.Invoke("방이 꽉 찼습니다.");
        }
    }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

    #endregion
}

