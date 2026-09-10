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
        if (_spawnedCharacters.ContainsKey(player)) return;
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
            runner.Spawn(rpf, rp, Quaternion.identity, player);
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
        runner.Spawn(prefabToSpawn, spawnPos, Quaternion.identity, player);
        // ?�?�머??OnSceneLoadDone ?�서 NetworkTimer �?처리?�다.
    }

    // --- ?�력 처리 ---
    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        var data = new NetworkInputData();

        data.direction.x = Input.GetAxisRaw("Horizontal");
        data.direction.y = Input.GetAxisRaw("Vertical");
        data.isJumpPressed = Input.GetButton("Jump") || Input.GetKey(KeyCode.Space);

        // ?�킬 ?�력: ?�보??LeftShift) ?�는 ?�성 ?�워???�식
        data.isSkillPressed = Input.GetKey(KeyCode.LeftShift) || VoiceSkillInput.IsSkillRequested;

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