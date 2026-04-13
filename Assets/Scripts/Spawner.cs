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

    private void Awake()
    {
        // 중복 생성 방지 및 파괴 방지
        var objs = FindObjectsOfType<Spawner>();
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
        var runner = FindObjectOfType<NetworkRunner>();
        if (runner != null)
        {
            runner.RemoveCallbacks(this);
            runner.AddCallbacks(this);
            Debug.Log("<color=green>[Spawner]</color> 콜백 등록 성공!");
        }
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        RegisterCallbacks();
        if (!runner.IsServer) return;

        string currentScene = SceneManager.GetActiveScene().name;

        // 1. 캐릭터 선택 씬인 경우
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
        // 2. 게임 스테이지 씬인 경우 (띄어쓰기 주의: TestStage)
        else if (currentScene == "TutorialStage")
        {
            Debug.Log("<color=yellow>[Spawner]</color> 스테이지 로드 완료. 플레이어 소환!");

            foreach (var p in runner.ActivePlayers)
            {
                SpawnSelectedPlayer(runner, p);
            }
        }
    }

    private void SpawnSelectedPlayer(NetworkRunner runner, PlayerRef player)
    {
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

        // StartPoint 오브젝트 찾기
        Vector3 spawnPos = new Vector3(-20f, -7f, 0f); // 기본값
        GameObject startPoint = GameObject.Find("StartPoint");

        if (startPoint != null)
        {
            spawnPos = startPoint.transform.position;
            // 플레이어끼리 겹치지 않게 간격 조정
            if (player.PlayerId % 2 == 0) spawnPos.x += 1.5f;
        }

        // 선택한 인덱스에 따라 프리팹 결정
        NetworkObject prefabToSpawn = (selectedIndex == 0) ? player1Prefab : player2Prefab;

        Debug.Log($"<color=green>[Spawn]</color> 플레이어 {player} (선택:{selectedIndex}) 소환 완료");
        runner.Spawn(prefabToSpawn, spawnPos, Quaternion.identity, player);
    }

    // --- 입력 전달 (점프, 사다리 이동 핵심) ---
    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        var data = new NetworkInputData();

        data.direction.x = Input.GetAxisRaw("Horizontal");
        data.direction.y = Input.GetAxisRaw("Vertical"); // W, S 키 (사다리)
        data.isJumpPressed = Input.GetButton("Jump") || Input.GetKey(KeyCode.Space);

        input.Set(data);
    }

    // --- 필수 인터페이스 구현 (비워둠) ---
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
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