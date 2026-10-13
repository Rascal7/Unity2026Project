using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 튜토리얼 힌트를 화면에 표시하는 로컬 UI 매니저(싱글턴).
/// - 씬에 수동 배치된 panel/messageText 가 있으면 그대로 사용하고,
///   없으면 게임 테마(우드/골드)에 맞춘 런타임 HUD 배너를 자동 생성한다.
/// - MovementKeybinds / SkillKeybinds 변경 이벤트를 구독하여,
///   설정창(ESC)에서 키를 바꾸면 현재 표시 중인 힌트 문구를 즉시 갱신한다.
/// </summary>
public class TutorialHintUI : MonoBehaviour
{
    public static TutorialHintUI Instance { get; private set; }

    [Tooltip("힌트 배경 패널 (켜고 끔, 미지정 시 런타임 자동 생성)")]
    public GameObject panel;
    [Tooltip("힌트 문구 TMP 텍스트 (미지정 시 런타임 uGUI 텍스트 사용)")]
    public TMP_Text messageText;

    private Text _runtimeText;
    private TutorialHintZone _activeZone;
    private string _activeRawMessage;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("TutorialHintUI");
        go.AddComponent<TutorialHintUI>();
    }

    public static TutorialHintUI EnsureInstance()
    {
        if (Instance != null) return Instance;
        var existing = FindFirstObjectByType<TutorialHintUI>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }
        var go = new GameObject("TutorialHintUI");
        return go.AddComponent<TutorialHintUI>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // 씬에 수동 배치된 TutorialHintUI가 있고 panel 참조를 가지고 있다면 기존 자동 생성 인스턴스에 이관
            if (panel != null && Instance.panel == null)
            {
                Instance.panel = panel;
                Instance.messageText = messageText;
            }
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (panel != null) panel.SetActive(false);

        MovementKeybinds.OnChanged += RefreshActiveHint;
        SkillKeybinds.OnChanged += RefreshActiveHint;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        MovementKeybinds.OnChanged -= RefreshActiveHint;
        SkillKeybinds.OnChanged -= RefreshActiveHint;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Hide();
    }

    public void Show(TutorialHintZone zone)
    {
        if (zone == null) return;
        _activeZone = zone;
        _activeRawMessage = zone.message;
        ApplyAndDisplay(zone.GetFormattedMessage());
    }

    public void Show(string msg)
    {
        _activeZone = null;
        _activeRawMessage = msg;
        ApplyAndDisplay(FormatDynamicText(msg));
    }

    public void Hide(TutorialHintZone zone = null)
    {
        if (zone != null && _activeZone != null && _activeZone != zone) return;
        _activeZone = null;
        _activeRawMessage = null;
        if (panel != null) panel.SetActive(false);
    }

    private void RefreshActiveHint()
    {
        if (panel == null || !panel.activeSelf) return;

        if (_activeZone != null)
        {
            ApplyAndDisplay(_activeZone.GetFormattedMessage());
        }
        else if (!string.IsNullOrEmpty(_activeRawMessage))
        {
            ApplyAndDisplay(FormatDynamicText(_activeRawMessage));
        }
    }

    private void ApplyAndDisplay(string formatted)
    {
        EnsureRuntimePanelIfNeeded();
        if (messageText != null) messageText.text = formatted;
        if (_runtimeText != null) _runtimeText.text = formatted;
        if (panel != null) panel.SetActive(true);
    }

    private void EnsureRuntimePanelIfNeeded()
    {
        if (panel != null) return;

        Font font = Resources.Load<Font>("Sunflower-Medium");
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var canvasGo = new GameObject("TutorialHintCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30500;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var panelGo = new GameObject("HintPanel", typeof(RectTransform));
        panelGo.transform.SetParent(canvasGo.transform, false);
        var rt = panelGo.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.88f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(760f, 84f);
        rt.anchoredPosition = Vector2.zero;

        var borderImg = panelGo.AddComponent<Image>();
        borderImg.color = new Color(0.19f, 0.11f, 0.05f, 0.94f);
        borderImg.raycastTarget = false;

        var bevelGo = new GameObject("Bevel", typeof(RectTransform));
        bevelGo.transform.SetParent(panelGo.transform, false);
        var bevelRt = bevelGo.GetComponent<RectTransform>();
        bevelRt.anchorMin = Vector2.zero;
        bevelRt.anchorMax = Vector2.one;
        bevelRt.offsetMin = new Vector2(4f, 4f);
        bevelRt.offsetMax = new Vector2(-4f, -4f);
        var bevelImg = bevelGo.AddComponent<Image>();
        bevelImg.color = new Color(0.60f, 0.43f, 0.24f, 0.94f);
        bevelImg.raycastTarget = false;

        var faceGo = new GameObject("Face", typeof(RectTransform));
        faceGo.transform.SetParent(panelGo.transform, false);
        var faceRt = faceGo.GetComponent<RectTransform>();
        faceRt.anchorMin = Vector2.zero;
        faceRt.anchorMax = Vector2.one;
        faceRt.offsetMin = new Vector2(8f, 8f);
        faceRt.offsetMax = new Vector2(-8f, -8f);
        var faceImg = faceGo.AddComponent<Image>();
        faceImg.color = new Color(0.28f, 0.18f, 0.10f, 0.94f);
        faceImg.raycastTarget = false;

        var textGo = new GameObject("MessageText", typeof(RectTransform));
        textGo.transform.SetParent(faceGo.transform, false);
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(16f, 4f);
        textRt.offsetMax = new Vector2(-16f, -4f);

        _runtimeText = textGo.AddComponent<Text>();
        _runtimeText.font = font;
        _runtimeText.fontSize = 30;
        _runtimeText.alignment = TextAnchor.MiddleCenter;
        _runtimeText.horizontalOverflow = HorizontalWrapMode.Overflow;
        _runtimeText.verticalOverflow = VerticalWrapMode.Overflow;
        _runtimeText.color = new Color(0.98f, 0.95f, 0.89f, 1f);
        _runtimeText.supportRichText = true;
        _runtimeText.raycastTarget = false;

        var outline = textGo.AddComponent<Outline>();
        outline.effectColor = new Color(0.12f, 0.07f, 0.02f, 0.95f);
        outline.effectDistance = new Vector2(2f, -2f);

        panel = panelGo;
        panel.SetActive(false);
    }

    // ---------- 동적 키바인딩 문자열 포맷 헬퍼 ----------

    public static string FormatActionPair(MovementKeybinds.Act first, MovementKeybinds.Act second)
    {
        string p1 = MovementKeybinds.Label(first, 0);
        string p2 = MovementKeybinds.Label(second, 0);
        string s1 = MovementKeybinds.Label(first, 1);
        string s2 = MovementKeybinds.Label(second, 1);

        bool hasPrimary = p1 != "-" || p2 != "-";
        bool hasSecondary = s1 != "-" || s2 != "-";

        if (hasPrimary && hasSecondary && (p1 != s1 || p2 != s2))
            return $"[{p1}/{p2}] 또는 [{s1}/{s2}]";
        if (hasPrimary)
            return $"[{p1}/{p2}]";
        if (hasSecondary)
            return $"[{s1}/{s2}]";
        return "[-]";
    }

    public static string FormatSingleAction(MovementKeybinds.Act act)
    {
        string p = MovementKeybinds.Label(act, 0);
        string s = MovementKeybinds.Label(act, 1);

        bool hasP = p != "-";
        bool hasS = s != "-" && s != p;

        if (hasP && hasS) return $"[{p}] 또는 [{s}]";
        if (hasP) return $"[{p}]";
        if (hasS) return $"[{s}]";
        return "[-]";
    }

    public static string FormatSkillSlot(int slot)
    {
        string key = SkillKeybinds.GetLabel(slot);
        string voice = slot switch
        {
            1 => "\"대시\" / \"당근\"",
            2 => "\"킁킁\" / \"점프\"",
            3 => "\"체인지\" / \"파워업\"",
            _ => ""
        };
        return string.IsNullOrEmpty(voice)
            ? $"[{key}]"
            : $"[{key}] 또는 음성 {voice}";
    }

    /// <summary>
    /// 고정 문자열("이동:  &lt; &gt;" 등)이나 토큰({Move}, {Jump}, {Ladder}, {Left}, {Right}, {Up}, {Down}, {Skill1~3})을
    /// 현재 설정된 키바인딩 문자열로 동적 치환한다.
    /// </summary>
    public static string FormatDynamicText(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";

        string trimmed = raw.Trim();
        if (trimmed == "이동:  < >" || trimmed == "이동: < >")
            return $"좌우 이동:  <color=#FAD16B>{FormatActionPair(MovementKeybinds.Act.Left, MovementKeybinds.Act.Right)}</color>";

        string result = raw
            .Replace("{Move}", FormatActionPair(MovementKeybinds.Act.Left, MovementKeybinds.Act.Right))
            .Replace("{Ladder}", FormatActionPair(MovementKeybinds.Act.Up, MovementKeybinds.Act.Down))
            .Replace("{Jump}", FormatSingleAction(MovementKeybinds.Act.Jump))
            .Replace("{Left}", FormatSingleAction(MovementKeybinds.Act.Left))
            .Replace("{Right}", FormatSingleAction(MovementKeybinds.Act.Right))
            .Replace("{Up}", FormatSingleAction(MovementKeybinds.Act.Up))
            .Replace("{Down}", FormatSingleAction(MovementKeybinds.Act.Down))
            .Replace("{Skill1}", FormatSkillSlot(1))
            .Replace("{Skill2}", FormatSkillSlot(2))
            .Replace("{Skill3}", FormatSkillSlot(3));

        // 레거시 플레이스홀더 치환
        if (result.Contains("< >"))
            result = result.Replace("< >", FormatActionPair(MovementKeybinds.Act.Left, MovementKeybinds.Act.Right));
        if (result.Contains("^ v") || result.Contains("↑ ↓"))
            result = result.Replace("^ v", FormatActionPair(MovementKeybinds.Act.Up, MovementKeybinds.Act.Down))
                           .Replace("↑ ↓", FormatActionPair(MovementKeybinds.Act.Up, MovementKeybinds.Act.Down));

        return result;
    }
}

