#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public static class SaveFileUIBuilder
{
    [MenuItem("Tools/UI/Build Save File Scene UI", false, 61)]
    public static void ManualBuild()
    {
        BuildSaveFileUI(true);
    }

    public static void BuildSaveFileUI(bool interactive)
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
            // 에디터 작업 방해 없이 Additive로 로드하여 수정 후 저장
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

            // 2. 메인 카메라 설정 (배경 어둡고 깔끔한 단색)
            foreach (var root in saveScene.GetRootGameObjects())
            {
                Camera c = root.GetComponentInChildren<Camera>();
                if (c != null)
                {
                    c.clearFlags = CameraClearFlags.SolidColor;
                    c.backgroundColor = new Color(0.10f, 0.12f, 0.16f, 1f);
                    break;
                }
            }

            // 3. 폰트 및 스프라이트 에셋 로드
            TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/NotoSansKR-Bold SDF.asset");
            if (fontAsset == null)
            {
                fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Sunflower-Bold SDF.asset");
            }

            Sprite bgSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Background/back.png");
            Sprite btnLongGreen = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Buttons/PNG/25Button_Long_Green.png")
                               ?? AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Buttons/PNG/13Button_Long_Green.png");
            Sprite btnLongBlue = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Buttons/PNG/26Button_Long_Blue.png")
                              ?? AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Buttons/PNG/12Button_Long_Blue.png");
            Sprite btnLongRed = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Buttons/PNG/27Button_Long_Red.png")
                             ?? AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Buttons/PNG/14Button_Long_Red.png");
            Sprite btnMidlBlue = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Buttons/PNG/11Button_Midl_Blue.png");
            Sprite btnMidlYellow = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Buttons/PNG/10Button_Midl_Yellow.png");

            Sprite cardBgSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/kenney_pixel-ui-pack/9-Slice/space_inlay.png")
                               ?? AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/kenney_pixel-ui-pack/9-Slice/Colored/grey.png");

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

            // 5. 배경 레이어 (일러스트 배경 + 어두운 오버레이)
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
            bgImg.color = new Color(0.4f, 0.45f, 0.55f, 0.5f);

            // 어두운 틴트 오버레이
            GameObject dimOverlay = new GameObject("DarkOverlay");
            dimOverlay.transform.SetParent(bgGo.transform, false);
            RectTransform dimRect = dimOverlay.AddComponent<RectTransform>();
            dimRect.anchorMin = Vector2.zero;
            dimRect.anchorMax = Vector2.one;
            dimRect.sizeDelta = Vector2.zero;
            Image dimImg = dimOverlay.AddComponent<Image>();
            dimImg.color = new Color(0.06f, 0.08f, 0.12f, 0.78f);

            // 6. 상단 글로벌 타이틀
            GameObject headerGo = new GameObject("Header");
            headerGo.transform.SetParent(canvasGo.transform, false);
            RectTransform headerRect = headerGo.AddComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0.5f, 1f);
            headerRect.anchorMax = new Vector2(0.5f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.anchoredPosition = new Vector2(0f, -40f);
            headerRect.sizeDelta = new Vector2(900f, 90f);

            CreateText(headerGo.transform, "MainTitle", "세이브 파일 (SAVE FILE)",
                Vector2.zero, new Vector2(900f, 60f), 46, FontStyles.Bold, TextAlignmentOptions.Center, fontAsset, new Color(1f, 0.9f, 0.45f, 1f));

            // 클라이언트 대기 알림
            TextMeshProUGUI clientNoticeTmp = CreateText(headerGo.transform, "ClientNoticeText", "방장(Host)이 세이브 파일을 선택하는 중입니다...",
                new Vector2(0f, -60f), new Vector2(800f, 32f), 20, FontStyles.Normal, TextAlignmentOptions.Center, fontAsset, new Color(0.85f, 0.85f, 0.95f, 1f));
            uiScript.clientNoticeText = clientNoticeTmp;

            // ==========================================
            // 7. 모드 선택 패널 (Mode Select Panel)
            // ==========================================
            GameObject modePanelGo = new GameObject("ModeSelectPanel");
            modePanelGo.transform.SetParent(canvasGo.transform, false);
            RectTransform modePanelRect = modePanelGo.AddComponent<RectTransform>();
            modePanelRect.anchorMin = new Vector2(0.5f, 0.5f);
            modePanelRect.anchorMax = new Vector2(0.5f, 0.5f);
            modePanelRect.pivot = new Vector2(0.5f, 0.5f);
            modePanelRect.anchoredPosition = new Vector2(0f, -20f);
            modePanelRect.sizeDelta = new Vector2(900f, 600f);
            uiScript.modeSelectPanel = modePanelGo;

            CreateText(modePanelGo.transform, "ModeSubtitle", "플레이 방식을 선택하세요",
                new Vector2(0f, 210f), new Vector2(600f, 40f), 24, FontStyles.Normal, TextAlignmentOptions.Center, fontAsset, new Color(0.8f, 0.85f, 0.9f, 1f));

            // [ 새 게임 버튼 ]
            var (newBtnGo, newBtn, _) = CreateBigMenuButton(
                modePanelGo.transform, "NewGameButton",
                new Vector2(0f, 100f), new Vector2(460f, 110f),
                "새 게임 (NEW GAME)", "새로운 슬롯에 모험을 기록합니다",
                btnLongGreen, fontAsset);
            uiScript.newGameModeButton = newBtn;

            // [ 이어하기 버튼 ]
            var (loadBtnGo, loadBtn, _) = CreateBigMenuButton(
                modePanelGo.transform, "LoadGameButton",
                new Vector2(0f, -40f), new Vector2(460f, 110f),
                "이어하기 (LOAD GAME)", "저장된 슬롯을 불러와 계속 플레이합니다",
                btnLongBlue, fontAsset);
            uiScript.loadGameModeButton = loadBtn;

            // [ 캐릭터 선택으로 돌아가기 버튼 ]
            var (backCharBtnGo, backCharBtn, _) = CreateSimpleButton(
                modePanelGo.transform, "BackToCharButton",
                new Vector2(0f, -180f), new Vector2(260f, 55f),
                "캐릭터 선택으로", btnMidlBlue, fontAsset, 22);
            uiScript.backToCharacterSelectButton = backCharBtn;

            // ==========================================
            // 8. 슬롯 선택 패널 (Slot Select Panel)
            // ==========================================
            GameObject slotPanelGo = new GameObject("SlotSelectPanel");
            slotPanelGo.transform.SetParent(canvasGo.transform, false);
            RectTransform slotPanelRect = slotPanelGo.AddComponent<RectTransform>();
            slotPanelRect.anchorMin = new Vector2(0.5f, 0.5f);
            slotPanelRect.anchorMax = new Vector2(0.5f, 0.5f);
            slotPanelRect.pivot = new Vector2(0.5f, 0.5f);
            slotPanelRect.anchoredPosition = new Vector2(0f, -30f);
            slotPanelRect.sizeDelta = new Vector2(980f, 680f);
            uiScript.slotSelectPanel = slotPanelGo;

            TextMeshProUGUI slotTitleTmp = CreateText(slotPanelGo.transform, "SlotPanelTitle", "새 게임 - 슬롯 선택",
                new Vector2(0f, 280f), new Vector2(700f, 46f), 32, FontStyles.Bold, TextAlignmentOptions.Center, fontAsset, Color.white);
            uiScript.slotPanelTitleText = slotTitleTmp;

            TextMeshProUGUI slotSubTmp = CreateText(slotPanelGo.transform, "SlotPanelSubtitle", "진행할 세이브 슬롯을 선택하세요 (1 ~ 5)",
                new Vector2(0f, 240f), new Vector2(700f, 32f), 18, FontStyles.Normal, TextAlignmentOptions.Center, fontAsset, new Color(0.75f, 0.8f, 0.85f, 1f));
            uiScript.slotPanelSubtitleText = slotSubTmp;

            // 슬롯 컨테이너
            GameObject slotsContainer = new GameObject("SlotsContainer");
            slotsContainer.transform.SetParent(slotPanelGo.transform, false);
            RectTransform slotsContainerRect = slotsContainer.AddComponent<RectTransform>();
            slotsContainerRect.anchorMin = new Vector2(0.5f, 0.5f);
            slotsContainerRect.anchorMax = new Vector2(0.5f, 0.5f);
            slotsContainerRect.pivot = new Vector2(0.5f, 0.5f);
            slotsContainerRect.anchoredPosition = new Vector2(0f, 15f);
            slotsContainerRect.sizeDelta = new Vector2(920f, 420f);

            uiScript.slotRows = new SaveFileUI.SlotRowUI[5];
            float startY = 160f;
            float spacingY = 80f;

            for (int i = 0; i < 5; i++)
            {
                int slotIdx = i + 1;
                float currentY = startY - (i * spacingY);

                var slotRow = CreateSlotRow(
                    slotsContainer.transform, $"SlotRow_{slotIdx}",
                    new Vector2(0f, currentY), new Vector2(900f, 70f),
                    slotIdx, cardBgSprite, btnLongRed, fontAsset);

                uiScript.slotRows[i] = slotRow;
            }

            // [ 뒤로 가기 버튼 ]
            var (backModeBtnGo, backModeBtn, _) = CreateSimpleButton(
                slotPanelGo.transform, "BackToModeButton",
                new Vector2(0f, -270f), new Vector2(240f, 55f),
                "뒤로 가기 (BACK)", btnMidlBlue, fontAsset, 22);
            uiScript.backToModeButton = backModeBtn;

            // ==========================================
            // 9. 확인 모달 (Confirm Modal)
            // ==========================================
            var (confirmModalGo, confirmMsgTmp, confirmYesBtn, confirmNoBtn) = CreateDialogModal(
                canvasGo.transform, "ConfirmModal",
                "슬롯에 기존 저장 데이터가 있습니다.\n덮어쓰고 새로 시작하시겠습니까?",
                "확인", "취소", btnMidlYellow, btnMidlBlue, fontAsset);
            uiScript.confirmModal = confirmModalGo;
            uiScript.confirmMessageText = confirmMsgTmp;
            uiScript.confirmYesButton = confirmYesBtn;
            uiScript.confirmNoButton = confirmNoBtn;
            confirmModalGo.SetActive(false);

            // ==========================================
            // 10. 알림 모달 (Notice Modal)
            // ==========================================
            var (noticeModalGo, noticeMsgTmp, noticeOkBtn) = CreateNoticeModal(
                canvasGo.transform, "NoticeModal",
                "저장된 데이터가 없습니다!",
                "확인", btnMidlBlue, fontAsset);
            uiScript.noticeModal = noticeModalGo;
            uiScript.noticeMessageText = noticeMsgTmp;
            uiScript.noticeOkButton = noticeOkBtn;
            noticeModalGo.SetActive(false);

            // 기본 활성화 상태 설정
            modePanelGo.SetActive(true);
            slotPanelGo.SetActive(false);

            EditorSceneManager.MarkSceneDirty(saveScene);
            EditorSceneManager.SaveScene(saveScene);
            Debug.Log("<color=green>[SaveFileUIBuilder]</color> 세이브 파일 씬(savefile.unity) UI 빌드 및 저장 성공!");
            if (interactive)
            {
                EditorUtility.DisplayDialog("완료", "세이브 파일 UI가 성공적으로 생성되었습니다!", "확인");
            }
        }
        finally
        {
            if (!isAlreadyOpen)
            {
                EditorSceneManager.CloseScene(saveScene, true);
                if (prevActiveScene.IsValid())
                {
                    EditorSceneManager.SetActiveScene(prevActiveScene);
                }
            }
        }
    }

    private static SaveFileUI.SlotRowUI CreateSlotRow(
        Transform parent, string name, Vector2 pos, Vector2 size,
        int slotIndex, Sprite bgSprite, Sprite deleteSprite, TMP_FontAsset font)
    {
        GameObject rowGo = new GameObject(name);
        rowGo.transform.SetParent(parent, false);
        RectTransform rowRect = rowGo.AddComponent<RectTransform>();
        rowRect.anchoredPosition = pos;
        rowRect.sizeDelta = size;

        Image rowBg = rowGo.AddComponent<Image>();
        if (bgSprite != null)
        {
            rowBg.sprite = bgSprite;
            rowBg.type = Image.Type.Sliced;
            rowBg.color = new Color(0.16f, 0.19f, 0.25f, 0.95f);
        }
        else
        {
            rowBg.color = new Color(0.18f, 0.20f, 0.28f, 0.95f);
        }

        Button slotBtn = rowGo.AddComponent<Button>();
        ColorBlock colors = slotBtn.colors;
        colors.highlightedColor = new Color(0.28f, 0.33f, 0.44f, 1f);
        colors.pressedColor = new Color(0.12f, 0.15f, 0.22f, 1f);
        slotBtn.colors = colors;

        // 슬롯 라벨 (예: SLOT 01)
        GameObject titleGo = new GameObject("SlotLabel");
        titleGo.transform.SetParent(rowGo.transform, false);
        RectTransform titleRect = titleGo.AddComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 0.5f);
        titleRect.anchorMax = new Vector2(0f, 0.5f);
        titleRect.pivot = new Vector2(0f, 0.5f);
        titleRect.anchoredPosition = new Vector2(25f, 0f);
        titleRect.sizeDelta = new Vector2(160f, 50f);

        TextMeshProUGUI titleTmp = titleGo.AddComponent<TextMeshProUGUI>();
        if (font != null) titleTmp.font = font;
        titleTmp.text = $"SLOT {slotIndex:00}";
        titleTmp.fontSize = 26;
        titleTmp.fontStyle = FontStyles.Bold;
        titleTmp.alignment = TextAlignmentOptions.Left;
        titleTmp.color = new Color(1f, 0.85f, 0.35f, 1f);

        // 슬롯 상세 정보 (스테이지 / 일시)
        GameObject infoGo = new GameObject("SlotInfo");
        infoGo.transform.SetParent(rowGo.transform, false);
        RectTransform infoRect = infoGo.AddComponent<RectTransform>();
        infoRect.anchorMin = new Vector2(0f, 0.5f);
        infoRect.anchorMax = new Vector2(1f, 0.5f);
        infoRect.pivot = new Vector2(0f, 0.5f);
        infoRect.anchoredPosition = new Vector2(200f, 0f);
        infoRect.sizeDelta = new Vector2(-320f, 50f);

        TextMeshProUGUI infoTmp = infoGo.AddComponent<TextMeshProUGUI>();
        if (font != null) infoTmp.font = font;
        infoTmp.text = "<color=#888888>[ 빈 슬롯 (EMPTY) ]</color>";
        infoTmp.fontSize = 20;
        infoTmp.alignment = TextAlignmentOptions.Left;
        infoTmp.color = Color.white;

        // 삭제 버튼 (오른쪽)
        GameObject delBtnGo = new GameObject("DeleteButton");
        delBtnGo.transform.SetParent(rowGo.transform, false);
        RectTransform delRect = delBtnGo.AddComponent<RectTransform>();
        delRect.anchorMin = new Vector2(1f, 0.5f);
        delRect.anchorMax = new Vector2(1f, 0.5f);
        delRect.pivot = new Vector2(1f, 0.5f);
        delRect.anchoredPosition = new Vector2(-15f, 0f);
        delRect.sizeDelta = new Vector2(85f, 44f);

        Image delImg = delBtnGo.AddComponent<Image>();
        if (deleteSprite != null)
        {
            delImg.sprite = deleteSprite;
            delImg.type = Image.Type.Simple;
        }
        else
        {
            delImg.color = new Color(0.8f, 0.2f, 0.2f, 1f);
        }

        Button delBtn = delBtnGo.AddComponent<Button>();

        GameObject delTextGo = new GameObject("Text");
        delTextGo.transform.SetParent(delBtnGo.transform, false);
        RectTransform delTextRect = delTextGo.AddComponent<RectTransform>();
        delTextRect.anchorMin = Vector2.zero;
        delTextRect.anchorMax = Vector2.one;
        delTextRect.sizeDelta = Vector2.zero;

        TextMeshProUGUI delTmp = delTextGo.AddComponent<TextMeshProUGUI>();
        if (font != null) delTmp.font = font;
        delTmp.text = "삭제";
        delTmp.fontSize = 18;
        delTmp.fontStyle = FontStyles.Bold;
        delTmp.alignment = TextAlignmentOptions.Center;
        delTmp.color = Color.white;

        return new SaveFileUI.SlotRowUI
        {
            slotIndex = slotIndex,
            slotButton = slotBtn,
            slotTitleText = titleTmp,
            slotInfoText = infoTmp,
            deleteButton = delBtn,
            background = rowBg
        };
    }

    private static (GameObject, Button, TextMeshProUGUI) CreateBigMenuButton(
        Transform parent, string name, Vector2 pos, Vector2 size,
        string title, string subtitle, Sprite btnSprite, TMP_FontAsset font)
    {
        GameObject btnGo = new GameObject(name);
        btnGo.transform.SetParent(parent, false);
        RectTransform rect = btnGo.AddComponent<RectTransform>();
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;

        Image img = btnGo.AddComponent<Image>();
        if (btnSprite != null)
        {
            img.sprite = btnSprite;
            img.type = Image.Type.Simple;
        }
        else
        {
            img.color = new Color(0.2f, 0.5f, 0.8f, 1f);
        }

        Button btn = btnGo.AddComponent<Button>();

        // 상단 타이틀
        GameObject titleGo = new GameObject("Title");
        titleGo.transform.SetParent(btnGo.transform, false);
        RectTransform titleRect = titleGo.AddComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 0.5f);
        titleRect.anchorMax = new Vector2(0.5f, 0.5f);
        titleRect.pivot = new Vector2(0.5f, 0.5f);
        titleRect.anchoredPosition = new Vector2(0f, 12f);
        titleRect.sizeDelta = new Vector2(size.x - 20f, 44f);

        TextMeshProUGUI titleTmp = titleGo.AddComponent<TextMeshProUGUI>();
        if (font != null) titleTmp.font = font;
        titleTmp.text = title;
        titleTmp.fontSize = 28;
        titleTmp.fontStyle = FontStyles.Bold;
        titleTmp.alignment = TextAlignmentOptions.Center;
        titleTmp.color = Color.white;

        // 하단 부제목
        GameObject subGo = new GameObject("Subtitle");
        subGo.transform.SetParent(btnGo.transform, false);
        RectTransform subRect = subGo.AddComponent<RectTransform>();
        subRect.anchorMin = new Vector2(0.5f, 0.5f);
        subRect.anchorMax = new Vector2(0.5f, 0.5f);
        subRect.pivot = new Vector2(0.5f, 0.5f);
        subRect.anchoredPosition = new Vector2(0f, -22f);
        subRect.sizeDelta = new Vector2(size.x - 20f, 30f);

        TextMeshProUGUI subTmp = subGo.AddComponent<TextMeshProUGUI>();
        if (font != null) subTmp.font = font;
        subTmp.text = subtitle;
        subTmp.fontSize = 15;
        subTmp.alignment = TextAlignmentOptions.Center;
        subTmp.color = new Color(0.9f, 0.95f, 1f, 0.85f);

        return (btnGo, btn, titleTmp);
    }

    private static (GameObject, Button, TextMeshProUGUI) CreateSimpleButton(
        Transform parent, string name, Vector2 pos, Vector2 size,
        string text, Sprite btnSprite, TMP_FontAsset font, float fontSize)
    {
        GameObject btnGo = new GameObject(name);
        btnGo.transform.SetParent(parent, false);
        RectTransform rect = btnGo.AddComponent<RectTransform>();
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;

        Image img = btnGo.AddComponent<Image>();
        if (btnSprite != null)
        {
            img.sprite = btnSprite;
            img.type = Image.Type.Simple;
        }
        else
        {
            img.color = new Color(0.25f, 0.35f, 0.5f, 1f);
        }

        Button btn = btnGo.AddComponent<Button>();

        GameObject textGo = new GameObject("Text");
        textGo.transform.SetParent(btnGo.transform, false);
        RectTransform textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;

        TextMeshProUGUI tmp = textGo.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;

        return (btnGo, btn, tmp);
    }

    private static (GameObject, TextMeshProUGUI, Button, Button) CreateDialogModal(
        Transform parent, string name, string defaultMsg,
        string yesText, string noText, Sprite yesSprite, Sprite noSprite, TMP_FontAsset font)
    {
        GameObject modalGo = new GameObject(name);
        modalGo.transform.SetParent(parent, false);
        RectTransform modalRect = modalGo.AddComponent<RectTransform>();
        modalRect.anchorMin = Vector2.zero;
        modalRect.anchorMax = Vector2.one;
        modalRect.sizeDelta = Vector2.zero;

        // 어두운 배경
        Image dim = modalGo.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.75f);

        // 팝업 박스
        GameObject boxGo = new GameObject("Box");
        boxGo.transform.SetParent(modalGo.transform, false);
        RectTransform boxRect = boxGo.AddComponent<RectTransform>();
        boxRect.anchoredPosition = Vector2.zero;
        boxRect.sizeDelta = new Vector2(560f, 280f);
        Image boxImg = boxGo.AddComponent<Image>();
        boxImg.color = new Color(0.14f, 0.16f, 0.22f, 0.98f);

        // 메시지 텍스트
        TextMeshProUGUI msgTmp = CreateText(boxGo.transform, "Message", defaultMsg,
            new Vector2(0f, 40f), new Vector2(500f, 100f), 22, FontStyles.Bold, TextAlignmentOptions.Center, font, Color.white);

        // 예 / 확인 버튼
        var (_, yesBtn, _) = CreateSimpleButton(boxGo.transform, "YesButton",
            new Vector2(-110f, -70f), new Vector2(170f, 50f), yesText, yesSprite, font, 20);

        // 아니오 / 취소 버튼
        var (_, noBtn, _) = CreateSimpleButton(boxGo.transform, "NoButton",
            new Vector2(110f, -70f), new Vector2(170f, 50f), noText, noSprite, font, 20);

        return (modalGo, msgTmp, yesBtn, noBtn);
    }

    private static (GameObject, TextMeshProUGUI, Button) CreateNoticeModal(
        Transform parent, string name, string defaultMsg,
        string okText, Sprite okSprite, TMP_FontAsset font)
    {
        GameObject modalGo = new GameObject(name);
        modalGo.transform.SetParent(parent, false);
        RectTransform modalRect = modalGo.AddComponent<RectTransform>();
        modalRect.anchorMin = Vector2.zero;
        modalRect.anchorMax = Vector2.one;
        modalRect.sizeDelta = Vector2.zero;

        Image dim = modalGo.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.75f);

        GameObject boxGo = new GameObject("Box");
        boxGo.transform.SetParent(modalGo.transform, false);
        RectTransform boxRect = boxGo.AddComponent<RectTransform>();
        boxRect.anchoredPosition = Vector2.zero;
        boxRect.sizeDelta = new Vector2(520f, 240f);
        Image boxImg = boxGo.AddComponent<Image>();
        boxImg.color = new Color(0.14f, 0.16f, 0.22f, 0.98f);

        TextMeshProUGUI msgTmp = CreateText(boxGo.transform, "Message", defaultMsg,
            new Vector2(0f, 30f), new Vector2(460f, 90f), 22, FontStyles.Bold, TextAlignmentOptions.Center, font, Color.white);

        var (_, okBtn, _) = CreateSimpleButton(boxGo.transform, "OkButton",
            new Vector2(0f, -60f), new Vector2(180f, 48f), okText, okSprite, font, 20);

        return (modalGo, msgTmp, okBtn);
    }

    private static TextMeshProUGUI CreateText(
        Transform parent, string name, string content,
        Vector2 pos, Vector2 size, float fontSize, FontStyles style,
        TextAlignmentOptions align, TMP_FontAsset font, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = content;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.color = color;

        return tmp;
    }
}
#endif
