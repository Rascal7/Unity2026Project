using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 캐릭터 선택 화면 UI 컨트롤러.
/// 로비(TitleScreen)와 동일한 비주얼 톤앤매너(우드 프레임, 골드 타이틀, 3D 픽셀 버튼)를 적용합니다.
/// </summary>
public class CharacterSelectUI : MonoBehaviour
{
    [Header("Buttons")]
    public Button[] selectButtons;
    public Button readyButton;

    [Header("Character Selection Highlights")]
    [Tooltip("선택 시 활성화될 외곽선 하이라이트 오브젝트")]
    public GameObject[] selectHighlights;
    [Tooltip("선택 시 버튼 스프라이트 (선택사항)")]
    public Sprite selectedButtonSprite;
    [Tooltip("기본 상태 버튼 스프라이트 (선택사항)")]
    public Sprite normalButtonSprite;

    [Header("Ready Button Visuals")]
    public Image readyButtonImage;
    public Sprite readySprite;
    public Sprite notReadySprite;
    public TextMeshProUGUI readyText;
    public string readyTextString = "준비 완료! (READY)";
    public string notReadyTextString = "준비하기 (READY)";

    [Header("Color Settings (Fallback)")]
    public Color normalColor = Color.white;
    public Color selectedColor = Color.yellow;
    public Color readyColor = Color.green;

    public static CharacterSelectUI Instance { get; private set; }

    private int _currentSelectedIndex = -1;

    // 세션 아이템 폴링 및 실시간 동기화 주기
    private const float ItemPollInterval = 0.15f;
    private const float ItemSpawnGrace = 5f;
    private float _nextPollTime;
    private float _syncStartTime;
    private bool _warnedMissingItem;
    private bool _spawnedFallbackItem;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        Debug.Log("<color=cyan>[UI]</color> CharacterSelectUI 초기화 시작");

        // 1. 로비 테마 비주얼 적용 (배경, 우드 프레임, 폰트, 버튼 텍스트 복구 및 스타일링)
        ApplyLobbyTheme();

        // 2. 캐릭터 선택 버튼 바인딩
        if (selectButtons != null && selectButtons.Length > 0)
        {
            for (int i = 0; i < selectButtons.Length; i++)
            {
                if (selectButtons[i] == null) continue;
                int index = i;
                selectButtons[i].onClick.RemoveAllListeners();
                selectButtons[i].onClick.AddListener(() => OnClickSelect(index));
                Debug.Log($"<color=cyan>[UI]</color> {selectButtons[i].name} -> {index}번 선택 버튼 등록");
            }
        }
        else
        {
            Button[] allButtons = GetComponentsInChildren<Button>(true);
            int selectIdx = 0;
            for (int i = 0; i < allButtons.Length; i++)
            {
                Button btn = allButtons[i];
                if (btn.name.Contains("Ready")) continue;

                int index = selectIdx++;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnClickSelect(index));
                Debug.Log($"<color=cyan>[UI]</color> {btn.name} -> {index}번 선택 버튼 등록 (자동)");
            }
        }

        // 3. 레디 버튼 바인딩
        if (readyButton == null)
        {
            GameObject readyObj = GameObject.Find("ReadyButton");
            if (readyObj == null) readyObj = GameObject.Find("Ready Button");
            if (readyObj != null) readyButton = readyObj.GetComponent<Button>();
        }

        if (readyButton != null)
        {
            readyButton.onClick.RemoveAllListeners();
            readyButton.onClick.AddListener(() => OnClickReady());
            Debug.Log($"<color=cyan>[UI]</color> {readyButton.name} -> Ready 버튼 등록");
        }

        if (readyButtonImage == null && readyButton != null)
        {
            readyButtonImage = readyButton.GetComponent<Image>();
        }

        if (readyText == null && readyButton != null)
        {
            readyText = readyButton.GetComponentInChildren<TextMeshProUGUI>();
        }

        // 초기 시각 상태 적용
        UpdateCharacterSelectionUI(-1);
        UpdateReadyUI(false);
        RefreshAllPlayersUI();
    }

    public void NotifySessionChanged()
    {
        RefreshAllPlayersUI();
    }

    private void Update()
    {
        if (_syncStartTime <= 0f) _syncStartTime = Time.time;
        if (Time.time < _nextPollTime) return;
        _nextPollTime = Time.time + ItemPollInterval;

        // 양 화면에서 상대방과 나의 선택 및 준비 상태를 실시간 동기화 표시
        RefreshAllPlayersUI();
    }

    /// <summary>
    /// 로비와 통일된 비주얼 테마를 적용합니다.
    /// </summary>
    private void ApplyLobbyTheme()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = GetComponent<Canvas>();

        if (canvas != null)
        {
            // 1) 숲 픽셀 아트 전체 배경 적용
            LobbyUITheme.ApplyBackground(canvas.transform);

            // 캔버스 스케일러 통일 (1920x1080)
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;
            }
        }

        // 2) 타이틀 배너 (CHARACTER SELECT + 서브 안내문)
        Transform titleObj = transform.Find("TitleText");
        if (titleObj != null)
        {
            var titleRt = titleObj.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0.5f, 1f);
            titleRt.anchorMax = new Vector2(0.5f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0f, -60f);
            titleRt.sizeDelta = new Vector2(1000f, 75f);

            var titleTmp = titleObj.GetComponent<TextMeshProUGUI>();
            if (titleTmp != null)
            {
                LobbyUITheme.StyleTitle(titleTmp, "CHARACTER SELECT", 54f);
            }

            Transform subObj = transform.Find("SubtitleText") ?? titleObj.Find("SubtitleText");
            if (subObj == null)
            {
                GameObject subGo = new GameObject("SubtitleText", typeof(RectTransform), typeof(TextMeshProUGUI));
                subGo.transform.SetParent(transform, false);
                var srt = subGo.GetComponent<RectTransform>();
                srt.anchorMin = new Vector2(0.5f, 1f);
                srt.anchorMax = new Vector2(0.5f, 1f);
                srt.pivot = new Vector2(0.5f, 1f);
                srt.anchoredPosition = new Vector2(0f, -135f);
                srt.sizeDelta = new Vector2(800f, 36f);
                var subTmp = subGo.GetComponent<TextMeshProUGUI>();
                if (titleTmp != null && titleTmp.font != null) subTmp.font = titleTmp.font;
                LobbyUITheme.StyleSubtitle(subTmp, "원하는 캐릭터를 선택하고 준비를 완료하세요", 22f);
            }
            else
            {
                var srt = subObj.GetComponent<RectTransform>();
                srt.anchorMin = new Vector2(0.5f, 1f);
                srt.anchorMax = new Vector2(0.5f, 1f);
                srt.pivot = new Vector2(0.5f, 1f);
                srt.anchoredPosition = new Vector2(0f, -135f);
                var subTmp = subObj.GetComponent<TextMeshProUGUI>();
                if (subTmp != null) LobbyUITheme.StyleSubtitle(subTmp, "원하는 캐릭터를 선택하고 준비를 완료하세요", 22f);
            }
        }

        // 3) 캐릭터 카드 (여우: 대쉬, 토끼: 슈퍼점프)
        StyleCharacterCard("SlotsContainer/Card_Fox", 0, "여우 (FOXY)", "스킬: 대쉬 [Shift]", "Title/char1");
        StyleCharacterCard("SlotsContainer/Card_Rabbit", 1, "토끼 (RABBIT)", "스킬: 슈퍼점프 [Shift]", "Title/char2");

        // 4) 레디 버튼 초기 스타일링
        if (readyButton != null)
        {
            LobbyUITheme.StylePixelButton(readyButton, LobbyUITheme.ButtonType.Stone, notReadyTextString, 30);
        }
    }

    private void StyleCharacterCard(string path, int cardIndex, string defaultTitle, string defaultSkill, string spritePath)
    {
        Transform cardTrans = transform.Find(path);
        if (cardTrans == null) return;

        GameObject cardGo = cardTrans.gameObject;

        // 우드 프레임 적용
        LobbyUITheme.StyleWoodPanel(cardGo, LobbyUITheme.WoodFill, 8f, 6f);

        // 카드 내 이름 텍스트
        Transform nameTrans = cardTrans.Find("NameText");
        if (nameTrans != null)
        {
            var nameTmp = nameTrans.GetComponent<TextMeshProUGUI>();
            if (nameTmp != null)
            {
                LobbyUITheme.StyleTitle(nameTmp, defaultTitle, 32f);
            }
        }

        // 캐릭터 아이콘 인셋 영역 스타일링
        Transform iconTrans = cardTrans.Find("CharacterIcon") ?? cardTrans.Find("IconBox/CharacterIcon");
        if (iconTrans != null)
        {
            var iconImg = iconTrans.GetComponent<Image>();
            if (iconImg != null)
            {
                iconImg.preserveAspect = true;
                if (iconImg.sprite == null)
                {
                    var sp = Resources.Load<Sprite>(spritePath);
                    if (sp != null) iconImg.sprite = sp;
                }
            }
        }

        // 스킬 텍스트 (대쉬 / 슈퍼점프)
        Transform skillTrans = cardTrans.Find("SkillText") ?? cardTrans.Find("SkillBox/SkillText");
        if (skillTrans != null)
        {
            var skillTmp = skillTrans.GetComponent<TextMeshProUGUI>();
            if (skillTmp != null)
            {
                LobbyUITheme.StyleSubtitle(skillTmp, defaultSkill, 19f);
            }
        }

        // 선택 버튼 (텍스트 "선택하기" 확실하게 생성/부착)
        Transform btnTrans = cardTrans.Find("SelectButton");
        if (btnTrans != null)
        {
            var btn = btnTrans.GetComponent<Button>();
            if (btn != null)
            {
                LobbyUITheme.StylePixelButton(btn, LobbyUITheme.ButtonType.Stone, "선택하기", 24);
            }
        }

        // 선택 하이라이트: 속이 빈 4면 테두리(Hollow Border) 적용 (카드 전체를 노랗게 덮지 않음!)
        Transform hlTrans = cardTrans.Find("HighlightOutline");
        if (hlTrans != null)
        {
            LobbyUITheme.SetupHollowOutline(hlTrans.gameObject, LobbyUITheme.Gold, 6f);
        }
    }

    public void OnClickSelect(int index)
    {
        Debug.Log($"<color=cyan>[UI]</color> {index}번 버튼 클릭!");
        SessionPlayerItem myItem = FindLocalPlayerItem();

        if (myItem != null)
        {
            // 준비 완료 상태에서는 캐릭터 변경 차단
            if (myItem.IsReady)
            {
                Debug.LogWarning("<color=yellow>[UI]</color> 준비 완료 상태에서는 캐릭터를 변경할 수 없습니다. 먼저 준비를 취소하세요.");
                return;
            }

            // 중복 픽 방지: 상대방이 이미 선택한 캐릭터인지 클라이언트 단에서 사전 차단
            var allItems = FindObjectsByType<SessionPlayerItem>(UnityEngine.FindObjectsSortMode.None);
            foreach (var item in allItems)
            {
                if (item != myItem && item.SelectedCharacterIndex == index && index != -1)
                {
                    Debug.LogWarning($"<color=yellow>[UI]</color> 상대방이 이미 선택한 캐릭터({index})이므로 중복 선택할 수 없습니다.");
                    return;
                }
            }

            if (myItem.HasInputAuthority || (myItem.Runner != null && myItem.Object.InputAuthority == myItem.Runner.LocalPlayer))
            {
                Debug.Log($"<color=lime>[UI]</color> {index}번 캐릭터 선택 요청 전송!");
                myItem.RPC_SelectCharacter(index);
                UpdateCharacterSelectionUI(index);
            }
            else
            {
                Debug.LogError("<color=red>[UI]</color> 찾은 아이템에 Input Authority가 없습니다!");
            }
        }
        else
        {
            Debug.LogWarning("<color=yellow>[UI]</color> 내 플레이어 세션 아이템을 동기화 중입니다. 잠시 후 다시 클릭해 주세요.");
        }
    }

    public void OnClickReady()
    {
        var myItem = FindLocalPlayerItem();
        if (myItem != null)
        {
            // 캐릭터 미선택 상태에서 준비 완료 방지
            if (myItem.SelectedCharacterIndex < 0 && !myItem.IsReady)
            {
                Debug.LogWarning("<color=yellow>[UI]</color> 먼저 캐릭터를 선택해야 준비 완료할 수 있습니다!");
                return;
            }

            Debug.Log($"<color=lime>[UI]</color> 레디 버튼 클릭! 현재 상태: {myItem.IsReady} -> {!myItem.IsReady}");
            bool nextReadyState = !myItem.IsReady;
            myItem.RPC_SetReady(nextReadyState);
            UpdateReadyUI(nextReadyState);
        }
        else
        {
            Debug.LogWarning("<color=yellow>[UI]</color> 내 플레이어 세션 아이템을 동기화 중입니다. 잠시 후 다시 클릭해 주세요.");
        }
    }

    /// <summary>
    /// 내 Input Authority 를 가진 세션 아이템을 찾는다.
    /// </summary>
    /// <param name="verbose">
    /// 사용자가 버튼을 눌러서 호출한 경우 true. 못 찾으면 로그를 남긴다.
    /// 씬 로드 직후 폴링처럼 "아직 없는 게 정상"인 경우에는 false 로 조용히 호출한다.
    /// </param>
    private SessionPlayerItem FindLocalPlayerItem(bool verbose = true)
    {
        var runner = FindFirstObjectByType<NetworkRunner>();
        if (runner == null)
        {
            if (verbose) Debug.LogError("<color=red>[UI]</color> NetworkRunner를 찾을 수 없습니다.");
            return null;
        }

        SessionPlayerItem[] allItems = FindObjectsByType<SessionPlayerItem>(UnityEngine.FindObjectsSortMode.None);

        // 1단계: HasInputAuthority 확인
        foreach (var item in allItems)
        {
            if (item.Object != null && item.Object.HasInputAuthority)
            {
                return item;
            }
        }

        // 2단계: InputAuthority == LocalPlayer 확인
        foreach (var item in allItems)
        {
            if (item.Object != null && item.Object.InputAuthority == runner.LocalPlayer)
            {
                return item;
            }
        }

        // 3단계: 호스트인데 아이템이 없는 경우 즉시 생성 (안전장치).
        // 폴링(verbose=false)에서는 스폰하지 않는다. Fusion 이 정상 스폰하는 중일 수 있는데
        // 매번 끼어들면 아이템이 중복 생성된다.
        if (verbose && runner.IsServer && !_spawnedFallbackItem)
        {
            var spawner = FindFirstObjectByType<Spawner>();
            if (spawner != null && spawner.sessionItemPrefab != null)
            {
                Debug.LogWarning("<color=yellow>[UI]</color> 호스트 세션 아이템이 없어 직접 생성합니다.");
                _spawnedFallbackItem = true;
                var newObj = runner.Spawn(spawner.sessionItemPrefab, Vector3.zero, Quaternion.identity, runner.LocalPlayer);
                if (newObj != null)
                {
                    return newObj.GetComponent<SessionPlayerItem>();
                }
            }
        }

        if (verbose)
        {
            // 아이템이 아예 없는 것과, 있는데 내 것이 없는 것은 원인이 다르다.
            if (allItems.Length == 0)
                Debug.LogWarning("<color=yellow>[UI]</color> 세션 아이템이 아직 스폰되지 않았습니다. 잠시 후 다시 시도하세요.");
            else
                Debug.LogWarning($"<color=yellow>[UI]</color> 방에 아이템 {allItems.Length}개가 있으나 " +
                                 $"내 권한(Input Authority, LocalPlayer={runner.LocalPlayer})이 있는 것이 없습니다.");
        }
        return null;
    }

    private void UpdateCharacterSelectionUI(int selectedIndex)
    {
        _currentSelectedIndex = selectedIndex;
        RefreshAllPlayersUI();
    }

    /// <summary>
    /// 방에 있는 모든 플레이어(나 및 상대방)의 캐릭터 선택 상태와 준비 상태를 양쪽 화면에 실시간으로 표시합니다.
    /// </summary>
    public void RefreshAllPlayersUI()
    {
        var runner = FindFirstObjectByType<NetworkRunner>();
        var allItems = FindObjectsByType<SessionPlayerItem>(UnityEngine.FindObjectsSortMode.None);

        SessionPlayerItem myItem = null;
        SessionPlayerItem remoteItem = null;

        if (runner != null)
        {
            foreach (var item in allItems)
            {
                if (item.Object == null) continue;
                if (item.Object.HasInputAuthority || (item.Runner != null && item.Object.InputAuthority == item.Runner.LocalPlayer))
                {
                    myItem = item;
                }
                else
                {
                    remoteItem = item;
                }
            }
        }
        else if (allItems.Length > 0)
        {
            myItem = allItems[0];
            if (allItems.Length > 1) remoteItem = allItems[1];
        }

        int myIndex = myItem != null ? myItem.SelectedCharacterIndex : _currentSelectedIndex;
        int remoteIndex = remoteItem != null ? remoteItem.SelectedCharacterIndex : -1;
        bool myReady = myItem != null && myItem.IsReady;
        bool remoteReady = remoteItem != null && remoteItem.IsReady;

        Transform[] cardRoots = new Transform[] {
            transform.Find("SlotsContainer/Card_Fox"),
            transform.Find("SlotsContainer/Card_Rabbit")
        };

        for (int i = 0; i < cardRoots.Length; i++)
        {
            if (cardRoots[i] == null) continue;
            bool isMine = (myIndex == i);
            bool isRemote = (remoteIndex == i);

            // 1. 카드 외곽선 골드 테두리 (내 선택 우선)
            if (selectHighlights != null && i < selectHighlights.Length && selectHighlights[i] != null)
            {
                var rootImg = selectHighlights[i].GetComponent<Image>();
                if (rootImg != null) rootImg.color = Color.clear;
                selectHighlights[i].SetActive(isMine);
            }

            // 2. 카드 베벨 테두리 색상: 내가 고르면 골드, 상대방이 고르면 청록(민트), 없으면 우드 기본
            Transform bevel = cardRoots[i].Find("_Bevel");
            if (bevel != null)
            {
                var bImg = bevel.GetComponent<Image>();
                if (bImg != null)
                {
                    if (isMine) bImg.color = LobbyUITheme.Gold;
                    else if (isRemote) bImg.color = new Color(0.36f, 0.88f, 0.90f);
                    else bImg.color = LobbyUITheme.WoodLight;
                }
            }

            // 3. 선택 버튼 상태 (중복 픽 방지)
            if (selectButtons != null && i < selectButtons.Length && selectButtons[i] != null)
            {
                if (isMine)
                {
                    LobbyUITheme.StylePixelButton(selectButtons[i], LobbyUITheme.ButtonType.Gold, "선택완료!", 24);
                    selectButtons[i].interactable = !myReady;
                }
                else if (isRemote)
                {
                    // 상대방이 이미 선점한 캐릭터: 중복 선택 불가 (클릭 비활성화)
                    LobbyUITheme.StylePixelButton(selectButtons[i], LobbyUITheme.ButtonType.Stone, "선점됨", 24);
                    selectButtons[i].interactable = false;
                }
                else
                {
                    // 선택 가능한 캐릭터
                    LobbyUITheme.StylePixelButton(selectButtons[i], LobbyUITheme.ButtonType.Stone, "선택하기", 24);
                    selectButtons[i].interactable = !myReady;
                }
            }

            // 4. 카드 내 실시간 선택자 뱃지 텍스트
            UpdateCardPlayerBadge(cardRoots[i], isMine, myReady, isRemote, remoteReady);
        }

        // 5. 내 레디 버튼 비주얼 갱신
        UpdateReadyUI(myReady);

        // 6. 상단 서브타이틀에 양쪽 플레이어 현황 종합 표시
        UpdateStatusSubtitle(myIndex, myReady, remoteItem != null, remoteIndex, remoteReady);
    }

    private void UpdateCardPlayerBadge(Transform cardTrans, bool isMine, bool myReady, bool isRemote, bool remoteReady)
    {
        Transform badgeTrans = cardTrans.Find("PlayerBadgeText");
        TextMeshProUGUI badgeTmp = null;

        if (badgeTrans == null)
        {
            // 컨테이너(어두운 반투명 배경) + 자식 텍스트 구조.
            // UGUI는 자식이 부모 '위'에 그려지므로, 배경=부모 / 텍스트=자식 이어야 텍스트가 보인다.
            GameObject badgeGo = new GameObject("PlayerBadgeText", typeof(RectTransform), typeof(Image));
            badgeGo.transform.SetParent(cardTrans, false);
            badgeTrans = badgeGo.transform;

            var rt = badgeGo.GetComponent<RectTransform>();
            // 카드 상단 '바깥'에 배치 (pivot 아래쪽) → 카드 안의 '선택완료!' 버튼과 겹치지 않음
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 8f); // 카드 위 가장자리 살짝 위
            rt.sizeDelta = new Vector2(260f, 34f);

            var bgImg = badgeGo.GetComponent<Image>();
            bgImg.color = new Color(0.08f, 0.05f, 0.03f, 0.80f); // 어두운 반투명 배경
            bgImg.raycastTarget = false;

            GameObject labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(badgeGo.transform, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;

            badgeTmp = labelGo.GetComponent<TextMeshProUGUI>();
            var titleTmp = transform.Find("TitleText")?.GetComponent<TextMeshProUGUI>();
            if (titleTmp != null && titleTmp.font != null) badgeTmp.font = titleTmp.font;
            badgeTmp.alignment = TextAlignmentOptions.Center;
            badgeTmp.fontSize = 18f;
            badgeTmp.fontStyle = FontStyles.Bold;
        }
        else
        {
            badgeTmp = badgeTrans.GetComponentInChildren<TextMeshProUGUI>();
        }

        if (badgeTmp == null) return;

        if (isMine && isRemote)
        {
            badgeTmp.text = "<color=#FAD16B>[나]</color> & <color=#5CE1E6>[상대방]</color> 선택";
        }
        else if (isMine)
        {
            string readyStr = myReady ? " <color=#74D974>(준비완료!)</color>" : "";
            badgeTmp.text = $"<color=#FAD16B>★ 내가 선택함</color>{readyStr}";
        }
        else if (isRemote)
        {
            string readyStr = remoteReady ? " <color=#74D974>(준비완료!)</color>" : " (선점됨)";
            badgeTmp.text = $"<color=#5CE1E6>● 상대방 선점</color>{readyStr}";
        }
        else
        {
            badgeTmp.text = "";
        }
    }

    private void UpdateStatusSubtitle(int myIdx, bool myReady, bool hasRemote, int remoteIdx, bool remoteReady)
    {
        Transform subObj = transform.Find("SubtitleText") ?? transform.Find("TitleText/SubtitleText");
        if (subObj == null) return;

        var subTmp = subObj.GetComponent<TextMeshProUGUI>();
        if (subTmp == null) return;

        string CharName(int idx) => (idx == 0) ? "여우" : (idx == 1) ? "토끼" : "미선택";
        string ReadyTag(bool ready) => ready ? "<color=#74D974>준비완료</color>" : "<color=#D0C0A8>선택중</color>";

        string myStr = $"<color=#FAD16B>[나]</color> {CharName(myIdx)} [{ReadyTag(myReady)}]";
        string remoteStr = hasRemote
            ? $"<color=#5CE1E6>[상대방]</color> {CharName(remoteIdx)} [{ReadyTag(remoteReady)}]"
            : "<color=#8C7B6B>[상대방] 접속 대기 중...</color>";

        subTmp.text = $"원하는 캐릭터를 선택하고 준비를 완료하세요\n<size=19>{myStr}   |   {remoteStr}</size>";
    }

    private void UpdateReadyUI(bool isReady)
    {
        if (readyButton == null)
        {
            GameObject readyObj = GameObject.Find("ReadyButton");
            if (readyObj == null) readyObj = GameObject.Find("Ready Button");
            if (readyObj != null) readyButton = readyObj.GetComponent<Button>();
        }

        if (readyButton != null)
        {
            if (isReady)
            {
                // 준비 완료: 선명한 그린 3D 픽셀 버튼
                LobbyUITheme.StylePixelButton(readyButton, LobbyUITheme.ButtonType.Green, readyTextString, 30);
            }
            else
            {
                // 준비 전: 스톤 3D 픽셀 버튼
                LobbyUITheme.StylePixelButton(readyButton, LobbyUITheme.ButtonType.Stone, notReadyTextString, 30);
            }
        }
    }
}
