using Fusion;
using Fusion.Addons.Physics;
using UnityEngine;
using UnityEngine.Tilemaps;

public class PlayerMovement : NetworkBehaviour
{
    [Header("Ladder Settings")]
    public float climbSpeed = 4f;
    public float ladderJumpHorizontalForce = 5f;

    [Header("Move Settings")]
    public float speed = 5f;
    public float jumpForce = 10f;

    [Header("Jump Feel (조작감)")]
    [Tooltip("착지 직전에 누른 점프를 이 시간(초) 동안 기억했다가 착지하면 뛴다. 0 이면 끔. (꾹 누르고 있어도 재점프는 하지 않는다)")]
    public float jumpBufferTime = 0.1f;
    [Tooltip("코요테 타임: 발판에서 떨어진 뒤 이 시간(초) 동안은 아직 점프할 수 있다. 0 이면 끔")]
    public float coyoteTime = 0.1f;
    [Tooltip("가변 점프: 상승 중에 점프 키를 떼면 상승 속도에 이 값을 곱한다(낮게 점프). 1 이면 끔")]
    [Range(0f, 1f)] public float jumpCutMultiplier = 0.5f;

    [Header("Acceleration (가속/감속, 초당 속도 변화량)")]
    [Tooltip("땅에서 최고 속도까지 붙는 가속. speed(5) / 70 ≈ 0.07초")]
    public float groundAcceleration = 70f;
    [Tooltip("땅에서 키를 떼거나 반대로 꺾을 때의 감속. 클수록 미끄러짐이 적다")]
    public float groundDeceleration = 90f;
    [Tooltip("공중 가속")]
    public float airAcceleration = 50f;
    [Tooltip("공중에서 키를 떼거나 반대로 꺾을 때의 감속")]
    public float airDeceleration = 40f;

    [Tooltip("최대 낙하 속도(양수). 카메라 낙하 룩어헤드(CameraFollow.fallSpeedForMaxLook)가 따라잡을 수 있도록 제한한다. 0 이면 제한 없음")]
    public float maxFallSpeed = 16f;

    // 캐릭터 스킬 종류 (챕터별). 프리팹에서 슬롯에 지정한다.
    // 주의: 프리팹에 정수로 직렬화되므로 새 값은 항상 끝에 추가한다.
    public enum CharacterSkill { None, FoxDash, FoxSniff, FoxShadow, RabbitCarrot, RabbitJump, RabbitStomp, FoxSwap, RabbitPowerUp }

    [Header("Skill Slots (챕터별)")]
    public CharacterSkill skillCh1 = CharacterSkill.None; // 슬롯1 (여우 대시 / 토끼 당근)
    public CharacterSkill skillCh2 = CharacterSkill.None; // 슬롯2 (여우 킁킁 / 토끼 점프)
    public CharacterSkill skillCh3 = CharacterSkill.None; // 슬롯3 (여우 위치 바꾸기 / 토끼 파워업)

    public static PlayerMovement Local { get; private set; }

    public bool IsFoxCharacter =>
        skillCh1 == CharacterSkill.FoxDash ||
        skillCh2 == CharacterSkill.FoxSniff ||
        skillCh3 == CharacterSkill.FoxSwap ||
        gameObject.name.IndexOf("Fox", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
        gameObject.name.IndexOf("Player1", System.StringComparison.OrdinalIgnoreCase) >= 0;

    public bool IsRabbitCharacter =>
        skillCh1 == CharacterSkill.RabbitCarrot ||
        skillCh2 == CharacterSkill.RabbitJump ||
        skillCh3 == CharacterSkill.RabbitPowerUp ||
        gameObject.name.IndexOf("Rabbit", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
        gameObject.name.IndexOf("Player2", System.StringComparison.OrdinalIgnoreCase) >= 0;

    public static PlayerMovement GetLocalPlayer()
    {
        if (Local != null && Local.gameObject != null && Local.Object != null && Local.Object.IsValid)
            return Local;

        var players = FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None);
        if (players == null || players.Length == 0) return null;

        PlayerMovement candidate = null;
        foreach (var p in players)
        {
            if (p == null || p.gameObject == null) continue;
            if (p.GetComponent<Rigidbody2D>() == null) continue;
            if (p.gameObject.name.Contains("Spawner")) continue;

            if (p.Object != null && p.Object.IsValid)
            {
                if (p.Object.HasInputAuthority)
                {
                    Local = p;
                    return p;
                }
                if (p.Runner != null && p.Object.InputAuthority == p.Runner.LocalPlayer)
                {
                    Local = p;
                    return p;
                }
                if (p.Object.HasStateAuthority && candidate == null) candidate = p;
            }
            else if (candidate == null)
            {
                candidate = p;
            }
        }
        if (candidate != null) Local = candidate;
        return candidate;
    }

    [Header("Cooldowns (초)")]
    public float dashCooldown = 1.5f;
    public float jumpCooldown = 1.5f;
    public float sniffCooldown = 6f;
    public float carrotCooldown = 3f;
    public float swapCooldown = 5f;
    public float powerUpCooldown = 8f;
    public float shadowCooldown = 6f;
    public float stompCooldown = 4f;

    [Header("Fox Shadow & Rabbit Stomp (확장 스킬)")]
    [Tooltip("여우 그림자 분신 유지 시간(초). 분신은 압력판/레버를 대신 작동시킨다")]
    public float shadowDuration = 6f;
    [Tooltip("토끼 스톰프 공중 급강하 속도")]
    public float stompDownSpeed = 20f;
    [Tooltip("토끼 스톰프 착지 충격파 반경(균열 바닥 파괴 및 레버 작동)")]
    public float stompShockwaveRadius = 3.2f;

    [Header("Fox Swap (위치 바꾸기)")]
    [Tooltip("이 거리 안의 다른 플레이어와만 위치를 바꾼다. 0 이면 거리 제한 없음")]
    public float swapMaxDistance = 0f;

    [Header("Rabbit Power Up (당근 먹고 다음 스킬 강화)")]
    [Tooltip("강화 상태 유지 시간(초). 이 안에 당근/도약을 쓰지 않으면 사라진다")]
    public float powerUpDuration = 10f;
    [Tooltip("강화된 도약 점프력 (superJumpForce 보다 크게)")]
    public float poweredJumpForce = 15f;
    [Tooltip("강화된 당근(큰 당근) 크기 배율")]
    public float bigCarrotScale = 2f;

    [Header("Fox Dash")]
    [Tooltip("대시 속도")]
    public float dashSpeed = 18f;
    [Tooltip("대시 지속시간(초)")]
    public float dashDuration = 0.2f;

    [Header("Rabbit Jump")]
    [Tooltip("강한 도약 점프력 (기본 점프보다 크게)")]
    public float superJumpForce = 12f;

    [Header("Fox Sniff (숨김 오브젝트 감지)")]
    public float sniffRadius = 5f;
    public float sniffRevealDuration = 4.5f;

    [Header("Rabbit Carrot (벽 고정 발판)")]
    public NetworkObject carrotPrefab;
    public float carrotForwardOffset = 0.6f;

    [Header("Launcher (점프대)")]
    [Tooltip("점프대로 쓸 타일. 이 타일 위에서 점프를 누르면 더 높이 뛴다. (올라서기만 해서는 안 튕긴다)")]
    public TileBase launcherTile;
    [Tooltip("점프대 위에서의 점프력. jumpForce(기본 10, 높이 2.55) 보다 크게. 13 이면 높이 4.3 정도")]
    public float launcherJumpForce = 13f;
    [Tooltip("발밑에서 이만큼 아래 지점의 타일을 점프대인지 검사한다")]
    public float launcherProbeDepth = 0.15f;

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
    [Tooltip("낙사 판정 Y. Spawned() 에서 스테이지 지형을 읽어 자동으로 덮어쓴다.")]
    public float fallDeathY = -10f;
    [Tooltip("지형 최하단에서 이만큼 더 내려가야 낙사로 친다.")]
    public float fallDeathMargin = 10f;
    [Tooltip("리스폰 직후 무적 시간(초). 같은 지점에서 반복 사망하는 것을 막는다.")]
    public float respawnGraceSeconds = 0.5f;

    // 낙사선은 스테이지 지형에 따라 달라지므로 씬 단위로 한 번만 계산해 캐시한다.
    private static string _fallLineScene;
    private static float _fallLineY;

    private NetworkRigidbody2D _nrb;
    private bool _respawnRequested;

    // ��Ʈ��ũ ����ȭ ����
    [Networked] private NetworkBool IsClimbing { get; set; }
    [Networked] private NetworkBool IsGrounded { get; set; }
    [Networked] private NetworkBool IsDead { get; set; }
    [Networked] private TickTimer RespawnGrace { get; set; }

    // 입력 엣지 판정: 이전 틱 버튼과 비교해 "눌린 순간"만 발동한다. (재시뮬레이션에서도 맞도록 Networked)
    [Networked] private NetworkButtons PrevButtons { get; set; }
    [Networked] private TickTimer JumpBuffer { get; set; }
    [Networked] private TickTimer CoyoteTimer { get; set; }   // 땅을 떠난 뒤에도 점프를 허용하는 유예
    [Networked] private NetworkBool JumpCutReady { get; set; } // 기본 점프로 상승 중 → 키를 떼면 낮게

    // 땅 위이거나, 발판에서 막 떨어져 코요테 타임이 남아 있으면 지상 점프 가능
    private bool CanGroundJump => IsGrounded || !CoyoteTimer.ExpiredOrNotRunning(Runner);

    // 지상 점프를 했으면 코요테/버퍼를 비워 한 번 누른 점프가 두 번 쓰이지 않게 한다.
    private void ConsumeGroundJump()
    {
        JumpBuffer = TickTimer.None;
        CoyoteTimer = TickTimer.None;
    }

    // 스킬 관련 네트워크 상태
    [Networked] private TickTimer SkillCd1 { get; set; }           // 슬롯별 쿨다운
    [Networked] private TickTimer SkillCd2 { get; set; }
    [Networked] private TickTimer SkillCd3 { get; set; }
    [Networked] private TickTimer DashTimer { get; set; }          // 대시 지속시간
    [Networked] private NetworkBool FacingRight { get; set; }      // 바라보는 방향 (대시 방향 결정)

    [Networked] private NetworkBool SkillAirborne { get; set; } // 토끼 도약 중(발사~착지)
    [Networked] private TickTimer PowerUpTimer { get; set; }    // 토끼 파워업 유지(다음 당근/도약 1회 강화)
    [Networked] private byte SwapCount { get; set; }            // 위치 바꾸기 횟수(연출 엣지 감지용)
    [Networked] private Vector2 SwapFrom { get; set; }          // 위치 바꾸기 출발/도착 지점(포털 연출용)
    [Networked] private Vector2 SwapTo { get; set; }
    [Networked] private byte SniffCount { get; set; }           // 킁킁 사용 횟수(연출 엣지 감지용)
    [Networked] private byte PowerSpendCount { get; set; }      // 파워업 소모 횟수(연출 엣지 감지용)
    [Networked] private NetworkBool PoweredJump { get; set; }   // 이번 도약이 강화 도약인가(발사~착지)

    // --- 연출(PlayerSkillVisuals)용 공개 상태 ---
    public bool IsDashingNow => Runner != null && !DashTimer.ExpiredOrNotRunning(Runner);
    public bool IsFacingRight => FacingRight;
    public bool IsSkillAirborne => SkillAirborne;
    public bool IsPoweredUp => Runner != null && !PowerUpTimer.ExpiredOrNotRunning(Runner);
    public float PowerUpRemainingSeconds
    {
        get
        {
            if (Runner == null || !Runner.IsRunning) return 0f;
            float? rem = PowerUpTimer.RemainingTime(Runner);
            return (rem.HasValue && rem.Value > 0f) ? rem.Value : 0f;
        }
    }
    public int SwapCounter => SwapCount;
    public Vector2 LastSwapFrom => SwapFrom;
    public Vector2 LastSwapTo => SwapTo;
    public int SniffCounter => SniffCount;
    public int PowerSpendCounter => PowerSpendCount;
    public bool IsPoweredJump => PoweredJump;
    // 튜토리얼 안내용 접근자: [Networked] 값은 Spawned()~Despawned() 사이, 오브젝트가 유효할 때만 접근 가능.
    // 연결 끊김/디스폰 직후에도 안전하도록 Object 유효성까지 함께 확인한다. (NetworkedAccessSafe)
    private bool NetworkedAccessSafe => _spawned && Object != null && Object.IsValid;
    public bool IsClimbingNow => NetworkedAccessSafe && IsClimbing;   // 사다리 등반 중인지(로컬 확인용)
    public bool IsGroundedNow => NetworkedAccessSafe && IsGrounded;   // 접지 상태(점프 감지용)
    public float VerticalVelocity => _rb != null ? _rb.linearVelocity.y : 0f; // 수직 속도(비네트워크, 안전)
    private bool _spawned;

    // 튜토리얼 미션 브리핑 중 로컬 입력을 잠근다(Spawner.OnInput 에서 빈 입력으로 전달).
    public static bool TutorialHold;

    // ���� ����
    private bool _isOnLadder;
    private bool _stompAirborne;
    private bool _stompPowered;
    private float _extraSkillReadyAt;

    // 로컬 오디오(점프/착지/사망/리스폰) 엣지 감지용 상태
    private bool _visInitialized;
    private bool _prevGroundedVis;
    private bool _prevDeadVis;
    private float _prevVelYVis;

    public override void Spawned()
    {
        _spawned = true;
        _rb = GetComponent<Rigidbody2D>();
        _anim = GetComponent<Animator>();
        _sr = GetComponent<SpriteRenderer>();

        
        if (_rb == null)
        {
            Debug.LogError($"[PlayerMovement] {gameObject.name}�� Rigidbody2D�� �����ϴ�! ������Ʈ�� �߰����ּ���.");
            return;
        }
        // Fusion NetworkRigidbody2D 자체 렌더 보간과의 지터 충돌을 방지하기 위해 내장 interpolation은 None으로 설정
        _rb.interpolation = RigidbodyInterpolation2D.None;
        _originalGravity = _rb.gravityScale;
        _nrb = GetComponent<NetworkRigidbody2D>();
        fallDeathY = ResolveFallDeathY();

        // 클라이언트 물리 시뮬레이션(움직임 예측) 안전 보장
        if (Runner != null && Runner.TryGetComponent<RunnerSimulatePhysics2D>(out var sim2d))
        {
            sim2d.ClientPhysicsSimulation = ClientPhysicsSimulation.SimulateAlways;
        }

        // 초기 방향은 오른쪽
        if (Object.HasStateAuthority)
            FacingRight = true;

        if (Object.HasInputAuthority)
        {
            Local = this;
            CameraFollow cam = FindFirstObjectByType<CameraFollow>();
            if (cam != null)
                cam.SetTarget(transform);

            if (SkillCooldownHUD.Instance == null)
            {
                var hudObj = new GameObject("SkillCooldownHUD");
                DontDestroyOnLoad(hudObj);
                hudObj.AddComponent<SkillCooldownHUD>();
            }
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        // 디스폰(연결 끊김 포함) 후에는 [Networked] 값 접근이 예외를 던지므로 즉시 접근을 막는다.
        _spawned = false;
        base.Despawned(runner, hasState);
        if (Local == this) Local = null;
    }

    private void OnDestroy()
    {
        if (Local == this) Local = null;
    }

    public override void FixedUpdateNetwork()
    {
        if (_rb == null) return;

        CheckGround();

        // 땅에 서 있는 동안 코요테 타이머를 계속 채워 둔다. (점프로 상승 중일 때는 채우지 않는다)
        if (IsGrounded && _rb.linearVelocity.y <= 0.1f && coyoteTime > 0f)
            CoyoteTimer = TickTimer.CreateFromSeconds(Runner, coyoteTime);

        if (transform.position.z != 0f)
        {
            transform.position = new Vector3(transform.position.x, transform.position.y, 0f);
        }

        bool introLocked = IsIntroLocked();

        // 인트로 카드가 떠 있는 동안에는 낙사 판정을 돌리지 않는다.
        if (!introLocked) CheckFallDeath();

        // 실제 리스폰은 물리 콜백이 아니라 틱 안에서 한 번만 수행한다.
        if (_respawnRequested)
        {
            _respawnRequested = false;
            Respawn();
        }

        // 인트로 중에도 PrevButtons 는 갱신한다. (인트로 내내 누르고 있던 키가 시작 순간 발동하지 않게)
        bool hasInput = GetInput(out NetworkInputData data);
        NetworkButtons pressed = default;
        if (hasInput)
        {
            pressed = data.buttons.GetPressed(PrevButtons);
            PrevButtons = data.buttons;
        }

        if (!introLocked && hasInput)
        {
            HandleLadder(data, ref pressed);
            Move(data, pressed);
        }
        else
        {
            NetworkButtons nonePressed = default;
            HandleLadder(default, ref nonePressed);

            // During the STAGE intro the timer hasn't started yet: freeze horizontal motion
            // so players can't get a head start before the run begins.
            if (introLocked && !IsClimbing)
                _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
        }

        UpdateSkillAirborne();
    }

    // 토끼 도약/스톰프 착지 감지: 공중 상태였다가 땅에 닿고 하강/정지하면 종료
    private void UpdateSkillAirborne()
    {
        if (_stompAirborne && IsGrounded && _rb.linearVelocity.y <= 0.5f)
        {
            _stompAirborne = false;
            bool powered = _stompPowered;
            _stompPowered = false;
            if (Runner != null && Runner.IsServer)
            {
                TriggerStompImpactOnServer(powered);
            }
        }

        if (SkillAirborne && IsGrounded && _rb.linearVelocity.y <= 0.1f)
        {
            SkillAirborne = false;
            PoweredJump = false;
        }
    }

    // True while the STAGE intro card is showing (NetworkTimer in PhaseIntro, before the run starts).
    private bool IsIntroLocked()
    {
        var nt = NetworkTimer.Instance;
        return nt != null && nt.Phase == NetworkTimer.PhaseIntro;
    }

    private void Move(NetworkInputData data, NetworkButtons pressed)
    {
        // 바라보는 방향 갱신 (입력이 있을 때만) - 대시 방향 결정에 사용
        if (data.direction.x > 0.01f) FacingRight = true;
        else if (data.direction.x < -0.01f) FacingRight = false;

        // 점프는 눌린 순간만. 공중에서 눌렀으면 jumpBufferTime 동안 기억했다가 착지하면 뛴다.
        bool jumpPressed = pressed.IsSet(InputButton.Jump);
        if (jumpPressed && jumpBufferTime > 0f)
            JumpBuffer = TickTimer.CreateFromSeconds(Runner, jumpBufferTime);
        bool wantJump = jumpPressed || !JumpBuffer.ExpiredOrNotRunning(Runner);

        // 스킬 발동 시도 (쿨다운이 끝났고 사다리 중이 아닐 때)
        float superJumpY = TryUseSkill(pressed);
        bool doSuperJump = superJumpY > 0f;

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
            xVelocity = AccelerateX(data.direction.x * speed);

            // 가변 점프: 기본 점프로 올라가는 중에 키를 떼면 상승을 끊는다. (점프한 다음 틱부터 검사)
            if (JumpCutReady)
            {
                if (yVelocity <= 0f) JumpCutReady = false;
                else if (!data.buttons.IsSet(InputButton.Jump))
                {
                    yVelocity *= jumpCutMultiplier;
                    JumpCutReady = false;
                }
            }

            if (doSuperJump)
            {
                // 토끼 강한 도약 (파워업 상태면 더 높이)
                yVelocity = superJumpY;
                JumpCutReady = false;
            }
            else if (wantJump && CanGroundJump)
            {
                // 점프대 위에서 점프를 누르면 더 높이 뛴다. (올라서기만 해서는 안 튕긴다)
                // 점프대 점프는 레벨 설계 높이를 지키도록 가변 점프를 적용하지 않는다.
                bool launcher = IsGrounded && IsOnLauncher();
                yVelocity = launcher ? launcherJumpForce : jumpForce;
                JumpCutReady = !launcher;
                ConsumeGroundJump();
            }
        }

        // 낙하 속도 제한: 카메라가 따라잡지 못할 만큼 빨리 떨어져 아래가 안 보이는 것을 막는다.
        float fallCap = _stompAirborne ? Mathf.Max(maxFallSpeed, stompDownSpeed) : maxFallSpeed;
        if (fallCap > 0f && !IsClimbing && yVelocity < -fallCap)
            yVelocity = -fallCap;

        _rb.linearVelocity = new Vector2(xVelocity, yVelocity);
    }

    /// <summary>
    /// 현재 x 속도를 목표 속도로 가속/감속한다. 같은 방향으로 미는 중이면 가속,
    /// 키를 뗐거나 반대로 꺾으면 감속 값을 써서 미끄러짐 없이 바로 멈추고 돈다.
    /// </summary>
    private float AccelerateX(float target)
    {
        // 대시 직후처럼 최고 속도를 넘은 값은 예전처럼 곧바로 최고 속도로 자른다. (대시 거리 유지)
        float current = Mathf.Clamp(_rb.linearVelocity.x, -speed, speed);

        bool pushing = Mathf.Abs(target) > 0.01f
                    && (Mathf.Abs(current) < 0.01f || Mathf.Sign(target) == Mathf.Sign(current));
        float rate = IsGrounded
            ? (pushing ? groundAcceleration : groundDeceleration)
            : (pushing ? airAcceleration : airDeceleration);

        if (rate <= 0f) return target; // 0 이면 예전처럼 즉시 반영
        return Mathf.MoveTowards(current, target, rate * Runner.DeltaTime);
    }

    /// <summary>
    /// 스킬 입력을 받아 발동을 시도한다.
    /// 반환값: 이번 틱에 토끼 강한 도약을 발동했으면 적용할 y속도(>0), 아니면 0 (Move 에서 적용).
    /// 여우 대시는 DashTimer 를 세팅해 이후 틱들에서 적용된다.
    /// </summary>
    private float TryUseSkill(NetworkButtons pressed)
    {
        // 눌린 순간의 슬롯만 (동시에 눌렸으면 낮은 번호 우선). 꾹 누르고 있어도 쿨다운 끝에 재발동하지 않는다.
        int slot = pressed.IsSet(InputButton.Skill1) ? 1
                 : pressed.IsSet(InputButton.Skill2) ? 2
                 : pressed.IsSet(InputButton.Skill3) ? 3 : 0;
        if (slot == 0) return 0f;
        if (!StageSkillRules.IsSlotAllowed(slot)) return 0f; // 스테이지에서 아직 열리지 않은 슬롯
        if (IsClimbing) return 0f;

        CharacterSkill active = SlotSkill(slot);
        if (active == CharacterSkill.None) return 0f;
        if (!SlotReady(slot)) return 0f; // 해당 슬롯 쿨다운 중

        return ExecuteSkillByType(active, slot);
    }

    private float ExecuteSkillByType(CharacterSkill active, int slot)
    {
        switch (active)
        {
            case CharacterSkill.FoxDash:
                bool poweredDash = ConsumePowerUp();
                DashTimer = TickTimer.CreateFromSeconds(Runner, poweredDash ? dashDuration * 1.35f : dashDuration);
                if (slot > 0) SetSlotCd(slot, dashCooldown);
                return 0f;

            case CharacterSkill.RabbitJump:
                if (!CanGroundJump) return 0f; // 땅에 있을 때만 도약 (코요테 타임 포함)
                ConsumeGroundJump();
                if (slot > 0) SetSlotCd(slot, jumpCooldown);
                SkillAirborne = true; // 연출: 도약 시작
                PoweredJump = ConsumePowerUp();
                return PoweredJump ? poweredJumpForce : superJumpForce;

            case CharacterSkill.FoxSniff:
                bool poweredSniff = ConsumePowerUp();
                if (slot > 0) SetSlotCd(slot, sniffCooldown);
                SniffCount++;
                DoSniff(poweredSniff);
                return 0f;

            case CharacterSkill.RabbitCarrot:
                if (slot > 0) SetSlotCd(slot, carrotCooldown);
                DoCarrot(ConsumePowerUp());
                return 0f;

            case CharacterSkill.FoxSwap:
                var partner = FindSwapPartner();
                if (partner == null) return 0f; // 바꿀 상대가 없으면 쿨다운 소모 없이 무시
                if (slot > 0) SetSlotCd(slot, swapCooldown);
                DoSwap(partner);
                return 0f;

            case CharacterSkill.RabbitPowerUp:
                if (IsPoweredUp) return 0f; // 이미 강화 상태면 중복 사용하지 않는다
                if (slot > 0) SetSlotCd(slot, powerUpCooldown);
                var puTimer = TickTimer.CreateFromSeconds(Runner, powerUpDuration);
                PowerUpTimer = puTimer;
                if (Runner != null && Runner.IsServer)
                {
                    foreach (var p in ActivePlayers())
                    {
                        if (p != null && p != this && !p.IsDead)
                        {
                            p.PowerUpTimer = puTimer;
                        }
                    }
                }
                return 0f;

            case CharacterSkill.FoxShadow:
                bool poweredShadow = ConsumePowerUp();
                if (slot > 0) SetSlotCd(slot, shadowCooldown);
                DoFoxShadow(poweredShadow);
                return 0f;

            case CharacterSkill.RabbitStomp:
                bool poweredStomp = ConsumePowerUp();
                if (slot > 0) SetSlotCd(slot, stompCooldown);
                DoRabbitStomp(poweredStomp);
                return 0f;

            default:
                return 0f;
        }
    }

    /// <summary>
    /// 음성 명령("그림자"/"분신", "스톰프"/"쿵")이나 특수 트리거로 FoxShadow / RabbitStomp 를 호출한다.
    /// </summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestExtraSkill(CharacterSkill skill)
    {
        if (IsDead || IsClimbing || IsIntroLocked()) return;
        if (Time.time < _extraSkillReadyAt) return;

        bool isFox = IsFoxCharacter;
        if (skill == CharacterSkill.FoxShadow && !isFox) return;
        if (skill == CharacterSkill.RabbitStomp && isFox) return;

        float cd = GetSkillCooldownTotal(skill);
        _extraSkillReadyAt = Time.time + Mathf.Max(1.0f, cd);
        ExecuteSkillByType(skill, 0);
    }

    // 여우 그림자 분신(FoxShadow): 현재 자리에 일정 시간 유지되는 그림자 분신을 남겨 압력판이나 레버를 대신 작동시킨다.
    private void DoFoxShadow(bool powered)
    {
        float dur = powered ? shadowDuration * 1.5f : shadowDuration;
        if (Runner != null && Runner.IsServer)
        {
            if (powered) DoSniff(true); // 강화 분신은 주변 숨김 오브젝트도 함께 감지
            RPC_SpawnFoxShadow(transform.position, FacingRight, dur, powered);
        }
        else
        {
            FoxShadowClone.SpawnClone(this, transform.position, FacingRight, dur, powered);
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_SpawnFoxShadow(Vector3 pos, NetworkBool facingRight, float duration, NetworkBool powered)
    {
        FoxShadowClone.SpawnClone(this, pos, facingRight, duration, powered);
    }

    // 토끼 쿵(RabbitStomp): 공중이면 급강하하여 착지 시 충격파를 일으키고, 지상에서는 즉시 바닥을 내리찍어 균열 블록 파괴 및 레버를 작동시킨다.
    private void DoRabbitStomp(bool powered)
    {
        if (!IsGrounded)
        {
            _stompAirborne = true;
            _stompPowered = powered;
            if (_rb != null)
            {
                _rb.linearVelocity = new Vector2(_rb.linearVelocity.x * 0.25f, -stompDownSpeed);
            }
        }
        else if (Runner != null && Runner.IsServer)
        {
            if (_rb != null)
            {
                _rb.linearVelocity = new Vector2(_rb.linearVelocity.x * 0.3f, 5.2f);
            }
            TriggerStompImpactOnServer(powered);
        }
    }

    private void TriggerStompImpactOnServer(bool powered)
    {
        float radius = powered ? stompShockwaveRadius * 1.5f : stompShockwaveRadius;
        Vector2 origin = transform.position;
        float rSq = radius * radius;

        // 1. 반경 내 드러난 레버(LeverSwitch) 충격파 토글 (네트워크 권한: 호스트)
        var levers = FindObjectsByType<LeverSwitch>(FindObjectsSortMode.None);
        foreach (var lv in levers)
        {
            if (lv != null && (((Vector2)lv.transform.position - origin).sqrMagnitude <= rSq))
            {
                lv.TryToggleOnServer();
            }
        }

        // 2. 강화 스톰프는 진동으로 반경 내 숨겨진 오브젝트도 잠시 드러냄
        if (powered)
        {
            var revealables = FindObjectsByType<Revealable>(FindObjectsSortMode.None);
            foreach (var rv in revealables)
            {
                if (rv != null && rv.IsWithinRange(origin, radius * 1.15f))
                {
                    rv.Reveal(3.0f);
                }
            }
        }

        // 3. 모든 피어에서 균열 블록(BreakableCrackedBlock) 파괴 및 충격파 VFX/SFX 재생
        RPC_PlayStompImpact(origin, radius, powered);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_PlayStompImpact(Vector2 origin, float radius, NetworkBool powered)
    {
        SfxPlayer.Stomp();

        // 반경 내 균열 블록(BreakableCrackedBlock) 파괴 (모든 클라이언트 동기화)
        var blocks = FindObjectsByType<BreakableCrackedBlock>(FindObjectsSortMode.None);
        foreach (var b in blocks)
        {
            if (b != null) b.TryBreak(origin, radius, powered);
        }

        var fx = SkillFxLibrary.Instance;
        if (fx != null)
        {
            int slId = _sr != null ? _sr.sortingLayerID : 0;
            Vector3 feet = new Vector3(origin.x, origin.y - 0.35f, 0f);
            Color tint = powered ? new Color(1f, 0.82f, 0.22f, 0.95f) : new Color(0.45f, 0.88f, 1f, 0.9f);
            if (fx.sniffRing != null && fx.sniffRing.Length > 0)
            {
                float ringScale = Mathf.Min(fx.sniffRingMaxScale, (radius * 2f) / Mathf.Max(0.01f, fx.sniffRingBaseDiameter));
                SpriteEffect.Spawn(fx.sniffRing, feet, fx.sniffRingFps * 1.35f, ringScale, false, slId, fx.sortingOrder, tint);
            }
            if (fx.powerUpRing != null && fx.powerUpRing.Length > 0)
            {
                SpriteEffect.Spawn(fx.powerUpRing, feet, fx.powerUpRingFps * 1.2f, fx.powerUpRingScale * (powered ? 1.4f : 1.05f), false, slId, fx.sortingOrder + 1, tint);
            }
        }

        if (Camera.main != null)
        {
            var cam = Camera.main.GetComponent<CameraFollow>();
            if (cam != null) cam.AddShake(powered ? 0.55f : 0.38f);
        }
    }

    // 파워업 상태면 소모하고 true. 여우/토끼 중 다음 스킬 사용자가 1회 강화 효과를 받는다.
    private bool ConsumePowerUp()
    {
        if (!IsPoweredUp) return false;
        PowerUpTimer = TickTimer.None;
        PowerSpendCount++;
        if (Runner != null && Runner.IsServer)
        {
            foreach (var p in ActivePlayers())
            {
                if (p != null && p != this)
                {
                    p.PowerUpTimer = TickTimer.None;
                }
            }
        }
        return true;
    }

    // 위치를 바꿀 상대: 가장 가까운 다른 플레이어 (swapMaxDistance > 0 이면 그 거리 안에서만)
    private PlayerMovement FindSwapPartner()
    {
        PlayerMovement best = null;
        float bestSq = float.MaxValue;
        float maxSq = swapMaxDistance > 0f ? swapMaxDistance * swapMaxDistance : float.MaxValue;

        foreach (var p in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
        {
            if (p == null || p == this) continue;
            if (p.Object == null || !p.Object.IsValid || p._rb == null) continue;
            if (p.IsDead) continue;

            float sq = ((Vector2)(p.transform.position - transform.position)).sqrMagnitude;
            if (sq > maxSq || sq >= bestSq) continue;
            best = p;
            bestSq = sq;
        }
        return best;
    }

    // 두 플레이어의 위치를 맞바꾼다. 호스트가 모든 플레이어의 StateAuthority 를 가지므로 호스트에서 실행.
    private void DoSwap(PlayerMovement other)
    {
        if (!Runner.IsServer) return;

        Vector3 myPos = transform.position;
        Vector3 otherPos = other.transform.position;
        myPos.z = otherPos.z = 0f;

        TeleportTo(otherPos);
        other.TeleportTo(myPos);

        SwapFrom = myPos; SwapTo = otherPos;
        other.SwapFrom = otherPos; other.SwapTo = myPos;
        SwapCount++;
        other.SwapCount++;
        Debug.Log($"<color=cyan>[Swap]</color> {name} <-> {other.name} 위치 교환");
    }

    private void TeleportTo(Vector3 pos)
    {
        // transform.position 직접 대입은 다음 틱에 되돌아가므로 반드시 Teleport 를 쓴다. (Respawn 참고)
        if (_nrb != null) _nrb.Teleport(pos);
        else transform.position = pos;

        _rb.linearVelocity = Vector2.zero;
        DashTimer = TickTimer.None;
        SkillAirborne = false;
        PoweredJump = false;
        _stompAirborne = false;
        _stompPowered = false;
    }

    public struct SkillSlotStatus
    {
        public CharacterSkill Skill;
        public float RemainingSeconds;
        public float TotalSeconds;
        public float NormalizedProgress; // 0: 준비완료, 1: 방금 사용
        public bool IsReady;
    }

    public CharacterSkill GetSlotSkill(int slot) => SlotSkill(slot);

    public float GetSkillCooldownTotal(CharacterSkill skill)
    {
        switch (skill)
        {
            case CharacterSkill.FoxDash: return dashCooldown;
            case CharacterSkill.RabbitJump: return jumpCooldown;
            case CharacterSkill.FoxSniff: return sniffCooldown;
            case CharacterSkill.RabbitCarrot: return carrotCooldown;
            case CharacterSkill.FoxSwap: return swapCooldown;
            case CharacterSkill.RabbitPowerUp: return powerUpCooldown;
            case CharacterSkill.FoxShadow: return shadowCooldown;
            case CharacterSkill.RabbitStomp: return stompCooldown;
            default: return 0f;
        }
    }

    public SkillSlotStatus GetSkillSlotStatus(int slot)
    {
        CharacterSkill skill = SlotSkill(slot);
        if (skill == CharacterSkill.None)
        {
            return new SkillSlotStatus { Skill = CharacterSkill.None, IsReady = false, RemainingSeconds = 0f, TotalSeconds = 0f, NormalizedProgress = 0f };
        }

        float total = GetSkillCooldownTotal(skill);
        if (Runner == null || !Runner.IsRunning)
        {
            return new SkillSlotStatus { Skill = skill, IsReady = true, RemainingSeconds = 0f, TotalSeconds = total, NormalizedProgress = 0f };
        }

        TickTimer timer = default;
        switch (slot)
        {
            case 1: timer = SkillCd1; break;
            case 2: timer = SkillCd2; break;
            case 3: timer = SkillCd3; break;
            default: return new SkillSlotStatus { Skill = CharacterSkill.None };
        }

        float? rem = timer.RemainingTime(Runner);
        float remaining = (rem.HasValue && rem.Value > 0f) ? rem.Value : 0f;
        bool isReady = timer.ExpiredOrNotRunning(Runner);
        float norm = (total > 0f && !isReady) ? Mathf.Clamp01(remaining / total) : 0f;

        return new SkillSlotStatus
        {
            Skill = skill,
            RemainingSeconds = remaining,
            TotalSeconds = total,
            NormalizedProgress = norm,
            IsReady = isReady
        };
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
    private void DoSniff(bool powered = false)
    {
        if (!Runner.IsServer) return;
        var all = FindObjectsByType<Revealable>(FindObjectsSortMode.None);
        Vector2 origin = transform.position;
        float radius = powered ? sniffRadius * 1.5f : sniffRadius;
        float duration = powered ? sniffRevealDuration * 1.35f : sniffRevealDuration;
        int revealed = 0;
        foreach (var rv in all)
        {
            // 거리 판정은 Revealable 이 직접 한다. 타일맵 숨김 발판처럼 오브젝트 위치와
            // 실제 내용의 위치가 다른 경우가 있어서 여기서 transform 으로 재면 안 된다.
            if (rv.IsWithinRange(origin, radius))
            {
                rv.Reveal(duration);
                revealed++;
            }
        }
        Debug.Log($"<color=cyan>[Sniff]</color> 반경 {radius} 안 {revealed}/{all.Length}개를 드러냈습니다.");
    }

    // 정면으로 당근 발사 (호스트가 네트워크 오브젝트 스폰). big = 파워업으로 강화된 큰 당근
    private void DoCarrot(bool big)
    {
        if (!Runner.IsServer) return;
        if (carrotPrefab == null) return;
        float dir = FacingRight ? 1f : -1f;
        float offset = carrotForwardOffset * (big ? bigCarrotScale : 1f);
        Vector3 pos = transform.position + new Vector3(dir * offset, 0f, 0f);
        float scale = big ? bigCarrotScale : 1f;
        // 크기는 Spawned() 전에 정해야 모든 클라이언트가 첫 프레임부터 같은 크기로 본다.
        var obj = Runner.Spawn(carrotPrefab, pos, Quaternion.identity, Object.InputAuthority,
            (runner, no) => { var c = no.GetComponent<CarrotProjectile>(); if (c != null) c.SizeScale = scale; });
        var carrot = obj != null ? obj.GetComponent<CarrotProjectile>() : null;
        if (carrot != null) carrot.Launch(FacingRight);
    }

    // 사다리에서 점프로 빠져나가면 그 점프 입력은 여기서 소비한다(pressed 에서 지움).
    private void HandleLadder(NetworkInputData data, ref NetworkButtons pressed)
    {
        if (_isOnLadder)
        {
            IsClimbing = true;
            _rb.gravityScale = 0f;
            JumpCutReady = false; // 사다리를 잡으면 이전 점프의 가변 점프 판정은 끝

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
            if (pressed.IsSet(InputButton.Jump))
            {
                pressed.Set(InputButton.Jump, false);
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

    /// <summary>
    /// 지금 밟고 있는 칸이 점프대 타일인지. 점프 입력이 들어온 순간에만 검사한다.
    /// 밟고 있는 콜라이더의 타일맵에서 발밑 셀을 찾아 타일을 비교한다.
    /// </summary>
    private bool IsOnLauncher()
    {
        if (launcherTile == null || groundCheck == null) return false;

        // 사다리 등 다른 콜라이더가 같이 걸릴 수 있으므로 겹친 것을 모두 본다.
        var cols = Physics2D.OverlapCircleAll(groundCheck.position, groundCheckRadius, groundLayer);
        if (cols == null || cols.Length == 0) return false;

        Vector3 probe = groundCheck.position + Vector3.down * launcherProbeDepth;
        foreach (var col in cols)
        {
            if (col == null) continue;
            // 지형 타일맵은 Tilemap + TilemapCollider2D + CompositeCollider2D 가 한 오브젝트에 있다.
            var tilemap = col.GetComponent<Tilemap>();
            if (tilemap == null) continue;
            if (tilemap.GetTile(tilemap.WorldToCell(probe)) == launcherTile) return true;
        }
        return false;
    }

    // �޼��� �߰�
    /// <summary>
    /// 낙사 기준선을 스테이지 지형에서 산출한다.
    /// groundLayer 에 속한 고정 콜라이더들의 최하단에서 fallDeathMargin 만큼 더 아래가 기준선이다.
    /// 스테이지마다 맵 깊이가 다르므로(Main Stage1 은 y=-51 까지 내려간다) 인스펙터 고정값을 쓰면 안 된다.
    /// </summary>
    private float ResolveFallDeathY()
    {
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (_fallLineScene == scene) return _fallLineY;

        float minY = float.MaxValue;
        var all = FindObjectsByType<Collider2D>(FindObjectsSortMode.None);
        foreach (var col in all)
        {
            if (col == null || !col.enabled || col.isTrigger) continue;
            if ((groundLayer.value & (1 << col.gameObject.layer)) == 0) continue;
            // 런타임에 스폰되는 네트워크 오브젝트(당근 발판 등)는 지형이 아니다.
            if (col.GetComponentInParent<NetworkObject>() != null) continue;
            minY = Mathf.Min(minY, col.bounds.min.y);
        }

        _fallLineScene = scene;
        _fallLineY = (minY < float.MaxValue) ? minY - fallDeathMargin : fallDeathY;

        if (minY < float.MaxValue)
            Debug.Log($"<color=orange>[FallDeath]</color> {scene}: 지형 최하단 {minY:0.0} -> 낙사선 {_fallLineY:0.0}");
        else
            Debug.LogWarning($"[FallDeath] {scene}: groundLayer 콜라이더를 찾지 못해 인스펙터 값 {fallDeathY} 를 유지합니다.");

        return _fallLineY;
    }

    private void CheckFallDeath()
    {
        if (!Object.HasStateAuthority) return;
        if (IsDead) return;

        if (transform.position.y < fallDeathY)
        {
            Die();
        }
    }

    public void Die()
    {
        // Trap 트리거는 Spawned() 전에도 들어올 수 있다.
        if (Object == null || Runner == null) return;
        if (!Object.HasStateAuthority) return;
        if (IsDead) return;
        if (!RespawnGrace.ExpiredOrNotRunning(Runner)) return; // 리스폰 직후 무적

        MarkDead();
        Debug.Log("<color=red>[PlayerMovement]</color> 플레이어 사망 -> 리스폰");

        // 협동 규칙: 한 명이 죽으면 다른 플레이어도 같이 죽어 체크포인트에서 함께 다시 시작한다.
        // 동반 사망은 상대의 리스폰 무적과 무관하게 적용한다.
        foreach (var p in ActivePlayers())
        {
            if (p == this || p.IsDead || !p.Object.HasStateAuthority) continue;
            p.MarkDead();
            Debug.Log($"<color=red>[PlayerMovement]</color> {p.name} 동반 사망");
        }
    }

    private void MarkDead()
    {
        IsDead = true;
        _respawnRequested = true; // 실제 리스폰은 FixedUpdateNetwork 안에서
    }

    /// <summary>
    /// 지금 스테이지에 스폰돼 있는 플레이어들. 협동 판정(동반 사망, 체크포인트/골 동시 도달)에 쓴다.
    /// </summary>
    public static System.Collections.Generic.List<PlayerMovement> ActivePlayers()
    {
        var list = new System.Collections.Generic.List<PlayerMovement>();
        foreach (var p in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
        {
            if (p == null || p.Object == null || !p.Object.IsValid) continue;
            list.Add(p);
        }
        return list;
    }

    /// <summary>
    /// reached 에 지금 스테이지의 플레이어가 모두 들어 있으면 true.
    /// 스테이지에 혼자면(솔로 테스트) 그 한 명만 도달해도 true.
    /// </summary>
    public static bool AllPlayersReached(System.Collections.Generic.HashSet<PlayerMovement> reached)
    {
        var players = ActivePlayers();
        if (players.Count == 0) return reached.Count > 0; // 네트워크 없이 돌리는 경우
        foreach (var p in players)
            if (!reached.Contains(p)) return false;
        return true;
    }

    /// <summary>
    /// 플레이어끼리 같은 좌표에 겹쳐 나오면 콜라이더가 서로 밀어내며 지형에 낑긴다.
    /// ActivePlayers 를 PlayerId 순으로 정렬한 순번만큼 x 로 벌린다 (ID 홀짝과 무관하게 항상 서로 다름).
    /// </summary>
    public const float SpawnSpacingX = 1.5f;

    public static float SpawnOffsetX(NetworkRunner runner, PlayerRef player)
    {
        if (runner == null || !player.IsRealPlayer) return 0f;
        int slot = 0;
        foreach (var p in runner.ActivePlayers)
            if (p.PlayerId < player.PlayerId) slot++;
        return slot * SpawnSpacingX;
    }

    private void Respawn()
    {
        Vector3 pos = CheckpointManager.hasCheckpoint
            ? CheckpointManager.lastCheckpointPosition
            : Vector3.zero;
        pos.x += SpawnOffsetX(Runner, Object.InputAuthority);
        pos.z = 0f;

        // transform.position 직접 대입은 NetworkRigidbody2D 가 캡처하는 RBPosition 과
        // 네트워크 상태를 갱신하지 않아 다음 틱에 원위치로 되돌아간다. 반드시 Teleport 를 쓴다.
        if (_nrb != null)
            _nrb.Teleport(pos);
        else
            transform.position = pos;

        _rb.linearVelocity = Vector2.zero;
        RespawnGrace = TickTimer.CreateFromSeconds(Runner, respawnGraceSeconds);
        IsDead = false;
    }

    // ESC 메뉴의 "다시하기": 요청한 플레이어의 캐릭터만 마지막 체크포인트로 리스폰.
    // 실제 리스폰은 StateAuthority(호스트)가 수행하므로, 입력 권한자가 호스트에 요청한다.
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestRespawn()
    {
        _respawnRequested = true; // FixedUpdateNetwork 에서 기존 Respawn() 경로로 처리
    }

    /// <summary>
    /// 로컬 플레이어(입력 권한을 가진 내 캐릭터)를 마지막 체크포인트로 리스폰 요청한다.
    /// 인게임에서 내 캐릭터가 없으면 아무 것도 하지 않는다(안전).
    /// </summary>
    public static bool RequestLocalRespawn()
    {
        var all = FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None);
        foreach (var p in all)
        {
            if (p != null && p.Object != null && p.Object.HasInputAuthority)
            {
                p.RPC_RequestRespawn();
                return true;
            }
        }
        return false;
    }

    public override void Render()
    {
        UpdateVisuals();
    }

    private bool _prevRespawnGraceVis;

    private void UpdateVisuals()
    {
        // 1. 바라보는 방향에 따라 스프라이트 반전 (호스트/클라이언트 완벽 동기화)
        if (_sr != null)
        {
            _sr.flipX = !FacingRight;
        }

        // 2. 점프 / 착지 / 사망 및 리스폰 효과음 엣지 감지
        if (_rb != null && Runner != null && Runner.IsRunning)
        {
            bool grounded = IsGrounded;
            bool dead = IsDead;
            bool graceActive = !RespawnGrace.ExpiredOrNotRunning(Runner);
            float vy = _rb.linearVelocity.y;

            if (!_visInitialized)
            {
                _visInitialized = true;
                _prevGroundedVis = grounded;
                _prevDeadVis = dead;
                _prevRespawnGraceVis = graceActive;
                _prevVelYVis = vy;
            }
            else
            {
                // 사망 -> 리스폰 무적 타이머가 새로 켜진 순간 (또는 IsDead 상승 엣지)
                if ((dead && !_prevDeadVis) || (graceActive && !_prevRespawnGraceVis))
                {
                    if (Object != null && Object.HasInputAuthority)
                    {
                        SfxPlayer.Death();
                        SfxPlayer.Respawn();
                    }
                }
                else
                {
                    // 기본 점프 상승 엣지 (토끼 슈퍼점프는 PlayerSkillVisuals 에서 별도 재생)
                    if (_prevGroundedVis && !grounded && vy > 3.5f && !SkillAirborne && !IsClimbing)
                    {
                        SfxPlayer.Jump();
                    }
                    // 일반 착지 엣지 (토끼 슈퍼점프/스톰프 착지는 각 스킬 연출에서 재생)
                    else if (!_prevGroundedVis && grounded && _prevVelYVis < -2.0f && !SkillAirborne && !_stompAirborne)
                    {
                        SfxPlayer.Land();
                    }
                }

                _prevGroundedVis = grounded;
                _prevDeadVis = dead;
                _prevRespawnGraceVis = graceActive;
                _prevVelYVis = vy;
            }
        }

        // 3. 애니메이션 파라미터 갱신 (원격 플레이어와 로컬 플레이어 모두 실제 속도 기반으로 자연스럽게 재생)
        if (_anim != null && _rb != null)
        {
            if (IsClimbing)
            {
                _anim.SetFloat("Speed", 0f);
                float climbAnimSpeed = Mathf.Abs(_rb.linearVelocity.y) > 0.05f ? 1f : 0f;
                _anim.SetFloat("ClimbSpeed", climbAnimSpeed);
            }
            else
            {
                float hSpeed = Mathf.Abs(_rb.linearVelocity.x);
                float animSpeed = hSpeed > 0.1f ? Mathf.Clamp01(hSpeed / speed) : 0f;
                _anim.SetFloat("Speed", animSpeed);
                _anim.SetFloat("ClimbSpeed", 0f);
            }

            _anim.SetBool("IsGrounded", IsGrounded);
            _anim.SetBool("IsClimbing", IsClimbing);
            _anim.SetFloat("YVelocity", IsClimbing ? 0f : _rb.linearVelocity.y);
        }
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
            // 디스폰/셧다운 중 콜라이더 파괴로 이 콜백이 불릴 때 [Networked] 세터 접근 예외 방지.
            if (NetworkedAccessSafe) IsClimbing = false;

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