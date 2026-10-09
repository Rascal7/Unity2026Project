using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 씬 전환용 검은 화면 페이드. 씬에 배치할 필요 없이 스스로 부트스트랩되고 씬이 바뀌어도 유지된다.
//  - FadeOut / FadeIn / SetBlack 으로 직접 제어한다.
//  - 검은 화면인 채로 일반 씬이 로드되면 자동으로 페이드 인한다.
//    컷씬 씬(CutsceneDirector 가 있는 씬)은 영상이 시작될 때 CutsceneDirector 가 직접 페이드 인한다.
public class ScreenFader : MonoBehaviour
{
    public static ScreenFader Instance { get; private set; }

    public const float DefaultDuration = 0.5f;
    private const float AutoFadeInDelay = 0.3f; // 씬 로드 직후 캐릭터 스폰 등이 끝날 시간을 잠깐 준다

    private CanvasGroup _group;
    private Coroutine _co;

    public bool IsBlack => _group != null && _group.alpha >= 0.99f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("ScreenFader");
        go.AddComponent<ScreenFader>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildUI();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!IsBlack) return;
        if (FindFirstObjectByType<CutsceneDirector>() != null) return; // 컷씬은 재생 시작 때 직접 페이드 인
        FadeIn(DefaultDuration, AutoFadeInDelay);
    }

    public void FadeOut(float duration = DefaultDuration) => Run(1f, duration, 0f);
    public void FadeIn(float duration = DefaultDuration, float delay = 0f) => Run(0f, duration, delay);

    public void SetBlack()
    {
        if (_co != null) { StopCoroutine(_co); _co = null; }
        SetAlpha(1f);
    }

    private void Run(float target, float duration, float delay)
    {
        if (_co != null) StopCoroutine(_co);
        _co = StartCoroutine(FadeRoutine(target, duration, delay));
    }

    private IEnumerator FadeRoutine(float target, float duration, float delay)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
        float from = _group.alpha;
        float t0 = Time.unscaledTime;
        while (duration > 0f)
        {
            float k = Mathf.Clamp01((Time.unscaledTime - t0) / duration);
            SetAlpha(Mathf.Lerp(from, target, k));
            if (k >= 1f) break;
            yield return null;
        }
        SetAlpha(target);
        _co = null;
    }

    private void SetAlpha(float a)
    {
        _group.alpha = a;
        _group.blocksRaycasts = a > 0.01f; // 어두운 동안 뒤 UI 클릭 방지
    }

    private void BuildUI()
    {
        var canvasGo = new GameObject("ScreenFaderCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32700; // 결과 화면(EndingCanvas 32000)과 컷씬 UI 보다 위
        canvasGo.AddComponent<GraphicRaycaster>();
        _group = canvasGo.AddComponent<CanvasGroup>();

        var img = new GameObject("Black", typeof(RectTransform)).AddComponent<Image>();
        img.transform.SetParent(canvasGo.transform, false);
        img.color = Color.black;
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        SetAlpha(0f);
    }
}
