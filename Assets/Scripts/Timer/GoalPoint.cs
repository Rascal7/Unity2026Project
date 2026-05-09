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
        if (TimerManager.Instance != null && TimerManager.Instance.isTimerRunning)
        {
            // 3. 타이머 정지
            TimerManager.Instance.StopTimer();
            float finalTime = TimerManager.Instance.elapsedTime;

            Debug.Log($"<color=lime>[Success]</color> 튜토리얼 클리어! 최종 기록: {finalTime}초");

            // 4. 서버(MySQL)로 데이터 전송
            // 이전에 만든 RankingManager가 있다면 여기서 호출합니다.
            if (RankingManager.Instance != null)
            {
                // 예: RankingManager.Instance.SaveScore(finalTime);
                // 지금은 로그로 대체하여 확인합니다.
                Debug.Log("<color=orange>[DB]</color> RankingManager를 통해 DB 저장을 요청합니다.");
            }

            // 5. 결과창을 띄우거나 로비로 이동 (필요시)
            // SceneManager.LoadScene("Lobby");
        }
    }
}