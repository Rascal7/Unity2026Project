using Fusion;
using UnityEngine;

public class SessionPlayerItem : NetworkBehaviour
{
    // 모든 플레이어에게 동기화될 변수들
    [Networked] public int SelectedCharacterIndex { get; set; } = -1; // -1은 미선택
    [Networked] public NetworkBool IsReady { get; set; }
    [Networked] public NetworkString<_16> PlayerName { get; set; }

    public override void Spawned()
    {
        // 이 데이터 객체의 주인(Input Authority)이 나일 때만 닉네임 설정
        if (Object.HasInputAuthority)
        {
            string name = PlayerPrefs.GetString("PlayerName", "Unknown");
            RPC_SetPlayerInfo(name);
        }

        // 씬 이동 시 파괴되지 않게 설정 (선택창 정보를 게임 씬까지 가져가야 함)
        DontDestroyOnLoad(gameObject);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetPlayerInfo(string name)
    {
        PlayerName = name;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SelectCharacter(int index)
    {
        SelectedCharacterIndex = index;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetReady(NetworkBool ready)
    {
        IsReady = ready;
    }
}