using System.Collections.Generic;
using UnityEngine;

// 중간 체크포인트 / 최종 골.
// 협동 규칙: 스테이지의 플레이어가 모두 밟아야 활성화된다. (스테이지에 혼자면 혼자 밟아도 된다)
public class Checkpoint : MonoBehaviour
{
    private Animator animator;
    private bool isActivated = false;

    // 이 체크포인트에 한 번이라도 닿은 플레이어
    private readonly HashSet<PlayerMovement> _reached = new HashSet<PlayerMovement>();

    [Header("최종 골")]
    public bool isEndGoal = false; // 마지막 스테이지의 최종 골이면 인스펙터에서 체크

    private void Awake()
    {
        animator = GetComponent<Animator>();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (isActivated || !other.CompareTag("Player")) return;

        var player = other.GetComponentInParent<PlayerMovement>();
        if (player != null) _reached.Add(player);

        if (!PlayerMovement.AllPlayersReached(_reached))
        {
            Debug.Log($"<color=green>[Checkpoint]</color> {other.name} 도착. 다른 플레이어를 기다리는 중...");
            return;
        }

        // 체크포인트 위치 저장 (사망 시 리스폰 지점)
        CheckpointManager.lastCheckpointPosition = transform.position;
        CheckpointManager.hasCheckpoint = true;
        Debug.Log($"<color=green>[Checkpoint]</color> 위치 저장: {transform.position}");

        if (isEndGoal)
        {
            HandleFinalClear();
        }
        else
        {
            SaveCheckpointProgress();
        }

        isActivated = true;

        if (animator != null)
        {
            animator.SetTrigger("Activated");
        }
    }

    // Phase2: mid-stage checkpoint. Persist current position + timer for continue-from-checkpoint.
    // Host only, and only while the run timer is actually running.
    private void SaveCheckpointProgress()
    {
        var nt = NetworkTimer.Instance;
        if (nt == null || nt.Phase != NetworkTimer.PhaseRunning) return;

        var runner = FindFirstObjectByType<Fusion.NetworkRunner>();
        if (runner != null && !runner.IsServer) return;

        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        SaveDataManager.SaveCheckpoint(scene, RunManager.AccumulatedTime, nt.ElapsedTime, transform.position);
    }

    // 최종 골: 모든 스테이지 기록을 합산한 "합산 시간"을 랭킹에 올리고, 종료 화면을 띄운다.
    private void HandleFinalClear()
    {
        var nt = NetworkTimer.Instance;
        if (nt == null || nt.Phase != NetworkTimer.PhaseRunning) return;

        // 기록/저장은 호스트만. 종료 화면은 각 피어의 GameEndController 가 Phase 를 보고 표시.
        var runner = FindFirstObjectByType<Fusion.NetworkRunner>();
        if (runner != null && !runner.IsServer) return;

        float seg = nt.ElapsedTime;                        // 이 스테이지 구간 기록
        RunManager.AddSegment(seg, RunManager.CurrentStageNumber());
        float total = RunManager.AccumulatedTime;          // 앞 스테이지들 + 이 구간 = 합산
        nt.FinishRun(total);                               // Phase=Finished, FinalTime=합산 (전 피어 동기화)

        var ranking = RankingManager.GetOrCreate();
        if (ranking != null) ranking.SaveScore(total);
        else Debug.LogError("<color=red>[Critical]</color> RankingManager 를 찾을 수 없습니다!");

        RunManager.ResetRun();
        Debug.Log($"<color=lime>[Success]</color> ALL CLEAR! 합산 기록: {total:0.00}초");
    }
}
