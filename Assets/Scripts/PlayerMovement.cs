using Fusion;
using UnityEngine;

public class PlayerMovement : NetworkBehaviour
{
    [Header("Ladder Settings")]
    public float climbSpeed = 4f;
    public float ladderJumpHorizontalForce = 5f;

    [Header("Move Settings")]
    public float speed = 5f;
    public float jumpForce = 10f;

    [Header("Ground Check")]
    public Transform groundCheck;
    public float groundCheckRadius = 0.15f;
    public LayerMask groundLayer;

    private Rigidbody2D _rb;
    private Animator _anim;
    private SpriteRenderer _sr;
    private float _originalGravity;

    // 네트워크 동기화 상태
    [Networked] private NetworkBool IsClimbing { get; set; }
    [Networked] private NetworkBool IsGrounded { get; set; }

    // 로컬 상태
    private bool _isOnLadder;

    public override void Spawned()
    {
        _rb = GetComponent<Rigidbody2D>();
        _anim = GetComponent<Animator>();
        _sr = GetComponent<SpriteRenderer>();

        
        if (_rb == null)
        {
            Debug.LogError($"[PlayerMovement] {gameObject.name}에 Rigidbody2D가 없습니다! 컴포넌트를 추가해주세요.");
            return;
        }
        _originalGravity = _rb.gravityScale;
        if (Object.HasInputAuthority)
        {
            CameraFollow cam = FindObjectOfType<CameraFollow>();
            if (cam != null)
                cam.SetTarget(transform);
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (_rb == null) return;

        CheckGround();

        if (transform.position.z != 0f)
        {
            transform.position = new Vector3(transform.position.x, transform.position.y, 0f);
        }

        if (GetInput(out NetworkInputData data))
        {
            HandleLadder(data);
            Move(data);
            FlipSprite(data);
            UpdateAnimation(data);
        }
        else
        {
            NetworkInputData emptyData = default;
            HandleLadder(emptyData);
            UpdateAnimation(emptyData);
        }
    }

    private void Move(NetworkInputData data)
    {
        float xVelocity = data.direction.x * speed;
        float yVelocity = _rb.linearVelocity.y;

        if (IsClimbing)
        {
            // 원래 코드처럼 사다리에서도 좌우 입력은 유지
            // 단, 위아래 입력이 없으면 y축은 0으로 고정
            yVelocity = Mathf.Abs(data.direction.y) > 0.1f
                ? data.direction.y * climbSpeed
                : 0f;
        }
        else
        {
            if (data.isJumpPressed && IsGrounded)
            {
                yVelocity = jumpForce;
            }
        }

        _rb.linearVelocity = new Vector2(xVelocity, yVelocity);
    }

    private void HandleLadder(NetworkInputData data)
    {
        if (_isOnLadder)
        {
            IsClimbing = true;
            _rb.gravityScale = 0f;

            // 원래 코드 느낌 유지:
            // 사다리 위에 있으면 위아래 입력 없을 때 y속도 0
            if (Mathf.Abs(data.direction.y) > 0.1f)
            {
                _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, data.direction.y * climbSpeed);
            }
            else
            {
                _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, 0f);
            }

            // 사다리에서 점프하면 탈출
            if (data.isJumpPressed)
            {
                IsClimbing = false;
                _isOnLadder = false;
                _rb.gravityScale = _originalGravity;

                float jumpX = data.direction.x * ladderJumpHorizontalForce;
                _rb.linearVelocity = new Vector2(jumpX, jumpForce);
            }
        }
        else
        {
            IsClimbing = false;
            _rb.gravityScale = _originalGravity;
        }
    }

    private void CheckGround()
    {
        if (groundCheck == null) return;

        IsGrounded = Physics2D.OverlapCircle(
            groundCheck.position,
            groundCheckRadius,
            groundLayer
        ) != null;
    }

    private void FlipSprite(NetworkInputData data)
    {
        if (_sr == null) return;

        if (data.direction.x > 0.01f)
            _sr.flipX = false;
        else if (data.direction.x < -0.01f)
            _sr.flipX = true;
    }

    private void UpdateAnimation(NetworkInputData data)
    {
        if (_anim == null) return;

        if (IsClimbing)
        {
            _anim.SetFloat("Speed", 0f);

            float climbAnimSpeed = Mathf.Abs(_rb.linearVelocity.y) > 0.05f ? 1f : 0f;
            _anim.SetFloat("ClimbSpeed", climbAnimSpeed);
        }
        else
        {
            _anim.SetFloat("Speed", Mathf.Abs(data.direction.x));
            _anim.SetFloat("ClimbSpeed", 0f);
        }

        _anim.SetBool("IsGrounded", IsGrounded);
        _anim.SetBool("IsClimbing", IsClimbing);
        _anim.SetFloat("YVelocity", IsClimbing ? 0f : _rb.linearVelocity.y);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Ladder"))
        {
            _isOnLadder = true;
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Ladder"))
        {
            _isOnLadder = false;
            IsClimbing = false;

            if (_rb != null)
                _rb.gravityScale = _originalGravity;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}