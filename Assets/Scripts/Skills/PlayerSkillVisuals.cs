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

    private void Awake()
    {
        _pm = GetComponent<PlayerMovement>();
        _no = GetComponent<NetworkObject>();
        _sr = GetComponent<SpriteRenderer>();

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
        if (dashing && !_prevDashing)           // 여우 대시 시작 → 바닥먼지(뒤쪽)
        {
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

        if (airborne && !_prevAirborne)         // 토끼 발사
        {
            _visualScale = new Vector3(launchStretch.x, launchStretch.y, 1f);
            SpriteEffect.Spawn(jumpLaunchFrames, feet, jumpLaunchFps, jumpLaunchScale,
                false, slId, effectSortingOrder, effectTint);
        }
        if (!airborne && _prevAirborne)         // 토끼 착지
        {
            _visualScale = new Vector3(landSquash.x, landSquash.y, 1f);
            _landTimer = landRecoverTime;
            SpriteEffect.Spawn(jumpLandFrames, feet, jumpLandFps, jumpLandScale,
                false, slId, effectSortingOrder, effectTint);
            if (localPlayer && cam != null) cam.AddShake(landShake);
        }

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
