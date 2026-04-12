using Fusion;
using UnityEngine;

public class PlayerSpawner : SimulationBehaviour, IPlayerJoined
{
    [Header("플레이어 프리팹 설정")]
    public NetworkObject player1Prefab; // 캐릭터 1번 프리팹 (인스펙터에서 할당)
    public NetworkObject player2Prefab; // 캐릭터 2번 프리팹 (인스펙터에서 할당)

    public void PlayerJoined(PlayerRef player)
    {
        // 1. 방장(서버)이 소환을 담당합니다.
        if (Runner.IsServer)
        {
            SpawnPlayer(player);
        }
    }

    private void SpawnPlayer(PlayerRef player)
    {
        // 2. 해당 플레이어의 SessionPlayerItem을 찾습니다.
        SessionPlayerItem playerInfo = null;
        foreach (var item in FindObjectsOfType<SessionPlayerItem>())
        {
            if (item.Object.InputAuthority == player)
            {
                playerInfo = item;
                break;
            }
        }

        // 3. 선택한 인덱스에 따라 프리팹 결정
        NetworkObject selectedPrefab = player1Prefab; // 기본값

        if (playerInfo != null)
        {
            // 인덱스가 0이면 프리팹 1, 그 외(1)면 프리팹 2
            selectedPrefab = (playerInfo.SelectedCharacterIndex == 0) ? player1Prefab : player2Prefab;
            Debug.Log($"플레이어 {player}는 {playerInfo.SelectedCharacterIndex}번을 골랐으므로 해당 프리팹을 생성합니다.");
        }

        // 4. 네트워크 상에 소환
        // 소환 위치는 적절히 Vector3로 지정하세요.
        Runner.Spawn(selectedPrefab, new Vector3(player.RawEncoded % 2 * 2, 1, 0), Quaternion.identity, player);
    }
}