using Fusion;
using UnityEngine;

public struct NetworkInputData : INetworkInput
{
    public Vector2 direction;
    public NetworkBool isJumpPressed; // 점프 버튼
    public byte skillSlot;            // 0=없음, 1/2/3 = 챕터 스킬 슬롯 (키보드/음성 공용)
}
