using UnityEngine;
using Fusion;

public class CameraFollow : MonoBehaviour
{
    public Transform target;       // 따라갈 대상
    public Vector3 offset = new Vector3(0, 0, -10); // 카메라와 캐릭터 사이의 거리
    public float smoothSpeed = 0.125f; // 카메라 움직임의 부드러움 정도

    void LateUpdate()
    {
        // 따라갈 대상이 생기면 부드럽게 이동합니다.
        if (target != null)
        {
            Vector3 desiredPosition = target.position + offset;
            Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed);
            transform.position = smoothedPosition;
        }
    }

    // 외부(PlayerMovement)에서 타겟을 설정해주기 위한 함수
    public void SetTarget(Transform playerTransform)
    {
        target = playerTransform;
    }
}