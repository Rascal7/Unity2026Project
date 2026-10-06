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

        SceneManager.sceneLoaded += OnSceneLoaded;
        SkillKeybinds.OnChanged += OnKeybindsChanged;
        EnsureCanvas();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SkillKeybinds.OnChanged -= OnKeybindsChanged;
        if (Instance == this) Instance = null;
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
        EnsureCanvas();
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

        // --- 준비 완료 펄스 하이라이트 (Gold Flash) ---
        var readyBorderGo = new GameObject("ReadyHighlight", typeof(RectTransform), typeof(Image));
        readyBorderGo.transform.SetParent(frameGo.transform, false);
        var readyBorderRt = readyBorderGo.GetComponent<RectTransform>();
        readyBorderRt.anchorMin = Vector2.zero;
        readyBorderRt.anchorMax = Vector2.one;
        readyBorderRt.offsetMin = new Vector2(-4f, -4f);
        readyBorderRt.offsetMax = new Vector2(4f, 4f);

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
            return;
        }

        // 2. 스테이지별 사용 가능 슬롯 수 (튜토리얼 0 / 1스테이지 1 / 2스테이지 2 / 3스테이지 3)
        int maxSlots = StageSkillRules.MaxSkillSlots(currentScene);
        bool showHud = maxSlots > 0;
        if (_hudContainer != null && _hudContainer.gameObject.activeSelf != showHud)
            _hudContainer.gameObject.SetActive(showHud);
        if (!showHud) return;

        LayoutSlots(maxSlots);

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

        // 펄스 연출 처리
        if (slot.pulseTimer > 0f)
        {
            slot.pulseTimer -= Time.deltaTime;
            float t = Mathf.Clamp01(slot.pulseTimer / 0.45f);
            // 테두리 플래시
            slot.readyHighlight.color = new Color(LobbyUITheme.Gold.r, LobbyUITheme.Gold.g, LobbyUITheme.Gold.b, t * 0.95f);
            // 슬롯 살짝 확대 pop
            float scale = 1f + (Mathf.Sin(t * Mathf.PI) * 0.14f);
            slot.rootTransform.localScale = new Vector3(scale, scale, 1f);
        }
        else if (_localPlayer.IsPoweredUp &&
                 (status.Skill == PlayerMovement.CharacterSkill.RabbitCarrot || status.Skill == PlayerMovement.CharacterSkill.RabbitJump))
        {
            // 파워업 상태: 다음에 강화될 스킬 슬롯을 주황빛으로 맥박치게 표시
            float a = 0.45f + 0.45f * Mathf.Sin(Time.unscaledTime * 8f);
            slot.readyHighlight.color = new Color(1f, 0.55f, 0.1f, a);
            slot.rootTransform.localScale = Vector3.one;
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
