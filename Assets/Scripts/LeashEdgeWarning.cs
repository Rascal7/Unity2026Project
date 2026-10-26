using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 공유 카메라 거리 제한 경고: 내 캐릭터가 상대와 너무 멀어져 더 못 가는 동안,
/// 막힌 방향의 화면 가장자리에 붉은 테두리를 깜빡여 보여준다. (PlayerMovement.IsLeashBlocked)
/// 씬에 직접 배치하지 않아도 게임 시작 시 자동으로 부트스트랩되며 씬 전환 시에도 유지된다.
/// </summary>
public class LeashEdgeWarning : MonoBehaviour
{
    public static LeashEdgeWarning Instance { get; private set; }

    [Tooltip("마지막으로 막힌 뒤 이 시간(초) 동안은 계속 막힌 것으로 본다(틱 사이 깜빡임 방지)")]
    public float holdSeconds = 0.12f;
    [Tooltip("테두리 두께 (화면 대비 비율)")]
    public float thickness = 0.1f;
    [Tooltip("테두리 최대 불투명도")]
    public float maxAlpha = 0.85f;
    public Color color = new Color(0.95f, 0.05f, 0.05f, 1f);
    [Tooltip("나타나는 속도 / 사라지는 속도")]
    public float fadeIn = 14f, fadeOut = 5f;
    [Tooltip("깜빡임 속도(초당 라디안)")]
    public float pulseSpeed = 9f;

    // 0: 왼쪽, 1: 오른쪽, 2: 아래, 3: 위
    private readonly Image[] _edges = new Image[4];
    private readonly float[] _alpha = new float[4];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("LeashEdgeWarning");
        go.AddComponent<LeashEdgeWarning>();
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
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void BuildUI()
    {
        var canvasGo = new GameObject("LeashEdgeCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 480; // 인게임 위, 파트너 표시(490)/스킬 HUD(500) 아래

        Sprite hGrad = CreateGradientSprite(true);   // 왼쪽 끝이 진하다
        Sprite vGrad = CreateGradientSprite(false);  // 아래쪽 끝이 진하다
        _edges[0] = CreateEdge(canvasGo.transform, "Left", hGrad, new Vector2(0f, 0f), new Vector2(thickness, 1f), Vector3.one);
        _edges[1] = CreateEdge(canvasGo.transform, "Right", hGrad, new Vector2(1f - thickness, 0f), new Vector2(1f, 1f), new Vector3(-1f, 1f, 1f));
        _edges[2] = CreateEdge(canvasGo.transform, "Bottom", vGrad, new Vector2(0f, 0f), new Vector2(1f, thickness), Vector3.one);
        _edges[3] = CreateEdge(canvasGo.transform, "Top", vGrad, new Vector2(0f, 1f - thickness), new Vector2(1f, 1f), new Vector3(1f, -1f, 1f));
    }

    private Image CreateEdge(Transform parent, string name, Sprite sprite, Vector2 anchorMin, Vector2 anchorMax, Vector3 scale)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.sizeDelta = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localScale = scale; // 같은 그라디언트를 뒤집어 반대편 가장자리에 쓴다

        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        img.color = new Color(color.r, color.g, color.b, 0f);
        img.enabled = false;
        return img;
    }

    private static Sprite CreateGradientSprite(bool horizontal)
    {
        const int N = 64;
        var tex = new Texture2D(horizontal ? N : 1, horizontal ? 1 : N, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        for (int i = 0; i < N; i++)
        {
            float t = (float)i / (N - 1);               // 0: 가장자리, 1: 안쪽
            // 가장자리 띠는 꽉 채우고(밝은 배경에서도 보이게) 그 안쪽으로 부드럽게 사라짐
            float f = Mathf.Clamp01((t - 0.12f) / 0.88f);
            float a = 1f - f * f * (3f - 2f * f);
            var c = new Color(1f, 1f, 1f, a);
            if (horizontal) tex.SetPixel(i, 0, c); else tex.SetPixel(0, i, c);
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
    }

    private void Update()
    {
        Vector2 dir = Vector2.zero;
        var me = PlayerMovement.Local;
        bool blocked = me != null && me.IsLeashBlocked(holdSeconds, out dir);

        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * pulseSpeed);
        float dt = Time.unscaledDeltaTime;
        for (int i = 0; i < 4; i++)
        {
            bool on = blocked && (i == 0 ? dir.x < 0f : i == 1 ? dir.x > 0f : i == 2 ? dir.y < 0f : dir.y > 0f);
            float rate = on ? fadeIn : fadeOut;
            _alpha[i] = Mathf.Lerp(_alpha[i], on ? 1f : 0f, 1f - Mathf.Exp(-rate * dt));

            var img = _edges[i];
            if (img == null) continue;
            bool visible = _alpha[i] > 0.01f;
            if (img.enabled != visible) img.enabled = visible;
            if (visible) img.color = new Color(color.r, color.g, color.b, _alpha[i] * maxAlpha * pulse);
        }
    }
}
