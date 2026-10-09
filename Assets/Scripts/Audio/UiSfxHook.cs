using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 개별 UI 요소에 붙는 효과음 훅 (UiSfx 가 자동으로 붙인다).
public class UiSfxHook : MonoBehaviour, IPointerEnterHandler, IPointerDownHandler, IPointerUpHandler
{
    private const float SliderTickInterval = 0.06f;
    private static readonly string[] CancelWords =
        { "취소", "닫기", "아니", "로비", "뒤로", "나가", "back", "cancel", "close", "exit", "no" };

    private Selectable _sel;
    private bool _dragging;
    private float _lastTick;

    private void Awake()
    {
        _sel = GetComponent<Selectable>();
        if (_sel is Button b) b.onClick.AddListener(OnButtonClick);
        else if (_sel is Toggle t) t.onValueChanged.AddListener(_ => { if (IsUserInput()) UiSfx.Click(); });
        else if (_sel is Slider s) s.onValueChanged.AddListener(OnSliderChanged);
        // TMP_Dropdown: 펼칠 때(OnPointerDown) 소리. 항목 선택은 항목(Toggle)의 훅이 낸다.
    }

    public void OnPointerEnter(PointerEventData e)
    {
        if (_sel != null && _sel.IsInteractable()) UiSfx.Hover();
    }

    public void OnPointerDown(PointerEventData e)
    {
        if (_sel is Slider) _dragging = true;
        else if (_sel is TMP_Dropdown && _sel.IsInteractable()) UiSfx.Click(); // 목록 펼치기
    }

    public void OnPointerUp(PointerEventData e) => _dragging = false;

    private void OnButtonClick()
    {
        if (IsTab()) UiSfx.Tab();
        else if (IsCancel()) UiSfx.Cancel();
        else UiSfx.Click();
    }

    private void OnSliderChanged(float _)
    {
        // 코드로 값을 넣을 때(설정 불러오기 등)는 무음, 사용자가 끌 때만 짧은 틱
        if (!_dragging || Time.unscaledTime - _lastTick < SliderTickInterval) return;
        _lastTick = Time.unscaledTime;
        UiSfx.SliderTick();
    }

    private bool IsUserInput()
    {
        var es = EventSystem.current;
        return es != null && es.currentSelectedGameObject == gameObject;
    }

    private bool IsTab() => name.IndexOf("Tab", System.StringComparison.OrdinalIgnoreCase) >= 0;

    private bool IsCancel()
    {
        string label = name;
        var tmp = GetComponentInChildren<TMP_Text>(true);
        if (tmp != null) label += " " + tmp.text;
        var txt = GetComponentInChildren<Text>(true);
        if (txt != null) label += " " + txt.text;
        label = label.ToLowerInvariant();
        foreach (var w in CancelWords)
        {
            // 영어 "no" 는 단어 단위로만 (예: "Notice" 오탐 방지)
            if (w == "no") { if (System.Text.RegularExpressions.Regex.IsMatch(label, @"\bno\b")) return true; }
            else if (label.Contains(w)) return true;
        }
        return false;
    }
}
