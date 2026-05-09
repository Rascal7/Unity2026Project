using UnityEngine;

public class Checkpoint : MonoBehaviour
{
    private Animator animator;
    private bool isActivated = false;

    [Header("엔딩 설정")]
    public bool isEndGoal = false; // 2번째 체크포인트라면 인스펙터에서 체크

    private void Awake()
    {
        animator = GetComponent<Animator>();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 1. 플레이어 태그 확인 및 중복 방지
        if (other.CompareTag("Player") && !isActivated)
        {
            // 2. 체크포인트 위치 저장
            CheckpointManager.lastCheckpointPosition = transform.position;
            CheckpointManager.hasCheckpoint = true;
            Debug.Log($"<color=green>[Checkpoint]</color> 위치 저장: {transform.position}");

            // 3. 마지막 목적지인지 확인
            if (isEndGoal)
            {
                HandleGameClear();
            }

            isActivated = true;

            if (animator != null)
            {
                animator.SetTrigger("Activated");
            }
        }
    } 
    private void HandleGameClear()
    {
        if (TimerManager.Instance != null && TimerManager.Instance.isTimerRunning)
        {
            // 타이머 정지 및 기록 확정
            TimerManager.Instance.StopTimer();
            float finalTime = TimerManager.Instance.elapsedTime;

            Debug.Log($"<color=lime>[Success]</color> 최종 클리어 시간: {finalTime}초");

            // 서버 전송 로직 호출
            if (RankingManager.Instance != null)
            {
                RankingManager.Instance.SaveScore(finalTime);
            }
            else { 
                Debug.LogError("<color=red>[Critical] RankingManager를 찾을 수 없습니다! 오브젝트가 씬에 있는지 확인하세요.</color>");
            }
        }
    }
} 