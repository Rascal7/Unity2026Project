using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로비(TitleScreen)와 동일한 픽셀 아트 우드/골드 테마를 모든 UI에 일관되게 적용하는 공용 헬퍼 클래스.
/// </summary>
public static class LobbyUITheme
{
    // ================= [ 컬러 팔레트 ] =================
    public static readonly Color WoodFill  = new Color(0.42f, 0.28f, 0.16f, 1f);  // 메인 우드 면 (#6B4729)
    public static readonly Color WoodDark  = new Color(0.19f, 0.11f, 0.05f, 1f);  // 어두운 외곽선 (#301C0D)
    public static readonly Color WoodLight = new Color(0.60f, 0.43f, 0.24f, 1f);  // 밝은 베벨 (#996E3D)
    public static readonly Color Gold      = new Color(0.98f, 0.82f, 0.42f, 1f);  // 골드 (#FAD16B)
    public static readonly Color GoldDeep  = new Color(0.13f, 0.08f, 0.02f, 1f);  // 딥 골드 그림자 (#211405)
    public static readonly Color Stone     = new Color(0.60f, 0.64f, 0.68f, 1f);  // 스톤 그레이 (#99A3AD)
    public static readonly Color StoneDark = new Color(0.13f, 0.13f, 0.15f, 1f);  // 스톤 그림자 (#212126)
    public static readonly Color Parch     = new Color(0.98f, 0.95f, 0.89f, 1f);  // 양피지 크림 (#FAF2E3)
    public static readonly Color FieldBg   = new Color(0.15f, 0.11f, 0.07f, 1f);  // 인셋 다크 배경 (#261C12)
    public static readonly Color Hint      = new Color(0.72f, 0.66f, 0.58f, 1f);  // 힌트 그레이
    public static readonly Color Ink       = new Color(0.12f, 0.07f, 0.02f, 1f);  // 잉크 블랙 (#1F1205)

    public static readonly Color GreenBtn  = new Color(0.32f, 0.74f, 0.32f, 1f);  // 준비/확인 그린
    public static readonly Color GreenDark = new Color(0.10f, 0.28f, 0.10f, 1f);  // 그린 그림자
    public static readonly Color RedBtn    = new Color(0.82f, 0.26f, 0.22f, 1f);  // 삭제/취소 레드
    public static readonly Color RedDark   = new Color(0.32f, 0.08f, 0.06f, 1f);  // 레드 그림자

    public enum ButtonType
    {
        Gold,
        Stone,
        Green,
        Red
    }

    private static Font _pixelFont;
    private static Font _plainFont;
    private static Texture2D _bgTexture;

    public static Font PixelFont
    {
        get
        {
            if (_pixelFont == null) _pixelFont = Resources.Load<Font>("BoldPixels");
            if (_pixelFont == null) _pixelFont = PlainFont;
            return _pixelFont;
        }
    }

    public static Font PlainFont
    {
        get
        {
            if (_plainFont == null) _plainFont = Resources.Load<Font>("Sunflower-Medium");
            if (_plainFont == null) _plainFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return _plainFont;
        }
    }

    public static Texture2D BackgroundTexture
    {
        get
        {
            if (_bgTexture == null) _bgTexture = Resources.Load<Texture2D>("Title/back");
            return _bgTexture;
        }
    }

    // ================= [ 전체 배경 적용 ] =================
    public static GameObject ApplyBackground(Transform canvasTransform)
    {
        if (canvasTransform == null) return null;

        Transform existing = canvasTransform.Find("LobbyThemeBackground");
        GameObject bgObj;
        if (existing != null)
        {
            bgObj = existing.gameObject;
        }
        else
        {
            bgObj = new GameObject("LobbyThemeBackground", typeof(RectTransform));
            bgObj.transform.SetParent(canvasTransform, false);
            bgObj.transform.SetAsFirstSibling();
        }

        var rt = bgObj.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var raw = bgObj.GetComponent<RawImage>();
        if (raw == null) raw = bgObj.AddComponent<RawImage>();
        raw.texture = BackgroundTexture;
        raw.color = Color.white;
        raw.raycastTarget = false;

        return bgObj;
    }

    // ================= [ 우드 패널 프레임 스타일링 ] =================
    public static void StyleWoodPanel(GameObject go, Color? fillColor = null, float padBorder = 6f, float padBevel = 6f)
    {
        if (go == null) return;

        Color fill = fillColor ?? WoodFill;

        var baseImg = go.GetComponent<Image>();
        if (baseImg == null) baseImg = go.AddComponent<Image>();
        baseImg.color = WoodDark;
        baseImg.raycastTarget = false;

        // Bevel 하이라이트
        Transform bevelTrans = go.transform.Find("_Bevel");
        GameObject bevelObj;
        if (bevelTrans == null)
        {
            bevelObj = new GameObject("_Bevel", typeof(RectTransform), typeof(Image));
            bevelObj.transform.SetParent(go.transform, false);
            bevelObj.transform.SetAsFirstSibling();
        }
        else bevelObj = bevelTrans.gameObject;

        var bevelRt = bevelObj.GetComponent<RectTransform>();
        bevelRt.anchorMin = Vector2.zero;
        bevelRt.anchorMax = Vector2.one;
        bevelRt.offsetMin = new Vector2(padBorder, padBorder);
        bevelRt.offsetMax = new Vector2(-padBorder, -padBorder);
        var bevelImg = bevelObj.GetComponent<Image>();
        bevelImg.color = WoodLight;
        bevelImg.raycastTarget = false;

        // Face 내부 면
        Transform faceTrans = bevelObj.transform.Find("_Face");
        GameObject faceObj;
        if (faceTrans == null)
        {
            faceObj = new GameObject("_Face", typeof(RectTransform), typeof(Image));
            faceObj.transform.SetParent(bevelObj.transform, false);
            faceObj.transform.SetAsFirstSibling();
        }
        else faceObj = faceTrans.gameObject;

        var faceRt = faceObj.GetComponent<RectTransform>();
        faceRt.anchorMin = Vector2.zero;
        faceRt.anchorMax = Vector2.one;
        float innerPad = padBevel;
        faceRt.offsetMin = new Vector2(innerPad, innerPad);
        faceRt.offsetMax = new Vector2(-innerPad, -innerPad);
        var faceImg = faceObj.GetComponent<Image>();
        faceImg.color = fill;
        faceImg.raycastTarget = false;
    }

    // ================= [ 속이 빈 테두리 하이라이트 (Hollow Border) ] =================
    /// <summary>
    /// 대상 하이라이트 오브젝트를 4면 테두리(Top, Bottom, Left, Right)로 구성하여,
    /// 카드 내부 콘텐츠(캐릭터, 이름, 버튼 등)를 가리지 않고 외곽 테두리만 빛나게 만듭니다.
    /// </summary>
    public static void SetupHollowOutline(GameObject outlineGo, Color borderColor, float thickness = 6f)
    {
        if (outlineGo == null) return;

        // 루트 오브젝트 Image가 전체 면을 채우지 않도록 투명 처리
        var rootImg = outlineGo.GetComponent<Image>();
        if (rootImg != null)
        {
            rootImg.color = Color.clear;
            rootImg.raycastTarget = false;
        }

        var rt = outlineGo.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-4f, -4f);
            rt.offsetMax = new Vector2(4f, 4f);
        }

        // 상, 하, 좌, 우 4방향 테두리 생성 및 색상 설정
        CreateBorderEdge(outlineGo.transform, "_BorderTop", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -thickness), Vector2.zero, borderColor);
        CreateBorderEdge(outlineGo.transform, "_BorderBottom", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, thickness), borderColor);
        CreateBorderEdge(outlineGo.transform, "_BorderLeft", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(thickness, 0f), borderColor);
        CreateBorderEdge(outlineGo.transform, "_BorderRight", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-thickness, 0f), Vector2.zero, borderColor);
    }

    private static void CreateBorderEdge(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
    {
        Transform edgeTrans = parent.Find(name);
        GameObject edgeGo;
        if (edgeTrans == null)
        {
            edgeGo = new GameObject(name, typeof(RectTransform), typeof(Image));
            edgeGo.transform.SetParent(parent, false);
        }
        else edgeGo = edgeTrans.gameObject;

        var rt = edgeGo.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;

        var img = edgeGo.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
    }

    // ================= [ 다크 인셋 박스 ] =================
    public static void StyleInsetBox(GameObject go, Color? innerColor = null, Color? borderColor = null, float pad = 3f)
    {
        if (go == null) return;

        Color border = borderColor ?? WoodDark;
        Color inner = innerColor ?? FieldBg;

        var borderImg = go.GetComponent<Image>();
        if (borderImg == null) borderImg = go.AddComponent<Image>();
        borderImg.color = border;
        borderImg.raycastTarget = false;

        Transform insetTrans = go.transform.Find("_InsetFace");
        GameObject insetObj;
        if (insetTrans == null)
        {
            insetObj = new GameObject("_InsetFace", typeof(RectTransform), typeof(Image));
            insetObj.transform.SetParent(go.transform, false);
            insetObj.transform.SetAsFirstSibling();
        }
        else insetObj = insetTrans.gameObject;

        var insetRt = insetObj.GetComponent<RectTransform>();
        insetRt.anchorMin = Vector2.zero;
        insetRt.anchorMax = Vector2.one;
        insetRt.offsetMin = new Vector2(pad, pad);
        insetRt.offsetMax = new Vector2(-pad, -pad);

        var insetImg = insetObj.GetComponent<Image>();
        insetImg.color = inner;
        insetImg.raycastTarget = false;
    }

    // ================= [ 3D 픽셀 버튼 스타일링 ] =================
    public static void StylePixelButton(Button btn, ButtonType type, string label = null, int? fontSize = null)
    {
        if (btn == null) return;

        Color faceColor;
        Color shadowColor;
        Color textColor;

        switch (type)
        {
            case ButtonType.Gold:
                faceColor = Gold;
                shadowColor = GoldDeep;
                textColor = Ink;
                break;
            case ButtonType.Green:
                faceColor = GreenBtn;
                shadowColor = GreenDark;
                textColor = Parch;
                break;
            case ButtonType.Red:
                faceColor = RedBtn;
                shadowColor = RedDark;
                textColor = Parch;
                break;
            case ButtonType.Stone:
            default:
                faceColor = Stone;
                shadowColor = StoneDark;
                textColor = Ink;
                break;
        }

        StylePixelButtonCustom(btn, faceColor, shadowColor, textColor, label, fontSize);
    }

    public static void StylePixelButtonCustom(Button btn, Color faceColor, Color shadowColor, Color textColor, string label = null, int? fontSize = null)
    {
        if (btn == null) return;

        // 투명 클릭 영역 및 바닥 그림자 레이어
        var hitImg = btn.GetComponent<Image>();
        if (hitImg == null) hitImg = btn.gameObject.AddComponent<Image>();
        hitImg.color = shadowColor;
        hitImg.raycastTarget = true;

        // Face 오브젝트 (살짝 위로 떠서 3D 버튼 입체감을 주는 레이어)
        Transform faceTrans = btn.transform.Find("_BtnFace");
        GameObject faceObj;
        if (faceTrans == null)
        {
            faceObj = new GameObject("_BtnFace", typeof(RectTransform), typeof(Image));
            faceObj.transform.SetParent(btn.transform, false);
            faceObj.transform.SetAsFirstSibling();
        }
        else faceObj = faceTrans.gameObject;

        var faceRt = faceObj.GetComponent<RectTransform>();
        faceRt.anchorMin = Vector2.zero;
        faceRt.anchorMax = Vector2.one;
        faceRt.offsetMin = new Vector2(0f, 6f);
        faceRt.offsetMax = new Vector2(0f, 0f);

        var faceImg = faceObj.GetComponent<Image>();
        faceImg.color = faceColor;
        faceImg.raycastTarget = false;

        btn.targetGraphic = faceImg;

        // 버튼 색상 전환 설정
        var cb = btn.colors;
        cb.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        cb.pressedColor = new Color(0.84f, 0.84f, 0.84f, 1f);
        cb.disabledColor = new Color(0.48f, 0.48f, 0.48f, 0.6f);
        cb.fadeDuration = 0.05f;
        btn.colors = cb;

        // 텍스트 컴포넌트: faceObj 아래에 확실하게 존재하도록 보장
        Text legacyText = faceObj.GetComponentInChildren<Text>(true);
        TextMeshProUGUI tmpText = faceObj.GetComponentInChildren<TextMeshProUGUI>(true);

        if (legacyText == null && tmpText == null)
        {
            legacyText = btn.GetComponentInChildren<Text>(true);
            if (legacyText != null)
            {
                legacyText.transform.SetParent(faceObj.transform, false);
            }
            else
            {
                tmpText = btn.GetComponentInChildren<TextMeshProUGUI>(true);
                if (tmpText != null)
                {
                    tmpText.transform.SetParent(faceObj.transform, false);
                }
            }
        }

        // 그래도 없으면 새 Text 컴포넌트 자동 생성
        if (legacyText == null && tmpText == null)
        {
            var textGo = new GameObject("BtnText", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(faceObj.transform, false);
            legacyText = textGo.GetComponent<Text>();
        }

        string textContent = label;

        if (legacyText != null)
        {
            var trt = legacyText.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;

            if (textContent == null) textContent = legacyText.text;
            if (string.IsNullOrEmpty(textContent)) textContent = label ?? "";

            bool isAscii = IsAllAscii(textContent);
            legacyText.font = isAscii ? PixelFont : PlainFont;
            legacyText.color = textColor;
            legacyText.alignment = TextAnchor.MiddleCenter;
            legacyText.horizontalOverflow = HorizontalWrapMode.Overflow;
            legacyText.verticalOverflow = VerticalWrapMode.Overflow;
            if (fontSize.HasValue) legacyText.fontSize = fontSize.Value;
            legacyText.text = textContent;
            legacyText.raycastTarget = false;

            if (textColor == Parch || textColor == Color.white)
            {
                var outl = legacyText.GetComponent<Outline>();
                if (outl == null) outl = legacyText.gameObject.AddComponent<Outline>();
                outl.effectColor = new Color(0.08f, 0.08f, 0.08f, 0.85f);
                outl.effectDistance = new Vector2(2f, -2f);
            }
        }

        if (tmpText != null)
        {
            var badOutline = tmpText.GetComponent<Outline>();
            if (badOutline != null)
            {
                if (Application.isPlaying) Object.Destroy(badOutline);
                else Object.DestroyImmediate(badOutline);
            }

            var trt = tmpText.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;

            if (textContent == null) textContent = tmpText.text;
            if (string.IsNullOrEmpty(textContent)) textContent = label ?? "";

            tmpText.color = textColor;
            tmpText.fontStyle = FontStyles.Bold;
            tmpText.alignment = TextAlignmentOptions.Center;
            if (fontSize.HasValue) tmpText.fontSize = fontSize.Value;
            tmpText.text = textContent;
            tmpText.raycastTarget = false;
        }
    }

    // ================= [ 타이틀 / 서브타이틀 스타일링 ] =================
    public static void StyleTitle(TextMeshProUGUI tmp, string text = null, float fontSize = 56f)
    {
        if (tmp == null) return;
        if (text != null) tmp.text = text;

        var badOutline = tmp.GetComponent<Outline>();
        if (badOutline != null)
        {
            if (Application.isPlaying) Object.Destroy(badOutline);
            else Object.DestroyImmediate(badOutline);
        }

        tmp.fontSize = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Gold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableVertexGradient = false;
        ApplyOutlineInstance(tmp, 0.22f, GoldDeep);
    }

    // TMP 의 tmp.outlineWidth 는 '공유 폰트 머티리얼'을 수정해 같은 폰트의 다른 텍스트(버튼·이름 등)까지
    // 아웃라인이 입혀져 획이 겹쳐 보이게 만든다. 반드시 이 텍스트 '전용 머티리얼 인스턴스'에만 적용한다.
    private static void ApplyOutlineInstance(TextMeshProUGUI tmp, float width, Color color)
    {
        if (tmp == null || tmp.fontSharedMaterial == null) return;
        var mat = new Material(tmp.fontSharedMaterial);
        mat.EnableKeyword(TMPro.ShaderUtilities.Keyword_Outline);
        mat.SetColor(TMPro.ShaderUtilities.ID_OutlineColor, color);
        mat.SetFloat(TMPro.ShaderUtilities.ID_OutlineWidth, width);
        tmp.fontMaterial = mat;
        tmp.UpdateMeshPadding();
    }

    public static void StyleSubtitle(TextMeshProUGUI tmp, string text = null, float fontSize = 24f)
    {
        if (tmp == null) return;
        if (text != null) tmp.text = text;

        var badOutline = tmp.GetComponent<Outline>();
        if (badOutline != null)
        {
            if (Application.isPlaying) Object.Destroy(badOutline);
            else Object.DestroyImmediate(badOutline);
        }

        tmp.fontSize = fontSize;
        tmp.color = Parch;
        tmp.alignment = TextAlignmentOptions.Center;
        ApplyOutlineInstance(tmp, 0.18f, new Color(0.10f, 0.06f, 0.02f, 0.85f));
    }

    public static bool IsAllAscii(string s)
    {
        if (string.IsNullOrEmpty(s)) return true;
        foreach (char c in s)
        {
            if (c > 127) return false;
        }
        return true;
    }
}
