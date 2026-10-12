using UnityEngine;
using UnityEngine.UI;
using Fusion;

/// <summary>
/// 플레이어(로컬)가 이 트리거 존에 들어오면 현재 키바인딩이 반영된 힌트를 화면에 띄우고, 나가면 숨긴다.
/// 또한 월드 오브젝트(Hint_Move, Hint_Jump, Hint_Ladder 등)에 부착 시 표지판 하단에 실시간 키 라벨도 함께 표시한다.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class TutorialHintZone : MonoBehaviour
{
    public enum HintActionType
    {
        Auto,
        Move,
        Jump,
        Ladder,
        Skill1,
        Skill2,
        Skill3,
        Custom
    }

    [Header("힌트 종류 및 문구")]
    [Tooltip("Auto 선택 시 오브젝트 이름(Hint_Move/Jump/Ladder) 또는 message 문구를 기반으로 자동 판별")]
    public HintActionType hintType = HintActionType.Auto;

    [TextArea]
    [Tooltip("Custom 모드이거나 추가 안내가 필요할 때 사용할 템플릿 ({Move}, {Jump}, {Ladder}, {Skill1~3} 토큰 지원)")]
    public string message = "이동:  {Move}";

    [Header("월드 공간 동적 키 라벨")]
    [Tooltip("켜두면 표지판 오브젝트 하단에 현재 바인딩된 키 텍스트를 월드 캔버스로 함께 표시")]
    public bool showWorldKeyLabel = true;
    public Vector3 worldLabelOffset = new Vector3(0f, -0.82f, 0f);

    private Text _worldLabel;

    private void Reset()
    {
        var c = GetComponent<Collider2D>();
        if (c != null) c.isTrigger = true;
    }

    private void Awake()
    {
        var c = GetComponent<Collider2D>();
        if (c != null) c.isTrigger = true;

        if (showWorldKeyLabel)
        {
            CreateWorldLabel();
            RefreshWorldLabel();
        }

        MovementKeybinds.OnChanged += RefreshWorldLabel;
        SkillKeybinds.OnChanged += RefreshWorldLabel;
    }

    private void OnDestroy()
    {
        MovementKeybinds.OnChanged -= RefreshWorldLabel;
        SkillKeybinds.OnChanged -= RefreshWorldLabel;
    }

    public HintActionType ResolveActionType()
    {
        if (hintType != HintActionType.Auto) return hintType;

        string n = gameObject.name.ToLowerInvariant();
        if (n.Contains("move")) return HintActionType.Move;
        if (n.Contains("jump")) return HintActionType.Jump;
        if (n.Contains("ladder")) return HintActionType.Ladder;
        if (n.Contains("skill1")) return HintActionType.Skill1;
        if (n.Contains("skill2")) return HintActionType.Skill2;
        if (n.Contains("skill3")) return HintActionType.Skill3;

        if (!string.IsNullOrEmpty(message))
        {
            if (message.Contains("점프") || message.Contains("{Jump}")) return HintActionType.Jump;
            if (message.Contains("사다리") || message.Contains("{Ladder}")) return HintActionType.Ladder;
            if (message.Contains("이동") || message.Contains("< >") || message.Contains("{Move}")) return HintActionType.Move;
        }

        return HintActionType.Custom;
    }

    public string GetFormattedMessage()
    {
        switch (ResolveActionType())
        {
            case HintActionType.Move:
                return $"좌우 이동 :  <color=#FAD16B>{TutorialHintUI.FormatActionPair(MovementKeybinds.Act.Left, MovementKeybinds.Act.Right)}</color>";
            case HintActionType.Jump:
                return $"점프 :  <color=#FAD16B>{TutorialHintUI.FormatSingleAction(MovementKeybinds.Act.Jump)}</color>  (길게 누르면 높이 점프)";
            case HintActionType.Ladder:
                return $"사다리 이동 :  <color=#FAD16B>{TutorialHintUI.FormatActionPair(MovementKeybinds.Act.Up, MovementKeybinds.Act.Down)}</color>";
            case HintActionType.Skill1:
                return $"스킬 1 :  <color=#FAD16B>{TutorialHintUI.FormatSkillSlot(1)}</color>";
            case HintActionType.Skill2:
                return $"스킬 2 :  <color=#FAD16B>{TutorialHintUI.FormatSkillSlot(2)}</color>";
            case HintActionType.Skill3:
                return $"스킬 3 :  <color=#FAD16B>{TutorialHintUI.FormatSkillSlot(3)}</color>";
            default:
                return TutorialHintUI.FormatDynamicText(message);
        }
    }

    public string GetShortWorldKeyText()
    {
        switch (ResolveActionType())
        {
            case HintActionType.Move:
                return TutorialHintUI.FormatActionPair(MovementKeybinds.Act.Left, MovementKeybinds.Act.Right);
            case HintActionType.Jump:
                return TutorialHintUI.FormatSingleAction(MovementKeybinds.Act.Jump);
            case HintActionType.Ladder:
                return TutorialHintUI.FormatActionPair(MovementKeybinds.Act.Up, MovementKeybinds.Act.Down);
            case HintActionType.Skill1:
                return TutorialHintUI.FormatSkillSlot(1);
            case HintActionType.Skill2:
                return TutorialHintUI.FormatSkillSlot(2);
            case HintActionType.Skill3:
                return TutorialHintUI.FormatSkillSlot(3);
            default:
                return TutorialHintUI.FormatDynamicText(message);
        }
    }

    private void CreateWorldLabel()
    {
        if (_worldLabel != null) return;

        var canvasGo = new GameObject("DynamicKeyLabelCanvas", typeof(RectTransform), typeof(Canvas));
        canvasGo.transform.SetParent(transform, false);
        canvasGo.transform.localPosition = worldLabelOffset;
        canvasGo.transform.localScale = new Vector3(0.012f, 0.012f, 0.012f);

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 25;

        var rt = canvasGo.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(420f, 70f);

        var textGo = new GameObject("KeyText", typeof(RectTransform), typeof(Text), typeof(Outline));
        textGo.transform.SetParent(canvasGo.transform, false);
        var trt = textGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;

        Font font = Resources.Load<Font>("Sunflower-Medium");
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        _worldLabel = textGo.GetComponent<Text>();
        _worldLabel.font = font;
        _worldLabel.fontSize = 32;
        _worldLabel.alignment = TextAnchor.MiddleCenter;
        _worldLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        _worldLabel.verticalOverflow = VerticalWrapMode.Overflow;
        _worldLabel.color = new Color(0.98f, 0.82f, 0.42f, 1f);
        _worldLabel.raycastTarget = false;

        var outline = textGo.GetComponent<Outline>();
        outline.effectColor = new Color(0.10f, 0.06f, 0.02f, 0.96f);
        outline.effectDistance = new Vector2(2.5f, -2.5f);
    }

    public void RefreshWorldLabel()
    {
        if (!showWorldKeyLabel)
        {
            if (_worldLabel != null) _worldLabel.gameObject.SetActive(false);
            return;
        }
        if (_worldLabel == null) CreateWorldLabel();
        if (_worldLabel != null)
        {
            _worldLabel.gameObject.SetActive(true);
            _worldLabel.text = GetShortWorldKeyText();
        }
    }

    // 네트워크가 없으면(단독 테스트) 그냥 통과, 있으면 로컬 플레이어만 true
    private bool IsLocalPlayer(Collider2D other)
    {
        if (!other.CompareTag("Player")) return false;
        var no = other.GetComponentInParent<NetworkObject>();
        return no == null || no.HasInputAuthority;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (IsLocalPlayer(other))
            TutorialHintUI.EnsureInstance().Show(this);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (IsLocalPlayer(other) && TutorialHintUI.Instance != null)
            TutorialHintUI.Instance.Hide(this);
    }
}

