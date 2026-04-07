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

        // --- 기존 로직: 캐릭터 선택 씬 ---
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

        // --- 추가된 로직: 테스트 스테이지 씬 캐릭터 소환 ---
        else if (currentScene == "TestStage")
        {
            Debug.Log("<color=yellow>[Spawner]</color> TestStage 로드 완료. 플레이어 소환 시작!");

            foreach (var p in runner.ActivePlayers)
            {
                SpawnSelectedPlayer(runner, p);
            }
        }
    }

    private void SpawnSelectedPlayer(NetworkRunner runner, PlayerRef player)
    {
        // 1. 해당 플레이어의 SessionPlayerItem 찾기
        int selectedIndex = 0;
        var items = FindObjectsOfType<SessionPlayerItem>();
        foreach (var item in items)
        {
            if (item.Object.InputAuthority == player)
            {
                selectedIndex = item.SelectedCharacterIndex;
                break;
            }
        }

        // 2. 선택 번호에 따라 프리팹 결정 (0번 -> 1번프리팹, 그외 -> 2번프리팹)
        NetworkObject prefabToSpawn = (selectedIndex == 0) ? player1Prefab : player2Prefab;

        // 3. 소환 위치 (플레이어별로 조금 다르게)
        Vector3 spawnPos = new Vector3(player.RawEncoded * 2, 1, 0);

        Debug.Log($"<color=green>[Spawn]</color> 플레이어 {player} (선택:{selectedIndex}) 소환 완료!");
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