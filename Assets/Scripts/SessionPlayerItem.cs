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

        // 보안 검사 1: 인덱스 유효 범위 (-1: 미선택, 0: 여우, 1: 토끼)
        if (index < -1 || index > 1)
        {
            Debug.LogWarning($"<color=red>[Security] 유효하지 않은 캐릭터 인덱스({index}) 요청 차단 (요청자: {Object.InputAuthority})</color>");
            return;
        }

        // 보안 검사 2: 준비 완료 상태에서는 캐릭터 변경 차단
        if (IsReady)
        {
            Debug.LogWarning($"<color=yellow>[Security] 준비 완료 상태에서는 캐릭터를 변경할 수 없습니다. (요청자: {Object.InputAuthority})</color>");
            return;
        }

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

        // 보안 검사: 캐릭터를 선택하지 않은 상태에서 준비 완료 요청 차단
        if (ready && SelectedCharacterIndex < 0)
        {
            Debug.LogWarning($"<color=red>[Security] 캐릭터를 선택하지 않은 상태에서 준비 완료 요청 차단 (요청자: {Object.InputAuthority})</color>");
            return;
        }

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

    private static float _lastSceneLoadTime = -999f;

    private void CheckAllReady()
    {
        TryProceedIfAllReady(Runner, "savefile");
    }

    public static bool TryProceedIfAllReady(NetworkRunner runner, string nextSceneName = "savefile")
    {
        if (runner == null || !runner.IsServer) return false;
        if (SceneManager.GetActiveScene().name != "CharacterSelect") return false;
        if (Time.unscaledTime - _lastSceneLoadTime < 2.0f) return false;

        var allItems = FindObjectsByType<SessionPlayerItem>(UnityEngine.FindObjectsSortMode.None);
        if (allItems.Length == 0) return false;

        int readyCount = 0;
        bool allCharactersSelected = true;
        foreach (var item in allItems)
        {
            if (item == null || item.Object == null) continue;
            if (item.IsReady) readyCount++;
            if (item.SelectedCharacterIndex == -1) allCharactersSelected = false;
        }

        int targetCount = Mathf.Max(1, System.Linq.Enumerable.Count(runner.ActivePlayers));
        if (allItems.Length < targetCount) return false;

        if (readyCount >= targetCount && allCharactersSelected)
        {
            _lastSceneLoadTime = Time.unscaledTime;
            Debug.Log($"<color=green>[출발]</color> 전원 준비 완료 ({readyCount}/{targetCount})! 세이브 파일 선택 씬({nextSceneName})으로 이동합니다.");
            int sceneIndex = SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{nextSceneName}.unity");
            if (sceneIndex >= 0)
            {
                runner.LoadScene(SceneRef.FromIndex(sceneIndex));
            }
            else
            {
                runner.LoadScene(nextSceneName, LoadSceneMode.Single);
            }
            return true;
        }

        return false;
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