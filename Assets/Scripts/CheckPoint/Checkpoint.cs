using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 중간 체크포인트 / 최종 골.
// 협동 규칙: 스테이지의 플레이어가 모두 밟아야 활성화된다. (스테이지에 혼자면 혼자 밟아도 된다)
public class Checkpoint : MonoBehaviour
{
    private Animator animator;
    private bool isActivated = false;

    // 이 체크포인트에 한 번이라도 닿은 플레이어
    private readonly HashSet<PlayerMovement> _reached = new HashSet<PlayerMovement>();

    [Header("최종 골")]
    public bool isEndGoal = false; // 마지막 스테이지의 최종 골이면 인스펙터에서 체크

    [Header("도착 인원 표시 (깃발 위 \"1/2\")")]
    public bool showReachedCount = true;
    [Tooltip("깃발 윗부분에서 얼마나 위에 띄울지(월드 단위)")]
    public float labelHeight = 0.6f;
    [Tooltip("모두 도착한 뒤 \"2/2\" 를 보여주고 사라지기까지 걸리는 시간(초)")]
    public float labelFadeDelay = 1.5f;

    // 표시용 도착 집합. 트리거(_reached)는 호스트에서만 확실히 불리므로,
    // 모든 피어가 같은 숫자를 보도록 플레이어 위치(동기화됨)로 겹침을 직접 확인해 합친다.
    private readonly HashSet<PlayerMovement> _seen = new HashSet<PlayerMovement>();
    private Collider2D _col;
    private Text _label;
    private CanvasGroup _labelGroup;
    private float _nextScanTime;
    private float _completedAt = -1f;
    private int _shownReached = -1, _shownTotal = -1;

    private void Awake()
    {
        animator = GetComponent<Animator>();
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
        int total = players.Count;
        if (total == 0) { _labelGroup.alpha = 0f; return; } // 아직 캐릭터가 스폰되지 않음

        int reached = 0;
        foreach (var p in players)
        {
            if (!_seen.Contains(p) && IsTouching(p)) _seen.Add(p);
            if (_seen.Contains(p) || _reached.Contains(p)) reached++;
        }

        if (_completedAt < 0f)
        {
            _labelGroup.alpha = 1f;
            if (reached >= total) _completedAt = Time.time; // 이후 페이드아웃
        }

        if (reached == _shownReached && total == _shownTotal) return;
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

    // 깃발 위에 떠 있는 월드 스페이스 라벨. 1 월드 단위 = 100 캔버스 픽셀.
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
        // 부모 스케일(뒤집힘 포함)과 무관하게 항상 같은 크기로 보이게 한다.
        Vector3 ls = transform.lossyScale;
        go.transform.localScale = new Vector3(0.01f / Mathf.Max(0.0001f, Mathf.Abs(ls.x)) * Mathf.Sign(ls.x),
                                              0.01f / Mathf.Max(0.0001f, Mathf.Abs(ls.y)) * Mathf.Sign(ls.y), 1f);

        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 200; // 타일/캐릭터 위
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

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (isActivated || !other.CompareTag("Player")) return;

        var player = other.GetComponentInParent<PlayerMovement>();
        if (player != null) _reached.Add(player);

        if (!PlayerMovement.AllPlayersReached(_reached))
        {
            Debug.Log($"<color=green>[Checkpoint]</color> {other.name} 도착. 다른 플레이어를 기다리는 중...");
            return;
        }

        if (isEndGoal)
        {
            // 최종 골은 리스폰 지점으로 삼지 않는다. (골에 리스폰되면 다시 트리거되어 자동 엔딩되는 문제 방지)
            HandleFinalClear();
        }
        else
        {
            // 중간 체크포인트만 사망 시 리스폰 지점으로 저장
            CheckpointManager.lastCheckpointPosition = transform.position;
            CheckpointManager.hasCheckpoint = true;
            Debug.Log($"<color=green>[Checkpoint]</color> 위치 저장: {transform.position}");
            SaveCheckpointProgress();
        }

        isActivated = true;

        if (animator != null)
        {
            animator.SetTrigger("Activated");
        }
    }

    // Phase2: mid-stage checkpoint. Persist current position + timer for continue-from-checkpoint.
    // Host only, and only while the run timer is actually running.
    private void SaveCheckpointProgress()
    {
        var nt = NetworkTimer.Instance;
        if (nt == null || nt.Phase != NetworkTimer.PhaseRunning) return;

        var runner = FindFirstObjectByType<Fusion.NetworkRunner>();
        if (runner != null && !runner.IsServer) return;

        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        SaveDataManager.SaveCheckpoint(scene, RunManager.AccumulatedTime, nt.ElapsedTime, transform.position);
    }

    // 최종 골: 모든 스테이지 기록을 합산한 "합산 시간"을 랭킹에 올리고, 종료 화면을 띄운다.
    private void HandleFinalClear()
    {
        var nt = NetworkTimer.Instance;
        if (nt == null || nt.Phase != NetworkTimer.PhaseRunning) return;

        // 기록/저장은 호스트만. 종료 화면은 각 피어의 GameEndController 가 Phase 를 보고 표시.
        var runner = FindFirstObjectByType<Fusion.NetworkRunner>();
        if (runner != null && !runner.IsServer) return;

        float seg = nt.ElapsedTime;                        // 이 스테이지 구간 기록
        RunManager.AddSegment(seg, RunManager.CurrentStageNumber());
        float total = RunManager.AccumulatedTime;          // 앞 스테이지들 + 이 구간 = 합산
        nt.FinishRun(total);                               // Phase=Finished, FinalTime=합산 (전 피어 동기화)

        var ranking = RankingManager.GetOrCreate();
        if (ranking != null) ranking.SaveScore(total);
        else Debug.LogError("<color=red>[Critical]</color> RankingManager 를 찾을 수 없습니다!");

        RunManager.ResetRun();
        Debug.Log($"<color=lime>[Success]</color> ALL CLEAR! 합산 기록: {total:0.00}초");
    }
}
