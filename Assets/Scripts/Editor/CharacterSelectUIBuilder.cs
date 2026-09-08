#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

/// <summary>
/// 로비(TitleScreen)와 동일한 픽셀 아트 우드/골드 테마의 캐릭터 선택 UI 빌더.
/// </summary>
public static class CharacterSelectUIBuilder
{
    [MenuItem("Tools/UI/Build Character Select UI", false, 60)]
    public static void ManualBuild()
    {
        BuildCharacterSelectUI(true);
    }

    public static void BuildCharacterSelectUI(bool interactive = false)
    {
        // 1. 기존 Canvas 정리
        GameObject oldCanvas = GameObject.Find("Canvas");
        if (oldCanvas != null)
        {
            Undo.DestroyObjectImmediate(oldCanvas);
        }

        TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/NotoSansKR-Bold SDF.asset")
                               ?? AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Sunflower-Bold SDF.asset");

        Sprite bgSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Title/back.png")
                       ?? AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Background/back.png");

        Sprite foxIcon = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Title/char1.png")
                      ?? AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprite/Player1/player-idle-1.png");
        Sprite rabbitIcon = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Title/char2.png")
                         ?? AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprite/Player2/player-idle-1.png");

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

        // 3. 로비 숲 배경 (Title/back.png)
        GameObject bgGo = new GameObject("Background");
        bgGo.transform.SetParent(canvasGo.transform, false);
        RectTransform bgRect = bgGo.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.sizeDelta = Vector2.zero;

        Image bgImg = bgGo.AddComponent<Image>();
        if (bgSprite != null)
        {
            bgImg.sprite = bgSprite;
            bgImg.type = Image.Type.Simple;
            bgImg.preserveAspect = false;
        }
        bgImg.color = Color.white;

        // 4. 타이틀 배너 (CHARACTER SELECT)
        GameObject titleGo = new GameObject("TitleText");
        titleGo.transform.SetParent(canvasGo.transform, false);
        RectTransform titleRect = titleGo.AddComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 1f);
        titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -60f);
        titleRect.sizeDelta = new Vector2(1000f, 75f);

        TextMeshProUGUI titleText = titleGo.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null) titleText.font = fontAsset;
        LobbyUITheme.StyleTitle(titleText, "CHARACTER SELECT", 54f);

        // 서브 타이틀
        GameObject subGo = new GameObject("SubtitleText");
        subGo.transform.SetParent(canvasGo.transform, false);
        RectTransform subRect = subGo.AddComponent<RectTransform>();
        subRect.anchorMin = new Vector2(0.5f, 1f);
        subRect.anchorMax = new Vector2(0.5f, 1f);
        subRect.pivot = new Vector2(0.5f, 1f);
        subRect.anchoredPosition = new Vector2(0f, -135f);
        subRect.sizeDelta = new Vector2(800f, 36f);

        TextMeshProUGUI subText = subGo.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null) subText.font = fontAsset;
        LobbyUITheme.StyleSubtitle(subText, "원하는 캐릭터를 선택하고 준비를 완료하세요", 22f);

        // 5. 슬롯 컨테이너
        GameObject slotsContainer = new GameObject("SlotsContainer");
        slotsContainer.transform.SetParent(canvasGo.transform, false);
        RectTransform slotsRect = slotsContainer.AddComponent<RectTransform>();
        slotsRect.anchorMin = new Vector2(0.5f, 0.5f);
        slotsRect.anchorMax = new Vector2(0.5f, 0.5f);
        slotsRect.pivot = new Vector2(0.5f, 0.5f);
        slotsRect.anchoredPosition = new Vector2(0f, 20f);
        slotsRect.sizeDelta = new Vector2(1200f, 620f);

        // 카드 1: 여우 (Fox) - 스킬: 대쉬 [Shift]
        var foxCard = CreateCharacterCard(
            parent: slotsContainer.transform,
            name: "Card_Fox",
            posX: -340f,
            title: "여우 (FOXY)",
            skill: "스킬: 대쉬 [Shift]",
            iconSprite: foxIcon,
            fontAsset: fontAsset
        );

        // 카드 2: 토끼 (Rabbit) - 스킬: 슈퍼점프 [Shift]
        var rabbitCard = CreateCharacterCard(
            parent: slotsContainer.transform,
            name: "Card_Rabbit",
            posX: 340f,
            title: "토끼 (RABBIT)",
            skill: "스킬: 슈퍼점프 [Shift]",
            iconSprite: rabbitIcon,
            fontAsset: fontAsset
        );

        // 6. 하단 READY 버튼 (로비 3D 픽셀 버튼)
        GameObject readyBtnGo = new GameObject("ReadyButton");
        readyBtnGo.transform.SetParent(canvasGo.transform, false);
        RectTransform readyRect = readyBtnGo.AddComponent<RectTransform>();
        readyRect.anchorMin = new Vector2(0.5f, 0f);
        readyRect.anchorMax = new Vector2(0.5f, 0f);
        readyRect.pivot = new Vector2(0.5f, 0f);
        readyRect.anchoredPosition = new Vector2(0f, 75f);
        readyRect.sizeDelta = new Vector2(400f, 85f);

        Button readyBtn = readyBtnGo.AddComponent<Button>();
        LobbyUITheme.StylePixelButton(readyBtn, LobbyUITheme.ButtonType.Stone, "준비하기 (READY)", 30);

        // 7. CharacterSelectUI 스크립트 연결
        uiScript.selectButtons = new Button[] { foxCard.selectButton, rabbitCard.selectButton };
        uiScript.selectHighlights = new GameObject[] { foxCard.outlineGo, rabbitCard.outlineGo };
        uiScript.readyButton = readyBtn;
        uiScript.readyButtonImage = readyBtnGo.GetComponent<Image>();
        uiScript.readyText = readyBtn.GetComponentInChildren<TextMeshProUGUI>();
        uiScript.notReadyTextString = "준비하기 (READY)";
        uiScript.readyTextString = "준비 완료! (READY)";

        // 8. 프리팹 갱신 (Assets/Prefebs/Canvas.prefab)
        string prefabPath = "Assets/Prefebs/Canvas.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
        {
            PrefabUtility.SaveAsPrefabAssetAndConnect(canvasGo, prefabPath, InteractionMode.UserAction);
            Debug.Log($"<color=lime>[UI Builder]</color> 프리팹 갱신 완료: {prefabPath}");
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        if (interactive)
        {
            EditorUtility.DisplayDialog("완료", "캐릭터 선택 UI가 로비 우드/골드 테마로 성공적으로 변경되었습니다!", "확인");
        }
        Debug.Log("<color=green>[UI Builder]</color> 캐릭터 선택 UI 빌드 완료!");
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
        cardRect.sizeDelta = new Vector2(380f, 520f);

        // 우드 프레임 적용
        LobbyUITheme.StyleWoodPanel(cardGo, LobbyUITheme.WoodFill, 8f, 6f);

        // 2. 캐릭터 이름
        GameObject titleGo = new GameObject("NameText");
        titleGo.transform.SetParent(cardGo.transform, false);
        RectTransform titleRect = titleGo.AddComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 1f);
        titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -24f);
        titleRect.sizeDelta = new Vector2(340f, 48f);

        TextMeshProUGUI titleTmp = titleGo.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null) titleTmp.font = fontAsset;
        LobbyUITheme.StyleTitle(titleTmp, title, 32f);

        // 3. 캐릭터 아이콘 인셋 영역
        GameObject iconBoxGo = new GameObject("IconBox");
        iconBoxGo.transform.SetParent(cardGo.transform, false);
        RectTransform iconBoxRect = iconBoxGo.AddComponent<RectTransform>();
        iconBoxRect.anchorMin = new Vector2(0.5f, 0.5f);
        iconBoxRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconBoxRect.pivot = new Vector2(0.5f, 0.5f);
        iconBoxRect.anchoredPosition = new Vector2(0f, 45f);
        iconBoxRect.sizeDelta = new Vector2(240f, 240f);
        LobbyUITheme.StyleInsetBox(iconBoxGo, LobbyUITheme.FieldBg, LobbyUITheme.WoodDark, 4f);

        GameObject iconGo = new GameObject("CharacterIcon");
        iconGo.transform.SetParent(iconBoxGo.transform, false);
        RectTransform iconRect = iconGo.AddComponent<RectTransform>();
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        iconRect.offsetMin = new Vector2(16f, 16f);
        iconRect.offsetMax = new Vector2(-16f, -16f);

        Image iconImg = iconGo.AddComponent<Image>();
        if (iconSprite != null)
        {
            iconImg.sprite = iconSprite;
            iconImg.preserveAspect = true;
        }

        // 4. 스킬 설명 텍스트
        GameObject skillBoxGo = new GameObject("SkillBox");
        skillBoxGo.transform.SetParent(cardGo.transform, false);
        RectTransform skillBoxRect = skillBoxGo.AddComponent<RectTransform>();
        skillBoxRect.anchorMin = new Vector2(0.5f, 0f);
        skillBoxRect.anchorMax = new Vector2(0.5f, 0f);
        skillBoxRect.pivot = new Vector2(0.5f, 0f);
        skillBoxRect.anchoredPosition = new Vector2(0f, 115f);
        skillBoxRect.sizeDelta = new Vector2(320f, 44f);
        LobbyUITheme.StyleInsetBox(skillBoxGo, new Color(0.12f, 0.08f, 0.05f, 0.9f), LobbyUITheme.WoodLight, 2f);

        GameObject skillGo = new GameObject("SkillText");
        skillGo.transform.SetParent(skillBoxGo.transform, false);
        RectTransform skillRect = skillGo.AddComponent<RectTransform>();
        skillRect.anchorMin = Vector2.zero;
        skillRect.anchorMax = Vector2.one;
        skillRect.sizeDelta = Vector2.zero;

        TextMeshProUGUI skillTmp = skillGo.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null) skillTmp.font = fontAsset;
        LobbyUITheme.StyleSubtitle(skillTmp, skill, 19f);

        // 5. 선택 버튼
        GameObject btnGo = new GameObject("SelectButton");
        btnGo.transform.SetParent(cardGo.transform, false);
        RectTransform btnRect = btnGo.AddComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0.5f, 0f);
        btnRect.anchorMax = new Vector2(0.5f, 0f);
        btnRect.pivot = new Vector2(0.5f, 0f);
        btnRect.anchoredPosition = new Vector2(0f, 25f);
        btnRect.sizeDelta = new Vector2(260f, 65f);

        Button selectBtn = btnGo.AddComponent<Button>();
        LobbyUITheme.StylePixelButton(selectBtn, LobbyUITheme.ButtonType.Stone, "선택하기", 24);

        // 6. 선택 하이라이트 골드 테두리 (속이 빈 4면 테두리로 구성하여 카드 내부를 가리지 않음)
        GameObject outlineGo = new GameObject("HighlightOutline");
        outlineGo.transform.SetParent(cardGo.transform, false);
        LobbyUITheme.SetupHollowOutline(outlineGo, LobbyUITheme.Gold, 6f);
        outlineGo.SetActive(false);

        return new CardResult
        {
            selectButton = selectBtn,
            outlineGo = outlineGo
        };
    }
}
#endif
