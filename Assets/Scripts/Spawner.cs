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

    // 1. 기존 playerPrefab을 지우고 캐릭터별 프리팹 두 개로 나눕니다.
    public NetworkObject player1Prefab;
    public NetworkObject player2Prefab;

    private void Awake()
    {
        var objs = FindObjectsOfType<Spawner>();
        if (objs.Length > 1) { Destroy(gameObject); return; }
        DontDestroyOnLoad(gameObject);
    }

    private void Start() { RegisterCallbacks(); }

    private void RegisterCallbacks()
    {
        var runner = FindObjectOfType<NetworkRunner>();
        if (runner != null)
        {
            runner.RemoveCallbacks(this);
            runner.AddCallbacks(this);
            Debug.Log("<color=green>[Spawner]</color> Runner 콜백 등록 성공!");
        }
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        RegisterCallbacks();
        if (!runner.IsServer) return;

        string currentScene = SceneManager.GetActiveScene().name;

        if (currentScene == "CharacterSelect")
        {
            var oldItems = FindObjectsOfType<SessionPlayerItem>();
            foreach (var old in oldItems)
            {
                if (old.Object != null) runner.Despawn(old.Object);
            }

            foreach (var p in runner.ActivePlayers)
            {
                runner.Spawn(sessionItemPrefab, Vector3.zero, Quaternion.identity, p);
            }
        }
        else if (currentScene == "TestStage")
        {
            Debug.Log("<color=yellow>[Spawner]</color> 스테이지 로드 완료. 플레이어 소환 시작!");

            // [수정] ActivePlayers를 기준으로 루프를 돌려야 모든 플레이어가 스폰됩니다.
            foreach (var p in runner.ActivePlayers)
            {
                SpawnSelectedPlayer(runner, p);
            }
        }
    }

    // 통합된 스폰 함수
    private void SpawnSelectedPlayer(NetworkRunner runner, PlayerRef player)
    {
        int selectedIndex = 0;
        var items = FindObjectsOfType<SessionPlayerItem>();
        bool foundItem = false;

        foreach (var item in items)
        {
            if (item.Object.InputAuthority == player)
            {
                selectedIndex = item.SelectedCharacterIndex;
                foundItem = true;
                break;
            }
        }

        if (!foundItem)
        {
            // [안전장치] 만약 정보를 못 찾았다면 기본값 0으로 스폰하거나 로그를 남깁니다.
            Debug.LogWarning($"<color=red>[Spawn]</color> 플레이어 {player}의 선택 정보를 찾지 못해 기본 캐릭터로 소환합니다.");
        }

        // 0번 -> 1번프리팹, 1번(혹은 그외) -> 2번프리팹
        NetworkObject prefabToSpawn = (selectedIndex == 0) ? player1Prefab : player2Prefab;

        Vector3 spawnPos;
        // PlayerId는 1부터 시작하므로 1번이면 첫번째 좌표
        if (player.PlayerId % 2 == 1)
        {
            spawnPos = new Vector3(-20f, -7f, 0f);
        }
        else
        {
            spawnPos = new Vector3(-20f, -5f, 0f);
        }

        Debug.Log($"<color=green>[Spawn]</color> 플레이어 {player} (선택:{selectedIndex}) -> {spawnPos} 위치 소환");
        runner.Spawn(prefabToSpawn, spawnPos, Quaternion.identity, player);
    }

    // --- 기존 인터페이스 및 로직 유지 ---
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        // 1. 입력 데이터를 담을 구조체를 만듭니다. (기존에 쓰시던 구조체 이름 확인!)
        var data = new NetworkInputData();

        // 2. 키보드 입력 받기
        data.direction.x = Input.GetAxisRaw("Horizontal"); // A, D 키
        data.direction.y = Input.GetAxisRaw("Vertical");

        data.isJumpPressed = Input.GetButton("Jump");

        // 3. Fusion에 입력값 전달
        input.Set(data);
    }
    // ... (이하 동일한 인터페이스들)
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
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}