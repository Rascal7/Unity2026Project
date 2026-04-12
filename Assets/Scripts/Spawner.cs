using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

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

<<<<<<< Updated upstream
            
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
        Debug.Log($"<color=white>[Spawner]</color> runner.Spawn 실행 직전! 플레이어: {player}</color>");
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
        Quaternion spawnRotation = Quaternion.identity;

        if (_spawnedCharacters.ContainsKey(player)) return;

        // 씬별로 스폰 위치를 다르게 하고 싶다면 여기서 씬 이름을 체크할 수 있습니다.
        // string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        Vector3 spawnPosition = new Vector3(-20, -7, 0);

        NetworkObject networkPlayerObject = runner.Spawn(playerPrefab, spawnPosition, Quaternion.identity, player);
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
        Debug.Log($"<color=cyan>[Spawner]</color> 플레이어 {player} 접속 확인!");

        // 호스트(서버)이고, 현재 로비가 아닌 게임 스테이지라면 즉시 스폰 시도
        if (runner.IsServer && SceneManager.GetActiveScene().name != "Lobby")
        {
            // 이미 딕셔너리에 있다면 중복 생성 방지
            if (_spawnedCharacters.ContainsKey(player)) return;

            Debug.Log($"<color=white>[Spawner]</color> 씬 로딩 전 접속한 {player}를 위해 즉시 스폰합니다.");
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
=======
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
        else if (currentScene == "Test Stage")
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
        Vector3 spawnPos = Vector3.zero; // 기본값
        GameObject startPoint = GameObject.Find("StartPoint"); // 이름으로 찾기

        if (startPoint != null)
        {
            spawnPos = startPoint.transform.position;

            // 두 명이 겹치지 않게 플레이어 아이디에 따라 약간의 간격을 줍니다.
            if (player.PlayerId % 2 == 0)
            {
                spawnPos.x += 1.5f; // 2번 플레이어는 오른쪽으로 1.5만큼 이동
            }
        }
        else
        {
            // StartPoint를 못 찾았을 때를 대비한 예외 처리
            Debug.LogWarning("<color=red>[Spawn]</color> StartPoint 오브젝트를 찾을 수 없습니다! 기본 좌표를 사용합니다.");
            spawnPos = new Vector3(-20f, -7f, 0f);
        }

        // 0번 -> 1번프리팹, 1번(혹은 그외) -> 2번프리팹
        NetworkObject prefabToSpawn = (selectedIndex == 0) ? player1Prefab : player2Prefab;

        Debug.Log($"<color=green>[Spawn]</color> 플레이어 {player} (선택:{selectedIndex}) -> {spawnPos} 위치 소환");
        runner.Spawn(prefabToSpawn, spawnPos, Quaternion.identity, player);
>>>>>>> Stashed changes
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
    public void OnSceneLoadDone(NetworkRunner runner)
    {
        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene == "Lobby") return;

        if (runner.IsServer)
        {
            // [진단용 로그 추가] 현재 접속자 수를 확인합니다.
            int playerCount = 0;
            foreach (var p in runner.ActivePlayers) playerCount++;

            Debug.Log($"<color=orange>[Spawner]</color> 현재 감지된 ActivePlayers 수: {playerCount}");

            // 1. 만약 ActivePlayers가 비어있다면, 로컬 플레이어라도 강제로 소환 시도
            if (playerCount == 0)
            {
                Debug.LogWarning("[Spawner] ActivePlayers가 비어있어 LocalPlayer로 소환을 시도합니다.");
                SpawnPlayer(runner, runner.LocalPlayer);
                return;
            }

            // 2. 정상적인 루프 처리
            foreach (var player in runner.ActivePlayers)
            {
                Debug.Log($"<color=white>[Spawner]</color> 루프 내부 - 현재 플레이어: {player}</color>");

                if (_spawnedCharacters.ContainsKey(player))
                {
                    Debug.LogWarning($"[Spawner] {player}는 이미 딕셔너리에 존재하여 스킵합니다.");
                    continue;
                }

                SpawnPlayer(runner, player);
            }
        }
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

    public void OnSceneLoadStart(NetworkRunner runner)
    {
        // 씬 이동이 시작되면 기존 스폰 정보를 초기화합니다.
        _spawnedCharacters.Clear();
        Debug.Log("<color=white>[Spawner]</color> 씬 이동 시작 - 딕셔너리 초기화 완료");
    }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}