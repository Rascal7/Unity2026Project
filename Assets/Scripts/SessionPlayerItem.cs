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
            Debug.Log($"<color=yellow>[Item Spawned]</color> ʱⰪ -1  Ϸ");
        }
        Debug.Log($"<color=yellow>[Item]</color> SessionPlayerItem ! : {Object.InputAuthority}");
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SelectCharacter(int index)
    {
        Debug.Log($"<color=cyan>[RPC ȣ]</color> {Object.InputAuthority} û ε: {index}");
        SelectedCharacterIndex = index;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetReady(NetworkBool ready)
    {
        // 1ܰ Ȯ:   ޽ ޾Ҵ°?
        Debug.Log($"<color=orange>[RPC ]</color> ÷̾ {Object.InputAuthority}  û: {ready}");

        IsReady = ready;

        // 2ܰ Ȯ:   ִ°?
        if (Runner.IsServer)
        {
            Debug.Log("<color=white>[Server]</color>   Ȯε. ο üũ .");
            CheckAllReady();
        }
        else
        {
            Debug.LogError("<color=red>[]</color> RPC ޾Ҵµ   ϴ! ( Ȯ ʿ)");
        }
    }

    private void CheckAllReady()
    {
        var allItems = FindObjectsByType<SessionPlayerItem>(UnityEngine.FindObjectsSortMode.None);
        Debug.Log($"<color=white>[üũ]</color>  ˻ SessionPlayerItem : {allItems.Length}");

        int readyCount = 0;
        foreach (var item in allItems)
        {
            if (item.IsReady) readyCount++;
        }

        int targetCount = Mathf.Max(1, System.Linq.Enumerable.Count(Runner.ActivePlayers));
        Debug.Log($"<color=white>[체크]</color> 준비 완료 인원: {readyCount} / 접속 인원: {targetCount}");

        if (readyCount >= targetCount)
        {
            Debug.Log("<color=green>[출발]</color> 전원 준비 완료! 스테이지로 이동합니다.");
            Runner.LoadScene("TutorialStage");
        }
    }
    public void OnIndexChanged()
    {
        Debug.Log($"<color=magenta>[ȭ ]</color> :{Object.InputAuthority} |  :{SelectedCharacterIndex}");

        if (SelectedCharacterIndex == 2)
        {
            Debug.LogError("<color=red>[ ˰]</color> ε 2 ߽ϴ! ȣ  Ȯϼ.");
        }
    }
}