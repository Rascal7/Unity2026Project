using Fusion;
using UnityEngine;

public struct NetworkInputData : INetworkInput
{
    public Vector2 direction;
    public NetworkBool isJumpPressed; // 점프 버튼
    public NetworkBool isSkillPressed; // 스킬 버튼 (키보드 or 음성)
}
