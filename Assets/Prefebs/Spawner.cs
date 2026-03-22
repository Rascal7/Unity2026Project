using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using UnityEngine;

public class Spawner : MonoBehaviour, INetworkRunnerCallbacks
{
    public NetworkPrefabRef playerPrefab; // GameObject 대신 NetworkPrefabRef 권장
    [SerializeField] private Dictionary<PlayerRef, NetworkObject> _spawnedCharacters = new Dictionary<PlayerRef, NetworkObject>();
    private NetworkRunner _runner;
    private bool _isInitialized;
    private void Start()
    {
        _runner = FindObjectOfType<NetworkRunner>();
        if (_runner != null)
        {
            _runner.AddCallbacks(this);
            Debug.Log("<color=yellow>[Spawner]</color> 등록 완료!");

            // [핵심] 이미 서버가 가동 중이고 플레이어(호스트)가 있다면 즉시 스폰
            if (_runner.IsServer)
            {
                foreach (var player in _runner.ActivePlayers)
                {
                    Debug.Log($"<color=orange>[Spawner]</color> 이미 접속 중인 {player} 발견, 스폰 시도!");
                    SpawnPlayer(_runner, player);
                }
            }
        }
        else
        {
            // 2. 만약 없다면, 잠시 기다렸다가 다시 찾거나 런처에서 직접 등록해줘야 합니다.
            Debug.LogError("<color=red>[Spawner]</color> NetworkRunner를 찾을 수 없습니다! 런처보다 먼저 실행되었을 수 있습니다.");
        }
    }

    // 캐릭터 생성 로직 분리
    private void SpawnPlayer(NetworkRunner runner, PlayerRef player)
    {
        // [중요] 이미 이 플레이어의 캐릭터가 딕셔너리에 있다면 생성을 취소합니다.
        if (_spawnedCharacters.ContainsKey(player))
        {
            Debug.LogWarning($"플레이어 {player}는 이미 캐릭터가 존재합니다.");
            return;
        }

        Debug.Log($"<color=cyan>[Spawner]</color> 플레이어 {player} 생성 실행");
        if (playerPrefab.IsValid == false)
        {
            Debug.LogError("Spawner에 Player Prefab이 할당되지 않았습니다! 인스펙터를 확인하세요.");
            return;
        }
        Vector3 spawnPosition = new Vector3(-20, -7, 0);
        Quaternion spawnRotation = Quaternion.identity;

        NetworkObject networkPlayerObject = runner.Spawn(playerPrefab, spawnPosition, spawnRotation, player);
        _spawnedCharacters.Add(player, networkPlayerObject);
    }
    public void OnConnectedToServer(NetworkRunner runner)
    {
        // 단순히 연결 성공 로그만 남기거나 비워둡니다.
        if (runner.IsServer)
        {
            Debug.Log("<color=green>[Spawner]</color> 서버/호스트로 연결되었습니다.");
        }
    }
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"<color=yellow>OnPlayerJoined 호출됨! 서버여부: {runner.IsServer}</color>");
        if (runner.IsServer)
        {
            SpawnPlayer(runner, player);
        }
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (runner.IsServer)
        {
            if (_spawnedCharacters.TryGetValue(player, out NetworkObject networkObject))
            {
                runner.Despawn(networkObject);
                _spawnedCharacters.Remove(player);
                Debug.Log($"<color=red>[Spawner]</color> 플레이어 {player} 퇴장 및 오브젝트 제거");
            }
        }
    }

    // --- 사용하지 않는 인터페이스 메서드들 (생략 가능하거나 기본 구현) ---
    // 참고: Fusion 2.0 이상에서는 필요한 메서드만 골라 쓰는 방식도 지원하지만, 
    // 현재 방식처럼 모두 구현해두는 것이 안전합니다.

    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        var data = new NetworkInputData();
        // 로컬의 빠른 업데이트 속도로 입력을 읽어 전송합니다.
        data.direction = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        data.isJumpPressed = Input.GetButton("Jump");

        data.isJumpPressed = Input.GetButton("Jump") || Input.GetKey(KeyCode.Space);

        input.Set(data);
    }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
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
}