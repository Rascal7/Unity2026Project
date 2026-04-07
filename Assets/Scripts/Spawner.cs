using Fusion;
using Fusion.Sockets; // [추가] NetAddress, NetDisconnectReason 등을 위해 필수!
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Spawner : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Prefabs")]
    public NetworkObject sessionItemPrefab; // 캐릭터 선택 데이터용
    public NetworkObject playerPrefab;      // 실제 게임 캐릭터용

    private void Start()
    {
        // Runner를 찾아서 콜백 등록
        var runner = FindObjectOfType<NetworkRunner>();
        if (runner != null)
        {
            runner.AddCallbacks(this);
            Debug.Log("<color=green>[Spawner]</color> Runner에 콜백 등록 성공!");
        }
    }

    // --- 플레이어 접속 시 실행 ---
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (!runner.IsServer) return;

        string currentScene = SceneManager.GetActiveScene().name;
        Debug.Log($"<color=cyan>[Spawner]</color> {player} 접속! 현재 씬: {currentScene}");

        // 캐릭터 선택창이면 데이터 주머니 생성
        if (currentScene == "CharacterSelect")
        {
            SpawnSessionItem(runner, player);
        }
    }

    // --- 씬 로딩 완료 시 실행 ---
    public void OnSceneLoadDone(NetworkRunner runner)
    {
        if (!runner.IsServer) return; // 호스트만 소환 가능

        if (SceneManager.GetActiveScene().name == "CharacterSelect")
        {
            foreach (var p in runner.ActivePlayers) // p는 접속 중인 플레이어들
            {
                Debug.Log($"[Spawner] 이미 접속 중인 {p}를 위해 아이템 생성");
                SpawnSessionItem(runner, p); // ★ 여기서 p를 넘겨줘야 함!
            }
        }
    }

    private void SpawnSessionItem(NetworkRunner runner, PlayerRef player)
    {
        if (sessionItemPrefab == null || player == PlayerRef.None) return;

        // [중복 방지] 이미 이 플레이어의 아이템이 씬에 있는지 확인
        var existItems = FindObjectsOfType<SessionPlayerItem>();
        foreach (var item in existItems)
        {
            if (item.Object != null && item.Object.InputAuthority == player)
            {
                Debug.Log($"<color=white>[Spawner]</color> {player}의 아이템이 이미 존재함. 스킵!");
                return;
            }
        }

        Debug.Log($"<color=yellow>[Spawner]</color> {player} 아이템 소환!");
        runner.Spawn(sessionItemPrefab, Vector3.zero, Quaternion.identity, player);
    }
    private void Awake()
    {
        // Spawner가 씬이 바뀌어도 파괴되지 않게 하려면 Awake에서 설정
        DontDestroyOnLoad(gameObject);
    }

    // --- 인터페이스 필수 구현 (오류 방지를 위해 정확한 타입 사용) ---
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
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