using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

public class BasicLauncher : MonoBehaviour
{
    private NetworkRunner _runner;
    private string _roomCode = ""; // 사용자가 입력할 방 코드
    private bool _isStarting = false;
    private string _playerName = "";

    private void Start()
    {
        _playerName = PlayerPrefs.GetString("PlayerName", "Player" + Random.Range(100, 999));
    }

    private void OnGUI()
    {
        // 랭킹 보드나 ESC 설정 메뉴가 열려있으면 레거시 GUI를 일체 그리지 않음
        if (RankingManager.Instance != null && RankingManager.Instance.IsBoardOpen) return;
        if (SettingsMenuUI.Instance != null && SettingsMenuUI.Instance.IsOpen) return;

        // 아직 룸에 접속하지 않은 상태 (로비 화면)
        if (_runner == null)
        {
            float buttonWidth = 260f;
            float buttonHeight = 44f;
            float spacing = 12f;
            float centerX = (Screen.width - buttonWidth) / 2f;
            
            // 타이틀 "보이스 히어로" 아래쪽의 패널 안으로 들어가도록 Y축 위치를 아래로 이동
            float startY = Screen.height * 0.38f;

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 14;
            labelStyle.fontStyle = FontStyle.Bold;
            labelStyle.normal.textColor = Color.white;

            // 1. 닉네임 입력창
            GUI.Label(new Rect(centerX, startY, buttonWidth, 20), "닉네임 (Nickname):", labelStyle);
            _playerName = GUI.TextField(new Rect(centerX, startY + 22, buttonWidth, 32), _playerName);
            PlayerPrefs.SetString("PlayerName", _playerName);

            // 2. 방 코드 입력창
            GUI.Label(new Rect(centerX, startY + 62, buttonWidth, 20), "방 코드 입력 (Join 시 필요):", labelStyle);
            _roomCode = GUI.TextField(new Rect(centerX, startY + 84, buttonWidth, 32), _roomCode);

            // 3. Host 버튼 (방 만들기)
            if (GUI.Button(new Rect(centerX, startY + 128, buttonWidth, buttonHeight), "Host (방 만들기)"))
            {
                if (string.IsNullOrEmpty(_roomCode))
                    _roomCode = Random.Range(1000, 9999).ToString();

                StartGame(GameMode.Host, _roomCode);
            }

            // 4. Join 버튼 (참가하기)
            if (GUI.Button(new Rect(centerX, startY + 128 + buttonHeight + spacing, buttonWidth, buttonHeight), "Join (참가하기)"))
            {
                if (!string.IsNullOrEmpty(_roomCode))
                {
                    StartGame(GameMode.Client, _roomCode);
                }
                else
                {
                    Debug.LogWarning("참가하려면 방 코드를 입력하세요!");
                }
            }
        }
        else
        {
            // 게임 접속 중/플레이 중: 화면 상단에 방 코드 안내
            GUIStyle roomCodeStyle = new GUIStyle(GUI.skin.label);
            roomCodeStyle.fontSize = 22;
            roomCodeStyle.fontStyle = FontStyle.Bold;
            roomCodeStyle.normal.textColor = Color.white;
            roomCodeStyle.alignment = TextAnchor.MiddleCenter;

            float labelWidth = 300f;
            float labelHeight = 45f;
            float xPos = (Screen.width - labelWidth) / 2f;
            float yPos = 30f;

            GUI.Box(new Rect(xPos, yPos, labelWidth, labelHeight), "");
            GUI.Label(new Rect(xPos, yPos, labelWidth, labelHeight), $"방 코드: {_roomCode}", roomCodeStyle);

            GUI.Label(new Rect(10, Screen.height - 30, 200, 25), $"Status: {(_runner.IsServer ? "Host" : "Client")}");
        }
    }

    async void StartGame(GameMode mode, string roomName)
    {
        if (_isStarting) return;
        _isStarting = true;

        _runner = GetComponent<NetworkRunner>();
        if (_runner == null)
        {
            _runner = gameObject.AddComponent<NetworkRunner>();
        }

        DontDestroyOnLoad(gameObject);
        _runner.ProvideInput = true;

        var spawner = FindObjectOfType<Spawner>();
        if (spawner != null)
        {
            _runner.RemoveCallbacks(spawner);
            _runner.AddCallbacks(spawner);
        }

        Debug.Log($"<color=white>[Launcher]</color> {mode} 모드로 시작 시도 중... 방 코드: {roomName}");

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
                Debug.Log("호스트: CharacterSelect 씬으로 이동");
                _runner.LoadScene("CharacterSelect", LoadSceneMode.Single);
            }
        }
        else
        {
            _isStarting = false;
            Debug.LogError($"[Launcher] 시작 실패: {result.ShutdownReason}");
        }
    }

    public async void ExitGame()
    {
        if (_runner != null)
        {
            await _runner.Shutdown();
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        SceneManager.LoadScene("Lobby");
    }
}
