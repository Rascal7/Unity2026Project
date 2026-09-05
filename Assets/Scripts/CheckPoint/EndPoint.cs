using Fusion;
using UnityEngine;

public class EndPoint : MonoBehaviour
{
    [Header("다음 씬 이름 (Build Settings에 등록된 이름과 정확히 일치해야 함)")]
    public string nextSceneName;

    private bool isTriggered = false;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player") || isTriggered) return;

        var runner = FindObjectOfType<NetworkRunner>();
        if (runner == null || !runner.IsServer) return;

        isTriggered = true;
        Debug.Log($"<color=cyan>[EndPoint]</color> 다음 스테이지로 이동: {nextSceneName}");

        CheckpointManager.ResetCheckpoint();

        runner.LoadScene(SceneRef.FromIndex(GetSceneIndex(nextSceneName)));
    }

    private int GetSceneIndex(string sceneName)
    {
        return UnityEngine.SceneManagement.SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{sceneName}.unity");
    }
}