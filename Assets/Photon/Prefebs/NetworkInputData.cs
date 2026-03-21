using Fusion;
using UnityEngine;

public struct NetworkInputData : INetworkInput
{
    public Vector2 direction;
    public NetworkBool isJumpPressed; // 점프 데이터 추가
}