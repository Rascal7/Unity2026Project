using Fusion;
using UnityEngine;
using UnityEngine.UI;
public class CharacterSelectUI : MonoBehaviour
{
    // CharacterSelectUI.cs
    public Button[] selectButtons;
    private void Start()
    {
        // [진단 1] 스크립트가 깨어났는지 확인
        Debug.Log("<color=red>[UI]</color> CharacterSelectUI Start 시작됨!");

        // 씬 내의 모든 버튼을 찾습니다. (비활성화된 것 포함)
        Button[] allButtons = GetComponentsInChildren<Button>(true);

        if (allButtons.Length == 0)
        {
            Debug.LogError("<color=red>[UI]</color> 자식 오브젝트에서 버튼을 하나도 찾지 못했습니다!");
            return;
        }

        for (int i = 0; i < allButtons.Length; i++)
        {
            int index = i;
            Button btn = allButtons[i];

            // 기존 연결 싹 청소 (Missing 방지)
            btn.onClick.RemoveAllListeners();

            // 코드로 새 기능 주입 (인스펙터에는 안 보이지만 실제론 작동함)
            btn.onClick.AddListener(() => {
                Debug.LogWarning($"<color=yellow>[버튼 클릭 확인]</color> {index}번 버튼이 실제로 눌렸습니다!");
                OnClickSelect(index);
            });

            Debug.Log($"<color=cyan>[UI 초기화]</color> {btn.name} 버튼 연결 성공!");
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
            myItem.RPC_SetReady(!myItem.IsReady);
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
    
}