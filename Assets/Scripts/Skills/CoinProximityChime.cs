using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 아직 드러나지 않은 구슬 조각(CoinItem)에 '갈 수 있는 길'로 가까워지면 한 번 알려준다.
/// - 거리는 직선이 아니라 빈 칸을 따라 이어진 길 거리(칸 수)로 잰다. 그래서 바닥/벽 너머 다른 층의 조각에는 반응하지 않는다.
/// - 새로 가까워진 순간 '띠리링' 효과음 1회 + 조각 위치에 희미한 반짝임 1회. 근처에 머무는 동안은 반복하지 않는다.
/// - 범위를 벗어났다가(exitRange) 다시 들어오고, cooldown 이 지났을 때만 다시 알려준다.
/// - 각 클라이언트가 자기 캐릭터(InputAuthority) 기준으로 판단하므로 여우·토끼 모두 들린다.
/// - 처음부터 보이는 조각(alwaysVisible), 이미 드러난 조각, 획득된 조각은 제외한다.
/// 씬에 배치할 필요 없이 게임 시작 시 자동 생성된다. (Hierarchy 의 [CoinProximityChime] 에서 값 조정/디버그 표시)
/// </summary>
public class CoinProximityChime : MonoBehaviour
{
    [Header("범위 (길 거리, 칸)")]
    [Tooltip("이 길 거리 안으로 들어오면 알려준다")]
    public int enterRange = 10;
    [Tooltip("이 길 거리 밖으로 나가야 다시 알려줄 수 있다")]
    public int exitRange = 14;
    [Tooltip("같은 조각을 다시 알려주기까지 최소 시간(초)")]
    public float cooldown = 12f;

    [Header("연출")]
    [Range(0f, 1.5f)] public float volume = 1.2f;
    public bool sparkleAtCoin = true;

    [Header("디버그")]
    [Tooltip("Scene 뷰에 길 거리 범위(초록=알림 범위, 노랑=벗어남 판정 전)와 감지된 조각을 표시")]
    public bool showDebugGizmos;

    private const float ScanInterval = 0.25f;
    private const float RescanListInterval = 1f;

    private static AudioClip _clip;

    private CoinItem[] _coins = new CoinItem[0];
    private PlayerMovement _local;
    private float _scanTimer, _listTimer;
    private readonly Dictionary<int, bool> _inRange = new Dictionary<int, bool>();
    private readonly Dictionary<int, float> _lastChime = new Dictionary<int, float>();
    private readonly Dictionary<Vector2Int, int> _dist = new Dictionary<Vector2Int, int>();
    private readonly Queue<Vector2Int> _queue = new Queue<Vector2Int>();
    private readonly Collider2D[] _hits = new Collider2D[8];
    private int _groundMask;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        var go = new GameObject("[CoinProximityChime]");
        DontDestroyOnLoad(go);
        go.AddComponent<CoinProximityChime>();
    }

    private void Awake()
    {
        _groundMask = LayerMask.GetMask("Ground");
    }

    private void Update()
    {
        _listTimer -= Time.deltaTime;
        if (_listTimer <= 0f) { _listTimer = RescanListInterval; RefreshLists(); }

        _scanTimer -= Time.deltaTime;
        if (_scanTimer > 0f) return;
        _scanTimer = ScanInterval;
        if (_local == null || _coins.Length == 0) return;

        Vector2Int start = Cell(_local.transform.position + Vector3.up * 0.5f);
        FloodFill(start, exitRange);

        foreach (var coin in _coins)
        {
            if (coin == null || coin.alwaysVisible) continue;
            if (coin.Object == null || !coin.Object.IsValid || coin.IsRevealed) continue;

            int id = coin.GetInstanceID();
            int d = _dist.TryGetValue(Cell(coin.transform.position), out int v) ? v : int.MaxValue;
            bool wasIn = _inRange.TryGetValue(id, out bool w) && w;

            if (!wasIn && d <= enterRange)
            {
                _inRange[id] = true;
                float last = _lastChime.TryGetValue(id, out float l) ? l : -999f;
                if (Time.time - last >= cooldown)
                {
                    _lastChime[id] = Time.time;
                    Notify(coin);
                }
            }
            else if (wasIn && d > exitRange)
            {
                _inRange[id] = false;
            }
        }
    }

    private void RefreshLists()
    {
        _coins = FindObjectsByType<CoinItem>(FindObjectsSortMode.None);
        _local = PlayerMovement.GetLocalPlayer();
    }

    private void Notify(CoinItem coin)
    {
        SfxPlayer.Play(GetClip(), volume);
        if (!sparkleAtCoin) return;
        var fx = SkillFxLibrary.Instance;
        if (fx == null || fx.revealShine == null || fx.revealShine.Length == 0) return;
        var sr = coin.GetComponent<SpriteRenderer>();
        int layer = sr != null ? sr.sortingLayerID : 0;
        SpriteEffect.Spawn(fx.revealShine, coin.transform.position, fx.revealShineFps * 0.8f, fx.revealShineScale * 0.9f,
            false, layer, fx.sortingOrder, new Color(1f, 0.92f, 0.45f, 0.75f));
    }

    // ---------- 길 거리 계산 (빈 칸을 따라 4방향으로 퍼짐) ----------

    private static Vector2Int Cell(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));

    private void FloodFill(Vector2Int start, int maxSteps)
    {
        _dist.Clear();
        _queue.Clear();
        _dist[start] = 0;
        _queue.Enqueue(start);
        while (_queue.Count > 0)
        {
            var c = _queue.Dequeue();
            int d = _dist[c];
            if (d >= maxSteps) continue;
            Step(c, new Vector2Int(c.x + 1, c.y), d);
            Step(c, new Vector2Int(c.x - 1, c.y), d);
            Step(c, new Vector2Int(c.x, c.y + 1), d);
            Step(c, new Vector2Int(c.x, c.y - 1), d);
        }
    }

    private void Step(Vector2Int from, Vector2Int to, int d)
    {
        if (_dist.ContainsKey(to)) return;
        if (IsSolid(to)) { _dist[to] = int.MaxValue; return; } // 막힌 칸 표시(재검사 방지)
        _dist[to] = d + 1;
        _queue.Enqueue(to);
    }

    // 지형(Ground 레이어)만 막힌 칸으로 본다. 숨겨진 발판·일방통행 발판·트리거는 지나갈 수 있는 칸으로 취급.
    private bool IsSolid(Vector2Int cell)
    {
        int n = Physics2D.OverlapBoxNonAlloc(new Vector2(cell.x + 0.5f, cell.y + 0.5f), new Vector2(0.8f, 0.8f), 0f, _hits, _groundMask);
        for (int i = 0; i < n; i++)
        {
            var col = _hits[i];
            if (col == null || col.isTrigger || col.usedByEffector) continue;
            if (col.GetComponentInParent<Revealable>() != null) continue;
            return true;
        }
        return false;
    }

    // 맑은 3음 '띠리링' (E6 - G#6 - B6). 한 번만 울리므로 획득음보다 또렷하게.
    private static AudioClip GetClip()
    {
        if (_clip != null) return _clip;
        const int sr = 44100;
        float[] notes = { 1318.51f, 1661.22f, 1975.53f };
        const float noteDur = 0.09f;
        const float tail = 0.25f;
        int noteSamples = Mathf.RoundToInt(sr * noteDur);
        int total = noteSamples * notes.Length + Mathf.RoundToInt(sr * tail);
        var data = new float[total];
        for (int n = 0; n < notes.Length; n++)
        {
            float phase = 0f;
            int startI = n * noteSamples;
            int len = total - startI; // 각 음이 겹치며 울려 퍼지게
            for (int i = 0; i < len; i++)
            {
                phase += notes[n] / sr;
                float t = (float)i / sr;
                float env = (t < 0.004f ? t / 0.004f : 1f) * Mathf.Exp(-t * 9f);
                float s = Mathf.Sin(phase * Mathf.PI * 2f) * 0.8f + Mathf.Sin(phase * Mathf.PI * 4f) * 0.2f;
                data[startI + i] += s * env * 0.32f;
            }
        }
        for (int i = 0; i < total; i++) data[i] = Mathf.Clamp(data[i], -0.95f, 0.95f);
        _clip = AudioClip.Create("SFX_CoinNearby", total, 1, sr, false);
        _clip.SetData(data, 0);
        return _clip;
    }

    private void OnDrawGizmos()
    {
        if (!showDebugGizmos || !Application.isPlaying) return;
        foreach (var kv in _dist)
        {
            if (kv.Value == int.MaxValue) continue;
            Gizmos.color = kv.Value <= enterRange ? new Color(0f, 1f, 0.3f, 0.25f) : new Color(1f, 0.9f, 0f, 0.2f);
            Gizmos.DrawCube(new Vector3(kv.Key.x + 0.5f, kv.Key.y + 0.5f, 0f), new Vector3(0.9f, 0.9f, 0.01f));
        }
        foreach (var coin in _coins)
        {
            if (coin == null || coin.alwaysVisible) continue;
            bool inR = _inRange.TryGetValue(coin.GetInstanceID(), out bool b) && b;
            Gizmos.color = inR ? Color.cyan : Color.gray;
            Gizmos.DrawWireSphere(coin.transform.position, 0.5f);
        }
    }
}
