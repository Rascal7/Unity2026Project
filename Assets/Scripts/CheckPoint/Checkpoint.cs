using UnityEngine;

public class Checkpoint : MonoBehaviour
{
    private Animator animator;
    private bool isActivated = false;

    [Header("���� ����")]
    public bool isEndGoal = false; // ������ ���������� ���� �����̸� �ν����Ϳ��� üũ

    private void Awake()
    {
        animator = GetComponent<Animator>();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 1. �÷��̾� �±� Ȯ�� �� �ߺ� ����
        if (other.CompareTag("Player") && !isActivated)
        {
            // 2. üũ����Ʈ ��ġ ����
            CheckpointManager.lastCheckpointPosition = transform.position;
            CheckpointManager.hasCheckpoint = true;
            Debug.Log($"<color=green>[Checkpoint]</color> ��ġ ����: {transform.position}");

            // 3. ������ ������(���� ��)���� Ȯ��
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
    }

    // ���� ��: ������ ������ ������ ���� "�ջ� �ð�"�� ��ŷ�� �ø���, ���� ȭ���� ����.
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

    private void HandleFinalClear()
    {
        var nt = NetworkTimer.Instance;
        if (nt == null || nt.Phase != NetworkTimer.PhaseRunning) return;

        // ����/������ ȣ��Ʈ��. ���� ȭ���� �� �Ǿ��� GameEndController �� Phase �� ���� ǥ��.
        var runner = FindFirstObjectByType<Fusion.NetworkRunner>();
        if (runner != null && !runner.IsServer) return;

        float seg = nt.ElapsedTime;                        // �� ������ �������� ����
        float total = RunManager.AccumulatedTime + seg;    // �� ���������� + ������ ���� = �ջ�
        nt.FinishRun(total);                               // Phase=Finished, FinalTime=�ջ� (�� �Ǿ� ����ȭ)

        var ranking = RankingManager.GetOrCreate();
        if (ranking != null) ranking.SaveScore(total);
        else Debug.LogError("<color=red>[Critical]</color> RankingManager �� ã�� �� �����ϴ�!");

        RunManager.ResetRun();
        Debug.Log($"<color=lime>[Success]</color> ALL CLEAR! �ջ� ���: {total:0.00}��");
    }
}
