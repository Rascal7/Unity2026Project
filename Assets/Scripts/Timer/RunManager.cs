using UnityEngine;

// 한 판(run) 동안 호스트 메모리에 스테이지별 구간 시간을 누적한다.
// - 정적 클래스라 씬 로드에 영향받지 않음(앱 수명 동안 유지, 도메인 리로드 시에만 초기화).
// - 랭킹에 올리는 최종 시간 = 여기 누적된 값 + 마지막 구간.
// - 로비 복귀 / 새 판 시작 시 ResetRun() 으로 초기화.
public static class RunManager
{
    // 지금까지 "완료 확정된" 스테이지들의 구간 시간 합 (초). 현재 진행 중 스테이지는 미포함.
    public static float AccumulatedTime { get; private set; }

    // 호스트가 다음 스테이지로 넘어갈 때 로드할 씬 이름 (선택 화면에서 [다음] 누르면 사용).
    public static string PendingNextScene { get; set; }

    public static void ResetRun()
    {
        AccumulatedTime = 0f;
        PendingNextScene = null;
        Debug.Log("<color=orange>[Run]</color> 러닝 누적 시간 초기화");
    }

    // 한 스테이지 구간 시간을 누적에 더한다. 반환값 = 누적 후 총합.
    public static float AddSegment(float segmentSeconds)
    {
        if (segmentSeconds < 0f) segmentSeconds = 0f;
        AccumulatedTime += segmentSeconds;
        Debug.Log($"<color=orange>[Run]</color> 구간 +{segmentSeconds:0.00}s → 누적 {AccumulatedTime:0.00}s");
        return AccumulatedTime;
    }
}
