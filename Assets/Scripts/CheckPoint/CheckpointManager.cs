using UnityEngine;

public class CheckpointManager : MonoBehaviour
{
    public static Vector3 lastCheckpointPosition;
    public static bool hasCheckpoint = false;

    public Transform startPoint; // Inspector에서 시작 위치 드래그

    void Awake()
    {
        if (!hasCheckpoint && startPoint != null)
        {
            lastCheckpointPosition = startPoint.position;
        }
    }
}