using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SessionPlayerItem : NetworkBehaviour
{
    [Networked, OnChangedRender(nameof(OnIndexChanged))]
    public int SelectedCharacterIndex { get; set; } 
    [Networked] public NetworkBool IsReady { get; set; }

    public override void Spawned()
    {
        DontDestroyOnLoad(gameObject);
        if (Object.HasStateAuthority)
        {
            SelectedCharacterIndex = -1;
            Debug.Log($"<color=yellow>[Item Spawned]</color> 초기값 -1 설정 완료");
        }
        Debug.Log($"<color=yellow>[Item]</color> SessionPlayerItem 생성됨! 주인: {Object.InputAuthority}");
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SelectCharacter(int index)
    {
        Debug.Log($"<color=cyan>[RPC 호출됨]</color> {Object.InputAuthority}가 요청한 인덱스: {index}");
        SelectedCharacterIndex = index;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetReady(NetworkBool ready)
    {
        // 1단계 확인: 서버가 이 메시지를 받았는가?
        Debug.Log($"<color=orange>[RPC 수신]</color> 플레이어 {Object.InputAuthority} 레디 요청: {ready}");

        IsReady = ready;

        // 2단계 확인: 서버 권한이 있는가?
        if (Runner.IsServer)
        {
            Debug.Log("<color=white>[Server]</color> 서버 권한 확인됨. 인원 체크 시작.");
            CheckAllReady();
        }
        else
        {
            Debug.LogError("<color=red>[에러]</color> RPC를 받았는데 서버 권한이 없습니다! (모드 확인 필요)");
        }
    }

    private void CheckAllReady()
    {
        var allItems = FindObjectsOfType<SessionPlayerItem>();
        Debug.Log($"<color=white>[체크]</color> 현재 검색된 SessionPlayerItem 개수: {allItems.Length}");

        int readyCount = 0;
        foreach (var item in allItems)
        {
            if (item.IsReady) readyCount++;
        }

        Debug.Log($"<color=white>[체크]</color> 준비 완료 인원: {readyCount} / 목표: 1");

        if (readyCount >= 1) // 테스트를 위해 1명만 돼도 넘어가게 설정
        {
            Debug.Log("<color=green>[성공]</color> 조건 충족! 씬 전환 시도.");
            Runner.LoadScene("TutorialStage");
        }
    }
    public void OnIndexChanged()
    {
        Debug.Log($"<color=magenta>[변화 감지]</color> 주인:{Object.InputAuthority} | 현재 값:{SelectedCharacterIndex}");

        if (SelectedCharacterIndex == 2)
        {
            Debug.LogError("<color=red>[범인 검거]</color> 인덱스가 2로 변했습니다! 호출 스택을 확인하세요.");
        }
    }
}