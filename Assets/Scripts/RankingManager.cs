using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public class RankingManager : MonoBehaviour
{
    public static RankingManager Instance;
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
            Destroy(gameObject);
    }

    public void SaveScore(float time)
    {
        Debug.Log($"서버로 {time}초 기록 전송 시도");
        StartCoroutine(PostRankingData("User1","User2",time));
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

                for (int i = 0; i < rankRows.Length; i++)
                {
                    if (i < ranks.items.Count)
                    {
                        var data = ranks.items[i];
                        rankRows[i].rankText.text = $"{i + 1}st";
                        rankRows[i].namesText.text = $"{data.player1} & {data.player2}";
                        rankRows[i].timeText.text = FormatTime(data.clearTime);
                    }
                    else
                    {
                        rankRows[i].rankText.text = $"{i + 1}st";
                        rankRows[i].namesText.text = "---";
                        rankRows[i].timeText.text = "--:--.--";
                    }
                }
            }
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