using Fusion;
using TMPro;
using UnityEngine;
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

        // 스테이지에서 아직 열리지 않은 스킬 슬롯이면 무시
        if (slot < 1 || slot > 3 || !StageSkillRules.IsSlotAllowed(slot)) return;

        // 자신의 캐릭터에 맞지 않는 음성 키워드는 무시
        bool isFox = IsFoxCharacter();
        if (isFox && VoiceSkillInput.IsRabbitKeyword(keyword)) return;
        if (!isFox && VoiceSkillInput.IsFoxKeyword(keyword)) return;

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
            return _pm.IsFoxCharacter;
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