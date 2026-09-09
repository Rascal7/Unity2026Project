using System;
using System.IO;
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

    /// <summary>
    /// 세이브 파일들이 저장되는 로컬 디렉토리 경로입니다.
    /// (Windows: %USERPROFILE%\AppData\LocalLow\DefaultCompany\2026UnityProject)
    /// </summary>
    public static string SaveDirectory => Application.persistentDataPath;

    public static int CurrentSlotIndex
    {
        get
        {
            string currentSlotFile = GetCurrentSlotFilePath();
            if (File.Exists(currentSlotFile))
            {
                try
                {
                    string text = File.ReadAllText(currentSlotFile).Trim();
                    if (int.TryParse(text, out int slot))
                    {
                        return Mathf.Clamp(slot, 1, 5);
                    }
                }
                catch { }
            }
            return PlayerPrefs.GetInt(CurrentSlotKey, 1);
        }
        set
        {
            int slot = Mathf.Clamp(value, 1, 5);
            PlayerPrefs.SetInt(CurrentSlotKey, slot);
            PlayerPrefs.Save();

            try
            {
                EnsureDirectoryExists();
                File.WriteAllText(GetCurrentSlotFilePath(), slot.ToString(), System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SaveDataManager] 현재 슬롯 번호 파일 저장 실패: {ex.Message}");
            }
        }
    }

    private static void EnsureDirectoryExists()
    {
        if (!Directory.Exists(SaveDirectory))
        {
            Directory.CreateDirectory(SaveDirectory);
        }
    }

    public static string GetSlotFilePath(int slotIndex)
    {
        slotIndex = Mathf.Clamp(slotIndex, 1, 5);
        return Path.Combine(SaveDirectory, $"save_slot_{slotIndex}.json");
    }

    private static string GetCurrentSlotFilePath()
    {
        return Path.Combine(SaveDirectory, "current_slot.txt");
    }

    public static SaveSlotData GetSlot(int slotIndex)
    {
        slotIndex = Mathf.Clamp(slotIndex, 1, 5);
        string filePath = GetSlotFilePath(slotIndex);

        // 1. 파일이 존재하는 경우 파일에서 로드
        if (File.Exists(filePath))
        {
            try
            {
                string json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
                var data = JsonUtility.FromJson<SaveSlotData>(json);
                if (data != null) return data;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveDataManager] 슬롯 {slotIndex} 파일 로드 실패: {ex.Message}");
            }
        }

        // 2. 파일이 없지만 기존 PlayerPrefs 레지스트리에 데이터가 남아있는 경우 (자동 마이그레이션)
        string legacyKey = SlotKeyPrefix + slotIndex;
        if (PlayerPrefs.HasKey(legacyKey))
        {
            try
            {
                string legacyJson = PlayerPrefs.GetString(legacyKey);
                var data = JsonUtility.FromJson<SaveSlotData>(legacyJson);
                if (data != null)
                {
                    // 파일로 저장하여 마이그레이션 완료
                    SaveSlot(data);
                    PlayerPrefs.DeleteKey(legacyKey);
                    PlayerPrefs.Save();
                    Debug.Log($"<color=lime>[SaveDataManager]</color> 슬롯 {slotIndex} 레지스트리 데이터를 파일로 자동 마이그레이션 완료!");
                    return data;
                }
            }
            catch { }
        }

        return new SaveSlotData(slotIndex);
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
        Debug.Log($"<color=lime>[SaveDataManager]</color> 슬롯 {slotIndex} 새 게임 생성 완료! (저장 위치: {GetSlotFilePath(slotIndex)})");
        return data;
    }

    public static void SaveSlot(SaveSlotData data)
    {
        if (data == null) return;
        data.slotIndex = Mathf.Clamp(data.slotIndex, 1, 5);
        data.updatedTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        try
        {
            EnsureDirectoryExists();
            string filePath = GetSlotFilePath(data.slotIndex);
            // 사람이 읽기 편하도록 prettyPrint=true 로 JSON 포맷팅 저장
            string json = JsonUtility.ToJson(data, true);

            File.WriteAllText(filePath, json, System.Text.Encoding.UTF8);

            // PlayerPrefs에도 호환성을 위해 저장
            string legacyKey = SlotKeyPrefix + data.slotIndex;
            PlayerPrefs.SetString(legacyKey, json);
            PlayerPrefs.Save();

            Debug.Log($"<color=cyan>[SaveDataManager]</color> 슬롯 {data.slotIndex} 파일 저장 완료: {filePath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SaveDataManager] 슬롯 {data.slotIndex} 파일 저장 실패: {ex.Message}");
        }
    }

    public static void DeleteSlot(int slotIndex)
    {
        slotIndex = Mathf.Clamp(slotIndex, 1, 5);
        string filePath = GetSlotFilePath(slotIndex);

        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            string legacyKey = SlotKeyPrefix + slotIndex;
            PlayerPrefs.DeleteKey(legacyKey);
            PlayerPrefs.Save();

            Debug.Log($"<color=yellow>[SaveDataManager]</color> 슬롯 {slotIndex} 파일 삭제 완료: {filePath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SaveDataManager] 슬롯 {slotIndex} 삭제 실패: {ex.Message}");
        }
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

    /// <summary>
    /// 세이브 파일이 위치한 폴더를 윈도우 파일 탐색기(Explorer)로 직접 엽니다.
    /// </summary>
    public static void OpenSaveFolder()
    {
        EnsureDirectoryExists();
        string path = SaveDirectory.Replace('/', '\\');
        System.Diagnostics.Process.Start("explorer.exe", path);
    }
}
