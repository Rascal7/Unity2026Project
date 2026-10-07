using System.Collections.Generic;
using UnityEngine;

// 한 번의 런(run) 동안 호스트 메모리에 스테이지별 기록을 누적한다.
// - Segments / SegmentStages: 완료한 스테이지들의 개별 구간 시간(초)과 그 스테이지 번호(병렬).
// - RestoredBase: 이어하기로 복원했지만 개별 내역이 없는 이전 누적분(구버전 세이브 대비).
// - AccumulatedTime = RestoredBase + Segments 합계 = 랭킹에 올리는 합산 기록.
// - 로비 복귀 / 새 게임 시 ResetRun() 으로 초기화.
public static class RunManager
{
    // 지금까지의 합산 기록(초). RestoredBase + 모든 구간의 합.
    public static float AccumulatedTime { get; private set; }

    // 완료한 스테이지들의 개별 구간 기록(초)과 각 구간의 스테이지 번호(병렬 리스트).
    public static readonly List<float> Segments = new List<float>();
    public static readonly List<int> SegmentStages = new List<int>();

    // 이어하기로 복원된, 개별 구간 내역이 없는 이전 누적분(0 이면 없음).
    public static float RestoredBase { get; private set; }

    // 호스트가 다음 스테이지로 넘어갈 때 로드할 씬 이름(종료 화면 [다음] 버튼에서 사용).
    public static string PendingNextScene { get; set; }

    public static void ResetRun()
    {
        AccumulatedTime = 0f;
        Segments.Clear();
        SegmentStages.Clear();
        RestoredBase = 0f;
        PendingNextScene = null;
        Debug.Log("<color=orange>[Run]</color> 런 누적 기록 초기화");
    }

    // 한 스테이지를 완료할 때 그 구간 시간을 스테이지 번호와 함께 누적한다. 반환 = 현재 합산.
    public static float AddSegment(float segmentSeconds, int stageNumber)
    {
        if (segmentSeconds < 0f) segmentSeconds = 0f;
        Segments.Add(segmentSeconds);
        SegmentStages.Add(stageNumber);
        AccumulatedTime += segmentSeconds;
        Debug.Log($"<color=orange>[Run]</color> {stageNumber}스테이지 구간 +{segmentSeconds:0.00}s → 합산 {AccumulatedTime:0.00}s (구간 {Segments.Count}개)");
        return AccumulatedTime;
    }

    // 이어하기(불러오기): 저장된 합산 총합 + 개별 구간/스테이지 번호를 복원한다.
    // 개별 구간이 없으면(구버전 세이브) 그 누적분을 RestoredBase 로 보존해 최종 합산·표시에 반영한다.
    public static void RestoreRun(float totalSeconds, float[] segTimes, int[] segStages)
    {
        Segments.Clear();
        SegmentStages.Clear();
        float segSum = 0f;
        if (segTimes != null)
        {
            for (int i = 0; i < segTimes.Length; i++)
            {
                float v = segTimes[i] < 0f ? 0f : segTimes[i];
                Segments.Add(v);
                int st = (segStages != null && i < segStages.Length) ? segStages[i] : (i + 1);
                SegmentStages.Add(st);
                segSum += v;
            }
        }
        if (totalSeconds < 0f) totalSeconds = 0f;
        RestoredBase = Mathf.Max(0f, totalSeconds - segSum);
        AccumulatedTime = totalSeconds;
        Debug.Log($"<color=orange>[Run]</color> 이어하기 복원: 합산 {AccumulatedTime:0.00}s, 구간 {Segments.Count}개, 이전기록 {RestoredBase:0.00}s");
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
