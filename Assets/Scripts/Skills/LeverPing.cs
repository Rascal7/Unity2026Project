using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 레버(LeverSwitch)가 ON 되는 순간, 레버로 열리거나 생긴 곳(문/계단 등)을 플레이어에게 알려주는 '핑' 표시.
/// - 위아래로 통통 튀는 아래 화살표 (문이 열리면 열린 통로 안, 계단 등이 생기면 그 위)
/// - 대상이 화면 밖이면 화면 가장자리에 대상 방향을 가리키는 화살표
/// - 레버 효과음 직후 '핑' 효과음
/// - duration 초 동안 표시, 내 캐릭터가 대상 근처에 오면 일찍 사라진다.
/// LeverSwitch 의 네트워크 On 값만 읽으므로 두 플레이어 화면 모두에 뜬다. 씬 배치 없이 자동 생성된다.
/// </summary>
public class LeverPing : MonoBehaviour
{
    [Tooltip("핑 표시 시간(초)")]
    public float duration = 8f;
    [Tooltip("내 캐릭터가 대상 영역에서 이 거리 안에 들어오면 표시를 일찍 끝낸다")]
    public float arriveDistance = 2.5f;
    public Color color = new Color(1f, 0.86f, 0.25f, 1f);

    private const int SortingOrder = 500;
    private const float EdgeMargin = 1.3f;

    private static Sprite _arrowSprite;
    private static AudioClip _pingClip;

    private readonly Dictionary<LeverSwitch, bool> _lastOn = new Dictionary<LeverSwitch, bool>();
    private readonly Dictionary<LeverSwitch, Bounds> _targetBounds = new Dictionary<LeverSwitch, Bounds>();
    private readonly List<Ping> _pings = new List<Ping>();
    private LeverSwitch[] _levers = new LeverSwitch[0];
    private float _rescanTimer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        var go = new GameObject("[LeverPing]");
        DontDestroyOnLoad(go);
        go.AddComponent<LeverPing>();
    }

    private void Update()
    {
        _rescanTimer -= Time.deltaTime;
        if (_rescanTimer <= 0f)
        {
            _rescanTimer = 1f;
            _levers = FindObjectsByType<LeverSwitch>(FindObjectsSortMode.None);
            RemoveDestroyedLevers();
        }

        foreach (var lever in _levers)
        {
            if (lever == null || lever.Object == null || !lever.Object.IsValid) continue;
            bool on = lever.On;
            if (!_lastOn.TryGetValue(lever, out bool was))
            {
                // 처음 본 레버: 현재 상태만 기록 (이미 켜진 레버로 핑하지 않음). 대상 위치도 열리기 전에 저장.
                _lastOn[lever] = on;
                if (!on) _targetBounds[lever] = ComputeTargetBounds(lever);
                continue;
            }
            if (on && !was) StartPing(lever);
            _lastOn[lever] = on;
        }

        for (int i = _pings.Count - 1; i >= 0; i--)
        {
            if (!_pings[i].Tick(this)) { _pings[i].Destroy(); _pings.RemoveAt(i); }
        }
    }

    // 씬 전환 등으로 파괴된 레버 기록 정리
    private void RemoveDestroyedLevers()
    {
        var dead = new List<LeverSwitch>();
        foreach (var k in _lastOn.Keys) if (k == null) dead.Add(k);
        foreach (var k in dead) { _lastOn.Remove(k); _targetBounds.Remove(k); }
    }

    private void StartPing(LeverSwitch lever)
    {
        Bounds b = _targetBounds.TryGetValue(lever, out var cached) ? cached : ComputeTargetBounds(lever);
        if (b.size == Vector3.zero) return;
        bool opensPassage = lever.deactivateOnTargets != null && lever.deactivateOnTargets.Length > 0;
        _pings.Add(new Ping(b, duration, color, opensPassage));
        Invoke(nameof(PlayPingSound), 0.3f);
    }

    private void PlayPingSound() => SfxPlayer.Play(GetPingClip(), 1f);

    // 레버가 켜고/끄는 대상들의 월드 영역(타일맵은 실제 타일 범위).
    private static Bounds ComputeTargetBounds(LeverSwitch lever)
    {
        bool has = false;
        Bounds total = default;
        void Add(GameObject t)
        {
            if (t == null) return;
            Bounds b;
            var tm = t.GetComponent<UnityEngine.Tilemaps.Tilemap>();
            if (tm != null)
            {
                bool any = false;
                Vector3Int min = Vector3Int.zero, max = Vector3Int.zero;
                foreach (var p in tm.cellBounds.allPositionsWithin)
                {
                    if (!tm.HasTile(p)) continue;
                    if (!any) { min = max = p; any = true; }
                    else { min = Vector3Int.Min(min, p); max = Vector3Int.Max(max, p); }
                }
                if (!any) return;
                Vector3 a = tm.CellToWorld(min), c = tm.CellToWorld(max + Vector3Int.one);
                b = new Bounds((a + c) * 0.5f, new Vector3(Mathf.Abs(c.x - a.x), Mathf.Abs(c.y - a.y), 0f));
            }
            else
            {
                var r = t.GetComponentInChildren<Renderer>(true);
                var col = t.GetComponentInChildren<Collider2D>(true);
                if (r != null) b = r.bounds;
                else if (col != null) b = col.bounds;
                else b = new Bounds(t.transform.position, Vector3.one);
            }
            if (!has) { total = b; has = true; } else total.Encapsulate(b);
        }
        if (lever.activateOnTargets != null) foreach (var t in lever.activateOnTargets) Add(t);
        if (lever.deactivateOnTargets != null) foreach (var t in lever.deactivateOnTargets) Add(t);
        return has ? total : default;
    }

    private static PlayerMovement LocalPlayer()
    {
        foreach (var pm in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
        {
            NetworkObject no = pm.Object;
            if (no != null && no.IsValid && no.HasInputAuthority) return pm;
        }
        return null;
    }

    // ---------- 핑 1개 ----------
    private class Ping
    {
        private readonly Bounds _b;
        private readonly Color _color;
        private readonly float _arrowBaseY;
        private float _remaining;
        private readonly GameObject _root;
        private readonly SpriteRenderer _arrow, _edgeArrow;
        private float _t;
        private float _localCheck;

        // opensPassage: 문처럼 사라져서 길이 열리는 대상이면 열린 통로 안(머리 높이)에, 계단처럼 생기는 대상이면 그 위에 화살표를 둔다.
        public Ping(Bounds b, float duration, Color color, bool opensPassage)
        {
            _b = b; _color = color; _remaining = duration;
            _arrowBaseY = opensPassage ? b.min.y + Mathf.Min(2.2f, b.size.y * 0.6f) : b.max.y + 1.1f;
            _root = new GameObject("LeverPingFx");
            _arrow = MakeSr("Arrow", ArrowSprite());
            _edgeArrow = MakeSr("EdgeArrow", ArrowSprite());
            _edgeArrow.transform.localScale = Vector3.one * 1.3f;
        }

        private SpriteRenderer MakeSr(string name, Sprite s)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s;
            sr.sortingOrder = SortingOrder;
            return sr;
        }

        // false 를 돌려주면 끝
        public bool Tick(LeverPing owner)
        {
            float dt = Time.deltaTime;
            _t += dt;
            _remaining -= dt;
            if (_remaining <= 0f) return false;

            // 내 캐릭터가 도착하면 일찍 마무리
            _localCheck -= dt;
            if (_localCheck <= 0f)
            {
                _localCheck = 0.25f;
                var me = LocalPlayer();
                if (me != null)
                {
                    var near = _b; near.Expand(owner.arriveDistance * 2f);
                    if (near.Contains(new Vector3(me.transform.position.x, me.transform.position.y, _b.center.z)))
                        _remaining = Mathf.Min(_remaining, 0.8f);
                }
            }

            float fade = Mathf.Clamp01(_remaining / 0.8f);
            float appear = Mathf.Clamp01(_t / 0.25f);
            float alpha = fade * appear;

            // 대상 위 화살표 (아래를 가리키며 통통)
            float bob = Mathf.Abs(Mathf.Sin(_t * 5f)) * 0.45f;
            _arrow.transform.position = new Vector3(_b.center.x, _arrowBaseY + bob, 0f);
            _arrow.color = WithA(_color, alpha);

            // 화면 밖이면 가장자리 화살표
            var cam = Camera.main;
            if (cam != null && cam.orthographic)
            {
                Vector2 cc = cam.transform.position;
                float h = cam.orthographicSize, w = h * cam.aspect;
                Vector2 d = (Vector2)_b.center - cc;
                float hx = w - EdgeMargin, hy = h - EdgeMargin;
                bool inside = Mathf.Abs(d.x) < hx && Mathf.Abs(d.y) < hy;
                _edgeArrow.enabled = !inside;
                if (!inside)
                {
                    float s = Mathf.Min(hx / Mathf.Max(0.001f, Mathf.Abs(d.x)), hy / Mathf.Max(0.001f, Mathf.Abs(d.y)));
                    Vector2 dir = d.normalized;
                    Vector2 pos = cc + d * s + dir * (Mathf.Sin(_t * 6f) * 0.2f);
                    _edgeArrow.transform.position = new Vector3(pos.x, pos.y, 0f);
                    _edgeArrow.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + 90f);
                    _edgeArrow.color = WithA(_color, alpha);
                }
            }
            else _edgeArrow.enabled = false;

            return true;
        }

        public void Destroy() { if (_root != null) Object.Destroy(_root); }
        private static Color WithA(Color c, float a) { c.a = a; return c; }
    }

    // ---------- 런타임 생성 스프라이트 / 효과음 ----------

    // 아래를 가리키는 픽셀 화살표 (테두리 진하게)
    private static Sprite ArrowSprite()
    {
        if (_arrowSprite != null) return _arrowSprite;
        string[] rows =
        {
            "...OOOOO...",
            "...OWWWO...",
            "...OWWWO...",
            "...OWWWO...",
            "...OWWWO...",
            "OOOOWWWOOOO",
            ".OWWWWWWWO.",
            "..OWWWWWO..",
            "...OWWWO...",
            "....OWO....",
            ".....O.....",
        };
        int w = rows[0].Length, h = rows.Length;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                char ch = rows[h - 1 - y][x];
                tex.SetPixel(x, y, ch == 'W' ? Color.white : ch == 'O' ? new Color(0.3f, 0.18f, 0.05f, 1f) : Color.clear);
            }
        tex.Apply();
        _arrowSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 9f);
        return _arrowSprite;
    }

    // '핑~' (A6 -> E6, 맑게 울림)
    private static AudioClip GetPingClip()
    {
        if (_pingClip != null) return _pingClip;
        const int sr = 44100;
        int total = Mathf.RoundToInt(sr * 0.55f);
        var data = new float[total];
        float[] notes = { 1760f, 1318.51f };
        int[] starts = { 0, Mathf.RoundToInt(sr * 0.12f) };
        for (int n = 0; n < notes.Length; n++)
        {
            float phase = 0f;
            for (int i = starts[n]; i < total; i++)
            {
                float t = (float)(i - starts[n]) / sr;
                phase += notes[n] / sr;
                float env = (t < 0.005f ? t / 0.005f : 1f) * Mathf.Exp(-t * 7f);
                data[i] += (Mathf.Sin(phase * Mathf.PI * 2f) * 0.85f + Mathf.Sin(phase * Mathf.PI * 6f) * 0.15f) * env * 0.38f;
            }
        }
        for (int i = 0; i < total; i++) data[i] = Mathf.Clamp(data[i], -0.95f, 0.95f);
        _pingClip = AudioClip.Create("SFX_LeverPing", total, 1, sr, false);
        _pingClip.SetData(data, 0);
        return _pingClip;
    }
}
