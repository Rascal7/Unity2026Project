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
            // 1. 버튼의 크기 설정
            float buttonWidth = 250f;
            float buttonHeight = 60f;
            float spacing = 20f; // 버튼 사이의 간격

            // 2. 화면 중앙 좌표 계산 (버튼 크기의 절반을 빼야 정확히 중앙에 옵니다)
            float centerX = (Screen.width - buttonWidth) / 2f;
            float centerY = (Screen.height - (buttonHeight * 2 + spacing)) / 2f;

            // 3. Host 버튼 배치
            if (GUI.Button(new Rect(centerX, centerY, buttonWidth, buttonHeight), "Host (방만들기)"))
            {
                StartGame(GameMode.Host);
            }

            // 4. Join 버튼 배치 (Host 버튼 아래에 배치)
            if (GUI.Button(new Rect(centerX, centerY + buttonHeight + spacing, buttonWidth, buttonHeight), "Join (참여하기)"))
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