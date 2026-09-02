using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public class RankingManager : MonoBehaviour
{
    public static RankingManager Instance;
    private bool _isAutoInstance = false;

    private void Awake()
    {
        // 이 컴포넌트는 씬 UI(UIManager)와 같은 GameObject 에 붙어 있다.
        // 여기서 DontDestroyOnLoad/Destroy(gameObject) 를 하면, 로비로 돌아왔을 때
        // 새로 로드된 UIManager 까지 파괴되어 랭킹보드 버튼이 죽는다.
        // 따라서 항상 "현재 씬의 인스턴스"를 Instance 로 둔다.
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // 씬에 RankingManager 가 없을 때(스테이지에서 바로 Play, 또는 로비 시작 후 스테이지 진입) 저장 요청만
    // 처리할 임시 인스턴스를 만든다. UI(rankRows)는 없고, 저장이 끝나면 스스로 사라진다.
    public static RankingManager GetOrCreate()
    {
        if (Instance == null)
        {
            var go = new GameObject("RankingManager (auto)");
            DontDestroyOnLoad(go); // 씬 전환 중에도 저장 코루틴이 끝나도록 유지
            var rm = go.AddComponent<RankingManager>();
            rm._isAutoInstance = true;
        }
        return Instance;
    }

    public void SaveScore(float time)
    {
        var names = ResolvePlayerNames();
        SaveScore(time, names.Item1, names.Item2);
    }

    // 실제 플레이어 닉네임을 직접 넘길 수 있는 오버로드
    public void SaveScore(float time, string p1, string p2)
    {
        p1 = string.IsNullOrWhiteSpace(p1) ? "Player1" : p1.Trim();
        p2 = string.IsNullOrWhiteSpace(p2) ? "Player2" : p2.Trim();
        Debug.Log($"서버로 {time}초 기록 전송 시도 ({p1} & {p2})");
        StartCoroutine(PostRankingData(p1, p2, time));
    }

    // 씬의 PlayerNameTag에서 닉네임을 수집 (순서는 정렬로 안정화 -> 중복 행 방지에 도움)
    private System.Tuple<string, string> ResolvePlayerNames()
    {
        var tags = FindObjectsOfType<PlayerNameTag>();
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

    IEnumerator PostRankingData(string p1,string p2,float time)
    {
        string url = "http://localhost:8080/api/ranks/save";
        RankData newData = new RankData();
        newData.player1 = p1;
        newData.player2 = p2;
        newData.clearTime = (double)time;

        string json = JsonUtility.ToJson(newData);

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json"); // JSON 형식임을 서버에 알림

            Debug.Log("<color=yellow>[Network]</color> 서버로 기록 전송 시작..."); // 전송 시작 로그
            yield return request.SendWebRequest();

            Debug.Log($"서버 응답 코드: {request.responseCode}");
            Debug.Log($"서버 응답 내용: {request.downloadHandler.text}");
            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("<color=lime>★★ [Success] 서버 저장 완료! ★★</color>");
                Debug.Log("서버 응답 내용: " + request.downloadHandler.text); 
                //RequestRanking();
            }
            else
            {
                Debug.LogError($"<color=red>[Error]</color> 기록 저장 실패: {request.error}");
            }
        }

        // 임시(auto) 인스턴스는 저장이 끝나면 스스로 정리
        if (_isAutoInstance)
        {
            if (Instance == this) Instance = null;
            Destroy(gameObject);
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

    // ---- 랭킹 보드(코드 생성 UI) 제어 ----
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
        string url = "http://localhost:8080/api/ranks/all";
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string json = "{\"items\":" + request.downloadHandler.text + "}";
                RankList ranks = JsonUtility.FromJson<RankList>(json);

                var board = RankingBoardUI.Instance;
                int rowCount = board != null ? RankingBoardUI.RowCount
                             : (rankRows != null ? rankRows.Length : 0);

                for (int i = 0; i < rowCount; i++)
                {
                    bool has = ranks != null && ranks.items != null && i < ranks.items.Count;
                    string players = "-";
                    string time = "--:--.--";
                    if (has)
                    {
                        var data = ranks.items[i];
                        players = $"{data.player1} & {data.player2}";
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
            else
            {
                Debug.LogError($"<color=red>[Ranking]</color> 랭킹 조회 실패: {request.error}");
            }
        }
    }

    // 1 -> "1st", 2 -> "2nd", 3 -> "3rd", 4 -> "4th" ... (11~13위는 th)
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