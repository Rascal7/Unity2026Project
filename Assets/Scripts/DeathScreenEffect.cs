using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 플레이어 사망 시 화면을 회색(Grayscale)으로 전환하고 비네트 효과를 주는 연출 컨트롤러.
/// 씬에 직접 배치하지 않아도 게임 시작 시 자동으로 부트스트랩되며 씬 전환 시에도 유지된다.
/// URP ColorAdjustments (채도 -100)와 풀스크린 캔버스 그레이 오버레이를 동시에 적용하여
/// 어떤 환경에서도 안정적이고 자연스러운 회색 화면 연출을 제공한다.
/// </summary>
public class DeathScreenEffect : MonoBehaviour
{
    public static DeathScreenEffect Instance { get; private set; }

    private Volume _volume;
    private ColorAdjustments _colorAdjustments;

    private CanvasGroup _canvasGroup;
    private Image _grayOverlay;
    private Image _vignetteOverlay;

    private float _intensity = 0f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("DeathScreenEffect");
        go.AddComponent<DeathScreenEffect>();
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

        BuildVolume();
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
        ResetEffect();
        // 새 씬은 기본적으로 포스트프로세싱을 꺼 둔다(픽셀아트가 블릿으로 흐려지는 것 방지).
        // 죽음 연출이 필요할 때만 Update 에서 켠다.
        SetCameraPostProcessing(false);
    }

    private void BuildVolume()
    {
        var volGo = new GameObject("DeathPostProcessVolume");
        volGo.transform.SetParent(transform, false);
        volGo.layer = 0; // Default layer

        _volume = volGo.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = 1000f; // 씬 기본 볼륨보다 높은 우선순위

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "DeathVolumeProfile";

        if (!profile.TryGet<ColorAdjustments>(out _colorAdjustments))
        {
            _colorAdjustments = profile.Add<ColorAdjustments>(true);
        }

        _colorAdjustments.saturation.overrideState = true;
        _colorAdjustments.saturation.value = 0f;

        _colorAdjustments.contrast.overrideState = true;
        _colorAdjustments.contrast.value = 0f;

        _volume.profile = profile;
    }

    private void BuildUI()
    {
        var canvasGo = new GameObject("DeathEffectCanvas");
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32650; // ScreenFader(32700) 바로 아래, 게임 UI 위
        canvasGo.AddComponent<GraphicRaycaster>();

        _canvasGroup = canvasGo.AddComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;
        _canvasGroup.blocksRaycasts = false;
        _canvasGroup.interactable = false;

        // 1. 전체 화면 다크 그레이 틴트 이미지
        var tintGo = new GameObject("GrayTint", typeof(RectTransform));
        tintGo.transform.SetParent(canvasGo.transform, false);
        var tintRect = tintGo.GetComponent<RectTransform>();
        tintRect.anchorMin = Vector2.zero;
        tintRect.anchorMax = Vector2.one;
        tintRect.sizeDelta = Vector2.zero;

        _grayOverlay = tintGo.AddComponent<Image>();
        _grayOverlay.color = new Color(0.2f, 0.2f, 0.2f, 0.45f);
        _grayOverlay.raycastTarget = false;

        // 2. 가장자리 비네트 이미지 (부드러운 테두리 어두움 연출)
        var vigGo = new GameObject("GrayVignette", typeof(RectTransform));
        vigGo.transform.SetParent(canvasGo.transform, false);
        var vigRect = vigGo.GetComponent<RectTransform>();
        vigRect.anchorMin = Vector2.zero;
        vigRect.anchorMax = Vector2.one;
        vigRect.sizeDelta = Vector2.zero;

        _vignetteOverlay = vigGo.AddComponent<Image>();
        _vignetteOverlay.sprite = CreateVignetteSprite();
        _vignetteOverlay.color = new Color(0.08f, 0.08f, 0.08f, 0.7f);
        _vignetteOverlay.raycastTarget = false;
    }

    private Sprite CreateVignetteSprite()
    {
        int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Vector2 center = new Vector2(0.5f, 0.5f);
        for (int y = 0; y < size; y++)
        {
            float v = (float)y / (size - 1);
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / (size - 1);
                float dist = Vector2.Distance(new Vector2(u, v), center) * 1.4142f; // 중심 0, 모서리 약 1
                float a = Mathf.SmoothStep(0.2f, 0.95f, dist);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    // 포스트프로세싱(채도 감소 그레이스케일)은 죽음 연출이 실제로 보일 때만 켠다.
    // 항상 켜두면 URP 가 중간 타깃으로 블릿하면서 픽셀아트가 전체적으로 흐려진다.
    private void SetCameraPostProcessing(bool on)
    {
        Camera cam = Camera.main;
        if (cam == null) cam = FindFirstObjectByType<Camera>();
        if (cam == null) return;
        var addData = cam.GetUniversalAdditionalCameraData();
        if (addData != null && addData.renderPostProcessing != on)
            addData.renderPostProcessing = on;
    }

    private void Update()
    {
        bool isDead = PlayerMovement.IsAnyLocalPlayerDead();
        float target = isDead ? 1f : 0f;

        // 사망 시 빠르게 회색으로 전환 (약 0.08~0.1초), 리스폰 시 부드럽게 복구 (약 0.3초)
        float speed = isDead ? 12f : 3.5f;
        _intensity = Mathf.MoveTowards(_intensity, target, Time.unscaledDeltaTime * speed);

        // 연출이 실제로 보일 때(강도 > 0)만 포스트프로세싱을 켜 둔다. 평상시엔 꺼서 화면을 또렷하게.
        SetCameraPostProcessing(_intensity > 0.001f);

        // URP Post-Processing ColorAdjustments 채도/대비 제어
        if (_colorAdjustments != null)
        {
            _colorAdjustments.saturation.value = Mathf.Lerp(0f, -100f, _intensity);
            _colorAdjustments.contrast.value = Mathf.Lerp(0f, 15f, _intensity);
        }

        // Overlay Canvas 투명도 제어
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = _intensity;
        }
    }

    public static void ResetEffect()
    {
        if (Instance != null)
        {
            Instance._intensity = 0f;
            if (Instance._canvasGroup != null) Instance._canvasGroup.alpha = 0f;
            if (Instance._colorAdjustments != null)
            {
                Instance._colorAdjustments.saturation.value = 0f;
                Instance._colorAdjustments.contrast.value = 0f;
            }
        }
    }
}
