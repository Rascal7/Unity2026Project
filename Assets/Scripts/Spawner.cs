using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Spawner : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Prefabs")]
    public NetworkObject sessionItemPrefab;
    public NetworkObject player1Prefab;
    public NetworkObject player2Prefab;

    [Tooltip("서버 연결이 끊겼을 때 돌아갈 씬 이름")]
    public string lobbySceneName = "Lobby";
    private bool _returningToLobby;
    private Dictionary<PlayerRef, NetworkObject> _spawnedCharacters = new Dictionary<PlayerRef, NetworkObject>();

    // Phase2: resume-from-checkpoint info for the current stage load (host only, set in OnSceneLoadDone).
    private Vector3? _resumeSpawnPos = null;
    private float _resumeTimerOffset = 0f;

    // 인게임 스테이지로 취급할 씬 이름들 (여기에 새 스테이지 추가하면 됨)
    private static readonly string[] InGameStageNames = { "TutorialStage", "Main Stage1", "Main Stage2", "Main Stage3", "TestStage", "GameStage" };

    private bool IsInGameStage(string sceneName)
    {
        foreach (var name in InGameStageNames)
        {
            if (sceneName == name) return true;
        }
        return false;
    }

    private void Awake()
    {
        // 에디터/빌드가 포커스를 잃어도 네트워크 루프를 계속 돌린다.
        // 이게 꺼져 있으면 Photon 이 AppOutOfFocusRecent 로 TimeoutDisconnect 를 띄운다.
        Application.runInBackground = true;

        var objs = FindObjectsByType<Spawner>(UnityEngine.FindObjectsSortMode.None);
        if (objs.Length > 1)
        {
            Destroy(gameObject);
            return;
        }
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        RegisterCallbacks();
    }

    private void RegisterCallbacks()
    {
        var runner = FindFirstObjectByType<NetworkRunner>();
        if (runner != null)
        {
            runner.RemoveCallbacks(this);
            runner.AddCallbacks(this);
            Debug.Log("<color=green>[Spawner]</color> 콜백 등록 완료!");
        }
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        RegisterCallbacks();
        if (!runner.IsServer) return;

        string currentScene = SceneManager.GetActiveScene().name;

        // 세이브 파일 선택(savefile) 및 이후 인게임 씬에서는 방 참가를 차단하고, 로비/캐릭터 선택에서만 열어둔다.
        if (runner.SessionInfo != null && runner.SessionInfo.IsValid)
        {
            runner.SessionInfo.IsOpen = BasicLauncher.CanJoinInScene(currentScene);
        }

        // 씬이 바뀌면 이전 씬의 NetworkObject 는 모두 Despawn 된다.
        // 여기서 비워두지 않으면 다음 스테이지에서 스폰 가드에 걸려 캐릭터가 안 나온다.
        _spawnedCharacters.Clear();

        // 1. 캐릭터 선택 씬
        if (currentScene == "CharacterSelect")
        {
            var oldItems = FindObjectsByType<SessionPlayerItem>(UnityEngine.FindObjectsSortMode.None);
            foreach (var old in oldItems)
            {
                if (old.Object != null) runner.Despawn(old.Object);
            }

            foreach (var p in runner.ActivePlayers)
            {
                runner.Spawn(sessionItemPrefab, Vector3.zero, Quaternion.identity, p);
            }
        }
        // 2. 인게임 스테이지 (Tutorial, Stage1, Stage2, Stage3 공통 처리)
        else if (IsInGameStage(currentScene))
        {
            Debug.Log($"<color=yellow>[Spawner]</color> 씬 로드 완료: {currentScene}. 플레이어 스폰 시작!");

            // 랭킹 리더보드를 미리 캐시해 둔다 (클리어 시 New Record 즉시 판정용).
            RankingManager.PrefetchLeaderboard();

            // Phase2: resume info (checkpoint pos + timer offset + collected coins) from the save slot, if any.
            _resumeSpawnPos = null;
            _resumeTimerOffset = 0f;
            if (currentScene != "TutorialStage")
            {
                var slot = SaveDataManager.GetSlot(SaveDataManager.CurrentSlotIndex);
                if (slot != null && slot.hasCheckpoint && slot.stageName == currentScene)
                {
                    _resumeSpawnPos = new Vector3(slot.checkpointX, slot.checkpointY, 0f);
                    _resumeTimerOffset = slot.stageElapsed;
                    CheckpointManager.lastCheckpointPosition = _resumeSpawnPos.Value;
                    CheckpointManager.hasCheckpoint = true;

                    // 체크포인트 저장 당시 획득했던 현재 스테이지 코인 복원 및 중복 코인 제거
                    RunManager.RestoreCheckpointStageShards(slot.checkpointStageShards, slot.checkpointCoinKeys);
                    if (RunManager.CurrentStageCollectedCoinKeys.Count > 0)
                    {
                        var coins = FindObjectsByType<CoinItem>(UnityEngine.FindObjectsSortMode.None);
                        foreach (var coin in coins)
                        {
                            if (coin == null) continue;
                            string key = RunManager.GetCoinKey(coin.transform.position);
                            if (RunManager.CurrentStageCollectedCoinKeys.Contains(key))
                            {
                                coin.DespawnAsAlreadyCollected(runner);
                            }
                        }
                    }

                    Debug.Log($"<color=orange>[Spawner]</color> resume checkpoint @ ({slot.checkpointX:0.0},{slot.checkpointY:0.0}) offset {slot.stageElapsed:0.00}s, restoredShards={slot.checkpointStageShards}");
                }
            }

            if (currentScene == "TutorialStage")
            {
                RunManager.ResetRun(); // 새 런 시작: 누적 초기화 (튜토리얼은 타이머 없음)
            }
            else if (NetworkTimer.Instance == null)
            {
                var timerPrefab = Resources.Load<NetworkObject>("NetworkTimer");
                if (timerPrefab != null)
                {
                    var timerObj = runner.Spawn(timerPrefab);
                    var nt = timerObj != null ? timerObj.GetComponent<NetworkTimer>() : null;
                    if (nt != null) nt.StartWithIntro(3f, _resumeTimerOffset);  // 스킬 등록 연출이 들어갈 수 있게 3초
                    Debug.Log("<color=orange>[Timer]</color> NetworkTimer 생성 및 시작 완료!");
                }
                else
                {
                    Debug.LogError("<color=red>[Spawner]</color> Resources/NetworkTimer를 찾을 수 없습니다! 로컬 타이머로 대체합니다.");
                    if (TimerManager.Instance != null)
                    {
                        TimerManager.Instance.ResetTimer();
                        TimerManager.Instance.StartTimer();
                    }
                }
            }

            foreach (var p in runner.ActivePlayers)
            {
                SpawnSelectedPlayer(runner, p);
            }
        }
    }

    private void SpawnSelectedPlayer(NetworkRunner runner, PlayerRef player)
    {
        // 이미 이 플레이어의 캐릭터가 살아있으면 또 스폰하지 않는다.
        // (중복 스폰되면 같은 InputAuthority 를 가진 캐릭터 둘이 같은 입력을 먹어 스킬/이펙트가 2배로 터진다)
        if (_spawnedCharacters.TryGetValue(player, out NetworkObject existing))
        {
            if (existing != null) return;
            _spawnedCharacters.Remove(player); // 이미 파괴된 항목은 정리
        }

        int selectedIndex = 0;
        var items = FindObjectsByType<SessionPlayerItem>(UnityEngine.FindObjectsSortMode.None);

        foreach (var item in items)
        {
            if (item.Object.InputAuthority == player)
            {
                selectedIndex = item.SelectedCharacterIndex;
                break;
            }
        }

        // StartPoint 오브젝트 찾기
        Vector3 spawnPos = new Vector3(-20f, -7f, 0f); // 기본값
        // Phase2: resume at the saved checkpoint position when loading mid-stage.
        if (_resumeSpawnPos.HasValue)
        {
            Vector3 rp = PlayerMovement.GetSafeSpawnPosition(runner, player, _resumeSpawnPos.Value);
            NetworkObject rpf = (selectedIndex == 0) ? player1Prefab : player2Prefab;
            Debug.Log($"<color=green>[Spawn]</color> resume player {player} @ checkpoint");
            var resumed = runner.Spawn(rpf, rp, Quaternion.identity, player);
            if (resumed != null) _spawnedCharacters[player] = resumed;
            return;
        }

        GameObject startPoint = GameObject.Find("StartPoint");

        if (startPoint != null)
        {
            spawnPos = startPoint.transform.position;
            // 플레이어끼리 겹치지 않게 살짝 이동
            spawnPos = PlayerMovement.GetSafeSpawnPosition(runner, player, startPoint.transform.position);
        }

        NetworkObject prefabToSpawn = (selectedIndex == 0) ? player1Prefab : player2Prefab;

        Debug.Log($"<color=green>[Spawn]</color> 플레이어 {player} (인덱스 {selectedIndex}) 스폰 완료");
        var spawned = runner.Spawn(prefabToSpawn, spawnPos, Quaternion.identity, player);
        if (spawned != null) _spawnedCharacters[player] = spawned;
        // 타이머는 OnSceneLoadDone 에서 NetworkTimer 로 처리한다.
    }

    // --- 입력 처리 ---
    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        var data = new NetworkInputData();

        // 옵션창이 열려 있으면(키 재설정 포함), 또는 튜토리얼 미션 브리핑 중이면 게임플레이 입력을 막는다.
        if (SettingsMenuUI.GameplayInputSuppressed || PlayerMovement.TutorialHold)
        {
            input.Set(data); // 전부 0/None (이동·점프·스킬 없음)
            return;
        }

        // 이동/점프: 설정 가능한 키 바인딩(기본 WASD+방향키, 점프 Space) 기반으로 디지털 입력 구성
        float mx = 0f, my = 0f;
        if (MovementKeybinds.Pressed(MovementKeybinds.Act.Right)) mx += 1f;
        if (MovementKeybinds.Pressed(MovementKeybinds.Act.Left))  mx -= 1f;
        if (MovementKeybinds.Pressed(MovementKeybinds.Act.Up))    my += 1f;
        if (MovementKeybinds.Pressed(MovementKeybinds.Act.Down))  my -= 1f;
        data.direction.x = mx;
        data.direction.y = my;
        data.buttons.Set(InputButton.Jump, MovementKeybinds.Pressed(MovementKeybinds.Act.Jump));

        // 스킬 슬롯 입력: 키보드(ESC 메뉴에서 재설정 가능, 기본 Shift/Ctrl/Alt) 또는 음성(키워드→슬롯)
        // 여기서는 "누르고 있는지"만 보낸다. 발동은 PlayerMovement 가 눌린 순간에만 한다.
        int voiceSlot = VoiceSkillInput.RequestedSlot;
        data.buttons.Set(InputButton.Skill1, Input.GetKey(SkillKeybinds.GetSlotKey(1)) || voiceSlot == 1);
        data.buttons.Set(InputButton.Skill2, Input.GetKey(SkillKeybinds.GetSlotKey(2)) || voiceSlot == 2);
        data.buttons.Set(InputButton.Skill3, Input.GetKey(SkillKeybinds.GetSlotKey(3)) || voiceSlot == 3);

        input.Set(data);
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (BasicLauncher.IsVoluntaryExit) return;

        if (runner.IsServer)
        {
            if (_spawnedCharacters.TryGetValue(player, out NetworkObject networkObject))
            {
                if (networkObject != null) runner.Despawn(networkObject);
                _spawnedCharacters.Remove(player);
                Debug.Log($"<color=red>[Spawner]</color> 플레이어 {player} 퇴장, 캐릭터 제거");
            }
            if (BasicLauncher.Instance != null)
            {
                BasicLauncher.Instance.NotifyPartnerLeftOnServer(runner, player);
            }
        }
        else
        {
            // 참가자(Join 측) 입장에서 플레이어가 나갔다면 호스트가 종료한 것
            Debug.LogWarning("<color=red>[Spawner]</color> 호스트가 접속을 종료했습니다.");
            ReturnToLobby(runner, "호스트가 접속을 종료했습니다.");
        }
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (!runner.IsServer) return;

        string currentScene = SceneManager.GetActiveScene().name;
        Debug.Log($"<color=cyan>[Spawner]</color> 플레이어 {player} 참가! 현재 씬: {currentScene}");

        // 1. 캐릭터 선택 씬에서 새 플레이어가 들어온 경우
        if (currentScene == "CharacterSelect")
        {
            bool alreadyExists = false;
            var items = FindObjectsByType<SessionPlayerItem>(UnityEngine.FindObjectsSortMode.None);
            foreach (var item in items)
            {
                if (item.Object != null && item.Object.InputAuthority == player)
                {
                    alreadyExists = true;
                    break;
                }
            }

            if (!alreadyExists && sessionItemPrefab != null)
            {
                Debug.Log($"<color=lime>[Spawner]</color> 참가한 플레이어 {player}를 위한 SessionPlayerItem 생성 완료!");
                runner.Spawn(sessionItemPrefab, Vector3.zero, Quaternion.identity, player);
            }
        }
        // 2. 세이브 파일 선택 또는 인게임 스테이지에서 중간 참가한 경우 차단
        else if (!BasicLauncher.CanJoinInScene(currentScene))
        {
            if (player != runner.LocalPlayer)
            {
                bool hadSessionItem = false;
                var items = FindObjectsByType<SessionPlayerItem>(UnityEngine.FindObjectsSortMode.None);
                foreach (var item in items)
                {
                    if (item != null && item.Object != null && item.Object.InputAuthority == player)
                    {
                        hadSessionItem = true;
                        break;
                    }
                }

                if (!hadSessionItem)
                {
                    Debug.LogWarning($"<color=red>[Spawner]</color> 진행 중인 게임({currentScene})에 외부 플레이어({player}) 참가 시도 -> 연결 해제");
                    runner.Disconnect(player);
                    return;
                }
            }
        }
    }

    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        // 자발적 퇴장(로비 이동 버튼 클릭)이거나 이미 로비 씬이면 알림을 띄우지 않는다.
        if (BasicLauncher.IsVoluntaryExit || SceneManager.GetActiveScene().name == lobbySceneName) return;
        if (BasicLauncher.Instance != null && BasicLauncher.Instance.IsStarting) return;

        string msg = shutdownReason == ShutdownReason.Ok
            ? "호스트가 세션을 종료했습니다."
            : $"호스트와의 연결이 종료되었습니다 ({shutdownReason}).";
        Debug.LogWarning($"<color=red>[Spawner]</color> NetworkRunner 비자발적 Shutdown 감지: {shutdownReason}.");
        ReturnToLobby(runner, msg);
    }

    public void OnConnectedToServer(NetworkRunner runner) { }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        if (BasicLauncher.IsVoluntaryExit) return;

        // 끊긴 채로 죽은 세션에 갇히지 않도록 안내 후 로비로 되돌린다.
        Debug.LogWarning($"<color=red>[Spawner]</color> 서버 연결이 끊겼습니다: {reason}.");
        ReturnToLobby(runner, $"호스트와의 연결이 끊어졌습니다 ({reason}).");
    }

    /// <summary>러너를 정리하고 로비 씬으로 돌아간다. 연결 끊김/종료 공용.</summary>
    private void ReturnToLobby(NetworkRunner runner, string reasonMessage = null)
    {
        if (BasicLauncher.Instance != null)
        {
            BasicLauncher.Instance.ForceReturnToLobby(reasonMessage);
            return;
        }

        if (_returningToLobby) return;
        _returningToLobby = true;

        Debug.Log($"<color=yellow>[Spawner]</color> ReturnToLobby 실행: {reasonMessage}");

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (runner != null)
        {
            try
            {
                if (runner.IsRunning) runner.Shutdown();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Spawner] Shutdown 예외 (무시): {ex.Message}");
            }

            if (runner != null && runner.gameObject != null)
            {
                Destroy(runner.gameObject);
            }
        }

        if (!string.IsNullOrEmpty(reasonMessage))
        {
            PlayerPrefs.SetString("DisconnectNotice", reasonMessage);
        }

        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene != lobbySceneName)
        {
            SceneManager.LoadScene(lobbySceneName);
        }
        else
        {
            _returningToLobby = false;
        }
    }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {
        if (runner != null && runner.IsServer)
        {
            string currentScene = SceneManager.GetActiveScene().name;
            if (!BasicLauncher.CanJoinInScene(currentScene))
            {
                request.Refuse();
            }
        }
    }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}