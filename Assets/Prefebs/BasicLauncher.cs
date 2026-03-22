using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

public class BasicLauncher : MonoBehaviour
{
    private NetworkRunner _runner;

    private void OnGUI()
    {
        if (_runner == null)
        {
            if (GUI.Button(new Rect(0, 0, 200, 40), "Host (방만들기)"))
            {
                StartGame(GameMode.Host);
            }
            if (GUI.Button(new Rect(0, 50, 200, 40), "Join (참여하기)"))
            {
                StartGame(GameMode.Client);
            }
        }
    }

    async void StartGame(GameMode mode)
    {
        // 1. 기존에 이미 러너가 있다면 파괴하거나 재사용 (중복 방지)
        _runner = GetComponent<NetworkRunner>();
        if (_runner == null)
        {
            _runner = gameObject.AddComponent<NetworkRunner>();
        }

        // [중요] 여기서 한번 더 체크해서 null 에러를 방지합니다.
        if (_runner == null)
        {
            Debug.LogError("NetworkRunner를 생성할 수 없습니다!");
            return;
        }

        _runner.ProvideInput = true;

        // Spawner 찾기
        var spawner = FindObjectOfType<Spawner>();
        if (spawner != null) _runner.AddCallbacks(spawner);

        // 2. 게임 세션 시작
        await _runner.StartGame(new StartGameArgs()
        {
            GameMode = mode,
            SessionName = "TestRoom",
            Scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex),
            SceneManager = gameObject.GetComponent<NetworkSceneManagerDefault>() ?? gameObject.AddComponent<NetworkSceneManagerDefault>()
        });

        Debug.Log("<color=yellow>[Launcher]</color> 게임 시작됨!");
    }
}