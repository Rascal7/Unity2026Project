using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PlayerNameTag : NetworkBehaviour
{
    // [Networked] 변수가 변하면 OnNameChanged를 실행하라고 설정
    [Networked, OnChangedRender(nameof(OnNameChanged))]
    public string Nickname { get; set; }

    public TextMeshProUGUI nameText;

    // --- 음성 명령 말풍선 팝업 UI ---
    private GameObject _bubbleRoot;
    private RectTransform _bubbleCardRt;
    private CanvasGroup _bubbleGroup;
    private Image _bubbleBorderImg;
    private Image _bubbleBgImg;
    private Text _bubbleText;
    private float _bubbleTimer;
    private float _bubbleDuration = 1.65f;
    private Vector3 _bubbleBaseLocalPos = new Vector3(0f, 1.55f, 0f);
    private PlayerMovement _pm;
    private bool _subscribedVoice;
    private float _lastPingSentTime = -999f;

    private void Awake()
    {
        _pm = GetComponent<PlayerMovement>();
    }

    public override void Spawned()
    {
        if (_pm == null) _pm = GetComponent<PlayerMovement>();

        // 모든 클라이언트가 접속하자마자 현재 데이터로 텍스트 업데이트
        OnNameChanged();

        if (Object.HasInputAuthority)
        {
            // 1. 저장된 이름을 정확한 키값("PlayerName")으로 가져옵니다.
            string myName = PlayerPrefs.GetString("PlayerName", "Unknown");
            Debug.Log($"<color=lime>[NameTag]</color> 내 이름은 {myName}입니다. 서버에 알립니다.");

            // 2. 서버에 내 이름을 보고
            RPC_SetNickname(myName);

            SubscribeVoiceEvents();
            CoopPingAndIndicatorHUD.EnsureInstance();
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        UnsubscribeVoiceEvents();
    }

    private void OnDestroy()
    {
        UnsubscribeVoiceEvents();
    }

    private void SubscribeVoiceEvents()
    {
        if (_subscribedVoice) return;
        VoiceSkillInput.OnVoiceCommandRecognized += HandleLocalVoiceRecognized;
        _subscribedVoice = true;
    }

    private void UnsubscribeVoiceEvents()
    {
        if (!_subscribedVoice) return;
        VoiceSkillInput.OnVoiceCommandRecognized -= HandleLocalVoiceRecognized;
        _subscribedVoice = false;
    }

    private void HandleLocalVoiceRecognized(string keyword, int slot)
    {
        if (Object == null || !Object.IsValid || !Object.HasInputAuthority) return;
        if (SettingsMenuUI.GameplayInputSuppressed) return;

        // 협동 핑 음성 명령 ("여기")
        if (slot == VoiceSkillInput.PingSlot)
        {
            RequestVoiceBubble("여기!");
            TriggerCoopPingAtCharacterFront("여기!");
            return;
        }

        // 확장 스킬 음성 명령 ("그림자"/"분신" -> FoxShadow, "스톰프"/"쿵" -> RabbitStomp)
        if (slot == VoiceSkillInput.ExtraSkillSlot ||
            keyword == "그림자" || keyword == "분신" || keyword == "스톰프" || keyword == "쿵")
        {
            bool isFox = IsFoxCharacter();
            if (isFox && (keyword == "그림자" || keyword == "분신"))
            {
                RequestVoiceBubble("분신!");
                if (_pm != null) _pm.RPC_RequestExtraSkill(PlayerMovement.CharacterSkill.FoxShadow);
            }
            else if (!isFox && (keyword == "스톰프" || keyword == "쿵"))
            {
                RequestVoiceBubble("쿵!");
                if (_pm != null) _pm.RPC_RequestExtraSkill(PlayerMovement.CharacterSkill.RabbitStomp);
            }
            return;
        }

        // 스테이지에서 아직 열리지 않은 스킬 슬롯이면 무시
        if (slot < 1 || slot > 3 || !StageSkillRules.IsSlotAllowed(slot)) return;

        string shout = ResolveSkillShoutText(keyword, slot);
        RequestVoiceBubble(shout);
    }

    private string ResolveSkillShoutText(string keyword, int slot)
    {
        if (_pm != null)
        {
            var skill = _pm.GetSlotSkill(slot);
            switch (skill)
            {
                case PlayerMovement.CharacterSkill.FoxDash: return "대시!";
                case PlayerMovement.CharacterSkill.FoxSniff: return "킁킁!";
                case PlayerMovement.CharacterSkill.FoxSwap: return "체인지!";
                case PlayerMovement.CharacterSkill.FoxShadow: return "분신!";
                case PlayerMovement.CharacterSkill.RabbitCarrot: return "당근!";
                case PlayerMovement.CharacterSkill.RabbitJump: return "점프!";
                case PlayerMovement.CharacterSkill.RabbitPowerUp: return "파워업!";
                case PlayerMovement.CharacterSkill.RabbitStomp: return "쿵!";
            }
        }
        return string.IsNullOrEmpty(keyword) ? "스킬!" : $"{keyword}!";
    }

    public void RequestVoiceBubble(string shoutText)
    {
        if (string.IsNullOrEmpty(shoutText)) return;
        ShowVoiceBubbleLocal(shoutText);

        if (Object != null && Object.IsValid && Runner != null && Runner.IsRunning && Object.HasInputAuthority)
        {
            RPC_BroadcastVoiceBubble(shoutText);
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
    public void RPC_BroadcastVoiceBubble(string shoutText)
    {
        // 입력 권한자(본인)는 RequestVoiceBubble 에서 즉시 표시했으므로 중복 실행 방지
        if (Object != null && Object.HasInputAuthority) return;
        ShowVoiceBubbleLocal(shoutText);
    }

    public void ShowVoiceBubbleLocal(string shoutText)
    {
        EnsureBubbleUI();
        if (_bubbleRoot == null || _bubbleText == null) return;

        bool isFox = IsFoxCharacter();
        Color accentBorder = isFox ? new Color(0.95f, 0.48f, 0.15f, 1f) : new Color(0.28f, 0.72f, 0.95f, 1f);
        if (shoutText.Contains("파워업"))
        {
            accentBorder = new Color(1f, 0.80f, 0.12f, 1f);
        }

        _bubbleBorderImg.color = accentBorder;
        _bubbleText.text = shoutText;
        _bubbleTimer = _bubbleDuration;
        _bubbleRoot.SetActive(true);
        _bubbleGroup.alpha = 1f;
    }

    public bool IsFoxCharacter()
    {
        if (_pm == null) _pm = GetComponent<PlayerMovement>();
        if (_pm != null)
        {
            if (_pm.skillCh1 == PlayerMovement.CharacterSkill.FoxDash ||
                _pm.skillCh2 == PlayerMovement.CharacterSkill.FoxSniff)
                return true;
            if (_pm.skillCh1 == PlayerMovement.CharacterSkill.RabbitCarrot ||
                _pm.skillCh2 == PlayerMovement.CharacterSkill.RabbitJump)
                return false;
        }
        return gameObject.name.IndexOf("Player1", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               gameObject.name.IndexOf("Fox", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public void TriggerCoopPingAtCharacterFront(string label = "여기!")
    {
        if (_pm == null) _pm = GetComponent<PlayerMovement>();
        float dir = (_pm != null && !_pm.IsFacingRight) ? -1f : 1f;
        Vector2 targetPos = (Vector2)transform.position + new Vector2(dir * 1.5f, 0.35f);
        TriggerCoopPingAtWorldPos(targetPos, label);
    }

    public void TriggerCoopPingAtWorldPos(Vector2 worldPos, string label = "여기!")
    {
        if (Time.unscaledTime - _lastPingSentTime < 0.65f) return;
        _lastPingSentTime = Time.unscaledTime;

        string sender = GetResolvedName();
        if (string.IsNullOrEmpty(sender)) sender = IsFoxCharacter() ? "여우" : "토끼";
        bool isFox = IsFoxCharacter();

        CoopPingAndIndicatorHUD.EnsureInstance().SpawnPingMarker(worldPos, sender, isFox, label);

        if (Object != null && Object.IsValid && Runner != null && Runner.IsRunning && Object.HasInputAuthority)
        {
            RPC_BroadcastCoopPing(worldPos, sender, isFox, label);
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
    public void RPC_BroadcastCoopPing(Vector2 worldPos, string senderName, NetworkBool isFox, string label)
    {
        if (Object != null && Object.HasInputAuthority) return;
        CoopPingAndIndicatorHUD.EnsureInstance().SpawnPingMarker(worldPos, senderName, isFox, label);
    }

    private void EnsureBubbleUI()
    {
        if (_bubbleRoot != null) return;

        float topY = 1.45f;
        var sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null)
        {
            topY = Mathf.Max(1.35f, (sr.bounds.max.y - transform.position.y) + 0.65f);
        }
        _bubbleBaseLocalPos = new Vector3(0f, topY, 0f);

        _bubbleRoot = new GameObject("VoiceSpeechBubble", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        _bubbleRoot.transform.SetParent(transform, false);
        _bubbleRoot.transform.localPosition = _bubbleBaseLocalPos;
        _bubbleRoot.transform.localRotation = Quaternion.identity;

        Vector3 ls = transform.lossyScale;
        float sx = 0.01f / Mathf.Max(0.0001f, Mathf.Abs(ls.x)) * Mathf.Sign(ls.x);
        float sy = 0.01f / Mathf.Max(0.0001f, Mathf.Abs(ls.y)) * Mathf.Sign(ls.y);
        _bubbleRoot.transform.localScale = new Vector3(sx, sy, 1f);

        var canvas = _bubbleRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 240;

        var rootRt = (RectTransform)_bubbleRoot.transform;
        rootRt.sizeDelta = new Vector2(220f, 84f);

        _bubbleGroup = _bubbleRoot.GetComponent<CanvasGroup>();
        _bubbleGroup.alpha = 0f;
        _bubbleGroup.interactable = false;
        _bubbleGroup.blocksRaycasts = false;

        // 말풍선 카드 컨테이너
        var cardGo = new GameObject("BubbleCard", typeof(RectTransform), typeof(Image), typeof(Outline));
        cardGo.transform.SetParent(_bubbleRoot.transform, false);
        _bubbleCardRt = cardGo.GetComponent<RectTransform>();
        _bubbleCardRt.anchorMin = new Vector2(0.5f, 0.5f);
        _bubbleCardRt.anchorMax = new Vector2(0.5f, 0.5f);
        _bubbleCardRt.pivot = new Vector2(0.5f, 0.5f);
        _bubbleCardRt.anchoredPosition = new Vector2(0f, 8f);
        _bubbleCardRt.sizeDelta = new Vector2(186f, 62f);

        _bubbleBorderImg = cardGo.GetComponent<Image>();
        _bubbleBorderImg.color = new Color(0.95f, 0.48f, 0.15f, 1f);
        _bubbleBorderImg.raycastTarget = false;

        var cardOutline = cardGo.GetComponent<Outline>();
        cardOutline.effectColor = new Color(0.12f, 0.07f, 0.02f, 0.95f);
        cardOutline.effectDistance = new Vector2(3f, -3f);

        // 내부 밝은 양피지 배경
        var innerGo = new GameObject("Inner", typeof(RectTransform), typeof(Image));
        innerGo.transform.SetParent(cardGo.transform, false);
        var innerRt = innerGo.GetComponent<RectTransform>();
        innerRt.anchorMin = Vector2.zero;
        innerRt.anchorMax = Vector2.one;
        innerRt.offsetMin = new Vector2(4f, 4f);
        innerRt.offsetMax = new Vector2(-4f, -4f);
        _bubbleBgImg = innerGo.GetComponent<Image>();
        _bubbleBgImg.color = new Color(1f, 0.97f, 0.88f, 0.98f);
        _bubbleBgImg.raycastTarget = false;

        // 말풍선 아래 꼬리 (▼)
        var tailGo = new GameObject("Tail", typeof(RectTransform), typeof(Text));
        tailGo.transform.SetParent(cardGo.transform, false);
        var tailRt = tailGo.GetComponent<RectTransform>();
        tailRt.anchorMin = new Vector2(0.5f, 0f);
        tailRt.anchorMax = new Vector2(0.5f, 0f);
        tailRt.pivot = new Vector2(0.5f, 1f);
        tailRt.anchoredPosition = new Vector2(0f, 6f);
        tailRt.sizeDelta = new Vector2(40f, 24f);
        var tailTxt = tailGo.GetComponent<Text>();
        tailTxt.font = LobbyUITheme.PlainFont;
        tailTxt.fontSize = 20;
        tailTxt.alignment = TextAnchor.UpperCenter;
        tailTxt.color = new Color(0.18f, 0.11f, 0.04f, 1f);
        tailTxt.text = "▼";
        tailTxt.raycastTarget = false;

        // 코믹 텍스트 ("대시!", "킁킁!", "파워업!" 등)
        var textGo = new GameObject("ShoutText", typeof(RectTransform), typeof(Text), typeof(Outline));
        textGo.transform.SetParent(innerGo.transform, false);
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(6f, 2f);
        textRt.offsetMax = new Vector2(-6f, -2f);
        _bubbleText = textGo.GetComponent<Text>();
        _bubbleText.font = LobbyUITheme.PlainFont;
        _bubbleText.fontSize = 30;
        _bubbleText.fontStyle = FontStyle.Bold;
        _bubbleText.alignment = TextAnchor.MiddleCenter;
        _bubbleText.horizontalOverflow = HorizontalWrapMode.Overflow;
        _bubbleText.verticalOverflow = VerticalWrapMode.Overflow;
        _bubbleText.color = new Color(0.16f, 0.08f, 0.02f, 1f);
        _bubbleText.raycastTarget = false;

        var txtOutline = textGo.GetComponent<Outline>();
        txtOutline.effectColor = new Color(1f, 0.88f, 0.42f, 0.9f);
        txtOutline.effectDistance = new Vector2(1.5f, -1.5f);

        _bubbleRoot.SetActive(false);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetNickname(string name)
    {
        // 서버가 값을 바꾸면 모든 클라이언트의 OnNameChanged가 실행됨
        Nickname = name;
    }

    // Fusion 2에서는 'static'과 'Changed<T>' 없이 이렇게 깔끔하게 작성합니다.
    public void OnNameChanged()
    {
        if (nameText != null && !string.IsNullOrEmpty(Nickname))
        {
            nameText.text = Nickname;
            nameText.ForceMeshUpdate();
        }
    }

    // 이름표 및 음성 말풍선이 카메라를 향해 똑바로 보이도록 처리
    private void LateUpdate()
    {
        Quaternion camRot = Camera.main != null ? Camera.main.transform.rotation : Quaternion.identity;

        if (nameText != null)
        {
            nameText.transform.rotation = camRot;
        }

        if (_bubbleRoot != null && _bubbleRoot.activeSelf)
        {
            _bubbleRoot.transform.rotation = camRot;
            Vector3 ls = transform.lossyScale;
            float sx = 0.01f / Mathf.Max(0.0001f, Mathf.Abs(ls.x)) * Mathf.Sign(ls.x);
            float sy = 0.01f / Mathf.Max(0.0001f, Mathf.Abs(ls.y)) * Mathf.Sign(ls.y);
            _bubbleRoot.transform.localScale = new Vector3(sx, sy, 1f);

            _bubbleTimer -= Time.deltaTime;
            if (_bubbleTimer <= 0f)
            {
                _bubbleRoot.SetActive(false);
            }
            else
            {
                float elapsed = _bubbleDuration - _bubbleTimer;
                // 등장 시 통통 튀는 팝업 스케일 연출
                float scale = 1f;
                if (elapsed < 0.16f)
                {
                    float p = elapsed / 0.16f;
                    scale = Mathf.Lerp(0.35f, 1.22f, p);
                }
                else if (elapsed < 0.28f)
                {
                    float p = (elapsed - 0.16f) / 0.12f;
                    scale = Mathf.Lerp(1.22f, 1.0f, p);
                }

                if (_bubbleCardRt != null)
                {
                    _bubbleCardRt.localScale = new Vector3(scale, scale, 1f);
                }

                // 위로 살짝 떠오르며 마지막 0.3초 동안 페이드아웃
                float floatOffset = Mathf.Min(0.25f, elapsed * 0.16f);
                _bubbleRoot.transform.localPosition = _bubbleBaseLocalPos + new Vector3(0f, floatOffset, 0f);
                _bubbleGroup.alpha = _bubbleTimer < 0.3f ? Mathf.Clamp01(_bubbleTimer / 0.3f) : 1f;
            }
        }
    }

    public string GetResolvedName()
    {
        if (!string.IsNullOrEmpty(Nickname)) return Nickname;
        if (nameText != null && !string.IsNullOrEmpty(nameText.text)) return nameText.text;
        return null;
    }
}

/// <summary>
/// 2인 협동 전용 HUD:
/// 1) 파트너가 카메라 화면 밖에 있을 때 화면 가장자리에 파트너 캐릭터 아이콘/이름/거리와 방향 화살표 표시 (Off-screen Indicator)
/// 2) 단축키(G / 마우스 휠클릭 / 우클릭 조준) 또는 음성("여기")으로 원하는 벽/발판 위치에 협동 핑 마커를 찍어 파트너와 공유
/// </summary>
public class CoopPingAndIndicatorHUD : MonoBehaviour
{
    public static CoopPingAndIndicatorHUD Instance { get; private set; }

    private Canvas _canvas;
    private RectTransform _canvasRt;

    // 화면 밖 파트너 위치 표시기 UI
    private GameObject _partnerIndicatorRoot;
    private RectTransform _partnerIndicatorRt;
    private CanvasGroup _partnerIndicatorGroup;
    private RectTransform _partnerArrowRt;
    private Image _partnerArrowImg;
    private Image _partnerBadgeBorder;
    private Image _partnerCharDot;
    private Text _partnerNameText;
    private Text _partnerDistText;

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
        _partnerIndicatorRt.sizeDelta = new Vector2(160f, 64f);

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
        _partnerArrowRt.sizeDelta = new Vector2(44f, 44f);
        var arrowTxt = arrowGo.GetComponent<Text>();
        arrowTxt.font = LobbyUITheme.PlainFont;
        arrowTxt.fontSize = 32;
        arrowTxt.fontStyle = FontStyle.Bold;
        arrowTxt.alignment = TextAnchor.MiddleCenter;
        arrowTxt.color = LobbyUITheme.Gold;
        arrowTxt.text = "▶";
        arrowTxt.raycastTarget = false;
        var arrowOutline = arrowGo.GetComponent<Outline>();
        arrowOutline.effectColor = LobbyUITheme.GoldDeep;
        arrowOutline.effectDistance = new Vector2(2f, -2f);

        // 항상 수평을 유지하는 파트너 카드 배지
        var cardGo = new GameObject("PartnerCard", typeof(RectTransform), typeof(Image), typeof(Outline));
        cardGo.transform.SetParent(_partnerIndicatorRoot.transform, false);
        var cardRt = cardGo.GetComponent<RectTransform>();
        cardRt.anchorMin = cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.pivot = new Vector2(0.5f, 0.5f);
        cardRt.sizeDelta = new Vector2(142f, 54f);
        _partnerBadgeBorder = cardGo.GetComponent<Image>();
        _partnerBadgeBorder.color = new Color(0.95f, 0.52f, 0.18f, 0.96f);
        _partnerBadgeBorder.raycastTarget = false;
        var cardOutline = cardGo.GetComponent<Outline>();
        cardOutline.effectColor = new Color(0.10f, 0.06f, 0.02f, 0.95f);
        cardOutline.effectDistance = new Vector2(2.5f, -2.5f);

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

        // 캐릭터 구분 컬러 아이콘 점
        var dotGo = new GameObject("CharDot", typeof(RectTransform), typeof(Image));
        dotGo.transform.SetParent(innerGo.transform, false);
        var dotRt = dotGo.GetComponent<RectTransform>();
        dotRt.anchorMin = dotRt.anchorMax = new Vector2(0f, 0.5f);
        dotRt.pivot = new Vector2(0f, 0.5f);
        dotRt.anchoredPosition = new Vector2(8f, 0f);
        dotRt.sizeDelta = new Vector2(14f, 32f);
        _partnerCharDot = dotGo.GetComponent<Image>();
        _partnerCharDot.color = new Color(0.95f, 0.52f, 0.18f, 1f);
        _partnerCharDot.raycastTarget = false;

        // 파트너 이름
        var nameGo = new GameObject("Name", typeof(RectTransform), typeof(Text));
        nameGo.transform.SetParent(innerGo.transform, false);
        var nameRt = nameGo.GetComponent<RectTransform>();
        nameRt.anchorMin = new Vector2(0f, 0.45f);
        nameRt.anchorMax = new Vector2(1f, 1f);
        nameRt.offsetMin = new Vector2(28f, 0f);
        nameRt.offsetMax = new Vector2(-6f, -2f);
        _partnerNameText = nameGo.GetComponent<Text>();
        _partnerNameText.font = LobbyUITheme.PlainFont;
        _partnerNameText.fontSize = 16;
        _partnerNameText.fontStyle = FontStyle.Bold;
        _partnerNameText.alignment = TextAnchor.MiddleLeft;
        _partnerNameText.horizontalOverflow = HorizontalWrapMode.Overflow;
        _partnerNameText.color = LobbyUITheme.Parch;
        _partnerNameText.raycastTarget = false;

        // 파트너 거리 ("18m")
        var distGo = new GameObject("Distance", typeof(RectTransform), typeof(Text));
        distGo.transform.SetParent(innerGo.transform, false);
        var distRt = distGo.GetComponent<RectTransform>();
        distRt.anchorMin = new Vector2(0f, 0f);
        distRt.anchorMax = new Vector2(1f, 0.50f);
        distRt.offsetMin = new Vector2(28f, 2f);
        distRt.offsetMax = new Vector2(-6f, 0f);
        _partnerDistText = distGo.GetComponent<Text>();
        _partnerDistText.font = LobbyUITheme.PixelFont;
        _partnerDistText.fontSize = 15;
        _partnerDistText.alignment = TextAnchor.MiddleLeft;
        _partnerDistText.color = LobbyUITheme.Gold;
        _partnerDistText.raycastTarget = false;

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

        // 0) 확장 스킬 단축키(F): 여우는 그림자 분신(FoxShadow), 토끼는 쿵(RabbitStomp) 발동
        if (Input.GetKeyDown(KeyCode.F))
        {
            var pm = localTag.GetComponent<PlayerMovement>();
            if (pm != null)
            {
                bool isFox = localTag.IsFoxCharacter();
                localTag.RequestVoiceBubble(isFox ? "분신!" : "쿵!");
                pm.RPC_RequestExtraSkill(isFox ? PlayerMovement.CharacterSkill.FoxShadow : PlayerMovement.CharacterSkill.RabbitStomp);
            }
            return;
        }

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

                if (SettingsMenuUI.Instance == null || !SettingsMenuUI.Instance.IsOpen)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
        }
    }

    private PlayerNameTag FindLocalPlayerNameTag()
    {
        foreach (var p in PlayerMovement.ActivePlayers())
        {
            if (p != null && p.Object != null && p.Object.HasInputAuthority)
            {
                return p.GetComponent<PlayerNameTag>();
            }
        }
        return null;
    }

    private void UpdatePartnerOffscreenIndicator()
    {
        if (_partnerIndicatorRoot == null) return;

        PlayerMovement localPlayer = null;
        PlayerMovement partnerPlayer = null;

        foreach (var p in PlayerMovement.ActivePlayers())
        {
            if (p == null || p.Object == null || !p.Object.IsValid) continue;
            if (p.Object.HasInputAuthority) localPlayer = p;
            else partnerPlayer = p;
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

        PlaceIndicatorOnScreenEdge(vp, _partnerIndicatorRt, _partnerArrowRt, 92f, 68f);

        // 파트너 정보(여우/토끼 색상, 닉네임, 거리) 갱신
        var tag = partnerPlayer.GetComponent<PlayerNameTag>();
        bool isFox = tag != null && tag.IsFoxCharacter();
        string charLabel = isFox ? "여우" : "토끼";
        string nick = tag != null ? tag.GetResolvedName() : null;
        if (string.IsNullOrEmpty(nick) || nick == "Unknown") nick = charLabel;

        Color charCol = isFox ? new Color(0.96f, 0.52f, 0.16f, 1f) : new Color(0.32f, 0.78f, 0.96f, 1f);
        _partnerBadgeBorder.color = charCol;
        _partnerCharDot.color = charCol;
        _partnerNameText.text = nick;

        Vector3 refPos = localPlayer != null ? localPlayer.transform.position : cam.transform.position;
        float dist = Vector2.Distance(refPos, partnerWorld);
        _partnerDistText.text = $"[{charLabel}] {dist:0}m";
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
        PlaceIndicatorOnScreenEdge(vp, _pingOffscreenRt, _pingOffscreenArrowRt, 110f, 88f);

        float dist = Vector2.Distance(cam.transform.position, latest.worldPos);
        _pingOffscreenText.text = $"✦ 핑 {dist:0}m";
    }

    private void PlaceIndicatorOnScreenEdge(Vector3 viewportPos, RectTransform rootRt, RectTransform arrowRt, float marginX, float marginY)
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
            arrowRt.anchoredPosition = dir * 74f;
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