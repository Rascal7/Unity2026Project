#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// 로비(TitleScreen)와 동일한 픽셀 아트 우드/골드 테마의 세이브 슬롯 UI 빌더.
/// </summary>
public static class SaveFileUIBuilder
{
    [MenuItem("Tools/UI/Build Save File Scene UI", false, 61)]
    public static void ManualBuild()
    {
        BuildSaveFileUI(true);
    }

    public static void BuildSaveFileUI(bool interactive = false)
    {
        string scenePath = "Assets/Scenes/savefile.unity";
        var activeScene = EditorSceneManager.GetActiveScene();
        bool isAlreadyOpen = (activeScene.path == scenePath);
        Scene saveScene;

        if (isAlreadyOpen)
        {
            saveScene = activeScene;
        }
        else
        {
            saveScene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        }

        if (!saveScene.IsValid())
        {
            Debug.LogError($"[SaveFileUIBuilder] {scenePath} 씬을 열 수 없습니다.");
            return;
        }

        Scene prevActiveScene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.SetActiveScene(saveScene);

        try
        {
            // 1. 기존 Canvas 정리
            foreach (var root in saveScene.GetRootGameObjects())
            {
                if (root.name == "SaveFileCanvas")
                {
                    Undo.DestroyObjectImmediate(root);
                }
            }

            // EventSystem 확인 및 생성
            bool hasEventSystem = false;
            foreach (var root in saveScene.GetRootGameObjects())
            {
                if (root.name == "EventSystem")
                {
                    hasEventSystem = true;
                    break;
                }
            }

            if (!hasEventSystem)
            {
                GameObject eventSystem = new GameObject("EventSystem");
                eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
                SceneManager.MoveGameObjectToScene(eventSystem, saveScene);
                Undo.RegisterCreatedObjectUndo(eventSystem, "Create EventSystem");
            }

            // 2. 메인 카메라
            foreach (var root in saveScene.GetRootGameObjects())
            {
                Camera c = root.GetComponentInChildren<Camera>();
                if (c != null)
                {
                    c.clearFlags = CameraClearFlags.SolidColor;
                    c.backgroundColor = new Color(0.12f, 0.08f, 0.05f, 1f);
                    break;
                }
            }

            // 3. 에셋 로드
            TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/NotoSansKR-Bold SDF.asset")
                                   ?? AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Sunflower-Bold SDF.asset");

            Sprite bgSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Title/back.png")
                           ?? AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Background/back.png");

            // 4. 최상위 Canvas 생성
            GameObject canvasGo = new GameObject("SaveFileCanvas");
            SceneManager.MoveGameObjectToScene(canvasGo, saveScene);
            Undo.RegisterCreatedObjectUndo(canvasGo, "Create SaveFileCanvas");

            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();
            SaveFileUI uiScript = canvasGo.AddComponent<SaveFileUI>();

            // 5. 로비 숲 배경 (Title/back.png)
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

            // 6. 상단 글로벌 헤더
            GameObject headerGo = new GameObject("Header");
            headerGo.transform.SetParent(canvasGo.transform, false);
            RectTransform headerRect = headerGo.AddComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0.5f, 1f);
            headerRect.anchorMax = new Vector2(0.5f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.anchoredPosition = new Vector2(0f, -40f);
            headerRect.sizeDelta = new Vector2(900f, 90f);

            var mainTitleTmp = CreateText(headerGo.transform, "MainTitle", "VOICE ADVENTURE",
                Vector2.zero, new Vector2(900f, 60f), 52, FontStyles.Bold, TextAlignmentOptions.Center, fontAsset, LobbyUITheme.Gold);
            LobbyUITheme.StyleTitle(mainTitleTmp, "VOICE ADVENTURE", 54f);

            // 클라이언트 알림
            TextMeshProUGUI clientNoticeTmp = CreateText(headerGo.transform, "ClientNoticeText", "방장(Host)이 세이브 파일을 선택하는 중입니다...",
                new Vector2(0f, -60f), new Vector2(800f, 32f), 22, FontStyles.Normal, TextAlignmentOptions.Center, fontAsset, LobbyUITheme.Parch);
            LobbyUITheme.StyleSubtitle(clientNoticeTmp, "방장(Host)이 세이브 파일을 선택하는 중입니다...", 22f);
            uiScript.clientNoticeText = clientNoticeTmp;

            // 7. 모드 선택 패널 (Mode Select Panel)
            GameObject modePanelGo = new GameObject("ModeSelectPanel");
            modePanelGo.transform.SetParent(canvasGo.transform, false);
            RectTransform modePanelRect = modePanelGo.AddComponent<RectTransform>();
            modePanelRect.anchorMin = new Vector2(0.5f, 0.5f);
            modePanelRect.anchorMax = new Vector2(0.5f, 0.5f);
            modePanelRect.pivot = new Vector2(0.5f, 0.5f);
            modePanelRect.anchoredPosition = new Vector2(0f, -20f);
            modePanelRect.sizeDelta = new Vector2(760f, 540f);
            uiScript.modeSelectPanel = modePanelGo;

            LobbyUITheme.StyleWoodPanel(modePanelGo, LobbyUITheme.WoodFill, 8f, 6f);

            var modeTitle = CreateText(modePanelGo.transform, "Title", "게임 모드 선택",
                new Vector2(0f, 170f), new Vector2(600f, 55f), 38, FontStyles.Bold, TextAlignmentOptions.Center, fontAsset, LobbyUITheme.Gold);
            LobbyUITheme.StyleTitle(modeTitle, "게임 모드 선택", 40f);

            var modeSub = CreateText(modePanelGo.transform, "ModeSubtitle", "새 게임을 시작하거나 기존 데이터를 불러오세요",
                new Vector2(0f, 120f), new Vector2(650f, 40f), 22, FontStyles.Normal, TextAlignmentOptions.Center, fontAsset, LobbyUITheme.Parch);
            LobbyUITheme.StyleSubtitle(modeSub, "새 게임을 시작하거나 기존 데이터를 불러오세요", 22f);

            // 새 게임 버튼 (골드 3D 픽셀 버튼)
            uiScript.newGameModeButton = CreatePixelButton(modePanelGo.transform, "NewGameButton",
                "새 게임 (NEW GAME)", new Vector2(0f, 40f), new Vector2(440f, 75f), LobbyUITheme.ButtonType.Gold, 28, fontAsset);

            // 이어하기 버튼 (스톤 3D 픽셀 버튼)
            uiScript.loadGameModeButton = CreatePixelButton(modePanelGo.transform, "LoadGameButton",
                "이어하기 (LOAD GAME)", new Vector2(0f, -50f), new Vector2(440f, 75f), LobbyUITheme.ButtonType.Stone, 28, fontAsset);

            // 뒤로가기 버튼
            uiScript.backToCharacterSelectButton = CreatePixelButton(modePanelGo.transform, "BackToCharButton",
                "뒤로가기 (BACK)", new Vector2(0f, -150f), new Vector2(300f, 60f), LobbyUITheme.ButtonType.Stone, 22, fontAsset);

            // 8. 슬롯 선택 패널 (Slot Select Panel)
            GameObject slotPanelGo = new GameObject("SlotSelectPanel");
            slotPanelGo.transform.SetParent(canvasGo.transform, false);
            RectTransform slotPanelRect = slotPanelGo.AddComponent<RectTransform>();
            slotPanelRect.anchorMin = new Vector2(0.5f, 0.5f);
            slotPanelRect.anchorMax = new Vector2(0.5f, 0.5f);
            slotPanelRect.pivot = new Vector2(0.5f, 0.5f);
            slotPanelRect.anchoredPosition = new Vector2(0f, -20f);
            slotPanelRect.sizeDelta = new Vector2(880f, 680f);
            uiScript.slotSelectPanel = slotPanelGo;

            LobbyUITheme.StyleWoodPanel(slotPanelGo, LobbyUITheme.WoodFill, 8f, 6f);

            uiScript.slotPanelTitleText = CreateText(slotPanelGo.transform, "SlotPanelTitle", "세이브 슬롯 선택",
                new Vector2(0f, 265f), new Vector2(600f, 50f), 38, FontStyles.Bold, TextAlignmentOptions.Center, fontAsset, LobbyUITheme.Gold);
            LobbyUITheme.StyleTitle(uiScript.slotPanelTitleText, "세이브 슬롯 선택", 40f);

            uiScript.slotPanelSubtitleText = CreateText(slotPanelGo.transform, "SlotPanelSubtitle", "슬롯을 선택해 게임을 진행하세요 (1 ~ 5)",
                new Vector2(0f, 225f), new Vector2(600f, 32f), 20, FontStyles.Normal, TextAlignmentOptions.Center, fontAsset, LobbyUITheme.Parch);
            LobbyUITheme.StyleSubtitle(uiScript.slotPanelSubtitleText, "슬롯을 선택해 게임을 진행하세요 (1 ~ 5)", 20f);

            // 슬롯 컨테이너
            GameObject slotsContainerGo = new GameObject("SlotsContainer");
            slotsContainerGo.transform.SetParent(slotPanelGo.transform, false);
            RectTransform scRect = slotsContainerGo.AddComponent<RectTransform>();
            scRect.anchorMin = new Vector2(0.5f, 0.5f);
            scRect.anchorMax = new Vector2(0.5f, 0.5f);
            scRect.pivot = new Vector2(0.5f, 0.5f);
            scRect.anchoredPosition = new Vector2(0f, 0f);
            scRect.sizeDelta = new Vector2(800f, 390f);

            uiScript.slotRows = new SaveFileUI.SlotRowUI[5];
            float startY = 150f;
            float stepY = 75f;

            for (int i = 0; i < 5; i++)
            {
                int slotIdx = i + 1;
                float posY = startY - (i * stepY);
                uiScript.slotRows[i] = CreateSlotRow(slotsContainerGo.transform, slotIdx, posY, fontAsset);
            }

            // 슬롯 패널 뒤로가기 버튼
            uiScript.backToModeButton = CreatePixelButton(slotPanelGo.transform, "BackToModeButton",
                "뒤로가기 (BACK)", new Vector2(0f, -260f), new Vector2(300f, 60f), LobbyUITheme.ButtonType.Stone, 22, fontAsset);

            slotPanelGo.SetActive(false);

            // 9. 확인 모달 (Confirm Modal)
            uiScript.confirmModal = CreateModal(canvasGo.transform, "ConfirmModal", "확인", out uiScript.confirmMessageText,
                out uiScript.confirmYesButton, out uiScript.confirmNoButton, fontAsset);
            uiScript.confirmModal.SetActive(false);

            // 10. 안내 모달 (Notice Modal)
            uiScript.noticeModal = CreateNoticeModal(canvasGo.transform, "NoticeModal", "알림", out uiScript.noticeMessageText,
                out uiScript.noticeOkButton, fontAsset);
            uiScript.noticeModal.SetActive(false);

            EditorSceneManager.MarkSceneDirty(saveScene);
            if (!isAlreadyOpen)
            {
                EditorSceneManager.SaveScene(saveScene);
                EditorSceneManager.CloseScene(saveScene, true);
            }

            if (interactive)
            {
                EditorUtility.DisplayDialog("완료", "세이브 슬롯 UI가 로비 우드/골드 테마로 성공적으로 변경되었습니다!", "확인");
            }
            Debug.Log("<color=green>[SaveFileUIBuilder]</color> 세이브 파일 씬 UI 빌드 완료!");
        }
        finally
        {
            if (prevActiveScene.IsValid() && prevActiveScene != EditorSceneManager.GetActiveScene())
            {
                EditorSceneManager.SetActiveScene(prevActiveScene);
            }
        }
    }

    private static Button CreatePixelButton(Transform parent, string name, string label, Vector2 pos, Vector2 size, LobbyUITheme.ButtonType type, int fontSize, TMP_FontAsset fontAsset)
    {
        GameObject btnGo = new GameObject(name);
        btnGo.transform.SetParent(parent, false);
        RectTransform rt = btnGo.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Button btn = btnGo.AddComponent<Button>();

        GameObject textGo = new GameObject("Text");
        textGo.transform.SetParent(btnGo.transform, false);
        RectTransform textRt = textGo.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.sizeDelta = Vector2.zero;

        TextMeshProUGUI tmp = textGo.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null) tmp.font = fontAsset;
        tmp.text = label;
        tmp.fontSize = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;

        LobbyUITheme.StylePixelButton(btn, type, label, fontSize);
        return btn;
    }

    private static SaveFileUI.SlotRowUI CreateSlotRow(Transform parent, int slotIndex, float posY, TMP_FontAsset fontAsset)
    {
        var rowUI = new SaveFileUI.SlotRowUI();
        rowUI.slotIndex = slotIndex;

        GameObject rowGo = new GameObject($"SlotRow_{slotIndex}");
        rowGo.transform.SetParent(parent, false);
        RectTransform rowRt = rowGo.AddComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0.5f, 0.5f);
        rowRt.anchorMax = new Vector2(0.5f, 0.5f);
        rowRt.pivot = new Vector2(0.5f, 0.5f);
        rowRt.anchoredPosition = new Vector2(0f, posY);
        rowRt.sizeDelta = new Vector2(780f, 64f);

        // 슬롯 메인 버튼 (어두운 인셋 필드)
        rowUI.slotButton = rowGo.AddComponent<Button>();
        LobbyUITheme.StyleInsetBox(rowGo, LobbyUITheme.FieldBg, LobbyUITheme.WoodDark, 3f);
        rowUI.background = rowGo.GetComponent<Image>();

        // 슬롯 라벨 (SLOT 01)
        GameObject labelGo = new GameObject("SlotLabel");
        labelGo.transform.SetParent(rowGo.transform, false);
        RectTransform labelRt = labelGo.AddComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0f, 0.5f);
        labelRt.anchorMax = new Vector2(0f, 0.5f);
        labelRt.pivot = new Vector2(0f, 0.5f);
        labelRt.anchoredPosition = new Vector2(25f, 0f);
        labelRt.sizeDelta = new Vector2(140f, 40f);

        rowUI.slotTitleText = labelGo.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null) rowUI.slotTitleText.font = fontAsset;
        rowUI.slotTitleText.text = $"SLOT {slotIndex:00}";
        LobbyUITheme.StyleTitle(rowUI.slotTitleText, $"SLOT {slotIndex:00}", 26f);

        // 슬롯 정보
        GameObject infoGo = new GameObject("SlotInfo");
        infoGo.transform.SetParent(rowGo.transform, false);
        RectTransform infoRt = infoGo.AddComponent<RectTransform>();
        infoRt.anchorMin = new Vector2(0f, 0.5f);
        infoRt.anchorMax = new Vector2(1f, 0.5f);
        infoRt.pivot = new Vector2(0f, 0.5f);
        infoRt.anchoredPosition = new Vector2(180f, 0f);
        infoRt.sizeDelta = new Vector2(-280f, 40f);

        rowUI.slotInfoText = infoGo.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null) rowUI.slotInfoText.font = fontAsset;
        rowUI.slotInfoText.text = "<color=#8C7B6B>[ 빈 슬롯 (EMPTY) ]</color>";
        rowUI.slotInfoText.fontSize = 20;
        rowUI.slotInfoText.alignment = TextAlignmentOptions.Left;
        rowUI.slotInfoText.color = LobbyUITheme.Parch;

        // 삭제 버튼 (레드 3D 픽셀 버튼)
        rowUI.deleteButton = CreatePixelButton(rowGo.transform, "DeleteButton", "삭제",
            new Vector2(340f, 0f), new Vector2(70f, 44f), LobbyUITheme.ButtonType.Red, 18, fontAsset);
        rowUI.deleteButton.gameObject.SetActive(false);

        return rowUI;
    }

    private static GameObject CreateModal(Transform parent, string name, string title,
        out TextMeshProUGUI messageText, out Button yesBtn, out Button noBtn, TMP_FontAsset fontAsset)
    {
        GameObject modalGo = new GameObject(name);
        modalGo.transform.SetParent(parent, false);
        RectTransform mRt = modalGo.AddComponent<RectTransform>();
        mRt.anchorMin = Vector2.zero;
        mRt.anchorMax = Vector2.one;
        mRt.sizeDelta = Vector2.zero;

        // 어두운 반투명 오버레이
        GameObject overlay = new GameObject("DarkOverlay");
        overlay.transform.SetParent(modalGo.transform, false);
        RectTransform oRt = overlay.AddComponent<RectTransform>();
        oRt.anchorMin = Vector2.zero;
        oRt.anchorMax = Vector2.one;
        oRt.sizeDelta = Vector2.zero;
        Image oImg = overlay.AddComponent<Image>();
        oImg.color = new Color(0f, 0f, 0f, 0.72f);

        // 모달 박스 (우드 프레임)
        GameObject boxGo = new GameObject("Box");
        boxGo.transform.SetParent(modalGo.transform, false);
        RectTransform bRt = boxGo.AddComponent<RectTransform>();
        bRt.anchorMin = new Vector2(0.5f, 0.5f);
        bRt.anchorMax = new Vector2(0.5f, 0.5f);
        bRt.pivot = new Vector2(0.5f, 0.5f);
        bRt.sizeDelta = new Vector2(620f, 340f);
        LobbyUITheme.StyleWoodPanel(boxGo, LobbyUITheme.WoodFill, 8f, 6f);

        var titleTmp = CreateText(boxGo.transform, "Title", title,
            new Vector2(0f, 105f), new Vector2(500f, 45f), 32, FontStyles.Bold, TextAlignmentOptions.Center, fontAsset, LobbyUITheme.Gold);
        LobbyUITheme.StyleTitle(titleTmp, title, 32f);

        messageText = CreateText(boxGo.transform, "Message", "내용을 입력하세요",
            new Vector2(0f, 25f), new Vector2(520f, 80f), 22, FontStyles.Normal, TextAlignmentOptions.Center, fontAsset, LobbyUITheme.Parch);
        LobbyUITheme.StyleSubtitle(messageText, null, 22f);

        yesBtn = CreatePixelButton(boxGo.transform, "YesButton", "확인",
            new Vector2(-90f, -85f), new Vector2(150f, 55f), LobbyUITheme.ButtonType.Green, 22, fontAsset);

        noBtn = CreatePixelButton(boxGo.transform, "NoButton", "취소",
            new Vector2(90f, -85f), new Vector2(150f, 55f), LobbyUITheme.ButtonType.Red, 22, fontAsset);

        return modalGo;
    }

    private static GameObject CreateNoticeModal(Transform parent, string name, string title,
        out TextMeshProUGUI messageText, out Button okBtn, TMP_FontAsset fontAsset)
    {
        GameObject modalGo = new GameObject(name);
        modalGo.transform.SetParent(parent, false);
        RectTransform mRt = modalGo.AddComponent<RectTransform>();
        mRt.anchorMin = Vector2.zero;
        mRt.anchorMax = Vector2.one;
        mRt.sizeDelta = Vector2.zero;

        GameObject overlay = new GameObject("DarkOverlay");
        overlay.transform.SetParent(modalGo.transform, false);
        RectTransform oRt = overlay.AddComponent<RectTransform>();
        oRt.anchorMin = Vector2.zero;
        oRt.anchorMax = Vector2.one;
        oRt.sizeDelta = Vector2.zero;
        Image oImg = overlay.AddComponent<Image>();
        oImg.color = new Color(0f, 0f, 0f, 0.72f);

        GameObject boxGo = new GameObject("Box");
        boxGo.transform.SetParent(modalGo.transform, false);
        RectTransform bRt = boxGo.AddComponent<RectTransform>();
        bRt.anchorMin = new Vector2(0.5f, 0.5f);
        bRt.anchorMax = new Vector2(0.5f, 0.5f);
        bRt.pivot = new Vector2(0.5f, 0.5f);
        bRt.sizeDelta = new Vector2(580f, 320f);
        LobbyUITheme.StyleWoodPanel(boxGo, LobbyUITheme.WoodFill, 8f, 6f);

        var titleTmp = CreateText(boxGo.transform, "Title", title,
            new Vector2(0f, 95f), new Vector2(480f, 45f), 32, FontStyles.Bold, TextAlignmentOptions.Center, fontAsset, LobbyUITheme.Gold);
        LobbyUITheme.StyleTitle(titleTmp, title, 32f);

        messageText = CreateText(boxGo.transform, "Message", "내용을 입력하세요",
            new Vector2(0f, 20f), new Vector2(480f, 80f), 22, FontStyles.Normal, TextAlignmentOptions.Center, fontAsset, LobbyUITheme.Parch);
        LobbyUITheme.StyleSubtitle(messageText, null, 22f);

        okBtn = CreatePixelButton(boxGo.transform, "OkButton", "확인",
            new Vector2(0f, -80f), new Vector2(170f, 55f), LobbyUITheme.ButtonType.Gold, 24, fontAsset);

        return modalGo;
    }

    private static TextMeshProUGUI CreateText(Transform parent, string name, string text,
        Vector2 pos, Vector2 size, float fontSize, FontStyles style, TextAlignmentOptions align, TMP_FontAsset font, Color color)
    {
        GameObject textGo = new GameObject(name);
        textGo.transform.SetParent(parent, false);
        RectTransform rt = textGo.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        TextMeshProUGUI tmp = textGo.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.color = color;
        return tmp;
    }
}
#endif
