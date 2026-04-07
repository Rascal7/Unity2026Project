using Fusion;
using UnityEngine;

public class SessionPlayerItem : NetworkBehaviour
{
    [Networked] public int SelectedCharacterIndex { get; set; } = -1;
    [Networked] public NetworkBool IsReady { get; set; }

    public override void Spawned()
    {
        Debug.Log($"<color=yellow>[Item]</color> SessionPlayerItem 생성됨! 주인: {Object.InputAuthority}");
        DontDestroyOnLoad(gameObject);

    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SelectCharacter(int index)
    {
        // 서버(Host)가 이 값을 받아서 네트워크 변수를 갱신합니다.
        Debug.Log($"서버에서 {index}번 캐릭터 선택됨!");
        SelectedCharacterIndex = index;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetReady(NetworkBool ready)
    {
        Debug.Log($"<color=orange>[Server]</color> 플레이어 {Object.InputAuthority} 레디 상태 변경: {ready}");
        IsReady = ready;
    }
}