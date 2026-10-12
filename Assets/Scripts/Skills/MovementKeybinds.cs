using UnityEngine;

/// <summary>
/// 이동/점프 조작 키를 저장/조회하는 전역 헬퍼 (스킬은 SkillKeybinds 담당).
/// - 각 액션은 기본(0)/보조(1) 두 개의 바인딩을 가진다. 예: 위 = W / ↑.
/// - PlayerPrefs 에 "MoveKey_{Action}_{slot}" 로 저장한다.
/// - 입력 판정(Spawner)과 설정 UI(SettingsMenuUI)가 이 값을 읽는다.
/// - 값이 바뀌면 OnChanged 로 알린다.
/// </summary>
public static class MovementKeybinds
{
    public enum Act { Up, Down, Left, Right, Jump }
    public const int SlotCount = 2; // 0=기본, 1=보조

    public static event System.Action OnChanged;

    private static readonly KeyCode[,] Defaults = new KeyCode[5, 2]
    {
        { KeyCode.W,     KeyCode.UpArrow    }, // Up
        { KeyCode.S,     KeyCode.DownArrow  }, // Down
        { KeyCode.A,     KeyCode.LeftArrow  }, // Left
        { KeyCode.D,     KeyCode.RightArrow }, // Right
        { KeyCode.Space, KeyCode.None       }, // Jump (보조 없음)
    };

    private static string Pref(Act a, int slot) => "MoveKey_" + a + "_" + slot;

    public static KeyCode Get(Act a, int slot)
    {
        if (slot < 0 || slot >= SlotCount) return KeyCode.None;
        int def = (int)Defaults[(int)a, slot];
        return (KeyCode)PlayerPrefs.GetInt(Pref(a, slot), def);
    }

    public static void Set(Act a, int slot, KeyCode key)
    {
        if (slot < 0 || slot >= SlotCount) return;
        PlayerPrefs.SetInt(Pref(a, slot), (int)key);
        PlayerPrefs.Save();
        OnChanged?.Invoke();
    }

    public static void ResetToDefaults()
    {
        for (int a = 0; a < 5; a++)
            for (int s = 0; s < SlotCount; s++)
                PlayerPrefs.SetInt(Pref((Act)a, s), (int)Defaults[a, s]);
        PlayerPrefs.Save();
        OnChanged?.Invoke();
    }

    /// <summary>해당 액션의 기본/보조 키 중 하나라도 눌려 있으면 true.</summary>
    public static bool Pressed(Act a)
    {
        var p = Get(a, 0);
        var s = Get(a, 1);
        return (p != KeyCode.None && Input.GetKey(p)) || (s != KeyCode.None && Input.GetKey(s));
    }

    public static string Label(Act a, int slot) => SkillKeybinds.KeyLabel(Get(a, slot));
}


