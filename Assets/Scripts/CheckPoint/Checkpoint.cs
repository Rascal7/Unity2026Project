using UnityEngine;

public class Checkpoint : MonoBehaviour
{
    private Animator animator;
    private bool isActivated = false;

    [Header("엔딩 설정")]
    public bool isEndGoal = false; // 마지막 스테이지의 최종 지점이면 인스펙터에서 체크

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

            // 3. 마지막 목적지(최종 골)인지 확인
            if (isEndGoal)
            {
                HandleFinalClear();
            }

            isActivated = true;

            if (animator != null)
            {
                animator.SetTrigger("Activated");
            }
        }
    }

    // 최종 골: 마지막 구간을 누적에 더해 "합산 시간"을 랭킹에 올리고, 종료 화면을 띄운다.
    private void HandleFinalClear()
    {
        var nt = NetworkTimer.Instance;
        if (nt == null || nt.Phase != NetworkTimer.PhaseRunning) return;

        // 정지/저장은 호스트만. 종료 화면은 각 피어의 GameEndController 가 Phase 를 보고 표시.
        var runner = FindFirstObjectByType<Fusion.NetworkRunner>();
        if (runner != null && !runner.IsServer) return;

        float seg = nt.ElapsedTime;                        // 이 마지막 스테이지 구간
        float total = RunManager.AccumulatedTime + seg;    // 앞 스테이지들 + 마지막 구간 = 합산
        nt.FinishRun(total);                               // Phase=Finished, FinalTime=합산 (전 피어 동기화)

        var ranking = RankingManager.GetOrCreate();
        if (ranking != null) ranking.SaveScore(total);
        else Debug.LogError("<color=red>[Critical]</color> RankingManager 를 찾을 수 없습니다!");

        RunManager.ResetRun();
        Debug.Log($"<color=lime>[Success]</color> ALL CLEAR! 합산 기록: {total:0.00}초");
    }
}
