using System;
using System.Collections;
using System.Globalization;
using UnityEngine;
using UnityEngine.Networking;

// Firebase Authentication(REST) 래퍼. SDK 없이 UnityWebRequest 로만 동작한다(랭킹과 동일한 방식).
//   - 이메일/비밀번호 회원가입·로그인
//   - 이메일 인증(필수): 인증 완료 전에는 로그인 차단
//   - 비밀번호 재설정(이메일)
//   - 닉네임 중복 방지: RTDB /nicknames/{닉} = uid 로 조회/예약
//   - 게스트 플레이(계정 없이)
//
// 사전 준비(콘솔):
//   1) Authentication > 로그인 방법 > 이메일/비밀번호 "사용 설정"
//   2) 아래 WEB_API_KEY 에 프로젝트 설정의 "웹 API 키"를 붙여넣기 (비밀값 아님)
public class AuthManager : MonoBehaviour
{
    public static AuthManager Instance { get; private set; }

    // ▼▼▼ 콘솔 > 프로젝트 설정 > 웹 API 키 를 여기에 붙여넣으세요 ▼▼▼
    private const string WEB_API_KEY = "AIzaSyB29oNJxpIVmAFX5swQiETgJUcNWgTB5OU";
    // ▲▲▲
    private const string RTDB_BASE = "https://unity-93c2a-default-rtdb.asia-southeast1.firebasedatabase.app/";

    private const string IDTOOLKIT = "https://identitytoolkit.googleapis.com/v1/accounts:";

    // ---------------- 세션 상태 ----------------
    public bool IsLoggedIn { get; private set; }
    public bool IsGuest { get; private set; }
    public string Uid { get; private set; }
    public string Email { get; private set; }
    public string Nickname { get; private set; }

    private string _idToken;
    private string _refreshToken;

    // 이메일 인증 대기(가입 직후) 상태 — 링크 클릭을 자동 감지하기 위한 재로그인용 자격
    private string _pendingEmail, _pendingPassword, _pendingNickname, _pendingUid;
    public string PendingEmail => _pendingEmail;
    public bool HasPendingVerification => !string.IsNullOrEmpty(_pendingEmail);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("AuthManager");
        go.AddComponent<AuthManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        StartCoroutine(WarmupRoutine());
    }

    // 에디터에서 Play 진입 후 '첫 HTTPS 요청'은 인증서 검사(revocation) 등으로 15~20초 지연될 수 있다.
    // (그래서 첫 회원가입/로그인만 '시간초과'가 뜨고, 이후엔 캐시되어 빨라진다.)
    // 로비 진입 시 두 백엔드 호스트를 '동시에' 예열해 그 지연을 눈에 안 띄게 먼저 지불한다.
    private IEnumerator WarmupRoutine()
    {
        if (!HasApiKey) yield break;
        StartCoroutine(WarmupOne("https://identitytoolkit.googleapis.com/", "인증서버"));
        StartCoroutine(WarmupOne(RtdbUrl(".info/server/warmup"), "DB"));
        yield break;
    }

    private IEnumerator WarmupOne(string url, string label)
    {
        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = 25;
            yield return req.SendWebRequest();
        }
        Debug.Log($"<color=grey>[Auth]</color> 네트워크 워밍업 완료 ({label})");
    }

    private bool HasApiKey => !string.IsNullOrEmpty(WEB_API_KEY) && WEB_API_KEY != "PASTE_YOUR_WEB_API_KEY_HERE";

    // ==================================================================
    //  공개 API (콜백: onSuccess / onError(한글 메시지))
    // ==================================================================

    // 회원가입: 유효성 검사 → 닉네임 중복확인 → 계정 생성 → 닉네임 예약 → 인증메일 발송.
    // 성공해도 "로그인 상태"로 만들지 않는다(이메일 인증 후 로그인해야 함).
    public void SignUp(string email, string password, string passwordConfirm, string nickname,
        Action onSuccess, Action<string> onError)
    {
        if (!HasApiKey) { onError?.Invoke("웹 API 키가 설정되지 않았습니다. (AuthManager.WEB_API_KEY)"); return; }

        email = (email ?? "").Trim();
        nickname = (nickname ?? "").Trim();

        string err = ValidateEmail(email) ?? ValidatePassword(password)
                     ?? (password != passwordConfirm ? "비밀번호가 일치하지 않습니다." : null)
                     ?? ValidateNickname(nickname);
        if (err != null) { onError?.Invoke(err); return; }

        StartCoroutine(SignUpRoutine(email, password, nickname, onSuccess, onError));
    }

    // 로그인: 인증 → 이메일 인증여부 확인(미인증이면 차단) → 세션 설정.
    public void Login(string email, string password, Action onSuccess, Action<string> onError, Action onEmailNotVerified = null)
    {
        if (!HasApiKey) { onError?.Invoke("웹 API 키가 설정되지 않았습니다. (AuthManager.WEB_API_KEY)"); return; }
        email = (email ?? "").Trim();
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        { onError?.Invoke("이메일과 비밀번호를 입력하세요."); return; }

        StartCoroutine(LoginRoutine(email, password, onSuccess, onError, onEmailNotVerified));
    }

    // 비밀번호 재설정 메일 발송.
    public void SendPasswordReset(string email, Action onSuccess, Action<string> onError)
    {
        if (!HasApiKey) { onError?.Invoke("웹 API 키가 설정되지 않았습니다."); return; }
        email = (email ?? "").Trim();
        string err = ValidateEmail(email);
        if (err != null) { onError?.Invoke(err); return; }
        StartCoroutine(SendOobRoutine("PASSWORD_RESET", email, null, onSuccess, onError));
    }

    // 현재 미인증 계정에 인증메일 재발송 (로그인 시도로 idToken 을 받은 직후 사용).
    public void ResendVerification(string email, string password, Action onSuccess, Action<string> onError)
    {
        if (!HasApiKey) { onError?.Invoke("웹 API 키가 설정되지 않았습니다."); return; }
        StartCoroutine(ResendVerifyRoutine(email, password, onSuccess, onError));
    }

    // 게스트 플레이: 계정 없이 진행. 닉네임만 세션에 둔다.
    public void PlayAsGuest(string nickname)
    {
        IsGuest = true;
        IsLoggedIn = false;
        Uid = null; Email = null; _idToken = null; _refreshToken = null;
        Nickname = string.IsNullOrWhiteSpace(nickname) ? ("Guest" + UnityEngine.Random.Range(1000, 9999)) : nickname.Trim();
        PlayerPrefs.SetString("PlayerName", Nickname);
        PlayerPrefs.Save();
        Debug.Log($"<color=cyan>[Auth]</color> 게스트 플레이: {Nickname}");
    }

    public void Logout()
    {
        IsLoggedIn = false; IsGuest = false;
        Uid = null; Email = null; Nickname = null; _idToken = null; _refreshToken = null;
        ClearPending();
    }

    private void ClearPending() { _pendingEmail = _pendingPassword = _pendingNickname = _pendingUid = null; }

    // 가입 직후 "인증 대기" 화면에서 주기적으로/수동으로 호출.
    // 재로그인해서 emailVerified 를 확인하고, 인증됐으면 곧바로 로그인 세션을 만든다.
    //  onVerified  : 인증 확인 + 로그인 완료
    //  onNotYet    : 아직 미인증
    //  onError     : 네트워크/자격 오류
    public void CheckEmailVerified(Action onVerified, Action onNotYet, Action<string> onError)
    {
        if (!HasPendingVerification || string.IsNullOrEmpty(_pendingPassword))
        { onError?.Invoke("인증 대기 상태가 아닙니다."); return; }
        StartCoroutine(CheckVerifiedRoutine(onVerified, onNotYet, onError));
    }

    public void ResendPendingVerification(Action onSuccess, Action<string> onError)
    {
        if (!HasPendingVerification || string.IsNullOrEmpty(_pendingPassword))
        { onError?.Invoke("인증 대기 상태가 아닙니다."); return; }
        StartCoroutine(ResendVerifyRoutine(_pendingEmail, _pendingPassword, onSuccess, onError));
    }

    private IEnumerator CheckVerifiedRoutine(Action onVerified, Action onNotYet, Action<string> onError)
    {
        // 최신 상태를 위해 재로그인(토큰 갱신) 후 lookup
        string body = "{\"email\":\"" + Esc(_pendingEmail) + "\",\"password\":\"" + Esc(_pendingPassword) + "\",\"returnSecureToken\":true}";
        string idToken = null, refresh = null, uid = null, errMsg = null;
        yield return PostJson(IDTOOLKIT + "signInWithPassword?key=" + WEB_API_KEY, body,
            resp => { idToken = ReadJsonString(resp, "idToken"); refresh = ReadJsonString(resp, "refreshToken"); uid = ReadJsonString(resp, "localId"); },
            e => errMsg = MapAuthError(e));
        if (errMsg != null) { onError?.Invoke(errMsg); yield break; }
        if (string.IsNullOrEmpty(idToken)) { onError?.Invoke("확인에 실패했습니다."); yield break; }

        bool verified = false; string displayName = null;
        yield return PostJson(IDTOOLKIT + "lookup?key=" + WEB_API_KEY, "{\"idToken\":\"" + Esc(idToken) + "\"}",
            resp => { verified = ReadJsonBool(resp, "emailVerified"); displayName = ReadJsonString(resp, "displayName"); },
            e => errMsg = MapAuthError(e));
        if (errMsg != null) { onError?.Invoke(errMsg); yield break; }

        if (!verified) { onNotYet?.Invoke(); yield break; }

        // 인증됨 → 곧바로 로그인 세션 확립
        IsLoggedIn = true; IsGuest = false;
        Uid = uid; Email = _pendingEmail; _idToken = idToken; _refreshToken = refresh;
        Nickname = !string.IsNullOrEmpty(_pendingNickname) ? _pendingNickname : (string.IsNullOrEmpty(displayName) ? _pendingEmail : displayName);
        PlayerPrefs.SetString("PlayerName", Nickname);
        PlayerPrefs.Save();
        ClearPending();
        Debug.Log($"<color=lime>[Auth]</color> 이메일 인증 확인 → 로그인: {Email} / {Nickname}");
        onVerified?.Invoke();
    }

    // 닉네임 사용 가능 여부만 조회 (UI 실시간 체크용).

    // 닉네임 사용 가능 여부만 조회 (UI 실시간 체크용).
    public void CheckNicknameAvailable(string nickname, Action<bool> cb)
    {
        string err = ValidateNickname(nickname);
        if (err != null) { cb?.Invoke(false); return; }
        StartCoroutine(NicknameAvailableRoutine(nickname.Trim(), cb));
    }

    // ==================================================================
    //  코루틴
    // ==================================================================

    private IEnumerator SignUpRoutine(string email, string password, string nickname,
        Action onSuccess, Action<string> onError)
    {
        // 1) 닉네임 선점 여부 확인
        bool available = false;
        yield return NicknameAvailableRoutine(nickname, v => available = v);
        if (!available) { onError?.Invoke("이미 사용 중인 닉네임입니다."); yield break; }

        // 2) 계정 생성
        string body = "{\"email\":\"" + Esc(email) + "\",\"password\":\"" + Esc(password) + "\",\"returnSecureToken\":true}";
        string idToken = null, uid = null, errMsg = null;
        yield return PostJson(IDTOOLKIT + "signUp?key=" + WEB_API_KEY, body,
            resp => { idToken = ReadJsonString(resp, "idToken"); uid = ReadJsonString(resp, "localId"); },
            e => errMsg = MapAuthError(e));
        if (errMsg != null) { onError?.Invoke(errMsg); yield break; }
        if (string.IsNullOrEmpty(idToken) || string.IsNullOrEmpty(uid))
        { onError?.Invoke("계정 생성에 실패했습니다."); yield break; }

        // 3) 표시이름(닉네임) 설정
        string upBody = "{\"idToken\":\"" + Esc(idToken) + "\",\"displayName\":\"" + Esc(nickname) + "\",\"returnSecureToken\":false}";
        yield return PostJson(IDTOOLKIT + "update?key=" + WEB_API_KEY, upBody, null, _ => { });

        // 4) 닉네임 예약 + 프로필 저장 (RTDB)
        yield return PutJson(RtdbUrl("nicknames/" + NickKey(nickname), idToken), "\"" + Esc(uid) + "\"", null, _ => { });
        string profile = "{\"nickname\":\"" + Esc(nickname) + "\",\"email\":\"" + Esc(email) + "\"}";
        yield return PutJson(RtdbUrl("users/" + uid, idToken), profile, null, _ => { });

        // 5) 인증메일 발송
        yield return SendOobRoutine("VERIFY_EMAIL", null, idToken, null, e => Debug.LogWarning("[Auth] 인증메일 발송 경고: " + e));

        // 인증 대기 자동감지를 위해 자격 보관
        _pendingEmail = email; _pendingPassword = password; _pendingNickname = nickname; _pendingUid = uid;

        Debug.Log($"<color=lime>[Auth]</color> 회원가입 완료(인증메일 발송): {email} / {nickname}");
        onSuccess?.Invoke();
    }

    private IEnumerator LoginRoutine(string email, string password, Action onSuccess, Action<string> onError, Action onEmailNotVerified)
    {
        string body = "{\"email\":\"" + Esc(email) + "\",\"password\":\"" + Esc(password) + "\",\"returnSecureToken\":true}";
        string idToken = null, refresh = null, uid = null, errMsg = null;
        yield return PostJson(IDTOOLKIT + "signInWithPassword?key=" + WEB_API_KEY, body,
            resp => { idToken = ReadJsonString(resp, "idToken"); refresh = ReadJsonString(resp, "refreshToken"); uid = ReadJsonString(resp, "localId"); },
            e => errMsg = MapAuthError(e));
        if (errMsg != null) { onError?.Invoke(errMsg); yield break; }
        if (string.IsNullOrEmpty(idToken)) { onError?.Invoke("로그인에 실패했습니다."); yield break; }

        // 이메일 인증 여부 확인
        bool verified = false; string displayName = null;
        string lookupBody = "{\"idToken\":\"" + Esc(idToken) + "\"}";
        yield return PostJson(IDTOOLKIT + "lookup?key=" + WEB_API_KEY, lookupBody,
            resp => { verified = ReadJsonBool(resp, "emailVerified"); displayName = ReadJsonString(resp, "displayName"); },
            e => errMsg = MapAuthError(e));
        if (errMsg != null) { onError?.Invoke(errMsg); yield break; }

        if (!verified)
        {
            // 인증 대기 화면(자동 감지·재발송)에서 재입력 없이 쓰도록 자격을 보관한다.
            _pendingEmail = email; _pendingPassword = password;
            _pendingNickname = displayName; _pendingUid = uid;
            if (onEmailNotVerified != null) onEmailNotVerified.Invoke();
            else onError?.Invoke("이메일 인증이 필요합니다. 메일함의 인증 링크를 확인해 주세요.");
            yield break;
        }

        // 닉네임 로드 (RTDB 프로필 우선, 없으면 displayName)
        string nick = displayName;
        yield return GetJson(RtdbUrl("users/" + uid + "/nickname"), resp =>
        {
            string n = TrimJsonScalar(resp);
            if (!string.IsNullOrEmpty(n)) nick = n;
        }, _ => { });

        IsLoggedIn = true; IsGuest = false;
        Uid = uid; Email = email; _idToken = idToken; _refreshToken = refresh;
        Nickname = string.IsNullOrEmpty(nick) ? email : nick;
        PlayerPrefs.SetString("PlayerName", Nickname);
        PlayerPrefs.Save();

        Debug.Log($"<color=lime>[Auth]</color> 로그인 성공: {Email} / {Nickname}");
        onSuccess?.Invoke();
    }

    private IEnumerator ResendVerifyRoutine(string email, string password, Action onSuccess, Action<string> onError)
    {
        // 인증메일 재발송에는 idToken 이 필요하므로 먼저 로그인 시도로 토큰을 얻는다.
        string body = "{\"email\":\"" + Esc((email ?? "").Trim()) + "\",\"password\":\"" + Esc(password) + "\",\"returnSecureToken\":true}";
        string idToken = null, errMsg = null;
        yield return PostJson(IDTOOLKIT + "signInWithPassword?key=" + WEB_API_KEY, body,
            resp => idToken = ReadJsonString(resp, "idToken"), e => errMsg = MapAuthError(e));
        if (errMsg != null) { onError?.Invoke(errMsg); yield break; }
        if (string.IsNullOrEmpty(idToken)) { onError?.Invoke("인증메일 재발송에 실패했습니다."); yield break; }

        yield return SendOobRoutine("VERIFY_EMAIL", null, idToken, onSuccess, onError);
    }

    private IEnumerator SendOobRoutine(string requestType, string email, string idToken,
        Action onSuccess, Action<string> onError)
    {
        string body = idToken != null
            ? "{\"requestType\":\"" + requestType + "\",\"idToken\":\"" + Esc(idToken) + "\"}"
            : "{\"requestType\":\"" + requestType + "\",\"email\":\"" + Esc(email) + "\"}";

        string errMsg = null;
        yield return PostJson(IDTOOLKIT + "sendOobCode?key=" + WEB_API_KEY, body, null, e => errMsg = MapAuthError(e));
        if (errMsg != null) { onError?.Invoke(errMsg); yield break; }
        onSuccess?.Invoke();
    }

    private IEnumerator NicknameAvailableRoutine(string nickname, Action<bool> cb)
    {
        bool taken = false;
        yield return GetJson(RtdbUrl("nicknames/" + NickKey(nickname)), resp =>
        {
            // 값이 있으면(=uid 문자열) 사용중, "null" 이면 사용 가능
            taken = !string.IsNullOrEmpty(resp) && resp.Trim() != "null";
        }, _ => { taken = false; }); // 조회 실패 시 일단 통과(서버 저장 단계에서 재확인)
        cb?.Invoke(!taken);
    }

    // ==================================================================
    //  HTTP 헬퍼
    // ==================================================================

    private string RtdbUrl(string path, string authToken = null)
    {
        string b = RTDB_BASE.EndsWith("/") ? RTDB_BASE : RTDB_BASE + "/";
        string url = b + path + ".json";
        string tok = !string.IsNullOrEmpty(authToken) ? authToken : _idToken;
        if (!string.IsNullOrEmpty(tok))
            url += "?auth=" + UnityWebRequest.EscapeURL(tok);
        return url;
    }

    private IEnumerator PostJson(string url, string json, Action<string> onOk, Action<string> onErr)
    {
        using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 25;
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
                onOk?.Invoke(req.downloadHandler.text);
            else
                onErr?.Invoke(req.downloadHandler != null ? req.downloadHandler.text : req.error);
        }
    }

    private IEnumerator PutJson(string url, string json, Action<string> onOk, Action<string> onErr)
    {
        using (UnityWebRequest req = UnityWebRequest.Put(url, System.Text.Encoding.UTF8.GetBytes(json)))
        {
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 15;
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success) onOk?.Invoke(req.downloadHandler.text);
            else onErr?.Invoke(req.error);
        }
    }

    private IEnumerator GetJson(string url, Action<string> onOk, Action<string> onErr)
    {
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            req.timeout = 15;
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success) onOk?.Invoke(req.downloadHandler.text);
            else onErr?.Invoke(req.error);
        }
    }

    // ==================================================================
    //  유효성 검사 / 유틸
    // ==================================================================

    private static string ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "이메일을 입력하세요.";
        int at = email.IndexOf('@');
        int dot = email.LastIndexOf('.');
        if (at <= 0 || dot < at + 2 || dot >= email.Length - 1) return "이메일 형식이 올바르지 않습니다.";
        return null;
    }

    private static string ValidatePassword(string pw)
    {
        if (string.IsNullOrEmpty(pw)) return "비밀번호를 입력하세요.";
        if (pw.Length < 6) return "비밀번호는 6자 이상이어야 합니다.";
        return null;
    }

    private static string ValidateNickname(string nick)
    {
        if (string.IsNullOrWhiteSpace(nick)) return "닉네임을 입력하세요.";
        nick = nick.Trim();
        if (nick.Length < 2 || nick.Length > 8) return "닉네임은 2~8자여야 합니다.";
        // RTDB 키에 쓸 수 없는 문자 금지
        foreach (char c in nick)
            if (c == '.' || c == '#' || c == '$' || c == '[' || c == ']' || c == '/' || char.IsControl(c))
                return "닉네임에 사용할 수 없는 문자가 있습니다.";
        return null;
    }

    // RTDB 키로 안전하게 (경로 인코딩)
    private static string NickKey(string nick) => UnityWebRequest.EscapeURL(nick.Trim());

    private static string Esc(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
    }

    // {"key":"value"} 에서 value 추출
    private static string ReadJsonString(string json, string key)
    {
        if (string.IsNullOrEmpty(json)) return null;
        string needle = "\"" + key + "\"";
        int i = json.IndexOf(needle, StringComparison.Ordinal);
        if (i < 0) return null;
        i = json.IndexOf(':', i + needle.Length);
        if (i < 0) return null;
        int q1 = json.IndexOf('"', i + 1);
        if (q1 < 0) return null;
        int q2 = json.IndexOf('"', q1 + 1);
        while (q2 > 0 && json[q2 - 1] == '\\') q2 = json.IndexOf('"', q2 + 1);
        if (q2 < 0) return null;
        return json.Substring(q1 + 1, q2 - q1 - 1).Replace("\\\"", "\"").Replace("\\\\", "\\");
    }

    // {"key":true/false} 추출
    private static bool ReadJsonBool(string json, string key)
    {
        if (string.IsNullOrEmpty(json)) return false;
        string needle = "\"" + key + "\"";
        int i = json.IndexOf(needle, StringComparison.Ordinal);
        if (i < 0) return false;
        i = json.IndexOf(':', i + needle.Length);
        if (i < 0) return false;
        int j = i + 1;
        while (j < json.Length && (json[j] == ' ' || json[j] == '\t' || json[j] == '\n' || json[j] == '\r')) j++;
        return j < json.Length && (json[j] == 't' || json[j] == 'T');
    }

    // RTDB 스칼라 응답("\"닉\"" 또는 "null")에서 문자열만
    private static string TrimJsonScalar(string resp)
    {
        if (string.IsNullOrEmpty(resp)) return null;
        resp = resp.Trim();
        if (resp == "null") return null;
        if (resp.Length >= 2 && resp[0] == '"' && resp[resp.Length - 1] == '"')
            return resp.Substring(1, resp.Length - 2);
        return resp;
    }

    // Firebase 에러 코드 → 한글 메시지
    private static string MapAuthError(string respOrError)
    {
        if (string.IsNullOrEmpty(respOrError)) return "네트워크 오류가 발생했습니다.";
        string code = ReadJsonString(respOrError, "message");
        if (string.IsNullOrEmpty(code)) code = respOrError;

        if (code.StartsWith("EMAIL_EXISTS")) return "이미 가입된 이메일입니다.";
        if (code.StartsWith("EMAIL_NOT_FOUND")) return "가입되지 않은 이메일입니다.";
        if (code.StartsWith("INVALID_PASSWORD") || code.StartsWith("INVALID_LOGIN_CREDENTIALS")) return "이메일 또는 비밀번호가 올바르지 않습니다.";
        if (code.StartsWith("WEAK_PASSWORD")) return "비밀번호는 6자 이상이어야 합니다.";
        if (code.StartsWith("INVALID_EMAIL")) return "이메일 형식이 올바르지 않습니다.";
        if (code.StartsWith("USER_DISABLED")) return "비활성화된 계정입니다.";
        if (code.StartsWith("TOO_MANY_ATTEMPTS")) return "시도가 너무 많습니다. 잠시 후 다시 시도하세요.";
        if (code.StartsWith("OPERATION_NOT_ALLOWED")) return "이메일/비밀번호 로그인이 콘솔에서 비활성화돼 있습니다.";
        return "오류: " + code;
    }
}
