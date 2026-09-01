using UnityEngine;
using TMPro; // TextMeshPro 사용을 위해 필수

public class TimerManager : MonoBehaviour
{
    public static TimerManager Instance;

    [Header("Status")]
    public float elapsedTime = 0f;    // 누적 시간 (초 단위)
    public bool isTimerRunning = false;

    private TextMeshProUGUI timerText; // 화면에 시간을 표시할 UI

    private void Awake()
    {
        // 싱글톤 패턴: 씬이 바뀌어도 하나만 존재하게 유지
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        // 네트워크 타이머가 있으면 그 값을 표시 (전 피어 동일값)
        var nt = NetworkTimer.Instance;
        if (nt != null)
        {
            SetDisplayTime(nt.IsFinished ? nt.FinalTime : nt.ElapsedTime);
            return;
        }

        // 폴백: 네트워크 타이머가 없을 때(에디터 단독 실행 등) 로컬 누적
        if (isTimerRunning)
        {
            elapsedTime += Time.deltaTime;
            SetDisplayTime(elapsedTime);
        }
    }

    // 초 단위 시간을 00:00.00 형식으로 화면에 표시
    public void SetDisplayTime(float t)
    {
        if (timerText == null) return;
        if (t < 0f) t = 0f;
        int minutes = Mathf.FloorToInt(t / 60f);
        int seconds = Mathf.FloorToInt(t % 60f);
        int centiseconds = Mathf.FloorToInt((t * 100f) % 100f);
        timerText.text = string.Format("{0:00}:{1:00}.{2:00}", minutes, seconds, centiseconds);
    }

    // 새로운 맵이 로드될 때마다 해당 맵의 UI를 찾아 연결하는 함수
    public void FindTimerUI()
    {
        GameObject uiObj = GameObject.Find("TimerText"); // 씬 내 "TimerText"라는 이름의 오브젝트 검색
        if (uiObj != null)
        {
            timerText = uiObj.GetComponent<TextMeshProUGUI>();
        }
    }

    private void UpdateTimerUI()
    {
        if (timerText != null)
        {
            // 00:00.00 형식 (분:초.밀리초)
            int minutes = Mathf.FloorToInt(elapsedTime / 60);
            int seconds = Mathf.FloorToInt(elapsedTime % 60);
            float milliseconds = (elapsedTime % 1) * 100;
            timerText.text = string.Format("{0:00}:{1:00}.{2:00}", minutes, seconds, milliseconds);
        }
    }

    public void StartTimer() { isTimerRunning = true; }

    public void StopTimer()
    {
        isTimerRunning = false;
        UpdateTimerUI(); // 정지 시점의 elapsedTime을 화면에 그대로 반영(저장값과 표시값 일치)
    }

    public void ResetTimer()
    {
        elapsedTime = 0f;
        UpdateTimerUI(); // 00:00.00 으로 초기화
    }
}
