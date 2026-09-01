using System;
using Fusion;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 클리어(NetworkTimer.IsFinished) 시 엔딩 화면을 띄우고 이벤트를 브로드캐스트한다.
// 씬에 아무것도 배치할 필요 없이 스스로 부트스트랩된다. UI 도 코드로 생성한다.
public class GameEndController : MonoBehaviour
{
    public static GameEndController Instance { get; private set; }

    // 다른 시스템(사운드/연출/업적 등)이 자유롭게 구독. 모든 피어에서 호출됨.
    public static event Action<float> OnGameCleared;

    [Header("옵션")]
    public string lobbySceneName = "Lobby";
    public float autoReturnDelay = 10f; // 0 이하면 자동 복귀 안 함

    private bool _shown;
    private bool _returning;
    private float _shownAtUnscaled;
    private GameObject _root;
    private Text _titleText;
    private Text _timeText;
    private Text _hintText;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("GameEndController");
        go.AddComponent<GameEndController>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 새 씬이 로드되면 엔딩 상태 초기화
        _shown = false;
        _returning = false;
        if (_root != null) _root.SetActive(false);
    }

    private void Update()
    {
        if (_returning) return;

        var nt = NetworkTimer.Instance;

        // 클리어 감지 (모든 피어: 동기화된 IsFinished 플래그 기반)
        if (!_shown && nt != null && nt.IsFinished)
            ShowEnding(nt.FinalTime);

        if (!_shown) return;

        if (_timeText != null && nt != null)
            _timeText.text = "CLEAR TIME   " + FormatTime(nt.IsFinished ? nt.FinalTime : nt.ElapsedTime);

        float elapsed = Time.unscaledTime - _shownAtUnscaled;

        if (_hintText != null)
        {
            if (autoReturnDelay > 0f)
            {
                int left = Mathf.Max(0, Mathf.CeilToInt(autoReturnDelay - elapsed));
                _hintText.text = left + "초 후 로비로 이동    ·    [R] 지금 이동";
            }
            else
            {
                _hintText.text = "[R] 로비로 이동";
            }
        }

        if (Input.GetKeyDown(KeyCode.R)) { ReturnToLobby(); return; }
        if (autoReturnDelay > 0f && elapsed >= autoReturnDelay) ReturnToLobby();
    }

    public void ShowEnding(float clearTime)
    {
        if (_shown) return;
        _shown = true;
        _shownAtUnscaled = Time.unscaledTime;

        EnsureUI();
        _root.SetActive(true);
        if (_titleText != null) _titleText.text = "STAGE CLEAR!";
        if (_timeText != null) _timeText.text = "CLEAR TIME   " + FormatTime(clearTime);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        Debug.Log($"<color=lime>[Ending]</color> STAGE CLEAR  -  {FormatTime(clearTime)}");

        try { OnGameCleared?.Invoke(clearTime); }
        catch (Exception e) { Debug.LogException(e); }
    }

    public async void ReturnToLobby()
    {
        if (_returning) return;
        _returning = true;
        _shown = false;
        if (_root != null) _root.SetActive(false);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 남아있는 러너를 모두 완전히 종료하고 GameObject 까지 정리한다.
        // (셧다운된 러너를 안은 persistent BasicLauncher 가 로비로 따라오면
        //  런처/러너가 중복되고, 다음 스크립트 재컴파일 때 Fusion 이 예외를 던진다.)
        foreach (var runner in FindObjectsOfType<NetworkRunner>())
        {
            if (runner == null) continue;
            await runner.Shutdown();
            if (runner != null) Destroy(runner.gameObject);
        }

        SceneManager.LoadScene(lobbySceneName);
    }

    // ---------- 런타임 UI 생성 (legacy uGUI, 폰트 에셋 불필요) ----------
    private void EnsureUI()
    {
        if (_root != null) return;

        if (FindObjectOfType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(es);
        }

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        _root = new GameObject("EndingCanvas");
        DontDestroyOnLoad(_root);

        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        _root.AddComponent<GraphicRaycaster>();

        // 배경 (반투명 검정)
        var bg = CreateChild("BG", _root.transform);
        var bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.82f);
        Stretch(bg.GetComponent<RectTransform>());

        _titleText = CreateText("Title", _root.transform, font, 96, FontStyle.Bold,
            new Vector2(0.5f, 0.68f), new Vector2(1400, 200), "STAGE CLEAR!");
        _titleText.color = new Color(1f, 0.92f, 0.35f);

        _timeText = CreateText("Time", _root.transform, font, 60, FontStyle.Normal,
            new Vector2(0.5f, 0.5f), new Vector2(1400, 140), "CLEAR TIME   00:00.00");

        _hintText = CreateText("Hint", _root.transform, font, 32, FontStyle.Normal,
            new Vector2(0.5f, 0.34f), new Vector2(1400, 90), "");
        _hintText.color = new Color(1f, 1f, 1f, 0.7f);

        // 로비 이동 버튼
        var btnGo = CreateChild("ReturnButton", _root.transform);
        var btnRect = btnGo.GetComponent<RectTransform>();
        btnRect.anchorMin = btnRect.anchorMax = new Vector2(0.5f, 0.2f);
        btnRect.sizeDelta = new Vector2(360, 96);
        btnRect.anchoredPosition = Vector2.zero;
        var btnImg = btnGo.AddComponent<Image>();
        btnImg.color = new Color(0.2f, 0.55f, 1f, 0.95f);
        var btn = btnGo.AddComponent<Button>();
        btn.onClick.AddListener(ReturnToLobby);
        var btnLabel = CreateText("Label", btnGo.transform, font, 34, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), new Vector2(360, 96), "로비로 나가기");
        Stretch(btnLabel.GetComponent<RectTransform>());

        _root.SetActive(false);
    }

    private static GameObject CreateChild(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static Text CreateText(string name, Transform parent, Font font, int size,
        FontStyle style, Vector2 anchor, Vector2 size2, string content)
    {
        var go = CreateChild(name, parent);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.sizeDelta = size2;
        rt.anchoredPosition = Vector2.zero;
        var t = go.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.color = Color.white;
        t.text = content;
        return t;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    public static string FormatTime(float t)
    {
        if (t < 0f) t = 0f;
        int m = Mathf.FloorToInt(t / 60f);
        int s = Mathf.FloorToInt(t % 60f);
        int cs = Mathf.FloorToInt((t * 100f) % 100f);
        return string.Format("{0:00}:{1:00}.{2:00}", m, s, cs);
    }
}
