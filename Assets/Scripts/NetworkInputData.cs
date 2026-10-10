using Fusion;
using UnityEngine;

// 버튼 입력은 "누르고 있는 상태"로 보내고, 받는 쪽(PlayerMovement)이 이전 틱과 비교해
// "눌린 순간"만 골라낸다. (GetKey 그대로 쓰면 꾹 누를 때 착지/쿨다운 종료마다 재발동한다)
public enum InputButton
{
    Jump   = 0,
    Skill1 = 1, // 챕터 스킬 슬롯 1/2/3 (키보드/음성 공용)
    Skill2 = 2,
    Skill3 = 3,
}

public struct NetworkInputData : INetworkInput
{
    public Vector2 direction;
    public NetworkButtons buttons;
}
