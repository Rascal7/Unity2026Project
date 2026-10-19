using System.Collections;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

// 호스트가 씬을 바꾸기 직전에 모든 피어의 화면을 함께 어둡게 하기 위한 1회용 네트워크 오브젝트.
//  - 호스트가 Resources/SceneFadeRelay 를 Spawn 하면 각 피어의 Spawned() 에서 페이드 아웃이 시작된다.
//  - 씬이 바뀌면 다른 네트워크 오브젝트처럼 자동으로 사라지고, 새 씬에서 ScreenFader 가 페이드 인한다.
public class SceneFadeRelay : NetworkBehaviour
{
    private const float LoadDelay = ScreenFader.DefaultDuration + 0.1f;

    public override void Spawned()
    {
        if (ScreenFader.Instance != null) ScreenFader.Instance.FadeOut();
    }

    // 모든 피어 페이드 아웃 → 잠시 후 씬 로드. 호스트 또는 솔로(러너 없음)에서 호출한다.
    public static void FadeAndLoad(string sceneName)
    {
        var fader = ScreenFader.Instance;
        var runner = FindFirstObjectByType<NetworkRunner>();
        bool networked = runner != null && runner.IsRunning;

        if (networked && !runner.IsServer)
        {
            Debug.LogWarning("[SceneFadeRelay] 클라이언트는 직접 씬을 전환할 수 없습니다.");
            return;
        }

        if (networked)
        {
            var prefab = Resources.Load<NetworkObject>("SceneFadeRelay");
            if (prefab != null) runner.SpawnAsync(prefab); // 동기 Spawn 은 씬 로드 직후 등에서 프리팹 로드에 실패할 수 있다
            else if (fader != null) fader.FadeOut(); // 프리팹이 없으면 호스트 화면만이라도 페이드
        }
        else if (fader != null)
        {
            fader.FadeOut();
        }

        if (fader != null) fader.StartCoroutine(LoadAfterDelay(runner, networked, sceneName));
        else LoadNow(runner, networked, sceneName);
    }

    public static bool SceneInBuild(string sceneName)
    {
        return SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{sceneName}.unity") >= 0;
    }

    private static IEnumerator LoadAfterDelay(NetworkRunner runner, bool networked, string sceneName)
    {
        yield return new WaitForSecondsRealtime(LoadDelay);
        LoadNow(runner, networked, sceneName);
    }

    private static void LoadNow(NetworkRunner runner, bool networked, string sceneName)
    {
        BasicLauncher.LoadNetworkOrLocalScene(sceneName);
    }
}
