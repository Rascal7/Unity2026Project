using Fusion;
using UnityEngine;
using UnityEngine.UI;
public class CharacterSelectUI : MonoBehaviour
{
    // CharacterSelectUI.cs
    public Button[] selectButtons;
    public Button readyButton;

    [Header("Highlight Settings")]
    public Color normalColor = Color.white;
    public Color selectedColor = Color.yellow; // 선택되었을 때 바뀔 색상
    public Color readyColor = Color.green;
    private void Start()
    {
        Debug.Log("<color=red>[UI]</color> CharacterSelectUI 시작");

        // [수정] 모든 버튼을 자동으로 연결하는 루프를 삭제하거나, 
        // 특정 버튼(레디 버튼 등)은 제외하도록 조건문을 걸어야 합니다.

        // 가장 깔끔한 방법: 버튼의 이름을 확인하거나 수동으로 연결하기
        Button[] allButtons = GetComponentsInChildren<Button>(true);

        for (int i = 0; i < allButtons.Length; i++)
        {
            Button btn = allButtons[i];
            int index = i;

            // 버튼 이름에 "Ready"가 포함되어 있다면 OnClickReady를 연결
            if (btn.name.Contains("Ready"))
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnClickReady());
                Debug.Log($"<color=cyan>[UI]</color> {btn.name}을 Ready 버튼으로 연결함");
            }
            // 그 외 버튼들은 선택 버튼으로 처리 (단, 인덱스 주의)
            else
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnClickSelect(index));
                Debug.Log($"<color=cyan>[UI]</color> {btn.name}을 {index}번 선택 버튼으로 연결함");
            }
        }
    }
    public void OnClickSelect(int index)
    {
        Debug.Log($"<color=cyan>[UI]</color> {index}번 버튼 클릭됨!");
        SessionPlayerItem myItem = FindLocalPlayerItem();

        if (myItem != null)
        {
            // [중요] RPC를 쏘기 전에 내 권한이 확실한지 한 번 더 체크합니다.
            if (myItem.HasInputAuthority)
            {
                Debug.Log($"<color=lime>[UI]</color> {index}번 캐릭터 선택 요청 보냄!");
                myItem.RPC_SelectCharacter(index);

                UpdateCharacterSelectionUI(index);
            }
            else
            {
                Debug.LogError("<color=red>[UI]</color> 찾은 아이템에 Input Authority가 없습니다!");
            }
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
    }

    private SessionPlayerItem FindLocalPlayerItem()
    {
        // 씬에 있는 모든 아이템을 다 긁어모읍니다.
        SessionPlayerItem[] allItems = FindObjectsOfType<SessionPlayerItem>();
        var runner = FindObjectOfType<NetworkRunner>();

        if (runner == null) return null;
        foreach (var item in allItems)
        {
            // 핵심: Fusion이 이 객체의 권한이 '나'에게 있다고 명시한 것만 리턴!
            if (item.Object != null && item.Object.HasInputAuthority)
            {
                Debug.Log("<color=lime>[UI]</color> 진짜 내 아이템(HasInputAuthority)을 찾았습니다!");
                return item;
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
                // 캐릭터 버튼끼리만 배타적으로 하이라이트 (0번 선택 시 1번은 꺼짐)
                btnImage.color = (i == selectedIndex) ? selectedColor : normalColor;
            }
        }
    }
    private void UpdateReadyUI(bool isReady)
    {
        if (readyButton == null)
        {
            // 만약 레디 버튼을 인스펙터에 안 넣었다면 이름으로 찾기
            GameObject readyObj = GameObject.Find("ReadyButton");
            if (readyObj != null) readyButton = readyObj.GetComponent<Button>();
        }

        if (readyButton != null)
        {
            Image btnImage = readyButton.GetComponent<Image>();
            if (btnImage != null)
            {
                // 준비 상태에 따라 색상 토글
                btnImage.color = isReady ? readyColor : normalColor;
            }

            // 텍스트도 바꾸고 싶다면?
            Text btnText = readyButton.GetComponentInChildren<Text>();
            if (btnText != null)
            {
                btnText.text = isReady ? "READY!" : "NOT READY";
            }
        }
    }

}