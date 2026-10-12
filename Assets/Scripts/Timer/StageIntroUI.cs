using Fusion;
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

    // ----- 스테이지 진입 시 새 스킬 등록 연출 -----
    private GameObject _skillCard;
    private RectTransform _skillCardRt;
    private CanvasGroup _skillCardCg;
    private Image _skillIcon;
    private Text _skillIconLabel;
    private Text _skillName;
    private Text _skillKey;
    private bool _skillTried;     // 이번 인트로에서 공개 시도했는지

    // 카드 연출 단계: 등장(pop) → 유지 → HUD 슬롯으로 날아가 꽂힘.
    private enum CardPhase { None, In, Hold, Fly, Done }
    private CardPhase _cardPhase = CardPhase.None;
    private float _cardT;
    private int _revealSlot;          // 이번에 공개(꽂을) HUD 슬롯
    private Vector3 _flyFrom, _flyTo; // 카드가 날아가는 시작/목표(화면 좌표)
    private const float CardInDur = 0.4f, CardHoldDur = 1.0f, CardFlyDur = 0.7f;
    private static readonly Vector2 CardHomePos = new Vector2(0f, -220f);

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

        // 인트로(타이머 정지) 동안, 플레이어가 준비되면 새 스킬 카드를 한 번 공개
        if (intro && !_skillTried) TryRevealSkill();
        AdvanceSkillAnim();
    }

    private void Show()
    {
        EnsureUI();
        string sceneName = SceneManager.GetActiveScene().name;
        _stageText.text = StageLabel(sceneName);
        _subText.text = SaveDataManager.GetStageDisplayName(sceneName);
        _root.SetActive(true);
        _shown = true;
        _skillTried = false;
        _cardPhase = CardPhase.None;
        _revealSlot = 0;
        if (_skillCard != null) _skillCard.SetActive(false);
    }

    private void Hide()
    {
        if (_root != null) _root.SetActive(false);
        if (_skillCard != null) _skillCard.SetActive(false);
        _shown = false;
        _skillTried = false;
        _cardPhase = CardPhase.None;
        // 연출이 끝나기 전에 숨겨지더라도 슬롯 억제는 반드시 해제(실제 스킬 표시).
        var hud = SkillCooldownHUD.Instance;
        if (hud != null && _revealSlot > 0) hud.FinishSlotReveal(_revealSlot);
        _revealSlot = 0;
    }

    // 이 스테이지에서 새로 열린 슬롯의 스킬을 찾아 카드로 공개한다. (스테이지1=슬롯1, 2=슬롯2, 3=슬롯3)
    private void TryRevealSkill()
    {
        int newSlot = StageSkillRules.MaxSkillSlots(SceneManager.GetActiveScene().name);
        if (newSlot <= 0) { _skillTried = true; return; } // 튜토리얼 등: 스킬 없음

        var player = FindLocalPlayer();
        if (player == null) return; // 아직 스폰 전 → 다음 프레임 재시도

        var skill = player.GetSkillSlotStatus(newSlot).Skill;
        if (skill == PlayerMovement.CharacterSkill.None) { _skillTried = true; return; }

        EnsureSkillCard();
        PopulateSkillCard(skill, newSlot);

        _revealSlot = newSlot;
        var hud = SkillCooldownHUD.Instance;
        if (hud != null) hud.SuppressSlotForReveal(newSlot);   // 그 슬롯을 '비어있음'으로

        _skillCard.SetActive(true);
        if (_skillCardCg != null) _skillCardCg.alpha = 1f;
        _skillCardRt.anchoredPosition = CardHomePos;
        _skillCardRt.localScale = Vector3.zero;
        _cardT = 0f;
        _cardPhase = CardPhase.In;
        _skillTried = true;
    }

    // 카드 연출: 등장(pop) → 유지 → HUD 슬롯으로 날아가 작아지며 꽂힘(슬롯 pop).
    private void AdvanceSkillAnim()
    {
        if (_skillCardRt == null || _skillCard == null || !_skillCard.activeSelf) return;
        float dt = Time.unscaledDeltaTime;

        switch (_cardPhase)
        {
            case CardPhase.In:
            {
                _cardT += dt;
                float t = Mathf.Clamp01(_cardT / CardInDur);
                float e = EaseOutBack(t);
                _skillCardRt.anchoredPosition = CardHomePos;
                _skillCardRt.localScale = new Vector3(e, e, 1f);
                if (_skillCardCg != null) _skillCardCg.alpha = 1f;
                if (t >= 1f) { _cardT = 0f; _cardPhase = CardPhase.Hold; }
                break;
            }
            case CardPhase.Hold:
            {
                _cardT += dt;
                _skillCardRt.localScale = Vector3.one;
                if (_cardT >= CardHoldDur)
                {
                    _cardT = 0f;
                    _flyFrom = _skillCardRt.position;
                    _flyTo = GetSlotWorld() ?? _flyFrom;
                    _cardPhase = CardPhase.Fly;
                }
                break;
            }
            case CardPhase.Fly:
            {
                _cardT += dt;
                float t = Mathf.Clamp01(_cardT / CardFlyDur);
                float e = Mathf.SmoothStep(0f, 1f, t);
                Vector3 to = GetSlotWorld() ?? _flyTo;   // 슬롯 위치가 안정되면 추적
                _skillCardRt.position = Vector3.Lerp(_flyFrom, to, e);
                float sc = Mathf.Lerp(1f, 0.22f, e);
                _skillCardRt.localScale = new Vector3(sc, sc, 1f);
                if (_skillCardCg != null)
                    _skillCardCg.alpha = 1f - Mathf.Clamp01((t - 0.5f) / 0.5f); // 후반부 페이드
                if (t >= 1f)
                {
                    var hud = SkillCooldownHUD.Instance;
                    if (hud != null) hud.FinishSlotReveal(_revealSlot);  // 슬롯에 꽂힘 + pop
                    _skillCard.SetActive(false);
                    _cardPhase = CardPhase.Done;
                }
                break;
            }
        }
    }

    // HUD 공개 슬롯 아이콘의 화면 중심(없으면 null).
    private Vector3? GetSlotWorld()
    {
        var hud = SkillCooldownHUD.Instance;
        if (hud != null && _revealSlot > 0 && hud.TryGetSlotIconWorldCenter(_revealSlot, out var w))
            return w;
        return null;
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float x = t - 1f;
        return 1f + c3 * x * x * x + c1 * x * x;
    }

    private PlayerMovement FindLocalPlayer()
    {
        var players = FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None);
        PlayerMovement cand = null;
        foreach (var p in players)
        {
            if (p == null) continue;
            if (p.GetComponent<Rigidbody2D>() == null) continue;
            if (p.gameObject.name.Contains("Spawner")) continue;
            if (p.Object != null && p.Object.IsValid)
            {
                if (p.Object.HasInputAuthority) return p;
                if (p.Object.HasStateAuthority && cand == null) cand = p;
            }
            else if (cand == null) cand = p;
        }
        return cand;
    }

    private void PopulateSkillCard(PlayerMovement.CharacterSkill skill, int slot)
    {
        var hud = SkillCooldownHUD.Instance;
        Sprite icon = hud != null ? hud.GetCustomIcon(skill) : null;
        if (icon != null)
        {
            _skillIcon.enabled = true;
            _skillIcon.sprite = icon;
            _skillIcon.color = Color.white;
            _skillIconLabel.text = "";
        }
        else
        {
            _skillIcon.enabled = false;
            _skillIconLabel.text = SkillInfo.Symbol(skill);
        }
        _skillName.text = SkillInfo.Name(skill);
        _skillKey.text = "[ " + SkillKeybinds.GetLabel(slot) + " ]";
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

    // 새 스킬 카드 UI(배너 아래 중앙). 한 번만 생성.
    private void EnsureSkillCard()
    {
        if (_skillCard != null) return;
        EnsureUI();

        _skillCard = NewChild("SkillCard", _root.transform);
        _skillCardRt = _skillCard.GetComponent<RectTransform>();
        _skillCardRt.anchorMin = _skillCardRt.anchorMax = new Vector2(0.5f, 0.5f);
        _skillCardRt.pivot = new Vector2(0.5f, 0.5f);
        _skillCardRt.sizeDelta = new Vector2(600f, 184f);
        _skillCardRt.anchoredPosition = CardHomePos;
        _skillCardCg = _skillCard.AddComponent<CanvasGroup>();
        Framed(_skillCard, WoodFill, WoodDark, WoodLight);

        Transform face = _skillCard.transform.Find("Face");
        Transform host = face != null ? face : _skillCard.transform;

        // 아이콘 박스(좌측, 세로 중앙)
        var iconBox = NewChild("IconBox", host);
        var ibr = iconBox.GetComponent<RectTransform>();
        ibr.anchorMin = ibr.anchorMax = new Vector2(0f, 0.5f);
        ibr.pivot = new Vector2(0f, 0.5f);
        ibr.sizeDelta = new Vector2(112f, 112f);
        ibr.anchoredPosition = new Vector2(30f, 0f);
        iconBox.AddComponent<Image>().color = WoodDark;
        var iconInner = AddInset(iconBox.transform, "Inner", 4f);
        iconInner.color = new Color(0.12f, 0.09f, 0.05f, 1f);

        var iconImgGo = NewChild("Icon", iconInner.transform);
        var iir = iconImgGo.GetComponent<RectTransform>();
        iir.anchorMin = Vector2.zero; iir.anchorMax = Vector2.one;
        iir.offsetMin = new Vector2(7f, 7f); iir.offsetMax = new Vector2(-7f, -7f);
        _skillIcon = iconImgGo.AddComponent<Image>();
        _skillIcon.preserveAspect = true; _skillIcon.raycastTarget = false;

        _skillIconLabel = CreateLabel(iconInner.transform, "Sym", new Vector2(0.5f, 0.5f), new Vector2(104f, 104f), "", _pixel, 50, Parch);

        // 우측 3줄 스택: "새 스킬 획득!" / 스킬명 / [키]  (모두 좌측 정렬, x=168)
        var head = LeftLabel(host, 168f, 0.8f, 380f, 36f, "새 스킬 획득!", _plain, 26, Gold);
        AddOutline(head.gameObject, GoldDeep, 2f);

        _skillName = LeftLabel(host, 168f, 0.5f, 400f, 64f, "", _plain, 50, Gold);
        AddOutline(_skillName.gameObject, GoldDeep, 3f);

        _skillKey = LeftLabel(host, 170f, 0.2f, 400f, 44f, "", _pixel, 32, Parch);

        _skillCard.SetActive(false);
    }

    // host 좌상단 기준 왼쪽 정렬 라벨(anchorY=세로 비율).
    private Text LeftLabel(Transform host, float x, float anchorY, float w, float h,
        string text, Font font, int fontSize, Color color)
    {
        var go = NewChild("L", host);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, anchorY);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(x, 0f);
        var t = go.AddComponent<Text>();
        t.font = font != null ? font : _plain;
        t.fontSize = fontSize;
        t.alignment = TextAnchor.MiddleLeft;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.color = color;
        t.text = text;
        t.raycastTarget = false;
        return t;
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
