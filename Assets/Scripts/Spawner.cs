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

    // 인게임 스테이지로 취급할 씬 이름들 (여기에 새 스테이지 추가하면 됨)
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
            Debug.Log("<color=green>[Spawner]</color> 콜백 등록 완료!");
        }
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        RegisterCallbacks();
        if (!runner.IsServer) return;

        string currentScene = SceneManager.GetActiveScene().name;

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
        // 2. 인게임 스테이지 (Tutorial, Stage1, Stage2 공통 처리)
        else if (IsInGameStage(currentScene))
        {
            Debug.Log($"<color=yellow>[Spawner]</color> 씬 로드 완료: {currentScene}. 플레이어 스폰 시작!");

            // 네트워크 타이머 생성 + 시작 (호스트만, 스테이지마다 새로 생성됨)
            if (NetworkTimer.Instance == null)
            {
                var timerPrefab = Resources.Load<NetworkObject>("NetworkTimer");
                if (timerPrefab != null)
                {
                    var timerObj = runner.Spawn(timerPrefab);
                    var nt = timerObj != null ? timerObj.GetComponent<NetworkTimer>() : null;
                    if (nt != null) nt.StartTimer();
                    Debug.Log("<color=orange>[Timer]</color> NetworkTimer 생성 및 시작 완료!");
                }
                else
                {
                    Debug.LogError("<color=red>[Spawner]</color> Resources/NetworkTimer 찾을 수 없습니다! 로컬 타이머로 대체합니다.");
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

        // StartPoint 오브젝트 찾기
        Vector3 spawnPos = new Vector3(-20f, -7f, 0f); // 기본값
        GameObject startPoint = GameObject.Find("StartPoint");

        if (startPoint != null)
        {
            spawnPos = startPoint.transform.position;
            // 플레이어끼리 겹치지 않게 살짝 이동
            if (player.PlayerId % 2 == 0) spawnPos.x += 1.5f;
        }

        NetworkObject prefabToSpawn = (selectedIndex == 0) ? player1Prefab : player2Prefab;

        Debug.Log($"<color=green>[Spawn]</color> 플레이어 {player} (인덱스:{selectedIndex}) 스폰 완료");
        runner.Spawn(prefabToSpawn, spawnPos, Quaternion.identity, player);
        // 타이머는 OnSceneLoadDone 에서 NetworkTimer 로 처리한다.
    }

    // --- 입력 처리 ---
    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        var data = new NetworkInputData();

        data.direction.x = Input.GetAxisRaw("Horizontal");
        data.direction.y = Input.GetAxisRaw("Vertical");
        data.isJumpPressed = Input.GetButton("Jump") || Input.GetKey(KeyCode.Space);

        // 스킬 입력: 키보드(LeftShift) 또는 음성 키워드 인식
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
                Debug.Log($"<color=red>[Spawner]</color> 플레이어 {player} 퇴장, 캐릭터 제거");
            }
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
        // 2. 인게임 스테이지에서 중간 참가한 경우 (Tutorial, Stage1, Stage2 공통 처리)
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