using UnityEngine;

public class CheckpointManager : MonoBehaviour
{
    public static Vector3 lastCheckpointPosition;
    public static bool hasCheckpoint = false;

    public Transform startPoint; // Inspector���� ���� ��ġ �巡��

    void Awake()
    {
        // 인스펙터 참조가 비어 있어도 동작하도록 씬에서 직접 찾는다.
        if (startPoint == null)
        {
            var go = GameObject.Find("StartPoint");
            if (go != null) startPoint = go.transform;
        }

        // hasCheckpoint 를 true 로 세워야 PlayerMovement.Respawn() 이 이 좌표를 쓴다.
        // (세우지 않으면 Vector3.zero 로 리스폰된다.)
        // 세이브 이어하기는 Spawner.OnSceneLoadDone 이 나중에 실행되며 이 값을 다시 덮어쓴다.
        if (!hasCheckpoint && startPoint != null)
        {
            lastCheckpointPosition = startPoint.position;
            hasCheckpoint = true;
        }
        else if (!hasCheckpoint)
        {
            Debug.LogWarning("[CheckpointManager] StartPoint 를 찾지 못했습니다. 사망 시 (0,0) 으로 리스폰됩니다.");
        }
    }

    // ���� �� �Ѿ �� ȣ���Ͽ� üũ ����Ʈ �ʱ�ȭ
    public static void ResetCheckpoint()
    {
        hasCheckpoint = false;
    }
}