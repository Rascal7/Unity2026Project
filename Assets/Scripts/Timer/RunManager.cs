using System.Collections.Generic;
using UnityEngine;

// 한 번의 런(run) 동안 호스트 메모리에 스테이지별 기록과 구슬 조각(CoinItem) 수집 현황을 누적한다.
// - Segments / SegmentStages / SegmentShards: 완료한 스테이지들의 개별 구간 시간(초), 스테이지 번호, 수집 조각 수(병렬).
// - RestoredBase: 이어하기로 복원했지만 개별 내역이 없는 이전 누적분(구버전 세이브 대비).
// - 조각 1개당 ShardTimeBonusSeconds(5초)만큼 구간 클리어 타임이 단축된다.
// - AccumulatedTime = RestoredBase + Segments 합계 = 랭킹에 올리는 합산 기록.
// - 로비 복귀 / 새 게임 시 ResetRun() 으로 초기화.
public static class RunManager
{
    public const float ShardTimeBonusSeconds = 5f;

    // 지금까지의 합산 기록(초). RestoredBase + 모든 구간의 합 (조각 단축 보너스 반영 후).
    public static float AccumulatedTime { get; private set; }

    // 완료한 스테이지들의 개별 구간 기록(초), 각 구간의 스테이지 번호, 각 구간의 조각 수집 개수(병렬 리스트).
    public static readonly List<float> Segments = new List<float>();
    public static readonly List<int> SegmentStages = new List<int>();
    public static readonly List<int> SegmentShards = new List<int>();

    // 현재 스테이지에서 수집한 조각 수, 전체 조각 수 및 수집한 조각 좌표 키 목록(체크포인트 이어하기용)
    public static int CurrentStageCollectedShards { get; private set; }
    public static int CurrentStageTotalShards { get; private set; }
    public static readonly HashSet<string> CurrentStageCollectedCoinKeys = new HashSet<string>();

    // 이번 런 전체에서 누적 수집한 총 조각 수
    public static int TotalCollectedShards { get; private set; }

    // 방금 클리어한 스테이지의 보너스 적용 전 원본 시간 및 수집 조각 수 (결과 화면 표시용)
    public static float LastCompletedStageRawTime { get; private set; }
    public static int LastCompletedStageShards { get; private set; }
    public static int LastCompletedStageTotalShards { get; private set; }
    public static float LastCompletedStageBonus => LastCompletedStageShards * ShardTimeBonusSeconds;

    // 이어하기로 복원된, 개별 구간 내역이 없는 이전 누적분(0 이면 없음).
    public static float RestoredBase { get; private set; }

    // 호스트가 다음 스테이지로 넘어갈 때 로드할 씬 이름(종료 화면 [다음] 버튼에서 사용).
    public static string PendingNextScene { get; set; }

    public static string GetCoinKey(Vector3 pos)
    {
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F1}_{1:F1}", pos.x, pos.y);
    }

    public static string[] GetCollectedCoinKeysArray()
    {
        if (CurrentStageCollectedCoinKeys.Count == 0) return new string[0];
        var arr = new string[CurrentStageCollectedCoinKeys.Count];
        CurrentStageCollectedCoinKeys.CopyTo(arr);
        return arr;
    }

    /// <summary>초 단위 시간을 "00:00.00" 형식 문자열로 변환하는 공용 포맷터.</summary>
    public static string FormatTime(float t)
    {
        if (t < 0f) t = 0f;
        int m = Mathf.FloorToInt(t / 60f);
        int s = Mathf.FloorToInt(t % 60f);
        int cs = Mathf.FloorToInt((t * 100f) % 100f);
        return string.Format("{0:00}:{1:00}.{2:00}", m, s, cs);
    }

    public static void ResetRun()
    {
        AccumulatedTime = 0f;
        Segments.Clear();
        SegmentStages.Clear();
        SegmentShards.Clear();
        CurrentStageCollectedShards = 0;
        CurrentStageTotalShards = 0;
        CurrentStageCollectedCoinKeys.Clear();
        TotalCollectedShards = 0;
        LastCompletedStageRawTime = 0f;
        LastCompletedStageShards = 0;
        LastCompletedStageTotalShards = 0;
        RestoredBase = 0f;
        PendingNextScene = null;
        Debug.Log("<color=orange>[Run]</color> 런 누적 기록 및 조각 카운트 초기화");
    }

    public static void RecordShardCollected(int count = 1, Vector3? worldPos = null)
    {
        if (count <= 0) return;
        if (worldPos.HasValue)
            CurrentStageCollectedCoinKeys.Add(GetCoinKey(worldPos.Value));
        CurrentStageCollectedShards += count;
        if (CurrentStageCollectedShards > CurrentStageTotalShards)
            CurrentStageTotalShards = CurrentStageCollectedShards;
    }

    public static void SyncStageShards(int collected, int total)
    {
        if (total > 0) CurrentStageTotalShards = total;
        if (collected > CurrentStageCollectedShards) CurrentStageCollectedShards = collected;
    }

    public static void ResetCurrentStageShards(int total = 0)
    {
        CurrentStageCollectedShards = 0;
        CurrentStageTotalShards = Mathf.Max(0, total);
        CurrentStageCollectedCoinKeys.Clear();
    }

    public static void RestoreCheckpointStageShards(int stageShards, string[] coinKeys)
    {
        if (stageShards > CurrentStageCollectedShards)
            CurrentStageCollectedShards = stageShards;
        if (coinKeys != null)
        {
            for (int i = 0; i < coinKeys.Length; i++)
            {
                if (!string.IsNullOrEmpty(coinKeys[i]))
                    CurrentStageCollectedCoinKeys.Add(coinKeys[i]);
            }
        }
    }

    public static void ApplySyncedResultStats(int stageShards, int stageTotalShards, int totalRunShards, float rawSegmentTime)
    {
        LastCompletedStageShards = Mathf.Max(0, stageShards);
        LastCompletedStageTotalShards = Mathf.Max(0, stageTotalShards);
        TotalCollectedShards = Mathf.Max(0, totalRunShards);
        LastCompletedStageRawTime = Mathf.Max(0f, rawSegmentTime);
    }

    // 한 스테이지를 완료할 때 조각 수집 보너스(-5초/개)를 반영하여 구간 시간을 누적한다. 반환 = 현재 합산.
    public static float AddSegment(float rawSegmentSeconds, int stageNumber)
    {
        if (rawSegmentSeconds < 0f) rawSegmentSeconds = 0f;

        int hudCollected = ShardCounterHUD.Instance != null ? ShardCounterHUD.Instance.CollectedCount : 0;
        int hudTotal = ShardCounterHUD.Instance != null ? ShardCounterHUD.Instance.TotalCount : 0;
        int stageShards = Mathf.Max(CurrentStageCollectedShards, hudCollected);
        int stageTotal = Mathf.Max(CurrentStageTotalShards, hudTotal);

        float bonus = stageShards * ShardTimeBonusSeconds;
        // 구간 기록은 최소 1.0초 이상 유지
        float adjustedSeconds = rawSegmentSeconds > 0f ? Mathf.Max(1.0f, rawSegmentSeconds - bonus) : 0f;

        LastCompletedStageRawTime = rawSegmentSeconds;
        LastCompletedStageShards = stageShards;
        LastCompletedStageTotalShards = stageTotal;

        Segments.Add(adjustedSeconds);
        SegmentStages.Add(stageNumber);
        SegmentShards.Add(stageShards);
        TotalCollectedShards += stageShards;
        AccumulatedTime += adjustedSeconds;

        CurrentStageCollectedShards = 0;
        CurrentStageTotalShards = 0;
        CurrentStageCollectedCoinKeys.Clear();

        Debug.Log($"<color=orange>[Run]</color> {stageNumber}스테이지 구간 원본 {rawSegmentSeconds:0.00}s - 조각보너스({stageShards}개, -{bonus:0.0}s) = {adjustedSeconds:0.00}s → 합산 {AccumulatedTime:0.00}s (총 조각 {TotalCollectedShards}개)");
        return AccumulatedTime;
    }

    // 이어하기(불러오기): 저장된 합산 총합 + 개별 구간/스테이지 번호/조각 수를 복원한다.
    public static void RestoreRun(float totalSeconds, float[] segTimes, int[] segStages, int totalShards = 0, int[] segShards = null)
    {
        Segments.Clear();
        SegmentStages.Clear();
        SegmentShards.Clear();
        CurrentStageCollectedShards = 0;
        CurrentStageTotalShards = 0;
        CurrentStageCollectedCoinKeys.Clear();

        float segSum = 0f;
        int shardSum = 0;
        if (segTimes != null)
        {
            for (int i = 0; i < segTimes.Length; i++)
            {
                float v = segTimes[i] < 0f ? 0f : segTimes[i];
                Segments.Add(v);
                int st = (segStages != null && i < segStages.Length) ? segStages[i] : (i + 1);
                SegmentStages.Add(st);
                int sh = (segShards != null && i < segShards.Length) ? Mathf.Max(0, segShards[i]) : 0;
                SegmentShards.Add(sh);
                shardSum += sh;
                segSum += v;
            }
        }
        TotalCollectedShards = Mathf.Max(totalShards, shardSum);
        if (totalSeconds < 0f) totalSeconds = 0f;
        RestoredBase = Mathf.Max(0f, totalSeconds - segSum);
        AccumulatedTime = totalSeconds;
        Debug.Log($"<color=orange>[Run]</color> 이어하기 복원: 합산 {AccumulatedTime:0.00}s, 구간 {Segments.Count}개, 누적 조각 {TotalCollectedShards}개");
    }

    // 씬 이름 끝의 숫자로 스테이지 번호를 얻는다. "Main Stage2" → 2, 숫자 없으면 0.
    public static int StageNumber(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return 0;
        string num = "";
        for (int i = sceneName.Length - 1; i >= 0 && char.IsDigit(sceneName[i]); i--) num = sceneName[i] + num;
        return (num.Length > 0 && int.TryParse(num, out int n)) ? n : 0;
    }

    // 현재 활성 씬의 스테이지 번호.
    public static int CurrentStageNumber()
    {
        return StageNumber(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    }
}

