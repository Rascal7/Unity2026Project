#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;

public static class SettingsMenuBuilder
{
    [MenuItem("Tools/UI/Create Settings Menu Canvas", false, 50)]
    public static void CreateSettingsMenuCanvas()
    {
        // 기존에 이미 씬에 SettingsMenuCanvas가 있다면 중복 방지를 위해 삭제
        GameObject oldCanvas = GameObject.Find("SettingsMenuCanvas");
        if (oldCanvas != null)
        {
            Undo.DestroyObjectImmediate(oldCanvas);
        }

        // 폰트 에셋 로드 (NotoSansKR-Bold SDF 자동 검색 및 적용)
        TMP_FontAsset defaultFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/NotoSansKR-Bold SDF.asset");
        if (defaultFont == null)
        {
            string[] guids = AssetDatabase.FindAssets("NotoSansKR-Bold SDF t:TMP_FontAsset");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                defaultFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            }
        }

        // 1. 최상위 Canvas 생성
        GameObject canvasGo = new GameObject("SettingsMenuCanvas");
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();
        SettingsMenuUI menuScript = canvasGo.AddComponent<SettingsMenuUI>();

        // 2. 전체 화면 딤(배경 어둡게) 생성
        GameObject dimGo = new GameObject("DimBackground");
        dimGo.transform.SetParent(canvasGo.transform, false);
        RectTransform dimRect = dimGo.AddComponent<RectTransform>();
        dimRect.anchorMin = Vector2.zero;
        dimRect.anchorMax = Vector2.one;
        dimRect.sizeDelta = Vector2.zero;
        Image dimImg = dimGo.AddComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, 0.65f);

        // 3. 메인 설정 패널 (스프라이트 적용 대상)
        GameObject panelGo = new GameObject("SettingsPanel");
        panelGo.transform.SetParent(dimGo.transform, false);
        RectTransform panelRect = panelGo.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(620, 520);
        Image panelImg = panelGo.AddComponent<Image>();
        panelImg.color = new Color(0.18f, 0.18f, 0.22f, 0.95f);

        menuScript.menuPanel = dimGo;

        // 4. 타이틀 텍스트
        CreateText(panelGo.transform, "TitleText", "게임 설정 (Settings)", new Vector2(0, 210), new Vector2(500, 50), 28, FontStyles.Bold, TextAlignmentOptions.Center, defaultFont);

        // ------------------------------------------
        // 5. 오디오 출력 섹션 (Output - 스피커/헤드폰)
        // ------------------------------------------
        CreateText(panelGo.transform, "OutputLabel", "🔊 오디오 출력 (스피커/헤드폰)", new Vector2(-15, 160), new Vector2(500, 30), 20, FontStyles.Bold, TextAlignmentOptions.Left, defaultFont);

        // 출력 장치 드롭다운 (드롭다운 1)
        TMP_Dropdown outDropdown = CreateDropdown(panelGo.transform, "OutputDeviceDropdown", new Vector2(0, 120), new Vector2(530, 34), defaultFont);
        menuScript.outputDeviceDropdown = outDropdown;

        // 마스터 볼륨 슬라이더
        var (sliderGo, sliderComp) = CreateSlider(panelGo.transform, "MasterVolumeSlider", new Vector2(-60, 75), new Vector2(380, 24));
        menuScript.masterVolumeSlider = sliderComp;

        TMP_Text volText = CreateText(panelGo.transform, "VolumeValueText", "100%", new Vector2(190, 75), new Vector2(90, 30), 18, FontStyles.Normal, TextAlignmentOptions.Left, defaultFont);
        menuScript.masterVolumeText = volText;

        // 구분선
        GameObject sep = new GameObject("Separator");
        sep.transform.SetParent(panelGo.transform, false);
        RectTransform sepRect = sep.AddComponent<RectTransform>();
        sepRect.anchoredPosition = new Vector2(0, 45);
        sepRect.sizeDelta = new Vector2(540, 2);
        Image sepImg = sep.AddComponent<Image>();
        sepImg.color = new Color(0.35f, 0.35f, 0.4f, 0.8f);

        // ------------------------------------------
        // 6. 오디오 입력 섹션 (Input - 마이크)
        // ------------------------------------------
        CreateText(panelGo.transform, "InputLabel", "🎙 오디오 입력 (마이크 장치)", new Vector2(-15, 20), new Vector2(500, 30), 20, FontStyles.Bold, TextAlignmentOptions.Left, defaultFont);

        // 마이크 장치 드롭다운 (드롭다운 2)
        TMP_Dropdown dropdown = CreateDropdown(panelGo.transform, "MicDropdown", new Vector2(0, -20), new Vector2(530, 34), defaultFont);
        menuScript.micDropdown = dropdown;

        // 마이크 감도 슬라이더
        CreateText(panelGo.transform, "MicSensLabel", "마이크 감도:", new Vector2(-195, -65), new Vector2(140, 30), 16, FontStyles.Normal, TextAlignmentOptions.Left, defaultFont);
        var (_, micSensSlider) = CreateSlider(panelGo.transform, "MicSensitivitySlider", new Vector2(0, -65), new Vector2(250, 24));
        menuScript.micSensitivitySlider = micSensSlider;

        TMP_Text micSensText = CreateText(panelGo.transform, "MicSensValueText", "100%", new Vector2(170, -65), new Vector2(70, 30), 16, FontStyles.Normal, TextAlignmentOptions.Left, defaultFont);
        menuScript.micSensitivityText = micSensText;

        // 마이크 입력 레벨 미터 바
        CreateText(panelGo.transform, "MeterLabel", "입력 레벨:", new Vector2(-195, -105), new Vector2(140, 30), 16, FontStyles.Normal, TextAlignmentOptions.Left, defaultFont);
        
        GameObject meterBg = new GameObject("MicMeterBg");
        meterBg.transform.SetParent(panelGo.transform, false);
        RectTransform meterBgRect = meterBg.AddComponent<RectTransform>();
        meterBgRect.anchoredPosition = new Vector2(0, -105);
        meterBgRect.sizeDelta = new Vector2(250, 18);
        Image meterBgImg = meterBg.AddComponent<Image>();
        meterBgImg.color = new Color(0.1f, 0.1f, 0.1f, 1f);

        GameObject meterFill = new GameObject("MicMeterFill");
        meterFill.transform.SetParent(meterBg.transform, false);
        RectTransform meterFillRect = meterFill.AddComponent<RectTransform>();
        meterFillRect.anchorMin = Vector2.zero;
        meterFillRect.anchorMax = Vector2.one;
        meterFillRect.sizeDelta = Vector2.zero;
        Image meterFillImg = meterFill.AddComponent<Image>();
        meterFillImg.color = new Color(0.2f, 0.85f, 0.3f, 1f);
        meterFillImg.type = Image.Type.Filled;
        meterFillImg.fillMethod = Image.FillMethod.Horizontal;
        meterFillImg.fillAmount = 0f;
        menuScript.micVolumeMeter = meterFillImg;

        // 마이크 테스트 버튼
        var (testBtnGo, testBtnComp, testBtnText) = CreateButton(panelGo.transform, "MicTestButton", "테스트", new Vector2(180, -105), new Vector2(90, 28), defaultFont);
        menuScript.micTestButton = testBtnComp;
        menuScript.micTestButtonText = testBtnText;

        // ------------------------------------------
        // 7. 하단 버튼 (계속하기 / 방 나가기)
        // ------------------------------------------
        var (_, resumeBtnComp, _) = CreateButton(panelGo.transform, "ResumeButton", "계속하기 (Resume)", new Vector2(-120, -195), new Vector2(210, 45), defaultFont);
        menuScript.resumeButton = resumeBtnComp;

        var (_, exitBtnComp, _) = CreateButton(panelGo.transform, "ExitButton", "방 나가기 (Exit)", new Vector2(120, -195), new Vector2(210, 45), defaultFont);
        menuScript.exitButton = exitBtnComp;

        // 생성 직후 메뉴 패널은 기본적으로 닫힌 상태로 설정
        dimGo.SetActive(false);

        // 프리팹 저장 (Assets/Prefebs 및 Assets/Resources 양쪽에 저장)
        string prefabPath = "Assets/Prefebs/SettingsMenuCanvas.prefab";
        if (!AssetDatabase.IsValidFolder("Assets/Prefebs"))
        {
            AssetDatabase.CreateFolder("Assets", "Prefebs");
        }
        PrefabUtility.SaveAsPrefabAssetAndConnect(canvasGo, prefabPath, InteractionMode.UserAction);

        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }
        PrefabUtility.SaveAsPrefabAsset(canvasGo, "Assets/Resources/SettingsMenuCanvas.prefab");
        Debug.Log($"<color=lime>[SettingsMenuBuilder]</color> 2개 드롭다운 설정 메뉴 Canvas 생성 완료: {prefabPath}");

        Selection.activeGameObject = canvasGo;
        Undo.RegisterCreatedObjectUndo(canvasGo, "Create Settings Menu Canvas");
    }

    private static TMP_Text CreateText(Transform parent, string name, string text, Vector2 pos, Vector2 size, float fontSize, FontStyles style, TextAlignmentOptions align, TMP_FontAsset font)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static (GameObject, Slider) CreateSlider(Transform parent, string name, Vector2 pos, Vector2 size)
    {
        GameObject sliderGo = new GameObject(name);
        sliderGo.transform.SetParent(parent, false);
        RectTransform sliderRt = sliderGo.AddComponent<RectTransform>();
        sliderRt.anchoredPosition = pos;
        sliderRt.sizeDelta = size;

        Slider slider = sliderGo.AddComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 1f;

        GameObject bg = new GameObject("Background");
        bg.transform.SetParent(sliderGo.transform, false);
        RectTransform bgRt = bg.AddComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.sizeDelta = Vector2.zero;
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.12f, 0.12f, 0.15f, 1f);

        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderGo.transform, false);
        RectTransform fillAreaRt = fillArea.AddComponent<RectTransform>();
        fillAreaRt.anchorMin = new Vector2(0f, 0.2f);
        fillAreaRt.anchorMax = new Vector2(1f, 0.8f);
        fillAreaRt.sizeDelta = new Vector2(-14, 0);

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        RectTransform fillRt = fill.AddComponent<RectTransform>();
        fillRt.sizeDelta = Vector2.zero;
        Image fillImg = fill.AddComponent<Image>();
        fillImg.color = new Color(0.25f, 0.6f, 0.95f, 1f);
        slider.fillRect = fillRt;

        GameObject handleArea = new GameObject("Handle Slide Area");
        handleArea.transform.SetParent(sliderGo.transform, false);
        RectTransform handleAreaRt = handleArea.AddComponent<RectTransform>();
        handleAreaRt.anchorMin = Vector2.zero;
        handleAreaRt.anchorMax = Vector2.one;
        handleAreaRt.sizeDelta = new Vector2(-14, 0);

        GameObject handle = new GameObject("Handle");
        handle.transform.SetParent(handleArea.transform, false);
        RectTransform handleRt = handle.AddComponent<RectTransform>();
        handleRt.sizeDelta = new Vector2(18, 0);
        Image handleImg = handle.AddComponent<Image>();
        handleImg.color = Color.white;
        slider.handleRect = handleRt;
        slider.targetGraphic = handleImg;

        return (sliderGo, slider);
    }

    private static (GameObject, Button, TMP_Text) CreateButton(Transform parent, string name, string text, Vector2 pos, Vector2 size, TMP_FontAsset font)
    {
        GameObject btnGo = new GameObject(name);
        btnGo.transform.SetParent(parent, false);
        RectTransform btnRt = btnGo.AddComponent<RectTransform>();
        btnRt.anchoredPosition = pos;
        btnRt.sizeDelta = size;

        Image btnImg = btnGo.AddComponent<Image>();
        btnImg.color = new Color(0.28f, 0.35f, 0.45f, 1f);

        Button btn = btnGo.AddComponent<Button>();
        btn.targetGraphic = btnImg;

        ColorBlock colors = btn.colors;
        colors.normalColor = new Color(0.28f, 0.35f, 0.45f, 1f);
        colors.highlightedColor = new Color(0.38f, 0.48f, 0.62f, 1f);
        colors.pressedColor = new Color(0.18f, 0.24f, 0.32f, 1f);
        btn.colors = colors;

        TMP_Text tmp = CreateText(btnGo.transform, "Text", text, Vector2.zero, size, 16, FontStyles.Bold, TextAlignmentOptions.Center, font);
        return (btnGo, btn, tmp);
    }

    private static TMP_Dropdown CreateDropdown(Transform parent, string name, Vector2 pos, Vector2 size, TMP_FontAsset font)
    {
        GameObject dropGo = new GameObject(name);
        dropGo.transform.SetParent(parent, false);
        RectTransform dropRt = dropGo.AddComponent<RectTransform>();
        dropRt.anchoredPosition = pos;
        dropRt.sizeDelta = size;

        Image dropImg = dropGo.AddComponent<Image>();
        dropImg.color = new Color(0.12f, 0.12f, 0.16f, 1f);

        TMP_Dropdown dropdown = dropGo.AddComponent<TMP_Dropdown>();
        dropdown.targetGraphic = dropImg;

        // Label
        GameObject labelGo = new GameObject("Label");
        labelGo.transform.SetParent(dropGo.transform, false);
        RectTransform labelRt = labelGo.AddComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.anchoredPosition = new Vector2(-15, 0);
        labelRt.sizeDelta = new Vector2(-40, 0);
        TextMeshProUGUI labelText = labelGo.AddComponent<TextMeshProUGUI>();
        if (font != null) labelText.font = font;
        labelText.fontSize = 15;
        labelText.alignment = TextAlignmentOptions.Left;
        labelText.color = Color.white;
        labelText.raycastTarget = false;
        dropdown.captionText = labelText;

        // Arrow
        GameObject arrowGo = new GameObject("Arrow");
        arrowGo.transform.SetParent(dropGo.transform, false);
        RectTransform arrowRt = arrowGo.AddComponent<RectTransform>();
        arrowRt.anchorMin = new Vector2(1, 0.5f);
        arrowRt.anchorMax = new Vector2(1, 0.5f);
        arrowRt.anchoredPosition = new Vector2(-18, 0);
        arrowRt.sizeDelta = new Vector2(20, 20);
        TextMeshProUGUI arrowText = arrowGo.AddComponent<TextMeshProUGUI>();
        if (font != null) arrowText.font = font;
        arrowText.text = "▼";
        arrowText.fontSize = 14;
        arrowText.alignment = TextAlignmentOptions.Center;
        arrowText.color = Color.gray;
        arrowText.raycastTarget = false;

        // Template
        GameObject template = new GameObject("Template");
        template.transform.SetParent(dropGo.transform, false);
        RectTransform templateRt = template.AddComponent<RectTransform>();
        templateRt.anchorMin = new Vector2(0, 0);
        templateRt.anchorMax = new Vector2(1, 0);
        templateRt.pivot = new Vector2(0.5f, 1f);
        templateRt.anchoredPosition = new Vector2(0, -2);
        templateRt.sizeDelta = new Vector2(0, 150);
        Image templateImg = template.AddComponent<Image>();
        templateImg.color = new Color(0.15f, 0.15f, 0.2f, 1f);

        ScrollRect scrollRect = template.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        // Viewport
        GameObject viewport = new GameObject("Viewport");
        viewport.transform.SetParent(template.transform, false);
        RectTransform viewportRt = viewport.AddComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.sizeDelta = Vector2.zero;
        viewport.AddComponent<Mask>().showMaskGraphic = false;
        Image viewportImg = viewport.AddComponent<Image>();
        viewportImg.color = Color.white;
        scrollRect.viewport = viewportRt;

        // Content
        GameObject content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        RectTransform contentRt = content.AddComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0, 1);
        contentRt.anchorMax = new Vector2(1, 1);
        contentRt.pivot = new Vector2(0.5f, 1);
        contentRt.sizeDelta = new Vector2(0, 28);
        scrollRect.content = contentRt;

        // Item
        GameObject item = new GameObject("Item");
        item.transform.SetParent(content.transform, false);
        RectTransform itemRt = item.AddComponent<RectTransform>();
        itemRt.anchorMin = new Vector2(0, 0.5f);
        itemRt.anchorMax = new Vector2(1, 0.5f);
        itemRt.sizeDelta = new Vector2(0, 28);

        Toggle itemToggle = item.AddComponent<Toggle>();

        // Item Label
        GameObject itemLabel = new GameObject("Item Label");
        itemLabel.transform.SetParent(item.transform, false);
        RectTransform itemLabelRt = itemLabel.AddComponent<RectTransform>();
        itemLabelRt.anchorMin = Vector2.zero;
        itemLabelRt.anchorMax = Vector2.one;
        itemLabelRt.sizeDelta = new Vector2(-20, 0);
        TextMeshProUGUI itemLabelText = itemLabel.AddComponent<TextMeshProUGUI>();
        if (font != null) itemLabelText.font = font;
        itemLabelText.fontSize = 14;
        itemLabelText.alignment = TextAlignmentOptions.Left;
        itemLabelText.color = Color.white;

        dropdown.template = templateRt;
        dropdown.itemText = itemLabelText;
        template.SetActive(false);

        return dropdown;
    }
}
#endif
