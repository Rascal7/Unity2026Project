using UnityEngine;
using Fusion;

public class CameraFollow : MonoBehaviour
{
    public Transform target;       // 따라갈 대상
    public Vector3 offset = new Vector3(0, 0, -10); // z는 2D 깊이

    [Header("Smoothing")]
    [Tooltip("가로 추적 부드러움")]
    public float smoothSpeed = 0.15f;
    [Tooltip("세로(데드존 밖) 추적 부드러움")]
    public float ySmoothSpeed = 0.15f;

    [Header("Framing (세로 데드존 - 카메라 창)")]
    [Tooltip("평상시 캐릭터가 화면 중앙보다 이만큼 아래에 위치")]
    public float verticalOffset = 2f;
    [Tooltip("캐릭터가 위로 이만큼 올라가도 카메라 고정(점프/도약 시 바닥 유지). 일반 점프 높이보다 크게")]
    public float lookUp = 3.5f;
    [Tooltip("캐릭터가 아래로 이만큼 내려가면 카메라가 따라감(웅덩이). 작을수록 착지 후 평상시 프레이밍에 가까움")]
    public float lookDown = 0.5f;

    [Header("Bounds")]
    public bool autoClampToBackground = true;
    public bool useManualBounds = false;
    public float minY = -100f, maxY = 100f;
    [Tooltip("급이동 중에도 캐릭터가 화면 밖으로 나가지 않게 하는 여백")]
    public float onScreenMargin = 0.6f;

    private Camera _cam;
    private bool _hasClamp;
    private float _minCamY, _maxCamY;

    private void Awake() { _cam = GetComponent<Camera>(); ComputeBounds(); }
    private void Start() { if (!_hasClamp) ComputeBounds(); }

    private void ComputeBounds()
    {
        float size = (_cam != null && _cam.orthographic) ? _cam.orthographicSize : 5f;
        if (useManualBounds) { _minCamY = minY + size; _maxCamY = maxY - size; _hasClamp = true; return; }
        if (!autoClampToBackground) { _hasClamp = false; return; }

        var bgRoot = GameObject.Find("Background");
        if (bgRoot == null) { _hasClamp = false; return; }
        var renderers = bgRoot.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) { _hasClamp = false; return; }

        float top = float.MinValue, bottom = float.MaxValue;
        foreach (var r in renderers)
        {
            if (r.bounds.max.y > top) top = r.bounds.max.y;
            if (r.bounds.min.y < bottom) bottom = r.bounds.min.y;
        }
        _maxCamY = top - size;
        _minCamY = bottom + size;
        if (_maxCamY < _minCamY) _maxCamY = _minCamY;
        _hasClamp = true;
    }

    private void LateUpdate()
    {
        if (target == null) return;

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);

        // --- 세로: 데드존 "카메라 창" ---
        // 밴드 안이면 카메라 Y 고정(홀드) → 평지/점프에서 완전히 부드럽고 흔들림 면역
        // 밴드 밖(도약 정점/웅덩이)에서만 따라 움직임
        float restS = -verticalOffset;
        float topEdge = restS + lookUp;
        float botEdge = restS - lookDown;
        float s = target.position.y - transform.position.y; // 캐릭터의 화면중앙 대비 높이(+위)
        float desiredY;
        if (s > topEdge) desiredY = target.position.y - topEdge;
        else if (s < botEdge) desiredY = target.position.y - botEdge;
        else desiredY = transform.position.y; // 홀드
        if (_hasClamp) desiredY = Mathf.Clamp(desiredY, _minCamY, _maxCamY);

        // 프레임률 독립 스무딩
        float kx = 1f - Mathf.Pow(1f - Mathf.Clamp01(smoothSpeed), dt * 60f);
        float ky = 1f - Mathf.Pow(1f - Mathf.Clamp01(ySmoothSpeed), dt * 60f);
        float nx = Mathf.Lerp(transform.position.x, target.position.x + offset.x, kx);
        float ny = Mathf.Lerp(transform.position.y, desiredY, ky);

        // 급이동 중에도 캐릭터가 화면 밖으로 안 나가게 (평지에선 비활성)
        float size = (_cam != null && _cam.orthographic) ? _cam.orthographicSize : 5f;
        float maxOff = size - onScreenMargin;
        ny = Mathf.Clamp(ny, target.position.y - maxOff, target.position.y + maxOff);
        if (_hasClamp) ny = Mathf.Clamp(ny, _minCamY, _maxCamY); // 배경 밖 방지(최우선)

        transform.position = new Vector3(nx, ny, offset.z);
    }

    public void SetTarget(Transform playerTransform)
    {
        target = playerTransform;
        ComputeBounds();
        if (target != null)
        {
            float y = target.position.y + verticalOffset;
            if (_hasClamp) y = Mathf.Clamp(y, _minCamY, _maxCamY);
            transform.position = new Vector3(target.position.x + offset.x, y, offset.z);
        }
    }
}
