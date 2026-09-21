using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 여우 Sniff 로 드러나는 "숨김 발판 타일맵". <see cref="Revealable"/> 의 타일맵 버전.
/// - 평소: TilemapRenderer OFF + TilemapCollider2D OFF (안 보이고 밟히지도 않음)
/// - Sniff: 호스트가 Reveal(sec) → 드러난 동안만 보이고 밟을 수 있다.
///
/// Revealable 을 상속하므로 PlayerMovement.DoSniff 의 FindObjectsByType&lt;Revealable&gt; 에 그대로 잡힌다.
/// 감지는 오브젝트의 transform.position 기준 거리(sniffRadius)로 하므로,
/// <b>발판 하나당 이 컴포넌트를 가진 타일맵 오브젝트 하나</b>로 두어야 근처 것만 드러난다.
/// (타일맵 하나에 전부 몰아넣으면 맵 반대편 발판까지 같이 드러난다.)
/// </summary>
[RequireComponent(typeof(Tilemap))]
[RequireComponent(typeof(TilemapRenderer))]
public class RevealableTilemap : Revealable
{
    [Header("등장 연출 (타일맵 틴트)")]
    [Tooltip("드러나는 순간 이 색으로 번쩍였다가 원래 색으로 돌아온다")]
    public Color flashColor = new Color(0.72f, 1f, 1f, 1f);

    private Tilemap _tilemap;
    private TilemapRenderer _renderer;
    private TilemapCollider2D _collider;
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

        base.Spawned();
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
            return;
        }

        _flashT += Time.deltaTime;
        float t = Mathf.Clamp01(_flashT / Mathf.Max(0.01f, appearTime));
        _tilemap.color = Color.Lerp(flashColor, _baseColor, t);
    }
}
