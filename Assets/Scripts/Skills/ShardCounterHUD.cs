using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 화면 좌측 상단에 이 스테이지의 구슬 조각(CoinItem) 수집 현황 "n / m" 을 표시하는 HUD.
/// - 런타임에 자동 생성된다(SkillCooldownHUD 와 같은 방식). 조각이 없는 씬에서는 숨긴다.
/// - m: 씬이 로드된 순간의 조각 수 (Fusion 이 이미 먹힌 조각을 지우기 전에 센다 → 중간 참가자도 같은 m).
/// - n: m - 아직 남아 있는 조각 수. 조각은 먹히면 Despawn 되므로 모든 피어에서 같은 숫자가 된다.
/// - 아이콘은 씬의 조각 스프라이트를 그대로 가져다 쓴다(코인 그림을 바꾸면 HUD 도 따라 바뀜).
/// </summary>
public class ShardCounterHUD : MonoBehaviour
{
    public static ShardCounterHUD Instance { get; private set; }

    private const float RefreshInterval = 0.2f;

    private Canvas _canvas;
    private RectTransform _panel;
    private Image _icon;
    private Text _countText;
    private Text _bonusText;

    private int _total;
    private int _collected = -1;
    private float _nextRefresh;
    private float _popT = 1f; // 0→1, 획득 순간 살짝 커졌다 돌아오는 연출

    public int CollectedCount => Mathf.Max(0, _collected);
    public int TotalCount => _total;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        new GameObject("ShardCounterHUD").AddComponent<ShardCounterHUD>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildUI();
        SceneManager.sceneLoaded += OnSceneLoaded;
        ResetForScene();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ResetForScene();

    private void ResetForScene()
    {
        var shards = FindShards();
        CoinItem.ActiveShards.Clear();
        foreach (var s in shards)
        {
            if (s != null && !CoinItem.ActiveShards.Contains(s))
                CoinItem.ActiveShards.Add(s);
        }

        _total = CoinItem.ActiveShards.Count;
        _collected = -1;
        _nextRefresh = 0f;
        RunManager.ResetCurrentStageShards(_total);

        foreach (var s in shards)
        {
            var sr = s.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null) { _icon.sprite = sr.sprite; break; }
        }

        _canvas.enabled = _total > 0;
    }

    private static CoinItem[] FindShards() =>
        FindObjectsByType<CoinItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);

    private static int CountRemainingShards()
    {
        var list = CoinItem.ActiveShards;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] == null) list.RemoveAt(i);
        }
        return list.Count;
    }

    private void Update()
    {
        if (_total <= 0) return;

        if (Time.unscaledTime >= _nextRefresh)
        {
            _nextRefresh = Time.unscaledTime + RefreshInterval;
            int remaining = CountRemainingShards();
            if (remaining > _total) _total = remaining; // 씬 로드 시점에 덜 잡혔던 경우 보정
            int collected = _total - remaining;
            if (collected != _collected)
            {
                if (_collected >= 0 && collected > _collected) _popT = 0f;
                _collected = collected;
                RunManager.SyncStageShards(collected, _total);
                _countText.text = $"{collected} / {_total}";
                _countText.color = collected >= _total ? LobbyUITheme.GreenBtn : LobbyUITheme.Gold;
                if (_bonusText != null)
                {
                    float bonusSec = collected * RunManager.ShardTimeBonusSeconds;
                    _bonusText.text = collected > 0 ? $"기록 -{bonusSec:0}초 단축!" : $"조각당 -{RunManager.ShardTimeBonusSeconds:0}초";
                    _bonusText.color = collected > 0 ? new Color(0.55f, 1f, 0.68f, 1f) : LobbyUITheme.Hint;
                }
            }
        }

        if (_popT < 1f)
        {
            _popT = Mathf.Min(1f, _popT + Time.unscaledDeltaTime / 0.3f);
            _panel.localScale = Vector3.one * (1f + 0.2f * Mathf.Sin(_popT * Mathf.PI));
        }
    }

    private void BuildUI()
    {
        var canvasGo = new GameObject("ShardHUD_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 500; // SkillCooldownHUD 와 같은 층

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // 좌측 상단 우드 패널
        var panelGo = new GameObject("ShardPanel", typeof(RectTransform), typeof(Image));
        panelGo.transform.SetParent(canvasGo.transform, false);
        _panel = (RectTransform)panelGo.transform;
        _panel.anchorMin = _panel.anchorMax = new Vector2(0f, 1f);
        _panel.pivot = new Vector2(0.5f, 0.5f);
        _panel.sizeDelta = new Vector2(246f, 86f);
        _panel.anchoredPosition = new Vector2(40f + 123f, -35f - 43f);
        LobbyUITheme.StyleWoodPanel(panelGo);

        var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGo.transform.SetParent(panelGo.transform, false);
        var irt = (RectTransform)iconGo.transform;
        irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f);
        irt.pivot = new Vector2(0f, 0.5f);
        irt.anchoredPosition = new Vector2(16f, 0f);
        irt.sizeDelta = new Vector2(52f, 52f);
        _icon = iconGo.GetComponent<Image>();
        _icon.preserveAspect = true;
        _icon.raycastTarget = false;

        var textGo = new GameObject("Count", typeof(RectTransform), typeof(Text), typeof(Outline));
        textGo.transform.SetParent(panelGo.transform, false);
        var trt = (RectTransform)textGo.transform;
        trt.anchorMin = new Vector2(0f, 0.32f);
        trt.anchorMax = new Vector2(1f, 1f);
        trt.offsetMin = new Vector2(76f, 0f);
        trt.offsetMax = new Vector2(-12f, -4f);
        _countText = textGo.GetComponent<Text>();
        _countText.font = LobbyUITheme.PixelFont;
        _countText.fontSize = 34;
        _countText.alignment = TextAnchor.MiddleCenter;
        _countText.horizontalOverflow = HorizontalWrapMode.Overflow;
        _countText.raycastTarget = false;
        _countText.color = LobbyUITheme.Gold;
        var outline = textGo.GetComponent<Outline>();
        outline.effectColor = LobbyUITheme.GoldDeep;
        outline.effectDistance = new Vector2(2f, -2f);

        var bonusGo = new GameObject("BonusText", typeof(RectTransform), typeof(Text), typeof(Outline));
        bonusGo.transform.SetParent(panelGo.transform, false);
        var brt = (RectTransform)bonusGo.transform;
        brt.anchorMin = new Vector2(0f, 0f);
        brt.anchorMax = new Vector2(1f, 0.38f);
        brt.offsetMin = new Vector2(76f, 4f);
        brt.offsetMax = new Vector2(-12f, 0f);
        _bonusText = bonusGo.GetComponent<Text>();
        _bonusText.font = LobbyUITheme.PlainFont;
        _bonusText.fontSize = 15;
        _bonusText.fontStyle = FontStyle.Bold;
        _bonusText.alignment = TextAnchor.MiddleCenter;
        _bonusText.horizontalOverflow = HorizontalWrapMode.Overflow;
        _bonusText.raycastTarget = false;
        _bonusText.color = LobbyUITheme.Hint;
        var bOutline = bonusGo.GetComponent<Outline>();
        bOutline.effectColor = LobbyUITheme.GoldDeep;
        bOutline.effectDistance = new Vector2(1.5f, -1.5f);
    }
}

