using UnityEngine;
using UnityEngine.SceneManagement;

public class GoalPoint : MonoBehaviour
{
    [Header("Settings")]
    public string currentMapName = "TutorialStage"; // 현재 맵 이름
    public bool isFinalGoal = true;                // 지금은 튜토리얼이 유일하므로 true

    private void OnTriggerEnter2D(Collider2D collision)
    {
        // 1. 플레이어가 닿았는지 확인
        if (collision.CompareTag("Player"))
        {
            Debug.Log($"<color=cyan>[Goal]</color> 플레이어 {collision.name} 도착!");

            // 2. 마지막 골인 지점이라면?
            if (isFinalGoal)
            {
                HandleGameClear();
            }
        }
    }

    private void HandleGameClear()
    {
        var nt = NetworkTimer.Instance;
        if (nt == null || !nt.IsRunning) return;

        // 정지/저장은 호스트만. 엔딩 화면은 GameEndController 가 동기화 플래그를 보고 자동 표시.
        var runner = FindObjectOfType<Fusion.NetworkRunner>();
        if (runner != null && !runner.IsServer) return;

        float finalTime = nt.StopTimer();
        Debug.Log($"<color=lime>[Success]</color> 클리어! 최종 기록: {finalTime}초");

        var ranking = RankingManager.GetOrCreate();
        if (ranking != null)
            ranking.SaveScore(finalTime);
    }
}