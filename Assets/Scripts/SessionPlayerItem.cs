using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SessionPlayerItem : NetworkBehaviour
{
    [Networked, OnChangedRender(nameof(OnIndexChanged))]
    public int SelectedCharacterIndex { get; set; } 
    [Networked, OnChangedRender(nameof(OnReadyChanged))]
    public NetworkBool IsReady { get; set; }

    public override void Spawned()
    {
        DontDestroyOnLoad(gameObject);
        if (Object.HasStateAuthority)
        {
            SelectedCharacterIndex = -1;
            Debug.Log($"<color=yellow>[Item Spawned]</color> 초기값 -1 설정 완료");
        }
        Debug.Log($"<color=yellow>[Item]</color> SessionPlayerItem 스폰! 소유자: {Object.InputAuthority}");
        CharacterSelectUI.Instance?.NotifySessionChanged();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SelectCharacter(int index)
    {
        Debug.Log($"<color=cyan>[RPC 요청]</color> {Object.InputAuthority} 요청 인덱스: {index}");

        // 중복 픽 방지 검사: 다른 플레이어가 이미 해당 인덱스를 선점했는지 확인
        var allItems = FindObjectsByType<SessionPlayerItem>(UnityEngine.FindObjectsSortMode.None);
        foreach (var item in allItems)
        {
            if (item != this && item.SelectedCharacterIndex == index && index != -1)
            {
                Debug.LogWarning($"<color=yellow>[픽 거부]</color> 이미 다른 플레이어({item.Object.InputAuthority})가 {index}번 캐릭터를 선점했습니다.");
                return;
            }
        }

        SelectedCharacterIndex = index;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetReady(NetworkBool ready)
    {
        Debug.Log($"<color=orange>[RPC 수신]</color> 플레이어 {Object.InputAuthority} 레디 상태: {ready}");
        IsReady = ready;

        if (Runner.IsServer)
        {
            Debug.Log("<color=white>[Server]</color> 서버에서 확인됨. 전원 레디 체크 시작.");
            CheckAllReady();
        }
        else
        {
            Debug.LogError("<color=red>[오류]</color> RPC를 받았으나 서버가 아닙니다!");
        }
    }

    private void CheckAllReady()
    {
        var allItems = FindObjectsByType<SessionPlayerItem>(UnityEngine.FindObjectsSortMode.None);
        Debug.Log($"<color=white>[체크]</color> 현재 SessionPlayerItem 개수: {allItems.Length}");

        int readyCount = 0;
        bool allCharactersSelected = true;
        foreach (var item in allItems)
        {
            if (item.IsReady) readyCount++;
            if (item.SelectedCharacterIndex == -1) allCharactersSelected = false;
        }

        int targetCount = Mathf.Max(1, System.Linq.Enumerable.Count(Runner.ActivePlayers));
        Debug.Log($"<color=white>[체크]</color> 준비 완료 인원: {readyCount} / 접속 인원: {targetCount}");

        if (readyCount >= targetCount && allCharactersSelected)
        {
            Debug.Log("<color=green>[출발]</color> 전원 준비 완료! 세이브 파일 선택 씬(savefile)으로 이동합니다.");
            int sceneIndex = SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/savefile.unity");
            if (sceneIndex >= 0)
            {
                Runner.LoadScene(SceneRef.FromIndex(sceneIndex));
            }
            else
            {
                Runner.LoadScene("savefile");
            }
        }
    }

    public void OnIndexChanged()
    {
        Debug.Log($"<color=magenta>[동기화]</color> 플레이어:{Object.InputAuthority} | 선택 인덱스:{SelectedCharacterIndex}");
        CharacterSelectUI.Instance?.NotifySessionChanged();
    }

    public void OnReadyChanged()
    {
        Debug.Log($"<color=magenta>[동기화]</color> 플레이어:{Object.InputAuthority} | 레디 상태:{IsReady}");
        CharacterSelectUI.Instance?.NotifySessionChanged();
    }
}