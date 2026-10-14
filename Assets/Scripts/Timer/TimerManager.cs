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
            else if (nt.Phase == NetworkTimer.PhaseAwaitingChoice) shown = nt.FinalTime;  // 이 스테이지 확정 기록
            else shown = nt.ElapsedTime;                                                  // 진행 중: 이 스테이지 0부터
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

    // 새로운 씬이 로드될 때마다 해당 씬의 UI를 찾아 연결하는 함수
    public void FindTimerUI()
    {
        GameObject uiObj = GameObject.Find("TimerText"); // 씬 안 "TimerText" 이름의 오브젝트 검색
        if (uiObj != null)
        {
            var found = uiObj.GetComponent<TextMeshProUGUI>();
            if (found != null) { timerText = found; return; }
        }

        // 씬에 TimerText가 없는 경우(일부 스테이지 씬에서 UI 오브젝트 누락)에도 타이머가
        // 사라지지 않도록 런타임에 동일 스타일로 생성한다. 실제 타이머가 동작할 때만 만든다.
        if (timerText == null && (NetworkTimer.Instance != null || isTimerRunning))
        {
            timerText = CreateFallbackTimerText();
        }
    }

    // 표준 타이머와 동일한 스타일(우상단, NotoSansKR-Bold, 60pt)의 TMP 텍스트를 런타임 생성.
    private TextMeshProUGUI CreateFallbackTimerText()
    {
        // 화면 오버레이 캔버스를 찾거나 없으면 새로 만든다.
        Canvas canvas = null;
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (c.renderMode == RenderMode.ScreenSpaceOverlay && c.isActiveAndEnabled) { canvas = c; break; }
        }
        if (canvas == null)
        {
            var canvasObj = new GameObject("TimerCanvas");
            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        }

        var go = new GameObject("TimerText");
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(canvas.transform, false);
        rt.anchorMin = new Vector2(1, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(1, 1);
        rt.anchoredPosition = new Vector2(-40, -40);
        rt.sizeDelta = new Vector2(500, 120);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = TMPro.TMP_Settings.defaultFontAsset; // NotoSansKR-Bold SDF (표준 타이머와 동일)
        tmp.fontSize = 60;
        tmp.color = Color.white;
        tmp.alignment = TMPro.TextAlignmentOptions.TopRight;
        tmp.raycastTarget = false;
        tmp.text = "00:00.00";
        return tmp;
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
