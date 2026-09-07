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

    private PlayerMovement _pm;
    private NetworkObject _no;
    private SpriteRenderer _sr;
    private SpriteRenderer _skillVisual; // 변형/회전 전용(콜라이더 영향 없음)

    private bool _prevDashing;
    private bool _prevAirborne;
    private float _aiTimer;
    private float _landTimer;
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

        // --- 이벤트 엣지 ---
        if (dashing && !_prevDashing && localPlayer && cam != null)
            cam.AddImpulse(new Vector2((_pm.IsFacingRight ? 1f : -1f) * camKick, 0f));

        if (airborne && !_prevAirborne)         // 토끼 발사
        {
            _visualScale = new Vector3(launchStretch.x, launchStretch.y, 1f);
        }
        if (!airborne && _prevAirborne)         // 토끼 착지
        {
            _visualScale = new Vector3(landSquash.x, landSquash.y, 1f);
            _landTimer = landRecoverTime;
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
