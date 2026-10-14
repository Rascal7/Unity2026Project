using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 레버나 압력판에 의해 작동하는 퍼즐 기믹 공통 인터페이스.
/// </summary>
public interface ILeverMechanism
{
    void OnMechanismStateChanged(bool isOn);
}

/// <summary>
/// Sniff 로 드러난 동안에만 당길 수 있는 레버. (Revealable 상속)
/// 드러난 상태에서 플레이어(또는 여우 분신 / 토끼 당근 / 스톰프 충격파)가 닿으면 On 토글 →
/// 연결된 대상(문, 이동 발판, 가시 함정 등)을 활성/비활성 및 기믹 동작 수행.
/// stayOnOnceActivated 이면 한 번 ON 된 뒤로는 고정된다.
/// </summary>
public class LeverSwitch : Revealable
{
    [Networked] public NetworkBool On { get; set; }

    [Tooltip("레버 ON 일 때 활성화할 대상들 (문 열림, 징검다리 생성, 이동 발판 가동 등)")]
    public GameObject[] activateOnTargets;
    [Tooltip("레버 ON 일 때 비활성화할 대상들 (철창/문 제거, 가시 함정 비활성화 등)")]
    public GameObject[] deactivateOnTargets;
    [Tooltip("켜면 한 번 ON 된 뒤로는 다시 OFF 되지 않는다")]
    public bool stayOnOnceActivated = true;

    [Header("레버 시각 연출")]
    [Tooltip("레버 조작 시 스프라이트 좌우 반전 여부")]
    public bool flipSpriteOnToggle = true;
    public Color offTint = Color.white;
    public Color onTint = new Color(0.55f, 1f, 0.68f, 1f);

    private SpriteRenderer _leverSr;
    private bool _prevOnState;
    private bool _stateInitialized;
    private float _lastToggleTime = -999f;

    public override void Spawned()
    {
        base.Spawned();
        _leverSr = GetComponent<SpriteRenderer>();
        _prevOnState = On;
        _stateInitialized = true;
        ApplySwitchState(On, false);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsRevealed) return;
        if (Object == null || !Object.HasStateAuthority) return; // 토글 판정은 호스트

        bool isPlayerOrClone = other.CompareTag("Player") || other.GetComponent<FoxShadowClone>() != null;
        bool isCarrot = other.GetComponent<CarrotProjectile>() != null;
        if (!isPlayerOrClone && !isCarrot) return;

        TryToggleOnServer();
    }

    /// <summary>
    /// 호스트에서 레버를 토글한다 (플레이어 접촉, 여우 분신, 토끼 당근, 토끼 스톰프 충격파 등에서 호출).
    /// </summary>
    public bool TryToggleOnServer()
    {
        if (Object == null || !Object.HasStateAuthority) return false;
        if (!IsRevealed) return false;
        if (stayOnOnceActivated && On) return false; // 한 번 ON 되면 고정
        if (Time.time - _lastToggleTime < 0.35f) return false;

        _lastToggleTime = Time.time;
        On = !On;
        Debug.Log($"<color=orange>[Lever]</color> '{name}' 토글 → On={On}");
        return true;
    }

    public override void Render()
    {
        base.Render();
        bool currentOn = On;
        bool playFx = _stateInitialized && (currentOn != _prevOnState);
        _prevOnState = currentOn;
        _stateInitialized = true;

        ApplySwitchState(currentOn, playFx);
    }

    public void ApplySwitchState(bool isOn, bool playFeedback)
    {
        if (_leverSr == null) _leverSr = GetComponent<SpriteRenderer>();
        if (_leverSr != null)
        {
            if (flipSpriteOnToggle) _leverSr.flipX = isOn;
            _leverSr.color = isOn ? onTint : offTint;
        }

        if (playFeedback)
        {
            SfxPlayer.Lever(isOn);
            var fx = SkillFxLibrary.Instance;
            if (fx != null && fx.revealShine != null && fx.revealShine.Length > 0)
            {
                SpriteEffect.Spawn(fx.revealShine, transform.position, fx.revealShineFps * 1.2f, fx.revealShineScale,
                    false, RevealFxSortingLayer(), fx.sortingOrder + 1, isOn ? onTint : Color.white);
            }
        }

        ApplyTargets(activateOnTargets, deactivateOnTargets, isOn);
    }

    public static void ApplyTargets(GameObject[] activateTargets, GameObject[] deactivateTargets, bool isOn)
    {
        if (activateTargets != null)
        {
            foreach (var t in activateTargets)
            {
                if (t == null) continue;
                var mechs = t.GetComponents<ILeverMechanism>();
                if (mechs != null && mechs.Length > 0)
                {
                    foreach (var m in mechs) m.OnMechanismStateChanged(isOn);
                }
                else if (t.activeSelf != isOn)
                {
                    t.SetActive(isOn);
                }
            }
        }

        if (deactivateTargets != null)
        {
            foreach (var t in deactivateTargets)
            {
                if (t == null) continue;
                var mechs = t.GetComponents<ILeverMechanism>();
                if (mechs != null && mechs.Length > 0)
                {
                    foreach (var m in mechs) m.OnMechanismStateChanged(!isOn);
                }
                else if (t.activeSelf != !isOn)
                {
                    t.SetActive(!isOn);
                }
            }
        }
    }
}

/// <summary>
/// 레버(LeverSwitch) 또는 협동 압력판(CoopPressurePlate)과 연동되어 부드럽게 열리고 닫히는 개폐형 문(Door) 기믹.
/// 대상 오브젝트에 이 컴포넌트를 붙이고 LeverSwitch 의 activateOnTargets 에 연결하면,
/// 단순 SetActive 대신 지정된 오프셋(openOffset)으로 부드럽게 슬라이드 개폐된다.
/// </summary>
public class CoopDoor : MonoBehaviour, ILeverMechanism
{
    [Tooltip("문이 열릴 때 이동할 오프셋 (기본: 위로 3.2m 상승)")]
    public Vector2 openOffset = new Vector2(0f, 3.2f);
    [Tooltip("문 개폐 속도 (초당 월드 단위)")]
    public float moveSpeed = 6f;
    [Tooltip("현재 열려 있는지 여부")]
    public bool isOpen = false;

    private Vector3 _closedPos;
    private bool _initialized;

    private void Awake()
    {
        EnsureInit();
    }

    private void EnsureInit()
    {
        if (_initialized) return;
        _initialized = true;
        _closedPos = transform.position;
    }

    public void OnMechanismStateChanged(bool isOn)
    {
        EnsureInit();
        if (isOpen != isOn)
        {
            isOpen = isOn;
        }
    }

    private void Update()
    {
        EnsureInit();
        Vector3 target = isOpen
            ? _closedPos + new Vector3(openOffset.x, openOffset.y, 0f)
            : _closedPos;

        if ((transform.position - target).sqrMagnitude > 0.0001f)
        {
            transform.position = Vector3.MoveTowards(transform.position, target, moveSpeed * Time.deltaTime);
        }
    }
}

/// <summary>
/// 레버(LeverSwitch) 또는 압력판에 의해 작동하거나 상시 왕복 이동하는 이동 발판(Moving Platform) 기믹.
/// 위에 올라탄 플레이어를 발판 이동량만큼 자연스럽게 함께 이동시킨다.
/// </summary>
public class CoopMovingPlatform : MonoBehaviour, ILeverMechanism
{
    [Tooltip("왕복 이동할 목표 오프셋 (시작 위치 기준)")]
    public Vector2 moveOffset = new Vector2(4f, 0f);
    [Tooltip("이동 속도 (초당 월드 단위)")]
    public float speed = 2.5f;
    [Tooltip("양 끝점에서 대기하는 시간(초)")]
    public float waitAtEnds = 0.35f;
    [Tooltip("레버 연결 없이 처음부터 작동할지 여부")]
    public bool isActive = true;

    private Vector3 _startPos;
    private Vector3 _endPos;
    private bool _goingToEnd = true;
    private float _waitTimer;
    private bool _initialized;
    private readonly HashSet<Rigidbody2D> _passengers = new HashSet<Rigidbody2D>();

    private void Awake()
    {
        EnsureInit();
    }

    private void EnsureInit()
    {
        if (_initialized) return;
        _initialized = true;
        _startPos = transform.position;
        _endPos = _startPos + new Vector3(moveOffset.x, moveOffset.y, 0f);
    }

    public void OnMechanismStateChanged(bool isOn)
    {
        EnsureInit();
        isActive = isOn;
    }

    private void FixedUpdate()
    {
        EnsureInit();
        if (!isActive) return;

        if (_waitTimer > 0f)
        {
            _waitTimer -= Time.fixedDeltaTime;
            return;
        }

        Vector3 target = _goingToEnd ? _endPos : _startPos;
        Vector3 prev = transform.position;
        Vector3 next = Vector3.MoveTowards(prev, target, speed * Time.fixedDeltaTime);
        Vector3 delta = next - prev;
        transform.position = next;

        if (delta.sqrMagnitude > 0.000001f)
        {
            foreach (var rb in _passengers)
            {
                if (rb != null)
                {
                    rb.position += new Vector2(delta.x, delta.y);
                }
            }
        }

        if ((next - target).sqrMagnitude <= 0.0004f)
        {
            _goingToEnd = !_goingToEnd;
            _waitTimer = waitAtEnds;
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.collider.CompareTag("Player") && collision.rigidbody != null)
        {
            _passengers.Add(collision.rigidbody);
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.rigidbody != null)
        {
            _passengers.Remove(collision.rigidbody);
        }
    }
}

/// <summary>
/// 플레이어(또는 여우의 그림자 분신 FoxShadowClone)가 밟고 있는 동안 작동하는 협동 압력판(Co-op Pressure Plate).
/// requiredPlayers = 2 로 설정하면 두 플레이어(또는 플레이어 + 여우 분신)가 동시에 밟아야 문이나 발판이 열린다.
/// </summary>
public class CoopPressurePlate : MonoBehaviour
{
    [Tooltip("작동에 필요한 인원 수 (1: 단독 또는 여우 분신 / 2: 2인 협동 또는 플레이어+분신)")]
    [Range(1, 2)] public int requiredCount = 1;
    [Tooltip("한 번 조건을 달성하면 발을 떼도 계속 켜져 있을지 여부")]
    public bool latchWhenActivated = false;

    [Header("연결 대상")]
    public GameObject[] activateOnTargets;
    public GameObject[] deactivateOnTargets;

    [Header("시각 표시")]
    public bool showCountLabel = true;
    public float labelHeight = 0.55f;

    private Collider2D _col;
    private SpriteRenderer _sr;
    private Vector3 _basePos;
    private bool _isPressed;
    private int _lastCount = -1;
    private Text _label;
    private CanvasGroup _labelGroup;

    private void Awake()
    {
        _col = GetComponent<Collider2D>();
        _sr = GetComponent<SpriteRenderer>();
        _basePos = transform.localPosition;
        if (showCountLabel) CreateWorldLabel();
    }

    private void Update()
    {
        int current = CountSteppingEntities();
        bool shouldPress = (latchWhenActivated && _isPressed) || (current >= requiredCount);

        if (shouldPress != _isPressed)
        {
            _isPressed = shouldPress;
            SfxPlayer.Lever(_isPressed);
            transform.localPosition = _isPressed ? _basePos + new Vector3(0f, -0.08f, 0f) : _basePos;
            if (_sr != null)
            {
                _sr.color = _isPressed ? new Color(0.55f, 1f, 0.68f, 1f) : Color.white;
            }
            LeverSwitch.ApplyTargets(activateOnTargets, deactivateOnTargets, _isPressed);
        }

        if (_label != null && current != _lastCount)
        {
            _lastCount = current;
            _label.text = $"{Mathf.Min(current, requiredCount)}/{requiredCount}";
            _label.color = _isPressed ? LobbyUITheme.GreenBtn : LobbyUITheme.Gold;
            if (_labelGroup != null) _labelGroup.alpha = (current > 0 || _isPressed) ? 1f : 0.75f;
        }
    }

    private int CountSteppingEntities()
    {
        if (_col == null) return 0;
        int count = 0;
        Bounds b = _col.bounds;
        b.Expand(0.15f);

        foreach (var p in PlayerMovement.ActivePlayers())
        {
            if (p == null) continue;
            var pc = p.GetComponent<Collider2D>();
            if (pc != null && b.Intersects(pc.bounds)) count++;
            else if (b.Contains(p.transform.position)) count++;
        }

        foreach (var clone in FoxShadowClone.ActiveClones)
        {
            if (clone == null) continue;
            if (b.Contains(clone.transform.position)) count++;
        }

        return count;
    }

    private void CreateWorldLabel()
    {
        float top = transform.position.y + 0.6f;
        if (_sr != null) top = _sr.bounds.max.y;
        else if (_col != null) top = _col.bounds.max.y;

        var go = new GameObject("PlateLabel", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        go.transform.SetParent(transform, true);
        go.transform.position = new Vector3(transform.position.x, top + labelHeight, 0f);
        go.transform.rotation = Quaternion.identity;
        Vector3 ls = transform.lossyScale;
        go.transform.localScale = new Vector3(0.01f / Mathf.Max(0.0001f, Mathf.Abs(ls.x)) * Mathf.Sign(ls.x),
                                              0.01f / Mathf.Max(0.0001f, Mathf.Abs(ls.y)) * Mathf.Sign(ls.y), 1f);

        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 200;
        ((RectTransform)go.transform).sizeDelta = new Vector2(180f, 54f);
        _labelGroup = go.GetComponent<CanvasGroup>();
        _labelGroup.alpha = 0.75f;

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Outline));
        textGo.transform.SetParent(go.transform, false);
        var rt = (RectTransform)textGo.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        _label = textGo.GetComponent<Text>();
        _label.font = LobbyUITheme.PixelFont;
        _label.fontSize = 42;
        _label.alignment = TextAnchor.MiddleCenter;
        _label.raycastTarget = false;
        _label.color = LobbyUITheme.Gold;

        var outline = textGo.GetComponent<Outline>();
        outline.effectColor = LobbyUITheme.GoldDeep;
        outline.effectDistance = new Vector2(2.5f, -2.5f);
    }
}

/// <summary>
/// 토끼의 스톰프(RabbitStomp, 공중 급강하 '쿵!') 충격파로 부술 수 있는 금이 간 바닥/벽 기믹 컴포넌트.
/// </summary>
public class BreakableCrackedBlock : MonoBehaviour
{
    public static readonly List<BreakableCrackedBlock> ActiveBlocks = new List<BreakableCrackedBlock>();

    [Tooltip("강화 스톰프(파워업 상태)로만 부술 수 있는지 여부")]
    public bool requirePoweredStomp = false;

    private Collider2D _col;
    private SpriteRenderer _sr;
    private bool _broken;

    private void OnEnable()
    {
        _col = GetComponent<Collider2D>();
        _sr = GetComponent<SpriteRenderer>();
        if (!ActiveBlocks.Contains(this)) ActiveBlocks.Add(this);
    }

    private void OnDisable()
    {
        ActiveBlocks.Remove(this);
    }

    public bool TryBreak(Vector2 stompOrigin, float radius, bool isPoweredStomp)
    {
        if (_broken || !gameObject.activeInHierarchy) return false;
        if (requirePoweredStomp && !isPoweredStomp) return false;

        float distSq;
        if (_col != null)
        {
            Vector2 closest = _col.ClosestPoint(stompOrigin);
            distSq = (closest - stompOrigin).sqrMagnitude;
        }
        else
        {
            distSq = ((Vector2)transform.position - stompOrigin).sqrMagnitude;
        }

        if (distSq > radius * radius) return false;

        _broken = true;
        SfxPlayer.BlockBreak();

        var fx = SkillFxLibrary.Instance;
        if (fx != null && fx.swapPuff != null && fx.swapPuff.Length > 0)
        {
            int sl = _sr != null ? _sr.sortingLayerID : 0;
            SpriteEffect.Spawn(fx.swapPuff, transform.position, fx.swapPuffFps * 1.2f, fx.swapPuffScale * 1.4f,
                false, sl, fx.sortingOrder + 1, new Color(0.85f, 0.75f, 0.62f, 1f));
        }

        gameObject.SetActive(false);
        return true;
    }
}

/// <summary>
/// 여우의 그림자 분신(FoxShadow) 스킬로 생성되는 분신 컴포넌트.
/// 제자리에 남아 협동 압력판(CoopPressurePlate)을 대신 밟아주거나 레버를 작동시키며,
/// 남은 지속시간 게이지와 신비로운 그림자 오라 연출을 표시한다.
/// </summary>
public class FoxShadowClone : MonoBehaviour
{
    public static readonly List<FoxShadowClone> ActiveClones = new List<FoxShadowClone>();

    private float _duration;
    private float _remaining;
    private SpriteRenderer _sr;
    private Image _timerFill;
    private CanvasGroup _group;
    private Vector3 _basePos;

    public static FoxShadowClone SpawnClone(PlayerMovement owner, Vector2 worldPos, bool facingRight, float duration, bool powered)
    {
        // 한 플레이어당 최대 1개의 활성 분신 유지 (새 분신 소환 시 이전 분신 교체)
        for (int i = ActiveClones.Count - 1; i >= 0; i--)
        {
            if (ActiveClones[i] != null && ActiveClones[i]._owner == owner)
            {
                Destroy(ActiveClones[i].gameObject);
                ActiveClones.RemoveAt(i);
            }
        }

        var go = new GameObject("FoxShadowClone");
        go.tag = "Player"; // 레버/압력판 트리거 호환
        go.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);

        var ownerSr = owner != null ? owner.GetComponent<SpriteRenderer>() : null;
        if (owner != null)
        {
            go.transform.localScale = owner.transform.localScale;
        }

        var sr = go.AddComponent<SpriteRenderer>();
        if (ownerSr != null)
        {
            sr.sprite = ownerSr.sprite;
            sr.sortingLayerID = ownerSr.sortingLayerID;
            sr.sortingOrder = ownerSr.sortingOrder - 1;
        }
        sr.flipX = !facingRight;
        sr.color = powered
            ? new Color(0.55f, 0.92f, 1f, 0.78f)
            : new Color(0.42f, 0.35f, 0.78f, 0.72f);

        // 물리 충돌 없이 트리거(레버/압력판)만 감지하도록 kinematic Rigidbody2D + Trigger Collider2D 부착
        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;

        var box = go.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = new Vector2(0.75f, 1.1f);

        var clone = go.AddComponent<FoxShadowClone>();
        clone.Init(owner, sr, duration, powered);
        return clone;
    }

    private PlayerMovement _owner;
    private bool _powered;

    private void Init(PlayerMovement owner, SpriteRenderer sr, float duration, bool powered)
    {
        _owner = owner;
        _sr = sr;
        _duration = Mathf.Max(0.5f, duration);
        _remaining = _duration;
        _powered = powered;
        _basePos = transform.position;

        if (!ActiveClones.Contains(this)) ActiveClones.Add(this);

        SfxPlayer.Shadow();
        var fx = SkillFxLibrary.Instance;
        if (fx != null && fx.swapPuff != null && fx.swapPuff.Length > 0)
        {
            int sl = _sr != null ? _sr.sortingLayerID : 0;
            SpriteEffect.Spawn(fx.swapPuff, transform.position, fx.swapPuffFps, fx.swapPuffScale * 1.1f,
                false, sl, fx.sortingOrder, new Color(0.55f, 0.45f, 0.95f, 0.9f));
        }

        BuildOverheadTimerUI();
    }

    private void BuildOverheadTimerUI()
    {
        var canvasGo = new GameObject("ShadowTimerCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        canvasGo.transform.SetParent(transform, false);
        canvasGo.transform.localPosition = new Vector3(0f, 1.15f, 0f);

        Vector3 ls = transform.lossyScale;
        canvasGo.transform.localScale = new Vector3(
            0.01f / Mathf.Max(0.0001f, Mathf.Abs(ls.x)) * Mathf.Sign(ls.x),
            0.01f / Mathf.Max(0.0001f, Mathf.Abs(ls.y)) * Mathf.Sign(ls.y),
            1f);

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 220;
        ((RectTransform)canvasGo.transform).sizeDelta = new Vector2(120f, 44f);
        _group = canvasGo.GetComponent<CanvasGroup>();

        var lblGo = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(Outline));
        lblGo.transform.SetParent(canvasGo.transform, false);
        var lrt = lblGo.GetComponent<RectTransform>();
        lrt.anchorMin = new Vector2(0f, 0.35f);
        lrt.anchorMax = new Vector2(1f, 1f);
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        var txt = lblGo.GetComponent<Text>();
        txt.font = LobbyUITheme.PlainFont;
        txt.fontSize = 18;
        txt.fontStyle = FontStyle.Bold;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = _powered ? new Color(1f, 0.92f, 0.38f, 1f) : new Color(0.75f, 0.85f, 1f, 1f);
        txt.text = _powered ? "★ 그림자 분신" : "그림자 분신";
        var ol = lblGo.GetComponent<Outline>();
        ol.effectColor = LobbyUITheme.GoldDeep;
        ol.effectDistance = new Vector2(1.5f, -1.5f);

        var barBg = new GameObject("BarBg", typeof(RectTransform), typeof(Image));
        barBg.transform.SetParent(canvasGo.transform, false);
        var brt = barBg.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0.1f, 0.08f);
        brt.anchorMax = new Vector2(0.9f, 0.28f);
        brt.offsetMin = brt.offsetMax = Vector2.zero;
        barBg.GetComponent<Image>().color = new Color(0.1f, 0.08f, 0.18f, 0.85f);

        var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGo.transform.SetParent(barBg.transform, false);
        var frt = fillGo.GetComponent<RectTransform>();
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = Vector2.one;
        frt.offsetMin = frt.offsetMax = Vector2.zero;
        _timerFill = fillGo.GetComponent<Image>();
        _timerFill.color = _powered ? new Color(1f, 0.84f, 0.25f, 1f) : new Color(0.55f, 0.78f, 1f, 1f);
    }

    private void Update()
    {
        _remaining -= Time.deltaTime;
        if (_remaining <= 0f)
        {
            var fx = SkillFxLibrary.Instance;
            if (fx != null && fx.swapPuff != null && fx.swapPuff.Length > 0)
            {
                int sl = _sr != null ? _sr.sortingLayerID : 0;
                SpriteEffect.Spawn(fx.swapPuff, transform.position, fx.swapPuffFps, fx.swapPuffScale * 0.9f,
                    false, sl, fx.sortingOrder, new Color(0.55f, 0.45f, 0.95f, 0.75f));
            }
            Destroy(gameObject);
            return;
        }

        float norm = Mathf.Clamp01(_remaining / _duration);
        if (_timerFill != null)
        {
            _timerFill.rectTransform.anchorMax = new Vector2(norm, 1f);
        }

        // 살짝 부유하는 그림자 연출
        float bob = Mathf.Sin(Time.time * 4.5f) * 0.05f;
        transform.position = _basePos + new Vector3(0f, bob, 0f);

        if (_sr != null && _remaining < 1.2f)
        {
            float blink = 0.35f + 0.35f * Mathf.Sin(Time.time * 18f);
            Color c = _sr.color;
            c.a = blink;
            _sr.color = c;
        }
    }

    private void OnDestroy()
    {
        ActiveClones.Remove(this);
    }
}

