using UnityEngine;

// �� ��(run) ���� ȣ��Ʈ �޸𸮿� ���������� ���� �ð��� �����Ѵ�.
// - ���� Ŭ������ �� �ε忡 ������� ����(�� ���� ���� ����, ������ ���ε� �ÿ��� �ʱ�ȭ).
// - ��ŷ�� �ø��� ���� �ð� = ���� ������ �� + ������ ����.
// - �κ� ���� / �� �� ���� �� ResetRun() ���� �ʱ�ȭ.
public static class RunManager
{
    // ���ݱ��� "�Ϸ� Ȯ����" ������������ ���� �ð� �� (��). ���� ���� �� ���������� ������.
    public static float AccumulatedTime { get; private set; }

    // ȣ��Ʈ�� ���� ���������� �Ѿ �� �ε��� �� �̸� (���� ȭ�鿡�� [����] ������ ���).
    public static string PendingNextScene { get; set; }

    public static void ResetRun()
    {
        AccumulatedTime = 0f;
        PendingNextScene = null;
        Debug.Log("<color=orange>[Run]</color> ���� ���� �ð� �ʱ�ȭ");
    }

    // �� �������� ���� �ð��� ������ ���Ѵ�. ��ȯ�� = ���� �� ����.
    // Restore accumulated run time when continuing a loaded game, so the sum carries across a load.
    public static void RestoreAccumulated(float seconds)
    {
        AccumulatedTime = seconds < 0f ? 0f : seconds;
        Debug.Log($"<color=orange>[Run]</color> restore accumulated = {AccumulatedTime:0.00}s");
    }

    public static float AddSegment(float segmentSeconds)
    {
        if (segmentSeconds < 0f) segmentSeconds = 0f;
        AccumulatedTime += segmentSeconds;
        Debug.Log($"<color=orange>[Run]</color> ���� +{segmentSeconds:0.00}s �� ���� {AccumulatedTime:0.00}s");
        return AccumulatedTime;
    }
}
