using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 최종 골(마지막 스테이지의 클리어 지점)용. Checkpoint(isEndGoal)와 동일한 종료 처리.
// 중간 스테이지의 관문은 EndPoint 를 사용한다.
public class GoalPoint : MonoBehaviour
{
    [Header("Settings")]
    public string currentMapName = "";
    public bool isFinalGoal = true; // 최종 골이면 true

    [Header("도착 인원 표시 (골 위 \"1/2\")")]
    public bool showReachedCount = true;
    public float labelHeight = 0.6f;
    public float labelFadeDelay = 1.5f;

    private bool _isTriggered = false;
    private readonly HashSet<PlayerMovement> _reached = new HashSet<PlayerMovement>();
    private readonly HashSet<PlayerMovement> _seen = new HashSet<PlayerMovement>();
    private Collider2D _col;
    private Text _label;
    private CanvasGroup _labelGroup;
    private float _nextScanTime;
    private float _completedAt = -1f;
    private int _shownReached = -1, _shownTotal = -1;

    private void Awake()
    {
        _col = GetComponent<Collider2D>();
        if (showReachedCount) CreateLabel();
    }

    private void Update()
    {
        if (_label == null) return;

        if (Time.time >= _nextScanTime)
        {
            _nextScanTime = Time.time + 0.1f;
            RefreshLabel();
        }

        if (_completedAt >= 0f)
        {
            float t = (Time.time - _completedAt - labelFadeDelay) / 0.5f;
            _labelGroup.alpha = 1f - Mathf.Clamp01(t);
        }
    }

    private void RefreshLabel()
    {
        var players = PlayerMovement.ActivePlayers();
        int total = Mathf.Max(2, players.Count);
        if (total == 0) { _labelGroup.alpha = 0f; return; }

        int reached = 0;
        foreach (var p in players)
        {
            if (!_seen.Contains(p) && IsTouching(p)) _seen.Add(p);
            if (_seen.Contains(p) || _reached.Contains(p)) reached++;
        }

        if (_completedAt < 0f)
        {
            _labelGroup.alpha = 1f;
            if (reached >= total) _completedAt = Time.time;
        }

        if (reached == _shownReached && total == _shownTotal) return;
        if (_shownReached >= 0 && reached > _shownReached)
        {
            if (reached >= total) SfxPlayer.CheckpointFull();
            else SfxPlayer.CheckpointHalf();
        }
        _shownReached = reached;
        _shownTotal = total;
        _label.text = $"{reached}/{total}";
        _label.color = reached >= total ? LobbyUITheme.GreenBtn : LobbyUITheme.Gold;
    }

    private bool IsTouching(PlayerMovement p)
    {
        if (_col == null) return false;
        var pc = p.GetComponent<Collider2D>();
        if (pc != null) return _col.bounds.Intersects(pc.bounds);
        return _col.OverlapPoint(p.transform.position);
    }

    private void CreateLabel()
    {
        float top = transform.position.y + 1f;
        var sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null) top = sr.bounds.max.y;
        else if (_col != null) top = _col.bounds.max.y;

        var go = new GameObject("ReachedLabel", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        go.transform.SetParent(transform, true);
        go.transform.position = new Vector3(transform.position.x, top + labelHeight, 0f);
        go.transform.rotation = Quaternion.identity;
        Vector3 ls = transform.lossyScale;
        go.transform.localScale = new Vector3(0.01f / Mathf.Max(0.0001f, Mathf.Abs(ls.x)) * Mathf.Sign(ls.x),
                                              0.01f / Mathf.Max(0.0001f, Mathf.Abs(ls.y)) * Mathf.Sign(ls.y), 1f);

        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 200;
        ((RectTransform)go.transform).sizeDelta = new Vector2(200f, 60f);
        _labelGroup = go.GetComponent<CanvasGroup>();
        _labelGroup.alpha = 0f;

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Outline));
        textGo.transform.SetParent(go.transform, false);
        var rt = (RectTransform)textGo.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        _label = textGo.GetComponent<Text>();
        _label.font = LobbyUITheme.PixelFont;
        _label.fontSize = 52;
        _label.alignment = TextAnchor.MiddleCenter;
        _label.horizontalOverflow = HorizontalWrapMode.Overflow;
        _label.verticalOverflow = VerticalWrapMode.Overflow;
        _label.raycastTarget = false;
        _label.color = LobbyUITheme.Gold;

        var outline = textGo.GetComponent<Outline>();
        outline.effectColor = LobbyUITheme.GoldDeep;
        outline.effectDistance = new Vector2(3f, -3f);
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (_isTriggered || !collision.CompareTag("Player")) return;

        var player = collision.GetComponentInParent<PlayerMovement>();
        if (player != null) _reached.Add(player);

        // 협동 규칙: 모든 활성 플레이어가 골에 도달해야 최종 클리어 처리
        if (!PlayerMovement.AllPlayersReached(_reached))
        {
            Debug.Log($"<color=cyan>[Goal]</color> {collision.name} 도착. 다른 플레이어를 기다리는 중...");
            return;
        }

        Debug.Log("<color=cyan>[Goal]</color> 전원 골인 완료!");
        if (isFinalGoal)
        {
            _isTriggered = true;
            HandleFinalClear();
        }
    }

    // 최종 골: 모든 스테이지 기록을 합산한 "합산 시간"을 랭킹에 올리고, 종료 화면을 띄운다.
    private void HandleFinalClear()
    {
        var nt = NetworkTimer.Instance;
        if (nt == null || nt.Phase != NetworkTimer.PhaseRunning) return;

        var runner = FindFirstObjectByType<Fusion.NetworkRunner>();
        if (runner != null && !runner.IsServer) return;

        float seg = nt.ElapsedTime;
        RunManager.AddSegment(seg, RunManager.CurrentStageNumber()); // 마지막 구간도 스테이지 번호와 함께 기록
        float total = RunManager.AccumulatedTime;
        nt.FinishRun(total);

        var ranking = RankingManager.GetOrCreate();
        if (ranking != null) ranking.SaveScore(total);

        RunManager.ResetRun();
        Debug.Log($"<color=lime>[Success]</color> ALL CLEAR! 합산 기록: {total:0.00}초");
    }
}
