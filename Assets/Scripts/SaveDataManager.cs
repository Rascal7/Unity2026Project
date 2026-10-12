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
    public float totalPlayTime = 0f;      // 이전 스테이지들의 누적 기록 (완료한 구간 합)

    // 완료한 스테이지들의 개별 기록(초) 목록. 이어하기 후 최종 결과의 "스테이지별 나열"에 사용.
    public float[] segmentTimes = new float[0];
    // 위 구간들의 스테이지 번호(병렬). 라벨을 실제 스테이지로 표기하기 위함.
    public int[] segmentStages = new int[0];
    // 완료한 스테이지들의 개별 구슬 조각 수집 수 및 누적 조각 수
    public int[] segmentShards = new int[0];
    public int totalCollectedShards = 0;

    // Phase2: 현재 스테이지 중간 체크포인트 이어하기용
    public float stageElapsed = 0f;       // 마지막 체크포인트 시점의 현재 스테이지 경과시간
    public float checkpointX = 0f;
    public float checkpointY = 0f;
    public bool hasCheckpoint = false;    // 현재 스테이지에 저장된 체크포인트가 있는지

    // 무결성 검증용 HMAC-SHA256 서명 (메모장 등 외부 변조 방지)
    public string hash = "";
    [NonSerialized] public bool isCorrupted = false; // 런타임 변조 감지 플래그

    public SaveSlotData() { }

    public SaveSlotData(int slot)
    {
        slotIndex = slot;
        hasData = false;
        stageName = "TutorialStage";
        stageDisplayName = "튜토리얼 (Tutorial)";
        updatedTime = "";
        totalPlayTime = 0f;
        totalCollectedShards = 0;
        hash = "";
        isCorrupted = false;
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

    // 세이브 데이터 위변조 방지용 내부 시크릿 솔트
    private const string SaveSecretKey = "Unity2026_Platformer_GameSecurityKey_!@#$9876";

    /// <summary>
    /// 세이브 데이터 핵심 상태값에 대한 HMAC-SHA256 무결성 해시를 계산합니다.
    /// </summary>
    public static string ComputeHash(SaveSlotData data)
    {
        if (data == null) return string.Empty;
        var sb = new System.Text.StringBuilder();
        sb.Append(data.slotIndex).Append('|')
          .Append(data.hasData).Append('|')
          .Append(data.stageName ?? "").Append('|')
          .Append(data.totalPlayTime.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
          .Append(data.stageElapsed.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
          .Append(data.checkpointX.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
          .Append(data.checkpointY.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
          .Append(data.hasCheckpoint);

        if (data.segmentTimes != null && data.segmentTimes.Length > 0)
        {
            sb.Append('|');
            for (int i = 0; i < data.segmentTimes.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(data.segmentTimes[i].ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return ComputeHMACSHA256(sb.ToString(), SaveSecretKey);
    }

    private static string ComputeHMACSHA256(string text, string key)
    {
        byte[] keyBytes = System.Text.Encoding.UTF8.GetBytes(key);
        byte[] textBytes = System.Text.Encoding.UTF8.GetBytes(text);
        using (var hmac = new System.Security.Cryptography.HMACSHA256(keyBytes))
        {
            byte[] hashBytes = hmac.ComputeHash(textBytes);
            var sb = new System.Text.StringBuilder(hashBytes.Length * 2);
            for (int i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("x2"));
            }
            return sb.ToString();
        }
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
                if (data != null)
                {
                    ValidateSaveIntegrity(data, slotIndex);
                    return data;
                }
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
                    ValidateSaveIntegrity(data, slotIndex);
                    // 파일로 저장하여 마이그레이션 완료 (서명 포함)
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

    private static void ValidateSaveIntegrity(SaveSlotData data, int slotIndex)
    {
        if (data == null || !data.hasData) return;

        if (string.IsNullOrEmpty(data.hash))
        {
            // 기존 레거시 세이브 파일 (서명 없음): 서명 생성 및 자동 마이그레이션
            Debug.Log($"<color=lime>[SaveSecurity]</color> 슬롯 {slotIndex} 기존 세이브 무결성 서명 자동 생성 및 갱신");
            SaveSlot(data);
        }
        else
        {
            string expectedHash = ComputeHash(data);
            if (!string.Equals(data.hash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogError($"<color=red>[SaveSecurity] 슬롯 {slotIndex} 세이브 데이터 위변조 감지! 무결성 검증 실패 (해시 불일치).</color>");
                data.isCorrupted = true;
                data.hasData = false;
            }
        }
    }

    public static bool HasSlotData(int slotIndex)
    {
        var slot = GetSlot(slotIndex);
        return slot != null && slot.hasData && !slot.isCorrupted;
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
            totalPlayTime = 0f,
            isCorrupted = false
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
        data.isCorrupted = false;
        // 데이터 저장 직전 무결성 해시 계산 및 서명
        data.hash = ComputeHash(data);

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

            Debug.Log($"<color=cyan>[SaveDataManager]</color> 슬롯 {data.slotIndex} 파일 저장 완료 (서명: {data.hash.Substring(0, Mathf.Min(8, data.hash.Length))}...): {filePath}");
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

    // 진행 스테이지 + "현재까지의 누적 기록(절대값)"을 함께 저장한다. (이어하기 시 합산 복원용)
    // UpdateProgress 의 '+=' 와 달리 절대값으로 덮어써서, 같은 스테이지 재시도 시 중복 누적을 막는다.
    public static void SaveProgressWithTime(string nextSceneName, float totalRunSeconds)
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
        data.totalPlayTime = totalRunSeconds < 0f ? 0f : totalRunSeconds;
        data.segmentTimes = RunManager.Segments.ToArray();  // 완료한 스테이지들의 개별 기록
        data.segmentStages = RunManager.SegmentStages.ToArray(); // 각 구간의 스테이지 번호
        data.segmentShards = RunManager.SegmentShards.ToArray(); // 각 구간의 조각 수집 개수
        data.totalCollectedShards = RunManager.TotalCollectedShards;
        // 새 스테이지 진입이므로 중간 체크포인트 정보는 초기화 (처음부터 시작)
        data.stageElapsed = 0f;
        data.hasCheckpoint = false;
        SaveSlot(data);
        Debug.Log($"<color=cyan>[SaveDataManager]</color> 진행+기록 저장: {data.stageDisplayName} / 누적 {data.totalPlayTime:0.00}s / 조각 {data.totalCollectedShards}개");
    }

    // Phase2: 스테이지 중간 체크포인트 저장 (이어하기 시 그 지점 + 타이머부터 복원).
    public static void SaveCheckpoint(string stageName, float accumulatedSeconds, float stageElapsedSeconds, Vector3 checkpointPos)
    {
        int slot = CurrentSlotIndex;
        var data = GetSlot(slot);
        if (!data.hasData)
        {
            data.hasData = true;
            data.slotIndex = slot;
        }

        data.stageName = stageName;
        data.stageDisplayName = GetStageDisplayName(stageName);
        data.totalPlayTime = accumulatedSeconds < 0f ? 0f : accumulatedSeconds;
        data.segmentTimes = RunManager.Segments.ToArray();  // 완료한 이전 스테이지들의 개별 기록
        data.segmentStages = RunManager.SegmentStages.ToArray(); // 각 구간의 스테이지 번호
        data.segmentShards = RunManager.SegmentShards.ToArray(); // 각 구간의 조각 수집 개수
        data.totalCollectedShards = RunManager.TotalCollectedShards;
        data.stageElapsed = stageElapsedSeconds < 0f ? 0f : stageElapsedSeconds;
        data.checkpointX = checkpointPos.x;
        data.checkpointY = checkpointPos.y;
        data.hasCheckpoint = true;
        SaveSlot(data);
        Debug.Log($"<color=cyan>[SaveDataManager]</color> 체크포인트 저장: {stageName} / 누적 {data.totalPlayTime:0.00}s / 구간 {data.stageElapsed:0.00}s @ ({checkpointPos.x:0.0},{checkpointPos.y:0.0})");
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
            case "Main Stage3":
                return "스테이지 3 (Stage 3)";
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
