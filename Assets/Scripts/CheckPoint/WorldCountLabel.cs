using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 체크포인트(Checkpoint), 스테이지 관문(EndPoint), 협동 압력판(CoopPressurePlate) 등에서
/// 오브젝트 상단에 월드 스페이스 "1/2" 카운트 라벨을 생성하는 공용 유틸리티.
/// </summary>
public static class WorldCountLabel
{
    public static void Create(
        Transform parent,
        string objectName,
        float topY,
        float heightOffset,
        Vector2 canvasSize,
        int fontSize,
        float outlineDist,
        float initialAlpha,
        out Text label,
        out CanvasGroup labelGroup)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        go.transform.SetParent(parent, true);
        go.transform.position = new Vector3(parent.position.x, topY + heightOffset, 0f);
        go.transform.rotation = Quaternion.identity;

        // 부모 스케일(좌우 뒤집힘 포함)과 무관하게 항상 일정한 크기로 보이게 보정 (1 월드 단위 = 100 캔버스 픽셀)
        Vector3 ls = parent.lossyScale;
        go.transform.localScale = new Vector3(
            0.01f / Mathf.Max(0.0001f, Mathf.Abs(ls.x)) * Mathf.Sign(ls.x),
            0.01f / Mathf.Max(0.0001f, Mathf.Abs(ls.y)) * Mathf.Sign(ls.y),
            1f);

        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 200; // 타일/캐릭터 위
        ((RectTransform)go.transform).sizeDelta = canvasSize;

        labelGroup = go.GetComponent<CanvasGroup>();
        labelGroup.alpha = initialAlpha;

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Outline));
        textGo.transform.SetParent(go.transform, false);
        var rt = (RectTransform)textGo.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        label = textGo.GetComponent<Text>();
        label.font = LobbyUITheme.PixelFont;
        label.fontSize = fontSize;
        label.alignment = TextAnchor.MiddleCenter;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.raycastTarget = false;
        label.color = LobbyUITheme.Gold;

        var outline = textGo.GetComponent<Outline>();
        outline.effectColor = LobbyUITheme.GoldDeep;
        outline.effectDistance = new Vector2(outlineDist, -outlineDist);
    }
}
