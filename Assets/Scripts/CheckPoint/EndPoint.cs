using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.UI;

// 스테이지 끝 지점(중간 관문). 플레이어가 닿으면 트리거.
//  - 타이머가 없는(또는 안 도는) 스테이지 = 튜토리얼: 완료 화면 표시 후 본 스테이지로.
//    (TutorialFlowController 가 있으면 축하/확인 화면을 띄우고, 없으면 즉시 이동)
//  - 타이머 스테이지: 구간 시간 확정 + 누적. 즉시 로드하지 않고 GameEndController 의
//    [다음 스테이지] 버튼(호스트)에서 로드한다.
public class EndPoint : MonoBehaviour
{
    [Header("다음 씬 이름 (Build Settings 에 등록된 이름과 정확히 일치해야 함)")]
    public string nextSceneName;

    [Header("도착 인원 표시 (관문 위 \"1/2\")")]
    public bool showReachedCount = true;
    public float labelHeight = 0.6f;
    public float labelFadeDelay = 1.5f;

    private bool isTriggered = false;

    // 끝 지점에 한 번이라도 닿은 플레이어
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
        int total = players.Count;
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

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player") || isTriggered) return;

        // 협동 규칙: 스테이지의 플레이어가 모두 도착해야 클리어. (혼자면 혼자 도착해도 된다)
        var player = other.GetComponentInParent<PlayerMovement>();
        if (player != null) _reached.Add(player);
        if (!PlayerMovement.AllPlayersReached(_reached))
        {
            Debug.Log($"<color=cyan>[EndPoint]</color> {other.name} 도착. 다른 플레이어를 기다리는 중...");
            return;
        }

        var runner = FindFirstObjectByType<NetworkRunner>();
        var nt = NetworkTimer.Instance;
        // NetworkTimer 가 존재하면 타이머 스테이지(인트로/러닝 포함). 튜토리얼은 타이머를 스폰하지 않으므로 nt == null.
        bool timerStage = (nt != null);

        if (timerStage)
        {
            // 타이머 스테이지(중간): 씬 전환/기록은 호스트만 처리
            if (runner == null || !runner.IsServer) return;
            isTriggered = true;

            float seg = nt.PauseForChoice();
            RunManager.AddSegment(seg, RunManager.CurrentStageNumber());
            nt.SyncStageChoiceBonus(seg);
            RunManager.PendingNextScene = nextSceneName;
            // 스테이지 클리어 시점에 "다음 스테이지"로 진행 저장 (체크포인트 초기화).
            // -> [다음 스테이지]를 누르든 [로비로]를 누르든, 이어하기는 다음 스테이지 시작부터.
            SaveDataManager.SaveProgressWithTime(nextSceneName, RunManager.AccumulatedTime);
            Debug.Log($"<color=cyan>[EndPoint]</color> 스테이지 클리어(구간 {seg:0.00}s). 누적 기록 → 다음 씬 진행 저장: {nextSceneName}");
            return;
        }


        // 타이머 없음 = 튜토리얼: 완료 화면 표시.
        // 멀티플레이에서 씬 전환은 호스트 권한이므로 클라이언트는 호스트에 위임(트리거 무시).
        // 솔로(러너 없음)는 아래 로직이 SceneManager 폴백으로 진행된다.
        if (runner != null && !runner.IsServer) return;
        isTriggered = true;

        if (TutorialFlowController.Instance != null)
        {
            Debug.Log($"<color=cyan>[EndPoint]</color> (튜토리얼) 완료 화면 표시. 다음 씬: {nextSceneName}");
            TutorialFlowController.Instance.ShowTutorialComplete(nextSceneName);
            return;
        }

        // 폴백: 컨트롤러가 없으면 기존처럼 즉시 이동
        Debug.Log($"<color=cyan>[EndPoint]</color> (타이머 없음) 즉시 이동: {nextSceneName}");
        CheckpointManager.ResetCheckpoint();
        SaveDataManager.SaveProgressWithTime(nextSceneName, RunManager.AccumulatedTime);
        int idx = GetSceneIndex(nextSceneName);
        if (runner != null && runner.IsServer && idx >= 0)
            runner.LoadScene(SceneRef.FromIndex(idx));
        else
            UnityEngine.SceneManagement.SceneManager.LoadScene(nextSceneName);
    }

    private int GetSceneIndex(string sceneName)
    {
        return UnityEngine.SceneManagement.SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{sceneName}.unity");
    }
}
