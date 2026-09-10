using UnityEngine;
using TMPro; // TextMeshPro ����� ���� �ʼ�

public class TimerManager : MonoBehaviour
{
    public static TimerManager Instance;

    [Header("Status")]
    public float elapsedTime = 0f;    // ���� �ð� (�� ����)
    public bool isTimerRunning = false;

    private TextMeshProUGUI timerText; // ȭ�鿡 �ð��� ǥ���� UI

    private void Awake()
    {
        // �̱��� ����: ���� �ٲ� �ϳ��� �����ϰ� ����
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
        // ��Ʈ��ũ Ÿ�̸Ӱ� ������ �� ���� ǥ�� (�� �Ǿ� ���ϰ�)
        var nt = NetworkTimer.Instance;
        if (nt != null)
        {
            // Cumulative display: previous stages (RunManager.AccumulatedTime) + this stage's elapsed.
            // - Finished: FinalTime already is the full total (AccumulatedTime was reset right after).
            // - AwaitingChoice: the just-finished segment is already folded into AccumulatedTime, so don't add ElapsedTime.
            // - Intro/Running: show accumulated + current elapsed (intro holds elapsed at 0).
            float shown;
            if (nt.IsFinished) shown = nt.FinalTime;
            else if (nt.Phase == NetworkTimer.PhaseAwaitingChoice) shown = RunManager.AccumulatedTime;
            else shown = RunManager.AccumulatedTime + nt.ElapsedTime;
            SetDisplayTime(shown);
            return;
        }

        // ����: ��Ʈ��ũ Ÿ�̸Ӱ� ���� ��(������ �ܵ� ���� ��) ���� ����
        if (isTimerRunning)
        {
            elapsedTime += Time.deltaTime;
            SetDisplayTime(elapsedTime);
        }
    }

    // �� ���� �ð��� 00:00.00 �������� ȭ�鿡 ǥ��
    public void SetDisplayTime(float t)
    {
        if (timerText == null) return;
        if (t < 0f) t = 0f;
        int minutes = Mathf.FloorToInt(t / 60f);
        int seconds = Mathf.FloorToInt(t % 60f);
        int centiseconds = Mathf.FloorToInt((t * 100f) % 100f);
        timerText.text = string.Format("{0:00}:{1:00}.{2:00}", minutes, seconds, centiseconds);
    }

    // ���ο� ���� �ε�� ������ �ش� ���� UI�� ã�� �����ϴ� �Լ�
    public void FindTimerUI()
    {
        GameObject uiObj = GameObject.Find("TimerText"); // �� �� "TimerText"��� �̸��� ������Ʈ �˻�
        if (uiObj != null)
        {
            timerText = uiObj.GetComponent<TextMeshProUGUI>();
        }
    }

    private void UpdateTimerUI()
    {
        if (timerText != null)
        {
            // 00:00.00 ���� (��:��.�и���)
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
        UpdateTimerUI(); // ���� ������ elapsedTime�� ȭ�鿡 �״�� �ݿ�(���尪�� ǥ�ð� ��ġ)
    }

    public void ResetTimer()
    {
        elapsedTime = 0f;
        UpdateTimerUI(); // 00:00.00 ���� �ʱ�ȭ
    }
}
