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
    [Tooltip("지형(Ground 레이어 고정 콜라이더) 좌우 끝에서 카메라를 멈춘다. 배경이 없는 씬은 지형 바닥 아래도 보여주지 않는다")]
    public bool clampToTerrain = true;
    [Tooltip("지형 끝 바깥으로 이만큼은 더 보여준다(월드 단위)")]
    public float terrainMargin = 0f;

    [Header("Impulse (흔들림/킥 - 추적 위에 더해지는 별도 레이어)")]
    [Tooltip("임펄스 감쇠 속도 (클수록 빨리 사라짐). 듀얼/시네머신 전환에도 호환되는 오프셋 방식")]
    public float impulseDamping = 14f;

    [Header("Zoom (스킬 연출 - 기본 크기 위에 더해지는 별도 레이어)")]
    [Tooltip("줌 펀치가 원래 크기로 돌아오는 속도 (클수록 빨리 복귀)")]
    public float zoomPunchDamping = 6f;
    [Tooltip("유지형 줌(강화 도약 등)이 목표값을 따라가는 속도")]
    public float zoomHoldResponse = 5f;

    [Header("Fast Follow (순간이동 직후 빠르게 따라붙기)")]
    [Tooltip("FastFollow 중의 추적 부드러움. smoothSpeed 보다 크게(=빠르게)")]
    public float fastFollowSmooth = 0.4f;

    [Header("Shared Camera (두 플레이어를 한 화면에)")]
    [Tooltip("켜면 내 캐릭터 대신 모든 플레이어의 중앙을 따라가고, 서로 멀어지면 모두 보이도록 줌아웃한다")]
    public bool sharedCamera = true;
    [Tooltip("화면 가장자리와 가장 바깥 플레이어 사이에 남길 가로 여백(월드 단위)")]
    public float framingMarginX = 2f;
    [Tooltip("화면 가장자리와 가장 바깥 플레이어 사이에 남길 세로 여백(월드 단위)")]
    public float framingMarginY = 1.5f;
    [Tooltip("그룹 줌이 목표 크기를 따라가는 속도 (클수록 즉각적)")]
    public float groupZoomResponse = 3f;
    [Tooltip("최대 줌아웃 크기(orthographicSize). 0 이면 PlayerMovement 의 coopMaxDistance 가 딱 화면에 들어오는 크기로 자동 계산")]
    public float maxGroupSize = 0f;

    private Camera _cam;
    private bool _hasClamp;
    private float _minCamY, _maxCamY;
    private float _boundTop, _boundBottom; // 월드 기준 세로 경계 (카메라 크기에 따라 _min/_maxCamY 를 다시 계산)
    private bool _hasClampX;
    private float _minCamX, _maxCamX;
    private float _boundLeft, _boundRight; // 월드 기준 가로 경계 (지형 좌우 끝)
    private float _baseSize = 5f;          // 줌 연출 전 원래 orthographicSize
    private float _zoomPunch;              // 순간 줌 (음수 = 줌인, 감쇠)
    private float _zoomHold;               // 유지형 줌 목표 (양수 = 줌아웃)
    private float _zoomHoldCur;
    private float _fastFollowUntil = -1f;
    private Vector3 _basePos;   // 추적이 계산한 순수 위치(흔들림 제외)
    private Vector2 _impulse;   // 방향성 킥 오프셋(감쇠)
    private float _shake;       // 진동형 흔들림 세기(감쇠)

    // 룩어헤드 상태
    private Rigidbody2D _targetRb;      // 있으면 속도를 직접 읽는다
    private Transform _cachedTarget;    // _targetRb 가 어느 target 것인지
    private float _prevTargetY;         // Rigidbody2D 가 없을 때의 위치 차분용
    private bool _hasPrevY;
    private float _lookAheadY;          // 현재 적용 중인 룩어헤드 오프셋 (+위 / -아래, 감쇠)

    // 공유 카메라 상태
    private readonly System.Collections.Generic.List<PlayerMovement> _players = new System.Collections.Generic.List<PlayerMovement>(4);
    private bool _grouped;              // 이번 프레임 2명 이상을 함께 잡고 있는가
    private Vector2 _groupMin, _groupMax; // 잡고 있는 플레이어들의 위치 범위 (1명이면 min == max)
    private float _groupSize = -1f;     // 그룹 줌이 반영된 기본 크기 (연출 줌 이전, 스무딩)

    /// <summary>방향성 킥을 더한다(대시 등). 추적과 독립적인 오프셋 레이어.</summary>
    public void AddImpulse(Vector2 kick) { _impulse += kick; }
    /// <summary>진동형 화면 흔들림을 더한다(착지 등). 추적과 독립적인 오프셋 레이어.</summary>
    public void AddShake(float amount) { _shake = Mathf.Max(_shake, amount); }
    /// <summary>순간 줌. 비율(-0.06 = 6% 줌인, +0.08 = 8% 줌아웃). 저절로 원래 크기로 돌아온다.</summary>
    public void AddZoomPunch(float amount) { if (Mathf.Abs(amount) > Mathf.Abs(_zoomPunch)) _zoomPunch = amount; }
    /// <summary>유지형 줌 목표 비율. 0 으로 되돌리면 부드럽게 원래 크기로 돌아온다.</summary>
    public void SetZoomHold(float amount) { _zoomHold = amount; }
    /// <summary>잠시 동안 평소보다 빠르게 대상을 따라간다(가까운 순간이동).</summary>
    public void FastFollow(float seconds) { _fastFollowUntil = Time.time + seconds; }
    /// <summary>
    /// 부드러운 추적 없이 바로 지정 위치(대상이 도착할 지점)로 옮긴다(먼 순간이동).
    /// 클라이언트에서는 순간이동한 위치가 한두 프레임 늦게 반영될 수 있어 도착 지점을 직접 받는다.
    /// </summary>
    public void SnapToTarget(Vector3 targetPos)
    {
        // 공유 카메라는 플레이어들의 중앙을 보므로 한 명이 순간이동해도(위치 바꾸기 등) 건너뛸 필요가 없다.
        if (_grouped) return;
        _lookAheadY = 0f;
        _hasPrevY = false;
        float y = targetPos.y + verticalOffset;
        if (_hasClamp) y = Mathf.Clamp(y, _minCamY, _maxCamY);
        float x = targetPos.x + offset.x;
        if (_hasClampX) x = Mathf.Clamp(x, _minCamX, _maxCamX);
        _basePos = new Vector3(x, y, offset.z);
        _impulse = Vector2.zero;
    }

    private void Awake()
    {
        _cam = GetComponent<Camera>();
        if (_cam != null && _cam.orthographic) _baseSize = _cam.orthographicSize;
        _basePos = transform.position;
        ComputeBounds();
    }
    private void Start() { if (!_hasClamp) ComputeBounds(); CacheTarget(); }

    private void CacheTarget()
    {
        _cachedTarget = target;
        _targetRb = target != null ? target.GetComponent<Rigidbody2D>() : null;
        _hasPrevY = false;
        _lookAheadY = 0f;
    }

    /// <summary>
    /// 대상의 세로 속도(+위). 혼자일 때 Rigidbody2D 가 있으면 그대로, 아니면(공유 카메라 포함) 추적 지점의 위치 차분으로 추정.
    /// 원격 플레이어의 Rigidbody2D 속도는 클라이언트에서 갱신되지 않으므로 공유 카메라는 차분을 쓴다.
    /// </summary>
    private float GetTargetVerticalSpeed(float y, float dt)
    {
        if (_targetRb != null && !_grouped) return _targetRb.linearVelocity.y;

        float v = _hasPrevY ? (y - _prevTargetY) / dt : 0f;
        _prevTargetY = y;
        _hasPrevY = true;
        return v;
    }

    private void ComputeBounds()
    {
        Bounds terrain = default;
        bool hasTerrain = clampToTerrain && TryGetTerrainBounds(out terrain);
        _hasClampX = hasTerrain;
        if (hasTerrain)
        {
            _boundLeft = terrain.min.x - terrainMargin;
            _boundRight = terrain.max.x + terrainMargin;
        }

        _hasClamp = false;
        if (useManualBounds) { _boundTop = maxY; _boundBottom = minY; _hasClamp = true; }
        else if (autoClampToBackground && TryGetBackgroundBounds(out float top, out float bottom))
        {
            _boundTop = top;
            _boundBottom = bottom;
            _hasClamp = true;
        }
        else if (hasTerrain)
        {
            // 배경이 없는 씬(Main Stage1/2)은 위쪽은 열어 두고(점프 높이 제한 없음) 지형 바닥 아래만 막는다.
            _boundTop = float.PositiveInfinity;
            _boundBottom = terrain.min.y - terrainMargin;
            _hasClamp = true;
        }
        ApplyBoundsForSize(CurrentSize());
    }

    private static bool TryGetBackgroundBounds(out float top, out float bottom)
    {
        top = float.MinValue; bottom = float.MaxValue;
        var bgRoot = GameObject.Find("Background");
        if (bgRoot == null) return false;
        var renderers = bgRoot.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return false;

        foreach (var r in renderers)
        {
            if (r.bounds.max.y > top) top = r.bounds.max.y;
            if (r.bounds.min.y < bottom) bottom = r.bounds.min.y;
        }
        return true;
    }

    /// <summary>
    /// 지형 범위: Ground 레이어의 고정 콜라이더 전체. 낙사선(PlayerMovement.ResolveFallDeathY)과 같은 기준으로,
    /// 런타임에 스폰되는 네트워크 오브젝트(당근 발판 등)와 트리거는 제외한다.
    /// </summary>
    private bool TryGetTerrainBounds(out Bounds bounds)
    {
        bounds = default;
        var pm = target != null ? target.GetComponent<PlayerMovement>() : null;
        int mask = pm != null && pm.groundLayer.value != 0 ? pm.groundLayer.value : LayerMask.GetMask("Ground");
        if (mask == 0) return false;

        bool found = false;
        foreach (var col in FindObjectsByType<Collider2D>(FindObjectsSortMode.None))
        {
            if (col == null || !col.enabled || col.isTrigger) continue;
            if ((mask & (1 << col.gameObject.layer)) == 0) continue;
            if (col.GetComponentInParent<Fusion.NetworkObject>() != null) continue;
            if (!found) { bounds = col.bounds; found = true; }
            else bounds.Encapsulate(col.bounds);
        }
        return found;
    }

    private float CurrentSize() => (_cam != null && _cam.orthographic) ? _cam.orthographicSize : 5f;

    // 줌으로 화면 크기가 바뀌어도 배경/지형 밖이 보이지 않도록 카메라 허용 범위를 다시 잡는다.
    private void ApplyBoundsForSize(float size)
    {
        if (_hasClamp)
        {
            _maxCamY = _boundTop - size;
            _minCamY = _boundBottom + size;
            if (_maxCamY < _minCamY) _maxCamY = _minCamY = (_boundTop + _boundBottom) * 0.5f;
        }
        if (_hasClampX)
        {
            float halfW = size * (_cam != null ? _cam.aspect : 16f / 9f);
            _maxCamX = _boundRight - halfW;
            _minCamX = _boundLeft + halfW;
            if (_maxCamX < _minCamX) _maxCamX = _minCamX = (_boundLeft + _boundRight) * 0.5f;
        }
    }

    /// <summary>
    /// 함께 잡을 플레이어들의 위치 범위를 모은다. 2명 이상이면 true(공유 카메라), 아니면 target 하나만 본다.
    /// 원격 플레이어도 보간된 transform 을 써서 화면이 부드럽게 움직인다.
    /// </summary>
    private bool UpdateGroup()
    {
        if (sharedCamera) PlayerMovement.GetActivePlayers(_players);
        else _players.Clear();

        int count = 0;
        for (int i = 0; i < _players.Count; i++)
        {
            var p = _players[i];
            if (p == null || !p.isActiveAndEnabled) continue;
            Vector2 pos = p.transform.position;
            if (count == 0) { _groupMin = _groupMax = pos; }
            else { _groupMin = Vector2.Min(_groupMin, pos); _groupMax = Vector2.Max(_groupMax, pos); }
            count++;
        }
        if (count < 2 && target != null) _groupMin = _groupMax = target.position;
        return count >= 2;
    }

    // 그룹 줌 최대 크기: 지정값이 없으면 플레이어 거리 제한이 여백까지 포함해 딱 들어오는 크기.
    // 그래서 거리 제한에 걸리는 순간 줌아웃도 함께 멈춘다.
    private float MaxGroupSize(float aspect)
    {
        if (maxGroupSize > 0f) return Mathf.Max(maxGroupSize, _baseSize);
        var pm = _players.Count > 0 ? _players[0] : null;
        float limit = 0f;
        if (pm != null && pm.coopMaxDistanceX > 0f) limit = Mathf.Max(limit, (pm.coopMaxDistanceX * 0.5f + framingMarginX) / aspect);
        if (pm != null && pm.coopMaxDistanceY > 0f) limit = Mathf.Max(limit, pm.coopMaxDistanceY * 0.5f + framingMarginY);
        return limit > 0f ? Mathf.Max(limit, _baseSize) : _baseSize * 2f;
    }

    private void UpdateZoom(float dt)
    {
        _zoomPunch = Mathf.Lerp(_zoomPunch, 0f, 1f - Mathf.Exp(-zoomPunchDamping * dt));
        _zoomHoldCur = Mathf.Lerp(_zoomHoldCur, _zoomHold, 1f - Mathf.Exp(-zoomHoldResponse * dt));
        if (_cam == null || !_cam.orthographic) return;

        // 그룹 줌: 모든 플레이어가 여백을 두고 들어오는 크기 (기본 크기보다 작아지지 않고 최대 크기에서 멈춘다)
        float aspect = Mathf.Max(_cam.aspect, 0.01f);
        float want = _baseSize;
        if (_grouped)
        {
            Vector2 half = (_groupMax - _groupMin) * 0.5f;
            float fit = Mathf.Max((half.x + framingMarginX) / aspect, half.y + framingMarginY);
            want = Mathf.Clamp(fit, _baseSize, MaxGroupSize(aspect));
        }
        if (_groupSize < 0f) _groupSize = want;
        _groupSize = Mathf.Lerp(_groupSize, want, 1f - Mathf.Exp(-groupZoomResponse * dt));

        _cam.orthographicSize = _groupSize * (1f + _zoomHoldCur + _zoomPunch);
        ApplyBoundsForSize(_cam.orthographicSize);
    }

    /// <summary>
    /// 범위 [lo, hi] 가 화면 중심 c 기준 ±maxOff 안에 들도록 c 를 민다.
    /// 범위가 화면보다 넓으면(한 명만 낙하, 거리 제한 직후 등) 둘 다 놓치지 않도록 priority(내 캐릭터)를 화면에 남기고
    /// 그 안에서 가운데 쪽으로 최대한 붙인다. 화면 밖 상대는 CoopPingAndIndicatorHUD 의 가장자리 표시가 가리킨다.
    /// </summary>
    private static float KeepRangeOnScreen(float c, float lo, float hi, float maxOff, float priority)
    {
        float min = hi - maxOff, max = lo + maxOff;
        if (min <= max) return Mathf.Clamp(c, min, max);
        return Mathf.Clamp((lo + hi) * 0.5f, priority - maxOff, priority + maxOff);
    }

    private void LateUpdate()
    {
        if (target == null) return;
        if (_cachedTarget != target) CacheTarget();

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);

        // 추적 지점: 공유 카메라면 플레이어들의 범위 중앙, 혼자면 target
        _grouped = UpdateGroup();
        Vector2 focus = (_groupMin + _groupMax) * 0.5f;

        UpdateZoom(dt);

        // --- 세로: 데드존 "카메라 창" ---
        // 밴드 안이면 카메라 Y 고정(홀드) → 평지/일반 점프에서 완전히 부드럽고 흔들림 면역
        // 밴드 밖(도약 정점/구덩이)에서만 따라 움직임
        float restS = -verticalOffset;
        float topEdge = restS + lookUp;
        float botEdge = restS - lookDown;
        float s = focus.y - _basePos.y; // 추적 지점의 화면중앙 대비 높이(+위)
        bool outUp = s > topEdge;
        bool outDown = s < botEdge;

        float desiredY;
        if (outUp) desiredY = focus.y - topEdge;
        else if (outDown) desiredY = focus.y - botEdge;
        else desiredY = _basePos.y; // 홀드

        // --- 룩어헤드: 데드존을 벗어났을 때만 이동 방향으로 카메라를 더 민다 ---
        // 밴드 안(일반 점프의 오르내림)에서는 목표가 0이라 카메라가 들썩이지 않는다.
        float goal = 0f;   // +위 / -아래
        float t = 0f;      // 룩어헤드 세기 0~1 (스무딩 가속에도 사용)
        if (lookAhead)
        {
            float vy = GetTargetVerticalSpeed(focus.y, dt);
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
        float xSmooth = smoothSpeed;
        if (Time.time < _fastFollowUntil)
        {
            xSmooth = Mathf.Max(xSmooth, fastFollowSmooth);
            ySmooth = Mathf.Max(ySmooth, fastFollowSmooth);
        }
        float kx = 1f - Mathf.Pow(1f - Mathf.Clamp01(xSmooth), dt * 60f);
        float ky = 1f - Mathf.Pow(1f - Mathf.Clamp01(ySmooth), dt * 60f);
        float nx = Mathf.Lerp(_basePos.x, focus.x + offset.x, kx);
        float ny = Mathf.Lerp(_basePos.y, desiredY, ky);

        // 급이동 중에도 캐릭터(공유 카메라면 모든 플레이어)가 화면 밖으로 안 나가게 (평지에선 비활성)
        float size = (_cam != null && _cam.orthographic) ? _cam.orthographicSize : 5f;
        float maxOff = size - onScreenMargin;
        ny = KeepRangeOnScreen(ny, _groupMin.y, _groupMax.y, maxOff, target.position.y);
        if (_grouped)
        {
            float halfW = size * ((_cam != null) ? _cam.aspect : 16f / 9f);
            nx = KeepRangeOnScreen(nx, _groupMin.x + offset.x, _groupMax.x + offset.x, halfW - onScreenMargin, target.position.x + offset.x);
        }
        if (_hasClamp) ny = Mathf.Clamp(ny, _minCamY, _maxCamY); // 배경 밖 방지(최우선)
        if (_hasClampX) nx = Mathf.Clamp(nx, _minCamX, _maxCamX); // 지형 좌우 끝 밖 방지(최우선)

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
            float x = target.position.x + offset.x;
            if (_hasClampX) x = Mathf.Clamp(x, _minCamX, _maxCamX);
            _basePos = new Vector3(x, y, offset.z);
            transform.position = _basePos;
        }
    }
}
