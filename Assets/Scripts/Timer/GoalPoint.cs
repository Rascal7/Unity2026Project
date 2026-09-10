using UnityEngine;

// 최종 골(마지막 스테이지의 마지막 지점)용. Checkpoint(isEndGoal)와 동일한 최종 처리.
// 중간 스테이지 진행은 EndPoint 가 담당한다.
public class GoalPoint : MonoBehaviour
{
    [Header("Settings")]
    public string currentMapName = "";
    public bool isFinalGoal = true; // 최종 골이면 true

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            Debug.Log($"<color=cyan>[Goal]</color> 플레이어 {collision.name} 도착!");
            if (isFinalGoal)
            {
                HandleFinalClear();
            }
        }
    }

    // 최종 골: 마지막 구간을 누적에 더해 "합산 시간"을 랭킹에 올리고, 종료 화면을 띄운다.
    private void HandleFinalClear()
    {
        var nt = NetworkTimer.Instance;
        if (nt == null || nt.Phase != NetworkTimer.PhaseRunning) return;

        var runner = FindFirstObjectByType<Fusion.NetworkRunner>();
        if (runner != null && !runner.IsServer) return;

        float seg = nt.ElapsedTime;
        float total = RunManager.AccumulatedTime + seg;
        nt.FinishRun(total);

        var ranking = RankingManager.GetOrCreate();
        if (ranking != null) ranking.SaveScore(total);

        RunManager.ResetRun();
        Debug.Log($"<color=lime>[Success]</color> ALL CLEAR! 합산 기록: {total:0.00}초");
    }
}
