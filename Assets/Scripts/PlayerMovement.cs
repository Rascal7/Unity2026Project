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

    // 캐릭터 스킬 종류 (챕터별). 프리팹에서 슬롯에 지정한다.
    public enum CharacterSkill { None, FoxDash, FoxSniff, FoxShadow, RabbitCarrot, RabbitJump, RabbitStomp }

    [Header("Skill Slots (챕터별)")]
    public CharacterSkill skillCh1 = CharacterSkill.None; // 슬롯1 (여우 대시 / 토끼 당근)
    public CharacterSkill skillCh2 = CharacterSkill.None; // 슬롯2 (여우 킁킁 / 토끼 점프)
    public CharacterSkill skillCh3 = CharacterSkill.None; // 슬롯3 (여우 그림자 / 토끼 스톰프)

    [Header("Cooldowns (초)")]
    public float dashCooldown = 1.5f;
    public float jumpCooldown = 1.5f;
    public float sniffCooldown = 6f;
    public float carrotCooldown = 3f;

    [Header("Fox Dash")]
    [Tooltip("대시 속도")]
    public float dashSpeed = 18f;
    [Tooltip("대시 지속시간(초)")]
    public float dashDuration = 0.2f;

    [Header("Rabbit Jump")]
    [Tooltip("강한 도약 점프력 (기본 점프보다 크게)")]
    public float superJumpForce = 18f;

    [Header("Fox Sniff (숨김 오브젝트 감지)")]
    public float sniffRadius = 5f;
    public float sniffRevealDuration = 4.5f;

    [Header("Rabbit Carrot (벽 고정 발판)")]
    public NetworkObject carrotPrefab;
    public float carrotForwardOffset = 0.6f;

    [Header("Ground Check")]
    public Transform groundCheck;
    public float groundCheckRadius = 0.15f;
    public LayerMask groundLayer;

    private Rigidbody2D _rb;
    private Animator _anim;
    private SpriteRenderer _sr;
    private float _originalGravity;

    // Ŭ���� ��ܿ� ���� �߰�
    [Header("Death Settings")]
    public float fallDeathY = -10f;

    private bool _isDead = false;

    // ��Ʈ��ũ ����ȭ ����
    [Networked] private NetworkBool IsClimbing { get; set; }
    [Networked] private NetworkBool IsGrounded { get; set; }

    // 스킬 관련 네트워크 상태
    [Networked] private TickTimer SkillCd1 { get; set; }           // 슬롯별 쿨다운
    [Networked] private TickTimer SkillCd2 { get; set; }
    [Networked] private TickTimer SkillCd3 { get; set; }
    [Networked] private TickTimer DashTimer { get; set; }          // 대시 지속시간
    [Networked] private NetworkBool FacingRight { get; set; }      // 바라보는 방향 (대시 방향 결정)

    [Networked] private NetworkBool SkillAirborne { get; set; } // 토끼 도약 중(발사~착지)

    // --- 연출(PlayerSkillVisuals)용 공개 상태 ---
    public bool IsDashingNow => Runner != null && !DashTimer.ExpiredOrNotRunning(Runner);
    public bool IsFacingRight => FacingRight;
    public bool IsSkillAirborne => SkillAirborne;

    // ���� ����
    private bool _isOnLadder;

    public override void Spawned()
    {
        _rb = GetComponent<Rigidbody2D>();
        _anim = GetComponent<Animator>();
        _sr = GetComponent<SpriteRenderer>();

        
        if (_rb == null)
        {
            Debug.LogError($"[PlayerMovement] {gameObject.name}�� Rigidbody2D�� �����ϴ�! ������Ʈ�� �߰����ּ���.");
            return;
        }
        _originalGravity = _rb.gravityScale;

        // 초기 방향은 오른쪽
        if (Object.HasStateAuthority)
            FacingRight = true;

        if (Object.HasInputAuthority)
        {
            CameraFollow cam = FindFirstObjectByType<CameraFollow>();
            if (cam != null)
                cam.SetTarget(transform);
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (_rb == null) return;

        CheckGround();

        CheckGround();
        CheckFallDeath();

        if (transform.position.z != 0f)
        {
            transform.position = new Vector3(transform.position.x, transform.position.y, 0f);
        }

        bool introLocked = IsIntroLocked();
        if (!introLocked && GetInput(out NetworkInputData data))
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

            // During the STAGE intro the timer hasn't started yet: freeze horizontal motion
            // so players can't get a head start before the run begins.
            if (introLocked && !IsClimbing)
                _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
        }

        UpdateSkillAirborne();
    }

    // 토끼 도약 착지 감지: 공중 상태였다가 땅에 닿고 하강/정지하면 종료
    private void UpdateSkillAirborne()
    {
        if (SkillAirborne && IsGrounded && _rb.linearVelocity.y <= 0.1f)
            SkillAirborne = false;
    }

    // True while the STAGE intro card is showing (NetworkTimer in PhaseIntro, before the run starts).
    private bool IsIntroLocked()
    {
        var nt = NetworkTimer.Instance;
        return nt != null && nt.Phase == NetworkTimer.PhaseIntro;
    }

    private void Move(NetworkInputData data)
    {
        // 바라보는 방향 갱신 (입력이 있을 때만) - 대시 방향 결정에 사용
        if (data.direction.x > 0.01f) FacingRight = true;
        else if (data.direction.x < -0.01f) FacingRight = false;

        // 스킬 발동 시도 (쿨다운이 끝났고 사다리 중이 아닐 때)
        bool doSuperJump = TryUseSkill(data);

        bool isDashing = !DashTimer.ExpiredOrNotRunning(Runner);

        float xVelocity;
        float yVelocity = _rb.linearVelocity.y;

        if (IsClimbing)
        {
            // ���� �ڵ�ó�� ��ٸ������� �¿� �Է��� ����
            // ��, ���Ʒ� �Է��� ������ y���� 0���� ����
            xVelocity = data.direction.x * speed;
            yVelocity = Mathf.Abs(data.direction.y) > 0.1f
                ? data.direction.y * climbSpeed
                : 0f;
        }
        else if (isDashing)
        {
            // 여우 대시: 바라보는 방향으로 빠르게 이동 (수평 대시)
            xVelocity = (FacingRight ? 1f : -1f) * dashSpeed;
            yVelocity = 0f;
        }
        else
        {
            xVelocity = data.direction.x * speed;

            if (doSuperJump)
            {
                // 토끼 강한 도약
                yVelocity = superJumpForce;
            }
            else if (data.isJumpPressed && IsGrounded)
            {
                yVelocity = jumpForce;
            }
        }

        _rb.linearVelocity = new Vector2(xVelocity, yVelocity);
    }

    /// <summary>
    /// 스킬 입력을 받아 발동을 시도한다.
    /// 반환값: 이번 틱에 토끼 강한 도약을 발동했으면 true (Move 에서 y속도로 적용).
    /// 여우 대시는 DashTimer 를 세팅해 이후 틱들에서 적용된다.
    /// </summary>
    private bool TryUseSkill(NetworkInputData data)
    {
        int slot = data.skillSlot;
        if (slot < 1 || slot > 3) return false;
        if (IsClimbing) return false;

        CharacterSkill active = SlotSkill(slot);
        if (active == CharacterSkill.None) return false;
        if (!SlotReady(slot)) return false; // 해당 슬롯 쿨다운 중

        switch (active)
        {
            case CharacterSkill.FoxDash:
                DashTimer = TickTimer.CreateFromSeconds(Runner, dashDuration);
                SetSlotCd(slot, dashCooldown);
                return false;

            case CharacterSkill.RabbitJump:
                if (!IsGrounded) return false; // 땅에 있을 때만 도약
                SetSlotCd(slot, jumpCooldown);
                SkillAirborne = true; // 연출: 도약 시작
                return true;

            case CharacterSkill.FoxSniff:
                SetSlotCd(slot, sniffCooldown);
                DoSniff();
                return false;

            case CharacterSkill.RabbitCarrot:
                SetSlotCd(slot, carrotCooldown);
                DoCarrot();
                return false;

            default:
                return false;
        }
    }

    private CharacterSkill SlotSkill(int slot)
    {
        switch (slot) { case 1: return skillCh1; case 2: return skillCh2; case 3: return skillCh3; default: return CharacterSkill.None; }
    }
    private bool SlotReady(int slot)
    {
        switch (slot)
        {
            case 1: return SkillCd1.ExpiredOrNotRunning(Runner);
            case 2: return SkillCd2.ExpiredOrNotRunning(Runner);
            case 3: return SkillCd3.ExpiredOrNotRunning(Runner);
            default: return false;
        }
    }
    private void SetSlotCd(int slot, float sec)
    {
        var t = TickTimer.CreateFromSeconds(Runner, sec);
        switch (slot) { case 1: SkillCd1 = t; break; case 2: SkillCd2 = t; break; case 3: SkillCd3 = t; break; }
    }

    // 근방의 숨김 오브젝트(Revealable)를 드러낸다. 호스트가 씬 오브젝트 권한을 가지므로 호스트에서 실행.
    private void DoSniff()
    {
        if (!Runner.IsServer) return;
        var all = FindObjectsByType<Revealable>(FindObjectsSortMode.None);
        float r2 = sniffRadius * sniffRadius;
        Vector2 origin = transform.position;
        foreach (var rv in all)
        {
            if (((Vector2)rv.transform.position - origin).sqrMagnitude <= r2)
                rv.Reveal(sniffRevealDuration);
        }
    }

    // 정면으로 당근 발사 (호스트가 네트워크 오브젝트 스폰)
    private void DoCarrot()
    {
        if (!Runner.IsServer) return;
        if (carrotPrefab == null) return;
        float dir = FacingRight ? 1f : -1f;
        Vector3 pos = transform.position + new Vector3(dir * carrotForwardOffset, 0f, 0f);
        var obj = Runner.Spawn(carrotPrefab, pos, Quaternion.identity, Object.InputAuthority);
        var carrot = obj != null ? obj.GetComponent<CarrotProjectile>() : null;
        if (carrot != null) carrot.Launch(FacingRight);
    }

    private void HandleLadder(NetworkInputData data)
    {
        if (_isOnLadder)
        {
            IsClimbing = true;
            _rb.gravityScale = 0f;

            // ���� �ڵ� ���� ����:
            // ��ٸ� ���� ������ ���Ʒ� �Է� ���� �� y�ӵ� 0
            if (Mathf.Abs(data.direction.y) > 0.1f)
            {
                _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, data.direction.y * climbSpeed);
            }
            else
            {
                _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, 0f);
            }

            // ��ٸ����� �����ϸ� Ż��
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

    // �޼��� �߰�
    private void CheckFallDeath()
    {
        if (_isDead) return;

        if (transform.position.y < fallDeathY)
        {
            Die();
        }
    }

    public void Die()
    {
        if (_isDead) return;
        _isDead = true;

        Debug.Log("�÷��̾� ���!");
        Respawn();
    }

    private void Respawn()
    {
        if (CheckpointManager.hasCheckpoint)
        {
            transform.position = CheckpointManager.lastCheckpointPosition;
        }
        else
        {
            transform.position = Vector3.zero; // �⺻ ���� ��ġ
        }

        _rb.linearVelocity = Vector2.zero;
        _isDead = false;
    }

    private void FlipSprite(NetworkInputData data)
    {
        if (_sr == null) return;

        // FacingRight 를 기준으로 뒤집기 (대시 중 정지 상태에서도 방향 유지)
        _sr.flipX = !FacingRight;
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

        // �� �κ� �߰�
        if (other.CompareTag("Trap"))
        {
            Die();
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