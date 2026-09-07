using Fusion;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CharacterSelectUI : MonoBehaviour
{
    [Header("Buttons")]
    public Button[] selectButtons;
    public Button readyButton;

    [Header("Character Selection Highlights")]
    [Tooltip("선택 시 활성화될 외곽선/하이라이트 오브젝트 (인덱스 순서대로 대응)")]
    public GameObject[] selectHighlights;
    [Tooltip("선택 시 버튼에 적용할 스프라이트 (미할당 시 색상 틴트 사용)")]
    public Sprite selectedButtonSprite;
    [Tooltip("기본 상태 버튼 스프라이트")]
    public Sprite normalButtonSprite;

    [Header("Ready Button Visuals")]
    public Image readyButtonImage;
    [Tooltip("준비 완료 시 레디 버튼 스프라이트 (초록색 등)")]
    public Sprite readySprite;
    [Tooltip("준비 전 레디 버튼 스프라이트 (파란색 등)")]
    public Sprite notReadySprite;
    public TextMeshProUGUI readyText;
    public string readyTextString = "READY!";
    public string notReadyTextString = "준비하기";

    [Header("Color Settings (Fallback)")]
    public Color normalColor = Color.white;
    public Color selectedColor = Color.yellow;
    public Color readyColor = Color.green;

    private bool _hasSyncedInitialState = false;

    private void Start()
    {
        Debug.Log("<color=cyan>[UI]</color> CharacterSelectUI 초기화 시작");

        // 1. 캐릭터 선택 버튼 바인딩
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
            // 폴백: 인스펙터에 미할당 시 자식 버튼 중 'Ready'가 아닌 버튼 탐색
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

        // 2. 레디 버튼 바인딩
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

        // 초기 시각 상태 적용 (아직 선택 안됨)
        UpdateCharacterSelectionUI(-1);
        UpdateReadyUI(false);
    }

    private void Update()
    {
        // 씬 로드 직후 늦게 스폰된 세션 아이템의 상태를 1회 자동 동기화
        if (!_hasSyncedInitialState)
        {
            var myItem = FindLocalPlayerItem();
            if (myItem != null)
            {
                if (myItem.SelectedCharacterIndex >= 0)
                {
                    UpdateCharacterSelectionUI(myItem.SelectedCharacterIndex);
                }
                UpdateReadyUI(myItem.IsReady);
                _hasSyncedInitialState = true;
            }
        }
    }

    public void OnClickSelect(int index)
    {
        Debug.Log($"<color=cyan>[UI]</color> {index}번 버튼 클릭!");
        SessionPlayerItem myItem = FindLocalPlayerItem();

        if (myItem != null)
        {
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

    private SessionPlayerItem FindLocalPlayerItem()
    {
        var runner = FindFirstObjectByType<NetworkRunner>();
        if (runner == null)
        {
            Debug.LogError("<color=red>[UI]</color> NetworkRunner를 찾을 수 없습니다.");
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

        // 3단계: 호스트인데 아이템이 없는 경우 즉시 생성 (안전장치)
        if (runner.IsServer)
        {
            var spawner = FindFirstObjectByType<Spawner>();
            if (spawner != null && spawner.sessionItemPrefab != null)
            {
                Debug.LogWarning("<color=yellow>[UI]</color> 호스트 세션 아이템이 없어 직접 생성합니다.");
                var newObj = runner.Spawn(spawner.sessionItemPrefab, Vector3.zero, Quaternion.identity, runner.LocalPlayer);
                if (newObj != null)
                {
                    return newObj.GetComponent<SessionPlayerItem>();
                }
            }
        }

        Debug.LogError("<color=red>[UI]</color> 씬에 아이템은 있으나, 내 권한(Input Authority)이 있는 것은 없습니다.");
        return null;
    }

    private void UpdateCharacterSelectionUI(int selectedIndex)
    {
        // 1. 외곽선/하이라이트 오브젝트 켜기/끄기
        if (selectHighlights != null)
        {
            for (int i = 0; i < selectHighlights.Length; i++)
            {
                if (selectHighlights[i] != null)
                {
                    selectHighlights[i].SetActive(i == selectedIndex);
                }
            }
        }

        // 2. 버튼 스프라이트 또는 색상 틴트 업데이트
        if (selectButtons != null)
        {
            for (int i = 0; i < selectButtons.Length; i++)
            {
                if (selectButtons[i] == null) continue;
                Image btnImage = selectButtons[i].GetComponent<Image>();
                if (btnImage != null)
                {
                    if (selectedButtonSprite != null && normalButtonSprite != null)
                    {
                        btnImage.sprite = (i == selectedIndex) ? selectedButtonSprite : normalButtonSprite;
                    }
                    else
                    {
                        btnImage.color = (i == selectedIndex) ? selectedColor : normalColor;
                    }
                }
            }
        }
    }

    private void UpdateReadyUI(bool isReady)
    {
        if (readyButton == null)
        {
            GameObject readyObj = GameObject.Find("ReadyButton");
            if (readyObj == null) readyObj = GameObject.Find("Ready Button");
            if (readyObj != null) readyButton = readyObj.GetComponent<Button>();
        }

        if (readyButtonImage == null && readyButton != null)
        {
            readyButtonImage = readyButton.GetComponent<Image>();
        }

        if (readyButtonImage != null)
        {
            if (readySprite != null && notReadySprite != null)
            {
                readyButtonImage.sprite = isReady ? readySprite : notReadySprite;
                // 스프라이트 본래 색상을 살리기 위해 흰색으로 유지
                readyButtonImage.color = Color.white;
            }
            else
            {
                readyButtonImage.color = isReady ? readyColor : normalColor;
            }
        }

        if (readyText == null && readyButton != null)
        {
            readyText = readyButton.GetComponentInChildren<TextMeshProUGUI>();
        }

        if (readyText != null)
        {
            readyText.text = isReady ? readyTextString : notReadyTextString;
        }
        else if (readyButton != null)
        {
            Text legacyText = readyButton.GetComponentInChildren<Text>();
            if (legacyText != null)
            {
                legacyText.text = isReady ? readyTextString : notReadyTextString;
            }
        }
    }
}
