using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 시작 메인 화면 (Voice Adventure) - Lobby 씬에 배치된 컴포넌트.
//  - [ExecuteAlways] 로 에디터 Game 뷰에서도 미리보기 (생성물은 HideAndDontSave 라 씬에 저장 안 됨)
//  - 배경: 위쪽 하늘 맵 이미지(back.png) + 아래로 갈수록 그라데이션 + 숲(far.png)
//  - 캐릭터(여우/토끼) 좌우 배치 + 가벼운 상하 흔들림
//  - 타이틀 BoldPixels, 입력창 Sunflower(가독성), 랭킹은 트로피 아이콘 버튼
[ExecuteAlways]
public class TitleScreen : MonoBehaviour
{
    public static TitleScreen Instance { get; private set; }

    private GameObject _root;
    private Font _pixel;
    private Font _plain;
    private InputField _nickField;
    private InputField _roomField;
    private Button _hostBtn;
    private Button _joinBtn;
    private Text _noteText;
    private BasicLauncher _launcher;
    private bool _busy;
    private float _busySince;

    private RectTransform _char1;
    private RectTransform _char2;
    private Vector2 _char1Base;
    private Vector2 _char2Base;

    private static readonly Color WoodFill  = new Color(0.42f, 0.28f, 0.16f, 1f);
    private static readonly Color WoodDark  = new Color(0.19f, 0.11f, 0.05f);
    private static readonly Color WoodLight = new Color(0.60f, 0.43f, 0.24f);
    private static readonly Color Gold      = new Color(0.98f, 0.82f, 0.42f);
    private static readonly Color GoldDeep  = new Color(0.13f, 0.08f, 0.02f);
    private static readonly Color Stone     = new Color(0.60f, 0.64f, 0.68f);
    private static readonly Color Parch     = new Color(0.98f, 0.95f, 0.89f);
    private static readonly Color FieldBg   = new Color(0.15f, 0.11f, 0.07f, 1f);
    private static readonly Color Hint      = new Color(0.72f, 0.66f, 0.58f);
    private static readonly Color Ink       = new Color(0.12f, 0.07f, 0.02f);

    private const HideFlags GenFlags = HideFlags.HideAndDontSave;

    private void OnEnable()
    {
        Instance = this;
        _pixel = Resources.Load<Font>("BoldPixels");
        _plain = Resources.Load<Font>("Sunflower-Medium");
        if (_plain == null) _plain = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_pixel == null) _pixel = _plain;
        Rebuild();
    }

    private void OnDisable()
    {
        UnregisterLauncherEvents();
        DestroyGenerated();
    }

    private void OnDestroy()
    {
        UnregisterLauncherEvents();
        DestroyGenerated();
        if (Instance == this) Instance = null;
    }

    private void Rebuild()
    {
        DestroyGenerated();
        Build();
        RefreshMenu();
    }

    private void DestroyGenerated()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var c = transform.GetChild(i).gameObject;
            if (c.name == "TitleCanvas" || c.name == "TitleEventSystem")
            {
                if (Application.isPlaying) Destroy(c);
                else DestroyImmediate(c);
            }
        }
        _root = null;
        _char1 = null;
        _char2 = null;
    }

    private void RefreshMenu()
    {
        _busy = false;
        EnsureLauncher();
        SetInteractable(true);
        if (_nickField != null)
            _nickField.text = PlayerPrefs.GetString("PlayerName", "Player" + Random.Range(100, 999));
        if (_noteText != null)
            _noteText.text = "";
    }

    private void EnsureLauncher()
    {
        if (_launcher == null)
        {
            _launcher = FindObjectOfType<BasicLauncher>();
        }
        RegisterLauncherEvents();
    }

    private void RegisterLauncherEvents()
    {
        if (_launcher != null)
        {
            _launcher.OnConnectionStarted -= HandleConnectionStarted;
            _launcher.OnConnectionStarted += HandleConnectionStarted;
            _launcher.OnConnectionFailed -= HandleConnectionFailed;
            _launcher.OnConnectionFailed += HandleConnectionFailed;
            _launcher.OnConnectionSuccess -= HandleConnectionSuccess;
            _launcher.OnConnectionSuccess += HandleConnectionSuccess;
        }
    }

    private void UnregisterLauncherEvents()
    {
        if (_launcher != null)
        {
            _launcher.OnConnectionStarted -= HandleConnectionStarted;
            _launcher.OnConnectionFailed -= HandleConnectionFailed;
            _launcher.OnConnectionSuccess -= HandleConnectionSuccess;
        }
    }

    private void HandleConnectionStarted()
    {
        _busy = true;
        _busySince = Time.realtimeSinceStartup;
        SetInteractable(false);
    }

    private void HandleConnectionFailed(string reason)
    {
        _busy = false;
        SetInteractable(true);
        if (_noteText != null)
        {
            _noteText.text = string.IsNullOrEmpty(reason) ? "접속 실패 - 다시 시도하세요" : reason;
        }
    }

    private void HandleConnectionSuccess()
    {
        _busy = false;
        if (_noteText != null)
        {
            _noteText.text = "접속 성공! 방으로 이동 중...";
        }
    }

    private void Update()
    {
        float t = Time.realtimeSinceStartup;
        if (_char1 != null) _char1.anchoredPosition = _char1Base + new Vector2(0f, Mathf.Sin(t * 2.0f) * 8f);
        if (_char2 != null) _char2.anchoredPosition = _char2Base + new Vector2(0f, Mathf.Sin(t * 2.0f + 1.6f) * 8f);

        if (!Application.isPlaying) return;

        // 비상용 안전 타임아웃 (네트워크 지연을 감안해 15초)
        if (_busy && t - _busySince > 15f)
        {
            _busy = false;
            SetInteractable(true);
            if (_noteText != null) _noteText.text = "연결 시간 초과 - 다시 시도하세요";
            if (_launcher != null) _ = _launcher.CleanupRunnerAsync();
        }
    }

    private void SetInteractable(bool v)
    {
        if (_hostBtn != null) _hostBtn.interactable = v;
        if (_joinBtn != null) _joinBtn.interactable = v;
    }

    private void OnHost()
    {
        if (!Application.isPlaying || _busy) return;
        EnsureLauncher();
        if (_launcher == null) return;
        _busy = true; _busySince = Time.realtimeSinceStartup;
        SetInteractable(false);
        if (_noteText != null) _noteText.text = "방 만드는 중...";
        _launcher.RequestHost(_roomField != null ? _roomField.text : "", _nickField != null ? _nickField.text : "");
    }

    private void OnJoin()
    {
        if (!Application.isPlaying || _busy) return;
        EnsureLauncher();
        if (_launcher == null) return;
        if (_roomField == null || string.IsNullOrWhiteSpace(_roomField.text))
        {
            if (_noteText != null) _noteText.text = "방 코드를 입력하세요";
            return;
        }
        _busy = true; _busySince = Time.realtimeSinceStartup;
        SetInteractable(false);
        if (_noteText != null) _noteText.text = "접속 중...";
        _launcher.RequestJoin(_roomField.text, _nickField != null ? _nickField.text : "");
    }

    private void OnRanking()
    {
        if (!Application.isPlaying) return;
        var rm = FindObjectOfType<RankingManager>();
        if (rm != null) rm.ToggleBoard();
        else RankingBoardUI.GetOrCreate().Toggle();
    }

    // ================= UI 빌드 =================
    private void Build()
    {
        if (FindObjectOfType<EventSystem>() == null)
        {
            var es = new GameObject("TitleEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            es.hideFlags = GenFlags;
            es.transform.SetParent(transform, false);
        }

        _root = new GameObject("TitleCanvas");
        _root.hideFlags = GenFlags;
        _root.transform.SetParent(transform, false);
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        _root.AddComponent<GraphicRaycaster>();

        // ---- 배경: back.png 한 장을 화면 전체로 늘림 (화면 비율에 맞춰 stretch) ----
        var bgTex = Resources.Load<Texture2D>("Title/back");
        if (bgTex != null)
        {
            var bg = NewChild("Background", _root.transform);
            var brt = bg.GetComponent<RectTransform>();
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
            brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
            var braw = bg.AddComponent<RawImage>();
            braw.texture = bgTex;
            braw.raycastTarget = false;
        }

        // ---- 캐릭터 ----
        _char1 = MakeCharacter("Char1_Fox", "Title/char1", new Vector2(0f, 0f), new Vector2(320f, 155f), 320f, false);
        _char2 = MakeCharacter("Char2_Rabbit", "Title/char2", new Vector2(1f, 0f), new Vector2(-320f, 155f), 320f, true);
        if (_char1 != null) _char1Base = _char1.anchoredPosition;
        if (_char2 != null) _char2Base = _char2.anchoredPosition;

        // ---- 타이틀 ----
        MakeTitle(_root.transform, "VOICE ADVENTURE", 150f, new Vector2(0.5f, 0.875f), 1820f);
        var sub = AddLabel(_root.transform, "보이스 어드벤쳐", 40f, new Vector2(0.5f, 0.70f), 1200f, TextAnchor.MiddleCenter, Parch, _plain);
        var subOut = sub.gameObject.AddComponent<Outline>();
        subOut.effectColor = new Color(0.06f, 0.14f, 0.10f, 0.95f);
        subOut.effectDistance = new Vector2(3f, -3f);

        // ---- 메뉴 패널 ----
        var panel = NewChild("Panel", _root.transform);
        var pr = panel.GetComponent<RectTransform>();
        pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
        pr.pivot = new Vector2(0.5f, 0.5f);
        pr.sizeDelta = new Vector2(760f, 540f);
        pr.anchoredPosition = new Vector2(0f, -150f);
        Framed(panel, WoodFill, WoodDark, WoodLight);

        AddLabel(panel.transform, "NICKNAME", 30f, new Vector2(0f, 1f), 400f, TextAnchor.UpperLeft, Parch, _pixel, new Vector2(52f, -44f));
        _nickField = MakeInput(panel.transform, new Vector2(52f, -92f), 656f, 84f, "이름을 입력하세요");

        AddLabel(panel.transform, "ROOM CODE", 30f, new Vector2(0f, 1f), 400f, TextAnchor.UpperLeft, Parch, _pixel, new Vector2(52f, -206f));
        _roomField = MakeInput(panel.transform, new Vector2(52f, -254f), 656f, 84f, "HOST 시 자동 생성");

        _hostBtn = MakeButton(panel.transform, new Vector2(52f, -374f), 312f, 112f, "HOST", Gold, GoldDeep, OnHost);
        _joinBtn = MakeButton(panel.transform, new Vector2(396f, -374f), 312f, 112f, "JOIN", Stone, new Color(0.13f, 0.13f, 0.15f), OnJoin);

        _noteText = AddLabel(_root.transform, "", 26f, new Vector2(0.5f, 0.5f), 1000f, TextAnchor.MiddleCenter,
            new Color(0.62f, 0.16f, 0.10f), _plain, new Vector2(0f, -452f));

        // ---- 트로피(랭킹) 버튼 ----
        MakeIconButton(_root.transform, "Title/trophy", new Vector2(1f, 1f), new Vector2(-40f, -40f), 92f, OnRanking);
        // 폰트 라이선스(BoldPixels, CC BY-SA 4.0) 표기는 README / 크레딧 화면에서 처리.

        SetGenFlagsRecursive(_root.transform);
    }

    private static void SetGenFlagsRecursive(Transform root)
    {
        root.gameObject.hideFlags = GenFlags;
        for (int i = 0; i < root.childCount; i++)
            SetGenFlagsRecursive(root.GetChild(i));
    }

    // ================= 헬퍼 =================
    private static GameObject NewChild(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.hideFlags = GenFlags;
        go.transform.SetParent(parent, false);
        return go;
    }

    private static Image FullImage(string name, Transform parent)
    {
        var go = NewChild(name, parent);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return go.AddComponent<Image>();
    }

    private static Sprite GradientSprite(Color top, Color mid, Color bot, int h)
    {
        var tex = new Texture2D(1, h, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        for (int y = 0; y < h; y++)
        {
            float f = y / (float)(h - 1);
            Color c = f < 0.5f ? Color.Lerp(bot, mid, f * 2f) : Color.Lerp(mid, top, (f - 0.5f) * 2f);
            tex.SetPixel(0, y, c);
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, 1f, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    // 텍스처의 [uvY, uvY+uvH] 세로 구간을 화면의 [bottomFrac, topFrac] 밴드에 가로 반복
    private void AddParallax(string name, Texture2D tex, float bottomFrac, float topFrac, float tilesX, float uvY, float uvH, Color tint)
    {
        if (tex == null) return;
        var go = NewChild(name, _root.transform);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, bottomFrac);
        rt.anchorMax = new Vector2(1f, topFrac);
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var raw = go.AddComponent<RawImage>();
        raw.texture = tex;
        raw.color = tint;
        raw.raycastTarget = false;
        raw.uvRect = new Rect(0f, uvY, Mathf.Max(1f, tilesX), uvH);
    }

    private RectTransform MakeCharacter(string name, string spritePath, Vector2 anchor, Vector2 pos, float size, bool mirror)
    {
        var sprites = Resources.LoadAll<Sprite>(spritePath);
        Sprite sp = (sprites != null && sprites.Length > 0) ? sprites[0] : Resources.Load<Sprite>(spritePath);
        if (sp == null) return null;

        var go = NewChild(name, _root.transform);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0f);
        float ratio = sp.rect.height <= 0f ? 1f : sp.rect.width / sp.rect.height;
        rt.sizeDelta = new Vector2(size * ratio, size);
        rt.anchoredPosition = pos;
        if (mirror) rt.localScale = new Vector3(-1f, 1f, 1f);
        var img = go.AddComponent<Image>();
        img.sprite = sp;
        img.preserveAspect = true;
        img.raycastTarget = false;
        return rt;
    }

    private static void Framed(GameObject go, Color fill, Color darkBorder, Color lightBevel)
    {
        go.AddComponent<Image>().color = darkBorder;
        AddInset(go.transform, "Bevel", 6f).color = lightBevel;
        AddInset(go.transform, "Face", 12f).color = fill;
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

    private void MakeTitle(Transform parent, string text, float size, Vector2 anchor, float width)
    {
        var sh = AddLabel(parent, text, size, anchor, width, TextAnchor.MiddleCenter, GoldDeep, _pixel);
        sh.GetComponent<RectTransform>().anchoredPosition += new Vector2(9f, -13f);

        var main = AddLabel(parent, text, size, anchor, width, TextAnchor.MiddleCenter, Gold, _pixel);
        var outline = main.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.09f, 0.05f, 0.01f, 1f);
        outline.effectDistance = new Vector2(5f, -5f);
    }

    private Text AddLabel(Transform parent, string content, float size, Vector2 anchor, float width,
        TextAnchor align, Color color, Font font, Vector2? offset = null)
    {
        var go = NewChild("Label", parent);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(anchor.x, anchor.y);
        rt.sizeDelta = new Vector2(width, Mathf.Max(size * 1.7f, 56f));
        rt.anchoredPosition = offset ?? Vector2.zero;
        var t = go.AddComponent<Text>();
        t.font = font != null ? font : _plain;
        t.fontSize = Mathf.RoundToInt(size);
        t.alignment = align;
        t.color = color;
        t.text = content;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    private InputField MakeInput(Transform panel, Vector2 topLeft, float w, float h, string placeholder)
    {
        var go = NewChild("Field", panel);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = topLeft;

        var bg = go.AddComponent<Image>();
        bg.color = FieldBg;
        var input = go.AddComponent<InputField>();
        input.targetGraphic = bg;

        var textGO = NewChild("Text", go.transform);
        InsetRect(textGO, 22f, 6f);
        var txt = textGO.AddComponent<Text>();
        txt.font = _plain;
        txt.fontSize = 40;
        txt.color = Parch;
        txt.alignment = TextAnchor.MiddleLeft;
        txt.supportRichText = false;
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        txt.verticalOverflow = VerticalWrapMode.Truncate;

        var phGO = NewChild("Placeholder", go.transform);
        InsetRect(phGO, 22f, 6f);
        var ph = phGO.AddComponent<Text>();
        ph.font = _plain;
        ph.fontSize = 34;
        ph.fontStyle = FontStyle.Italic;
        ph.color = Hint;
        ph.alignment = TextAnchor.MiddleLeft;
        ph.text = placeholder;

        input.textComponent = txt;
        input.placeholder = ph;
        input.caretColor = Gold;
        input.customCaretColor = true;
        input.caretWidth = 4;
        input.lineType = InputField.LineType.SingleLine;
        input.characterLimit = 16;
        return input;
    }

    private static void InsetRect(GameObject go, float padX, float padY)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padX, padY);
        rt.offsetMax = new Vector2(-padX, -padY);
    }

    private Button MakeButton(Transform parent, Vector2 topLeft, float w, float h, string label,
        Color face, Color shadow, UnityEngine.Events.UnityAction onClick)
    {
        var go = NewChild("Btn_" + label, parent);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = topLeft;

        var hit = go.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        var btn = go.AddComponent<Button>();

        var shImg = AddInset(go.transform, "Shadow", 0f);
        shImg.rectTransform.offsetMin = new Vector2(0f, -8f);
        shImg.rectTransform.offsetMax = new Vector2(0f, -8f);
        shImg.color = shadow;

        var faceImg = AddInset(go.transform, "Face", 0f);
        faceImg.color = face;
        btn.targetGraphic = faceImg;
        var cb = btn.colors;
        cb.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
        cb.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.55f);
        cb.fadeDuration = 0.05f;
        btn.colors = cb;
        btn.onClick.AddListener(onClick);

        var l = NewChild("L", faceImg.transform);
        InsetRect(l, 0f, 0f);
        var lt = l.AddComponent<Text>();
        lt.font = _pixel;
        lt.fontSize = 46;
        lt.alignment = TextAnchor.MiddleCenter;
        lt.color = Ink;
        lt.text = label;
        lt.raycastTarget = false;
        return btn;
    }

    private void MakeIconButton(Transform parent, string spritePath, Vector2 anchor, Vector2 pos, float size,
        UnityEngine.Events.UnityAction onClick)
    {
        var go = NewChild("IconBtn", parent);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = pos;

        var img = go.AddComponent<Image>();
        var sprites = Resources.LoadAll<Sprite>(spritePath);
        Sprite sp = (sprites != null && sprites.Length > 0) ? sprites[0] : Resources.Load<Sprite>(spritePath);
        if (sp != null) { img.sprite = sp; img.preserveAspect = true; }
        else img.color = Gold;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var cb = btn.colors;
        cb.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        cb.fadeDuration = 0.05f;
        btn.colors = cb;
        btn.onClick.AddListener(onClick);
    }
}
