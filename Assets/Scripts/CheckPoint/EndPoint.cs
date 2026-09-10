using Fusion;
using UnityEngine;

// 스테이지 끝 지점(중간 관문). 플레이어가 닿으면 트리거.
//  - 타이머가 없는(또는 안 도는) 스테이지 = 튜토리얼: 완료 화면 표시 후 본 스테이지로.
//    (TutorialFlowController 가 있으면 축하/확인 화면을 띄우고, 없으면 즉시 이동)
//  - 타이머 스테이지: 구간 시간 확정 + 누적. 즉시 로드하지 않고 GameEndController 의
//    [다음 스테이지] 버튼(호스트)에서 로드한다.
public class EndPoint : MonoBehaviour
{
    [Header("다음 씬 이름 (Build Settings 에 등록된 이름과 정확히 일치해야 함)")]
    public string nextSceneName;

    private bool isTriggered = false;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player") || isTriggered) return;

        var runner = FindFirstObjectByType<NetworkRunner>();
        var nt = NetworkTimer.Instance;
        // NetworkTimer 가 존재하면 타이머 스테이지(인트로/러닝 포함). 튜토리얼은 타이머를 스폰하지 않으므로 nt == null.
        bool timerStage = (nt != null);

        if (timerStage)
        {
            // 타이머 스테이지(중간): 씬 전환/기록은 호스트만 처리
            if (runner == null || !runner.IsServer) return;
            isTriggered = true;

            float seg = nt.PauseForChoice();
            RunManager.AddSegment(seg);
            RunManager.PendingNextScene = nextSceneName;
            // 스테이지 클리어 시점에 "다음 스테이지"로 진행 저장 (체크포인트 초기화).
            // -> [다음 스테이지]를 누르든 [로비로]를 누르든, 이어하기는 다음 스테이지 시작부터.
            SaveDataManager.SaveProgressWithTime(nextSceneName, RunManager.AccumulatedTime);
            Debug.Log($"<color=cyan>[EndPoint]</color> 스테이지 클리어(구간 {seg:0.00}s). 누적 기록 → 다음 씬 진행 저장: {nextSceneName}");
            return;
        }

        // 타이머 없음 = 튜토리얼: 완료 화면 표시.
        // 멀티플레이에서 씬 전환은 호스트 권한이므로 클라이언트는 호스트에 위임(트리거 무시).
        // 솔로(러너 없음)는 아래 로직이 SceneManager 폴백으로 진행된다.
        if (runner != null && !runner.IsServer) return;
        isTriggered = true;

        if (TutorialFlowController.Instance != null)
        {
            Debug.Log($"<color=cyan>[EndPoint]</color> (튜토리얼) 완료 화면 표시. 다음 씬: {nextSceneName}");
            TutorialFlowController.Instance.ShowTutorialComplete(nextSceneName);
            return;
        }

        // 폴백: 컨트롤러가 없으면 기존처럼 즉시 이동
        Debug.Log($"<color=cyan>[EndPoint]</color> (타이머 없음) 즉시 이동: {nextSceneName}");
        CheckpointManager.ResetCheckpoint();
        SaveDataManager.SaveProgressWithTime(nextSceneName, RunManager.AccumulatedTime);
        int idx = GetSceneIndex(nextSceneName);
        if (runner != null && runner.IsServer && idx >= 0)
            runner.LoadScene(SceneRef.FromIndex(idx));
        else
            UnityEngine.SceneManagement.SceneManager.LoadScene(nextSceneName);
    }

    private int GetSceneIndex(string sceneName)
    {
        return UnityEngine.SceneManagement.SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{sceneName}.unity");
    }
}
