using System;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 세이브 슬롯 선택 및 모드(새 게임/이어하기) 관리 UI.
/// 로비(TitleScreen)와 동일한 픽셀 아트 우드 & 골드 테마를 일관되게 적용합니다.
/// </summary>
public class SaveFileUI : MonoBehaviour
{
    public enum SlotActionType
    {
        NewGame,
        LoadGame
    }

    [System.Serializable]
    public class SlotRowUI
    {
        public int slotIndex = 1;
        public Button slotButton;
        public TextMeshProUGUI slotTitleText;
        public TextMeshProUGUI slotInfoText;
        public Button deleteButton;
        public Image background;
    }

    [Header("Panels")]
    public GameObject modeSelectPanel;
    public GameObject slotSelectPanel;

    [Header("Mode Select UI")]
    public Button newGameModeButton;
    public Button loadGameModeButton;
    public Button backToCharacterSelectButton;

    [Header("Slot Select UI")]
    public TextMeshProUGUI slotPanelTitleText;
    public TextMeshProUGUI slotPanelSubtitleText;
    public Button backToModeButton;
    public SlotRowUI[] slotRows = new SlotRowUI[5];

    [Header("Confirm Modal (Overwrite / Delete)")]
    public GameObject confirmModal;
    public TextMeshProUGUI confirmMessageText;
    public Button confirmYesButton;
    public Button confirmNoButton;

    [Header("Notice Modal (Alerts)")]
    public GameObject noticeModal;
    public TextMeshProUGUI noticeMessageText;
    public Button noticeOkButton;

    [Header("Multiplayer / Host Notice")]
    public TextMeshProUGUI clientNoticeText;

    private SlotActionType _currentAction = SlotActionType.NewGame;
    private Action _pendingConfirmAction = null;
    private GameObject _waitingPanelGo;
    private TextMeshProUGUI _waitingSubtitleText;
    private TextMeshProUGUI _waitingInfoText;

    private void Start()
    {
        ApplyLobbyTheme();
        BindEvents();
        CheckNetworkAuthority();
    }

    /// <summary>
    /// 로비와 통일된 우드/골드 픽셀 테마 적용
    /// </summary>
    private void ApplyLobbyTheme()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = GetComponent<Canvas>();

        if (canvas != null)
        {
            // 1) 로비와 동일한 숲 배경 적용
            LobbyUITheme.ApplyBackground(canvas.transform);

            // 기존의 평면 단색 Background 및 DarkOverlay 오브젝트 비활성화
            Transform legacyBg = canvas.transform.Find("Background");
            if (legacyBg != null)
            {
                legacyBg.gameObject.SetActive(false);
            }

            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;
            }
        }

        // 2) 모드 선택 패널 스타일링
        if (modeSelectPanel != null)
        {
            LobbyUITheme.StyleWoodPanel(modeSelectPanel, LobbyUITheme.WoodFill, 8f, 6f);

            Transform mainTitleTrans = modeSelectPanel.transform.Find("MainTitle");
            if (mainTitleTrans != null)
            {
                var titleTmp = mainTitleTrans.GetComponent<TextMeshProUGUI>();
                if (titleTmp != null) LobbyUITheme.StyleTitle(titleTmp, "VOICE ADVENTURE", 58f);
            }

            Transform subTrans = modeSelectPanel.transform.Find("ModeSubtitle");
            if (subTrans != null)
            {
                var subTmp = subTrans.GetComponent<TextMeshProUGUI>();
                if (subTmp != null) LobbyUITheme.StyleSubtitle(subTmp, "모드를 선택하세요", 26f);
            }

            if (newGameModeButton != null)
            {
                LobbyUITheme.StylePixelButton(newGameModeButton, LobbyUITheme.ButtonType.Gold, "새 게임 (NEW GAME)", 30);
            }

            if (loadGameModeButton != null)
            {
                LobbyUITheme.StylePixelButton(loadGameModeButton, LobbyUITheme.ButtonType.Stone, "이어하기 (LOAD GAME)", 30);
            }

            if (backToCharacterSelectButton != null)
            {
                LobbyUITheme.StylePixelButton(backToCharacterSelectButton, LobbyUITheme.ButtonType.Stone, "뒤로가기 (BACK)", 24);
            }
        }

        // 3) 슬롯 선택 패널 스타일링
        if (slotSelectPanel != null)
        {
            LobbyUITheme.StyleWoodPanel(slotSelectPanel, LobbyUITheme.WoodFill, 8f, 6f);

            if (slotPanelTitleText != null)
            {
                LobbyUITheme.StyleTitle(slotPanelTitleText, null, 46f);
            }

            if (slotPanelSubtitleText != null)
            {
                LobbyUITheme.StyleSubtitle(slotPanelSubtitleText, null, 24f);
            }

            if (backToModeButton != null)
            {
                LobbyUITheme.StylePixelButton(backToModeButton, LobbyUITheme.ButtonType.Stone, "뒤로가기 (BACK)", 26);
            }

            // 슬롯 행들 스타일링
            if (slotRows != null)
            {
                for (int i = 0; i < slotRows.Length; i++)
                {
                    var row = slotRows[i];
                    if (row == null) continue;

                    if (row.slotButton != null)
                    {
                        LobbyUITheme.StyleInsetBox(row.slotButton.gameObject, LobbyUITheme.FieldBg, LobbyUITheme.WoodDark, 3f);
                    }

                    if (row.slotTitleText != null)
                    {
                        LobbyUITheme.StyleTitle(row.slotTitleText, null, 26f);
                    }

                    if (row.deleteButton != null)
                    {
                        LobbyUITheme.StylePixelButton(row.deleteButton, LobbyUITheme.ButtonType.Red, "삭제", 20);
                    }
                }
            }
        }

        // 4) 확인 모달 스타일링
        if (confirmModal != null)
        {
            Transform box = confirmModal.transform.Find("Box");
            if (box != null) LobbyUITheme.StyleWoodPanel(box.gameObject, LobbyUITheme.WoodFill, 8f, 6f);

            Transform darkOverlay = confirmModal.transform.Find("DarkOverlay");
            if (darkOverlay != null)
            {
                var img = darkOverlay.GetComponent<Image>();
                if (img != null) img.color = new Color(0f, 0f, 0f, 0.72f);
            }

            if (confirmYesButton != null)
            {
                LobbyUITheme.StylePixelButton(confirmYesButton, LobbyUITheme.ButtonType.Green, "확인", 24);
            }

            if (confirmNoButton != null)
            {
                LobbyUITheme.StylePixelButton(confirmNoButton, LobbyUITheme.ButtonType.Red, "취소", 24);
            }

            if (confirmMessageText != null)
            {
                LobbyUITheme.StyleSubtitle(confirmMessageText, null, 24f);
            }
        }

        // 5) 안내 모달 스타일링
        if (noticeModal != null)
        {
            Transform box = noticeModal.transform.Find("Box");
            if (box != null) LobbyUITheme.StyleWoodPanel(box.gameObject, LobbyUITheme.WoodFill, 8f, 6f);

            Transform darkOverlay = noticeModal.transform.Find("DarkOverlay");
            if (darkOverlay != null)
            {
                var img = darkOverlay.GetComponent<Image>();
                if (img != null) img.color = new Color(0f, 0f, 0f, 0.72f);
            }

            if (noticeOkButton != null)
            {
                LobbyUITheme.StylePixelButton(noticeOkButton, LobbyUITheme.ButtonType.Gold, "확인", 24);
            }

            if (noticeMessageText != null)
            {
                LobbyUITheme.StyleSubtitle(noticeMessageText, null, 24f);
            }
        }

        // 6) 멀티플레이어 안내 텍스트
        if (clientNoticeText != null)
        {
            LobbyUITheme.StyleSubtitle(clientNoticeText, null, 24f);
        }
    }

    private void BindEvents()
    {
        if (newGameModeButton != null)
        {
            newGameModeButton.onClick.RemoveAllListeners();
            newGameModeButton.onClick.AddListener(() => OpenSlotSelect(SlotActionType.NewGame));
        }

        if (loadGameModeButton != null)
        {
            loadGameModeButton.onClick.RemoveAllListeners();
            loadGameModeButton.onClick.AddListener(() => OpenSlotSelect(SlotActionType.LoadGame));
        }

        if (backToCharacterSelectButton != null)
        {
            backToCharacterSelectButton.onClick.RemoveAllListeners();
            backToCharacterSelectButton.onClick.AddListener(OnClickBackToCharacterSelect);
        }

        if (backToModeButton != null)
        {
            backToModeButton.onClick.RemoveAllListeners();
            backToModeButton.onClick.AddListener(ShowModeSelect);
        }

        if (confirmYesButton != null)
        {
            confirmYesButton.onClick.RemoveAllListeners();
            confirmYesButton.onClick.AddListener(() =>
            {
                CloseConfirmModal();
                _pendingConfirmAction?.Invoke();
                _pendingConfirmAction = null;
            });
        }

        if (confirmNoButton != null)
        {
            confirmNoButton.onClick.RemoveAllListeners();
            confirmNoButton.onClick.AddListener(() =>
            {
                CloseConfirmModal();
                _pendingConfirmAction = null;
            });
        }

        if (noticeOkButton != null)
        {
            noticeOkButton.onClick.RemoveAllListeners();
            noticeOkButton.onClick.AddListener(CloseNoticeModal);
        }

        if (slotRows != null)
        {
            for (int i = 0; i < slotRows.Length; i++)
            {
                int slotIndex = i + 1;
                var row = slotRows[i];
                if (row == null) continue;

                row.slotIndex = slotIndex;

                if (row.slotButton != null)
                {
                    row.slotButton.onClick.RemoveAllListeners();
                    row.slotButton.onClick.AddListener(() => OnClickSlot(slotIndex));
                }

                if (row.deleteButton != null)
                {
                    row.deleteButton.onClick.RemoveAllListeners();
                    row.deleteButton.onClick.AddListener(() => OnClickDeleteSlot(slotIndex));
                }
            }
        }
    }

    private void Update()
    {
        // 멀티플레이 클라이언트인지 지속 확인하여 세이브 파일 패널이 뜨지 않도록 방어
        var runner = FindFirstObjectByType<NetworkRunner>();
        if (runner != null && runner.IsRunning && !runner.IsServer)
        {
            if (_waitingPanelGo == null || !_waitingPanelGo.activeSelf)
            {
                ShowClientWaitingView();
            }
        }
    }

    private void CheckNetworkAuthority()
    {
        var runner = FindFirstObjectByType<NetworkRunner>();
        bool isMultiplayer = runner != null && runner.IsRunning;
        bool isHost = isMultiplayer ? runner.IsServer : true;

        if (isMultiplayer && !isHost)
        {
            // join 인원(클라이언트): 세이브 파일 선택 화면을 패스하고 대기화면으로 직행
            ShowClientWaitingView();
        }
        else
        {
            // 호스트 또는 싱글플레이: 세이브 파일 모드 선택 화면 표시
            if (_waitingPanelGo != null) _waitingPanelGo.SetActive(false);
            ShowModeSelect();
        }
    }

    private void ShowClientWaitingView()
    {
        EnsureWaitingPanel();

        if (_waitingPanelGo != null) _waitingPanelGo.SetActive(true);
        if (modeSelectPanel != null) modeSelectPanel.SetActive(false);
        if (slotSelectPanel != null) slotSelectPanel.SetActive(false);
        CloseConfirmModal();
        CloseNoticeModal();

        UpdateWaitingInfo();
    }

    private void EnsureWaitingPanel()
    {
        if (_waitingPanelGo != null) return;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = GetComponent<Canvas>();
        Transform parentTrans = (canvas != null) ? canvas.transform : transform;

        GameObject panel = new GameObject("ClientWaitingPanel", typeof(RectTransform));
        panel.transform.SetParent(parentTrans, false);
        _waitingPanelGo = panel;

        var rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(920f, 500f);
        rt.anchoredPosition = Vector2.zero;

        LobbyUITheme.StyleWoodPanel(panel, LobbyUITheme.WoodFill, 8f, 6f);

        // 1. 타이틀
        GameObject titleGo = new GameObject("WaitingTitle", typeof(RectTransform), typeof(TextMeshProUGUI));
        titleGo.transform.SetParent(panel.transform, false);
        var trt = titleGo.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(0.5f, 1f);
        trt.anchorMax = new Vector2(0.5f, 1f);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.sizeDelta = new Vector2(860f, 65f);
        trt.anchoredPosition = new Vector2(0f, -40f);
        var tTmp = titleGo.GetComponent<TextMeshProUGUI>();
        LobbyUITheme.StyleTitle(tTmp, "방장 대기 중 (WAITING FOR HOST)", 48f);

        // 2. 서브타이틀
        GameObject subGo = new GameObject("WaitingSubtitle", typeof(RectTransform), typeof(TextMeshProUGUI));
        subGo.transform.SetParent(panel.transform, false);
        var srt = subGo.GetComponent<RectTransform>();
        srt.anchorMin = new Vector2(0.5f, 1f);
        srt.anchorMax = new Vector2(0.5f, 1f);
        srt.pivot = new Vector2(0.5f, 1f);
        srt.sizeDelta = new Vector2(860f, 40f);
        srt.anchoredPosition = new Vector2(0f, -115f);
        _waitingSubtitleText = subGo.GetComponent<TextMeshProUGUI>();
        if (tTmp.font != null) _waitingSubtitleText.font = tTmp.font;
        LobbyUITheme.StyleSubtitle(_waitingSubtitleText, "방장(Host)이 세이브 파일 및 게임 모드를 선택하고 있습니다.", 22f);

        // 3. 중앙 인셋 박스
        GameObject inset = new GameObject("WaitingInset", typeof(RectTransform), typeof(Image));
        inset.transform.SetParent(panel.transform, false);
        var irt = inset.GetComponent<RectTransform>();
        irt.anchorMin = new Vector2(0.5f, 0.5f);
        irt.anchorMax = new Vector2(0.5f, 0.5f);
        irt.pivot = new Vector2(0.5f, 0.5f);
        irt.sizeDelta = new Vector2(800f, 190f);
        irt.anchoredPosition = new Vector2(0f, -15f);
        LobbyUITheme.StyleInsetBox(inset, LobbyUITheme.FieldBg, LobbyUITheme.WoodDark, 4f);

        // 4. 인셋 내부 정보 텍스트
        GameObject infoGo = new GameObject("WaitingInfoText", typeof(RectTransform), typeof(TextMeshProUGUI));
        infoGo.transform.SetParent(inset.transform, false);
        var infort = infoGo.GetComponent<RectTransform>();
        infort.anchorMin = new Vector2(0f, 0f);
        infort.anchorMax = new Vector2(1f, 1f);
        infort.sizeDelta = new Vector2(-40f, -20f);
        infort.anchoredPosition = Vector2.zero;
        _waitingInfoText = infoGo.GetComponent<TextMeshProUGUI>();
        if (tTmp.font != null) _waitingInfoText.font = tTmp.font;
        _waitingInfoText.fontSize = 22f;
        _waitingInfoText.alignment = TextAlignmentOptions.Center;

        // 5. 하단 안내 팁
        GameObject tipGo = new GameObject("WaitingTip", typeof(RectTransform), typeof(TextMeshProUGUI));
        tipGo.transform.SetParent(panel.transform, false);
        var tiprt = tipGo.GetComponent<RectTransform>();
        tiprt.anchorMin = new Vector2(0.5f, 0f);
        tiprt.anchorMax = new Vector2(0.5f, 0f);
        tiprt.pivot = new Vector2(0.5f, 0f);
        tiprt.sizeDelta = new Vector2(860f, 35f);
        tiprt.anchoredPosition = new Vector2(0f, 25f);
        var tipTmp = tipGo.GetComponent<TextMeshProUGUI>();
        if (tTmp.font != null) tipTmp.font = tTmp.font;
        LobbyUITheme.StyleSubtitle(tipTmp, "Tip: 선택이 완료되면 자동으로 인게임으로 전환됩니다.", 19f);
    }

    private void UpdateWaitingInfo()
    {
        if (_waitingInfoText == null) return;

        string roomCode = BasicLauncher.Instance != null ? BasicLauncher.Instance.RoomCode : "";
        string codeStr = !string.IsNullOrEmpty(roomCode) ? $"<color=#FAD16B>방 코드: {roomCode}</color>\n" : "";

        string myCharStr = "";
        var items = FindObjectsByType<SessionPlayerItem>(UnityEngine.FindObjectsSortMode.None);
        var runner = FindFirstObjectByType<NetworkRunner>();
        if (runner != null)
        {
            foreach (var item in items)
            {
                if (item.Object != null && (item.Object.HasInputAuthority || item.Object.InputAuthority == runner.LocalPlayer))
                {
                    string charName = (item.SelectedCharacterIndex == 0) ? "여우 (FOXY)" : (item.SelectedCharacterIndex == 1) ? "토끼 (RABBIT)" : "선택됨";
                    myCharStr = $"<color=#74D974>내 캐릭터: {charName}</color>\n";
                    break;
                }
            }
        }

        _waitingInfoText.text = $"{codeStr}{myCharStr}\n<color=#FAF2E3>방장이 세이브 슬롯을 선택하면\n곧바로 스테이지가 시작됩니다. 잠시만 기다려 주세요...</color>";
    }

    public void ShowModeSelect()
    {
        if (_waitingPanelGo != null) _waitingPanelGo.SetActive(false);
        if (modeSelectPanel != null) modeSelectPanel.SetActive(true);
        if (slotSelectPanel != null) slotSelectPanel.SetActive(false);
        CloseConfirmModal();
        CloseNoticeModal();
    }

    public void OpenSlotSelect(SlotActionType actionType)
    {
        _currentAction = actionType;

        if (modeSelectPanel != null) modeSelectPanel.SetActive(false);
        if (slotSelectPanel != null) slotSelectPanel.SetActive(true);

        if (slotPanelTitleText != null)
        {
            slotPanelTitleText.text = (_currentAction == SlotActionType.NewGame)
                ? "새 게임 - 슬롯 선택"
                : "이어하기 - 슬롯 선택";
            LobbyUITheme.StyleTitle(slotPanelTitleText, null, 46f);
        }

        if (slotPanelSubtitleText != null)
        {
            slotPanelSubtitleText.text = (_currentAction == SlotActionType.NewGame)
                ? "시작할 세이브 슬롯을 선택하세요 (1 ~ 5)"
                : "불러올 세이브 슬롯을 선택하세요 (1 ~ 5)";
            LobbyUITheme.StyleSubtitle(slotPanelSubtitleText, null, 24f);
        }

        RefreshSlotList();
    }

    public void RefreshSlotList()
    {
        if (slotRows == null) return;

        for (int i = 0; i < slotRows.Length; i++)
        {
            int slotIndex = i + 1;
            var row = slotRows[i];
            if (row == null) continue;

            var data = SaveDataManager.GetSlot(slotIndex);

            if (row.slotTitleText != null)
            {
                row.slotTitleText.text = $"SLOT {slotIndex:00}";
                LobbyUITheme.StyleTitle(row.slotTitleText, null, 26f);
            }

            if (row.slotInfoText != null)
            {
                if (data != null && data.hasData)
                {
                    row.slotInfoText.text = $"<color=#FAD16B><b>{data.stageDisplayName}</b></color>   <color=#FAF2E3>{data.updatedTime}</color>";
                }
                else
                {
                    row.slotInfoText.text = "<color=#8C7B6B>[ 빈 슬롯 (EMPTY) ]</color>";
                }
            }

            if (row.deleteButton != null)
            {
                row.deleteButton.gameObject.SetActive(data != null && data.hasData);
            }
        }
    }

    public void OnClickSlot(int slotIndex)
    {
        var runner = FindFirstObjectByType<NetworkRunner>();
        if (runner != null && runner.IsRunning && !runner.IsServer)
        {
            ShowNoticeModal("방장(Host)만 세이브 슬롯을 선택할 수 있습니다.");
            return;
        }

        bool hasData = SaveDataManager.HasSlotData(slotIndex);

        if (_currentAction == SlotActionType.NewGame)
        {
            if (hasData)
            {
                _pendingConfirmAction = () => ProceedNewGame(slotIndex);
                ShowConfirmModal($"슬롯 {slotIndex}에 이미 저장된 데이터가 있습니다.\n덮어쓰고 새로 시작하시겠습니까?");
            }
            else
            {
                ProceedNewGame(slotIndex);
            }
        }
        else // LoadGame
        {
            if (hasData)
            {
                ProceedLoadGame(slotIndex);
            }
            else
            {
                ShowNoticeModal($"슬롯 {slotIndex}에 저장된 데이터가 없습니다!\n새 게임 모드로 시작해 주세요.");
            }
        }
    }

    private void ProceedNewGame(int slotIndex)
    {
        Debug.Log($"<color=lime>[SaveFileUI]</color> 새 게임 시작 (슬롯 {slotIndex}) -> TutorialStage 로드");
        SaveDataManager.CreateNewGame(slotIndex);
        LoadTargetScene("TutorialStage");
    }

    private void ProceedLoadGame(int slotIndex)
    {
        var data = SaveDataManager.GetSlot(slotIndex);
        SaveDataManager.CurrentSlotIndex = slotIndex;
        string targetScene = string.IsNullOrEmpty(data.stageName) ? "TutorialStage" : data.stageName;

        // 이어하기: 이전 스테이지들의 누적 기록 + 개별 구간 기록을 복원 (최종 합산·나열에 반영)
        RunManager.RestoreAccumulated(data.totalPlayTime);
        RunManager.RestoreSegments(data.segmentTimes);

        Debug.Log($"<color=lime>[SaveFileUI]</color> 이어하기 (슬롯 {slotIndex}) -> {targetScene} 로드");
        LoadTargetScene(targetScene);
    }

    private void OnClickDeleteSlot(int slotIndex)
    {
        var runner = FindFirstObjectByType<NetworkRunner>();
        if (runner != null && runner.IsRunning && !runner.IsServer)
        {
            ShowNoticeModal("방장(Host)만 세이브 슬롯을 삭제할 수 있습니다.");
            return;
        }

        _pendingConfirmAction = () =>
        {
            SaveDataManager.DeleteSlot(slotIndex);
            RefreshSlotList();
        };
        ShowConfirmModal($"슬롯 {slotIndex}의 세이브 데이터를 정말 삭제하시겠습니까?\n삭제된 데이터는 복구할 수 없습니다.");
    }

    private void OnClickBackToCharacterSelect()
    {
        LoadTargetScene("CharacterSelect");
    }

    private void LoadTargetScene(string sceneName)
    {
        var runner = FindFirstObjectByType<NetworkRunner>();
        if (runner != null && runner.IsRunning)
        {
            if (runner.IsServer)
            {
                int sceneIndex = SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{sceneName}.unity");
                if (sceneIndex >= 0)
                {
                    runner.LoadScene(SceneRef.FromIndex(sceneIndex));
                }
                else
                {
                    runner.LoadScene(sceneName, LoadSceneMode.Single);
                }
            }
            else
            {
                Debug.LogWarning("[SaveFileUI] 클라이언트는 직접 씬을 전환할 수 없습니다.");
            }
        }
        else
        {
            SceneManager.LoadScene(sceneName);
        }
    }

    private void ShowConfirmModal(string message)
    {
        if (confirmModal != null)
        {
            if (confirmMessageText != null) confirmMessageText.text = message;
            confirmModal.SetActive(true);
        }
    }

    private void CloseConfirmModal()
    {
        if (confirmModal != null) confirmModal.SetActive(false);
    }

    private void ShowNoticeModal(string message)
    {
        if (noticeModal != null)
        {
            if (noticeMessageText != null) noticeMessageText.text = message;
            noticeModal.SetActive(true);
        }
    }

    private void CloseNoticeModal()
    {
        if (noticeModal != null) noticeModal.SetActive(false);
    }
}
