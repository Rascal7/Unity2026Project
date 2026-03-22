using Fusion;
using UnityEngine;

public class PlayerMovement : NetworkBehaviour
{
    [Header("Movement Settings")]
    public float speed = 5f;
    public float jumpForce = 10f;
    public float climbSpeed = 4f;

    [Header("Ground Check")]
    public Transform groundCheck;
    public float groundCheckRadius = 0.15f;
    public LayerMask groundLayer;

    private Rigidbody2D _rb;
    private Animator _anim;
    private SpriteRenderer _sr;
    private float _originalGravity;

    // 네트워크 상태 변수 (동기화 필요)
    [Networked] private NetworkBool IsOnLadder { get; set; }
    [Networked] private NetworkBool IsClimbing { get; set; }
    [Networked] private NetworkBool IsGrounded { get; set; }

    public override void Spawned()
    {
        _rb = GetComponent<Rigidbody2D>();
        _anim = GetComponent<Animator>();
        _sr = GetComponent<SpriteRenderer>();
        _originalGravity = _rb.gravityScale;

        // 로컬 플레이어라면 카메라 타겟 설정
        if (Object.HasInputAuthority)
        {
            CameraFollow cam = FindObjectOfType<CameraFollow>();
            if (cam != null) cam.SetTarget(this.transform);
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (groundCheck == null)
        {
            // 로그가 너무 많이 찍히는 걸 방지하기 위해 로컬 플레이어일 때만 경고 출력
            if (Object.HasInputAuthority)
                Debug.LogWarning("[PlayerMovement] groundCheck가 할당되지 않았습니다. 인스펙터를 확인하세요!");
            return;
        }
        // 1. 바닥 체크 (호스트/서버에서 계산)
        CheckGround();
        if (transform.position.z != 0)
        {
            transform.position = new Vector3(transform.position.x, transform.position.y, 0);
        }
        if (GetInput(out NetworkInputData data))
        {
            // 2. 사다리 및 중력 처리
            HandleLadderLogic(data);

            // 3. 이동 및 점프 처리
            Move(data);
        }
        if (IsClimbing)
        {
            Debug.Log($"<color=cyan>[Status]</color> IsClimbing: {IsClimbing}, IsOnLadder: {IsOnLadder}");
        }
        // 4. 애니메이션 파라미터 업데이트
        UpdateAnimationParams();
        // groundCheck가 연결 안 되어 있으면 그냥 함수를 나가라 (에러 방지

        IsGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer) != null;
    }

    private void Move(NetworkInputData data)
    {
        float xVelocity = data.direction.x * speed;
        float yVelocity = _rb.linearVelocity.y;

        if (IsClimbing)
        {
            // [핵심] 사다리 타기 중일 때의 이동 로직
            // 입력이 있으면 그 방향으로 움직이고, 없으면 0으로 고정(멈춤)해야 합니다.
            if (Mathf.Abs(data.direction.y) > 0.1f)
            {
                yVelocity = data.direction.y * climbSpeed;
            }
            else
            {
                yVelocity = 0f; // 입력이 없으면 사다리에 고정
            }
        }
        else if (data.isJumpPressed && IsGrounded)
        {
            yVelocity = jumpForce;
        }

        _rb.linearVelocity = new Vector2(xVelocity, yVelocity);

        // 방향 전환
        if (Mathf.Abs(data.direction.x) > 0.1f)
        {
            _sr.flipX = data.direction.x < 0;
        }
    }

    private void HandleLadderLogic(NetworkInputData data)
    {
        // [수정] 사다리 근처에 있고 위/아래 입력이 있을 때만 클라이밍 시작
        if (IsOnLadder && Mathf.Abs(data.direction.y) > 0.1f)
        {
            if (!IsClimbing) _rb.linearVelocity = Vector2.zero; // [보강] 사다리 잡는 순간 속도 초기화
            IsClimbing = true;
        }

        // 바닥에 닿았거나 사다리를 벗어나면 해제
        if (!IsOnLadder || (IsGrounded && data.direction.y < -0.1f))
        {
            IsClimbing = false;
        }

        // 중력 제어
        _rb.gravityScale = IsClimbing ? 0f : _originalGravity;

        // [꿀팁] 사다리 타기 중에는 좌우 흔들림을 방지하기 위해 x속도를 줄이기도 합니다.
    }

    private void CheckGround()
    {
        // 충돌한 객체가 null이 아니면(존재하면) true, 없으면 false가 됩니다.
        if (groundCheck == null) return;
        IsGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer) != null;
    }

    private void UpdateAnimationParams()
    {
        if (_anim == null) return;

        _anim.SetBool("IsGrounded", IsGrounded);
        _anim.SetBool("IsClimbing", IsClimbing);

        if (IsClimbing)
        {
            _anim.SetFloat("Speed", 0f);
            _anim.SetFloat("ClimbSpeed", Mathf.Abs(_rb.linearVelocity.y));
        }
        else
        {
            _anim.SetFloat("Speed", Mathf.Abs(_rb.linearVelocity.x));
            _anim.SetFloat("ClimbSpeed", 0f);
        }

        _anim.SetFloat("YVelocity", IsClimbing ? 0f : _rb.linearVelocity.y);
    }

    // 트리거 체크는 서버에서만 수행하거나, [Networked] 변수를 통해 동기화
    private void OnTriggerEnter2D(Collider2D other)
    {
        // 로그가 찍히는지 확인 (안 찍히면 Collider 설정 문제)
        Debug.Log($"<color=yellow>[Trigger]</color> 무언가와 닿음: {other.name}, 태그: {other.tag}");

        if (other.CompareTag("Ladder"))
        {
            // 호스트(서버) 권한이 있는 경우에만 네트워크 변수 수정 가능
            if (Object.HasStateAuthority)
            {
                IsOnLadder = true;
                Debug.Log("<color=green>[Success]</color> 사다리 감지됨!");
            }
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Ladder"))
        {
            if (Object.HasStateAuthority)
            {
                IsOnLadder = false;
            }
        }
    }
}