using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 여우 Sniff 로 드러나는 "숨김 발판 타일맵". <see cref="Revealable"/> 의 타일맵 버전.
/// - 평소: TilemapRenderer OFF + TilemapCollider2D OFF (안 보이고 밟히지도 않음)
/// - Sniff: 호스트가 Reveal(sec) → 드러난 동안만 보이고 밟을 수 있다.
///   드러나는 동안 테두리가 일정한 밝기로 발광하고(맥박 없음),
///   사라지기 직전에는 테두리만 깜박인다. 발판 본체는 깜박이지 않는다.
///
/// Revealable 을 상속하므로 PlayerMovement.DoSniff 의 FindObjectsByType&lt;Revealable&gt; 에 그대로 잡힌다.
/// 감지 거리는 <see cref="IsWithinRange"/> 에서 타일의 실제 영역 기준으로 다시 계산한다.
/// <b>발판 하나당 이 컴포넌트를 가진 타일맵 오브젝트 하나</b>로 두어야 근처 것만 드러난다.
/// </summary>
[RequireComponent(typeof(Tilemap))]
[RequireComponent(typeof(TilemapRenderer))]
public class RevealableTilemap : Revealable
{
    [Header("등장 연출 (타일맵 틴트)")]
    [Tooltip("드러나는 순간 이 색으로 번쩍였다가 원래 색으로 돌아온다")]
    public Color flashColor = new Color(0.72f, 1f, 1f, 1f);

    [Header("발광 테두리")]
    [Tooltip("테두리를 만들지 여부. 같은 타일을 복사한 실루엣을 뒤에 살짝 키워서 그린다. " +
             "끄면 사라지기 전 깜박임도 같이 없어진다(깜박이는 주체가 테두리이므로)")]
    public bool drawGlowOutline = true;
    [Tooltip("테두리 두께(월드 유닛). 타일 1칸이 1유닛")]
    public float glowThickness = 0.12f;
    // 밝기는 glowColor 의 알파를 그대로 쓴다. 상속된 glowScale / glowPulseSpeed 는
    // 스프라이트용이라 타일맵에서는 쓰지 않는다(맥박 없음, 두께는 glowThickness).

    [Header("사라지기 전 깜박임 (테두리만)")]
    [Tooltip("남은 시간이 이보다 적으면 테두리가 깜박인다. 0이면 깜박이지 않는다")]
    public float blinkSeconds = 1.5f;
    [Tooltip("초당 깜박임 횟수. 사라질 때가 가까워지면 더 빨라진다")]
    public float blinkPerSecond = 4f;
    [Tooltip("깜박임이 가장 어두울 때의 투명도")]
    [Range(0f, 1f)] public float blinkMinAlpha = 0.15f;

    private Tilemap _tilemap;
    private TilemapRenderer _renderer;
    private TilemapCollider2D _collider;
    private Tilemap _glowTilemap;
    private TilemapRenderer _glowRenderer;
    private Color _baseColor = Color.white;
    private float _flashT;
    private bool _prevRevealed;

    public override void Spawned()
    {
        // base.Spawned() 가 Apply(false) 를 부르므로 그 전에 참조를 잡아둔다.
        _tilemap = GetComponent<Tilemap>();
        _renderer = GetComponent<TilemapRenderer>();
        _collider = GetComponent<TilemapCollider2D>();
        if (_tilemap != null) _baseColor = _tilemap.color;

        if (drawGlowOutline) BuildGlowOutline();

        base.Spawned();
    }

    /// <summary>
    /// 같은 타일을 복사한 타일맵을 뒤에 하나 더 두고, 타일 영역 중심을 기준으로
    /// 살짝 키워서 테두리처럼 삐져나오게 한다. (Revealable 의 글로우 실루엣과 같은 방식)
    /// </summary>
    private void BuildGlowOutline()
    {
        if (_tilemap == null || _renderer == null) return;

        var go = new GameObject("GlowOutline");
        go.transform.SetParent(transform, false);

        _glowTilemap = go.AddComponent<Tilemap>();
        _glowRenderer = go.AddComponent<TilemapRenderer>();

        _glowTilemap.tileAnchor = _tilemap.tileAnchor;
        _glowTilemap.orientation = _tilemap.orientation;
        foreach (var pos in _tilemap.cellBounds.allPositionsWithin)
        {
            var tile = _tilemap.GetTile(pos);
            if (tile != null) _glowTilemap.SetTile(pos, tile);
        }

        _glowRenderer.sortingLayerID = _renderer.sortingLayerID;
        _glowRenderer.sortingOrder = _renderer.sortingOrder - 1;  // 본체 뒤

        // 스프라이트 원래 색과 무관하게 순수 글로우색 실루엣으로 출력
        var glowMat = Resources.Load<Material>("GlowSilhouette");
        if (glowMat != null) _glowRenderer.sharedMaterial = glowMat;

        // localScale 은 오브젝트 원점(보통 Grid 0,0) 기준이라 그냥 키우면 타일이 멀리 날아간다.
        // 타일 영역 중심을 기준으로 커지도록 위치를 보정한다.
        Bounds lb = _tilemap.localBounds;
        float sx = lb.size.x > 0.0001f ? (lb.size.x + 2f * glowThickness) / lb.size.x : 1f;
        float sy = lb.size.y > 0.0001f ? (lb.size.y + 2f * glowThickness) / lb.size.y : 1f;
        go.transform.localScale = new Vector3(sx, sy, 1f);
        go.transform.localPosition = new Vector3(lb.center.x * (1f - sx), lb.center.y * (1f - sy), 0f);

        _glowRenderer.enabled = false;
    }

    /// <summary>
    /// 타일맵 오브젝트의 transform 은 보통 Grid 원점(0,0)에 있고 실제 타일은 멀리 떨어져 있다.
    /// 그래서 transform 위치로 거리를 재면 엉뚱한 곳을 재게 된다.
    /// 깔려 있는 타일의 실제 영역(경계 상자)까지의 거리로 판정한다.
    /// </summary>
    public override bool IsWithinRange(Vector2 point, float radius)
    {
        var tm = _tilemap != null ? _tilemap : GetComponent<Tilemap>();
        if (tm == null) return base.IsWithinRange(point, radius);

        Bounds local = tm.localBounds;
        var world = new Bounds(tm.transform.TransformPoint(local.center),
                               Vector3.Scale(local.size, tm.transform.lossyScale));

        Vector3 probe = new Vector3(point.x, point.y, world.center.z);
        Vector3 closest = world.ClosestPoint(probe);
        float dx = closest.x - point.x;
        float dy = closest.y - point.y;
        return dx * dx + dy * dy <= radius * radius;
    }

    protected override int RevealFxSortingLayer() => _renderer != null ? _renderer.sortingLayerID : base.RevealFxSortingLayer();

    protected override Bounds RevealFxBounds()
    {
        var tm = _tilemap != null ? _tilemap : GetComponent<Tilemap>();
        if (tm == null) return base.RevealFxBounds();
        Bounds local = tm.localBounds;
        return new Bounds(tm.transform.TransformPoint(local.center),
                          Vector3.Scale(local.size, tm.transform.lossyScale));
    }

    /// <summary>스프라이트 대신 타일맵 렌더러/콜라이더를 껐다 켠다.</summary>
    protected override void Apply(bool revealed)
    {
        base.Apply(revealed); // SpriteRenderer/Collider2D 는 없으므로 무해

        if (_renderer != null) _renderer.enabled = revealed;
        if (_collider != null) _collider.enabled = revealed;
    }

    public override void Render()
    {
        base.Render();
        if (_tilemap == null) return;

        bool revealed = IsRevealed;
        if (revealed && !_prevRevealed) _flashT = 0f; // 드러나는 순간(엣지)
        _prevRevealed = revealed;

        if (!revealed)
        {
            _tilemap.color = _baseColor;
            if (_glowRenderer != null) _glowRenderer.enabled = false;
            return;
        }

        _flashT += Time.deltaTime;
        float appear = Mathf.Clamp01(_flashT / Mathf.Max(0.01f, appearTime)); // 0→1

        // 본체: 등장 순간만 플래시 색 → 원래 색. 깜박임은 테두리만 한다.
        _tilemap.color = Color.Lerp(flashColor, _baseColor, appear);

        // 테두리: 맥박 없이 일정한 밝기로 켜 두고, 사라지기 직전에만 깜박인다.
        if (_glowRenderer != null && _glowTilemap != null)
        {
            _glowRenderer.enabled = true;
            var g = glowColor;
            g.a = glowColor.a * BlinkFactor();
            _glowTilemap.color = g;
        }
    }

    /// <summary>사라지기 직전 깜박임 계수(1 = 그대로, 낮을수록 투명).</summary>
    private float BlinkFactor()
    {
        if (blinkSeconds <= 0f || Runner == null) return 1f;

        float? remain = RevealTimer.RemainingTime(Runner);
        if (!remain.HasValue || remain.Value > blinkSeconds) return 1f;

        // 끝으로 갈수록 빠르게 깜박인다
        float t = Mathf.Clamp01(remain.Value / blinkSeconds);        // 1 → 0
        float freq = Mathf.Lerp(blinkPerSecond * 2.5f, blinkPerSecond, t);
        float wave = 0.5f + 0.5f * Mathf.Sin(Time.time * freq * Mathf.PI * 2f);
        return Mathf.Lerp(blinkMinAlpha, 1f, wave);
    }
}
