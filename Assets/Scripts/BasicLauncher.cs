using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

public class BasicLauncher : MonoBehaviour
{
    private NetworkRunner _runner;
    private string _roomCode = ""; // 사용자가 입력할 방 코드
    private bool _isStarting = false;
    private string _playerName = "";
    private bool _isMenuOpen = false;
    private bool _useIMGUIMenu = false; // 새 TitleScreen 사용. 옛 IMGUI 시작 메뉴는 꺼둔다.
    private void Start()
    {
        // 이전에 썼던 이름 불러오기
        _playerName = PlayerPrefs.GetString("PlayerName", "Player" + Random.Range(100, 999));
    }
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            _isMenuOpen = !_isMenuOpen;
            // 마우스 커서 상태 조절
            Cursor.lockState = _isMenuOpen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = _isMenuOpen;
        }
    }
    private void OnGUI()
    {
        // 랭킹 보드가 열려 있으면 IMGUI 메뉴/버튼도 막는다 (모달)
        if (RankingManager.Instance != null && RankingManager.Instance.IsBoardOpen) return;

        if (_useIMGUIMenu && _runner == null)
        {
            float buttonWidth = 250f;
            float buttonHeight = 40f;
            float spacing = 15f;
            float centerX = (Screen.width - buttonWidth) / 2f;
            float centerY = (Screen.height - 200) -30/ 2f;

            // 1. 방 코드 입력창 (텍스트 필드)
            GUI.Label(new Rect(centerX, centerY - 25, buttonWidth, 20), "방 코드 입력 (Join 시 필요):");
            _roomCode = GUI.TextField(new Rect(centerX, centerY, buttonWidth, buttonHeight), _roomCode);
            GUI.Label(new Rect(centerX, centerY - 80 , buttonWidth, 20), "닉네임:");
            _playerName = GUI.TextField(new Rect(centerX, centerY - 60 , buttonWidth, 30), _playerName);
            PlayerPrefs.SetString("PlayerName", _playerName); // 입력할 때마다 저장
            // 2. Host 버튼 (랜덤 코드 생성)
            if (GUI.Button(new Rect(centerX, centerY + buttonHeight + spacing, buttonWidth, buttonHeight), "Host (방 만들기)"))
            {
                // 호스트는 코드가 없으면 랜덤하게 생성 (예: 4자리 숫자)
                if (string.IsNullOrEmpty(_roomCode))
                    _roomCode = Random.Range(1000, 9999).ToString();

                StartGame(GameMode.Host, _roomCode);
            }

            // 3. Join 버튼 (입력한 코드로 접속)
            if (GUI.Button(new Rect(centerX, centerY + (buttonHeight + spacing) * 2 - 20, buttonWidth, buttonHeight), "Join (참여하기)"))
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
        if (_isMenuOpen)
        {
            float w = 200; float h = 150;
            Rect menuRect = new Rect(Screen.width / 2 - w / 2, Screen.height / 2 - h / 2, w, h);
            GUI.Box(menuRect, "Menu");

            if (GUI.Button(new Rect(menuRect.x + 50, menuRect.y + 40, 100, 30), "계속하기"))
                _isMenuOpen = false;
            if (GUI.Button(new Rect(menuRect.x + 50, menuRect.y + 80, 100, 30), "방 나가기"))
            {
                ExitGame();
            }

         
        }
    }

    // TitleScreen 에서 호출하는 진입점
    public void RequestHost(string roomCode, string nick)
    {
        ApplyNick(nick);
        string code = string.IsNullOrWhiteSpace(roomCode) ? Random.Range(1000, 9999).ToString() : roomCode.Trim();
        _roomCode = code;
        StartGame(GameMode.Host, code);
    }

    public void RequestJoin(string roomCode, string nick)
    {
        ApplyNick(nick);
        if (string.IsNullOrWhiteSpace(roomCode))
        {
            Debug.LogError("[Launcher] 접속할 방 코드를 입력하세요");
            return;
        }
        _roomCode = roomCode.Trim();
        StartGame(GameMode.Client, _roomCode);
    }

    private void ApplyNick(string nick)
    {
        if (!string.IsNullOrWhiteSpace(nick))
        {
            _playerName = nick.Trim();
            PlayerPrefs.SetString("PlayerName", _playerName);
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
            // [수정] 현재 씬을 기준으로 시작하도록 설정
            Scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex),

            // [핵심] SceneManager가 없으면 에러가 납니다. 아래 코드로 확실히 추가/연결하세요.
            SceneManager = gameObject.GetComponent<NetworkSceneManagerDefault>() ?? gameObject.AddComponent<NetworkSceneManagerDefault>()
        });

        if (result.Ok)
        {
            if (mode == GameMode.Host)
            {
                Debug.Log("호스트: CharacterSelect 이동");
                // [주의] 위에서 SceneManager가 잘 초기화되었다면 이제 이 코드가 작동합니다.
                _runner.LoadScene("CharacterSelect", LoadSceneMode.Single);
            }
        }
        else
        {
            _isStarting = false;
            Debug.LogError($"[Launcher] 시작 실패: {result.ShutdownReason}");
        }
    }
    private async void ExitGame()
    {
        if (_runner != null)
        {
            // 1. 네트워크 연결을 완전히 종료 (매우 중요!)
            await _runner.Shutdown();
        }

        // 2. 마우스 커서 다시 활성화 (로비에서 클릭해야 하니까요)
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 3. 로비 씬으로 이동
        // 주의: Build Settings에 등록된 로비 씬 이름과 똑같아야 합니다!
        SceneManager.LoadScene("Lobby");
    }
}