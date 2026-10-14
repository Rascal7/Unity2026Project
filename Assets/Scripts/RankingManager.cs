using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public class RankingManager : MonoBehaviour
{
    public static RankingManager Instance;
    private bool _isAutoInstance = false;

    // TOP5 진입 시 (순위, 기록초) 브로드캐스트 — NewRecordUI 가 구독해 축하 연출.
    public static event System.Action<int, float> OnTopRankAchieved;

    [Header("Firebase Realtime Database")]
    [SerializeField]
    private string _firebaseBaseUrl = "https://unity-93c2a-default-rtdb.asia-southeast1.firebasedatabase.app/";

    private void Awake()
    {
        // 이 오브젝트는 각 UI(UIManager)와 같은 GameObject 에 붙어 있을 수 있다.
        // 항상 "가장 최근 인스턴스"를 Instance 에 둔다.
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // 외부에서 RankingManager 가 없을 때 임시 인스턴스 생성
    public static RankingManager GetOrCreate()
    {
        if (Instance == null)
        {
            var go = new GameObject("RankingManager (auto)");
            DontDestroyOnLoad(go);
            var rm = go.AddComponent<RankingManager>();
            rm._isAutoInstance = true;
        }
        return Instance;
    }

    // 최소 물리적 유효 클리어 타임 (스테이지 1~3 전체 클리어 기준 물리적 최소치: 15초)
    public const float MinValidClearTime = 15.0f;
    public const float MaxValidClearTime = 86400.0f;

    private static float _lastSubmitTimestamp = -999f;
    private static float _lastSubmittedTimeVal = -1f;
    private int _pendingSubmitShards = 0;

    public void SaveScore(float time)
    {
        var names = ResolvePlayerNames();
        SaveScore(time, names.Item1, names.Item2);
    }

    // 특정 플레이어 닉네임을 직접 넘길 수 있는 오버로드
    public void SaveScore(float time, string p1, string p2)
    {
        _pendingSubmitShards = Mathf.Max(0, RunManager.TotalCollectedShards);

        // 1. 수치 유효성 검사 (NaN, Infinity, 0 이하, 비현실적인 타임)
        if (float.IsNaN(time) || float.IsInfinity(time) || time <= 0f)
        {
            Debug.LogWarning($"<color=red>[Ranking Security] 비정상적인 클리어 타임 값({time}) 등록 차단.</color>");
            return;
        }

        // 조각 단축 보너스로 인해 최소 유효 시간 아래로 내려간 정상 플레이 기록은 최소치로 보정
        if (time < MinValidClearTime && _pendingSubmitShards > 0 && (time + _pendingSubmitShards * RunManager.ShardTimeBonusSeconds) >= MinValidClearTime)
        {
            time = MinValidClearTime;
        }

        if (time < MinValidClearTime)
        {
            Debug.LogWarning($"<color=red>[Ranking Security] 비정상적인 초고속 클리어 타임({time:F2}s < 최소 {MinValidClearTime}s) 감지. 랭킹 등록이 차단되었습니다.</color>");
            return;
        }

        if (time > MaxValidClearTime)
        {
            Debug.LogWarning($"<color=red>[Ranking Security] 비정상적으로 긴 클리어 타임({time:F2}s) 등록 차단.</color>");
            return;
        }

        // 2. 네트워크 권한 검사 (멀티플레이에서 호스트/서버만 전송 가능)
        var runner = FindFirstObjectByType<Fusion.NetworkRunner>();
        if (runner != null && runner.IsRunning && !runner.IsServer)
        {
            Debug.LogWarning("<color=red>[Ranking Security] 클라이언트 권한으로는 직접 랭킹을 등록할 수 없습니다. 호스트만 전송 가능합니다.</color>");
            return;
        }

        // 3. 중복 전송 방지 (Debounce / Rate Limiting)
        if (Time.unscaledTime - _lastSubmitTimestamp < 3.0f && Mathf.Approximately(_lastSubmittedTimeVal, time))
        {
            Debug.LogWarning($"<color=yellow>[Ranking Security] 중복 랭킹 전송 요청 무시 (최근 전송: {_lastSubmittedTimeVal:F2}s)</color>");
            return;
        }

        _lastSubmitTimestamp = Time.unscaledTime;
        _lastSubmittedTimeVal = time;

        // 4. 플레이어 이름 검증 및 새니타이징
        p1 = SanitizePlayerName(p1, "Player1");
        p2 = SanitizePlayerName(p2, "Player2");

        Debug.Log($"<color=cyan>[Ranking]</color> 클리어 {time:F2}초 (조각 {_pendingSubmitShards}개) 랭킹 등록 시도 ({p1} & {p2})");
        StartCoroutine(SaveAndEvaluateRoutine(p1, p2, time, _pendingSubmitShards));
    }

    private static string SanitizePlayerName(string name, string fallback)
    {
        if (string.IsNullOrWhiteSpace(name)) return fallback;
        name = name.Trim();
        var sb = new System.Text.StringBuilder();
        foreach (char c in name)
        {
            if (!char.IsControl(c) && c != '\"' && c != '\\')
            {
                sb.Append(c);
            }
        }
        string result = sb.ToString().Trim();
        if (string.IsNullOrEmpty(result)) return fallback;
        if (result.Length > 12) result = result.Substring(0, 12);
        return result;
    }

    // 씬의 PlayerNameTag에서 닉네임 수집
    private System.Tuple<string, string> ResolvePlayerNames()
    {
        var tags = FindObjectsByType<PlayerNameTag>(UnityEngine.FindObjectsSortMode.None);
        var names = new List<string>();
        foreach (var tag in tags)
        {
            if (tag != null && !string.IsNullOrWhiteSpace(tag.Nickname))
                names.Add(tag.Nickname.Trim());
        }
        names.Sort(System.StringComparer.Ordinal);

        string p1 = names.Count > 0 ? names[0] : PlayerPrefs.GetString("PlayerName", "Player1");
        string p2 = names.Count > 1 ? names[1] : "Player2";
        return System.Tuple.Create(p1, p2);
    }

    private string GetFirebaseEndpoint(string pathAndQuery)
    {
        string baseUrl = _firebaseBaseUrl.Trim();
        if (!baseUrl.EndsWith("/")) baseUrl += "/";
        string url = baseUrl + pathAndQuery;

        // Firebase Auth 인증 토큰이 존재하고, 쿼리에 auth 파라미터가 없으면 자동 첨부
        string token = AuthManager.Instance != null ? AuthManager.Instance.IdToken : null;
        if (!string.IsNullOrEmpty(token) && !url.Contains("auth="))
        {
            char sep = url.Contains("?") ? '&' : '?';
            url = $"{url}{sep}auth={token}";
        }
        return url;
    }

    // 최근 리더보드(상위 5) 캐시 — 클리어 즉시 순위 판정용. Prefetch 로 미리 채운다.
    private static List<RankData> _cachedTop = null;

    // 스테이지 진입 등에서 미리 호출해두면, 클리어 순간 네트워크 대기 없이 순위를 계산할 수 있다.
    public static void PrefetchLeaderboard()
    {
        var rm = GetOrCreate();
        if (rm != null) rm.StartCoroutine(rm.FetchTopRoutine());
    }

    private IEnumerator FetchTopRoutine()
    {
        if (AuthManager.Instance != null && string.IsNullOrEmpty(AuthManager.Instance.IdToken))
        {
            yield return StartCoroutine(AuthManager.Instance.EnsureTokenRoutine(_ => { }));
        }

        string url = GetFirebaseEndpoint("ranks.json?orderBy=\"clearTime\"&limitToFirst=5");
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                _cachedTop = ParseFirebaseRanks(req.downloadHandler.text);
            }
            else
            {
                // 인덱스 미설정 등으로 실패 시 일반 ranks.json 으로 폴백
                string fallbackUrl = GetFirebaseEndpoint("ranks.json");
                using (UnityWebRequest reqFallback = UnityWebRequest.Get(fallbackUrl))
                {
                    yield return reqFallback.SendWebRequest();
                    if (reqFallback.result == UnityWebRequest.Result.Success)
                    {
                        _cachedTop = ParseFirebaseRanks(reqFallback.downloadHandler.text);
                    }
                }
            }
        }
    }

    private static int ComputeRank(List<RankData> top, float time)
    {
        if (top == null) return -1;
        int faster = 0;
        foreach (var r in top) if (r.clearTime < (double)time) faster++;
        return faster + 1; // 나보다 빠른 기록 수 + 1
    }

    private void FireIfTop(int rank, float time)
    {
        if (rank >= 1 && rank <= 5)
        {
            Debug.Log($"<color=magenta>[Ranking]</color> NEW RECORD! {rank}위 ({time:F2}s)");
            try { OnTopRankAchieved?.Invoke(rank, time); }
            catch (System.Exception e) { Debug.LogException(e); }
        }
    }

    // 클리어 즉시(캐시가 있으면) 순위를 판정해 축하 이벤트를 먼저 쏘고, 그 다음 기록을 저장한다.
    IEnumerator SaveAndEvaluateRoutine(string p1, string p2, float time, int shards)
    {
        int rank;
        if (_cachedTop != null)
        {
            // 캐시로 즉시 판정 → 깨자마자 New Record 표시 (네트워크 대기 없음)
            rank = ComputeRank(_cachedTop, time);
            FireIfTop(rank, time);
        }
        else
        {
            // 캐시가 없으면 지금 조회 후 판정 (약간의 지연)
            yield return StartCoroutine(FetchTopRoutine());
            rank = ComputeRank(_cachedTop, time);
            FireIfTop(rank, time);
        }

        // 기록 저장 (POST)
        yield return StartCoroutine(PostRankingData(p1, p2, time, shards));

        // 다음 판정을 위해 캐시 갱신
        yield return StartCoroutine(FetchTopRoutine());

        // 임시(auto) 인스턴스 정리
        if (_isAutoInstance)
        {
            if (Instance == this) Instance = null;
            Destroy(gameObject);
        }
    }

    IEnumerator PostRankingData(string p1, string p2, float time, int shards)
    {
        if (AuthManager.Instance != null && string.IsNullOrEmpty(AuthManager.Instance.IdToken))
        {
            yield return StartCoroutine(AuthManager.Instance.EnsureTokenRoutine(_ => { }));
        }

        string url = GetFirebaseEndpoint("ranks.json");

        RankData newData = new RankData
        {
            player1 = p1,
            player2 = p2,
            clearTime = (double)time,
            createdAt = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            shards = shards
        };

        string json = JsonUtility.ToJson(newData);

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            Debug.Log("<color=yellow>[Firebase]</color> 랭킹 데이터 전송 중...");
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"<color=lime>★ [Firebase Success] 랭킹 저장 완료! ★ (응답: {request.downloadHandler.text})</color>");
            }
            else
            {
                Debug.LogError($"<color=red>[Firebase Error]</color> 랭킹 저장 실패: {request.error} | Response: {request.downloadHandler?.text}");
            }
        }

    }

    [System.Serializable]
    public class RankRowUI
    {
        public TextMeshProUGUI rankText;    // 1st, 2nd... 가 들어갈 칸
        public TextMeshProUGUI namesText;   // 플레이어 이름들이 들어갈 칸
        public TextMeshProUGUI timeText;    // 클리어 타임이 들어갈 칸
    }

    [System.Serializable]
    public class RankData
    {
        public string player1;
        public string player2;
        public double clearTime;
        public long createdAt;
        public int shards;
    }

    [System.Serializable]
    public class RankList
    {
        public List<RankData> items;
    }

    public RankRowUI[] rankRows;

    public void RequestRanking()
    {
        StartCoroutine(GetRankingData());
    }

    // ---- 랭킹 보드(자동 생성 UI) 연동 ----
    public bool IsBoardOpen
    {
        get { return RankingBoardUI.Instance != null && RankingBoardUI.Instance.IsOpen; }
    }

    public void ShowBoard()
    {
        RankingBoardUI.GetOrCreate().Show();
        RequestRanking();
    }

    public void HideBoard()
    {
        if (RankingBoardUI.Instance != null) RankingBoardUI.Instance.Hide();
    }

    public void ToggleBoard()
    {
        var board = RankingBoardUI.GetOrCreate();
        if (board.IsOpen)
        {
            board.Hide();
        }
        else
        {
            board.Show();
            RequestRanking();
        }
    }

    IEnumerator GetRankingData()
    {
        if (AuthManager.Instance != null && string.IsNullOrEmpty(AuthManager.Instance.IdToken))
        {
            yield return StartCoroutine(AuthManager.Instance.EnsureTokenRoutine(_ => { }));
        }

        // Firebase Realtime Database: clearTime 기준 오름차순(빠른 순) 상위 20개 조회
        string url = GetFirebaseEndpoint("ranks.json?orderBy=\"clearTime\"&limitToFirst=20");
        string rawJson = null;

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                rawJson = request.downloadHandler.text;
            }
            else
            {
                Debug.LogWarning($"<color=yellow>[Firebase]</color> 인덱스 정렬 쿼리 실패 ({request.error}). 일반 조회로 폴백합니다.");
            }
        }

        // orderBy 쿼리 실패 시 (규칙에 indexOn 미설정 등), 전체 ranks.json 을 받아와 클라이언트에서 정렬
        if (string.IsNullOrEmpty(rawJson))
        {
            string fallbackUrl = GetFirebaseEndpoint("ranks.json");
            using (UnityWebRequest reqFallback = UnityWebRequest.Get(fallbackUrl))
            {
                yield return reqFallback.SendWebRequest();
                if (reqFallback.result == UnityWebRequest.Result.Success)
                {
                    rawJson = reqFallback.downloadHandler.text;
                }
                else
                {
                    Debug.LogError($"<color=red>[Firebase]</color> 랭킹 조회 실패: {reqFallback.error} | Response: {reqFallback.downloadHandler?.text}");
                }
            }
        }

        List<RankData> ranks = ParseFirebaseRanks(rawJson);

        var board = RankingBoardUI.Instance;
        int rowCount = board != null ? RankingBoardUI.RowCount
                     : (rankRows != null ? rankRows.Length : 0);

        for (int i = 0; i < rowCount; i++)
        {
            bool has = ranks != null && i < ranks.Count;
            string players = "-";
            string time = "--:--.--";
            if (has)
            {
                var data = ranks[i];
                string shardBadge = data.shards > 0 ? $"  <color=#FFD94D>[✦{data.shards}]</color>" : "";
                players = $"{data.player1} & {data.player2}{shardBadge}";
                time = FormatTime(data.clearTime);
            }

            if (board != null)
            {
                if (has) board.SetEntry(i, i + 1, players, time);
                else board.SetEmpty(i, i + 1);
            }

            if (rankRows != null && i < rankRows.Length && rankRows[i] != null)
            {
                if (rankRows[i].rankText != null) rankRows[i].rankText.text = Ordinal(i + 1);
                if (rankRows[i].namesText != null) rankRows[i].namesText.text = has ? players : "---";
                if (rankRows[i].timeText != null) rankRows[i].timeText.text = time;
            }
        }
    }

    /// <summary>
    /// Firebase Realtime Database의 객체 딕셔너리 응답 {"-key1":{...}, "-key2":{...}} 을
    /// List<RankData>로 안전하게 파싱하고 클리어 타임 기준 오름차순으로 정렬합니다.
    /// </summary>
    private List<RankData> ParseFirebaseRanks(string json)
    {
        var list = new List<RankData>();
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "null" || json.Trim() == "{}")
            return list;

        int depth = 0;
        int startIdx = -1;
        bool inQuotes = false;
        bool escape = false;

        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];

            if (escape)
            {
                escape = false;
                continue;
            }

            if (c == '\\')
            {
                escape = true;
                continue;
            }

            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (inQuotes) continue;

            if (c == '{')
            {
                depth++;
                if (depth == 2)
                {
                    startIdx = i;
                }
            }
            else if (c == '}')
            {
                if (depth == 2 && startIdx != -1)
                {
                    string itemJson = json.Substring(startIdx, i - startIdx + 1);
                    try
                    {
                        var item = JsonUtility.FromJson<RankData>(itemJson);
                        if (item != null)
                        {
                            list.Add(item);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[Ranking] 개별 항목 파싱 예외 (무시됨): {ex.Message}");
                    }
                    startIdx = -1;
                }
                depth--;
            }
        }

        // 클라이언트 측에서 클리어 타임 기준 오름차순(빠른 기록 순) 정렬 재보장
        list.Sort((a, b) => a.clearTime.CompareTo(b.clearTime));
        return list;
    }

    // 1 -> "1st", 2 -> "2nd", 3 -> "3rd", 4 -> "4th" ... (11~13은 th)
    string Ordinal(int n)
    {
        int mod100 = n % 100;
        if (mod100 >= 11 && mod100 <= 13) return n + "th";
        switch (n % 10)
        {
            case 1: return n + "st";
            case 2: return n + "nd";
            case 3: return n + "rd";
            default: return n + "th";
        }
    }

    string FormatTime(double time)
    {
        int min = (int)time / 60;
        int sec = (int)time % 60;
        int ms = (int)((time - (int)time) * 100);
        return string.Format("{0:00}:{1:00}.{2:00}", min, sec, ms);
    }
}