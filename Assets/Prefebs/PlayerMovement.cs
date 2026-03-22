using Fusion;
using UnityEngine;

public class PlayerMovement : NetworkBehaviour
{
    private Rigidbody2D _rb;
    public float speed = 5f;
    public float jumpForce = 10f;

    public override void Spawned()
    {
        _rb = GetComponent<Rigidbody2D>();

        // [추가] 이 캐릭터가 내가 조종하는 캐릭터(Local Player)라면 카메라가 나를 따라오게 함
        if (Object.HasInputAuthority)
        {
            CameraFollow cam = FindObjectOfType<CameraFollow>();
            if (cam != null)
            {
                cam.SetTarget(this.transform);
            }
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (GetInput(out NetworkInputData data))
        {
            // 좌우 이동
            float moveX = data.direction.x * speed;

            // 점프 로직 (바닥 체크 포함)
            float moveY = _rb.linearVelocity.y;
            if (data.isJumpPressed && Mathf.Abs(_rb.linearVelocity.y) < 0.01f)
            {
                moveY = jumpForce;
            }

            _rb.linearVelocity = new Vector2(moveX, moveY);
        }
    }
}