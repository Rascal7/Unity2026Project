using System;
using UnityEngine;

/// <summary>
/// 스킬 슬롯(1/2/3)에 대한 키보드 단축키를 저장/조회하는 전역 헬퍼.
/// - PlayerPrefs 에 "SkillKey1/2/3" 로 KeyCode(int) 를 저장한다.
/// - 기본값은 기존 하드코딩과 동일: 슬롯1=LeftShift, 슬롯2=LeftControl, 슬롯3=LeftAlt.
/// - 입력 판정(Spawner)과 HUD 라벨(SkillCooldownHUD)이 모두 이 값을 읽는다.
/// - 값이 바뀌면 OnChanged 로 알려 HUD 가 즉시 갱신하도록 한다.
/// </summary>
public static class SkillKeybinds
{
    public const int MinSlot = 1;
    public const int MaxSlot = 3;

    /// <summary>바인딩이 변경될 때 발생 (HUD 등이 라벨 갱신에 사용).</summary>
    public static event Action OnChanged;

    private static readonly KeyCode[] Defaults =
    {
        KeyCode.LeftShift,   // slot 1
        KeyCode.LeftControl, // slot 2
        KeyCode.LeftAlt,     // slot 3
    };

    private static string PrefKey(int slot) => "SkillKey" + slot;

    /// <summary>슬롯(1~3)에 배정된 키를 반환. 저장값이 없으면 기본값.</summary>
    public static KeyCode GetSlotKey(int slot)
    {
        if (slot < MinSlot || slot > MaxSlot) return KeyCode.None;
        int def = (int)Defaults[slot - 1];
        int stored = PlayerPrefs.GetInt(PrefKey(slot), def);
        return (KeyCode)stored;
    }

    /// <summary>슬롯 키를 저장하고 변경 이벤트를 발생시킨다.</summary>
    public static void SetSlotKey(int slot, KeyCode key)
    {
        if (slot < MinSlot || slot > MaxSlot) return;
        PlayerPrefs.SetInt(PrefKey(slot), (int)key);
        PlayerPrefs.Save();
        OnChanged?.Invoke();
    }

    /// <summary>모든 슬롯을 기본값으로 되돌린다.</summary>
    public static void ResetToDefaults()
    {
        for (int s = MinSlot; s <= MaxSlot; s++)
            PlayerPrefs.SetInt(PrefKey(s), (int)Defaults[s - 1]);
        PlayerPrefs.Save();
        OnChanged?.Invoke();
    }

    /// <summary>해당 키를 이미 쓰고 있는 다른 슬롯 번호를 반환(exceptSlot 제외). 없으면 0.</summary>
    public static int FindSlotUsing(KeyCode key, int exceptSlot)
    {
        for (int s = MinSlot; s <= MaxSlot; s++)
        {
            if (s == exceptSlot) continue;
            if (GetSlotKey(s) == key) return s;
        }
        return 0;
    }

    /// <summary>슬롯 키의 짧은 표시 라벨 (HUD 뱃지/설정 UI 공용).</summary>
    public static string GetLabel(int slot) => KeyLabel(GetSlotKey(slot));

    /// <summary>KeyCode 를 사람이 읽기 좋은 짧은 문자열로.</summary>
    public static string KeyLabel(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.LeftShift:
            case KeyCode.RightShift: return "Shift";
            case KeyCode.LeftControl:
            case KeyCode.RightControl: return "Ctrl";
            case KeyCode.LeftAlt:
            case KeyCode.RightAlt: return "Alt";
            case KeyCode.Space: return "Space";
            case KeyCode.Return: return "Enter";
            case KeyCode.Tab: return "Tab";
            case KeyCode.UpArrow: return "↑";
            case KeyCode.DownArrow: return "↓";
            case KeyCode.LeftArrow: return "←";
            case KeyCode.RightArrow: return "→";
            case KeyCode.None: return "-";
        }

        string s = key.ToString();
        // "Alpha1" -> "1", "Keypad1" -> "N1"
        if (s.StartsWith("Alpha")) return s.Substring(5);
        if (s.StartsWith("Keypad")) return "N" + s.Substring(6);
        return s; // 알파벳 키 등은 그대로 (예: "F", "Q")
    }
}
