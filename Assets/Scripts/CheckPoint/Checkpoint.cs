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
        var nt = NetworkTimer.Instance;
        if (nt == null || !nt.IsRunning) return;

        // 정지/저장 트리거는 호스트만. 엔딩 화면은 각 피어의 GameEndController 가
        // 동기화된 IsFinished 플래그를 보고 스스로 띄운다.
        var runner = FindObjectOfType<Fusion.NetworkRunner>();
        if (runner != null && !runner.IsServer) return;

        float finalTime = nt.StopTimer();
        Debug.Log($"<color=lime>[Success]</color> 최종 클리어 시간: {finalTime}초");

        var ranking = RankingManager.GetOrCreate();
        if (ranking != null)
            ranking.SaveScore(finalTime);
        else
            Debug.LogError("<color=red>[Critical] RankingManager 생성에 실패했습니다!</color>");
    }
} 