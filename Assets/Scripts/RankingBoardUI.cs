using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 랭킹 보드 UI (UGUI 기반).
/// NotoSansKR-Bold SDF 한글 폰트를 완벽 적용하여 랭킹 및 닉네임을 선명하게 표시합니다.
/// </summary>
public class RankingBoardUI : MonoBehaviour
{
    public static RankingBoardUI Instance { get; private set; }

    public const int RowCount = 5;

    private static readonly Color Gold = new Color(0.98f, 0.77f, 0.19f);
    private static readonly Color Silver = new Color(0.82f, 0.85f, 0.90f);
    private static readonly Color Bronze = new Color(0.85f, 0.53f, 0.35f);
    private static readonly Color Normal = new Color(0.92f, 0.93f, 0.95f);
    private static readonly Color Dim = new Color(0.55f, 0.58f, 0.65f);
    private static readonly Color PanelColor = new Color(0.08f, 0.10f, 0.14f, 0.96f);

    private GameObject _root;
    private TextMeshProUGUI[] _rankText = new TextMeshProUGUI[RowCount];
    private TextMeshProUGUI[] _nameText = new TextMeshProUGUI[RowCount];
    private TextMeshProUGUI[] _timeText = new TextMeshProUGUI[RowCount];

    public bool IsOpen { get; private set; }
    public static int LastEscapeClosedFrame { get; private set; } = -1;

    public static RankingBoardUI GetOrCreate()
    {
        if (Instance != null) return Instance;
        var existing = FindFirstObjectByType<RankingBoardUI>();
        if (existing != null) return existing;

        var go = new GameObject("RankingBoardUI");
        return go.AddComponent<RankingBoardUI>();
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Build();
            Hide();
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        if (IsOpen && (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Tab)))
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                LastEscapeClosedFrame = Time.frameCount;
            }
            Hide();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            // 설정창이 열려 있거나 입력창 포커스 중이거나 로비 씬(입력창 Tab 이동 우선, 트로피 버튼 사용)일 때는 Tab으로 열지 않음
            if (SettingsMenuUI.Instance != null && SettingsMenuUI.Instance.IsOpen) return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Lobby") return;

            var es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null)
            {
                var go = es.currentSelectedGameObject;
                if (go.GetComponent<InputField>() != null || go.GetComponent<TMP_InputField>() != null)
                    return;
            }

            Show();
        }
    }

    public void Show()
    {
        if (_root != null) _root.SetActive(true);
        IsOpen = true;
    }

    public void Hide()
    {
        if (_root != null) _root.SetActive(false);
        IsOpen = false;
    }

    public void Toggle()
    {
        if (IsOpen) Hide();
        else Show();
    }

    public void SetEntry(int i, int rank, string players, string time)
    {
        if (i < 0 || i >= RowCount) return;
        _rankText[i].text = Ordinal(rank);
        _rankText[i].color = RankColor(rank);
        _nameText[i].text = players;
        _nameText[i].color = Normal;
        _timeText[i].text = time;
        _timeText[i].color = Normal;
    }

    public void SetEmpty(int i, int rank)
    {
        if (i < 0 || i >= RowCount) return;
        _rankText[i].text = Ordinal(rank);
        _rankText[i].color = Dim;
        _nameText[i].text = "-";
        _nameText[i].color = Dim;
        _timeText[i].text = "--:--.--";
        _timeText[i].color = Dim;
    }

    private static Color RankColor(int rank)
    {
        switch (rank)
        {
            case 1: return Gold;
            case 2: return Silver;
            case 3: return Bronze;
            default: return Normal;
        }
    }

    private static string Ordinal(int n)
    {
        int m = n % 100;
        if (m >= 11 && m <= 13) return n + "th";
        switch (n % 10)
        {
            case 1: return n + "st";
            case 2: return n + "nd";
            case 3: return n + "rd";
            default: return n + "th";
        }
    }

    private void Build()
    {
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            es.transform.SetParent(transform, false);
        }

        _root = new GameObject("RankingCanvas");
        _root.transform.SetParent(transform, false);

        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        _root.AddComponent<GraphicRaycaster>();

        var blocker = NewChild("Blocker", _root.transform);
        Stretch(blocker);
        var bImg = blocker.AddComponent<Image>();
        bImg.color = new Color(0.03f, 0.04f, 0.06f, 0.55f);
        bImg.raycastTarget = true;

        const float PW = 980f;
        const float PH = 720f;
        var panel = NewChild("Panel", _root.transform);
        var pr = panel.GetComponent<RectTransform>();
        pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
        pr.pivot = new Vector2(0.5f, 0.5f);
        pr.sizeDelta = new Vector2(PW, PH);
        panel.AddComponent<Image>().color = PanelColor;

        var accent = NewChild("Accent", panel.transform);
        var ar = accent.GetComponent<RectTransform>();
        ar.anchorMin = new Vector2(0f, 1f);
        ar.anchorMax = new Vector2(1f, 1f);
        ar.pivot = new Vector2(0.5f, 1f);
        ar.sizeDelta = new Vector2(0f, 6f);
        ar.anchoredPosition = Vector2.zero;
        accent.AddComponent<Image>().color = Gold;

        AddText(panel.transform, 56f, 30f, 520f, 60f, "RANKING (랭킹)", 46f, FontStyles.Bold, TextAlignmentOptions.Left, Color.white);

        var close = NewChild("Close", panel.transform);
        var cr = close.GetComponent<RectTransform>();
        cr.anchorMin = cr.anchorMax = new Vector2(1f, 1f);
        cr.pivot = new Vector2(1f, 1f);
        cr.sizeDelta = new Vector2(54f, 54f);
        cr.anchoredPosition = new Vector2(-26f, -26f);
        close.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.14f);
        close.AddComponent<Button>().onClick.AddListener(Hide);
        AddTextStretch(close.transform, "X", 30f, new Color(1f, 1f, 1f, 0.9f));

        const float headerY = 128f;
        AddText(panel.transform, 56f, headerY, 160f, 34f, "RANK", 20f, FontStyles.Bold, TextAlignmentOptions.Left, Dim);
        AddText(panel.transform, 236f, headerY, 460f, 34f, "PLAYERS (플레이어)", 20f, FontStyles.Bold, TextAlignmentOptions.Left, Dim);
        AddText(panel.transform, 700f, headerY, 224f, 34f, "TIME (기록)", 20f, FontStyles.Bold, TextAlignmentOptions.Right, Dim);

        var div = NewChild("Divider", panel.transform);
        var dr = div.GetComponent<RectTransform>();
        dr.anchorMin = new Vector2(0f, 1f);
        dr.anchorMax = new Vector2(1f, 1f);
        dr.pivot = new Vector2(0.5f, 1f);
        dr.sizeDelta = new Vector2(-104f, 2f);
        dr.anchoredPosition = new Vector2(0f, -(headerY + 42f));
        div.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.16f);

        const float rowTop = headerY + 58f;
        const float rowH = 98f;
        for (int i = 0; i < RowCount; i++)
        {
            float y = rowTop + i * rowH;

            var rowBg = NewChild("RowBg" + i, panel.transform);
            var rr = rowBg.GetComponent<RectTransform>();
            rr.anchorMin = new Vector2(0f, 1f);
            rr.anchorMax = new Vector2(1f, 1f);
            rr.pivot = new Vector2(0.5f, 1f);
            rr.sizeDelta = new Vector2(-88f, rowH - 12f);
            rr.anchoredPosition = new Vector2(0f, -y);
            rowBg.AddComponent<Image>().color = (i % 2 == 0)
                ? new Color(1f, 1f, 1f, 0.07f)
                : new Color(1f, 1f, 1f, 0.02f);

            _rankText[i] = AddText(panel.transform, 56f, y + 14f, 160f, rowH - 28f, "", 32f, FontStyles.Bold, TextAlignmentOptions.Left, Normal);
            _nameText[i] = AddText(panel.transform, 236f, y + 14f, 460f, rowH - 28f, "", 27f, FontStyles.Normal, TextAlignmentOptions.Left, Normal);
            _timeText[i] = AddText(panel.transform, 700f, y + 14f, 224f, rowH - 28f, "", 29f, FontStyles.Bold, TextAlignmentOptions.Right, Normal);

            SetEmpty(i, i + 1);
        }
    }

    private static GameObject NewChild(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void Stretch(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void Apply(TextMeshProUGUI t)
    {
        if (TMP_Settings.defaultFontAsset != null) t.font = TMP_Settings.defaultFontAsset;
        t.raycastTarget = false;
    }

    private TextMeshProUGUI AddText(Transform panel, float x, float y, float w, float h, string content,
        float size, FontStyles style, TextAlignmentOptions align, Color color)
    {
        var go = NewChild("Txt", panel);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(x, -y);
        var t = go.AddComponent<TextMeshProUGUI>();
        Apply(t);
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = align;
        t.color = color;
        t.text = content;
        return t;
    }

    private void AddTextStretch(Transform parent, string content, float size, Color color)
    {
        var go = NewChild("Txt", parent);
        Stretch(go);
        var t = go.AddComponent<TextMeshProUGUI>();
        Apply(t);
        t.fontSize = size;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.color = color;
        t.text = content;
    }
}
