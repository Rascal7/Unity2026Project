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
    [Tooltip("평상시 캐릭터가 화면 중앙보다 이만큼 아래에 위치. 0이면 정중앙. 키울수록 화면 아래쪽 바닥/땅이 덜 보이고 위쪽 시야가 넓어진다")]
    public float verticalOffset = 1.5f;
    [Tooltip("캐릭터가 위로 이만큼 올라가도 카메라 고정(점프 시 바닥 유지). 평상시 위치 기준이므로 일반 점프 높이 + verticalOffset 보다 크게 잡는다")]
    public float lookUp = 3f;
    [Tooltip("캐릭터가 아래로 이만큼 내려가도 카메라 고정. 작은 턱을 내려갈 때 카메라가 들썩이지 않을 정도로")]
    public float lookDown = 1f;

    [Header("Look-Ahead (진행 방향을 미리 보여줌)")]
    [Tooltip("데드존 밖으로 벗어났을 때, 이동 방향(낙하=아래 / 도약=위)으로 카메라를 더 밀어 진행 방향을 보여준다")]
    public bool lookAhead = true;
    [Tooltip("낙하 시 카메라를 추가로 내리는 최대 거리")]
    public float fallLookAheadDistance = 3f;
    [Tooltip("이 낙하 속도(양수)에서 아래 룩어헤드가 최대치. PlayerMovement 의 maxFallSpeed 와 맞추면 좋다")]
    public float fallSpeedForMaxLook = 16f;
    [Tooltip("상승 시 카메라를 추가로 올리는 최대 거리. 아래보다 작게 잡는 게 보통(위험은 대개 아래에 있다)")]
    public float riseLookAheadDistance = 1.5f;
    [Tooltip("이 상승 속도에서 위 룩어헤드가 최대치. superJumpForce 와 맞추면 좋다")]
    public float riseSpeedForMaxLook = 18f;
    [Tooltip("이 속도 미만의 느린 오르내림(사다리 등)에서는 룩어헤드를 걸지 않는다")]
    public float lookAheadMinSpeed = 2f;
    [Tooltip("룩어헤드 중 세로 추적 부드러움. ySmoothSpeed 보다 크게(=빠르게) 해야 낙하를 따라잡는다")]
    public float lookAheadYSmoothSpeed = 0.35f;
    [Tooltip("룩어헤드가 붙고 빠지는 속도. 클수록 즉각적, 작을수록 부드럽다")]
    public float lookAheadResponse = 6f;

    [Header("Bounds")]
    public bool autoClampToBackground = true;
    public bool useManualBounds = false;
    public float minY = -100f, maxY = 100f;
    [Tooltip("급이동 중에도 캐릭터가 화면 밖으로 나가지 않게 하는 여백")]
    public float onScreenMargin = 0.6f;

    [Header("Impulse (흔들림/킥 - 추적 위에 더해지는 별도 레이어)")]
    [Tooltip("임펄스 감쇠 속도 (클수록 빨리 사라짐). 듀얼/시네머신 전환에도 호환되는 오프셋 방식")]
    public float impulseDamping = 14f;

    private Camera _cam;
    private bool _hasClamp;
    private float _minCamY, _maxCamY;
    private Vector3 _basePos;   // 추적이 계산한 순수 위치(흔들림 제외)
    private Vector2 _impulse;   // 방향성 킥 오프셋(감쇠)
    private float _shake;       // 진동형 흔들림 세기(감쇠)

    // 룩어헤드 상태
    private Rigidbody2D _targetRb;      // 있으면 속도를 직접 읽는다
    private Transform _cachedTarget;    // _targetRb 가 어느 target 것인지
    private float _prevTargetY;         // Rigidbody2D 가 없을 때의 위치 차분용
    private bool _hasPrevY;
    private float _lookAheadY;          // 현재 적용 중인 룩어헤드 오프셋 (+위 / -아래, 감쇠)

    /// <summary>방향성 킥을 더한다(대시 등). 추적과 독립적인 오프셋 레이어.</summary>
    public void AddImpulse(Vector2 kick) { _impulse += kick; }
    /// <summary>진동형 화면 흔들림을 더한다(착지 등). 추적과 독립적인 오프셋 레이어.</summary>
    public void AddShake(float amount) { _shake = Mathf.Max(_shake, amount); }

    private void Awake() { _cam = GetComponent<Camera>(); _basePos = transform.position; ComputeBounds(); }
    private void Start() { if (!_hasClamp) ComputeBounds(); CacheTarget(); }

    private void CacheTarget()
    {
        _cachedTarget = target;
        _targetRb = target != null ? target.GetComponent<Rigidbody2D>() : null;
        _hasPrevY = false;
        _lookAheadY = 0f;
    }

    /// <summary>대상의 세로 속도(+위). Rigidbody2D 가 있으면 그대로, 없으면 위치 차분으로 추정.</summary>
    private float GetTargetVerticalSpeed(float dt)
    {
        if (_targetRb != null) return _targetRb.linearVelocity.y;

        float y = target.position.y;
        float v = _hasPrevY ? (y - _prevTargetY) / dt : 0f;
        _prevTargetY = y;
        _hasPrevY = true;
        return v;
    }

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
        if (_cachedTarget != target) CacheTarget();

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);

        // --- 세로: 데드존 "카메라 창" ---
        // 밴드 안이면 카메라 Y 고정(홀드) → 평지/일반 점프에서 완전히 부드럽고 흔들림 면역
        // 밴드 밖(도약 정점/구덩이)에서만 따라 움직임
        float restS = -verticalOffset;
        float topEdge = restS + lookUp;
        float botEdge = restS - lookDown;
        float s = target.position.y - _basePos.y; // 캐릭터의 화면중앙 대비 높이(+위)
        bool outUp = s > topEdge;
        bool outDown = s < botEdge;

        float desiredY;
        if (outUp) desiredY = target.position.y - topEdge;
        else if (outDown) desiredY = target.position.y - botEdge;
        else desiredY = _basePos.y; // 홀드

        // --- 룩어헤드: 데드존을 벗어났을 때만 이동 방향으로 카메라를 더 민다 ---
        // 밴드 안(일반 점프의 오르내림)에서는 목표가 0이라 카메라가 들썩이지 않는다.
        float goal = 0f;   // +위 / -아래
        float t = 0f;      // 룩어헤드 세기 0~1 (스무딩 가속에도 사용)
        if (lookAhead)
        {
            float vy = GetTargetVerticalSpeed(dt);
            if (outDown && vy < 0f)
            {
                t = Mathf.InverseLerp(lookAheadMinSpeed, Mathf.Max(fallSpeedForMaxLook, lookAheadMinSpeed + 0.01f), -vy);
                goal = -t * fallLookAheadDistance;
            }
            else if (outUp && vy > 0f)
            {
                t = Mathf.InverseLerp(lookAheadMinSpeed, Mathf.Max(riseSpeedForMaxLook, lookAheadMinSpeed + 0.01f), vy);
                goal = t * riseLookAheadDistance;
            }
        }
        // 밴드로 돌아오거나 착지하면 goal 이 0이 되며 같은 속도로 평상시 프레이밍에 복귀
        _lookAheadY = Mathf.Lerp(_lookAheadY, goal, 1f - Mathf.Exp(-Mathf.Max(lookAheadResponse, 0f) * dt));
        desiredY += _lookAheadY;

        if (_hasClamp) desiredY = Mathf.Clamp(desiredY, _minCamY, _maxCamY);

        // 프레임률 독립 스무딩 (룩어헤드 중에는 더 빠르게 따라붙는다)
        float ySmooth = Mathf.Lerp(ySmoothSpeed, lookAheadYSmoothSpeed, t);
        float kx = 1f - Mathf.Pow(1f - Mathf.Clamp01(smoothSpeed), dt * 60f);
        float ky = 1f - Mathf.Pow(1f - Mathf.Clamp01(ySmooth), dt * 60f);
        float nx = Mathf.Lerp(_basePos.x, target.position.x + offset.x, kx);
        float ny = Mathf.Lerp(_basePos.y, desiredY, ky);

        // 급이동 중에도 캐릭터가 화면 밖으로 안 나가게 (평지에선 비활성)
        float size = (_cam != null && _cam.orthographic) ? _cam.orthographicSize : 5f;
        float maxOff = size - onScreenMargin;
        ny = Mathf.Clamp(ny, target.position.y - maxOff, target.position.y + maxOff);
        if (_hasClamp) ny = Mathf.Clamp(ny, _minCamY, _maxCamY); // 배경 밖 방지(최우선)

        _basePos = new Vector3(nx, ny, offset.z);

        // 킥/흔들림 오프셋 감쇠 후 추적 위치 위에 더함 (추적과 독립적인 레이어)
        _impulse = Vector2.Lerp(_impulse, Vector2.zero, 1f - Mathf.Exp(-impulseDamping * dt));
        _shake = Mathf.Lerp(_shake, 0f, 1f - Mathf.Exp(-impulseDamping * dt));
        Vector2 shakeOffset = _shake > 0.001f ? Random.insideUnitCircle * _shake : Vector2.zero;
        transform.position = _basePos + (Vector3)_impulse + (Vector3)shakeOffset;
    }

    public void SetTarget(Transform playerTransform)
    {
        target = playerTransform;
        ComputeBounds();
        CacheTarget();
        if (target != null)
        {
            float y = target.position.y + verticalOffset;
            if (_hasClamp) y = Mathf.Clamp(y, _minCamY, _maxCamY);
            _basePos = new Vector3(target.position.x + offset.x, y, offset.z);
            transform.position = _basePos;
        }
    }
}
