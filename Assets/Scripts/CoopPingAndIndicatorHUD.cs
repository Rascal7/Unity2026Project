using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 2인 협동 전용 HUD:
/// 1) 파트너가 카메라 화면 밖에 있을 때 화면 가장자리에 파트너 캐릭터 아이콘/방향 화살표 표시 (Off-screen Indicator)
/// 2) 단축키(G / 마우스 휠클릭 / 우클릭 조준) 또는 음성("여기")으로 원하는 벽/발판 위치에 협동 핑 마커를 찍어 파트너와 공유
/// </summary>
public class CoopPingAndIndicatorHUD : MonoBehaviour
{
    public static CoopPingAndIndicatorHUD Instance { get; private set; }

    private Canvas _canvas;
    private RectTransform _canvasRt;

    // 화면 밖 파트너 위치 표시기 UI (글자 없이 얼굴 아이콘만 표시)
    private GameObject _partnerIndicatorRoot;
    private RectTransform _partnerIndicatorRt;
    private CanvasGroup _partnerIndicatorGroup;
    private RectTransform _partnerArrowRt;
    private Text _partnerArrowText;
    private Image _partnerBadgeBorder;
    private Image _partnerFaceImg;

    private static Sprite _foxFaceSprite;
    private static Sprite _rabbitFaceSprite;

    private static Sprite GetFoxFaceSprite()
    {
        if (_foxFaceSprite == null) _foxFaceSprite = Resources.Load<Sprite>("Title/char1");
        return _foxFaceSprite;
    }

    private static Sprite GetRabbitFaceSprite()
    {
        if (_rabbitFaceSprite == null) _rabbitFaceSprite = Resources.Load<Sprite>("Title/char2");
        return _rabbitFaceSprite;
    }

    // 화면 밖 핑 위치 표시기 UI
    private GameObject _pingOffscreenRoot;
    private RectTransform _pingOffscreenRt;
    private RectTransform _pingOffscreenArrowRt;
    private Text _pingOffscreenText;

    // 활성 월드 핑 마커 관리
    private class ActivePingMarker
    {
        public GameObject root;
        public RectTransform cardRt;
        public RectTransform ringRt;
        public Image ringImg;
        public CanvasGroup group;
        public Vector2 worldPos;
        public float timer;
        public float duration;
    }

    private readonly List<ActivePingMarker> _activePings = new List<ActivePingMarker>();
    private bool _isAimingWithRightMouse;
    private CursorLockMode _prevCursorLockState = CursorLockMode.None;
    private bool _prevCursorVisible = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
    }

    public static CoopPingAndIndicatorHUD EnsureInstance()
    {
        if (Instance != null) return Instance;
        var existing = FindFirstObjectByType<CoopPingAndIndicatorHUD>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        var go = new GameObject("CoopPingAndIndicatorHUD");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<CoopPingAndIndicatorHUD>();
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        EnsureUI();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ClearAllPings();
        _isAimingWithRightMouse = false;
    }

    private void EnsureUI()
    {
        if (_canvas != null) return;

        var canvasGo = new GameObject("CoopHUD_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.overrideSorting = true;
        _canvas.sortingOrder = 490; // 인게임 위, 모달/설정창 아래
        _canvasRt = canvasGo.GetComponent<RectTransform>();

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        BuildPartnerIndicatorUI(canvasGo.transform);
        BuildPingOffscreenUI(canvasGo.transform);
    }

    private void BuildPartnerIndicatorUI(Transform parent)
    {
        _partnerIndicatorRoot = new GameObject("PartnerOffscreenIndicator", typeof(RectTransform), typeof(CanvasGroup));
        _partnerIndicatorRoot.transform.SetParent(parent, false);
        _partnerIndicatorRt = _partnerIndicatorRoot.GetComponent<RectTransform>();
        _partnerIndicatorRt.anchorMin = _partnerIndicatorRt.anchorMax = new Vector2(0.5f, 0.5f);
        _partnerIndicatorRt.pivot = new Vector2(0.5f, 0.5f);
        _partnerIndicatorRt.sizeDelta = new Vector2(72f, 72f);

        _partnerIndicatorGroup = _partnerIndicatorRoot.GetComponent<CanvasGroup>();
        _partnerIndicatorGroup.alpha = 0f;
        _partnerIndicatorGroup.interactable = false;
        _partnerIndicatorGroup.blocksRaycasts = false;

        // 회전하는 외곽 방향 화살표
        var arrowGo = new GameObject("DirectionArrow", typeof(RectTransform), typeof(Text), typeof(Outline));
        arrowGo.transform.SetParent(_partnerIndicatorRoot.transform, false);
        _partnerArrowRt = arrowGo.GetComponent<RectTransform>();
        _partnerArrowRt.anchorMin = _partnerArrowRt.anchorMax = new Vector2(0.5f, 0.5f);
        _partnerArrowRt.pivot = new Vector2(0.5f, 0.5f);
        _partnerArrowRt.sizeDelta = new Vector2(36f, 36f);
        _partnerArrowText = arrowGo.GetComponent<Text>();
        _partnerArrowText.font = LobbyUITheme.PlainFont;
        _partnerArrowText.fontSize = 28;
        _partnerArrowText.fontStyle = FontStyle.Bold;
        _partnerArrowText.alignment = TextAnchor.MiddleCenter;
        _partnerArrowText.color = LobbyUITheme.Gold;
        _partnerArrowText.text = "▶";
        _partnerArrowText.raycastTarget = false;
        var arrowOutline = arrowGo.GetComponent<Outline>();
        arrowOutline.effectColor = LobbyUITheme.GoldDeep;
        arrowOutline.effectDistance = new Vector2(2f, -2f);

        // 항상 수평을 유지하는 파트너 얼굴 배지 (외곽 테두리)
        var cardGo = new GameObject("PartnerCard", typeof(RectTransform), typeof(Image), typeof(Outline));
        cardGo.transform.SetParent(_partnerIndicatorRoot.transform, false);
        var cardRt = cardGo.GetComponent<RectTransform>();
        cardRt.anchorMin = cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.pivot = new Vector2(0.5f, 0.5f);
        cardRt.sizeDelta = new Vector2(60f, 60f);
        _partnerBadgeBorder = cardGo.GetComponent<Image>();
        _partnerBadgeBorder.color = new Color(0.95f, 0.52f, 0.18f, 0.96f);
        _partnerBadgeBorder.raycastTarget = false;
        var cardOutline = cardGo.GetComponent<Outline>();
        cardOutline.effectColor = new Color(0.10f, 0.06f, 0.02f, 0.95f);
        cardOutline.effectDistance = new Vector2(2.5f, -2.5f);

        // 내부 어두운 면
        var innerGo = new GameObject("Inner", typeof(RectTransform), typeof(Image));
        innerGo.transform.SetParent(cardGo.transform, false);
        var innerRt = innerGo.GetComponent<RectTransform>();
        innerRt.anchorMin = Vector2.zero;
        innerRt.anchorMax = Vector2.one;
        innerRt.offsetMin = new Vector2(3f, 3f);
        innerRt.offsetMax = new Vector2(-3f, -3f);
        var innerImg = innerGo.GetComponent<Image>();
        innerImg.color = new Color(0.16f, 0.10f, 0.05f, 0.94f);
        innerImg.raycastTarget = false;

        // 파트너 캐릭터 얼굴 아이콘 (Title/char1 또는 Title/char2)
        var faceGo = new GameObject("FaceIcon", typeof(RectTransform), typeof(Image));
        faceGo.transform.SetParent(innerGo.transform, false);
        var faceRt = faceGo.GetComponent<RectTransform>();
        faceRt.anchorMin = Vector2.zero;
        faceRt.anchorMax = Vector2.one;
        faceRt.offsetMin = new Vector2(2f, 2f);
        faceRt.offsetMax = new Vector2(-2f, -2f);
        _partnerFaceImg = faceGo.GetComponent<Image>();
        _partnerFaceImg.preserveAspect = true;
        _partnerFaceImg.raycastTarget = false;

        _partnerIndicatorRoot.SetActive(false);
    }

    private void BuildPingOffscreenUI(Transform parent)
    {
        _pingOffscreenRoot = new GameObject("PingOffscreenIndicator", typeof(RectTransform));
        _pingOffscreenRoot.transform.SetParent(parent, false);
        _pingOffscreenRt = _pingOffscreenRoot.GetComponent<RectTransform>();
        _pingOffscreenRt.anchorMin = _pingOffscreenRt.anchorMax = new Vector2(0.5f, 0.5f);
        _pingOffscreenRt.pivot = new Vector2(0.5f, 0.5f);
        _pingOffscreenRt.sizeDelta = new Vector2(130f, 44f);

        var arrowGo = new GameObject("Arrow", typeof(RectTransform), typeof(Text), typeof(Outline));
        arrowGo.transform.SetParent(_pingOffscreenRoot.transform, false);
        _pingOffscreenArrowRt = arrowGo.GetComponent<RectTransform>();
        _pingOffscreenArrowRt.anchorMin = _pingOffscreenArrowRt.anchorMax = new Vector2(0.5f, 0.5f);
        _pingOffscreenArrowRt.sizeDelta = new Vector2(40f, 40f);
        var arrowTxt = arrowGo.GetComponent<Text>();
        arrowTxt.font = LobbyUITheme.PlainFont;
        arrowTxt.fontSize = 28;
        arrowTxt.fontStyle = FontStyle.Bold;
        arrowTxt.alignment = TextAnchor.MiddleCenter;
        arrowTxt.color = new Color(1f, 0.88f, 0.22f, 1f);
        arrowTxt.text = "▶";
        arrowTxt.raycastTarget = false;
        var ao = arrowGo.GetComponent<Outline>();
        ao.effectColor = LobbyUITheme.GoldDeep;
        ao.effectDistance = new Vector2(2f, -2f);

        var badgeGo = new GameObject("PingBadge", typeof(RectTransform), typeof(Image), typeof(Outline));
        badgeGo.transform.SetParent(_pingOffscreenRoot.transform, false);
        var badgeRt = badgeGo.GetComponent<RectTransform>();
        badgeRt.anchorMin = badgeRt.anchorMax = new Vector2(0.5f, 0.5f);
        badgeRt.sizeDelta = new Vector2(116f, 36f);
        badgeGo.GetComponent<Image>().color = new Color(0.32f, 0.20f, 0.05f, 0.95f);
        var bo = badgeGo.GetComponent<Outline>();
        bo.effectColor = new Color(1f, 0.86f, 0.22f, 1f);
        bo.effectDistance = new Vector2(2f, -2f);

        var txtGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
        txtGo.transform.SetParent(badgeGo.transform, false);
        var txtRt = txtGo.GetComponent<RectTransform>();
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = txtRt.offsetMax = Vector2.zero;
        _pingOffscreenText = txtGo.GetComponent<Text>();
        _pingOffscreenText.font = LobbyUITheme.PlainFont;
        _pingOffscreenText.fontSize = 15;
        _pingOffscreenText.fontStyle = FontStyle.Bold;
        _pingOffscreenText.alignment = TextAnchor.MiddleCenter;
        _pingOffscreenText.color = new Color(1f, 0.92f, 0.38f, 1f);
        _pingOffscreenText.raycastTarget = false;

        _pingOffscreenRoot.SetActive(false);
    }

    private void Update()
    {
        if (!BasicLauncher.IsInGameStage())
        {
            if (_partnerIndicatorRoot != null && _partnerIndicatorRoot.activeSelf)
                _partnerIndicatorRoot.SetActive(false);
            if (_pingOffscreenRoot != null && _pingOffscreenRoot.activeSelf)
                _pingOffscreenRoot.SetActive(false);
            return;
        }

        EnsureUI();
        HandleLocalPingInput();
        UpdateActivePingMarkers();
    }

    private void LateUpdate()
    {
        if (!BasicLauncher.IsInGameStage() || Camera.main == null) return;

        UpdatePartnerOffscreenIndicator();
        UpdatePingOffscreenIndicator();
    }

    private void HandleLocalPingInput()
    {
        if (SettingsMenuUI.GameplayInputSuppressed) return;
        if (BasicLauncher.Instance != null && BasicLauncher.Instance.IsDisconnectModalOpen) return;

        PlayerNameTag localTag = FindLocalPlayerNameTag();
        if (localTag == null) return;

        // 1) 빠른 핑: G 키 또는 마우스 휠클릭(Mouse2) → 캐릭터 정면(당근/발판 위치)에 즉시 핑 + "여기!" 말풍선
        if (Input.GetKeyDown(KeyCode.G) || Input.GetKeyDown(KeyCode.Mouse2))
        {
            localTag.RequestVoiceBubble("여기!");
            localTag.TriggerCoopPingAtCharacterFront("여기!");
            return;
        }

        // 2) 마우스 조준 핑: 우클릭(Mouse1)을 누르고 있는 동안 커서를 잠시 활성화하여 원하는 벽/발판 위치를 정확히 찍을 수 있음
        if (Input.GetMouseButtonDown(1))
        {
            _isAimingWithRightMouse = true;
            _prevCursorLockState = Cursor.lockState;
            _prevCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (_isAimingWithRightMouse)
        {
            // 우클릭을 떼거나 좌클릭을 누른 지점에 월드 좌표 핑 발사
            if (Input.GetMouseButtonUp(1) || Input.GetMouseButtonDown(0))
            {
                _isAimingWithRightMouse = false;
                if (Camera.main != null)
                {
                    Vector3 mp = Input.mousePosition;
                    mp.z = -Camera.main.transform.position.z;
                    Vector3 world = Camera.main.ScreenToWorldPoint(mp);
                    localTag.RequestVoiceBubble("여기!");
                    localTag.TriggerCoopPingAtWorldPos(new Vector2(world.x, world.y), "여기!");
                }

                // 2D 플랫폼 게임이므로 강제로 Locked/숨김 처리하지 않고 직전 커서 상태로 복원
                if (SettingsMenuUI.Instance == null || !SettingsMenuUI.Instance.IsOpen)
                {
                    Cursor.lockState = _prevCursorLockState;
                    Cursor.visible = _prevCursorVisible;
                }
            }
        }
    }

    private PlayerNameTag FindLocalPlayerNameTag()
    {
        var local = PlayerMovement.GetLocalPlayer();
        return local != null ? local.GetComponent<PlayerNameTag>() : null;
    }

    private void UpdatePartnerOffscreenIndicator()
    {
        if (_partnerIndicatorRoot == null) return;

        PlayerMovement partnerPlayer = null;

        foreach (var p in PlayerMovement.ActivePlayers())
        {
            if (p == null || p.Object == null || !p.Object.IsValid) continue;
            if (!p.Object.HasInputAuthority)
            {
                partnerPlayer = p;
                break;
            }
        }

        if (partnerPlayer == null)
        {
            if (_partnerIndicatorRoot.activeSelf) _partnerIndicatorRoot.SetActive(false);
            return;
        }

        Camera cam = Camera.main;
        Vector3 partnerWorld = partnerPlayer.transform.position + new Vector3(0f, 0.5f, 0f);
        Vector3 vp = cam.WorldToViewportPoint(partnerWorld);

        bool onScreen = vp.z > 0f && vp.x >= 0.03f && vp.x <= 0.97f && vp.y >= 0.04f && vp.y <= 0.96f;
        if (onScreen)
        {
            if (_partnerIndicatorRoot.activeSelf) _partnerIndicatorRoot.SetActive(false);
            return;
        }

        if (!_partnerIndicatorRoot.activeSelf) _partnerIndicatorRoot.SetActive(true);
        _partnerIndicatorGroup.alpha = 1f;

        PlaceIndicatorOnScreenEdge(vp, _partnerIndicatorRt, _partnerArrowRt, 54f, 54f, 48f);

        // 파트너 정보(여우/토끼 얼굴 아이콘 및 테두리/화살표 색상) 갱신
        var tag = partnerPlayer.GetComponent<PlayerNameTag>();
        bool isFox = tag != null ? tag.IsFoxCharacter() : partnerPlayer.IsFoxCharacter;

        Color charCol = isFox ? new Color(0.96f, 0.52f, 0.16f, 1f) : new Color(0.32f, 0.78f, 0.96f, 1f);
        if (_partnerBadgeBorder != null) _partnerBadgeBorder.color = charCol;
        if (_partnerArrowText != null) _partnerArrowText.color = charCol;
        if (_partnerFaceImg != null)
        {
            _partnerFaceImg.sprite = isFox ? GetFoxFaceSprite() : GetRabbitFaceSprite();
        }
    }

    private void UpdatePingOffscreenIndicator()
    {
        if (_pingOffscreenRoot == null) return;

        if (_activePings.Count == 0 || Camera.main == null)
        {
            if (_pingOffscreenRoot.activeSelf) _pingOffscreenRoot.SetActive(false);
            return;
        }

        var latest = _activePings[_activePings.Count - 1];
        Camera cam = Camera.main;
        Vector3 vp = cam.WorldToViewportPoint(new Vector3(latest.worldPos.x, latest.worldPos.y, 0f));
        bool onScreen = vp.z > 0f && vp.x >= 0.04f && vp.x <= 0.96f && vp.y >= 0.05f && vp.y <= 0.95f;

        if (onScreen)
        {
            if (_pingOffscreenRoot.activeSelf) _pingOffscreenRoot.SetActive(false);
            return;
        }

        if (!_pingOffscreenRoot.activeSelf) _pingOffscreenRoot.SetActive(true);
        PlaceIndicatorOnScreenEdge(vp, _pingOffscreenRt, _pingOffscreenArrowRt, 110f, 88f, 74f);

        float dist = Vector2.Distance(cam.transform.position, latest.worldPos);
        _pingOffscreenText.text = $"✦ 핑 {dist:0}m";
    }

    private void PlaceIndicatorOnScreenEdge(Vector3 viewportPos, RectTransform rootRt, RectTransform arrowRt, float marginX, float marginY, float arrowOffset = 74f)
    {
        if (_canvasRt == null) return;
        Vector2 canvasSize = _canvasRt.rect.size;
        if (canvasSize.x <= 1f || canvasSize.y <= 1f) canvasSize = new Vector2(1920f, 1080f);

        Vector2 dir = new Vector2(viewportPos.x - 0.5f, viewportPos.y - 0.5f);
        if (viewportPos.z < 0f) dir = -dir;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right;
        dir.Normalize();

        float halfW = canvasSize.x * 0.5f - marginX;
        float halfH = canvasSize.y * 0.5f - marginY;

        float tx = Mathf.Abs(dir.x) > 0.0001f ? halfW / Mathf.Abs(dir.x) : float.MaxValue;
        float ty = Mathf.Abs(dir.y) > 0.0001f ? halfH / Mathf.Abs(dir.y) : float.MaxValue;
        float t = Mathf.Min(tx, ty);

        Vector2 edgePos = dir * t;
        // 우측 하단 스킬 HUD 영역 침범 방지
        if (edgePos.x > halfW - 360f && edgePos.y < -halfH + 170f)
        {
            edgePos.y = -halfH + 170f;
        }

        rootRt.anchoredPosition = edgePos;

        if (arrowRt != null)
        {
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            arrowRt.localRotation = Quaternion.Euler(0f, 0f, angle);
            arrowRt.anchoredPosition = dir * arrowOffset;
        }
    }

    public void SpawnPingMarker(Vector2 worldPos, string senderName, bool isFox, string label)
    {
        UiSfx.Click();

        // 기존 핑이 너무 많으면 가장 오래된 것 정리
        while (_activePings.Count >= 3)
        {
            if (_activePings[0].root != null) Destroy(_activePings[0].root);
            _activePings.RemoveAt(0);
        }

        var rootGo = new GameObject("CoopPingMarker", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        rootGo.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);
        rootGo.transform.localScale = new Vector3(0.01f, 0.01f, 1f);

        var canvas = rootGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 235;

        var rootRt = (RectTransform)rootGo.transform;
        rootRt.sizeDelta = new Vector2(260f, 160f);

        var group = rootGo.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        Color accent = isFox ? new Color(1f, 0.56f, 0.16f, 1f) : new Color(0.32f, 0.82f, 1f, 1f);

        // 퍼져나가는 파동 링
        var ringGo = new GameObject("PulseRing", typeof(RectTransform), typeof(Image), typeof(Outline));
        ringGo.transform.SetParent(rootGo.transform, false);
        var ringRt = ringGo.GetComponent<RectTransform>();
        ringRt.anchorMin = ringRt.anchorMax = new Vector2(0.5f, 0.25f);
        ringRt.pivot = new Vector2(0.5f, 0.5f);
        ringRt.anchoredPosition = Vector2.zero;
        ringRt.sizeDelta = new Vector2(36f, 36f);
        var ringImg = ringGo.GetComponent<Image>();
        ringImg.color = new Color(accent.r, accent.g, accent.b, 0.55f);
        var ringOutline = ringGo.GetComponent<Outline>();
        ringOutline.effectColor = Color.white;
        ringOutline.effectDistance = new Vector2(2f, -2f);

        // 상단 핑 배지 카드
        var cardGo = new GameObject("PingCard", typeof(RectTransform), typeof(Image), typeof(Outline));
        cardGo.transform.SetParent(rootGo.transform, false);
        var cardRt = cardGo.GetComponent<RectTransform>();
        cardRt.anchorMin = cardRt.anchorMax = new Vector2(0.5f, 0.25f);
        cardRt.pivot = new Vector2(0.5f, 0f);
        cardRt.anchoredPosition = new Vector2(0f, 26f);
        cardRt.sizeDelta = new Vector2(190f, 52f);
        cardGo.GetComponent<Image>().color = new Color(0.18f, 0.11f, 0.04f, 0.96f);
        var cardOutline = cardGo.GetComponent<Outline>();
        cardOutline.effectColor = accent;
        cardOutline.effectDistance = new Vector2(3f, -3f);

        var txtGo = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(Outline));
        txtGo.transform.SetParent(cardGo.transform, false);
        var txtRt = txtGo.GetComponent<RectTransform>();
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = new Vector2(6f, 2f);
        txtRt.offsetMax = new Vector2(-6f, -2f);
        var txt = txtGo.GetComponent<Text>();
        txt.font = LobbyUITheme.PlainFont;
        txt.fontSize = 22;
        txt.fontStyle = FontStyle.Bold;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        txt.color = new Color(1f, 0.94f, 0.65f, 1f);
        string roleTag = isFox ? "여우" : "토끼";
        txt.text = $"[{roleTag}] {label}";
        var txtOutline = txtGo.GetComponent<Outline>();
        txtOutline.effectColor = LobbyUITheme.GoldDeep;
        txtOutline.effectDistance = new Vector2(1.5f, -1.5f);

        // 아래 화살표 포인터 (▼)
        var ptrGo = new GameObject("Pointer", typeof(RectTransform), typeof(Text), typeof(Outline));
        ptrGo.transform.SetParent(cardGo.transform, false);
        var ptrRt = ptrGo.GetComponent<RectTransform>();
        ptrRt.anchorMin = ptrRt.anchorMax = new Vector2(0.5f, 0f);
        ptrRt.pivot = new Vector2(0.5f, 1f);
        ptrRt.anchoredPosition = new Vector2(0f, 4f);
        ptrRt.sizeDelta = new Vector2(40f, 28f);
        var ptrTxt = ptrGo.GetComponent<Text>();
        ptrTxt.font = LobbyUITheme.PlainFont;
        ptrTxt.fontSize = 24;
        ptrTxt.alignment = TextAnchor.UpperCenter;
        ptrTxt.color = accent;
        ptrTxt.text = "▼";
        var ptrOutline = ptrGo.GetComponent<Outline>();
        ptrOutline.effectColor = LobbyUITheme.GoldDeep;
        ptrOutline.effectDistance = new Vector2(2f, -2f);

        _activePings.Add(new ActivePingMarker
        {
            root = rootGo,
            cardRt = cardRt,
            ringRt = ringRt,
            ringImg = ringImg,
            group = group,
            worldPos = worldPos,
            timer = 4.2f,
            duration = 4.2f
        });
    }

    private void UpdateActivePingMarkers()
    {
        Quaternion camRot = Camera.main != null ? Camera.main.transform.rotation : Quaternion.identity;

        for (int i = _activePings.Count - 1; i >= 0; i--)
        {
            var m = _activePings[i];
            if (m.root == null)
            {
                _activePings.RemoveAt(i);
                continue;
            }

            m.timer -= Time.deltaTime;
            if (m.timer <= 0f)
            {
                Destroy(m.root);
                _activePings.RemoveAt(i);
                continue;
            }

            m.root.transform.rotation = camRot;
            float elapsed = m.duration - m.timer;

            // 위아래로 통통 튀는 포인터 배지 연출
            if (m.cardRt != null)
            {
                float bounce = Mathf.Abs(Mathf.Sin(elapsed * 6.5f)) * 12f;
                m.cardRt.anchoredPosition = new Vector2(0f, 22f + bounce);
            }

            // 동심원 파동 확산 연출
            if (m.ringRt != null && m.ringImg != null)
            {
                float cycle = Mathf.Repeat(elapsed * 1.4f, 1f);
                float ringSize = Mathf.Lerp(24f, 110f, cycle);
                m.ringRt.sizeDelta = new Vector2(ringSize, ringSize);
                Color c = m.ringImg.color;
                c.a = (1f - cycle) * 0.65f;
                m.ringImg.color = c;
            }

            if (m.group != null)
            {
                m.group.alpha = m.timer < 0.45f ? Mathf.Clamp01(m.timer / 0.45f) : 1f;
            }
        }
    }

    private void ClearAllPings()
    {
        for (int i = 0; i < _activePings.Count; i++)
        {
            if (_activePings[i].root != null)
                Destroy(_activePings[i].root);
        }
        _activePings.Clear();
    }
}
