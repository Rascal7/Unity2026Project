using Fusion;
using UnityEngine;

public class CharacterSelectUI : MonoBehaviour
{
    // CharacterSelectUI.cs

    public void OnClickSelect(int index)
    {
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
        SessionPlayerItem[] allItems = FindObjectsOfType<SessionPlayerItem>();
        NetworkRunner runner = FindObjectOfType<NetworkRunner>();

        // 내 로컬 플레이어의 번호 (예: 1)
        int myPlayerID = runner.LocalPlayer.PlayerId;

        foreach (var item in allItems)
        {
            if (item.Object == null) continue;

            // 아이템이 가진 권한의 번호 (예: 1)
            int ownerID = item.Object.InputAuthority.PlayerId;

            Debug.Log($"[비교] 아이템 주인 ID: {ownerID}, 내 로컬 ID: {myPlayerID}");

            if (ownerID == myPlayerID)
            {
                Debug.Log("<color=lime>[UI]</color> ID 일치! 내 아이템을 찾았습니다.");
                return item;
            }
        }

        // 만약 1인 테스트 중이라면, 권한 체크를 무시하고 0번 인덱스를 강제 반환해서 테스트 진행
        if (allItems.Length > 0)
        {
            Debug.LogWarning("[UI] 권한 체크 실패로 첫 번째 아이템을 강제 연결합니다.");
            return allItems[0];
        }

        return null;
    }
}