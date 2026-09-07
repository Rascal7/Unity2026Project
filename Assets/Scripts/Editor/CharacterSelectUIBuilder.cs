#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

public static class CharacterSelectUIBuilder
{
    [MenuItem("Tools/UI/Build Character Select UI", false, 60)]
    public static void BuildCharacterSelectUI()
    {
        // 1. 기존 씬 내 Canvas 탐색 및 제거/대체 확인
        GameObject oldCanvas = GameObject.Find("Canvas");
        if (oldCanvas != null)
        {
            Undo.DestroyObjectImmediate(oldCanvas);
        }

        // 폰트 에셋 로드 (NotoSansKR-Bold SDF 또는 Sunflower)
        TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/NotoSansKR-Bold SDF.asset");
        if (fontAsset == null)
        {
            fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Sunflower-Bold SDF.asset");
        }

        // 스프라이트 에셋 로드
        Sprite btnMidlBlue = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Buttons/PNG/11Button_Midl_Blue.png");
        Sprite btnMidlYellow = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Buttons/PNG/10Button_Midl_Yellow.png");
        Sprite btnLongBlue = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Buttons/PNG/12Button_Long_Blue.png");
        Sprite btnLongGreen = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Buttons/PNG/13Button_Long_Green.png");

        Sprite cardBgSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/kenney_pixel-ui-pack/9-Slice/space_inlay.png");
        if (cardBgSprite == null)
            cardBgSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/kenney_pixel-ui-pack/9-Slice/Colored/grey.png");

        Sprite outlineYellow = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/kenney_pixel-ui-pack/9-Slice/Outline/yellow.png");
        Sprite foxIcon = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprite/Player1/player-idle-1.png");
        Sprite rabbitIcon = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprite/Player2/player-idle-1.png");

        // 2. 최상위 Canvas 생성
        GameObject canvasGo = new GameObject("Canvas");
        Undo.RegisterCreatedObjectUndo(canvasGo, "Build Character Select UI");

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();
        CharacterSelectUI uiScript = canvasGo.AddComponent<CharacterSelectUI>();

        // 3. 타이틀 텍스트
        GameObject titleGo = new GameObject("TitleText");
        titleGo.transform.SetParent(canvasGo.transform, false);
        RectTransform titleRect = titleGo.AddComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 1f);
        titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -60f);
        titleRect.sizeDelta = new Vector2(800f, 100f);

        TextMeshProUGUI titleText = titleGo.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null) titleText.font = fontAsset;
        titleText.text = "캐릭터 선택";
        titleText.fontSize = 54;
        titleText.fontStyle = FontStyles.Bold;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.color = new Color(1f, 0.95f, 0.8f, 1f);

        // 4. 슬롯 컨테이너
        GameObject slotsContainer = new GameObject("SlotsContainer");
        slotsContainer.transform.SetParent(canvasGo.transform, false);
        RectTransform slotsRect = slotsContainer.AddComponent<RectTransform>();
        slotsRect.anchorMin = new Vector2(0.5f, 0.5f);
        slotsRect.anchorMax = new Vector2(0.5f, 0.5f);
        slotsRect.pivot = new Vector2(0.5f, 0.5f);
        slotsRect.anchoredPosition = new Vector2(0f, 30f);
        slotsRect.sizeDelta = new Vector2(1200f, 600f);

        // --- 카드 1: 여우 (Fox) ---
        var foxCard = CreateCharacterCard(
            parent: slotsContainer.transform,
            name: "Card_Fox",
            posX: -340f,
            title: "여우 (Fox)",
            skill: "스킬: 질주 대시 [Shift]",
            iconSprite: foxIcon,
            cardBgSprite: cardBgSprite,
            btnSprite: btnMidlBlue,
            outlineSprite: outlineYellow,
            fontAsset: fontAsset
        );

        // --- 카드 2: 토끼 (Rabbit) ---
        var rabbitCard = CreateCharacterCard(
            parent: slotsContainer.transform,
            name: "Card_Rabbit",
            posX: 340f,
            title: "토끼 (Rabbit)",
            skill: "스킬: 슈퍼 점프 [Shift]",
            iconSprite: rabbitIcon,
            cardBgSprite: cardBgSprite,
            btnSprite: btnMidlBlue,
            outlineSprite: outlineYellow,
            fontAsset: fontAsset
        );

        // 5. 하단 READY 버튼
        GameObject readyBtnGo = new GameObject("ReadyButton");
        readyBtnGo.transform.SetParent(canvasGo.transform, false);
        RectTransform readyRect = readyBtnGo.AddComponent<RectTransform>();
        readyRect.anchorMin = new Vector2(0.5f, 0f);
        readyRect.anchorMax = new Vector2(0.5f, 0f);
        readyRect.pivot = new Vector2(0.5f, 0f);
        readyRect.anchoredPosition = new Vector2(0f, 80f);
        readyRect.sizeDelta = new Vector2(380f, 90f);

        Image readyImg = readyBtnGo.AddComponent<Image>();
        if (btnLongBlue != null)
        {
            readyImg.sprite = btnLongBlue;
            readyImg.type = Image.Type.Simple;
        }

        Button readyBtn = readyBtnGo.AddComponent<Button>();
        readyBtn.targetGraphic = readyImg;

        // Ready 텍스트
        GameObject readyTextGo = new GameObject("Text");
        readyTextGo.transform.SetParent(readyBtnGo.transform, false);
        RectTransform readyTextRect = readyTextGo.AddComponent<RectTransform>();
        readyTextRect.anchorMin = Vector2.zero;
        readyTextRect.anchorMax = Vector2.one;
        readyTextRect.sizeDelta = Vector2.zero;

        TextMeshProUGUI readyTmp = readyTextGo.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null) readyTmp.font = fontAsset;
        readyTmp.text = "준비하기 (READY)";
        readyTmp.fontSize = 34;
        readyTmp.fontStyle = FontStyles.Bold;
        readyTmp.alignment = TextAlignmentOptions.Center;
        readyTmp.color = Color.white;

        // 6. CharacterSelectUI 컴포넌트 변수 연결
        uiScript.selectButtons = new Button[] { foxCard.selectButton, rabbitCard.selectButton };
        uiScript.selectHighlights = new GameObject[] { foxCard.outlineGo, rabbitCard.outlineGo };
        uiScript.normalButtonSprite = btnMidlBlue;
        uiScript.selectedButtonSprite = btnMidlYellow;

        uiScript.readyButton = readyBtn;
        uiScript.readyButtonImage = readyImg;
        uiScript.notReadySprite = btnLongBlue;
        uiScript.readySprite = btnLongGreen;
        uiScript.readyText = readyTmp;
        uiScript.notReadyTextString = "준비하기 (READY)";
        uiScript.readyTextString = "준비 완료! (READY)";

        // 7. 프리팹 갱신 (Assets/Prefebs/Canvas.prefab)
        string prefabPath = "Assets/Prefebs/Canvas.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
        {
            PrefabUtility.SaveAsPrefabAssetAndConnect(canvasGo, prefabPath, InteractionMode.UserAction);
            Debug.Log($"<color=lime>[UI Builder]</color> 프리팹 갱신 완료: {prefabPath}");
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("완료", "캐릭터 선택 UI가 다운로드된 에셋(Buttons & Kenney Pixel UI)으로 성공적으로 빌드되었습니다!", "확인");
        Debug.Log("<color=green>[UI Builder]</color> 캐릭터 선택 UI 생성 완료!");
    }

    private struct CardResult
    {
        public Button selectButton;
        public GameObject outlineGo;
    }

    private static CardResult CreateCharacterCard(
        Transform parent,
        string name,
        float posX,
        string title,
        string skill,
        Sprite iconSprite,
        Sprite cardBgSprite,
        Sprite btnSprite,
        Sprite outlineSprite,
        TMP_FontAsset fontAsset)
    {
        // 1. 카드 본체 (Card Root)
        GameObject cardGo = new GameObject(name);
        cardGo.transform.SetParent(parent, false);
        RectTransform cardRect = cardGo.AddComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.anchoredPosition = new Vector2(posX, 0f);
        cardRect.sizeDelta = new Vector2(360f, 480f);

        // 카드 배경
        Image bgImg = cardGo.AddComponent<Image>();
        if (cardBgSprite != null)
        {
            bgImg.sprite = cardBgSprite;
            bgImg.type = Image.Type.Sliced;
            bgImg.color = new Color(0.18f, 0.2f, 0.25f, 0.92f);
        }

        // 2. 캐릭터 이름
        GameObject titleGo = new GameObject("NameText");
        titleGo.transform.SetParent(cardGo.transform, false);
        RectTransform titleRect = titleGo.AddComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 1f);
        titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -25f);
        titleRect.sizeDelta = new Vector2(320f, 50f);

        TextMeshProUGUI titleTmp = titleGo.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null) titleTmp.font = fontAsset;
        titleTmp.text = title;
        titleTmp.fontSize = 32;
        titleTmp.fontStyle = FontStyles.Bold;
        titleTmp.alignment = TextAlignmentOptions.Center;
        titleTmp.color = Color.white;

        // 3. 캐릭터 아이콘 이미지
        GameObject iconGo = new GameObject("CharacterIcon");
        iconGo.transform.SetParent(cardGo.transform, false);
        RectTransform iconRect = iconGo.AddComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0.5f, 0.5f);
        iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = new Vector2(0f, 30f);
        iconRect.sizeDelta = new Vector2(160f, 160f);

        Image iconImg = iconGo.AddComponent<Image>();
        if (iconSprite != null)
        {
            iconImg.sprite = iconSprite;
            iconImg.preserveAspect = true;
        }

        // 4. 스킬 설명 텍스트
        GameObject skillGo = new GameObject("SkillText");
        skillGo.transform.SetParent(cardGo.transform, false);
        RectTransform skillRect = skillGo.AddComponent<RectTransform>();
        skillRect.anchorMin = new Vector2(0.5f, 0f);
        skillRect.anchorMax = new Vector2(0.5f, 0f);
        skillRect.pivot = new Vector2(0.5f, 0f);
        skillRect.anchoredPosition = new Vector2(0f, 115f);
        skillRect.sizeDelta = new Vector2(320f, 40f);

        TextMeshProUGUI skillTmp = skillGo.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null) skillTmp.font = fontAsset;
        skillTmp.text = skill;
        skillTmp.fontSize = 20;
        skillTmp.alignment = TextAlignmentOptions.Center;
        skillTmp.color = new Color(1f, 0.85f, 0.4f, 1f);

        // 5. 선택 버튼
        GameObject btnGo = new GameObject("SelectButton");
        btnGo.transform.SetParent(cardGo.transform, false);
        RectTransform btnRect = btnGo.AddComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0.5f, 0f);
        btnRect.anchorMax = new Vector2(0.5f, 0f);
        btnRect.pivot = new Vector2(0.5f, 0f);
        btnRect.anchoredPosition = new Vector2(0f, 30f);
        btnRect.sizeDelta = new Vector2(240f, 65f);

        Image btnImg = btnGo.AddComponent<Image>();
        if (btnSprite != null)
        {
            btnImg.sprite = btnSprite;
            btnImg.type = Image.Type.Simple;
        }

        Button selectBtn = btnGo.AddComponent<Button>();
        selectBtn.targetGraphic = btnImg;

        // 선택 버튼 텍스트
        GameObject btnTextGo = new GameObject("Text");
        btnTextGo.transform.SetParent(btnGo.transform, false);
        RectTransform btnTextRect = btnTextGo.AddComponent<RectTransform>();
        btnTextRect.anchorMin = Vector2.zero;
        btnTextRect.anchorMax = Vector2.one;
        btnTextRect.sizeDelta = Vector2.zero;

        TextMeshProUGUI btnTmp = btnTextGo.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null) btnTmp.font = fontAsset;
        btnTmp.text = "선택";
        btnTmp.fontSize = 26;
        btnTmp.fontStyle = FontStyles.Bold;
        btnTmp.alignment = TextAlignmentOptions.Center;
        btnTmp.color = Color.white;

        // 6. 선택 시 표시되는 하이라이트 외곽선 (Kenney Outline/yellow)
        GameObject outlineGo = new GameObject("HighlightOutline");
        outlineGo.transform.SetParent(cardGo.transform, false);
        RectTransform outlineRect = outlineGo.AddComponent<RectTransform>();
        outlineRect.anchorMin = Vector2.zero;
        outlineRect.anchorMax = Vector2.one;
        outlineRect.sizeDelta = new Vector2(16f, 16f); // 카드보다 약간 크게 감싸도록

        Image outlineImg = outlineGo.AddComponent<Image>();
        if (outlineSprite != null)
        {
            outlineImg.sprite = outlineSprite;
            outlineImg.type = Image.Type.Sliced;
        }
        outlineImg.raycastTarget = false;
        outlineGo.SetActive(false); // 기본 상태는 비활성화

        return new CardResult
        {
            selectButton = selectBtn,
            outlineGo = outlineGo
        };
    }
}
#endif
