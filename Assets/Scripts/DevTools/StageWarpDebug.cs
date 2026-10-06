#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Fusion;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

// 테스트용: 숫자키 1/2/3 으로 Main Stage1/2/3 시작점으로 바로 이동한다.
// 스테이지는 씬 단위이므로 해당 스테이지 씬을 처음부터 다시 로드한다(= StartPoint 에서 시작).
//  - 멀티: 씬 전환은 호스트만 가능하므로 호스트가 누를 때만 동작 (클라이언트는 같이 따라감)
//  - 솔로(러너 없음): 스테이지 씬 안에서만 동작
// 에디터 / Development Build 에서만 컴파일된다. 씬에 배치할 필요 없음(자동 생성).
public class StageWarpDebug : MonoBehaviour
{
    private static readonly string[] StageScenes = { "Main Stage1", "Main Stage2", "Main Stage3" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("[StageWarpDebug]");
        DontDestroyOnLoad(go);
        go.AddComponent<StageWarpDebug>();
    }

    private void Update()
    {
        int stage = 0;
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) stage = 1;
        else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) stage = 2;
        else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) stage = 3;
        if (stage == 0 || IsTyping()) return;

        Warp(StageScenes[stage - 1]);
    }

    private static void Warp(string sceneName)
    {
        int idx = SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{sceneName}.unity");
        if (idx < 0)
        {
            Debug.LogWarning($"[StageWarpDebug] Build Settings 에 '{sceneName}' 이 없습니다.");
            return;
        }

        var runner = FindFirstObjectByType<NetworkRunner>();
        bool networked = runner != null && runner.IsRunning;
        if (networked && !runner.IsServer) return; // 씬 전환은 호스트만
        if (!networked && SceneManager.GetActiveScene().name.IndexOf("Stage", System.StringComparison.OrdinalIgnoreCase) < 0)
            return; // 로비/캐릭터 선택 등에서는 무시

        // 이전 스테이지 체크포인트/누적 기록이 새 스테이지로 넘어가지 않도록 초기화
        CheckpointManager.ResetCheckpoint();
        RunManager.ResetRun();
        Debug.Log($"<color=magenta>[StageWarpDebug]</color> {sceneName} 시작점으로 이동");

        if (networked) runner.LoadScene(SceneRef.FromIndex(idx));
        else SceneManager.LoadScene(idx);
    }

    // 입력 필드(채팅/설정 등)에 타이핑 중이면 무시
    private static bool IsTyping()
    {
        var es = EventSystem.current;
        var sel = es != null ? es.currentSelectedGameObject : null;
        if (sel == null) return false;
        return sel.GetComponent<TMPro.TMP_InputField>() != null
            || sel.GetComponent<UnityEngine.UI.InputField>() != null;
    }
}
#endif
