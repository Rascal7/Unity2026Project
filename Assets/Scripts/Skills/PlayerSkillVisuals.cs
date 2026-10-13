using UnityEngine;
using Fusion;

/// <summary>
/// 스킬 연출(로컬 렌더 전용). 물리/판정에는 전혀 영향 없음.
/// 여우 대시: 스트레치 + 잔상 + 카메라 킥.
/// 토끼 도약: 발사 스트레치 + 착지 스쿼시 + 화면 흔들림 (회전 없음).
/// 스프라이트 변형은 전용 SkillVisual 차일드로만 처리 → 콜라이더/네임태그/그라운드체크에 영향 없음.
/// 상태는 [Networked](DashTimer/SkillAirborne) 기반이라 모든 클라이언트가 동일하게 본다(연출은 각자 로컬 렌더).
/// </summary>
public class PlayerSkillVisuals : MonoBehaviour
{
    [Header("여우 대시 - 스트레치")]
    public Vector2 dashStretch = new Vector2(1.4f, 0.7f);
    public float stretchSharpness = 25f;

    [Header("여우 대시 - 잔상")]
    public float afterimageInterval = 0.03f;
    public float afterimageLife = 0.28f;
    public Color afterimageTint = new Color(0.7f, 0.85f, 1f, 0.55f);

    [Header("여우 대시 - 카메라 킥")]
    public float camKick = 0.7f;

    [Header("토끼 도약 - 스쿼시/스트레치")]
    public Vector2 launchStretch = new Vector2(0.7f, 1.4f); // 발사: 세로로 쭉
    public Vector2 landSquash = new Vector2(1.45f, 0.6f);   // 착지: 납작
    public float landRecoverTime = 0.14f;

    [Header("토끼 도약 - 착지 흔들림")]
    public float landShake = 0.35f;  // 착지 화면 흔들림 세기

    [Header("이펙트 프레임 (슬라이스된 스프라이트)")]
    public Sprite[] dashSmokeFrames;     // 여우 대시 먼지 (바닥먼지)
    public float dashSmokeFps = 20f;
    public float dashSmokeScale = 1.6f;
    public float dashSmokeBackOffset = 0.4f; // 대시 반대방향(뒤)으로 이만큼 이동
    public float dashSmokeLift = 0.18f;      // 발밑에서 살짝 위로
    [Tooltip("대시 중 경로를 따라 먼지를 남김")]
    public bool dashDustTrail = true;
    public float dashDustInterval = 0.03f;
    public float dashDustScale = 1.1f;
    public Sprite[] jumpLaunchFrames;    // 토끼 발사 먼지 (바닥먼지)
    public float jumpLaunchFps = 22f;
    public float jumpLaunchScale = 1.2f;
    public Sprite[] jumpLandFrames;      // 토끼 착지 poof (pimen)
    public float jumpLandFps = 24f;
    public float jumpLandScale = 1.3f;

    [Header("토끼 파워업 - 강화 상태 발광")]
    public Color powerUpTint = new Color(1f, 0.6f, 0.25f, 1f);
    public float powerUpPulseSpeed = 9f;

    [Header("여우 체인지 - 카메라")]
    public float swapShake = 0.2f;
    [Tooltip("가까운 교환(화면 안): 이 시간 동안 빠르게 따라간다")]
    public float swapFastFollowSeconds = 0.3f;
    [Tooltip("먼 교환(화면 밖): 바로 넘어간 뒤 이 비율만큼 줌인했다 풀린다")]
    public float swapFarZoomIn = 0.1f;

    [Header("스킬 2·3 카메라")]
    [Tooltip("여우 킁킁 발동 시 줌인 비율")]
    public float sniffZoomIn = 0.06f;
    [Tooltip("토끼 도약 발사 시 화면을 아래로 미는 세기")]
    public float jumpLaunchCamKick = 0.3f;
    [Tooltip("토끼 강화 도약 중 줌아웃 비율")]
    public float poweredJumpZoomOut = 0.08f;
    [Tooltip("토끼 파워업(당근 먹기) 순간 줌인 비율")]
    public float powerUpZoomIn = 0.08f;
    public float powerUpShake = 0.15f;
    [Tooltip("큰 당근 던질 때 던진 방향 카메라 킥")]
    public float bigCarrotCamKick = 0.4f;

    [Header("이펙트 공통")]
    public Color effectTint = Color.white;
    public int effectSortingOrder = 6;   // 플레이어(4) 앞
    public float groundOffset = 0f;      // 발밑 기준 추가 보정(+위/-아래)

    private PlayerMovement _pm;
    private NetworkObject _no;
    private SpriteRenderer _sr;
    private SpriteRenderer _skillVisual; // 변형/회전 전용(콜라이더 영향 없음)

    private bool _prevDashing;
    private bool _prevAirborne;
    private float _aiTimer;
    private float _landTimer;
    private float _dashDustTimer;
    private Vector3 _visualScale = Vector3.one;
    private int _prevSwapCount = -1;
    private int _prevSniffCount = -1;
    private int _prevPowerSpend = -1;
    private bool _wasPoweredUp;
    private bool _holdingZoom;
    private Color _baseColor = Color.white;
    private SpriteRenderer _aura;   // 파워업 유지 중 발밑 불꽃 링(반복 재생)
    private float _auraT;

    private void Awake()
    {
        _pm = GetComponent<PlayerMovement>();
        _no = GetComponent<NetworkObject>();
        _sr = GetComponent<SpriteRenderer>();
        if (_sr != null) _baseColor = _sr.color;

        var go = new GameObject("SkillVisual");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        _skillVisual = go.AddComponent<SpriteRenderer>();
        _skillVisual.enabled = false;
    }

    private void LateUpdate()
    {
        if (_pm == null || _sr == null) return;

        bool dashing = _pm.IsDashingNow;
        bool airborne = _pm.IsSkillAirborne;
        bool localPlayer = (_no == null || _no.HasInputAuthority);
        float dt = Time.deltaTime;
        CameraFollow cam = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;

        int slId = _sr.sortingLayerID;
        // 발밑 = 스프라이트 경계 하단(캐릭터/프레임마다 자동 적응) + 보정
        Vector3 feet = new Vector3(transform.position.x, _sr.bounds.min.y + groundOffset, transform.position.z);

        // --- 이벤트 엣지 ---
        if (dashing && !_prevDashing)           // 여우 대시 시작 → 바닥먼지(뒤쪽) + 대시 효과음
        {
            SfxPlayer.Dash();
            Vector3 dashPos = feet + Vector3.right * (_pm.IsFacingRight ? -dashSmokeBackOffset : dashSmokeBackOffset);
            dashPos.y += dashSmokeLift;
            SpriteEffect.Spawn(dashSmokeFrames, dashPos, dashSmokeFps, dashSmokeScale,
                _pm.IsFacingRight, slId, effectSortingOrder, effectTint);
            if (localPlayer && cam != null)
                cam.AddImpulse(new Vector2((_pm.IsFacingRight ? 1f : -1f) * camKick, 0f));
        }

        // 대시 중 먼지 트레일 (경로를 따라 먼지가 인다) - 밋밋함 보완
        if (dashing && dashDustTrail)
        {
            _dashDustTimer -= dt;
            if (_dashDustTimer <= 0f)
            {
                _dashDustTimer = dashDustInterval;
                Vector3 tp = new Vector3(feet.x, feet.y + dashSmokeLift, feet.z);
                SpriteEffect.Spawn(dashSmokeFrames, tp, dashSmokeFps, dashDustScale,
                    _pm.IsFacingRight, slId, effectSortingOrder, effectTint);
            }
        }
        else _dashDustTimer = 0f;

        if (airborne && !_prevAirborne)         // 토끼 발사 + 슈퍼점프 효과음
        {
            SfxPlayer.SuperJump();
            _visualScale = new Vector3(launchStretch.x, launchStretch.y, 1f);
            SpriteEffect.Spawn(jumpLaunchFrames, feet, jumpLaunchFps, jumpLaunchScale,
                false, slId, effectSortingOrder, effectTint);
            if (localPlayer && cam != null) cam.AddImpulse(new Vector2(0f, -jumpLaunchCamKick)); // 튕겨 오르는 느낌
        }
        if (!airborne && _prevAirborne)         // 토끼 착지 + 착지 효과음
        {
            SfxPlayer.Land();
            _visualScale = new Vector3(landSquash.x, landSquash.y, 1f);
            _landTimer = landRecoverTime;
            SpriteEffect.Spawn(jumpLandFrames, feet, jumpLandFps, jumpLandScale,
                false, slId, effectSortingOrder, effectTint);
            if (localPlayer && cam != null) cam.AddShake(landShake);
        }

        bool valid = _pm.Object != null && _pm.Object.IsValid;
        var fx = SkillFxLibrary.Instance;
        float feetToCenter = transform.position.y - feet.y;

        // 토끼 강화 도약: 공중에 있는 동안 살짝 줌아웃해서 더 높은 곳을 보여준다
        if (localPlayer && cam != null)
        {
            bool hold = valid && airborne && _pm.IsPoweredJump;
            if (hold != _holdingZoom)
            {
                _holdingZoom = hold;
                cam.SetZoomHold(hold ? poweredJumpZoomOut : 0f);
            }
        }

        // 여우 킁킁: 냄새 파동(킁킁 반경만큼 퍼지는 링) + 코 앞 냄새 연기 + 살짝 줌인 + 킁킁 효과음
        int sniffCount = valid ? _pm.SniffCounter : 0;
        if (_prevSniffCount >= 0 && sniffCount != _prevSniffCount)
        {
            SfxPlayer.Sniff();
            if (fx != null)
            {
                float ringScale = Mathf.Min(fx.sniffRingMaxScale,
                    (_pm.sniffRadius * 2f) / Mathf.Max(0.01f, fx.sniffRingBaseDiameter));
                SpriteEffect.Spawn(fx.sniffRing, transform.position, fx.sniffRingFps, ringScale,
                    false, slId, fx.sortingOrder, fx.sniffTint);
                float dir = _pm.IsFacingRight ? 1f : -1f;
                Vector3 nose = new Vector3(transform.position.x + dir * 0.45f, feet.y + feetToCenter * 1.3f, transform.position.z);
                SpriteEffect.Spawn(fx.sniffSmoke, nose, fx.sniffSmokeFps, fx.sniffSmokeScale,
                    !_pm.IsFacingRight, slId, fx.sortingOrder, fx.sniffTint);
            }
            if (localPlayer && cam != null) cam.AddZoomPunch(-sniffZoomIn);
        }
        _prevSniffCount = sniffCount;

        // 여우 체인지: 출발/도착 두 지점에 포털 + 도착 연기 + 워프 효과음. 상대방도 같은 연출을 자기 화면에서 본다.
        int swapCount = valid ? _pm.SwapCounter : 0;
        if (_prevSwapCount >= 0 && swapCount != _prevSwapCount)
        {
            SfxPlayer.Swap();
            Vector3 from = _pm.LastSwapFrom;
            Vector3 to = _pm.LastSwapTo;
            if (fx != null)
            {
                SpriteEffect.Spawn(fx.swapPortal, from, fx.swapPortalFps, fx.swapPortalScale, false, slId, fx.sortingOrder, Color.white);
                SpriteEffect.Spawn(fx.swapPortal, to, fx.swapPortalFps, fx.swapPortalScale, false, slId, fx.sortingOrder, Color.white);
                SpriteEffect.Spawn(fx.swapPuff, to + Vector3.down * feetToCenter, fx.swapPuffFps, fx.swapPuffScale, false, slId, fx.sortingOrder, Color.white);
            }

            if (localPlayer && cam != null)
            {
                Camera uc = cam.GetComponent<Camera>();
                float halfWidth = (uc != null && uc.orthographic) ? uc.orthographicSize * uc.aspect : 8f;
                if (Vector2.Distance(from, to) > halfWidth)
                {
                    // 화면 밖으로 멀리 이동: 바로 넘어가고 줌인했다 풀어서 장면 전환을 자연스럽게
                    cam.SnapToTarget(to);
                    cam.AddZoomPunch(-swapFarZoomIn);
                }
                else
                {
                    cam.FastFollow(swapFastFollowSeconds); // 가까우면 빠르게 따라가 이어지는 느낌
                }
                cam.AddShake(swapShake);
            }
        }
        _prevSwapCount = swapCount;

        // 토끼 파워업: 먹는 순간 퍼프+별 + 파워업 효과음, 강화 동안 발밑 불꽃 링 반복 + 주황빛 발광
        bool powered = valid && _pm.IsPoweredUp;
        if (powered)
        {
            if (!_wasPoweredUp)
            {
                SfxPlayer.PowerUp();
                if (fx != null)
                    SpriteEffect.Spawn(fx.powerUpBurst, transform.position, fx.powerUpBurstFps, fx.powerUpBurstScale, false, slId, fx.sortingOrder, Color.white);
                if (localPlayer && cam != null)
                {
                    cam.AddZoomPunch(-powerUpZoomIn);
                    cam.AddShake(powerUpShake);
                }
            }
            float t = 0.5f + 0.5f * Mathf.Sin(Time.time * powerUpPulseSpeed);
            _sr.color = Color.Lerp(_baseColor, powerUpTint, 0.35f + 0.45f * t);
        }
        else if (_wasPoweredUp)
        {
            _sr.color = _baseColor;
        }
        _wasPoweredUp = powered;
        UpdatePowerAura(powered, feet, fx);

        // 파워업 소모(큰 당근 / 강화 도약): 노란 충격 링 + 큰 당근이면 던진 방향으로 카메라 킥
        int spend = valid ? _pm.PowerSpendCounter : 0;
        if (_prevPowerSpend >= 0 && spend != _prevPowerSpend)
        {
            if (fx != null)
                SpriteEffect.Spawn(fx.powerUpRing, transform.position, fx.powerUpRingFps, fx.powerUpRingScale, false, slId, fx.sortingOrder, Color.white);
            if (localPlayer && cam != null && !_pm.IsPoweredJump)
                cam.AddImpulse(new Vector2((_pm.IsFacingRight ? 1f : -1f) * bigCarrotCamKick, 0f));
        }
        _prevPowerSpend = spend;

        bool showVisual = dashing || airborne || _landTimer > 0f;

        if (showVisual)
        {
            _skillVisual.enabled = true;
            _sr.enabled = false;
            _skillVisual.sprite = _sr.sprite;
            _skillVisual.flipX = _sr.flipX;
            _skillVisual.color = _sr.color;
            _skillVisual.sortingLayerID = _sr.sortingLayerID;
            _skillVisual.sortingOrder = _sr.sortingOrder;

            Vector3 targetScale;
            if (dashing)
            {
                targetScale = new Vector3(dashStretch.x, dashStretch.y, 1f);
                _aiTimer -= dt;
                if (_aiTimer <= 0f) { _aiTimer = afterimageInterval; SpawnAfterimage(); }
            }
            else if (airborne)
            {
                targetScale = Vector3.one; // 발사 스트레치에서 서서히 복원
            }
            else // 착지 복원
            {
                targetScale = Vector3.one;
                _landTimer -= dt;
            }

            _visualScale = Vector3.Lerp(_visualScale, targetScale, 1f - Mathf.Exp(-stretchSharpness * dt));
            _skillVisual.transform.localScale = _visualScale;
        }
        else if (_skillVisual.enabled)
        {
            _skillVisual.enabled = false;
            _skillVisual.transform.localScale = Vector3.one;
            _skillVisual.transform.localRotation = Quaternion.identity;
            _visualScale = Vector3.one;
            _sr.enabled = true;
        }

        _prevDashing = dashing;
        _prevAirborne = airborne;
    }

    // 파워업 유지 중 발밑 불꽃 링을 반복 재생한다 (캐릭터를 따라다님, 캐릭터 뒤에 그림)
    private void UpdatePowerAura(bool on, Vector3 feet, SkillFxLibrary fx)
    {
        bool show = on && fx != null && fx.powerUpAura != null && fx.powerUpAura.Length > 0;
        if (!show)
        {
            if (_aura != null && _aura.enabled) _aura.enabled = false;
            _auraT = 0f;
            return;
        }

        if (_aura == null)
        {
            var go = new GameObject("PowerUpAura");
            _aura = go.AddComponent<SpriteRenderer>();
        }
        // 부모 스케일(프리팹마다 다름)에 영향받지 않도록 월드 기준으로 배치한다
        _aura.transform.position = new Vector3(feet.x, feet.y, transform.position.z);
        _aura.transform.localScale = Vector3.one * fx.powerUpAuraScale;
        _aura.sortingLayerID = _sr.sortingLayerID;
        _aura.sortingOrder = _sr.sortingOrder - 1;
        _aura.enabled = true;

        _auraT += Time.deltaTime * fx.powerUpAuraFps;
        _aura.sprite = fx.powerUpAura[(int)_auraT % fx.powerUpAura.Length];
    }

    private void OnDestroy()
    {
        if (_aura != null) Destroy(_aura.gameObject);
        // 강화 도약 중에 사라지면 카메라 줌이 남지 않도록 되돌린다
        if (_holdingZoom)
        {
            var cam = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
            if (cam != null) cam.SetZoomHold(0f);
        }
    }

    private void SpawnAfterimage()
    {
        if (_sr.sprite == null) return;
        var go = new GameObject("Afterimage");
        go.transform.position = transform.position;
        go.transform.rotation = transform.rotation;
        go.transform.localScale = new Vector3(
            transform.localScale.x * dashStretch.x,
            transform.localScale.y * dashStretch.y,
            transform.localScale.z);

        var g = go.AddComponent<SpriteRenderer>();
        g.sprite = _sr.sprite;
        g.flipX = _sr.flipX;
        g.color = afterimageTint;
        g.sortingLayerID = _sr.sortingLayerID;
        g.sortingOrder = _sr.sortingOrder - 1;
        go.AddComponent<Afterimage>().Init(afterimageLife, afterimageTint);
    }
}
