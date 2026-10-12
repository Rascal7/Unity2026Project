// 스킬 표시 정보(이름/심볼)의 단일 소스. HUD와 스테이지 인트로 연출이 공용으로 쓴다.
public static class SkillInfo
{
    public static string Name(PlayerMovement.CharacterSkill skill)
    {
        switch (skill)
        {
            case PlayerMovement.CharacterSkill.FoxDash:        return "대시";
            case PlayerMovement.CharacterSkill.FoxSniff:       return "킁킁";
            case PlayerMovement.CharacterSkill.FoxShadow:      return "그림자";
            case PlayerMovement.CharacterSkill.RabbitCarrot:   return "당근";
            case PlayerMovement.CharacterSkill.RabbitJump:     return "도약";
            case PlayerMovement.CharacterSkill.RabbitStomp:    return "스톰프";
            case PlayerMovement.CharacterSkill.FoxSwap:        return "체인지";
            case PlayerMovement.CharacterSkill.RabbitPowerUp:  return "파워업";
            default: return "";
        }
    }

    public static string Symbol(PlayerMovement.CharacterSkill skill)
    {
        switch (skill)
        {
            case PlayerMovement.CharacterSkill.FoxDash:        return ">>";
            case PlayerMovement.CharacterSkill.FoxSniff:       return "!?";
            case PlayerMovement.CharacterSkill.FoxShadow:      return "◈";
            case PlayerMovement.CharacterSkill.RabbitCarrot:   return "▲";
            case PlayerMovement.CharacterSkill.RabbitJump:     return "⇑";
            case PlayerMovement.CharacterSkill.RabbitStomp:    return "⇓";
            case PlayerMovement.CharacterSkill.FoxSwap:        return "<>";
            case PlayerMovement.CharacterSkill.RabbitPowerUp:  return "UP";
            default: return "";
        }
    }
}
