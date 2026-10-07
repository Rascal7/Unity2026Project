using UnityEngine;

// ���� ��(������ ���������� ������ ����)��. Checkpoint(isEndGoal)�� ������ ���� ó��.
// �߰� �������� ������ EndPoint �� ����Ѵ�.
public class GoalPoint : MonoBehaviour
{
    [Header("Settings")]
    public string currentMapName = "";
    public bool isFinalGoal = true; // ���� ���̸� true

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            Debug.Log($"<color=cyan>[Goal]</color> �÷��̾� {collision.name} ����!");
            if (isFinalGoal)
            {
                HandleFinalClear();
            }
        }
    }

    // ���� ��: ������ ������ ������ ���� "�ջ� �ð�"�� ��ŷ�� �ø���, ���� ȭ���� ����.
    private void HandleFinalClear()
    {
        var nt = NetworkTimer.Instance;
        if (nt == null || nt.Phase != NetworkTimer.PhaseRunning) return;

        var runner = FindFirstObjectByType<Fusion.NetworkRunner>();
        if (runner != null && !runner.IsServer) return;

        float seg = nt.ElapsedTime;
        RunManager.AddSegment(seg, RunManager.CurrentStageNumber()); // 마지막 구간도 스테이지 번호와 함께 기록
        float total = RunManager.AccumulatedTime;
        nt.FinishRun(total);

        var ranking = RankingManager.GetOrCreate();
        if (ranking != null) ranking.SaveScore(total);

        RunManager.ResetRun();
        Debug.Log($"<color=lime>[Success]</color> ALL CLEAR! �ջ� ���: {total:0.00}��");
    }
}
