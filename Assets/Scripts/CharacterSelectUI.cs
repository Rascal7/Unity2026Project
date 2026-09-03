using Fusion;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CharacterSelectUI : MonoBehaviour
{
    public Button[] selectButtons;
    public Button readyButton;

    [Header("Highlight Settings")]
    public Color normalColor = Color.white;
    public Color selectedColor = Color.yellow;
    public Color readyColor = Color.green;

    private void Start()
    {
        Debug.Log("<color=cyan>[UI]</color> CharacterSelectUI 초기화 시작");

        Button[] allButtons = GetComponentsInChildren<Button>(true);

        for (int i = 0; i < allButtons.Length; i++)
        {
            Button btn = allButtons[i];
            int index = i;

            if (btn.name.Contains("Ready"))
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnClickReady());
                Debug.Log($"<color=cyan>[UI]</color> {btn.name} -> Ready 버튼 등록");
            }
            else
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnClickSelect(index));
                Debug.Log($"<color=cyan>[UI]</color> {btn.name} -> {index}번 선택 버튼 등록");
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
        var runner = FindObjectOfType<NetworkRunner>();
        if (runner == null)
        {
            Debug.LogError("<color=red>[UI]</color> NetworkRunner를 찾을 수 없습니다.");
            return null;
        }

        SessionPlayerItem[] allItems = FindObjectsOfType<SessionPlayerItem>();

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
            var spawner = FindObjectOfType<Spawner>();
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
        for (int i = 0; i < selectButtons.Length; i++)
        {
            if (selectButtons[i] == null) continue;
            Image btnImage = selectButtons[i].GetComponent<Image>();
            if (btnImage != null)
            {
                btnImage.color = (i == selectedIndex) ? selectedColor : normalColor;
            }
        }
    }

    private void UpdateReadyUI(bool isReady)
    {
        if (readyButton == null)
        {
            GameObject readyObj = GameObject.Find("ReadyButton");
            if (readyObj != null) readyButton = readyObj.GetComponent<Button>();
        }

        if (readyButton != null)
        {
            Image btnImage = readyButton.GetComponent<Image>();
            if (btnImage != null)
            {
                btnImage.color = isReady ? readyColor : normalColor;
            }

            Text btnText = readyButton.GetComponentInChildren<Text>();
            if (btnText != null)
            {
                btnText.text = isReady ? "READY!" : "NOT READY";
            }

            TextMeshProUGUI tmpText = readyButton.GetComponentInChildren<TextMeshProUGUI>();
            if (tmpText != null)
            {
                tmpText.text = isReady ? "READY!" : "NOT READY";
            }
        }
    }
}
