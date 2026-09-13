using Fusion;
using UnityEngine;

/// <summary>
/// 토끼 Carrot 스킬: 정면으로 날아가 벽/바닥에 닿으면 고정되어 일정 시간 "발판"이 되는 네트워크 발사체.
/// - 비행 중: 콜라이더 트리거(밀지 않음), 진행 방향 레이캐스트로 벽 감지.
/// - 고정: 솔리드 콜라이더 + Ground 레이어 → 플레이어가 밟고 설 수 있음. stickDuration 후 소멸.
/// - 이동/판정은 호스트(StateAuthority)에서, 콜라이더/레이어 상태는 네트워크 Stuck 기반으로 전 클라이언트 반영.
/// </summary>
public class CarrotProjectile : NetworkBehaviour
{
    [Networked] public NetworkBool FacingRight { get; set; }
    [Networked] public NetworkBool Stuck { get; set; }
    [Networked] private TickTimer Life { get; set; }
    [Networked] private TickTimer FlightTimeout { get; set; }

    [Header("Flight")]
    public float speed = 12f;
    public float maxFlightTime = 1.5f;   // 벽을 못 만나면 소멸
    public float wallCheckDist = 0.4f;
    public LayerMask wallMask;           // 벽/바닥 (Ground)

    [Header("Stick")]
    public float stickDuration = 4f;     // 발판 유지 시간
    public string stuckLayerName = "Ground";

    [Header("Effects")]
    public Sprite[] launchFx;             // 던질 때 먼지
    public Sprite[] impactFx;             // 벽에 박힐 때 먼지
    public float fxFps = 22f;
    public float launchFxScale = 1.1f;
    public float impactFxScale = 1.4f;
    public float impactShake = 0.25f;     // 착탄 화면 흔들림(로컬 카메라)
    public int fxSortingOrder = 6;

    private Collider2D _col;
    private SpriteRenderer _sr;
    private int _stuckLayer;
    private int _flyLayer;
    private bool _prevStuck;
    private bool _launchDone;

    public override void Spawned()
    {
        _col = GetComponent<Collider2D>();
        _sr = GetComponent<SpriteRenderer>();
        _stuckLayer = LayerMask.NameToLayer(stuckLayerName);
        if (_stuckLayer < 0) _stuckLayer = gameObject.layer;
        _flyLayer = gameObject.layer;

        if (Object.HasStateAuthority)
            FlightTimeout = TickTimer.CreateFromSeconds(Runner, maxFlightTime);

        ApplyStuckState();
    }

    /// <summary>스폰 직후 호스트가 발사 방향을 지정.</summary>
    public void Launch(bool facingRight)
    {
        if (Object.HasStateAuthority) FacingRight = facingRight;
    }

    public override void FixedUpdateNetwork()
    {
        ApplyStuckState(); // 시뮬레이션(호스트/예측 클라)에서 콜라이더 상태 일치

        if (!Object.HasStateAuthority) return;

        if (!Stuck)
        {
            float dir = FacingRight ? 1f : -1f;
            float step = speed * Runner.DeltaTime;
            var hit = Physics2D.Raycast(transform.position, new Vector2(dir, 0f), step + wallCheckDist, wallMask);
            if (hit.collider != null)
            {
                Vector3 pp = transform.position;
                pp.x = hit.point.x - dir * (wallCheckDist * 0.5f);
                transform.position = pp;
                Stuck = true;
                Life = TickTimer.CreateFromSeconds(Runner, stickDuration);
            }
            else
            {
                transform.position += new Vector3(dir * step, 0f, 0f);
                if (FlightTimeout.Expired(Runner)) Runner.Despawn(Object);
            }
        }
        else if (Life.Expired(Runner))
        {
            Runner.Despawn(Object);
        }
    }

    public override void Render()
    {
        ApplyStuckState();
        if (_sr != null) _sr.flipX = !FacingRight;

        // 던지기 먼지 (등장 시 1회)
        if (!_launchDone)
        {
            _launchDone = true;
            SpawnFx(launchFx, transform.position, launchFxScale, !FacingRight);
        }

        // 벽에 박히는 순간 (Stuck 엣지) → 먼지 + 화면 흔들림
        if (Stuck && !_prevStuck)
        {
            SpawnFx(impactFx, transform.position, impactFxScale, false);
            var cam = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
            if (cam != null) cam.AddShake(impactShake);
        }
        _prevStuck = Stuck;
    }

    private void SpawnFx(Sprite[] frames, Vector3 pos, float scale, bool flip)
    {
        int sl = _sr != null ? _sr.sortingLayerID : 0;
        SpriteEffect.Spawn(frames, pos, fxFps, scale, flip, sl, fxSortingOrder, Color.white);
    }

    private void ApplyStuckState()
    {
        bool stuck = Stuck;
        if (_col != null) _col.isTrigger = !stuck;      // 고정되면 솔리드 발판
        int L = stuck ? _stuckLayer : _flyLayer;
        if (gameObject.layer != L) gameObject.layer = L;
    }
}
