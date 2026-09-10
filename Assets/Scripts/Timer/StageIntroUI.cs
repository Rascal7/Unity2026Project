using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 메인 스테이지 진입 시 "STAGE N" 카드를 잠깐 보여준다(게임 테마: 우드/골드/픽셀).
//  - NetworkTimer.Phase == PhaseIntro 인 동안 표시되고, 타이머가 시작(Running)되면 사라진다.
//  - Phase 는 [Networked] 라 호스트/클라이언트 모두 같은 타이밍에 카드를 본다.
//  - 스테이지 번호는 현재 씬 이름("Main Stage1" -> 1)의 끝 숫자에서 뽑는다.
public class StageIntroUI : MonoBehaviour
{
    public static StageIntroUI Instance { get; private set; }

    private static readonly Color WoodFill  = new Color(0.42f, 0.28f, 0.16f, 1f);
    private static readonly Color WoodDark  = new Color(0.19f, 0.11f, 0.05f, 1f);
    private static readonly Color WoodLight = new Color(0.60f, 0.43f, 0.24f, 1f);
    private static readonly Color Gold      = new Color(0.98f, 0.82f, 0.42f, 1f);
    private static readonly Color GoldDeep  = new Color(0.13f, 0.08f, 0.02f, 1f);
    private static readonly Color Parch     = new Color(0.98f, 0.95f, 0.89f, 1f);

    private Font _pixel;
    private Font _plain;
    private GameObject _root;
    private Text _stageText;
    private Text _subText;
    private bool _shown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("StageIntroUI");
        go.AddComponent<StageIntroUI>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        _pixel = Resources.Load<Font>("BoldPixels");
        _plain = Resources.Load<Font>("Sunflower-Medium");
        if (_plain == null) _plain = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_plain == null) _plain = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (_pixel == null) _pixel = _plain;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Hide(); // 씬 바뀌면 일단 숨김. 인트로 페이즈면 Update 가 다시 띄운다.
    }

    private void Update()
    {
        var nt = NetworkTimer.Instance;
        bool intro = nt != null && nt.Phase == NetworkTimer.PhaseIntro;
        if (intro && !_shown) Show();
        else if (!intro && _shown) Hide();
    }

    private void Show()
    {
        EnsureUI();
        string sceneName = SceneManager.GetActiveScene().name;
        _stageText.text = StageLabel(sceneName);
        _subText.text = SaveDataManager.GetStageDisplayName(sceneName);
        _root.SetActive(true);
        _shown = true;
    }

    private void Hide()
    {
        if (_root != null) _root.SetActive(false);
        _shown = false;
    }

    // "Main Stage1" -> "STAGE 1". 끝 숫자가 없으면 "STAGE".
    private static string StageLabel(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return "STAGE";
        int end = sceneName.Length - 1;
        int start = end;
        while (start >= 0 && char.IsDigit(sceneName[start])) start--;
        if (start == end) return "STAGE"; // 끝에 숫자 없음
        string num = sceneName.Substring(start + 1);
        return "STAGE " + num;
    }

    private void EnsureUI()
    {
        if (_root != null) return;

        _root = new GameObject("StageIntroCanvas");
        DontDestroyOnLoad(_root);
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 31500;
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        _root.AddComponent<GraphicRaycaster>();

        // 가벼운 디밍(배경 스테이지가 비쳐 보이도록 연하게)
        var dim = NewChild("Dim", _root.transform);
        var dimImg = dim.AddComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, 0.38f);
        dimImg.raycastTarget = false;
        Stretch(dim.GetComponent<RectTransform>());

        // 중앙 우드 배너
        var banner = NewChild("Banner", _root.transform);
        var brt = banner.GetComponent<RectTransform>();
        brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
        brt.pivot = new Vector2(0.5f, 0.5f);
        brt.sizeDelta = new Vector2(980f, 300f);
        brt.anchoredPosition = Vector2.zero;
        Framed(banner, WoodFill, WoodDark, WoodLight);

        _stageText = CreateLabel(banner.transform, "Stage", new Vector2(0.5f, 0.62f),
            new Vector2(940f, 170f), "STAGE 1", _pixel, 120, Gold);
        AddOutline(_stageText.gameObject, GoldDeep, 5f);

        _subText = CreateLabel(banner.transform, "Sub", new Vector2(0.5f, 0.26f),
            new Vector2(940f, 80f), "", _plain, 40, Parch);
        AddOutline(_subText.gameObject, new Color(0.06f, 0.06f, 0.04f, 0.9f), 2f);

        _root.SetActive(false);
    }

    private static GameObject NewChild(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void Framed(GameObject go, Color fill, Color darkBorder, Color lightBevel)
    {
        var border = go.AddComponent<Image>();
        border.color = darkBorder;
        border.raycastTarget = false;
        AddInset(go.transform, "Bevel", 8f).color = lightBevel;
        AddInset(go.transform, "Face", 18f).color = fill;
    }

    private static Image AddInset(Transform parent, string name, float pad)
    {
        var go = NewChild(name, parent);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(pad, pad); rt.offsetMax = new Vector2(-pad, -pad);
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    private Text CreateLabel(Transform parent, string name, Vector2 anchor, Vector2 size, string content,
        Font font, int fontSize, Color color)
    {
        var go = NewChild(name, parent);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;
        var t = go.AddComponent<Text>();
        t.font = font != null ? font : _plain;
        t.fontSize = fontSize;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.color = color;
        t.text = content;
        t.raycastTarget = false;
        return t;
    }

    private static void AddOutline(GameObject go, Color color, float dist)
    {
        var o = go.AddComponent<Outline>();
        o.effectColor = color;
        o.effectDistance = new Vector2(dist, -dist);
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
