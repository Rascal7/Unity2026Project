using System;
using Fusion;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 튜토리얼 전용 흐름 컨트롤러 (런타임 생성 uGUI). 게임 테마(우드/골드/픽셀)에 맞춰 꾸민다.
//  - 튜토리얼 씬에서 우상단에 [건너뛰기] 버튼을 표시한다. 누르면 확인 없이 바로 본 스테이지로.
//  - 튜토리얼을 정상 완료(EndPoint 도달)하면 ShowTutorialComplete() 로 축하/확인 화면을 띄운다.
//    -> [도전하기]: 진행도 저장 후 본 스테이지(Main Stage1) 로드
//    -> [로비로]:   러너 종료 후 로비 씬으로
//
//  씬 전환은 Fusion 에서 호스트(서버) 권한이다:
//    - 호스트 또는 솔로(러너 없음): 직접 로드 (솔로는 SceneManager 폴백으로 서버 없이도 테스트 가능)
//    - 클라이언트: 버튼 비활성 + 안내(호스트가 진행)
public class TutorialFlowController : MonoBehaviour
{
    public static TutorialFlowController Instance { get; private set; }

    [Header("설정")]
    public string tutorialSceneName = "TutorialStage";
    public string defaultMainStage = "Main Stage1";
    public string lobbySceneName = "Lobby";

    private string _targetScene = "Main Stage1";
    private bool _confirmShown;
    private bool _busy;

    // ----- 테마 팔레트 (TitleScreen 과 동일 톤) -----
    private static readonly Color WoodFill  = new Color(0.42f, 0.28f, 0.16f, 1f);
    private static readonly Color WoodDark  = new Color(0.19f, 0.11f, 0.05f, 1f);
    private static readonly Color WoodLight = new Color(0.60f, 0.43f, 0.24f, 1f);
    private static readonly Color Gold      = new Color(0.98f, 0.82f, 0.42f, 1f);
    private static readonly Color GoldDeep  = new Color(0.13f, 0.08f, 0.02f, 1f);
    private static readonly Color Stone     = new Color(0.55f, 0.60f, 0.66f, 1f);
    private static readonly Color Parch     = new Color(0.98f, 0.95f, 0.89f, 1f);
    private static readonly Color Ink       = new Color(0.12f, 0.07f, 0.02f, 1f);

    // UI
    private Font _pixel;   // BoldPixels (라틴/숫자 전용)
    private Font _plain;   // Sunflower-Medium (한글)
    private GameObject _skipRoot;
    private GameObject _skipBtn;

    // ----- 조작 안내 패널(체크리스트 + 입력 피드백) -----
    private GameObject _guideRoot;
    private readonly System.Collections.Generic.List<GuideItem> _guideItems = new System.Collections.Generic.List<GuideItem>();
    private Text _guideFooter;             // 모두 완료 시 "트로피까지!" 안내
    private PlayerMovement _localPlayer;   // 사다리 등반 감지용(로컬)

    // 체크리스트 한 줄: 체크박스/라벨/키 + 완료 판정과 키 라벨 제공자.
    private class GuideItem
    {
        public Image check;
        public Text label;
        public Text key;
        public bool done;
        public float pulse;
        public System.Func<bool> pressed;    // 이 동작이 입력되는 중인지
        public System.Func<string> keyText;  // 현재 바인딩 키 라벨
    }

    private GameObject _confirmRoot;
    private Text _titleText;
    private Text _bodyText;
    private GameObject _challengeBtn;
    private GameObject _lobbyBtn;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("TutorialFlowController");
        go.AddComponent<TutorialFlowController>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        LoadFonts();
        SceneManager.sceneLoaded += OnSceneLoaded;
        // 키 재설정 시 조작 안내를 즉시 갱신
        MovementKeybinds.OnChanged += RefreshGuide;
        SkillKeybinds.OnChanged += RefreshGuide;
    }

    private void Start()
    {
        // 부트스트랩 시점에 이미 활성화된 첫 씬은 sceneLoaded 가 불리지 않으므로 여기서 한 번 검사.
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        MovementKeybinds.OnChanged -= RefreshGuide;
        SkillKeybinds.OnChanged -= RefreshGuide;
        if (Instance == this) Instance = null;
    }

    private void LoadFonts()
    {
        _pixel = Resources.Load<Font>("BoldPixels");
        _plain = Resources.Load<Font>("Sunflower-Medium");
        if (_plain == null) _plain = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_plain == null) _plain = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (_pixel == null) _pixel = _plain;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _confirmShown = false;
        _busy = false;
        if (_confirmRoot != null) _confirmRoot.SetActive(false);

        bool inTutorial = scene.name == tutorialSceneName;
        if (inTutorial)
        {
            EnsureSkipUI();
            EnsureGuideUI();
            foreach (var it in _guideItems) { it.done = false; it.pulse = 0f; }
            _localPlayer = null;
            RefreshGuide();
            var ep = FindFirstObjectByType<EndPoint>();
            _targetScene = (ep != null && !string.IsNullOrEmpty(ep.nextSceneName)) ? ep.nextSceneName : defaultMainStage;
        }
        if (_skipRoot != null) _skipRoot.SetActive(inTutorial);
        if (_guideRoot != null) _guideRoot.SetActive(inTutorial);
    }

    private void Update()
    {
        if (_skipBtn != null)
        {
            bool show = !_confirmShown && SceneManager.GetActiveScene().name == tutorialSceneName && LocalCanControlScene();
            if (_skipBtn.activeSelf != show) _skipBtn.SetActive(show);
        }

        UpdateGuide();
    }

    // 체크리스트 진행 + 입력 피드백(키 반짝) 갱신.
    private void UpdateGuide()
    {
        if (_guideRoot == null || !_guideRoot.activeSelf || _guideItems.Count == 0) return;

        bool allDone = true;
        foreach (var it in _guideItems)
        {
            if (it.pressed())
            {
                it.pulse = 0.25f;
                if (!it.done) { it.done = true; it.check.color = Gold; it.label.color = Gold; }
            }
            PulseKey(it.key, ref it.pulse);
            if (!it.done) allDone = false;
        }

        if (_guideFooter != null && _guideFooter.gameObject.activeSelf != allDone)
            _guideFooter.gameObject.SetActive(allDone);
    }

    // 사다리 등반은 로컬 플레이어의 실제 등반 상태로 판정(위/아래 키를 아무데서나 눌러 통과하지 못하게).
    private bool LadderPressed()
    {
        var p = GetLocalPlayer();
        return p != null && p.IsClimbingNow;
    }

    private PlayerMovement GetLocalPlayer()
    {
        if (_localPlayer != null && _localPlayer.gameObject != null &&
            (_localPlayer.Object == null || _localPlayer.Object.IsValid))
            return _localPlayer;

        _localPlayer = null;
        PlayerMovement cand = null;
        foreach (var p in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
        {
            if (p == null) continue;
            if (p.GetComponent<Rigidbody2D>() == null) continue;
            if (p.gameObject.name.Contains("Spawner")) continue;
            if (p.Object != null && p.Object.IsValid)
            {
                if (p.Object.HasInputAuthority) { _localPlayer = p; return p; }
                if (p.Object.HasStateAuthority && cand == null) cand = p;
            }
            else if (cand == null) cand = p;
        }
        _localPlayer = cand;
        return cand;
    }

    // 키 라벨이 눌린 순간 밝게 확대 → 서서히 원복.
    private void PulseKey(Text key, ref float timer)
    {
        if (key == null) return;
        if (timer > 0f)
        {
            timer -= Time.deltaTime;
            float t = Mathf.Clamp01(timer / 0.25f);
            key.color = Color.Lerp(Gold, Color.white, t);
            float s = 1f + 0.22f * t;
            key.rectTransform.localScale = new Vector3(s, s, 1f);
        }
        else
        {
            key.color = Gold;
            key.rectTransform.localScale = Vector3.one;
        }
    }

    // ---------- 외부 진입점 ----------

    public void ShowTutorialComplete(string nextSceneName)
    {
        if (_confirmShown) return;
        if (!string.IsNullOrEmpty(nextSceneName)) _targetScene = nextSceneName;
        EnsureConfirmUI();
        _confirmShown = true;
        _confirmRoot.SetActive(true);
        // 튜토리얼엔 스테이지 타이머 결과 화면이 없으므로 완료 축하 화면에서 클리어 징글을 직접 재생
        if (!SfxPlayer.Play("Jingle_StageClear")) UiSfx.PopupOpen();
        if (_guideRoot != null) _guideRoot.SetActive(false); // 완료 화면에선 안내 숨김

        bool canControl = LocalCanControlScene();
        _bodyText.text = canControl ? "본 스테이지에 도전하시겠어요?" : "호스트가 본 스테이지를 시작합니다...";
        _challengeBtn.SetActive(canControl);
        _lobbyBtn.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnSkip()
    {
        if (_busy) return;
        GoToMainStage(_targetScene);
    }

    private void OnChallenge()
    {
        if (_busy) return;
        GoToMainStage(_targetScene);
    }

    // ---------- 씬 전환 ----------

    private bool LocalCanControlScene()
    {
        var runner = FindFirstObjectByType<NetworkRunner>();
        return runner == null || runner.IsServer;
    }

    private void GoToMainStage(string sceneName)
    {
        if (_busy) return;
        if (string.IsNullOrEmpty(sceneName)) sceneName = defaultMainStage;

        var runner = FindFirstObjectByType<NetworkRunner>();
        if (runner != null && !runner.IsServer)
        {
            Debug.Log("<color=yellow>[Tutorial]</color> 클라이언트는 본 스테이지 진입을 호스트에 위임합니다.");
            return;
        }

        _busy = true;
        CheckpointManager.ResetCheckpoint();
        SaveDataManager.SaveProgressWithTime(sceneName, RunManager.AccumulatedTime);
        HideAll();

        int idx = SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{sceneName}.unity");
        if (runner != null && runner.IsServer && idx >= 0)
        {
            Debug.Log($"<color=lime>[Tutorial]</color> 호스트: 본 스테이지 로드 → {sceneName}");
            runner.LoadScene(SceneRef.FromIndex(idx));
        }
        else
        {
            Debug.Log($"<color=lime>[Tutorial]</color> 솔로: 본 스테이지 로드 → {sceneName}");
            SceneManager.LoadScene(sceneName);
        }
    }

    private async void OnLobby()
    {
        if (_busy) return;
        _busy = true;
        HideAll();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        foreach (var runner in FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None))
        {
            if (runner == null) continue;
            try { await runner.Shutdown(); } catch (Exception e) { Debug.LogException(e); }
            if (runner != null) Destroy(runner.gameObject);
        }

        SceneManager.LoadScene(lobbySceneName);
    }

    private void HideAll()
    {
        if (_skipRoot != null) _skipRoot.SetActive(false);
        if (_guideRoot != null) _guideRoot.SetActive(false);
        if (_confirmRoot != null) _confirmRoot.SetActive(false);
    }

    // ---------- UI 빌드 (테마 적용) ----------

    private void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(es);
        }
    }

    private Canvas NewCanvas(string name, int sortingOrder)
    {
        var root = new GameObject(name);
        DontDestroyOnLoad(root);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    private void EnsureSkipUI()
    {
        if (_skipRoot != null) return;
        EnsureEventSystem();

        var canvas = NewCanvas("TutorialSkipCanvas", 31000);
        _skipRoot = canvas.gameObject;

        // 우상단 우드 프레임 버튼: "건너뛰기 ▶" (골드 한글 + 외곽선)
        _skipBtn = MakeWoodButton(_skipRoot.transform, "SkipButton",
            new Vector2(1f, 1f), new Vector2(-40f, -40f), new Vector2(230f, 78f),
            WoodFill, "건너뛰기  ▶", Gold, _plain, 30, OnSkip, 0.92f);

        _skipRoot.SetActive(false);
    }

    // 좌상단 조작 안내 패널(체크리스트 + 입력 피드백): 좌우 이동 / 점프.
    // 실제 바인딩 키를 표시하고, 키를 눌러 해보면 체크된다. 키 재설정 시 자동 갱신.
    private void EnsureGuideUI()
    {
        if (_guideRoot != null) return;
        EnsureEventSystem();

        var canvas = NewCanvas("TutorialGuideCanvas", 30900);
        _guideRoot = canvas.gameObject;

        // 좌상단 우드 패널 (테두리 → 베벨 → 어두운 면)
        var panel = NewChild("GuidePanel", _guideRoot.transform);
        var prt = panel.GetComponent<RectTransform>();
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0f, 1f);
        prt.sizeDelta = new Vector2(500f, 322f);
        prt.anchoredPosition = new Vector2(40f, -40f);
        var border = AddInset(panel.transform, "Border", 0f); border.color = WoodDark;
        var bevel  = AddInset(panel.transform, "Bevel", 4f);  bevel.color = WoodLight;
        var face   = AddInset(panel.transform, "Face", 8f);   face.color = new Color(0.15f, 0.11f, 0.07f, 0.94f);
        Transform P = face.transform;

        GuideText(P, new Vector2(18f, -8f), new Vector2(460f, 40f), "조작 안내", _plain, 30, Gold, TextAnchor.UpperLeft);

        _guideItems.Clear();
        BuildRow(P, -56f,  "왼쪽으로 이동",  () => MovementKeybinds.Pressed(MovementKeybinds.Act.Left),  () => MoveKey(MovementKeybinds.Act.Left));
        BuildRow(P, -102f, "오른쪽으로 이동", () => MovementKeybinds.Pressed(MovementKeybinds.Act.Right), () => MoveKey(MovementKeybinds.Act.Right));
        BuildRow(P, -148f, "점프",         () => MovementKeybinds.Pressed(MovementKeybinds.Act.Jump),  () => MoveKey(MovementKeybinds.Act.Jump));
        BuildRow(P, -194f, "사다리 오르기",  LadderPressed,
            () => MoveKey(MovementKeybinds.Act.Up) + "   " + MoveKey(MovementKeybinds.Act.Down));

        _guideFooter = GuideText(P, new Vector2(18f, -250f), new Vector2(460f, 40f),
            "좋아요!  이제 트로피까지 가보세요", _plain, 24, Parch, TextAnchor.MiddleLeft);
        _guideFooter.gameObject.SetActive(false);

        _guideRoot.SetActive(false);
    }

    // 체크박스 + 설명 + 키 한 줄을 만들어 _guideItems 에 등록.
    private void BuildRow(Transform parent, float y, string desc,
        System.Func<bool> pressed, System.Func<string> keyText)
    {
        // 체크박스 (어두운 테두리 + 내부 상자: 완료 시 골드로)
        var boxBorder = NewChild("ChkBorder", parent);
        var bbr = boxBorder.GetComponent<RectTransform>();
        bbr.anchorMin = bbr.anchorMax = bbr.pivot = new Vector2(0f, 1f);
        bbr.sizeDelta = new Vector2(30f, 30f);
        bbr.anchoredPosition = new Vector2(18f, y - 4f);
        boxBorder.AddComponent<Image>().color = WoodDark;
        var check = AddInset(boxBorder.transform, "Chk", 4f);
        check.color = new Color(0.10f, 0.08f, 0.05f, 1f); // 미완료: 어두움

        var label = GuideText(parent, new Vector2(60f, y), new Vector2(210f, 36f), desc, _plain, 25, Parch, TextAnchor.MiddleLeft);
        var key = GuideText(parent, new Vector2(280f, y), new Vector2(210f, 36f), "", _pixel, 26, Gold, TextAnchor.MiddleLeft);

        _guideItems.Add(new GuideItem { check = check, label = label, key = key, pressed = pressed, keyText = keyText });
    }

    // 좌상단 기준(anchor 0,1) 픽셀 좌표 배치 라벨.
    private Text GuideText(Transform parent, Vector2 anchoredPos, Vector2 size, string text,
        Font font, int fontSize, Color color, TextAnchor align)
    {
        var go = NewChild("T", parent);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;
        var t = go.AddComponent<Text>();
        t.font = font != null ? font : _plain;
        t.fontSize = fontSize;
        t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.color = color;
        t.text = text;
        t.raycastTarget = false;
        return t;
    }

    // 현재 바인딩 키 + 체크 상태 반영.
    private void RefreshGuide()
    {
        if (_guideItems.Count == 0) return;

        bool allDone = true;
        var dark = new Color(0.10f, 0.08f, 0.05f, 1f);
        foreach (var it in _guideItems)
        {
            if (it.key != null) it.key.text = it.keyText();
            it.check.color = it.done ? Gold : dark;
            it.label.color = it.done ? Gold : Parch;
            if (!it.done) allDone = false;
        }
        if (_guideFooter != null) _guideFooter.gameObject.SetActive(allDone);
    }

    private static string MoveKey(MovementKeybinds.Act a) => SkillKeybinds.KeyLabel(MovementKeybinds.Get(a, 0));

    private void EnsureConfirmUI()
    {
        if (_confirmRoot != null) return;
        EnsureEventSystem();

        var canvas = NewCanvas("TutorialConfirmCanvas", 32001);
        _confirmRoot = canvas.gameObject;

        // 어두운 배경
        var bg = NewChild("BG", _confirmRoot.transform);
        var bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.04f, 0.03f, 0.02f, 0.82f);
        Stretch(bg.GetComponent<RectTransform>());

        // 제목: 픽셀 폰트 영문 (테마), 골드 + 외곽선
        _titleText = CreateLabel(_confirmRoot.transform, "Title", new Vector2(0.5f, 0.68f),
            new Vector2(1500f, 180f), "TUTORIAL CLEAR!", _pixel, 110, Gold, TextAnchor.MiddleCenter);
        AddOutline(_titleText.gameObject, GoldDeep, 5f);

        // 부제: 한글
        _bodyText = CreateLabel(_confirmRoot.transform, "Body", new Vector2(0.5f, 0.53f),
            new Vector2(1500f, 120f), "본 스테이지에 도전하시겠어요?", _plain, 46, Parch, TextAnchor.MiddleCenter);
        AddOutline(_bodyText.gameObject, new Color(0.06f, 0.14f, 0.10f, 0.95f), 2f);

        _challengeBtn = MakeWoodButton(_confirmRoot.transform, "ChallengeButton",
            new Vector2(0.5f, 0.34f), new Vector2(215f, 0f), new Vector2(400f, 108f),
            Gold, "본 스테이지 도전하기", Ink, _plain, 34, OnChallenge, 1f);

        _lobbyBtn = MakeWoodButton(_confirmRoot.transform, "LobbyButton",
            new Vector2(0.5f, 0.34f), new Vector2(-215f, 0f), new Vector2(400f, 108f),
            Stone, "로비로", Parch, _plain, 34, OnLobby, 1f);

        _confirmRoot.SetActive(false);
    }

    // 우드 프레임(테두리/베벨/면) + 라벨을 가진 버튼. 반환값은 버튼 GameObject.
    // faceColor 가 골드/스톤이면 버튼 면색으로, 우드면 기본 우드룩으로 쓰인다.
    private GameObject MakeWoodButton(Transform parent, string name, Vector2 anchor, Vector2 offset, Vector2 size,
        Color faceColor, string label, Color labelColor, Font labelFont, int fontSize,
        UnityEngine.Events.UnityAction onClick, float alpha)
    {
        var go = NewChild(name, parent);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.sizeDelta = size;
        rt.anchoredPosition = offset;

        // 투명 히트영역 + 버튼 컴포넌트
        var hit = go.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        var btn = go.AddComponent<Button>();

        // 테두리(어두운) → 베벨(밝은) → 면
        var border = AddInset(go.transform, "Border", 0f);
        border.color = WithAlpha(WoodDark, alpha);
        var bevel = AddInset(go.transform, "Bevel", 4f);
        bevel.color = WithAlpha(WoodLight, alpha);
        var face = AddInset(go.transform, "Face", 8f);
        face.color = WithAlpha(faceColor, alpha);
        btn.targetGraphic = face;

        var cb = btn.colors;
        cb.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
        cb.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        cb.fadeDuration = 0.05f;
        btn.colors = cb;
        btn.onClick.AddListener(onClick);

        var lbl = CreateLabel(face.transform, "Label", new Vector2(0.5f, 0.5f), size, label, labelFont, fontSize, labelColor, TextAnchor.MiddleCenter);
        Stretch(lbl.GetComponent<RectTransform>());
        AddOutline(lbl.gameObject, GoldDeep, 2f);
        return go;
    }

    private static Color WithAlpha(Color c, float a) { c.a = a; return c; }

    private static GameObject NewChild(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
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
        Font font, int fontSize, Color color, TextAnchor align)
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
        t.alignment = align;
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
