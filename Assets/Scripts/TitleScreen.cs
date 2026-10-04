using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 시작 메인 화면 (Voice Adventure) - Lobby 씬에 배치된 컴포넌트.
//  - [ExecuteAlways] 로 에디터 Game 뷰에서도 미리보기 (생성물은 HideAndDontSave 라 씬에 저장 안 됨)
//  - 배경: 위쪽 하늘 맵 이미지(back.png) + 아래로 갈수록 그라데이션 + 숲(far.png)
//  - 캐릭터(여우/토끼) 좌우 배치 + 가벼운 상하 흔들림
//  - 타이틀 BoldPixels, 입력창 Sunflower(가독성), 랭킹은 트로피 아이콘 버튼
[ExecuteAlways]
public class TitleScreen : MonoBehaviour
{
    public static TitleScreen Instance { get; private set; }

    private GameObject _root;
    private Font _pixel;
    private Font _plain;
    private InputField _roomField;
    private Button _hostBtn;
    private Button _joinBtn;
    private Text _noteText;
    private GameObject _disconnectBanner;   // 패널 '아래'에 뜨는 접속 종료 알림 배너
    private Text _disconnectBannerText;
    private BasicLauncher _launcher;
    private bool _busy;
    private float _busySince;

    // ---- 인증(로그인/회원가입) 통합 상태 ----
    private enum AuthView { Login, SignUp, Find, Verify }
    private AuthView _authView = AuthView.Login;
    private InputField _authEmail, _authPw, _authPw2, _authNick;
    private string _pendingNote;
    private bool _pendingNoteErr;
    private bool _needResend;   // 미인증 로그인 시도 후에만 "인증메일 재발송" 링크 표시
    private float _verifyPollAt;          // 인증 대기 화면 자동확인 폴링 타이머
    private Text _nickStatus;             // 회원가입 닉네임 실시간 상태 라벨
    private string _nickPending;          // 검사 대기 중인 닉네임
    private float _nickCheckAt;           // 닉네임 디바운스 타이머
    private UnityEngine.UI.Selectable[] _tabOrder;   // Tab 키 이동 순서(현재 뷰 기준)
    private static readonly Color ErrCol = new Color(0.96f, 0.55f, 0.45f);
    private static readonly Color OkCol  = new Color(0.72f, 0.90f, 0.62f);

    private RectTransform _char1;
    private RectTransform _char2;
    private Vector2 _char1Base;
    private Vector2 _char2Base;

    private static readonly Color WoodFill  = new Color(0.42f, 0.28f, 0.16f, 1f);
    private static readonly Color WoodDark  = new Color(0.19f, 0.11f, 0.05f);
    private static readonly Color WoodLight = new Color(0.60f, 0.43f, 0.24f);
    private static readonly Color Gold      = new Color(0.98f, 0.82f, 0.42f);
    private static readonly Color GoldDeep  = new Color(0.13f, 0.08f, 0.02f);
    private static readonly Color Stone     = new Color(0.60f, 0.64f, 0.68f);
    private static readonly Color Parch     = new Color(0.98f, 0.95f, 0.89f);
    private static readonly Color FieldBg   = new Color(0.15f, 0.11f, 0.07f, 1f);
    private static readonly Color Hint      = new Color(0.72f, 0.66f, 0.58f);
    private static readonly Color Ink       = new Color(0.12f, 0.07f, 0.02f);

    private const HideFlags GenFlags = HideFlags.HideAndDontSave;

    private void OnEnable()
    {
        Instance = this;
        _pixel = Resources.Load<Font>("BoldPixels");
        _plain = Resources.Load<Font>("Sunflower-Medium");
        if (_plain == null) _plain = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_pixel == null) _pixel = _plain;
        Rebuild();
    }

    private void OnDisable()
    {
        UnregisterLauncherEvents();
        DestroyGenerated();
    }

    private void OnDestroy()
    {
        UnregisterLauncherEvents();
        DestroyGenerated();
        if (Instance == this) Instance = null;
    }

    private void Rebuild()
    {
        DestroyGenerated();
        Build();
        RefreshMenu();
    }

    private void DestroyGenerated()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var c = transform.GetChild(i).gameObject;
            if (c.name == "TitleCanvas" || c.name == "TitleEventSystem")
            {
                if (Application.isPlaying) Destroy(c);
                else DestroyImmediate(c);
            }
        }
        _root = null;
        _char1 = null;
        _char2 = null;
    }

    private void RefreshMenu()
    {
        _busy = false;
        EnsureLauncher();
        SetInteractable(true);
        // 접속 종료 안내는 '패널 안 상태 줄'이 아니라 '패널 아래 배너'로 표시 (로그인/회원가입 링크와 겹치지 않게)
        if (PlayerPrefs.HasKey("DisconnectNotice"))
        {
            string notice = PlayerPrefs.GetString("DisconnectNotice");
            PlayerPrefs.DeleteKey("DisconnectNotice");
            ShowDisconnectBanner(notice);
        }

        if (_noteText != null)
        {
            if (!string.IsNullOrEmpty(_pendingNote)) { _noteText.text = _pendingNote; _noteText.color = _pendingNoteErr ? ErrCol : OkCol; }
            else _noteText.text = "";
        }
        _pendingNote = null;
    }

    // 패널 아래 알림 배너 표시 (접속 종료 등)
    private void ShowDisconnectBanner(string message)
    {
        if (_disconnectBanner == null || _disconnectBannerText == null) return;
        _disconnectBannerText.text = message;
        _disconnectBanner.SetActive(true);
    }

    private void EnsureLauncher()
    {
        if (_launcher == null)
        {
            if (BasicLauncher.Instance != null)
            {
                _launcher = BasicLauncher.Instance;
            }
            else
            {
                _launcher = FindFirstObjectByType<BasicLauncher>();
                if (_launcher == null)
                {
                    // 비활성 오브젝트에 있을 수 있으니 포함해서 검색
                    var all = FindObjectsByType<BasicLauncher>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    if (all != null && all.Length > 0) _launcher = all[0];
                }

                if (_launcher == null)
                {
                    // 이름으로 직접 오브젝트 검색 폴백
                    var go = GameObject.Find("NetworkLauncher");
                    if (go != null) _launcher = go.GetComponent<BasicLauncher>();
                }
            }
        }
        // 호스팅하려면 런처 오브젝트가 켜져 있어야 한다. (에디트 모드에선 씬을 건드리지 않도록 플레이 중에만)
        if (Application.isPlaying && _launcher != null && !_launcher.gameObject.activeSelf)
        {
            Debug.Log("<color=cyan>[TitleScreen]</color> NetworkLauncher 가 비활성이라 활성화합니다.");
            _launcher.gameObject.SetActive(true);
        }
        RegisterLauncherEvents();
    }

    private void RegisterLauncherEvents()
    {
        if (_launcher != null)
        {
            _launcher.OnConnectionStarted -= HandleConnectionStarted;
            _launcher.OnConnectionStarted += HandleConnectionStarted;
            _launcher.OnConnectionFailed -= HandleConnectionFailed;
            _launcher.OnConnectionFailed += HandleConnectionFailed;
            _launcher.OnConnectionSuccess -= HandleConnectionSuccess;
            _launcher.OnConnectionSuccess += HandleConnectionSuccess;
        }
    }

    private void UnregisterLauncherEvents()
    {
        if (_launcher != null)
        {
            _launcher.OnConnectionStarted -= HandleConnectionStarted;
            _launcher.OnConnectionFailed -= HandleConnectionFailed;
            _launcher.OnConnectionSuccess -= HandleConnectionSuccess;
        }
    }

    private void HandleConnectionStarted()
    {
        _busy = true;
        _busySince = Time.realtimeSinceStartup;
        SetInteractable(false);
    }

    private void HandleConnectionFailed(string reason)
    {
        _busy = false;
        SetInteractable(true);
        Debug.LogWarning($"<color=red>[TitleScreen]</color> 접속 실패: {reason}");
        if (_noteText != null)
        {
            _noteText.text = string.IsNullOrEmpty(reason) ? "접속 실패 - 다시 시도하세요" : reason;
            _noteText.color = ErrCol;
        }
    }

    private void HandleConnectionSuccess()
    {
        _busy = false;
        if (_noteText != null)
        {
            _noteText.text = "접속 성공! 방으로 이동 중...";
        }
    }

    private void Update()
    {
        float t = Time.realtimeSinceStartup;
        if (_char1 != null) _char1.anchoredPosition = _char1Base + new Vector2(0f, Mathf.Sin(t * 2.0f) * 8f);
        if (_char2 != null) _char2.anchoredPosition = _char2Base + new Vector2(0f, Mathf.Sin(t * 2.0f + 1.6f) * 8f);

        if (!Application.isPlaying) return;

        // Tab / Shift+Tab 으로 입력칸 이동 (레거시 InputField는 자동 이동이 없음)
        if (_tabOrder != null && _tabOrder.Length > 1 && Input.GetKeyDown(KeyCode.Tab))
        {
            var es = EventSystem.current;
            GameObject cur = es != null ? es.currentSelectedGameObject : null;
            int idx = -1;
            for (int i = 0; i < _tabOrder.Length; i++)
                if (_tabOrder[i] != null && _tabOrder[i].gameObject == cur) { idx = i; break; }
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            int step = shift ? -1 : 1;
            int next = idx < 0 ? 0 : ((idx + step) % _tabOrder.Length + _tabOrder.Length) % _tabOrder.Length;
            var field = _tabOrder[next] as InputField;
            if (field != null) { field.Select(); field.ActivateInputField(); }
        }

        // 비상용 안전 타임아웃: '실제 HOST/JOIN 연결이 진행 중'일 때만 발동한다.
        // (인증 요청은 자체 25초 타임아웃+콜백으로 _busy 를 풀므로, 여기서 오탐으로
        //  "연결 시간 초과"를 띄우면 안 된다. IsStarting 은 StartGame 동안만 true.)
        if (_busy && _launcher != null && _launcher.IsStarting && t - _busySince > 20f)
        {
            _busy = false;
            SetInteractable(true);
            if (_noteText != null) _noteText.text = "연결 시간 초과 - 다시 시도하세요";
            _ = _launcher.CleanupRunnerAsync();
        }

        // 인증 대기: 메일 링크 클릭을 5초 간격으로 자동 감지 → 인증되면 자동 로그인
        if (_authView == AuthView.Verify && !_busy && AuthManager.Instance != null
            && !AuthManager.Instance.IsLoggedIn && Time.unscaledTime >= _verifyPollAt)
        {
            _verifyPollAt = Time.unscaledTime + 5f;
            AuthManager.Instance.CheckEmailVerified(
                () => { _pendingNote = "이메일 인증 완료! 환영합니다."; _pendingNoteErr = false; Rebuild(); },
                () => { },
                _ => { });
        }

        // 회원가입 닉네임 실시간 중복 체크 (디바운스 경과 시 1회 조회)
        if (_authView == AuthView.SignUp && _nickStatus != null && _nickCheckAt > 0f && Time.unscaledTime >= _nickCheckAt)
        {
            _nickCheckAt = 0f;
            RunNicknameCheck();
        }
    }

    private void SetInteractable(bool v)
    {
        if (_hostBtn != null) _hostBtn.interactable = v;
        if (_joinBtn != null) _joinBtn.interactable = v;
    }

    private void OnHost()
    {
        if (!Application.isPlaying || _busy) return;
        EnsureLauncher();
        if (_launcher == null)
        {
            if (_noteText != null) { _noteText.text = "런처(NetworkLauncher)를 찾을 수 없습니다."; _noteText.color = ErrCol; }
            Debug.LogWarning("<color=red>[TitleScreen]</color> BasicLauncher(NetworkLauncher)를 찾지 못했습니다. Lobby 씬에 있는지 확인하세요.");
            return;
        }
        _busy = true; _busySince = Time.realtimeSinceStartup;
        SetInteractable(false);
        if (_noteText != null) _noteText.text = "방 만드는 중...";
        Debug.Log($"<color=cyan>[TitleScreen]</color> RequestHost 호출 room='{(_roomField != null ? _roomField.text : "")}' nick='{ResolveNickname()}'");
        _launcher.RequestHost(_roomField != null ? _roomField.text : "", ResolveNickname());
    }

    private void OnJoin()
    {
        if (!Application.isPlaying || _busy) return;
        EnsureLauncher();
        if (_launcher == null) return;
        if (_roomField == null || string.IsNullOrWhiteSpace(_roomField.text))
        {
            if (_noteText != null) _noteText.text = "방 코드를 입력하세요";
            return;
        }
        _busy = true; _busySince = Time.realtimeSinceStartup;
        SetInteractable(false);
        if (_noteText != null) _noteText.text = "접속 중...";
        _launcher.RequestJoin(_roomField.text, ResolveNickname());
    }

    private void OnRanking()
    {
        if (!Application.isPlaying) return;
        var rm = FindObjectOfType<RankingManager>();
        if (rm != null) rm.ToggleBoard();
        else RankingBoardUI.GetOrCreate().Toggle();
    }

    // ================= 인증/메뉴 패널 =================
    private bool IsAuthed()
    {
        var a = AuthManager.Instance;
        return Application.isPlaying && a != null && (a.IsLoggedIn || a.IsGuest);
    }

    private string ResolveNickname()
    {
        var a = AuthManager.Instance;
        if (a != null && !string.IsNullOrEmpty(a.Nickname)) return a.Nickname;
        return PlayerPrefs.GetString("PlayerName", "Player" + Random.Range(100, 999));
    }

    private void BuildPanel()
    {
        if (IsAuthed()) { BuildGameMenu(); return; }
        switch (_authView)
        {
            case AuthView.SignUp: BuildSignupView(); break;
            case AuthView.Find: BuildFindView(); break;
            case AuthView.Verify: BuildVerifyView(); break;
            default: BuildLoginView(); break;
        }
    }

    private Transform MakePanel(Vector2 size, Vector2 offset)
    {
        var panel = NewChild("Panel", _root.transform);
        var pr = panel.GetComponent<RectTransform>();
        pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
        pr.pivot = new Vector2(0.5f, 0.5f);
        pr.sizeDelta = size;
        pr.anchoredPosition = offset;
        Framed(panel, WoodFill, WoodDark, WoodLight);

        // 접속 종료 등 알림 배너: 우드 프레임 '바깥 아래'에 별도 표시 (기본 숨김)
        var banner = NewChild("DisconnectBanner", panel.transform);
        var brt = banner.GetComponent<RectTransform>();
        brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0f); // 패널 하단 기준
        brt.pivot = new Vector2(0.5f, 1f);                     // 아래로 뻗음
        brt.sizeDelta = new Vector2(size.x, 56f);
        brt.anchoredPosition = new Vector2(0f, -18f);          // 프레임 아래로 18px
        var bimg = banner.AddComponent<Image>();
        bimg.color = new Color(0.19f, 0.11f, 0.05f, 0.96f);    // 우드 다크 톤
        _disconnectBannerText = AddLabel(banner.transform, "", 19f, new Vector2(0.5f, 0.5f),
            size.x - 40f, TextAnchor.MiddleCenter, ErrCol, _plain, Vector2.zero);
        _disconnectBanner = banner;
        banner.SetActive(false);

        return panel.transform;
    }

    // 로그인/게스트 상태: 환영 + ROOM CODE + HOST/JOIN
    private void BuildGameMenu()
    {
        var panel = MakePanel(new Vector2(720f, 470f), new Vector2(0f, -150f));
        bool guest = AuthManager.Instance != null && AuthManager.Instance.IsGuest;

        AddLabel(panel, "환영합니다", 24f, new Vector2(0f, 1f), 400f, TextAnchor.UpperLeft, Hint, _plain, new Vector2(48f, -40f));
        AddLabel(panel, ResolveNickname() + (guest ? "  (게스트)" : ""), 40f, new Vector2(0f, 1f), 624f, TextAnchor.UpperLeft, Gold, _plain, new Vector2(48f, -76f));

        AddLabel(panel, "ROOM CODE", 28f, new Vector2(0f, 1f), 400f, TextAnchor.UpperLeft, Parch, _pixel, new Vector2(48f, -152f));
        _roomField = MakeInput(panel, new Vector2(48f, -184f), 624f, 74f, "HOST 시 자동 생성");
        _tabOrder = new UnityEngine.UI.Selectable[] { _roomField };

        _hostBtn = MakeButton(panel, new Vector2(48f, -282f), 300f, 104f, "HOST", Gold, GoldDeep, OnHost);
        _joinBtn = MakeButton(panel, new Vector2(372f, -282f), 300f, 104f, "JOIN", Stone, new Color(0.13f, 0.13f, 0.15f), OnJoin);

        MakeTextLink(panel, new Vector2(48f, -406f), 400f, 36f, guest ? "로그인 / 회원가입" : "로그아웃", OnLogoutClick);
        _noteText = AddLabel(panel, "", 21f, new Vector2(0.5f, 0f), 640f, TextAnchor.MiddleCenter, Parch, _plain, new Vector2(0f, 22f));
    }

    // 로그인 폼
    private void BuildLoginView()
    {
        var panel = MakePanel(new Vector2(720f, 620f), new Vector2(0f, -150f));
        AddLabel(panel, "LOGIN", 44f, new Vector2(0.5f, 1f), 600f, TextAnchor.UpperCenter, Gold, _pixel, new Vector2(0f, -34f));

        AddLabel(panel, "이메일", 25f, new Vector2(0f, 1f), 400f, TextAnchor.UpperLeft, Parch, _plain, new Vector2(48f, -100f));
        _authEmail = MakeInput(panel, new Vector2(48f, -132f), 624f, 72f, "you@example.com", 64, false);

        AddLabel(panel, "비밀번호", 25f, new Vector2(0f, 1f), 400f, TextAnchor.UpperLeft, Parch, _plain, new Vector2(48f, -224f));
        _authPw = MakeInput(panel, new Vector2(48f, -256f), 624f, 72f, "비밀번호", 32, true);
        _tabOrder = new UnityEngine.UI.Selectable[] { _authEmail, _authPw };

        MakeButton(panel, new Vector2(48f, -352f), 624f, 82f, "로그인", Gold, GoldDeep, OnLoginClick, 40, _plain);

        _noteText = AddLabel(panel, "", 21f, new Vector2(0.5f, 1f), 640f, TextAnchor.MiddleCenter, Parch, _plain, new Vector2(0f, -450f));

        // 하단 링크: 가운데 정렬. 미인증 로그인 시도 시에만 재발송 링크 추가.
        if (_needResend)
        {
            MakeTextLink(panel, new Vector2(-190f, -490f), 150f, 36f, "회원가입", () => SwitchView(AuthView.SignUp), true);
            MakeTextLink(panel, new Vector2(-20f, -490f), 170f, 36f, "비밀번호 찾기", () => SwitchView(AuthView.Find), true);
            MakeTextLink(panel, new Vector2(175f, -490f), 200f, 36f, "인증메일 재발송", OnResendClick, true);
        }
        else
        {
            MakeTextLink(panel, new Vector2(-90f, -490f), 150f, 36f, "회원가입", () => SwitchView(AuthView.SignUp), true);
            MakeTextLink(panel, new Vector2(90f, -490f), 170f, 36f, "비밀번호 찾기", () => SwitchView(AuthView.Find), true);
        }

        MakeButton(panel, new Vector2(48f, -536f), 624f, 58f, "게스트로 시작", Stone, new Color(0.13f, 0.13f, 0.15f), OnGuestClick, 30, _plain);
    }

    // 회원가입 폼
    private void BuildSignupView()
    {
        var panel = MakePanel(new Vector2(720f, 730f), new Vector2(0f, -120f));
        AddLabel(panel, "SIGN UP", 44f, new Vector2(0.5f, 1f), 600f, TextAnchor.UpperCenter, Gold, _pixel, new Vector2(0f, -32f));

        AddLabel(panel, "이메일", 23f, new Vector2(0f, 1f), 400f, TextAnchor.UpperLeft, Parch, _plain, new Vector2(48f, -92f));
        _authEmail = MakeInput(panel, new Vector2(48f, -122f), 624f, 66f, "you@example.com", 64, false);

        AddLabel(panel, "비밀번호 (6자 이상)", 23f, new Vector2(0f, 1f), 500f, TextAnchor.UpperLeft, Parch, _plain, new Vector2(48f, -204f));
        _authPw = MakeInput(panel, new Vector2(48f, -234f), 624f, 66f, "비밀번호", 32, true);

        AddLabel(panel, "비밀번호 확인", 23f, new Vector2(0f, 1f), 500f, TextAnchor.UpperLeft, Parch, _plain, new Vector2(48f, -316f));
        _authPw2 = MakeInput(panel, new Vector2(48f, -346f), 624f, 66f, "비밀번호 확인", 32, true);

        AddLabel(panel, "닉네임 (2~8자, 중복 불가)", 23f, new Vector2(0f, 1f), 360f, TextAnchor.UpperLeft, Parch, _plain, new Vector2(48f, -428f));
        _nickStatus = AddLabel(panel, "", 22f, new Vector2(1f, 1f), 280f, TextAnchor.UpperRight, Hint, _plain, new Vector2(-48f, -428f));
        _authNick = MakeInput(panel, new Vector2(48f, -458f), 624f, 66f, "게임에서 보일 이름", 8, false);
        _authNick.onValueChanged.AddListener(OnNickChanged);
        _tabOrder = new UnityEngine.UI.Selectable[] { _authEmail, _authPw, _authPw2, _authNick };

        MakeButton(panel, new Vector2(48f, -552f), 624f, 80f, "가입하기", Gold, GoldDeep, OnSignupClick, 40, _plain);
        _noteText = AddLabel(panel, "", 21f, new Vector2(0.5f, 1f), 640f, TextAnchor.MiddleCenter, Parch, _plain, new Vector2(0f, -646f));
        MakeTextLink(panel, new Vector2(48f, -684f), 300f, 36f, "← 로그인으로", () => SwitchView(AuthView.Login));
    }

    // 비밀번호 찾기 폼
    private void BuildFindView()
    {
        var panel = MakePanel(new Vector2(720f, 450f), new Vector2(0f, -150f));
        AddLabel(panel, "비밀번호 찾기", 36f, new Vector2(0.5f, 1f), 600f, TextAnchor.UpperCenter, Gold, _plain, new Vector2(0f, -34f));
        AddLabel(panel, "가입한 이메일로 재설정 링크를 보냅니다.", 21f, new Vector2(0.5f, 1f), 640f, TextAnchor.UpperCenter, Hint, _plain, new Vector2(0f, -90f));

        AddLabel(panel, "이메일", 25f, new Vector2(0f, 1f), 400f, TextAnchor.UpperLeft, Parch, _plain, new Vector2(48f, -146f));
        _authEmail = MakeInput(panel, new Vector2(48f, -178f), 624f, 74f, "you@example.com", 64, false);
        _tabOrder = new UnityEngine.UI.Selectable[] { _authEmail };

        MakeButton(panel, new Vector2(48f, -276f), 624f, 82f, "재설정 메일 보내기", Gold, GoldDeep, OnFindClick, 36, _plain);
        _noteText = AddLabel(panel, "", 21f, new Vector2(0.5f, 1f), 640f, TextAnchor.MiddleCenter, Parch, _plain, new Vector2(0f, -372f));
        MakeTextLink(panel, new Vector2(48f, -404f), 300f, 36f, "← 로그인으로", () => SwitchView(AuthView.Login));
    }

    // 이메일 인증 대기 화면 (가입 직후). 링크 클릭을 자동 감지(폴링)해 로그인까지 진행.
    private void BuildVerifyView()
    {
        var panel = MakePanel(new Vector2(720f, 440f), new Vector2(0f, -150f));
        _tabOrder = null;
        AddLabel(panel, "이메일 인증", 36f, new Vector2(0.5f, 1f), 600f, TextAnchor.UpperCenter, Gold, _plain, new Vector2(0f, -34f));

        string em = AuthManager.Instance != null ? AuthManager.Instance.PendingEmail : "";
        AddLabel(panel, string.IsNullOrEmpty(em) ? "인증 메일을 보냈습니다." : em, 25f, new Vector2(0.5f, 1f), 660f, TextAnchor.UpperCenter, Parch, _plain, new Vector2(0f, -96f));
        AddLabel(panel, "메일함에서 인증 링크를 누르면 자동으로 확인됩니다.", 20f, new Vector2(0.5f, 1f), 660f, TextAnchor.UpperCenter, Hint, _plain, new Vector2(0f, -134f));

        MakeButton(panel, new Vector2(48f, -200f), 624f, 78f, "인증 완료했어요 · 새로고침", Gold, GoldDeep, OnVerifyRefreshClick, 30, _plain);

        _noteText = AddLabel(panel, "", 21f, new Vector2(0.5f, 1f), 640f, TextAnchor.MiddleCenter, Hint, _plain, new Vector2(0f, -300f));

        MakeTextLink(panel, new Vector2(0f, -340f), 240f, 36f, "인증메일 재발송", OnResendPendingClick, true);
        MakeTextLink(panel, new Vector2(0f, -382f), 240f, 36f, "← 로그인으로", () => SwitchView(AuthView.Login), true);
    }

    private void SwitchView(AuthView v) { _needResend = false; _authView = v; Rebuild(); }

    private void SetNote(string msg, bool error)
    {
        if (_noteText != null) { _noteText.text = msg; _noteText.color = error ? ErrCol : Parch; }
    }

    // ---- 인증 액션 ----
    private void OnLoginClick()
    {
        if (!Application.isPlaying || _busy) return;
        if (AuthManager.Instance == null) { SetNote("인증 모듈을 찾을 수 없습니다.", true); return; }
        _needResend = false;
        _busy = true; SetNote("로그인 중...", false);
        AuthManager.Instance.Login(_authEmail != null ? _authEmail.text : "", _authPw != null ? _authPw.text : "",
            () => { _busy = false; _pendingNote = "환영합니다!"; _pendingNoteErr = false; Rebuild(); },
            msg => { _busy = false; SetNote(msg, true); },
            () => { _busy = false; _needResend = false; _authView = AuthView.Verify; _verifyPollAt = Time.unscaledTime + 5f; _pendingNote = "이메일 인증이 필요합니다 · 메일의 링크를 눌러주세요"; _pendingNoteErr = true; Rebuild(); });
    }

    private void OnSignupClick()
    {
        if (!Application.isPlaying || _busy) return;
        if (AuthManager.Instance == null) { SetNote("인증 모듈을 찾을 수 없습니다.", true); return; }
        _busy = true; SetNote("가입 처리 중...", false);
        AuthManager.Instance.SignUp(
            _authEmail != null ? _authEmail.text : "", _authPw != null ? _authPw.text : "",
            _authPw2 != null ? _authPw2.text : "", _authNick != null ? _authNick.text : "",
            () => { _busy = false; _authView = AuthView.Verify; _verifyPollAt = Time.unscaledTime + 5f; _pendingNote = "인증 대기 중 · 메일의 링크를 눌러주세요"; _pendingNoteErr = false; Rebuild(); },
            msg => { _busy = false; SetNote(msg, true); });
    }

    private void OnFindClick()
    {
        if (!Application.isPlaying || _busy) return;
        if (AuthManager.Instance == null) { SetNote("인증 모듈을 찾을 수 없습니다.", true); return; }
        _busy = true; SetNote("메일 발송 중...", false);
        AuthManager.Instance.SendPasswordReset(_authEmail != null ? _authEmail.text : "",
            () => { _busy = false; _authView = AuthView.Login; _pendingNote = "비밀번호 재설정 메일을 보냈습니다. 메일함을 확인하세요."; _pendingNoteErr = false; Rebuild(); },
            msg => { _busy = false; SetNote(msg, true); });
    }

    private void OnResendClick()
    {
        if (!Application.isPlaying || _busy) return;
        if (AuthManager.Instance == null) { SetNote("인증 모듈을 찾을 수 없습니다.", true); return; }
        _busy = true; SetNote("인증메일 재발송 중...", false);
        AuthManager.Instance.ResendVerification(_authEmail != null ? _authEmail.text : "", _authPw != null ? _authPw.text : "",
            () => { _busy = false; SetNote("인증메일을 다시 보냈습니다. 메일함을 확인하세요.", false); },
            msg => { _busy = false; SetNote(msg, true); });
    }

    private void OnGuestClick()
    {
        if (!Application.isPlaying || _busy) return;
        if (AuthManager.Instance != null) AuthManager.Instance.PlayAsGuest(null);
        Rebuild();
    }

    private void OnLogoutClick()
    {
        if (!Application.isPlaying) return;
        if (AuthManager.Instance != null) AuthManager.Instance.Logout();
        _authView = AuthView.Login;
        Rebuild();
    }

    private void OnVerifyRefreshClick()
    {
        if (!Application.isPlaying || _busy) return;
        if (AuthManager.Instance == null) return;
        _busy = true; SetNote("확인 중...", false);
        AuthManager.Instance.CheckEmailVerified(
            () => { _busy = false; _pendingNote = "이메일 인증 완료! 환영합니다."; _pendingNoteErr = false; Rebuild(); },
            () => { _busy = false; SetNote("아직 인증 전이에요. 메일의 링크를 눌렀는지 확인해 주세요.", true); },
            msg => { _busy = false; SetNote(msg, true); });
    }

    private void OnResendPendingClick()
    {
        if (!Application.isPlaying || _busy) return;
        if (AuthManager.Instance == null) return;
        _busy = true; SetNote("인증메일 재발송 중...", false);
        AuthManager.Instance.ResendPendingVerification(
            () => { _busy = false; SetNote("인증메일을 다시 보냈습니다. 메일함을 확인하세요.", false); },
            msg => { _busy = false; SetNote(msg, true); });
    }

    private void OnNickChanged(string v)
    {
        _nickPending = v;
        _nickCheckAt = Time.unscaledTime + 0.5f; // 디바운스
        if (_nickStatus != null) _nickStatus.text = "";
    }

    private void RunNicknameCheck()
    {
        if (_nickStatus == null) return;
        string nick = (_nickPending ?? "").Trim();
        if (nick.Length == 0) { SetNickStatus("", Hint); return; }
        if (nick.Length < 2 || nick.Length > 8) { SetNickStatus("2~8자", ErrCol); return; }
        SetNickStatus("확인 중...", Hint);
        if (AuthManager.Instance == null) { SetNickStatus("", Hint); return; }
        AuthManager.Instance.CheckNicknameAvailable(nick, ok =>
        {
            if (_authNick != null && _authNick.text.Trim() == nick)
                SetNickStatus(ok ? "사용 가능" : "이미 사용 중", ok ? OkCol : ErrCol);
        });
    }

    private void SetNickStatus(string msg, Color c)
    {
        if (_nickStatus != null) { _nickStatus.text = msg; _nickStatus.color = c; }
    }

    private Button MakeTextLink(Transform parent, Vector2 pos, float w, float h, string label,
        UnityEngine.Events.UnityAction onClick, bool center = false)
    {
        var go = NewChild("Link", parent);
        var rt = go.GetComponent<RectTransform>();
        var a = center ? new Vector2(0.5f, 1f) : new Vector2(0f, 1f);
        rt.anchorMin = rt.anchorMax = a;
        rt.pivot = a;
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = pos;

        var hit = go.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = hit;

        var l = NewChild("L", go.transform);
        InsetRect(l, 0f, 0f);
        var lt = l.AddComponent<Text>();
        lt.font = _plain; lt.fontSize = 24;
        lt.alignment = center ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
        lt.color = new Color(0.85f, 0.8f, 0.62f, 1f);
        lt.text = label; lt.raycastTarget = false;
        btn.onClick.AddListener(onClick);
        return btn;
    }

    // ================= UI 빌드 =================
    private void Build()
    {
        if (FindObjectOfType<EventSystem>() == null)
        {
            var es = new GameObject("TitleEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            es.hideFlags = GenFlags;
            es.transform.SetParent(transform, false);
        }

        _root = new GameObject("TitleCanvas");
        _root.hideFlags = GenFlags;
        _root.transform.SetParent(transform, false);
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        _root.AddComponent<GraphicRaycaster>();

        // ---- 배경: back.png 한 장을 화면 전체로 늘림 (화면 비율에 맞춰 stretch) ----
        var bgTex = Resources.Load<Texture2D>("Title/back");
        if (bgTex != null)
        {
            var bg = NewChild("Background", _root.transform);
            var brt = bg.GetComponent<RectTransform>();
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
            brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
            var braw = bg.AddComponent<RawImage>();
            braw.texture = bgTex;
            braw.raycastTarget = false;
        }

        // ---- 캐릭터 ----
        _char1 = MakeCharacter("Char1_Fox", "Title/char1", new Vector2(0f, 0f), new Vector2(320f, 155f), 320f, false);
        _char2 = MakeCharacter("Char2_Rabbit", "Title/char2", new Vector2(1f, 0f), new Vector2(-320f, 155f), 320f, true);
        if (_char1 != null) _char1Base = _char1.anchoredPosition;
        if (_char2 != null) _char2Base = _char2.anchoredPosition;

        // ---- 타이틀 ----
        MakeTitle(_root.transform, "VOICE ADVENTURE", 150f, new Vector2(0.5f, 0.875f), 1820f);
        var sub = AddLabel(_root.transform, "보이스 어드벤쳐", 40f, new Vector2(0.5f, 0.70f), 1200f, TextAnchor.MiddleCenter, Parch, _plain);
        var subOut = sub.gameObject.AddComponent<Outline>();
        subOut.effectColor = new Color(0.06f, 0.14f, 0.10f, 0.95f);
        subOut.effectDistance = new Vector2(3f, -3f);

        // ---- 메뉴/인증 패널 (로그인 상태에 따라 내용이 바뀜) ----
        BuildPanel();

        // ---- 트로피(랭킹) 버튼 ----
        MakeIconButton(_root.transform, "Title/skymon-trophy", new Vector2(1f, 1f), new Vector2(-40f, -40f), 92f, OnRanking);
        // 폰트 라이선스(BoldPixels, CC BY-SA 4.0) 표기는 README / 크레딧 화면에서 처리.

        SetGenFlagsRecursive(_root.transform);
    }

    private static void SetGenFlagsRecursive(Transform root)
    {
        root.gameObject.hideFlags = GenFlags;
        for (int i = 0; i < root.childCount; i++)
            SetGenFlagsRecursive(root.GetChild(i));
    }

    // ================= 헬퍼 =================
    private static GameObject NewChild(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.hideFlags = GenFlags;
        go.transform.SetParent(parent, false);
        return go;
    }

    private static Image FullImage(string name, Transform parent)
    {
        var go = NewChild(name, parent);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return go.AddComponent<Image>();
    }

    private static Sprite GradientSprite(Color top, Color mid, Color bot, int h)
    {
        var tex = new Texture2D(1, h, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        for (int y = 0; y < h; y++)
        {
            float f = y / (float)(h - 1);
            Color c = f < 0.5f ? Color.Lerp(bot, mid, f * 2f) : Color.Lerp(mid, top, (f - 0.5f) * 2f);
            tex.SetPixel(0, y, c);
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, 1f, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    // 텍스처의 [uvY, uvY+uvH] 세로 구간을 화면의 [bottomFrac, topFrac] 밴드에 가로 반복
    private void AddParallax(string name, Texture2D tex, float bottomFrac, float topFrac, float tilesX, float uvY, float uvH, Color tint)
    {
        if (tex == null) return;
        var go = NewChild(name, _root.transform);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, bottomFrac);
        rt.anchorMax = new Vector2(1f, topFrac);
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var raw = go.AddComponent<RawImage>();
        raw.texture = tex;
        raw.color = tint;
        raw.raycastTarget = false;
        raw.uvRect = new Rect(0f, uvY, Mathf.Max(1f, tilesX), uvH);
    }

    private RectTransform MakeCharacter(string name, string spritePath, Vector2 anchor, Vector2 pos, float size, bool mirror)
    {
        var sprites = Resources.LoadAll<Sprite>(spritePath);
        Sprite sp = (sprites != null && sprites.Length > 0) ? sprites[0] : Resources.Load<Sprite>(spritePath);
        if (sp == null) return null;

        var go = NewChild(name, _root.transform);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0f);
        float ratio = sp.rect.height <= 0f ? 1f : sp.rect.width / sp.rect.height;
        rt.sizeDelta = new Vector2(size * ratio, size);
        rt.anchoredPosition = pos;
        if (mirror) rt.localScale = new Vector3(-1f, 1f, 1f);
        var img = go.AddComponent<Image>();
        img.sprite = sp;
        img.preserveAspect = true;
        img.raycastTarget = false;
        return rt;
    }

    private static void Framed(GameObject go, Color fill, Color darkBorder, Color lightBevel)
    {
        go.AddComponent<Image>().color = darkBorder;
        AddInset(go.transform, "Bevel", 6f).color = lightBevel;
        AddInset(go.transform, "Face", 12f).color = fill;
    }

    private static Image AddInset(Transform parent, string name, float pad)
    {
        var go = NewChild(name, parent);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(pad, pad); rt.offsetMax = new Vector2(-pad, -pad);
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    private void MakeTitle(Transform parent, string text, float size, Vector2 anchor, float width)
    {
        var sh = AddLabel(parent, text, size, anchor, width, TextAnchor.MiddleCenter, GoldDeep, _pixel);
        sh.GetComponent<RectTransform>().anchoredPosition += new Vector2(9f, -13f);

        var main = AddLabel(parent, text, size, anchor, width, TextAnchor.MiddleCenter, Gold, _pixel);
        var outline = main.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.09f, 0.05f, 0.01f, 1f);
        outline.effectDistance = new Vector2(5f, -5f);
    }

    private Text AddLabel(Transform parent, string content, float size, Vector2 anchor, float width,
        TextAnchor align, Color color, Font font, Vector2? offset = null)
    {
        var go = NewChild("Label", parent);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(anchor.x, anchor.y);
        rt.sizeDelta = new Vector2(width, Mathf.Max(size * 1.7f, 56f));
        rt.anchoredPosition = offset ?? Vector2.zero;
        var t = go.AddComponent<Text>();
        t.font = font != null ? font : _plain;
        t.fontSize = Mathf.RoundToInt(size);
        t.alignment = align;
        t.color = color;
        t.text = content;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    private InputField MakeInput(Transform panel, Vector2 topLeft, float w, float h, string placeholder, int charLimit = 16, bool password = false)
    {
        var go = NewChild("Field", panel);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = topLeft;

        var bg = go.AddComponent<Image>();
        bg.color = FieldBg;
        var input = go.AddComponent<InputField>();
        input.targetGraphic = bg;

        var textGO = NewChild("Text", go.transform);
        InsetRect(textGO, 22f, 6f);
        var txt = textGO.AddComponent<Text>();
        txt.font = _plain;
        txt.fontSize = 40;
        txt.color = Parch;
        txt.alignment = TextAnchor.MiddleLeft;
        txt.supportRichText = false;
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        txt.verticalOverflow = VerticalWrapMode.Truncate;

        var phGO = NewChild("Placeholder", go.transform);
        InsetRect(phGO, 22f, 6f);
        var ph = phGO.AddComponent<Text>();
        ph.font = _plain;
        ph.fontSize = 34;
        ph.fontStyle = FontStyle.Italic;
        ph.color = Hint;
        ph.alignment = TextAnchor.MiddleLeft;
        ph.text = placeholder;

        input.textComponent = txt;
        input.placeholder = ph;
        input.caretColor = Gold;
        input.customCaretColor = true;
        input.caretWidth = 4;
        input.lineType = InputField.LineType.SingleLine;
        input.characterLimit = charLimit;
        if (password) input.contentType = InputField.ContentType.Password;
        return input;
    }

    private static void InsetRect(GameObject go, float padX, float padY)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padX, padY);
        rt.offsetMax = new Vector2(-padX, -padY);
    }

    private Button MakeButton(Transform parent, Vector2 topLeft, float w, float h, string label,
        Color face, Color shadow, UnityEngine.Events.UnityAction onClick, int fontSize = 46, Font labelFont = null)
    {
        var go = NewChild("Btn_" + label, parent);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = topLeft;

        var hit = go.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        var btn = go.AddComponent<Button>();

        var shImg = AddInset(go.transform, "Shadow", 0f);
        shImg.rectTransform.offsetMin = new Vector2(0f, -8f);
        shImg.rectTransform.offsetMax = new Vector2(0f, -8f);
        shImg.color = shadow;

        var faceImg = AddInset(go.transform, "Face", 0f);
        faceImg.color = face;
        btn.targetGraphic = faceImg;
        var cb = btn.colors;
        cb.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
        cb.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.55f);
        cb.fadeDuration = 0.05f;
        btn.colors = cb;
        btn.onClick.AddListener(onClick);

        var l = NewChild("L", faceImg.transform);
        InsetRect(l, 0f, 0f);
        var lt = l.AddComponent<Text>();
        lt.font = labelFont != null ? labelFont : _pixel;
        lt.fontSize = fontSize;
        lt.alignment = TextAnchor.MiddleCenter;
        lt.color = Ink;
        lt.text = label;
        lt.raycastTarget = false;
        return btn;
    }

    private void MakeIconButton(Transform parent, string spritePath, Vector2 anchor, Vector2 pos, float size,
        UnityEngine.Events.UnityAction onClick)
    {
        var go = NewChild("IconBtn", parent);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = pos;

        // 갈색 버튼 배경(#6A472A) + 어두운 외곽선(#2F1C16)
        var bg = go.AddComponent<Image>();
        bg.color = new Color32(0x6A, 0x47, 0x2A, 0xFF);
        var bgOutline = go.AddComponent<UnityEngine.UI.Outline>();
        bgOutline.effectColor = new Color32(0x2F, 0x1C, 0x16, 0xFF);
        bgOutline.effectDistance = new Vector2(3f, -3f);

        // 골드 아이콘(#FAD16A) + 외곽선(#2F1C16)
        var iconGo = NewChild("Icon", go.transform);
        var irt = iconGo.GetComponent<RectTransform>();
        irt.anchorMin = new Vector2(0.14f, 0.14f); irt.anchorMax = new Vector2(0.86f, 0.86f);
        irt.offsetMin = Vector2.zero; irt.offsetMax = Vector2.zero;
        var iimg = iconGo.AddComponent<Image>();
        var sprites = Resources.LoadAll<Sprite>(spritePath);
        Sprite sp = (sprites != null && sprites.Length > 0) ? sprites[0] : Resources.Load<Sprite>(spritePath);
        iimg.color = new Color32(0xFA, 0xD1, 0x6A, 0xFF);
        if (sp != null) { iimg.sprite = sp; iimg.preserveAspect = true; }
        var iconOutline = iconGo.AddComponent<UnityEngine.UI.Outline>();
        iconOutline.effectColor = new Color32(0x2F, 0x1C, 0x16, 0xFF);
        iconOutline.effectDistance = new Vector2(2f, -2f);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = bg;
        var cb = btn.colors;
        cb.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        cb.fadeDuration = 0.05f;
        btn.colors = cb;
        btn.onClick.AddListener(onClick);
    }
}
