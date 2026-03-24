using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

public class BasicLauncher : MonoBehaviour
{
    private NetworkRunner _runner;
    private string _roomCode = ""; // 사용자가 입력할 방 코드
    private bool _isStarting = false;

    private void OnGUI()
    {
        if (_runner == null)
        {
            float buttonWidth = 250f;
            float buttonHeight = 40f;
            float spacing = 15f;
            float centerX = (Screen.width - buttonWidth) / 2f;
            float centerY = (Screen.height - 200) / 2f;

            // 1. 방 코드 입력창 (텍스트 필드)
            GUI.Label(new Rect(centerX, centerY - 25, buttonWidth, 20), "방 코드 입력 (Join 시 필요):");
            _roomCode = GUI.TextField(new Rect(centerX, centerY, buttonWidth, buttonHeight), _roomCode);

            // 2. Host 버튼 (랜덤 코드 생성)
            if (GUI.Button(new Rect(centerX, centerY + buttonHeight + spacing, buttonWidth, buttonHeight), "Host (방 만들기)"))
            {
                // 호스트는 코드가 없으면 랜덤하게 생성 (예: 4자리 숫자)
                if (string.IsNullOrEmpty(_roomCode))
                    _roomCode = Random.Range(1000, 9999).ToString();

                StartGame(GameMode.Host, _roomCode);
            }

            // 3. Join 버튼 (입력한 코드로 접속)
            if (GUI.Button(new Rect(centerX, centerY + (buttonHeight + spacing) * 2, buttonWidth, buttonHeight), "Join (참여하기)"))
            {
                if (!string.IsNullOrEmpty(_roomCode))
                {
                    StartGame(GameMode.Client, _roomCode);
                }
                else
                {
                    Debug.LogError("접속할 방 코드를 입력해주세요!");
                }
            }
        }
        else if (_runner.IsServer || _runner.IsClient)
        {
            // 룸 코드 스타일 설정 (글씨 크게 만들기)
            GUIStyle roomCodeStyle = new GUIStyle(GUI.skin.label);
            roomCodeStyle.fontSize = 20; // 글자 크기
            roomCodeStyle.fontStyle = FontStyle.Bold;
            roomCodeStyle.alignment = TextAnchor.MiddleCenter;
            roomCodeStyle.normal.textColor = Color.yellow; // 노란색으로 강조

            // 1. 화면 중앙 상단에 배치 (가로 중앙, 위에서 50px 내려온 지점)
            float labelWidth = 300f;
            float labelHeight = 40f;
            float xPos = (Screen.width - labelWidth) / 2f;
            float yPos = 50f;

            // 박스 배경과 함께 방 코드 출력
            GUI.Box(new Rect(xPos, yPos, labelWidth, labelHeight), "");
            GUI.Label(new Rect(xPos, yPos, labelWidth, labelHeight), $"방 코드: {_roomCode}", roomCodeStyle);

            // (선택) 하단에 현재 상태 표시
            GUI.Label(new Rect(10, Screen.height - 30, 200, 25), $"Status: {(_runner.IsServer ? "Host" : "Client")}");
        }
    }

    async void StartGame(GameMode mode, string roomName)
    {
        if (_isStarting) return;
        _isStarting = true;

        // 1. Runner 확보 (중복 방지 로직)
        // 현재 오브젝트에서 가져와보고, 없으면 새로 추가합니다.
        _runner = GetComponent<NetworkRunner>();
        if (_runner == null)
        {
            _runner = gameObject.AddComponent<NetworkRunner>();
        }

        // 씬 이동 시 파괴 방지
        DontDestroyOnLoad(gameObject);
        _runner.ProvideInput = true;

        // 2. Spawner 연결
        var spawner = FindObjectOfType<Spawner>();
        if (spawner != null)
        {
            // 중복 등록 방지: 기존 콜백을 다 지우고 새로 등록하거나 체크
            _runner.RemoveCallbacks(spawner);
            _runner.AddCallbacks(spawner);
        }

        Debug.Log($"<color=white>[Launcher]</color> {mode} 접속 시도 중... 방 코드: {roomName}");

        // 3. 게임 세션 시작
        var result = await _runner.StartGame(new StartGameArgs()
        {
            GameMode = mode,
            SessionName = roomName,
            Scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex),
            SceneManager = gameObject.GetComponent<NetworkSceneManagerDefault>() ?? gameObject.AddComponent<NetworkSceneManagerDefault>()
        });

        if (result.Ok)
        {
            if (mode == GameMode.Host)
            {
                Debug.Log("<color=orange>[Launcher]</color> 호스트: TestStage 이동 시작");
                _runner.LoadScene("TestStage", LoadSceneMode.Single);
            }
        }
        else
        {
            _isStarting = false;
            Debug.LogError($"[Launcher] 시작 실패: {result.ShutdownReason}");
        }
    }
}