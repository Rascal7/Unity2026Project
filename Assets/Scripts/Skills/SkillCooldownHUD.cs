using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 인게임 스킬 쿨타임을 화면에 실시간으로 표시하는 HUD 시스템.
/// - 런타임에 게임 씬 진입 시 자동으로 HUD Canvas를 초기화합니다.
/// - 로컬 플레이어(InputAuthority)의 스킬 슬롯과 쿨다운을 실시간으로 감지하여 바인딩합니다.
/// - 픽셀 아트 우드/골드 테마(LobbyUITheme)와 어우러지는 디자인을 제공합니다.
/// - 방사형(Radial 360) 쿨다운 게이지, 잔여 시간 숫자, 단축키 뱃지, 준비 완료 펄스 연출이 포함됩니다.
/// - 추천 무료 에셋 등의 외부 스프라이트를 인스펙터나 코드로 손쉽게 장착할 수 있습니다.
/// </summary>
public class SkillCooldownHUD : MonoBehaviour
{
    public static SkillCooldownHUD Instance { get; private set; }

    [Serializable]
    public class SkillIconOverride
    {
        public PlayerMovement.CharacterSkill skill;
        public Sprite iconSprite;
    }

    [Header("스킬별 커스텀 아이콘 (무료 에셋 등 연결용)")]
    [Tooltip("에셋을 다운로드받아 여기에 스프라이트를 등록하면 기본 아이콘 대신 표시됩니다.")]
    public List<SkillIconOverride> customIcons = new List<SkillIconOverride>();

    private Canvas _canvas;
    private CanvasScaler _scaler;
    private RectTransform _hudContainer;
    private PlayerMovement _localPlayer;

    private readonly List<SlotUI> _slotUIs = new List<SlotUI>();
    private Sprite _whiteFillSprite;
    private Sprite _micSprite;

    // 실시간 마이크 인디케이터 UI
    private RectTransform _micRoot;
    private Image _micGlowBorder;
    private Image _micFrameImage;
    private Image _micBevelImage;
    private Image _micIconImage;
    private Image _micLevelFill;
    private Text _micStatusText;
    private float _voiceFlashTimer;
    private string _voiceFlashText = "";

    // 토끼 파워업 공유 버프 상단 배너 UI
    private GameObject _powerUpBanner;
    private RectTransform _powerUpBannerRt;
    private Image _powerUpBannerBg;
    private Image _powerUpBarFill;
    private Text _powerUpBannerText;

    // 스테이지 진입 '스킬 등록 연출' 동안, 공개 예정 슬롯을 비어있는(잠김) 것처럼 표시한다.
    private int _revealSuppressSlot = -1;

    // 씬 필터링: 로비, 캐릭터 선택 등 메뉴 씬에서는 숨김
    private static readonly HashSet<string> MenuSceneNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Lobby", "TitleScreen", "CharacterSelect", "savefile"
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("SkillCooldownHUD");
        go.AddComponent<SkillCooldownHUD>();
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

        _whiteFillSprite = CreateSolidSprite();
        _micSprite = CreateMicPixelSprite();

        SceneManager.sceneLoaded += OnSceneLoaded;
        SkillKeybinds.OnChanged += OnKeybindsChanged;
        VoiceSkillInput.OnVoiceCommandRecognized += OnVoiceRecognized;
        EnsureCanvas();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SkillKeybinds.OnChanged -= OnKeybindsChanged;
        VoiceSkillInput.OnVoiceCommandRecognized -= OnVoiceRecognized;
        if (Instance == this) Instance = null;
    }

    private void OnVoiceRecognized(string keyword, int slot)
    {
        _voiceFlashText = $"{keyword}!";
        _voiceFlashTimer = 1.35f;

        if (slot >= 1 && slot <= _slotUIs.Count)
        {
            var s = _slotUIs[slot - 1];
            if (s != null) s.pulseTimer = 0.55f;
        }
    }

    // 단축키가 바뀌면 채워진 슬롯의 라벨도 즉시 갱신되도록 강제 재적용을 유도한다.
    // (UpdateSlot 은 lastSkill 이 바뀔 때만 라벨을 다시 쓰므로 None 으로 리셋해 재적용시킨다.)
    private void OnKeybindsChanged()
    {
        foreach (var s in _slotUIs)
            if (s != null) s.lastSkill = PlayerMovement.CharacterSkill.None;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _localPlayer = null;
        _revealSuppressSlot = -1;   // 연출 억제 상태 초기화
        EnsureCanvas();
    }

    // ---- 스테이지 진입 스킬 등록 연출(StageIntroUI 와 연동) ----

    // 공개 예정 슬롯을 비어있는(잠김)으로 표시 시작.
    public void SuppressSlotForReveal(int slot) => _revealSuppressSlot = slot;

    // 연출 종료: 억제 해제 + 해당 슬롯을 실제 스킬로 다시 적용하며 '받는' 팝 연출.
    public void FinishSlotReveal(int slot)
    {
        if (_revealSuppressSlot == slot) _revealSuppressSlot = -1;
        var s = FindSlot(slot);
        if (s != null)
        {
            s.lastSkill = PlayerMovement.CharacterSkill.None; // 다음 UpdateSlot 에서 스킬 비주얼 재적용
            s.pulseTimer = 0.5f;                              // 꽂히는 순간 pop
        }
    }

    // 슬롯 아이콘의 화면(월드) 중심 — 카드가 날아올 목표. (오버레이 캔버스라 position=화면 픽셀)
    public bool TryGetSlotIconWorldCenter(int slot, out Vector3 world)
    {
        world = default;
        var s = FindSlot(slot);
        if (s == null || s.iconImage == null || !s.root.activeSelf) return false;
        world = s.iconImage.rectTransform.position;
        return true;
    }

    private SlotUI FindSlot(int slot)
    {
        for (int i = 0; i < _slotUIs.Count; i++)
            if (_slotUIs[i] != null && _slotUIs[i].slotIndex == slot) return _slotUIs[i];
        return null;
    }

    private void EnsureCanvas()
    {
        if (_canvas != null) return;

        var canvasGo = new GameObject("SkillHUD_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);

        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.overrideSorting = true;
        _canvas.sortingOrder = 500; // 최상단 노출 보장

        _scaler = canvasGo.GetComponent<CanvasScaler>();
        _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        _scaler.referenceResolution = new Vector2(1920, 1080);
        _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        _scaler.matchWidthOrHeight = 0.5f;

        // HUD 컨테이너 (화면 우측 하단 배치)
        var contGo = new GameObject("HUD_Container", typeof(RectTransform));
        contGo.transform.SetParent(canvasGo.transform, false);
        _hudContainer = contGo.GetComponent<RectTransform>();

        // 우측 하단 앵커 (여백 X: -40, Y: 35)
        _hudContainer.anchorMin = new Vector2(1f, 0f);
        _hudContainer.anchorMax = new Vector2(1f, 0f);
        _hudContainer.pivot = new Vector2(1f, 0f);
        _hudContainer.anchoredPosition = new Vector2(-40f, 35f);
        _hudContainer.sizeDelta = new Vector2(380f, 140f);

        _slotUIs.Clear();
        _layoutSlotCount = -1;
        // 슬롯 1, 2, 3 생성 (스테이지별로 보이는 개수는 Update 에서 조정)
        for (int i = 1; i <= StageSkillRules.MaxSlots; i++)
        {
            var slot = CreateSlotUI(i);
            _slotUIs.Add(slot);
        }

        CreateMicIndicatorUI();
        CreatePowerUpBannerUI();
    }

    private const float SlotSize = 96f; // 기존 78f에서 약 23% 확대
    private const float SlotSpacing = 18f;
    private int _layoutSlotCount = -1;

    // 보이는 슬롯 수 기준 우측 정렬 (슬롯 1이 왼쪽, 마지막 슬롯이 가장 오른쪽)
    private static float SlotPosX(int slotIndex, int visibleCount)
    {
        return -((visibleCount - slotIndex) * (SlotSize + SlotSpacing));
    }

    // 스테이지에서 열린 슬롯 수가 바뀌면 빈틈 없이 다시 우측 정렬한다.
    private void LayoutSlots(int visibleCount)
    {
        if (_layoutSlotCount == visibleCount) return;
        _layoutSlotCount = visibleCount;
        for (int i = 0; i < _slotUIs.Count; i++)
        {
            var s = _slotUIs[i];
            s.rootTransform.anchoredPosition = new Vector2(SlotPosX(s.slotIndex, visibleCount), 0f);
        }

        if (_micRoot != null)
        {
            float micX = visibleCount > 0
                ? SlotPosX(1, visibleCount) - (SlotSize + SlotSpacing)
                : 0f;
            _micRoot.anchoredPosition = new Vector2(micX, 0f);
        }

        if (_powerUpBannerRt != null)
        {
            float totalWidth = Mathf.Max(1, visibleCount) * SlotSize + Mathf.Max(0, visibleCount - 1) * SlotSpacing;
            _powerUpBannerRt.sizeDelta = new Vector2(Mathf.Max(260f, totalWidth), 36f);
            _powerUpBannerRt.anchoredPosition = new Vector2(0f, SlotSize + 40f);
        }
    }

    private static string KeyName(int slotIndex)
    {
        // ESC 메뉴에서 재설정한 단축키를 반영 (기본 Shift/Ctrl/Alt).
        return SkillKeybinds.GetLabel(slotIndex);
    }

    private SlotUI CreateSlotUI(int slotIndex)
    {
        float slotSize = SlotSize;
        float posX = SlotPosX(slotIndex, StageSkillRules.MaxSlots);

        var rootGo = new GameObject($"Slot_{slotIndex}", typeof(RectTransform));
        rootGo.transform.SetParent(_hudContainer, false);
        var rootRt = rootGo.GetComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(1f, 0f);
        rootRt.anchorMax = new Vector2(1f, 0f);
        rootRt.pivot = new Vector2(1f, 0f);
        rootRt.anchoredPosition = new Vector2(posX, 0f);
        rootRt.sizeDelta = new Vector2(slotSize, slotSize + 32f);

        // --- 상단 단축키 뱃지 (예: [Shift], [Ctrl]) ---
        var keyBadgeGo = new GameObject("KeyBadge", typeof(RectTransform), typeof(Image));
        keyBadgeGo.transform.SetParent(rootGo.transform, false);
        var keyBadgeRt = keyBadgeGo.GetComponent<RectTransform>();
        keyBadgeRt.anchorMin = new Vector2(0.5f, 1f);
        keyBadgeRt.anchorMax = new Vector2(0.5f, 1f);
        keyBadgeRt.pivot = new Vector2(0.5f, 1f);
        keyBadgeRt.anchoredPosition = new Vector2(0f, 0f);
        keyBadgeRt.sizeDelta = new Vector2(slotSize, 28f);

        var keyBadgeBg = keyBadgeGo.GetComponent<Image>();
        keyBadgeBg.color = LobbyUITheme.WoodDark;

        var keyTextGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
        keyTextGo.transform.SetParent(keyBadgeGo.transform, false);
        var keyTextRt = keyTextGo.GetComponent<RectTransform>();
        keyTextRt.anchorMin = Vector2.zero;
        keyTextRt.anchorMax = Vector2.one;
        keyTextRt.offsetMin = Vector2.zero;
        keyTextRt.offsetMax = Vector2.zero;

        var keyText = keyTextGo.GetComponent<Text>();
        keyText.font = LobbyUITheme.PixelFont;
        keyText.fontSize = 15;
        keyText.alignment = TextAnchor.MiddleCenter;
        keyText.color = LobbyUITheme.Gold;
        keyText.text = KeyName(slotIndex);

        // --- 슬롯 메인 프레임 (우드 & 골드) ---
        var frameGo = new GameObject("Frame", typeof(RectTransform), typeof(Image));
        frameGo.transform.SetParent(rootGo.transform, false);
        var frameRt = frameGo.GetComponent<RectTransform>();
        frameRt.anchorMin = new Vector2(0f, 0f);
        frameRt.anchorMax = new Vector2(1f, 0f);
        frameRt.pivot = new Vector2(0.5f, 0f);
        frameRt.anchoredPosition = new Vector2(0f, 0f);
        frameRt.sizeDelta = new Vector2(0f, slotSize);

        var frameImg = frameGo.GetComponent<Image>();
        frameImg.color = LobbyUITheme.WoodDark;

        // 베벨 테두리
        var bevelGo = new GameObject("Bevel", typeof(RectTransform), typeof(Image));
        bevelGo.transform.SetParent(frameGo.transform, false);
        var bevelRt = bevelGo.GetComponent<RectTransform>();
        bevelRt.anchorMin = Vector2.zero;
        bevelRt.anchorMax = Vector2.one;
        bevelRt.offsetMin = new Vector2(3f, 3f);
        bevelRt.offsetMax = new Vector2(-3f, -3f);

        var bevelImg = bevelGo.GetComponent<Image>();
        bevelImg.color = LobbyUITheme.WoodLight;

        // 내부 배경 (어두운 인셋 필드)
        var innerGo = new GameObject("InnerBg", typeof(RectTransform), typeof(Image));
        innerGo.transform.SetParent(bevelGo.transform, false);
        var innerRt = innerGo.GetComponent<RectTransform>();
        innerRt.anchorMin = Vector2.zero;
        innerRt.anchorMax = Vector2.one;
        innerRt.offsetMin = new Vector2(3f, 3f);
        innerRt.offsetMax = new Vector2(-3f, -3f);

        var innerImg = innerGo.GetComponent<Image>();
        innerImg.color = LobbyUITheme.FieldBg;

        // --- 스킬 아이콘 (스프라이트 또는 텍스트) ---
        var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGo.transform.SetParent(innerGo.transform, false);
        var iconRt = iconGo.GetComponent<RectTransform>();
        iconRt.anchorMin = Vector2.zero;
        iconRt.anchorMax = Vector2.one;
        iconRt.offsetMin = new Vector2(4f, 4f);
        iconRt.offsetMax = new Vector2(-4f, -4f);

        var iconImg = iconGo.GetComponent<Image>();
        iconImg.color = Color.white;
        iconImg.preserveAspect = true;

        // 아이콘 기본 텍스트 (심볼/스킬명 표시용)
        var iconTextGo = new GameObject("IconLabel", typeof(RectTransform), typeof(Text));
        iconTextGo.transform.SetParent(innerGo.transform, false);
        var iconTextRt = iconTextGo.GetComponent<RectTransform>();
        iconTextRt.anchorMin = Vector2.zero;
        iconTextRt.anchorMax = Vector2.one;
        iconTextRt.offsetMin = Vector2.zero;
        iconTextRt.offsetMax = Vector2.zero;

        var iconText = iconTextGo.GetComponent<Text>();
        iconText.font = LobbyUITheme.PixelFont;
        iconText.fontSize = 26;
        iconText.alignment = TextAnchor.MiddleCenter;
        iconText.color = LobbyUITheme.Parch;

        // --- 쿨다운 방사형 오버레이 (Radial 360 Fill) ---
        var cdOverlayGo = new GameObject("CooldownOverlay", typeof(RectTransform), typeof(Image));
        cdOverlayGo.transform.SetParent(innerGo.transform, false);
        var cdOverlayRt = cdOverlayGo.GetComponent<RectTransform>();
        cdOverlayRt.anchorMin = Vector2.zero;
        cdOverlayRt.anchorMax = Vector2.one;
        cdOverlayRt.offsetMin = Vector2.zero;
        cdOverlayRt.offsetMax = Vector2.zero;

        var cdOverlayImg = cdOverlayGo.GetComponent<Image>();
        cdOverlayImg.sprite = _whiteFillSprite;
        cdOverlayImg.type = Image.Type.Filled;
        cdOverlayImg.fillMethod = Image.FillMethod.Radial360;
        cdOverlayImg.fillOrigin = (int)Image.Origin360.Top;
        cdOverlayImg.fillClockwise = false; // 반시계 방향 채움 (쿨타임이 줄어들며 시계방향으로 열림)
        cdOverlayImg.color = new Color(0.05f, 0.03f, 0.02f, 0.78f);
        cdOverlayImg.raycastTarget = false;

        // --- 쿨다운 숫자 텍스트 ---
        var cdTextGo = new GameObject("CooldownText", typeof(RectTransform), typeof(Text));
        cdTextGo.transform.SetParent(innerGo.transform, false);
        var cdTextRt = cdTextGo.GetComponent<RectTransform>();
        cdTextRt.anchorMin = Vector2.zero;
        cdTextRt.anchorMax = Vector2.one;
        cdTextRt.offsetMin = Vector2.zero;
        cdTextRt.offsetMax = Vector2.zero;

        var cdText = cdTextGo.GetComponent<Text>();
        cdText.font = LobbyUITheme.PixelFont;
        cdText.fontSize = 30;
        cdText.alignment = TextAnchor.MiddleCenter;
        cdText.color = LobbyUITheme.Gold;
        var cdOutline = cdTextGo.AddComponent<Outline>();
        cdOutline.effectColor = LobbyUITheme.GoldDeep;
        cdOutline.effectDistance = new Vector2(2f, -2f);

        // --- 준비 완료 / 파워업 외곽 광채 테두리 (Gold Halo) ---
        var readyBorderGo = new GameObject("ReadyHighlight", typeof(RectTransform), typeof(Image));
        readyBorderGo.transform.SetParent(frameGo.transform, false);
        readyBorderGo.transform.SetAsFirstSibling(); // 아이콘을 가리지 않도록 프레임 가장 뒤(외곽 테두리)에 배치
        var readyBorderRt = readyBorderGo.GetComponent<RectTransform>();
        readyBorderRt.anchorMin = Vector2.zero;
        readyBorderRt.anchorMax = Vector2.one;
        readyBorderRt.offsetMin = new Vector2(-6f, -6f);
        readyBorderRt.offsetMax = new Vector2(6f, 6f);

        var readyBorderImg = readyBorderGo.GetComponent<Image>();
        readyBorderImg.color = new Color(LobbyUITheme.Gold.r, LobbyUITheme.Gold.g, LobbyUITheme.Gold.b, 0f);
        readyBorderImg.raycastTarget = false;

        return new SlotUI
        {
            slotIndex = slotIndex,
            root = rootGo,
            rootTransform = rootRt,
            keyText = keyText,
            frameImage = frameImg,
            bevelImage = bevelImg,
            iconImage = iconImg,
            iconLabel = iconText,
            cooldownOverlay = cdOverlayImg,
            cooldownText = cdText,
            readyHighlight = readyBorderImg
        };
    }

    private void Update()
    {
        if (_canvas == null || _hudContainer == null)
        {
            EnsureCanvas();
        }

        string currentScene = SceneManager.GetActiveScene().name;
        bool isMenuScene = MenuSceneNames.Contains(currentScene);

        // 1. 로컬 플레이어 검색 및 바인딩
        if (_localPlayer == null || _localPlayer.gameObject == null || (_localPlayer.Object != null && !_localPlayer.Object.IsValid))
        {
            FindLocalPlayer();
        }

        // 메뉴 씬이면서 플레이어가 아직 없으면 HUD 숨김
        if (isMenuScene && _localPlayer == null)
        {
            if (_hudContainer != null && _hudContainer.gameObject.activeSelf)
                _hudContainer.gameObject.SetActive(false);
            return;
        }

        if (_localPlayer == null)
        {
            // 플레이어가 아직 없으면 슬롯 숨김
            for (int i = 0; i < _slotUIs.Count; i++)
                _slotUIs[i].root.SetActive(false);
            if (_micRoot != null && _micRoot.gameObject.activeSelf)
                _micRoot.gameObject.SetActive(false);
            if (_powerUpBanner != null && _powerUpBanner.activeSelf)
                _powerUpBanner.SetActive(false);
            return;
        }

        // 2. 스테이지별 사용 가능 슬롯 수 (튜토리얼 0 / 1스테이지 1 / 2스테이지 2 / 3스테이지 3)
        // 튜토리얼(maxSlots == 0)에서도 실시간 마이크 인디케이터는 표시하여 음성 감도를 확인할 수 있게 함
        int maxSlots = StageSkillRules.MaxSkillSlots(currentScene);
        if (_hudContainer != null && !_hudContainer.gameObject.activeSelf)
            _hudContainer.gameObject.SetActive(true);

        LayoutSlots(maxSlots);
        UpdateMicIndicator();
        UpdatePowerUpBanner(maxSlots);

        // 3. 각 슬롯 갱신 (열리지 않은 슬롯은 숨김)
        for (int i = 0; i < _slotUIs.Count; i++)
        {
            var slot = _slotUIs[i];
            if (slot.slotIndex > maxSlots)
            {
                if (slot.root.activeSelf) slot.root.SetActive(false);
                continue;
            }
            UpdateSlot(slot);
        }
    }

    private void FindLocalPlayer()
    {
        var players = FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None);
        if (players == null || players.Length == 0)
        {
            _localPlayer = null;
            return;
        }

        PlayerMovement candidate = null;

        foreach (var p in players)
        {
            if (p == null) continue;

            // 가짜 플레이어나 Spawner 제외
            if (p.GetComponent<Rigidbody2D>() == null) continue;
            if (p.gameObject.name.Contains("Spawner")) continue;

            // 스킬이 전혀 없는 더미 오브젝트 제외
            if (p.skillCh1 == PlayerMovement.CharacterSkill.None && p.skillCh2 == PlayerMovement.CharacterSkill.None)
                continue;

            // 1순위: Fusion InputAuthority 소유
            if (p.Object != null && p.Object.IsValid)
            {
                if (p.Object.HasInputAuthority)
                {
                    candidate = p;
                    break;
                }
                if (p.Runner != null && p.Object.InputAuthority == p.Runner.LocalPlayer)
                {
                    candidate = p;
                    break;
                }
                if (p.Object.HasStateAuthority && candidate == null)
                {
                    candidate = p;
                }
            }
            else if (candidate == null)
            {
                // 오프라인 / 단독 씬 테스트
                candidate = p;
            }
        }

        if (candidate != null && candidate != _localPlayer)
        {
            _localPlayer = candidate;
            Debug.Log($"<color=lime>[SkillHUD]</color> 로컬 플레이어 감지 성공: {_localPlayer.gameObject.name} (슬롯1: {_localPlayer.skillCh1}, 슬롯2: {_localPlayer.skillCh2})");
        }
    }

    private void UpdateSlot(SlotUI slot)
    {
        // 등록 연출 동안 공개 예정 슬롯은 아직 '비어있는(잠김)'처럼 보여준다.
        if (slot.slotIndex == _revealSuppressSlot)
        {
            if (!slot.root.activeSelf) slot.root.SetActive(true);
            ApplyEmptySlotVisuals(slot);
            return;
        }

        var status = _localPlayer.GetSkillSlotStatus(slot.slotIndex);

        if (status.Skill == PlayerMovement.CharacterSkill.None)
        {
            // 열린 슬롯인데 캐릭터에 스킬이 배정되지 않은 경우: 빈칸/잠김 슬롯으로 표시
            if (!slot.root.activeSelf) slot.root.SetActive(true);
            ApplyEmptySlotVisuals(slot);
            return;
        }

        if (!slot.root.activeSelf)
            slot.root.SetActive(true);

        // 스킬 메타데이터 (이름, 아이콘 등) 갱신
        if (slot.lastSkill != status.Skill)
        {
            slot.lastSkill = status.Skill;
            ApplySkillVisuals(slot, status.Skill);
        }

        // 쿨다운 수치 적용
        bool wasOnCooldown = !slot.wasReady;
        bool isReadyNow = status.IsReady;

        if (isReadyNow)
        {
            slot.cooldownOverlay.fillAmount = 0f;
            slot.cooldownText.text = "";
            slot.frameImage.color = LobbyUITheme.WoodDark;
            slot.bevelImage.color = LobbyUITheme.WoodLight;

            // 쿨다운 끝나서 사용 가능해진 순간 펄스 연출
            if (wasOnCooldown)
            {
                slot.pulseTimer = 0.45f;
            }
        }
        else
        {
            slot.cooldownOverlay.fillAmount = status.NormalizedProgress;

            if (status.RemainingSeconds >= 10f)
                slot.cooldownText.text = Mathf.CeilToInt(status.RemainingSeconds).ToString();
            else if (status.RemainingSeconds > 0.05f)
                slot.cooldownText.text = status.RemainingSeconds.ToString("0.0");
            else
                slot.cooldownText.text = "";

            slot.frameImage.color = new Color(0.22f, 0.14f, 0.08f, 1f);
            slot.bevelImage.color = new Color(0.35f, 0.24f, 0.15f, 1f);
        }

        // 펄스 연출 & 토끼 파워업 공유 버프 황금 글로우 처리
        if (slot.pulseTimer > 0f)
        {
            slot.pulseTimer -= Time.deltaTime;
            float t = Mathf.Clamp01(slot.pulseTimer / 0.45f);
            slot.readyHighlight.color = new Color(LobbyUITheme.Gold.r, LobbyUITheme.Gold.g, LobbyUITheme.Gold.b, t * 0.95f);
            float scale = 1f + (Mathf.Sin(t * Mathf.PI) * 0.14f);
            slot.rootTransform.localScale = new Vector3(scale, scale, 1f);
        }
        else if (_localPlayer.IsPoweredUp)
        {
            // 파워업 공유 버프 활성 상태: 열린 모든 스킬 슬롯에 황금색 글로우 & 맥박 연출
            float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8.5f);
            slot.readyHighlight.color = new Color(1f, 0.84f, 0.18f, 0.55f + 0.40f * wave);
            slot.frameImage.color = Color.Lerp(slot.frameImage.color, new Color(0.95f, 0.68f, 0.12f, 1f), 0.55f + 0.35f * wave);
            slot.bevelImage.color = Color.Lerp(slot.bevelImage.color, new Color(1f, 0.92f, 0.45f, 1f), 0.65f + 0.30f * wave);
            float scale = 1f + 0.035f * wave;
            slot.rootTransform.localScale = new Vector3(scale, scale, 1f);
        }
        else
        {
            slot.readyHighlight.color = new Color(LobbyUITheme.Gold.r, LobbyUITheme.Gold.g, LobbyUITheme.Gold.b, 0f);
            slot.rootTransform.localScale = Vector3.one;
        }

        slot.wasReady = isReadyNow;
    }

    private void ApplySkillVisuals(SlotUI slot, PlayerMovement.CharacterSkill skill)
    {
        string keyBadge = KeyName(slot.slotIndex);
        string skillName = "";
        string symbol = "";

        switch (skill)
        {
            case PlayerMovement.CharacterSkill.FoxDash:
                skillName = "대시";
                symbol = ">>";
                break;
            case PlayerMovement.CharacterSkill.FoxSniff:
                skillName = "킁킁";
                symbol = "!?";
                break;
            case PlayerMovement.CharacterSkill.FoxShadow:
                skillName = "그림자";
                symbol = "◈";
                break;
            case PlayerMovement.CharacterSkill.RabbitCarrot:
                skillName = "당근";
                symbol = "▲";
                break;
            case PlayerMovement.CharacterSkill.RabbitJump:
                skillName = "도약";
                symbol = "⇑";
                break;
            case PlayerMovement.CharacterSkill.RabbitStomp:
                skillName = "스톰프";
                symbol = "⇓";
                break;
            case PlayerMovement.CharacterSkill.FoxSwap:
                skillName = "체인지";
                symbol = "<>";
                break;
            case PlayerMovement.CharacterSkill.RabbitPowerUp:
                skillName = "파워업";
                symbol = "UP";
                break;
        }

        slot.keyText.text = $"{keyBadge}\n{skillName}";
        slot.keyText.color = LobbyUITheme.Gold; // 빈 슬롯 표시에서 바뀐 색 복구
        slot.iconLabel.color = LobbyUITheme.Parch;

        // 커스텀 등록 스프라이트 검사
        Sprite custom = GetCustomIcon(skill);
        if (custom != null)
        {
            slot.iconImage.gameObject.SetActive(true);
            slot.iconImage.sprite = custom;
            // 컬러 도트 아이콘(토끼 당근/도약/파워업, 여우 대시/킁킁/체인지)은 고유 컬러 유지, 그 외 흰색 벡터 아이콘은 골드 틴트 적용
            if (skill == PlayerMovement.CharacterSkill.RabbitCarrot ||
                skill == PlayerMovement.CharacterSkill.RabbitJump ||
                skill == PlayerMovement.CharacterSkill.RabbitPowerUp ||
                skill == PlayerMovement.CharacterSkill.FoxDash ||
                skill == PlayerMovement.CharacterSkill.FoxSniff ||
                skill == PlayerMovement.CharacterSkill.FoxSwap)
            {
                slot.iconImage.color = Color.white;
            }
            else
            {
                slot.iconImage.color = LobbyUITheme.Gold;
            }
            slot.iconLabel.text = "";
        }
        else
        {
            // 기본 심볼 텍스트 모드
            slot.iconImage.gameObject.SetActive(false);
            slot.iconLabel.text = symbol;
        }
    }

    private void ApplyEmptySlotVisuals(SlotUI slot)
    {
        slot.lastSkill = PlayerMovement.CharacterSkill.None;
        slot.keyText.text = $"{KeyName(slot.slotIndex)}\n[준비중]";
        slot.keyText.color = LobbyUITheme.Hint;

        slot.frameImage.color = new Color(0.18f, 0.14f, 0.11f, 0.9f);
        slot.bevelImage.color = new Color(0.28f, 0.22f, 0.18f, 0.9f);

        Sprite lockIcon = Resources.Load<Sprite>("Skills/locked");
        if (lockIcon == null)
        {
            var all = Resources.LoadAll<Sprite>("Skills/locked");
            if (all != null && all.Length > 0) lockIcon = all[0];
        }

        if (lockIcon != null)
        {
            slot.iconImage.gameObject.SetActive(true);
            slot.iconImage.sprite = lockIcon;
            slot.iconImage.color = new Color(0.6f, 0.55f, 0.48f, 0.65f);
            slot.iconLabel.text = "";
        }
        else
        {
            slot.iconImage.gameObject.SetActive(false);
            slot.iconLabel.text = "+";
            slot.iconLabel.color = new Color(0.6f, 0.55f, 0.48f, 0.65f);
        }

        slot.cooldownOverlay.fillAmount = 0f;
        slot.cooldownText.text = "";
        slot.readyHighlight.color = Color.clear;
        slot.rootTransform.localScale = Vector3.one;
    }

    private readonly Dictionary<PlayerMovement.CharacterSkill, Sprite> _cachedResourceIcons = new Dictionary<PlayerMovement.CharacterSkill, Sprite>();

    public Sprite GetCustomIcon(PlayerMovement.CharacterSkill skill)
    {
        for (int i = 0; i < customIcons.Count; i++)
        {
            if (customIcons[i].skill == skill && customIcons[i].iconSprite != null)
                return customIcons[i].iconSprite;
        }

        // Resources/Skills/{skill} 에서 자동 로드
        if (_cachedResourceIcons.TryGetValue(skill, out Sprite cached))
            return cached;

        Sprite loaded = Resources.Load<Sprite>($"Skills/{skill}");
        if (loaded == null)
        {
            var all = Resources.LoadAll<Sprite>($"Skills/{skill}");
            if (all != null && all.Length > 0)
                loaded = all[0];
        }

        if (loaded != null)
        {
            _cachedResourceIcons[skill] = loaded;
            return loaded;
        }

        return null;
    }

    /// <summary>
    /// 런타임 또는 에디터에서 특정 스킬 아이콘을 등록/교체할 때 호출
    /// </summary>
    public void SetSkillIcon(PlayerMovement.CharacterSkill skill, Sprite icon)
    {
        for (int i = 0; i < customIcons.Count; i++)
        {
            if (customIcons[i].skill == skill)
            {
                customIcons[i].iconSprite = icon;
                return;
            }
        }
        customIcons.Add(new SkillIconOverride { skill = skill, iconSprite = icon });
    }

    private void CreateMicIndicatorUI()
    {
        if (_micRoot != null) return;

        float slotSize = SlotSize;
        var rootGo = new GameObject("MicIndicator", typeof(RectTransform));
        rootGo.transform.SetParent(_hudContainer, false);
        _micRoot = rootGo.GetComponent<RectTransform>();
        _micRoot.anchorMin = new Vector2(1f, 0f);
        _micRoot.anchorMax = new Vector2(1f, 0f);
        _micRoot.pivot = new Vector2(1f, 0f);
        _micRoot.sizeDelta = new Vector2(slotSize, slotSize + 32f);

        // 상단 뱃지 ("VOICE")
        var badgeGo = new GameObject("VoiceBadge", typeof(RectTransform), typeof(Image));
        badgeGo.transform.SetParent(rootGo.transform, false);
        var badgeRt = badgeGo.GetComponent<RectTransform>();
        badgeRt.anchorMin = new Vector2(0.5f, 1f);
        badgeRt.anchorMax = new Vector2(0.5f, 1f);
        badgeRt.pivot = new Vector2(0.5f, 1f);
        badgeRt.anchoredPosition = Vector2.zero;
        badgeRt.sizeDelta = new Vector2(slotSize, 28f);
        badgeGo.GetComponent<Image>().color = LobbyUITheme.WoodDark;

        var badgeTextGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
        badgeTextGo.transform.SetParent(badgeGo.transform, false);
        var badgeTextRt = badgeTextGo.GetComponent<RectTransform>();
        badgeTextRt.anchorMin = Vector2.zero;
        badgeTextRt.anchorMax = Vector2.one;
        badgeTextRt.offsetMin = badgeTextRt.offsetMax = Vector2.zero;
        var badgeText = badgeTextGo.GetComponent<Text>();
        badgeText.font = LobbyUITheme.PixelFont;
        badgeText.fontSize = 14;
        badgeText.alignment = TextAnchor.MiddleCenter;
        badgeText.color = LobbyUITheme.Gold;
        badgeText.text = "VOICE\n음성인식";

        // 메인 프레임
        var frameGo = new GameObject("Frame", typeof(RectTransform), typeof(Image));
        frameGo.transform.SetParent(rootGo.transform, false);
        var frameRt = frameGo.GetComponent<RectTransform>();
        frameRt.anchorMin = new Vector2(0f, 0f);
        frameRt.anchorMax = new Vector2(1f, 0f);
        frameRt.pivot = new Vector2(0.5f, 0f);
        frameRt.anchoredPosition = Vector2.zero;
        frameRt.sizeDelta = new Vector2(0f, slotSize);
        _micFrameImage = frameGo.GetComponent<Image>();
        _micFrameImage.color = LobbyUITheme.WoodDark;

        // 발화 시 외곽 광채 테두리
        var glowGo = new GameObject("SpeakGlow", typeof(RectTransform), typeof(Image));
        glowGo.transform.SetParent(frameGo.transform, false);
        glowGo.transform.SetAsFirstSibling();
        var glowRt = glowGo.GetComponent<RectTransform>();
        glowRt.anchorMin = Vector2.zero;
        glowRt.anchorMax = Vector2.one;
        glowRt.offsetMin = new Vector2(-6f, -6f);
        glowRt.offsetMax = new Vector2(6f, 6f);
        _micGlowBorder = glowGo.GetComponent<Image>();
        _micGlowBorder.color = Color.clear;
        _micGlowBorder.raycastTarget = false;

        // 베벨
        var bevelGo = new GameObject("Bevel", typeof(RectTransform), typeof(Image));
        bevelGo.transform.SetParent(frameGo.transform, false);
        var bevelRt = bevelGo.GetComponent<RectTransform>();
        bevelRt.anchorMin = Vector2.zero;
        bevelRt.anchorMax = Vector2.one;
        bevelRt.offsetMin = new Vector2(3f, 3f);
        bevelRt.offsetMax = new Vector2(-3f, -3f);
        _micBevelImage = bevelGo.GetComponent<Image>();
        _micBevelImage.color = LobbyUITheme.WoodLight;

        // 내부 배경
        var innerGo = new GameObject("InnerBg", typeof(RectTransform), typeof(Image));
        innerGo.transform.SetParent(bevelGo.transform, false);
        var innerRt = innerGo.GetComponent<RectTransform>();
        innerRt.anchorMin = Vector2.zero;
        innerRt.anchorMax = Vector2.one;
        innerRt.offsetMin = new Vector2(3f, 3f);
        innerRt.offsetMax = new Vector2(-3f, -3f);
        innerGo.GetComponent<Image>().color = LobbyUITheme.FieldBg;

        // 실시간 볼륨 레벨 게이지 (아래에서 위로 차오르는 반투명 바)
        var levelGo = new GameObject("LevelBar", typeof(RectTransform), typeof(Image));
        levelGo.transform.SetParent(innerGo.transform, false);
        var levelRt = levelGo.GetComponent<RectTransform>();
        levelRt.anchorMin = Vector2.zero;
        levelRt.anchorMax = Vector2.one;
        levelRt.offsetMin = Vector2.zero;
        levelRt.offsetMax = Vector2.zero;
        _micLevelFill = levelGo.GetComponent<Image>();
        _micLevelFill.sprite = _whiteFillSprite;
        _micLevelFill.type = Image.Type.Filled;
        _micLevelFill.fillMethod = Image.FillMethod.Vertical;
        _micLevelFill.fillOrigin = (int)Image.OriginVertical.Bottom;
        _micLevelFill.fillAmount = 0f;
        _micLevelFill.color = new Color(0.28f, 0.85f, 0.45f, 0.35f);
        _micLevelFill.raycastTarget = false;

        // 마이크 도트 아이콘
        var iconGo = new GameObject("MicIcon", typeof(RectTransform), typeof(Image));
        iconGo.transform.SetParent(innerGo.transform, false);
        var iconRt = iconGo.GetComponent<RectTransform>();
        iconRt.anchorMin = new Vector2(0.5f, 0.5f);
        iconRt.anchorMax = new Vector2(0.5f, 0.5f);
        iconRt.pivot = new Vector2(0.5f, 0.5f);
        iconRt.anchoredPosition = new Vector2(0f, 10f);
        iconRt.sizeDelta = new Vector2(46f, 46f);
        _micIconImage = iconGo.GetComponent<Image>();
        _micIconImage.sprite = _micSprite;
        _micIconImage.preserveAspect = true;
        _micIconImage.color = LobbyUITheme.Parch;
        _micIconImage.raycastTarget = false;

        // 하단 상태/인식 키워드 라벨
        var statusGo = new GameObject("StatusText", typeof(RectTransform), typeof(Text), typeof(Outline));
        statusGo.transform.SetParent(innerGo.transform, false);
        var statusRt = statusGo.GetComponent<RectTransform>();
        statusRt.anchorMin = new Vector2(0f, 0f);
        statusRt.anchorMax = new Vector2(1f, 0f);
        statusRt.pivot = new Vector2(0.5f, 0f);
        statusRt.anchoredPosition = new Vector2(0f, 4f);
        statusRt.sizeDelta = new Vector2(0f, 24f);
        _micStatusText = statusGo.GetComponent<Text>();
        _micStatusText.font = LobbyUITheme.PlainFont;
        _micStatusText.fontSize = 15;
        _micStatusText.fontStyle = FontStyle.Bold;
        _micStatusText.alignment = TextAnchor.MiddleCenter;
        _micStatusText.horizontalOverflow = HorizontalWrapMode.Overflow;
        _micStatusText.color = LobbyUITheme.Parch;
        _micStatusText.raycastTarget = false;
        var stOutline = statusGo.GetComponent<Outline>();
        stOutline.effectColor = LobbyUITheme.GoldDeep;
        stOutline.effectDistance = new Vector2(1.5f, -1.5f);
    }

    private void CreatePowerUpBannerUI()
    {
        if (_powerUpBanner != null) return;

        _powerUpBanner = new GameObject("PowerUpBanner", typeof(RectTransform), typeof(Image), typeof(Outline));
        _powerUpBanner.transform.SetParent(_hudContainer, false);
        _powerUpBannerRt = _powerUpBanner.GetComponent<RectTransform>();
        _powerUpBannerRt.anchorMin = new Vector2(1f, 0f);
        _powerUpBannerRt.anchorMax = new Vector2(1f, 0f);
        _powerUpBannerRt.pivot = new Vector2(1f, 0f);
        _powerUpBannerRt.anchoredPosition = new Vector2(0f, SlotSize + 40f);
        _powerUpBannerRt.sizeDelta = new Vector2(320f, 36f);

        _powerUpBannerBg = _powerUpBanner.GetComponent<Image>();
        _powerUpBannerBg.color = new Color(0.24f, 0.14f, 0.04f, 0.95f);
        _powerUpBannerBg.raycastTarget = false;

        var bannerOutline = _powerUpBanner.GetComponent<Outline>();
        bannerOutline.effectColor = new Color(1f, 0.84f, 0.2f, 1f);
        bannerOutline.effectDistance = new Vector2(2f, -2f);

        // 하단 잔여 시간 게이지 바
        var barGo = new GameObject("TimeBar", typeof(RectTransform), typeof(Image));
        barGo.transform.SetParent(_powerUpBanner.transform, false);
        var barRt = barGo.GetComponent<RectTransform>();
        barRt.anchorMin = new Vector2(0f, 0f);
        barRt.anchorMax = new Vector2(1f, 0f);
        barRt.pivot = new Vector2(0.5f, 0f);
        barRt.anchoredPosition = Vector2.zero;
        barRt.sizeDelta = new Vector2(0f, 5f);
        _powerUpBarFill = barGo.GetComponent<Image>();
        _powerUpBarFill.sprite = _whiteFillSprite;
        _powerUpBarFill.type = Image.Type.Filled;
        _powerUpBarFill.fillMethod = Image.FillMethod.Horizontal;
        _powerUpBarFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        _powerUpBarFill.color = new Color(1f, 0.86f, 0.22f, 1f);
        _powerUpBarFill.raycastTarget = false;

        var txtGo = new GameObject("BannerText", typeof(RectTransform), typeof(Text), typeof(Outline));
        txtGo.transform.SetParent(_powerUpBanner.transform, false);
        var txtRt = txtGo.GetComponent<RectTransform>();
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = new Vector2(6f, 4f);
        txtRt.offsetMax = new Vector2(-6f, -2f);
        _powerUpBannerText = txtGo.GetComponent<Text>();
        _powerUpBannerText.font = LobbyUITheme.PlainFont;
        _powerUpBannerText.fontSize = 16;
        _powerUpBannerText.fontStyle = FontStyle.Bold;
        _powerUpBannerText.alignment = TextAnchor.MiddleCenter;
        _powerUpBannerText.horizontalOverflow = HorizontalWrapMode.Overflow;
        _powerUpBannerText.color = new Color(1f, 0.92f, 0.42f, 1f);
        _powerUpBannerText.raycastTarget = false;
        var txtOutline = txtGo.GetComponent<Outline>();
        txtOutline.effectColor = LobbyUITheme.GoldDeep;
        txtOutline.effectDistance = new Vector2(1.5f, -1.5f);

        _powerUpBanner.SetActive(false);
    }

    private void UpdateMicIndicator()
    {
        if (_micRoot == null) return;
        if (!_micRoot.gameObject.activeSelf) _micRoot.gameObject.SetActive(true);

        bool speaking = VoiceSkillInput.IsSpeaking;
        bool listening = VoiceSkillInput.IsListening;
        bool calibrating = VoiceSkillInput.IsCalibrating;
        float level = VoiceSkillInput.CurrentLevel;

        if (_micLevelFill != null)
        {
            _micLevelFill.fillAmount = speaking ? Mathf.Max(0.18f, level) : level * 0.4f;
            _micLevelFill.color = speaking
                ? new Color(0.28f, 0.92f, 0.48f, 0.45f)
                : new Color(0.95f, 0.80f, 0.32f, 0.20f);
        }

        if (_voiceFlashTimer > 0f)
        {
            _voiceFlashTimer -= Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_voiceFlashTimer / 1.35f);
            float pop = 1f + Mathf.Sin(t * Mathf.PI) * 0.10f;
            _micRoot.localScale = new Vector3(pop, pop, 1f);
            _micGlowBorder.color = new Color(1f, 0.86f, 0.22f, 0.85f);
            _micFrameImage.color = new Color(0.55f, 0.36f, 0.10f, 1f);
            _micBevelImage.color = LobbyUITheme.Gold;
            _micIconImage.color = LobbyUITheme.Gold;
            _micStatusText.text = $"✦ {_voiceFlashText}";
            _micStatusText.color = LobbyUITheme.Gold;
            return;
        }

        _micRoot.localScale = Vector3.one;

        if (!listening)
        {
            _micGlowBorder.color = Color.clear;
            _micFrameImage.color = new Color(0.18f, 0.14f, 0.11f, 0.9f);
            _micBevelImage.color = new Color(0.28f, 0.22f, 0.18f, 0.9f);
            _micIconImage.color = LobbyUITheme.Hint;
            _micStatusText.text = "MIC 꺼짐";
            _micStatusText.color = LobbyUITheme.Hint;
        }
        else if (calibrating)
        {
            _micGlowBorder.color = Color.clear;
            _micFrameImage.color = LobbyUITheme.WoodDark;
            _micBevelImage.color = LobbyUITheme.WoodLight;
            _micIconImage.color = LobbyUITheme.Gold;
            _micStatusText.text = "소음측정..";
            _micStatusText.color = LobbyUITheme.Gold;
        }
        else if (speaking)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 12f);
            _micGlowBorder.color = new Color(0.32f, 0.95f, 0.52f, 0.55f + 0.35f * pulse);
            _micFrameImage.color = new Color(0.16f, 0.36f, 0.20f, 1f);
            _micBevelImage.color = new Color(0.42f, 0.92f, 0.56f, 1f);
            _micIconImage.color = new Color(0.55f, 1f, 0.68f, 1f);
            _micStatusText.text = "듣는 중!";
            _micStatusText.color = new Color(0.55f, 1f, 0.68f, 1f);
        }
        else
        {
            _micGlowBorder.color = Color.clear;
            _micFrameImage.color = LobbyUITheme.WoodDark;
            _micBevelImage.color = LobbyUITheme.WoodLight;
            _micIconImage.color = LobbyUITheme.Parch;
            _micStatusText.text = "MIC 대기";
            _micStatusText.color = LobbyUITheme.Parch;
        }
    }

    private void UpdatePowerUpBanner(int maxSlots)
    {
        if (_powerUpBanner == null || _localPlayer == null) return;

        bool active = maxSlots > 0 && _localPlayer.IsPoweredUp;
        if (_powerUpBanner.activeSelf != active)
        {
            _powerUpBanner.SetActive(active);
        }
        if (!active) return;

        float rem = _localPlayer.PowerUpRemainingSeconds;
        float total = Mathf.Max(0.1f, _localPlayer.powerUpDuration);
        float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 9f);

        if (_powerUpBarFill != null)
        {
            _powerUpBarFill.fillAmount = rem > 0f ? Mathf.Clamp01(rem / total) : 1f;
        }
        if (_powerUpBannerBg != null)
        {
            _powerUpBannerBg.color = Color.Lerp(
                new Color(0.26f, 0.15f, 0.04f, 0.96f),
                new Color(0.48f, 0.30f, 0.06f, 0.98f),
                wave);
        }
        if (_powerUpBannerText != null)
        {
            string secStr = rem > 0f ? $" ({rem:0.0}s)" : "";
            _powerUpBannerText.text = $"★ POWER UP : 다음 스킬 강화!{secStr} ★";
        }
    }

    private static Sprite CreateMicPixelSprite()
    {
        const int S = 32;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var px = new Color[S * S];
        for (int i = 0; i < px.Length; i++) px[i] = Color.clear;

        // 픽셀아트 마이크 캡슐 + 받침대 스탠드
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                int dx = Mathf.Abs(x - 16);
                // 마이크 헤드 (둥근 캡슐: y 14..28, 반폭 5)
                bool inHead = (y >= 15 && y <= 26 && dx <= 5) ||
                              ((y == 14 || y == 27) && dx <= 4) ||
                              ((y == 13 || y == 28) && dx <= 2);
                // 헤드 그릴 가로줄 디테일
                if (inHead && (y == 19 || y == 22 || y == 25) && dx <= 3)
                {
                    px[y * S + x] = new Color(1f, 1f, 1f, 0.45f);
                    continue;
                }
                // U자형 홀더 (y 10..17, 반폭 8..9)
                bool inCup = (y >= 12 && y <= 17 && (dx == 8 || dx == 9)) ||
                             (y >= 10 && y <= 11 && dx >= 4 && dx <= 8) ||
                             (y == 9 && dx <= 4);
                // 스탠드 기둥 & 바닥 받침대 (y 4..9)
                bool inStand = (y >= 5 && y <= 9 && dx <= 1) ||
                               (y >= 3 && y <= 4 && dx <= 7);

                if (inHead || inCup || inStand)
                {
                    px[y * S + x] = Color.white;
                }
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 32f);
    }

    private static Sprite CreateSolidSprite()
    {
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        Color[] cols = new Color[16];
        for (int i = 0; i < cols.Length; i++) cols[i] = Color.white;
        tex.SetPixels(cols);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
    }

    private class SlotUI
    {
        public int slotIndex;
        public GameObject root;
        public RectTransform rootTransform;
        public Text keyText;
        public Image frameImage;
        public Image bevelImage;
        public Image iconImage;
        public Text iconLabel;
        public Image cooldownOverlay;
        public Text cooldownText;
        public Image readyHighlight;

        public PlayerMovement.CharacterSkill lastSkill = PlayerMovement.CharacterSkill.None;
        public bool wasReady = true;
        public float pulseTimer = 0f;
    }
}
