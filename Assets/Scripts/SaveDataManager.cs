using System;
using UnityEngine;

[Serializable]
public class SaveSlotData
{
    public int slotIndex = 1;
    public bool hasData = false;
    public string stageName = "TutorialStage";
    public string stageDisplayName = "튜토리얼 (Tutorial)";
    public string updatedTime = "";
    public float totalPlayTime = 0f;

    public SaveSlotData() { }

    public SaveSlotData(int slot)
    {
        slotIndex = slot;
        hasData = false;
        stageName = "TutorialStage";
        stageDisplayName = "튜토리얼 (Tutorial)";
        updatedTime = "";
        totalPlayTime = 0f;
    }
}

public static class SaveDataManager
{
    private const string CurrentSlotKey = "SaveData_CurrentSlot";
    private const string SlotKeyPrefix = "SaveData_Slot_";

    public static int CurrentSlotIndex
    {
        get => PlayerPrefs.GetInt(CurrentSlotKey, 1);
        set => PlayerPrefs.SetInt(CurrentSlotKey, Mathf.Clamp(value, 1, 5));
    }

    public static SaveSlotData GetSlot(int slotIndex)
    {
        slotIndex = Mathf.Clamp(slotIndex, 1, 5);
        string key = SlotKeyPrefix + slotIndex;
        if (!PlayerPrefs.HasKey(key))
        {
            return new SaveSlotData(slotIndex);
        }

        try
        {
            string json = PlayerPrefs.GetString(key);
            var data = JsonUtility.FromJson<SaveSlotData>(json);
            if (data == null) return new SaveSlotData(slotIndex);
            return data;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SaveDataManager] 슬롯 {slotIndex} 로드 실패: {ex.Message}");
            return new SaveSlotData(slotIndex);
        }
    }

    public static bool HasSlotData(int slotIndex)
    {
        var slot = GetSlot(slotIndex);
        return slot != null && slot.hasData;
    }

    public static SaveSlotData CreateNewGame(int slotIndex)
    {
        slotIndex = Mathf.Clamp(slotIndex, 1, 5);
        var data = new SaveSlotData(slotIndex)
        {
            hasData = true,
            stageName = "TutorialStage",
            stageDisplayName = GetStageDisplayName("TutorialStage"),
            updatedTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            totalPlayTime = 0f
        };

        SaveSlot(data);
        CurrentSlotIndex = slotIndex;
        Debug.Log($"<color=lime>[SaveDataManager]</color> 슬롯 {slotIndex} 새 게임 생성 완료!");
        return data;
    }

    public static void SaveSlot(SaveSlotData data)
    {
        if (data == null) return;
        data.slotIndex = Mathf.Clamp(data.slotIndex, 1, 5);
        data.updatedTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        string key = SlotKeyPrefix + data.slotIndex;
        string json = JsonUtility.ToJson(data);
        PlayerPrefs.SetString(key, json);
        PlayerPrefs.Save();
    }

    public static void DeleteSlot(int slotIndex)
    {
        slotIndex = Mathf.Clamp(slotIndex, 1, 5);
        string key = SlotKeyPrefix + slotIndex;
        PlayerPrefs.DeleteKey(key);
        PlayerPrefs.Save();
        Debug.Log($"<color=yellow>[SaveDataManager]</color> 슬롯 {slotIndex} 삭제 완료");
    }

    public static void UpdateProgress(string nextSceneName, float addPlayTime = 0f)
    {
        int slot = CurrentSlotIndex;
        var data = GetSlot(slot);
        if (!data.hasData)
        {
            data.hasData = true;
            data.slotIndex = slot;
        }

        data.stageName = nextSceneName;
        data.stageDisplayName = GetStageDisplayName(nextSceneName);
        data.totalPlayTime += addPlayTime;
        SaveSlot(data);
        Debug.Log($"<color=cyan>[SaveDataManager]</color> 슬롯 {slot} 진행도 업데이트: {data.stageDisplayName} ({data.stageName})");
    }

    public static string GetStageDisplayName(string stageName)
    {
        switch (stageName)
        {
            case "TutorialStage":
                return "튜토리얼 (Tutorial)";
            case "Main Stage1":
                return "스테이지 1 (Stage 1)";
            case "Main Stage2":
                return "스테이지 2 (Stage 2)";
            case "TestStage":
                return "테스트 스테이지 (Test Stage)";
            default:
                return string.IsNullOrEmpty(stageName) ? "튜토리얼 (Tutorial)" : stageName;
        }
    }
}
