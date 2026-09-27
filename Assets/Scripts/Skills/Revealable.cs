using Fusion;
using UnityEngine;

/// <summary>
/// 여우 Sniff 로 드러나는 숨김 오브젝트의 베이스 (B안: 드러난 동안만 보이고 상호작용 가능).
/// - 평소: SpriteRenderer OFF + Collider OFF (안 보이고 못 만짐). GameObject 는 활성 유지(감지/틱).
/// - Sniff: 호스트가 Reveal(sec) → [Networked] RevealTimer 설정 → 모든 클라이언트가 드러남.
/// - 드러날 때 테두리 발광(글로우 실루엣 복제본) + 등장 플래시 + 펄스 연출. (콜라이더엔 영향 없음)
/// NetworkObject 가 필요하며, 씬에 배치하면 Fusion 씬 오브젝트로 동작(호스트 권한).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class Revealable : NetworkBehaviour
{
    [Networked] public TickTimer RevealTimer { get; set; }

    [Header("Glow (발광 테두리)")]
    public Color glowColor = new Color(0.45f, 0.95f, 1f, 1f);
    [Tooltip("글로우 실루엣 크기 배수 (1보다 크게 → 테두리처럼 삐져나옴)")]
    public float glowScale = 1.16f;
    public float glowPulseSpeed = 6f;
    [Tooltip("등장 플래시/팝 지속(초)")]
    public float appearTime = 0.28f;

    private SpriteRenderer _sr;    // 메인
    private SpriteRenderer _glow;  // 발광 테두리(실루엣 복제)
    private Collider2D _col;
    private bool _wasRevealed;
    private float _appearT;

    public bool IsRevealed => Runner != null && !RevealTimer.ExpiredOrNotRunning(Runner);

    /// <summary>
    /// 이 오브젝트가 point 로부터 radius 안에 있는지. Sniff 감지 판정에 쓴다.
    /// 기본은 transform 위치 기준 원. 타일맵처럼 오브젝트 위치와 실제 내용의 위치가
    /// 다른 경우에는 이 메서드를 재정의해야 한다.
    /// </summary>
    public virtual bool IsWithinRange(Vector2 point, float radius)
    {
        return ((Vector2)transform.position - point).sqrMagnitude <= radius * radius;
    }

    public override void Spawned()
    {
        _sr = GetComponent<SpriteRenderer>();
        _col = GetComponent<Collider2D>();

        if (_sr != null)
        {
            var g = new GameObject("Glow");
            g.transform.SetParent(transform, false);
            g.transform.localScale = Vector3.one * glowScale;
            _glow = g.AddComponent<SpriteRenderer>();
            _glow.sprite = _sr.sprite;
            _glow.sortingLayerID = _sr.sortingLayerID;
            _glow.sortingOrder = _sr.sortingOrder - 1; // 메인 뒤
            _glow.color = glowColor;
            // 스프라이트 원래 색과 무관하게 순수 글로우색 실루엣으로 출력
            var glowMat = Resources.Load<Material>("GlowSilhouette");
            if (glowMat != null) _glow.sharedMaterial = glowMat;
            _glow.enabled = false;
        }
        Apply(false);
    }

    /// <summary>호스트가 이 오브젝트를 N초간 드러낸다.</summary>
    public void Reveal(float seconds)
    {
        if (Object != null && Object.HasStateAuthority)
            RevealTimer = TickTimer.CreateFromSeconds(Runner, seconds);
    }

    public override void Render()
    {
        bool revealed = IsRevealed;
        if (revealed && !_wasRevealed)                 // 드러나는 순간(엣지)
        {
            _appearT = 0f;
            SpawnRevealShine();
        }
        _wasRevealed = revealed;

        Apply(revealed);

        if (_glow != null)
        {
            _glow.enabled = revealed;
            if (revealed)
            {
                if (_sr != null && _glow.sprite != _sr.sprite) _glow.sprite = _sr.sprite;
                _appearT += Time.deltaTime;
                float appear = Mathf.Clamp01(_appearT / Mathf.Max(0.01f, appearTime)); // 0→1

                // 펄스(은은한 발광) + 등장 플래시(초반 밝게)
                float pulse = 0.4f + 0.3f * Mathf.Sin(Time.time * glowPulseSpeed);
                float flash = 1f - appear; // 등장 순간 최대 → 감쇠
                var c = glowColor; c.a = Mathf.Clamp01(pulse + flash * 0.7f);
                _glow.color = c;

                // 등장 시 살짝 크게 퍼졌다 정착 + 미세 펄스
                float sc = glowScale + Mathf.Lerp(0.5f, 0f, appear) + 0.04f * Mathf.Sin(Time.time * glowPulseSpeed);
                _glow.transform.localScale = Vector3.one * sc;
            }
        }
    }

    /// <summary>드러남 반짝 이펙트를 뿌릴 영역. 타일맵은 실제 타일 영역으로 재정의한다.</summary>
    protected virtual Bounds RevealFxBounds()
    {
        if (_sr != null && _sr.sprite != null) return _sr.bounds;
        return new Bounds(transform.position, Vector3.one);
    }

    protected virtual int RevealFxSortingLayer() => _sr != null ? _sr.sortingLayerID : 0;

    // 드러난 오브젝트 위에 반짝임. 넓은 발판은 폭을 따라 여러 개(최대 4개) 뿌린다.
    private void SpawnRevealShine()
    {
        var fx = SkillFxLibrary.Instance;
        if (fx == null || fx.revealShine == null || fx.revealShine.Length == 0) return;

        Bounds b = RevealFxBounds();
        int count = Mathf.Clamp(Mathf.CeilToInt(b.size.x / 2f), 1, 4);
        int layer = RevealFxSortingLayer();
        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0.5f : (i + 0.5f) / count;
            var pos = new Vector3(Mathf.Lerp(b.min.x, b.max.x, t), b.center.y, 0f);
            SpriteEffect.Spawn(fx.revealShine, pos, fx.revealShineFps, fx.revealShineScale,
                false, layer, fx.sortingOrder, Color.white);
        }
    }

    protected virtual void Apply(bool revealed)
    {
        if (_sr != null) _sr.enabled = revealed;
        if (_col != null) _col.enabled = revealed; // B안: 드러난 동안만 상호작용
    }
}
