using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Fusion.Addons.Physics;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

public class BasicLauncher : MonoBehaviour, INetworkRunnerCallbacks
{
    public static BasicLauncher Instance { get; private set; }

    // 한 방의 최대 인원. 이 게임은 2인 협동이라 2명 초과 입장을 막는다.
    private const int MAX_PLAYERS = 2;

    private NetworkRunner _runner;
    private string _roomCode = ""; // 사용자가 입력할 방 코드
    private bool _isStarting = false;
    private bool _isReturningToLobby = false;
    private string _playerName = "";
    [SerializeField] private bool _useIMGUIMenu = false; // 새 TitleScreen 도입으로 구 IMGUI 로비 메뉴는 기본 비활성화

    // 연결 상태 이벤트
    public event Action OnConnectionStarted;
    public event Action<string> OnConnectionFailed;
    public event Action OnConnectionSuccess;

    public bool IsStarting => _isStarting;
    public NetworkRunner Runner => _runner;

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

        Application.logMessageReceived += OnLogMessageReceived;
    }

    private void Start()
    {
        _playerName = PlayerPrefs.GetString("PlayerName", "Player" + UnityEngine.Random.Range(100, 999));
    }

    private void OnDestroy()
    {
        Application.logMessageReceived -= OnLogMessageReceived;
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnLogMessageReceived(string condition, string stackTrace, UnityEngine.LogType type)
    {
        if (string.IsNullOrEmpty(condition)) return;

        // Photon Fusion의 서버 연결 해제 메시지 감지
        // 예: [Fusion] Got DisconnectMessage. Code: 104 Msg: "Server has disconnected. "
        if (condition.Contains("Code: 104") || condition.Contains("Server has disconnected"))
        {
            if (_runner != null && !_runner.IsServer)
            {
                Debug.LogWarning("[Launcher] 호스트 접속 종료 감지 (Code 104). 자동으로 로비로 복귀합니다.");
                ForceReturnToLobby("호스트가 접속을 종료하여 로비로 이동했습니다.");
            }
        }
    }

    public string RoomCode => _roomCode;

    private static readonly string[] InGameStageNames = { "TutorialStage", "Main Stage1", "Main Stage2", "Main Stage3" };

    public static bool IsInGameStage()
    {
        string currentScene = SceneManager.GetActiveScene().name;
        foreach (var name in InGameStageNames)
        {
            if (currentScene == name) return true;
        }
        return false;
    }

    private void OnGUI()
    {
        // 랭킹 보드나 ESC 설정 메뉴가 열려있으면 레거시 GUI를 일체 그리지 않음
        if (RankingManager.Instance != null && RankingManager.Instance.IsBoardOpen) return;
        if (SettingsMenuUI.Instance != null && SettingsMenuUI.Instance.IsOpen) return;

        // 아직 룸에 접속하지 않은 상태 (로비 화면) - _useIMGUIMenu가 true일 때만 레거시 IMGUI 표시
        if (_useIMGUIMenu && (_runner == null || !_runner.IsRunning))
        {
            float buttonWidth = 260f;
            float buttonHeight = 44f;
            float spacing = 12f;
            float centerX = (Screen.width - buttonWidth) / 2f;
            
            // 타이틀 "보이스 히어로" 아래쪽의 패널 안으로 들어가도록 Y축 위치를 아래로 이동
            float startY = Screen.height * 0.38f;

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 14;
            labelStyle.fontStyle = FontStyle.Bold;
            labelStyle.normal.textColor = Color.white;

            // 1. 닉네임 입력창
            GUI.Label(new Rect(centerX, startY, buttonWidth, 20), "닉네임 (Nickname):", labelStyle);
            _playerName = GUI.TextField(new Rect(centerX, startY + 22, buttonWidth, 32), _playerName);
            PlayerPrefs.SetString("PlayerName", _playerName);

            // 2. 방 코드 입력창
            GUI.Label(new Rect(centerX, startY + 62, buttonWidth, 20), "방 코드 입력 (Join 시 필요):", labelStyle);
            _roomCode = GUI.TextField(new Rect(centerX, startY + 84, buttonWidth, 32), _roomCode);

            // 3. Host 버튼 (방 만들기)
            if (GUI.Button(new Rect(centerX, startY + 128, buttonWidth, buttonHeight), "Host (방 만들기)"))
            {
                if (string.IsNullOrEmpty(_roomCode))
                    _roomCode = UnityEngine.Random.Range(1000, 9999).ToString();

                StartGame(GameMode.Host, _roomCode);
            }

            // 4. Join 버튼 (참가하기)
            if (GUI.Button(new Rect(centerX, startY + 128 + buttonHeight + spacing, buttonWidth, buttonHeight), "Join (참가하기)"))
            {
                if (!string.IsNullOrEmpty(_roomCode))
                {
                    StartGame(GameMode.Client, _roomCode);
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

            // 대기실 / 캐릭터 선택 / 세이브파일 화면: 화면 상단에 방 코드 안내
            GUIStyle roomCodeStyle = new GUIStyle(GUI.skin.label);
            roomCodeStyle.fontSize = 22;
            roomCodeStyle.fontStyle = FontStyle.Bold;
            roomCodeStyle.normal.textColor = Color.white;
            roomCodeStyle.alignment = TextAnchor.MiddleCenter;

            // 타이틀(중앙)과 겹치지 않도록 좌상단 구석으로 배치
            float labelWidth = 230f;
            float labelHeight = 42f;
            float xPos = 20f;
            float yPos = 20f;

            GUI.Box(new Rect(xPos, yPos, labelWidth, labelHeight), "");
            GUI.Label(new Rect(xPos, yPos, labelWidth, labelHeight), $"방 코드: {_roomCode}", roomCodeStyle);

            GUI.Label(new Rect(10, Screen.height - 30, 200, 25), $"Status: {(_runner.IsServer ? "Host" : "Client")}");
        }
    }

    // TitleScreen 등 외부 UI에서 호출하는 진입점
    public void RequestHost(string roomCode, string nick)
    {
        ApplyNick(nick);
        string code = string.IsNullOrWhiteSpace(roomCode) ? UnityEngine.Random.Range(1000, 9999).ToString() : roomCode.Trim();
        _roomCode = code;
        StartGame(GameMode.Host, code);
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
        _roomCode = roomCode.Trim();
        StartGame(GameMode.Client, _roomCode);
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
    }

    async void StartGame(GameMode mode, string roomName)
    {
        if (_isStarting)
        {
            Debug.LogWarning("[Launcher] 이미 연결 시도 중입니다. 잠시 기다려주세요.");
            return;
        }

        _isStarting = true;
        OnConnectionStarted?.Invoke();

        try
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

            _runner.ProvideInput = true;
            _runner.RemoveCallbacks(this);
            _runner.AddCallbacks(this);

            var spawner = FindFirstObjectByType<Spawner>();
            if (spawner != null)
            {
                _runner.RemoveCallbacks(spawner);
                _runner.AddCallbacks(spawner);
            }

            Debug.Log($"<color=white>[Launcher]</color> {mode} 모드로 시작 시도 중... 방 코드: {roomName}");

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
                Debug.Log($"<color=green>[Launcher]</color> {mode} 세션 연결 성공! (방 코드: {roomName})");
                DontDestroyOnLoad(runnerGO);
                OnConnectionSuccess?.Invoke();

                if (mode == GameMode.Host)
                {
                    Debug.Log("호스트: CharacterSelect 씬으로 이동");
                    _runner.LoadScene("CharacterSelect", LoadSceneMode.Single);
                }
            }
            else
            {
                string reasonMsg = FormatShutdownReason(result.ShutdownReason);
                Debug.LogError($"[Launcher] 시작 실패: {result.ShutdownReason} ({reasonMsg})");

                // 실패 시 즉시 러너 파괴 및 초기화하여 다음 시도(Host 등)가 가능하도록 복구
                await CleanupRunnerAsync();
                OnConnectionFailed?.Invoke(reasonMsg);
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
                return "이미 종료되었거나 닫힌 방입니다.";
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
        await CleanupRunnerAsync();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        SceneManager.LoadScene("Lobby");
    }

    /// <summary>
    /// 호스트 접속 끊김 등의 사유로 블로킹/데드락 없이 즉시 로비 씬으로 안전하게 탈출합니다.
    /// </summary>
    public void ForceReturnToLobby(string reason = "호스트와의 연결이 끊어졌습니다.")
    {
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

        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene != "Lobby")
        {
            SceneManager.LoadScene("Lobby");
        }

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
    }

    #region INetworkRunnerCallbacks Implementation

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.LogWarning($"<color=red>[Launcher]</color> 서버 연결이 끊겼습니다: {reason}. 로비로 이동합니다.");
        if (runner != null && !runner.IsServer)
        {
            ForceReturnToLobby($"서버 연결이 끊겼습니다 ({reason}).");
        }
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.LogWarning($"<color=red>[Launcher]</color> NetworkRunner Shutdown: {shutdownReason}.");
        // 정상 로비 복귀나 타이틀 이동(Ok) 외의 비정상 셧다운일 때 클라이언트는 로비로 복귀
        if (runner != null && !runner.IsServer && shutdownReason != ShutdownReason.Ok)
        {
            ForceReturnToLobby($"호스트와의 연결이 종료되었습니다 ({shutdownReason}).");
        }
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        // 2인 게임에서 호스트가 나갔을 경우 클라이언트 자동 퇴장
        if (runner != null && !runner.IsServer)
        {
            Debug.LogWarning($"<color=red>[Launcher]</color> 상대 플레이어 {player} 퇴장 감지. 로비로 복귀합니다.");
            ForceReturnToLobby("호스트가 접속을 종료했습니다.");
        }
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
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
