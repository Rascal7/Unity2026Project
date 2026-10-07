using System;
using System.Collections;
using Fusion;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// NetworkTimer.Phase 에 따라 종료 화면을 띄운다. 씬에 배치할 필요 없이 스스로 부트스트랩된다.
//   AwaitingChoice(중간 클리어) → "STAGE CLEAR" + [다음 스테이지]/[로비로] 선택 (다음은 호스트만)
//   Finished(최종 클리어)       → "ALL CLEAR" + 기록 표시 + 자동 로비 복귀
public class GameEndController : MonoBehaviour
{
    public static GameEndController Instance { get; private set; }

    public static event Action<float> OnRunFinished; // 최종 클리어(합산시간) 브로드캐스트

    [Header("옵션")]
    public string lobbySceneName = "Lobby";
    public float autoReturnDelay = 10f; // 최종 클리어 후 자동 로비 복귀까지 (초)

    private int _shownPhase = -1;   // 현재 화면에 표시 중인 Phase (-1 = 없음)
    private bool _returning;
    private float _shownAtUnscaled;
    private int _pendingRecordRank = -1;   // 이번 런의 순위 (1..5), 없으면 -1

    private GameObject _root;
    private Text _titleText;
    private Text _timeText;
    private Text _hintText;
    private Text _recordText;      // New Record 표시 줄 (ALL CLEAR 카드에 통합)
    private Text _finalList;       // 최종 결과: 스테이지별 기록 나열 + 합산 (애니메이션)
    private GameObject _nextBtn;
    private GameObject _lobbyBtn;
    private Coroutine _finalAnim;
    private const float HintDefaultY = 0.44f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("GameEndController");
        go.AddComponent<GameEndController>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
        RankingManager.OnTopRankAchieved += OnTopRank;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        RankingManager.OnTopRankAchieved -= OnTopRank;
        if (Instance == this) Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _shownPhase = -1;
        _returning = false;
        _pendingRecordRank = -1;
        if (_root != null) _root.SetActive(false);
    }

    private void Update()
    {
        if (_returning) return;

        var nt = NetworkTimer.Instance;
        if (nt == null)
        {
            if (_shownPhase != -1) Hide();
            return;
        }

        int phase = nt.Phase;

        // 게임 진행 중이면 화면 숨김 (인트로/러닝 포함)
        if (phase == NetworkTimer.PhaseRunning || phase == NetworkTimer.PhaseIntro)
        {
            if (_shownPhase != -1) Hide();
            return;
        }

        // 표시할 단계가 바뀌었으면 화면 갱신
        if (phase != _shownPhase)
        {
            if (phase == NetworkTimer.PhaseAwaitingChoice) ShowChoice(nt.FinalTime);
            else if (phase == NetworkTimer.PhaseFinished) ShowFinal(nt.FinalTime);
        }

        // 최종 클리어: 자동 로비 복귀 카운트다운
        if (_shownPhase == NetworkTimer.PhaseFinished)
        {
            float elapsed = Time.unscaledTime - _shownAtUnscaled;
            if (_hintText != null && autoReturnDelay > 0f)
            {
                int left = Mathf.Max(0, Mathf.CeilToInt(autoReturnDelay - elapsed));
                _hintText.text = left + "초 후 로비로 이동   ·   [R] 즉시 이동";
            }
            if (Input.GetKeyDown(KeyCode.R)) { OnLobby(); return; }
            if (autoReturnDelay > 0f && elapsed >= autoReturnDelay) OnLobby();
        }
    }

    // ---------- 화면 표시 ----------
    private void ShowChoice(float segmentTime)
    {
        EnsureUI();
        _shownPhase = NetworkTimer.PhaseAwaitingChoice;
        _shownAtUnscaled = Time.unscaledTime;
        _root.SetActive(true);

        if (_finalAnim != null) { StopCoroutine(_finalAnim); _finalAnim = null; }
        if (_finalList != null) _finalList.gameObject.SetActive(false);
        if (_timeText != null) _timeText.gameObject.SetActive(true);
        SetHintAnchorY(HintDefaultY);

        if (_recordText != null) _recordText.gameObject.SetActive(false); // 중간 클리어엔 New Record 없음
        _titleText.text = "STAGE CLEAR!";
        // 이번 스테이지 기록만 표시 (어느 스테이지인지 명확히)
        _timeText.text = CurrentStageLabel() + " 기록  " + FormatTime(segmentTime);

        bool isHost = IsHost();
        _nextBtn.SetActive(isHost);         // 다음 스테이지는 호스트만
        _lobbyBtn.SetActive(true);          // 로비는 누구나
        _hintText.text = isHost ? "" : "다음 스테이지는 호스트가 진행합니다";

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void ShowFinal(float totalTime)
    {
        EnsureUI();
        _shownPhase = NetworkTimer.PhaseFinished;
        _shownAtUnscaled = Time.unscaledTime;
        _root.SetActive(true);

        _titleText.text = "ALL CLEAR!";
        _nextBtn.SetActive(false);
        _lobbyBtn.SetActive(false);

        // 스테이지별 구간 기록 + 스테이지 번호 + 이전 누적분을 NetworkTimer 에서 읽어 애니메이션으로 나열 → 합산
        var nt = NetworkTimer.Instance;
        int count = nt != null ? Mathf.Clamp(nt.SegmentCount, 0, NetworkTimer.MaxSegments) : 0;
        var times = new float[count];
        var stages = new int[count];
        for (int i = 0; i < count; i++) { times[i] = nt.SegmentTimes.Get(i); stages[i] = nt.SegmentStages.Get(i); }
        float restoredBase = nt != null ? nt.RestoredBase : 0f;

        if (_timeText != null) _timeText.gameObject.SetActive(false); // 최종은 _finalList 로 표시
        if (_finalAnim != null) StopCoroutine(_finalAnim);
        _finalAnim = StartCoroutine(AnimateFinalResults(times, stages, restoredBase, totalTime));

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        Debug.Log($"<color=lime>[Ending]</color> ALL CLEAR - {FormatTime(totalTime)}");
        try { OnRunFinished?.Invoke(totalTime); }
        catch (Exception e) { Debug.LogException(e); }
    }

    // 최종 결과 애니메이션: (이전 기록) + 스테이지별 기록을 하나씩 나열 → 구분선 → 합산(카운트업).
    private IEnumerator AnimateFinalResults(float[] times, int[] stages, float restoredBase, float total)
    {
        SetHintAnchorY(0.14f);                 // 자동복귀 카운트다운 힌트는 아래로 내림
        _finalList.gameObject.SetActive(true);
        _finalList.text = "";
        string acc = "";

        // 개별 내역 없는 복원 누적분(이어하기)이 있으면 먼저 표시해 합산을 투명하게.
        if (restoredBase > 0.05f)
        {
            acc += "이전 기록        " + FormatTime(restoredBase) + "\n";
            _finalList.text = acc;
            yield return new WaitForSecondsRealtime(0.45f);
        }

        for (int i = 0; i < times.Length; i++)
        {
            int sn = (stages != null && i < stages.Length && stages[i] > 0) ? stages[i] : (i + 1);
            acc += sn + "스테이지        " + FormatTime(times[i]) + "\n";
            _finalList.text = acc;
            yield return new WaitForSecondsRealtime(0.45f);
        }

        if (times.Length > 0 || restoredBase > 0.05f)
        {
            acc += "──────────────\n";
            _finalList.text = acc;
            yield return new WaitForSecondsRealtime(0.25f);
        }

        // 합산 기록 카운트업
        float dur = 0.9f, t0 = Time.unscaledTime;
        while (true)
        {
            float k = Mathf.Clamp01((Time.unscaledTime - t0) / dur);
            _finalList.text = acc + "합산 기록        " + FormatTime(total * k);
            if (k >= 1f) break;
            yield return null;
        }
        _finalList.text = acc + "합산 기록        " + FormatTime(total);

        ApplyRecord();      // New Record 순위가 있으면 표시
        _finalAnim = null;
    }

    private void SetHintAnchorY(float y)
    {
        if (_hintText == null) return;
        var rt = _hintText.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, y);
    }

    // RankingManager 가 TOP5 진입을 알려주면 순위를 저장하고, 최종 카드가 떠 있으면 즉시 표시.
    private void OnTopRank(int rank, float time)
    {
        _pendingRecordRank = rank;
        if (_shownPhase == NetworkTimer.PhaseFinished) ApplyRecord();
    }

    private void ApplyRecord()
    {
        if (_recordText == null) return;
        bool top = _pendingRecordRank >= 1 && _pendingRecordRank <= 5;
        if (top) _recordText.text = "★ NEW RECORD ・ " + _pendingRecordRank + "위 ★";
        _recordText.gameObject.SetActive(top);
    }

    private void Hide()
    {
        _shownPhase = -1;
        if (_finalAnim != null) { StopCoroutine(_finalAnim); _finalAnim = null; }
        if (_finalList != null) { _finalList.text = ""; _finalList.gameObject.SetActive(false); }
        if (_timeText != null) _timeText.gameObject.SetActive(true);
        SetHintAnchorY(HintDefaultY);
        if (_root != null) _root.SetActive(false);
    }

    // ---------- 버튼 핸들러 ----------
    private void OnNext()
    {
        var runner = FindFirstObjectByType<NetworkRunner>();
        if (runner == null || !runner.IsServer) return; // 씬 로드는 호스트만

        string next = RunManager.PendingNextScene;
        if (string.IsNullOrEmpty(next))
        {
            Debug.LogWarning("<color=yellow>[Ending]</color> 다음 씬 이름이 비어 있습니다.");
            return;
        }

        CheckpointManager.ResetCheckpoint();
        SaveDataManager.SaveProgressWithTime(next, RunManager.AccumulatedTime);
        Hide();

        int idx = SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{next}.unity");
        runner.LoadScene(SceneRef.FromIndex(idx));
    }

    private async void OnLobby()
    {
        if (_returning) return;
        _returning = true;
        Hide();
        RunManager.ResetRun();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        foreach (var runner in FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None))
        {
            if (runner == null) continue;
            await runner.Shutdown();
            if (runner != null) Destroy(runner.gameObject);
        }

        SceneManager.LoadScene(lobbySceneName);
    }

    private bool IsHost()
    {
        var runner = FindFirstObjectByType<NetworkRunner>();
        return runner != null && runner.IsServer;
    }

    // ---------- 런타임 UI 생성 (legacy uGUI) ----------
    private void EnsureUI()
    {
        if (_root != null) return;

        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(es);
        }

        // 한글 지원 폰트(Sunflower) 우선 로드. Arial(LegacyRuntime)은 한글 글리프가 없어 카드 한글이 깨진다.
        Font font = Resources.Load<Font>("Sunflower-Medium");
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        _root = new GameObject("EndingCanvas");
        DontDestroyOnLoad(_root);
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        _root.AddComponent<GraphicRaycaster>();

        var bg = CreateChild("BG", _root.transform);
        bg.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.82f);
        Stretch(bg.GetComponent<RectTransform>());

        _titleText = CreateText("Title", _root.transform, font, 92, FontStyle.Bold,
            new Vector2(0.5f, 0.70f), new Vector2(1500, 180), "STAGE CLEAR!");
        _titleText.color = new Color(1f, 0.92f, 0.35f);

        _timeText = CreateText("Time", _root.transform, font, 52, FontStyle.Normal,
            new Vector2(0.5f, 0.54f), new Vector2(1500, 120), "");

        _hintText = CreateText("Hint", _root.transform, font, 30, FontStyle.Normal,
            new Vector2(0.5f, 0.44f), new Vector2(1500, 80), "");
        _hintText.color = new Color(1f, 1f, 1f, 0.7f);

        // New Record 줄 (최종 카드 상단, 골드). 기본 숨김.
        _recordText = CreateText("Record", _root.transform, font, 64, FontStyle.Bold,
            new Vector2(0.5f, 0.85f), new Vector2(1600, 130), "");
        _recordText.color = new Color(1f, 0.92f, 0.35f);
        _recordText.gameObject.SetActive(false);

        // 최종 결과: 스테이지별 기록 나열 + 합산 (애니메이션). 기본 숨김, ALL CLEAR 에서만 사용.
        _finalList = CreateText("FinalList", _root.transform, font, 44, FontStyle.Normal,
            new Vector2(0.5f, 0.46f), new Vector2(1400, 440), "");
        _finalList.gameObject.SetActive(false);

        _nextBtn = CreateButton("NextButton", new Vector2(0.5f, 0.28f), new Vector2(200f, 0f),
            new Color(0.98f, 0.82f, 0.42f), new Color(0.12f, 0.07f, 0.02f), "다음 스테이지", font, OnNext);
        _lobbyBtn = CreateButton("LobbyButton", new Vector2(0.5f, 0.28f), new Vector2(-200f, 0f),
            new Color(0.55f, 0.6f, 0.66f), new Color(0.1f, 0.1f, 0.12f), "로비로", font, OnLobby);

        _root.SetActive(false);
    }

    private GameObject CreateButton(string name, Vector2 anchor, Vector2 offset, Color face, Color textColor,
        string label, Font font, UnityEngine.Events.UnityAction onClick)
    {
        var go = CreateChild(name, _root.transform);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.sizeDelta = new Vector2(360, 100);
        rt.anchoredPosition = offset;
        var img = go.AddComponent<Image>();
        img.color = face;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
        var lbl = CreateText("Label", go.transform, font, 36, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), new Vector2(360, 100), label);
        lbl.color = textColor;
        Stretch(lbl.GetComponent<RectTransform>());
        return go;
    }

    private static GameObject CreateChild(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static Text CreateText(string name, Transform parent, Font font, int size,
        FontStyle style, Vector2 anchor, Vector2 size2, string content)
    {
        var go = CreateChild(name, parent);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.sizeDelta = size2;
        rt.anchoredPosition = Vector2.zero;
        var t = go.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.color = Color.white;
        t.text = content;
        return t;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // 현재 씬 이름에서 스테이지 라벨을 만든다. "Main Stage2" → "2스테이지", 숫자 없으면 "스테이지".
    private static string CurrentStageLabel()
    {
        string name = SceneManager.GetActiveScene().name;
        string num = "";
        for (int i = name.Length - 1; i >= 0 && char.IsDigit(name[i]); i--) num = name[i] + num;
        return string.IsNullOrEmpty(num) ? "스테이지" : (num + "스테이지");
    }

    public static string FormatTime(float t)
    {
        if (t < 0f) t = 0f;
        int m = Mathf.FloorToInt(t / 60f);
        int s = Mathf.FloorToInt(t % 60f);
        int cs = Mathf.FloorToInt((t * 100f) % 100f);
        return string.Format("{0:00}:{1:00}.{2:00}", m, s, cs);
    }
}
