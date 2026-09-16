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
    private Dictionary<PlayerRef, NetworkObject> _spawnedCharacters = new Dictionary<PlayerRef, NetworkObject>();

    // Phase2: resume-from-checkpoint info for the current stage load (host only, set in OnSceneLoadDone).
    private Vector3? _resumeSpawnPos = null;
    private float _resumeTimerOffset = 0f;

    // ?�게???�테?��?�?취급?????�름??(?�기?????�테?��? 추�??�면 ??
    private static readonly string[] InGameStageNames = { "TutorialStage", "Main Stage1", "Main Stage2" };

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
            Debug.Log("<color=green>[Spawner]</color> 콜백 ?�록 ?�료!");
        }
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        RegisterCallbacks();
        if (!runner.IsServer) return;

        string currentScene = SceneManager.GetActiveScene().name;

        // 씬이 바뀌면 이전 씬의 NetworkObject 는 모두 Despawn 된다.
        // 여기서 비워두지 않으면 다음 스테이지에서 스폰 가드에 걸려 캐릭터가 안 나온다.
        _spawnedCharacters.Clear();

        // 1. 캐릭???�택 ??
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
        // 2. ?�게???�테?��? (Tutorial, Stage1, Stage2 공통 처리)
        else if (IsInGameStage(currentScene))
        {
            Debug.Log($"<color=yellow>[Spawner]</color> ??로드 ?�료: {currentScene}. ?�레?�어 ?�폰 ?�작!");

            // ?�트?�크 ?�?�머 ?�성 + ?�작 (?�스?�만, ?�테?��?마다 ?�로 ?�성??
            // 랭킹 리더보드를 미리 캐시해 둔다 (클리어 시 New Record 즉시 판정용).
            RankingManager.PrefetchLeaderboard();

            // Phase2: resume info (checkpoint pos + timer offset) from the save slot, if any.
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
                    Debug.Log($"<color=orange>[Spawner]</color> resume checkpoint @ ({slot.checkpointX:0.0},{slot.checkpointY:0.0}) offset {slot.stageElapsed:0.00}s");
                }
            }

            if (currentScene == "TutorialStage")
            {
                RunManager.ResetRun(); // �� �� ����: ���� �ʱ�ȭ (Ʃ�丮���� Ÿ�̸� ����)
            }
            else if (NetworkTimer.Instance == null)
            {
                var timerPrefab = Resources.Load<NetworkObject>("NetworkTimer");
                if (timerPrefab != null)
                {
                    var timerObj = runner.Spawn(timerPrefab);
                    var nt = timerObj != null ? timerObj.GetComponent<NetworkTimer>() : null;
                    if (nt != null) nt.StartWithIntro(2f, _resumeTimerOffset);
                    Debug.Log("<color=orange>[Timer]</color> NetworkTimer ?�성 �??�작 ?�료!");
                }
                else
                {
                    Debug.LogError("<color=red>[Spawner]</color> Resources/NetworkTimer 찾을 ???�습?�다! 로컬 ?�?�머�??�체합?�다.");
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

        // StartPoint ?�브?�트 찾기
        Vector3 spawnPos = new Vector3(-20f, -7f, 0f); // 기본�?
        // Phase2: resume at the saved checkpoint position when loading mid-stage.
        if (_resumeSpawnPos.HasValue)
        {
            Vector3 rp = _resumeSpawnPos.Value;
            if (player.PlayerId % 2 == 0) rp.x += 1.5f;
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
            // ?�레?�어?�리 겹치지 ?�게 ?�짝 ?�동
            if (player.PlayerId % 2 == 0) spawnPos.x += 1.5f;
        }

        NetworkObject prefabToSpawn = (selectedIndex == 0) ? player1Prefab : player2Prefab;

        Debug.Log($"<color=green>[Spawn]</color> ?�레?�어 {player} (?�덱??{selectedIndex}) ?�폰 ?�료");
        var spawned = runner.Spawn(prefabToSpawn, spawnPos, Quaternion.identity, player);
        if (spawned != null) _spawnedCharacters[player] = spawned;
        // ?�?�머??OnSceneLoadDone ?�서 NetworkTimer �?처리?�다.
    }

    // --- ?�력 처리 ---
    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        var data = new NetworkInputData();

        data.direction.x = Input.GetAxisRaw("Horizontal");
        data.direction.y = Input.GetAxisRaw("Vertical");
        data.isJumpPressed = Input.GetButton("Jump") || Input.GetKey(KeyCode.Space);

        // 스킬 슬롯 입력: 키보드(Shift=1, Ctrl=2, Alt=3) 또는 음성(키워드→슬롯)
        byte slot = 0;
        if (Input.GetKey(KeyCode.LeftShift)) slot = 1;
        else if (Input.GetKey(KeyCode.LeftControl)) slot = 2;
        else if (Input.GetKey(KeyCode.LeftAlt)) slot = 3;
        if (slot == 0) slot = (byte)VoiceSkillInput.RequestedSlot;
        data.skillSlot = slot;

        input.Set(data);
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (runner.IsServer)
        {
            if (_spawnedCharacters.TryGetValue(player, out NetworkObject networkObject))
            {
                if (networkObject != null) runner.Despawn(networkObject);
                _spawnedCharacters.Remove(player);
                Debug.Log($"<color=red>[Spawner]</color> ?�레?�어 {player} ?�장, 캐릭???�거");
            }
        }
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (!runner.IsServer) return;

        string currentScene = SceneManager.GetActiveScene().name;
        Debug.Log($"<color=cyan>[Spawner]</color> ?�레?�어 {player} 참�?! ?�재 ?? {currentScene}");

        // 1. 캐릭???�택 ?�에?????�레?�어가 ?�어??경우
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
                Debug.Log($"<color=lime>[Spawner]</color> 참�????�레?�어 {player}�??�한 SessionPlayerItem ?�성 ?�료!");
                runner.Spawn(sessionItemPrefab, Vector3.zero, Quaternion.identity, player);
            }
        }
        // 2. ?�게???�테?��??�서 중간 참�???경우 (Tutorial, Stage1, Stage2 공통 처리)
        else if (IsInGameStage(currentScene))
        {
            if (!_spawnedCharacters.ContainsKey(player))
            {
                SpawnSelectedPlayer(runner, player);
            }
        }
    }

    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
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