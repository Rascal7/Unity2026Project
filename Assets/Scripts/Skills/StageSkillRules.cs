using System;
using UnityEngine.SceneManagement;

/// <summary>
/// 스테이지별로 사용할 수 있는 스킬 슬롯 수.
/// - 튜토리얼: 0 (스킬 사용 불가)
/// - Main Stage1: 슬롯 1 까지
/// - Main Stage2: 슬롯 2 까지
/// - Main Stage3 및 그 외(테스트 씬 등): 슬롯 3 전부
/// PlayerMovement(발동 판정)와 SkillCooldownHUD(슬롯 표시 개수)가 같은 규칙을 쓴다.
/// </summary>
public static class StageSkillRules
{
    public const int MaxSlots = 3;

    public static int MaxSkillSlots(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return MaxSlots;
        if (sceneName.IndexOf("Tutorial", StringComparison.OrdinalIgnoreCase) >= 0) return 0;
        if (sceneName.Equals("Main Stage1", StringComparison.OrdinalIgnoreCase)) return 1;
        if (sceneName.Equals("Main Stage2", StringComparison.OrdinalIgnoreCase)) return 2;
        return MaxSlots;
    }

    public static int CurrentMaxSkillSlots => MaxSkillSlots(SceneManager.GetActiveScene().name);

    public static bool IsSlotAllowed(int slot) => slot >= 1 && slot <= CurrentMaxSkillSlots;
}
