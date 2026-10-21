using System.Collections.Generic;
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
