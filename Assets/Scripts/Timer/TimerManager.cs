using UnityEngine;
using TMPro; // TextMeshPro 사용을 위해 필수

public class TimerManager : MonoBehaviour
{
    public static TimerManager Instance;

    [Header("Status")]
    public float elapsedTime = 0f;    // 경과 시간 (초 단위)
    public bool isTimerRunning = false;

    private TextMeshProUGUI timerText; // 화면에 시간을 표시할 UI

    private void Awake()
    {
        // 싱글톤 패턴: 씬이 바뀌어도 하나만 유지하게 설정
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
        // 네트워크 타이머가 있으면 그 값을 표시 (양 피어 동일값)
        var nt = NetworkTimer.Instance;
        if (nt != null)
        {
            float shown;
            if (nt.IsFinished) shown = nt.FinalTime;
            else if (nt.Phase == NetworkTimer.PhaseAwaitingChoice) shown = nt.FinalTime;  // 이 스테이지 확정 기록
            else shown = nt.ElapsedTime;                                                  // 진행 중: 이 스테이지 0부터
            SetDisplayTime(shown);
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
        timerText.text = RunManager.FormatTime(t);
    }

    // 새로운 씬이 로드될 때마다 해당 씬의 UI를 찾아 연결하는 함수
    public void FindTimerUI()
    {
        GameObject uiObj = GameObject.Find("TimerText"); // 씬 안 "TimerText" 이름의 오브젝트 검색
        if (uiObj != null)
        {
            timerText = uiObj.GetComponent<TextMeshProUGUI>();
        }
    }

    private void UpdateTimerUI()
    {
        if (timerText != null)
        {
            timerText.text = RunManager.FormatTime(elapsedTime);
        }
    }

    public void StartTimer() { isTimerRunning = true; }

    public void StopTimer()
    {
        isTimerRunning = false;
        UpdateTimerUI(); // 정지 시점의 elapsedTime을 화면에 그대로 반영(필드값과 표시값 일치)
    }

    public void ResetTimer()
    {
        elapsedTime = 0f;
        UpdateTimerUI(); // 00:00.00 으로 초기화
    }
}
