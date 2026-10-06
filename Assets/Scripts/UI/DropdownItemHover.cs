using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// 커스텀 드롭다운(TMP_Dropdown) 항목의 실제 마우스 호버 상태 표시.
/// - 템플릿 Item 에 부착하면 각 항목으로 복제되어도 자기 자식만 제어한다
///   (자식 참조는 Instantiate 시 복제본으로 리매핑됨).
/// - 호버: 좌측 골드 픽셀 바 표시 + 글씨 밝게. 벗어나면 원복.
/// - 선택(현재 값) 상태는 Toggle.graphic(우측 골드 점)으로 별도 표시되어 호버와 구분된다.
/// </summary>
public class DropdownItemHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public GameObject hoverBar;   // 좌측 골드 강조 바
    public TMP_Text label;        // 항목 텍스트
    public Color32 normalColor = new Color32(0xE8, 0xE4, 0xD8, 0xFF); // 메인 텍스트
    public Color32 hoverColor  = new Color32(0xFF, 0xE6, 0xA0, 0xFF); // 하이라이트 골드

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (hoverBar != null) hoverBar.SetActive(true);
        if (label != null) label.color = hoverColor;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (hoverBar != null) hoverBar.SetActive(false);
        if (label != null) label.color = normalColor;
    }
}
