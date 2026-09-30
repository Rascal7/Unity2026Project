using UnityEngine;

// �� ��(run) ���� ȣ��Ʈ �޸𸮿� ���������� ���� �ð��� �����Ѵ�.
// - ���� Ŭ������ �� �ε忡 ������� ����(�� ���� ���� ����, ������ ���ε� �ÿ��� �ʱ�ȭ).
// - ��ŷ�� �ø��� ���� �ð� = ���� ������ �� + ������ ����.
// - �κ� ���� / �� �� ���� �� ResetRun() ���� �ʱ�ȭ.
public static class RunManager
{
    // ���ݱ��� "�Ϸ� Ȯ����" ������������ ���� �ð� �� (��). ���� ���� �� ���������� ������.
    public static float AccumulatedTime { get; private set; }

    // 스테이지별 구간 기록(초)을 진행 순서대로 저장. 최종 결과의 "스테이지별 나열 + 합산"에 사용.
    public static readonly System.Collections.Generic.List<float> Segments = new System.Collections.Generic.List<float>();

    // 이어하기(불러오기) 시, 세이브에 저장된 이전 스테이지 개별 기록을 되살린다.
    public static void RestoreSegments(float[] seg)
    {
        Segments.Clear();
        if (seg != null && seg.Length > 0) Segments.AddRange(seg);
        Debug.Log($"<color=orange>[Run]</color> restore segments = {Segments.Count}");
    }

    // ȣ��Ʈ�� ���� ���������� �Ѿ �� �ε��� �� �̸� (���� ȭ�鿡�� [����] ������ ���).
    public static string PendingNextScene { get; set; }

    public static void ResetRun()
    {
        AccumulatedTime = 0f;
        Segments.Clear();
        PendingNextScene = null;
        Debug.Log("<color=orange>[Run]</color> ���� ���� �ð� �ʱ�ȭ");
    }

    // �� �������� ���� �ð��� ������ ���Ѵ�. ��ȯ�� = ���� �� ����.
    // Restore accumulated run time when continuing a loaded game, so the sum carries across a load.
    public static void RestoreAccumulated(float seconds)
    {
        AccumulatedTime = seconds < 0f ? 0f : seconds;
        Segments.Clear();
        Debug.Log($"<color=orange>[Run]</color> restore accumulated = {AccumulatedTime:0.00}s");
    }

    public static float AddSegment(float segmentSeconds)
    {
        if (segmentSeconds < 0f) segmentSeconds = 0f;
        Segments.Add(segmentSeconds);
        AccumulatedTime += segmentSeconds;
        Debug.Log($"<color=orange>[Run]</color> ���� +{segmentSeconds:0.00}s �� ���� {AccumulatedTime:0.00}s");
        return AccumulatedTime;
    }
}
