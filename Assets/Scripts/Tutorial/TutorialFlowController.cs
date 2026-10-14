using System;
using Fusion;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 튜토리얼 전용 흐름 컨트롤러 (런타임 생성 uGUI). 게임 테마(우드/골드/픽셀)에 맞춰 꾸민다.
//  - 튜토리얼 씬에서 우상단에 [건너뛰기] 버튼을 표시한다. 누르면 확인 없이 바로 본 스테이지로.
//  - 튜토리얼을 정상 완료(EndPoint 도달)하면 ShowTutorialComplete() 로 축하/확인 화면을 띄운다.
//    -> [도전하기]: 진행도 저장 후 본 스테이지(Main Stage1) 로드
//    -> [로비로]:   러너 종료 후 로비 씬으로
//
//  씬 전환은 Fusion 에서 호스트(서버) 권한이다:
//    - 호스트 또는 솔로(러너 없음): 직접 로드 (솔로는 SceneManager 폴백으로 서버 없이도 테스트 가능)
//    - 클라이언트: 버튼 비활성 + 안내(호스트가 진행)
public class TutorialFlowController : MonoBehaviour
{
    public static TutorialFlowController Instance { get; private set; }

    [Header("설정")]
    public string tutorialSceneName = "TutorialStage";
    public string defaultMainStage = "Main Stage1";
    public string lobbySceneName = "Lobby";

    private string _targetScene = "Main Stage1";
    private bool _confirmShown;
    private bool _busy;

    // ----- 테마 팔레트 (TitleScreen 과 동일 톤) -----
    private static readonly Color WoodFill  = new Color(0.42f, 0.28f, 0.16f, 1f);
    private static readonly Color WoodDark  = new Color(0.19f, 0.11f, 0.05f, 1f);
    private static readonly Color WoodLight = new Color(0.60f, 0.43f, 0.24f, 1f);
    private static readonly Color Gold      = new Color(0.98f, 0.82f, 0.42f, 1f);
    private static readonly Color GoldDeep  = new Color(0.13f, 0.08f, 0.02f, 1f);
    private static readonly Color Stone     = new Color(0.55f, 0.60f, 0.66f, 1f);
    private static readonly Color Parch     = new Color(0.98f, 0.95f, 0.89f, 1f);
    private static readonly Color Ink       = new Color(0.12f, 0.07f, 0.02f, 1f);

    // UI
    private Font _pixel;   // BoldPixels (라틴/숫자 전용)
    private Font _plain;   // Sunflower-Medium (한글)
    private GameObject _skipRoot;
    private GameObject _skipBtn;

    // ----- 조작 안내(순차 미션: 중앙 브리핑 카드 + 사이드 미션 목록 + 월드 화살표) -----
    private GameObject _guideRoot;
    private Image _dim;                  // 전체 화면 암전 오버레이

    // 중앙 브리핑 카드(미션 시작 알림)
    private RectTransform _cardRt;
    private CanvasGroup _cardCg;
    private Text _cardCounter;           // "1/4"
    private Text _cardText;              // 미션 문구
    private Text _cardKey;               // 키 힌트(펄스)
    private float _keyPulse;

    // 사이드 미션 목록(퀘스트 로그) — 미션이 시작될 때 한 줄씩 추가(페이드/팝) 된다.
    private RectTransform[] _rowRt;
    private CanvasGroup[] _rowCg;
    private Image[] _rowBox;
    private Text[] _rowCheck;
    private Text[] _rowLabel;
    private bool[] _rowShown;
    private float[] _rowPop;
    private Vector3 _cardFlyFrom;   // 브리핑 카드가 목록으로 날아갈 시작 위치

    // 미션 상태기계: [이동(Travel) → 암전+중앙 등장(BriefIn) → 유지(BriefHold) → 밝아짐(BriefOut) → 플레이(Play)]
    private enum Phase { Travel, BriefIn, BriefHold, BriefOut, Play }
    private Phase _phase;
    private float _phaseT;

    [Header("미션 연출 타이밍(초)")]
    public float briefInDur = 0.35f;    // 암전 + 중앙 카드 등장
    public float briefHoldDur = 2.0f;   // 중앙 유지(입력 잠금)
    public float briefOutDur = 0.5f;    // 밝아지며 카드 사라짐

    [Header("브리핑이 뜨는 위치(목표까지 남은 거리)")]
    public float ladderBriefLead = 8f;  // 사다리(x≈50.5) 앞 이 거리(≈x42.9)에서 사다리 미션 브리핑
    public float trophyBriefLead = 2f;  // 사다리 위로 올라와 CP2 에 닿으면 트로피 미션 브리핑(등반 중엔 안 뜸)

    private PlayerMovement _localPlayer;

    // 협동 판정: 미션은 '전체 플레이어'가 조건을 만족해야 진행. 위치/등반/접지가 모두 [Networked] 라
    // 피어마다 동일하게 계산되어 브리핑이 자동 동기화된다(= 호스트 기준 동일 화면).
    private readonly System.Collections.Generic.List<PlayerMovement> _playersBuf = new System.Collections.Generic.List<PlayerMovement>();
    private int _playersFrame = -1;
    private readonly System.Collections.Generic.HashSet<PlayerMovement> _jumped = new System.Collections.Generic.HashSet<PlayerMovement>();
    private readonly System.Collections.Generic.HashSet<PlayerMovement> _climbed = new System.Collections.Generic.HashSet<PlayerMovement>();
    private readonly System.Collections.Generic.Dictionary<PlayerMovement, bool> _prevGrounded = new System.Collections.Generic.Dictionary<PlayerMovement, bool>();

    private readonly System.Collections.Generic.List<MissionStep> _steps = new System.Collections.Generic.List<MissionStep>();
    private int _stepIndex;

    // 씬 오브젝트 위치 캐시(런타임 해석)
    private Vector3? _posMove, _posLadder, _posTrophy, _posCp2;

    // 월드 목표 마커(통통 튀는 화살표)
    private Transform _marker;
    private SpriteRenderer _markerArrow;
    private SpriteRenderer _markerGlow;     // 지면 글로우(메이플 리스폰 느낌)
    private static Sprite _arrowSprite;
    private static Sprite _glowSprite;

    // 중앙 카드 레이아웃(상단-중앙 앵커 기준)
    private static readonly Vector2 CardCenterPos = new Vector2(0f, -330f);
    private const float CardCenterScale = 1.0f;
    private const float DimMax = 0.62f;

    // 순차 미션 한 단계.
    private class MissionStep
    {
        public string title;                        // 목록용 짧은 이름
        public string prompt;                       // 중앙 브리핑 문구
        public System.Func<string> keyText;         // 키 힌트
        public System.Func<bool> briefWhen;         // 참이면 브리핑 시작(null=이전 완료 즉시)
        public System.Func<Vector3?> travelTarget;  // 브리핑 전 이동 화살표(null=goal)
        public System.Func<Vector3?> goal;          // 액션 화살표 목표(null=플레이어 머리 위)
        public System.Func<bool> isDone;            // 완료 판정(terminal 이면 무시)
        public bool terminal;                       // 마지막(트로피): EndPoint 가 마무리
        public bool showGlow;                       // 이동 중(Travel) 브리핑 지점 지면에 글로우 표시
    }

    private GameObject _confirmRoot;
    private Text _titleText;
    private Text _bodyText;
    private GameObject _challengeBtn;
    private GameObject _lobbyBtn;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("TutorialFlowController");
        go.AddComponent<TutorialFlowController>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        LoadFonts();
        SceneManager.sceneLoaded += OnSceneLoaded;
        // 키 재설정 시 조작 안내를 즉시 갱신
        MovementKeybinds.OnChanged += RefreshGuide;
        SkillKeybinds.OnChanged += RefreshGuide;
    }

    private void Start()
    {
        // 부트스트랩 시점에 이미 활성화된 첫 씬은 sceneLoaded 가 불리지 않으므로 여기서 한 번 검사.
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        MovementKeybinds.OnChanged -= RefreshGuide;
        SkillKeybinds.OnChanged -= RefreshGuide;
        PlayerMovement.TutorialHold = false;
        if (Instance == this) Instance = null;
    }

    private void LoadFonts()
    {
        _pixel = Resources.Load<Font>("BoldPixels");
        _plain = Resources.Load<Font>("Sunflower-Medium");
        if (_plain == null) _plain = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_plain == null) _plain = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (_pixel == null) _pixel = _plain;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _confirmShown = false;
        _busy = false;
        if (_confirmRoot != null) _confirmRoot.SetActive(false);

        bool inTutorial = scene.name == tutorialSceneName;
        if (inTutorial)
        {
            EnsureSkipUI();
            EnsureGuideUI();
            _localPlayer = null;
            ResetMissions();
            DisableLegacyTutorialHints();
            var ep = FindFirstObjectByType<EndPoint>();
            _targetScene = (ep != null && !string.IsNullOrEmpty(ep.nextSceneName)) ? ep.nextSceneName : defaultMainStage;
        }
        if (_skipRoot != null) _skipRoot.SetActive(inTutorial);
        if (_guideRoot != null) _guideRoot.SetActive(inTutorial);
        if (!inTutorial)
        {
            if (_marker != null) _marker.gameObject.SetActive(false);
            PlayerMovement.TutorialHold = false;
        }
    }

    // 신(新) 미션 시스템(브리핑 카드 + 사이드 목록 + 월드 화살표/글로우)이 모든 안내를 담당하므로,
    // 구(舊) 힌트 시스템(Hint_* 표지판의 트리거 존 배너 + 떠 있는 월드 키 라벨)은 끈다.
    // 이 존들은 미션 흐름과 무관하게 플레이어가 지나가기만 하면 발동해, 미션 브리핑 지점에
    // 도착하기 전에 점프/사다리 안내가 미리 떠버리는 중복/혼선을 유발했다.
    private void DisableLegacyTutorialHints()
    {
        // 혹시 열려 있던 구 배너를 닫는다.
        if (TutorialHintUI.Instance != null) TutorialHintUI.Instance.Hide();

        var hintsRoot = GameObject.Find("TutorialHints");
        if (hintsRoot != null) hintsRoot.SetActive(false); // 표지판 스프라이트/트리거존/월드 라벨 일괄 제거
    }

    private void Update()
    {
        if (_skipBtn != null)
        {
            bool show = !_confirmShown && SceneManager.GetActiveScene().name == tutorialSceneName && LocalCanControlScene();
            if (_skipBtn.activeSelf != show) _skipBtn.SetActive(show);
        }

        UpdateGuide();
    }

    // 미션 상태기계 + 월드 화살표 + 중앙 카드/사이드 목록 갱신.
    private void UpdateGuide()
    {
        if (_guideRoot == null || !_guideRoot.activeSelf || _steps.Count == 0)
        {
            PlayerMovement.TutorialHold = false;
            return;
        }

        _phaseT += Time.deltaTime;
        UpdateRows();     // 목록 행 등장(페이드/팝) 애니메이션
        UpdateLatches();  // 전체 플레이어의 점프/등반 달성 래치 갱신

        var step = _steps[_stepIndex];

        // 브리핑 등장/유지 동안 로컬 입력 정지(밝아지면 해제).
        PlayerMovement.TutorialHold = (_phase == Phase.BriefIn || _phase == Phase.BriefHold);

        switch (_phase)
        {
            case Phase.Travel:   // 다음 브리핑 지점까지 이동(자유 조작)
            {
                SetDim(0f);
                SetCard(0f, CardCenterScale);
                if (step.briefWhen == null || step.briefWhen()) { _phase = Phase.BriefIn; _phaseT = 0f; }
                break;
            }
            case Phase.BriefIn:
            {
                float t = briefInDur > 0f ? Mathf.Clamp01(_phaseT / briefInDur) : 1f;
                SetDim(DimMax * t);
                SetCard(t, Mathf.Lerp(0.88f, CardCenterScale, t));
                if (_phaseT >= briefInDur) { _phase = Phase.BriefHold; _phaseT = 0f; }
                break;
            }
            case Phase.BriefHold:
            {
                SetDim(DimMax);
                SetCard(1f, CardCenterScale);
                if (_phaseT >= briefHoldDur)
                {
                    _cardFlyFrom = _cardRt != null ? _cardRt.position : Vector3.zero;  // 목록으로 날아갈 시작점
                    _phase = Phase.BriefOut; _phaseT = 0f;
                }
                break;
            }
            case Phase.BriefOut:   // 카드가 해당 미션 목록 항목으로 날아가 '추가'된다
            {
                float t = briefOutDur > 0f ? Mathf.Clamp01(_phaseT / briefOutDur) : 1f;
                float e = Mathf.SmoothStep(0f, 1f, t);
                SetDim(DimMax * (1f - t));
                FlyCardToRow(_stepIndex, e);
                if (_phaseT >= briefOutDur)
                {
                    RevealRow(_stepIndex);       // 목록에 항목 추가(팝)
                    _phase = Phase.Play; _phaseT = 0f;
                }
                break;
            }
            case Phase.Play:
            {
                SetDim(0f);
                SetCard(0f, CardCenterScale);

                if (StepInputPressed(_stepIndex)) _keyPulse = 0.25f;

                // 터미널(트로피)은 EndPoint 가 완료 처리 → 자동 판정 없음
                if (!step.terminal && step.isDone != null && step.isDone())
                    EnterStep(_stepIndex + 1);
                break;
            }
        }

        // ----- 월드 마커 -----
        var cur = _steps[_stepIndex];
        if (_marker != null && !_marker.gameObject.activeSelf) _marker.gameObject.SetActive(true);

        // 화살표: 브리핑 표시 중(BriefIn/Hold) 아니고 목표가 있으면 그 위에. (점프 미션은 goal=null → 화살표 없음)
        Vector3? arrowT = null;
        if (_phase != Phase.BriefIn && _phase != Phase.BriefHold)
            arrowT = (_phase == Phase.Travel)
                ? (cur.travelTarget != null ? cur.travelTarget() : null)
                : (cur.goal != null ? cur.goal() : null);
        UpdateArrow(arrowT);

        // 글로우: 사다리 미션으로 이동(Travel) 중일 때만, 고정 지면 위치에.
        Vector3? glowP = (cur.showGlow && _phase == Phase.Travel && cur.travelTarget != null)
            ? cur.travelTarget() : (Vector3?)null;
        UpdateGlow(glowP);
    }


    // 미션 진입: 브리핑 조건이 있으면 이동(Travel)부터, 없으면 바로 암전 브리핑.
    private void EnterStep(int index)
    {
        bool advanced = index > _stepIndex;
        _stepIndex = Mathf.Clamp(index, 0, _steps.Count - 1);
        _phaseT = 0f;
        _keyPulse = 0f;
        var step = _steps[_stepIndex];
        _phase = (step.briefWhen == null) ? Phase.BriefIn : Phase.Travel;
        SetCardContent();
        UpdateList();
        if (advanced) UiSfx.PopupOpen();   // 이전 미션 완료 알림
    }

    private void SetDim(float a)
    {
        if (_dim == null) return;
        var c = _dim.color; c.a = a; _dim.color = c;
    }

    // 중앙 카드 표시(알파 + 스케일 + 키 펄스).
    private void SetCard(float alpha, float scale)
    {
        if (_cardCg != null) _cardCg.alpha = alpha;
        if (_cardRt != null)
        {
            _cardRt.anchoredPosition = CardCenterPos;
            _cardRt.localScale = new Vector3(scale, scale, 1f);
        }
        PulseKey(_cardKey, ref _keyPulse);
    }

    private void HideMarker()
    {
        if (_marker != null && _marker.gameObject.activeSelf) _marker.gameObject.SetActive(false);
    }

    // 브리핑 카드가 중앙에서 해당 미션 목록 행으로 날아가며 작아지고 사라진다.
    private void FlyCardToRow(int i, float e)
    {
        if (_cardRt == null) return;
        Vector3 to = (_rowRt != null && i >= 0 && i < _rowRt.Length && _rowRt[i] != null)
            ? _rowRt[i].position : _cardFlyFrom;
        _cardRt.position = Vector3.Lerp(_cardFlyFrom, to, e);
        float sc = Mathf.Lerp(CardCenterScale, 0.22f, e);
        _cardRt.localScale = new Vector3(sc, sc, 1f);
        if (_cardCg != null) _cardCg.alpha = 1f - e;
        PulseKey(_cardKey, ref _keyPulse);
    }

    // 목록에 미션 항목을 '추가'(등장+팝).
    private void RevealRow(int i)
    {
        if (_rowShown == null || i < 0 || i >= _rowShown.Length || _rowShown[i]) return;
        _rowShown[i] = true;
        _rowPop[i] = 0.35f;
        UiSfx.PopupOpen();
        UpdateList();
    }

    // 추가된 행의 페이드/팝 애니메이션 진행.
    private void UpdateRows()
    {
        if (_rowRt == null) return;
        float dt = Time.deltaTime;
        for (int i = 0; i < _rowRt.Length; i++)
        {
            if (_rowShown != null && _rowShown[i])
            {
                if (_rowCg[i] != null) _rowCg[i].alpha = Mathf.MoveTowards(_rowCg[i].alpha, 1f, dt * 6f);
                float s = 1f;
                if (_rowPop[i] > 0f) { _rowPop[i] -= dt; s = 1f + 0.35f * Mathf.Clamp01(_rowPop[i] / 0.35f); }
                if (_rowRt[i] != null) _rowRt[i].localScale = new Vector3(s, s, 1f);
            }
            else
            {
                if (_rowCg[i] != null) _rowCg[i].alpha = 0f;
                if (_rowRt[i] != null) _rowRt[i].localScale = Vector3.one;
            }
        }
    }

    // 화살표: 목표 위를 둥둥 떠서 아래를 가리킴(null=숨김). 글로우와 독립.
    private void UpdateArrow(Vector3? tgt)
    {
        if (_markerArrow == null) return;
        bool show = tgt.HasValue;
        if (_markerArrow.gameObject.activeSelf != show) _markerArrow.gameObject.SetActive(show);
        if (!show) return;
        float bob = Mathf.Sin(Time.time * 4f) * 0.18f;
        _markerArrow.transform.position = new Vector3(tgt.Value.x, tgt.Value.y + 2.2f + bob, 0f);
        _markerArrow.transform.localScale = Vector3.one * (1.0f + 0.05f * Mathf.Sin(Time.time * 6f));
    }

    // 지면 글로우(메이플 리스폰 느낌): 지정 지면 위치에서 맥박(null=숨김). 화살표와 독립.
    private void UpdateGlow(Vector3? pos)
    {
        if (_markerGlow == null) return;
        bool show = pos.HasValue;
        if (_markerGlow.gameObject.activeSelf != show) _markerGlow.gameObject.SetActive(show);
        if (!show) return;
        _markerGlow.transform.position = new Vector3(pos.Value.x, pos.Value.y, 0f);
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 3.2f);
        float gs = 1f + 0.18f * pulse;
        _markerGlow.transform.localScale = new Vector3(1.9f * gs, 0.8f * gs, 1f);
        var c = _markerGlow.color; c.a = 0.4f + 0.35f * pulse; _markerGlow.color = c;
    }

    // 해당 미션의 키가 눌리는 중인지(키 힌트 반짝 피드백용).
    private bool StepInputPressed(int idx)
    {
        switch (idx)
        {
            case 0: return MovementKeybinds.Pressed(MovementKeybinds.Act.Left) || MovementKeybinds.Pressed(MovementKeybinds.Act.Right);
            case 1: return MovementKeybinds.Pressed(MovementKeybinds.Act.Jump);
            case 2: return MovementKeybinds.Pressed(MovementKeybinds.Act.Up) || MovementKeybinds.Pressed(MovementKeybinds.Act.Down);
            default: return false;
        }
    }

    // 중앙 카드 내용(번호/문구/키) 갱신.
    private void SetCardContent()
    {
        if (_steps.Count == 0) return;
        var step = _steps[_stepIndex];
        if (_cardCounter != null) _cardCounter.text = (_stepIndex + 1) + "/" + _steps.Count;
        if (_cardText != null) _cardText.text = step.prompt;
        if (_cardKey != null) _cardKey.text = step.keyText != null ? step.keyText() : "";
    }

    // 사이드 미션 목록: 완료=체크(골드), 현재=강조(흰색), 예정=흐리게.
    private void UpdateList()
    {
        if (_rowLabel == null) return;
        var dark = new Color(0.10f, 0.08f, 0.05f, 1f);
        var dim  = new Color(Parch.r, Parch.g, Parch.b, 0.45f);
        for (int i = 0; i < _rowLabel.Length; i++)
        {
            bool done = i < _stepIndex;
            bool cur  = i == _stepIndex;
            if (_rowCheck[i] != null) _rowCheck[i].gameObject.SetActive(done);
            if (_rowBox[i]   != null) _rowBox[i].color = (done || cur) ? Gold : dark;
            if (_rowLabel[i] != null) _rowLabel[i].color = done ? Gold : (cur ? Color.white : dim);
        }
    }

    // 미션/연출 상태 초기화(튜토리얼 씬 진입 시).
    private void ResetMissions()
    {
        _jumped.Clear();
        _climbed.Clear();
        _prevGrounded.Clear();
        _keyPulse = 0f;
        _posMove = _posLadder = _posTrophy = _posCp2 = null;
        PlayerMovement.TutorialHold = false;
        SetDim(0f);
        HideMarker();
        // 목록 행 숨김으로 초기화(미션 시작 시 하나씩 추가)
        if (_rowShown != null)
            for (int i = 0; i < _rowShown.Length; i++)
            {
                _rowShown[i] = false; _rowPop[i] = 0f;
                if (_rowCg[i] != null) _rowCg[i].alpha = 0f;
                if (_rowRt[i] != null) _rowRt[i].localScale = Vector3.one;
            }
        if (_cardCg != null) _cardCg.alpha = 0f;
        _stepIndex = 0;
        EnterStep(0);   // 첫 미션(브리핑 조건 없음 → 바로 암전 브리핑)
    }

    // ----- 협동 판정 헬퍼 -----

    // 이번 프레임의 게임플레이 플레이어 목록(프레임당 1회 스캔 캐시). 온라인=유효 NetworkObject, 솔로=그대로.
    private System.Collections.Generic.List<PlayerMovement> Players()
    {
        if (_playersFrame == Time.frameCount) return _playersBuf;
        _playersFrame = Time.frameCount;
        _playersBuf.Clear();
        foreach (var p in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
        {
            if (p == null) continue;
            if (p.GetComponent<Rigidbody2D>() == null) continue;
            if (p.gameObject.name.Contains("Spawner")) continue;
            if (p.Object != null && !p.Object.IsValid) continue;
            _playersBuf.Add(p);
        }
        return _playersBuf;
    }

    // 모든 플레이어가 조건을 만족해야 true(아무도 없으면 false).
    private bool AllPlayers(System.Func<PlayerMovement, bool> cond)
    {
        var ps = Players();
        if (ps.Count == 0) return false;
        for (int i = 0; i < ps.Count; i++) if (!cond(ps[i])) return false;
        return true;
    }

    // 전체 플레이어의 점프/등반 달성을 래치(한 번 하면 유지)로 기록.
    private void UpdateLatches()
    {
        var ps = Players();
        for (int i = 0; i < ps.Count; i++)
        {
            var p = ps[i];
            bool grounded = p.IsGroundedNow;
            _prevGrounded.TryGetValue(p, out bool prev);
            if (prev && !grounded && !p.IsClimbingNow) _jumped.Add(p); // 접지→공중(등반 아님) = 점프
            _prevGrounded[p] = grounded;
            if (p.IsClimbingNow) _climbed.Add(p);
        }
    }

    // ----- 미션 정의 & 목표 위치 해석(런타임, 하드코딩 없이 씬 오브젝트에서) -----
    private void BuildSteps()
    {
        _steps.Clear();
        // 1) 이동: 스폰에서 바로 브리핑 → 체크포인트까지
        _steps.Add(new MissionStep
        {
            title   = "오른쪽으로 이동",
            prompt  = "오른쪽으로 이동하세요!",
            keyText = () => MoveKey(MovementKeybinds.Act.Left) + " / " + MoveKey(MovementKeybinds.Act.Right) + "   좌우 이동",
            briefWhen = null,
            goal    = MoveTarget,
            isDone  = () => { var t = MoveTarget(); return t.HasValue && AllPlayers(p => p.transform.position.x >= t.Value.x - 0.6f); }
        });
        // 2) 점프: 이동 완료 직후 브리핑
        _steps.Add(new MissionStep
        {
            title   = "점프",
            prompt  = "점프해 보세요!",
            keyText = () => MoveKey(MovementKeybinds.Act.Jump) + "   점프",
            briefWhen = null,
            goal    = () => null,   // 플레이어 머리 위
            isDone  = () => AllPlayers(p => _jumped.Contains(p))
        });
        // 3) 사다리: 사다리에 다가가면 브리핑(화살표 따라 접근), 실제 등반으로 완료
        _steps.Add(new MissionStep
        {
            title   = "사다리 오르기",
            prompt  = "사다리를 타고 올라가세요!",
            keyText = () => MoveKey(MovementKeybinds.Act.Up) + " / " + MoveKey(MovementKeybinds.Act.Down) + "   오르내리기",
            travelTarget = LadderBriefSpot,   // 점프 후: 사다리 브리핑 지점 지면에 글로우를 도착할 때까지 표시
            showGlow = true,
            briefWhen = () => { var t = LadderTarget(); return t.HasValue && AllPlayers(p => p.transform.position.x >= t.Value.x - ladderBriefLead); },
            goal    = LadderTarget,
            isDone  = () => AllPlayers(p => _climbed.Contains(p))
        });
        // 4) 트로피(터미널): 꼭대기 체크포인트 도착 시 브리핑, EndPoint 가 완료 처리
        _steps.Add(new MissionStep
        {
            title   = "트로피 도착",
            prompt  = "트로피까지 달려가세요!",
            keyText = () => "결승점으로!",
            travelTarget = Cp2Target,
            // 전원이 사다리에서 내려서(등반 아님) 땅에 선 채 CP2 에 닿았을 때만 — 등반 중엔 안 뜸
            briefWhen = () =>
            {
                var t = Cp2Target();
                if (!t.HasValue) return false;
                return AllPlayers(p => !p.IsClimbingNow && p.IsGroundedNow
                    && p.transform.position.x >= t.Value.x - trophyBriefLead
                    && Mathf.Abs(p.transform.position.y - t.Value.y) <= 2.5f);
            },
            goal    = TrophyTarget,
            terminal = true,
            isDone  = () => false
        });
    }

    private Vector3? MoveTarget()
    {
        if (_posMove.HasValue) return _posMove;
        var go = GameObject.Find("Checkpoint_1");
        if (go != null) _posMove = go.transform.position;
        return _posMove;
    }

    private Vector3? Cp2Target()
    {
        if (_posCp2.HasValue) return _posCp2;
        var go = GameObject.Find("Checkpoint_2");
        if (go != null) _posCp2 = go.transform.position;
        return _posCp2;
    }

    // '사다리 브리핑 발동 지점'(사다리 x - 리드). 지면 높이는 사다리 위쪽(=플랫폼 지면)으로 고정 — 플레이어를 따라가지 않음.
    private Vector3? LadderBriefSpot()
    {
        var t = LadderTarget();
        if (!t.HasValue) return null;
        return new Vector3(t.Value.x - ladderBriefLead, t.Value.y, 0f);
    }

    private Vector3? LadderTarget()
    {
        if (_posLadder.HasValue) return _posLadder;
        // "Ladder" 태그는 Ladders 타일맵(원점, transform=0,0)에 있으므로 transform 이 아니라
        // 콜라이더 bounds 로 실제 사다리 위치를 구한다.
        GameObject tagGo = null;
        try { tagGo = GameObject.FindGameObjectWithTag("Ladder"); } catch { }
        if (tagGo != null)
        {
            // "Ladders" 에는 빈 TilemapCollider2D(bounds 0)와 실제 CompositeCollider2D 가 함께 있다.
            // 0 크기 콜라이더는 걸러내고 가장 큰 것을 실제 사다리로 본다.
            Collider2D best = null;
            foreach (var c in tagGo.GetComponentsInChildren<Collider2D>())
            {
                var s = c.bounds.size;
                if (s.x * s.y < 0.01f) continue;
                if (best == null || s.sqrMagnitude > best.bounds.size.sqrMagnitude) best = c;
            }
            if (best != null) { var b = best.bounds; _posLadder = new Vector3(b.center.x, b.max.y, 0f); } // 사다리 위쪽 진입부
            else if (tagGo.transform.position.sqrMagnitude > 0.01f) _posLadder = tagGo.transform.position;
        }
        if (!_posLadder.HasValue)   // 폴백: Hint_Ladder(비활성 포함)
        {
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                if (t.name == "Hint_Ladder" && t.gameObject.scene.IsValid()) { _posLadder = t.position; break; }
        }
        return _posLadder;
    }

    private Vector3? TrophyTarget()
    {
        if (_posTrophy.HasValue) return _posTrophy;
        var ep = FindFirstObjectByType<EndPoint>();
        if (ep != null) _posTrophy = ep.transform.position;
        return _posTrophy;
    }

    private PlayerMovement GetLocalPlayer()
    {
        if (_localPlayer != null && _localPlayer.gameObject != null &&
            (_localPlayer.Object == null || _localPlayer.Object.IsValid))
            return _localPlayer;

        _localPlayer = null;
        PlayerMovement cand = null;
        foreach (var p in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
        {
            if (p == null) continue;
            if (p.GetComponent<Rigidbody2D>() == null) continue;
            if (p.gameObject.name.Contains("Spawner")) continue;
            if (p.Object != null && p.Object.IsValid)
            {
                if (p.Object.HasInputAuthority) { _localPlayer = p; return p; }
                if (p.Object.HasStateAuthority && cand == null) cand = p;
            }
            else if (cand == null) cand = p;
        }
        _localPlayer = cand;
        return cand;
    }

    // 키 라벨이 눌린 순간 밝게 확대 → 서서히 원복.
    private void PulseKey(Text key, ref float timer)
    {
        if (key == null) return;
        if (timer > 0f)
        {
            timer -= Time.deltaTime;
            float t = Mathf.Clamp01(timer / 0.25f);
            key.color = Color.Lerp(Gold, Color.white, t);
            float s = 1f + 0.22f * t;
            key.rectTransform.localScale = new Vector3(s, s, 1f);
        }
        else
        {
            key.color = Gold;
            key.rectTransform.localScale = Vector3.one;
        }
    }

    // ---------- 외부 진입점 ----------

    public void ShowTutorialComplete(string nextSceneName)
    {
        if (_confirmShown) return;
        if (!string.IsNullOrEmpty(nextSceneName)) _targetScene = nextSceneName;
        EnsureConfirmUI();
        _confirmShown = true;
        _confirmRoot.SetActive(true);
        // 튜토리얼엔 스테이지 타이머 결과 화면이 없으므로 완료 축하 화면에서 클리어 징글을 직접 재생
        if (!SfxPlayer.Play("Jingle_StageClear")) UiSfx.PopupOpen();
        if (_guideRoot != null) _guideRoot.SetActive(false); // 완료 화면에선 안내 숨김
        if (_marker != null) _marker.gameObject.SetActive(false);
        PlayerMovement.TutorialHold = false;

        bool canControl = LocalCanControlScene();
        _bodyText.text = canControl ? "본 스테이지에 도전하시겠어요?" : "호스트가 본 스테이지를 시작합니다...";
        _challengeBtn.SetActive(canControl);
        _lobbyBtn.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnSkip()
    {
        if (_busy) return;
        GoToMainStage(_targetScene);
    }

    private void OnChallenge()
    {
        if (_busy) return;
        GoToMainStage(_targetScene);
    }

    // ---------- 씬 전환 ----------

    private bool LocalCanControlScene()
    {
        var runner = FindFirstObjectByType<NetworkRunner>();
        return runner == null || runner.IsServer;
    }

    private void GoToMainStage(string sceneName)
    {
        if (_busy) return;
        if (string.IsNullOrEmpty(sceneName)) sceneName = defaultMainStage;

        var runner = FindFirstObjectByType<NetworkRunner>();
        if (runner != null && !runner.IsServer)
        {
            Debug.Log("<color=yellow>[Tutorial]</color> 클라이언트는 본 스테이지 진입을 호스트에 위임합니다.");
            return;
        }

        _busy = true;
        CheckpointManager.ResetCheckpoint();
        SaveDataManager.SaveProgressWithTime(sceneName, RunManager.AccumulatedTime);
        HideAll();

        int idx = SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{sceneName}.unity");
        if (runner != null && runner.IsServer && idx >= 0)
        {
            Debug.Log($"<color=lime>[Tutorial]</color> 호스트: 본 스테이지 로드 → {sceneName}");
            runner.LoadScene(SceneRef.FromIndex(idx));
        }
        else
        {
            Debug.Log($"<color=lime>[Tutorial]</color> 솔로: 본 스테이지 로드 → {sceneName}");
            SceneManager.LoadScene(sceneName);
        }
    }

    private async void OnLobby()
    {
        if (_busy) return;
        _busy = true;
        BasicLauncher.MarkVoluntaryExit();
        HideAll();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        foreach (var runner in FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None))
        {
            if (runner == null) continue;
            try { await runner.Shutdown(); } catch (Exception e) { Debug.LogException(e); }
            if (runner != null) Destroy(runner.gameObject);
        }

        SceneManager.LoadScene(lobbySceneName);
    }

    private void HideAll()
    {
        if (_skipRoot != null) _skipRoot.SetActive(false);
        if (_guideRoot != null) _guideRoot.SetActive(false);
        if (_confirmRoot != null) _confirmRoot.SetActive(false);
        if (_marker != null) _marker.gameObject.SetActive(false);
        PlayerMovement.TutorialHold = false;
    }

    // ---------- UI 빌드 (테마 적용) ----------

    private void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(es);
        }
    }

    private Canvas NewCanvas(string name, int sortingOrder)
    {
        var root = new GameObject(name);
        DontDestroyOnLoad(root);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    private void EnsureSkipUI()
    {
        if (_skipRoot != null) return;
        EnsureEventSystem();

        var canvas = NewCanvas("TutorialSkipCanvas", 31000);
        _skipRoot = canvas.gameObject;

        // 우상단 우드 프레임 버튼: "건너뛰기 ▶" (골드 한글 + 외곽선)
        _skipBtn = MakeWoodButton(_skipRoot.transform, "SkipButton",
            new Vector2(1f, 1f), new Vector2(-40f, -40f), new Vector2(230f, 78f),
            WoodFill, "건너뛰기  ▶", Gold, _plain, 30, OnSkip, 0.92f);

        _skipRoot.SetActive(false);
    }

    // 튜토리얼 안내 UI: 사이드 미션 목록(암전 아래) + 암전 오버레이 + 중앙 브리핑 카드(암전 위).
    private void EnsureGuideUI()
    {
        if (_guideRoot != null) return;
        EnsureEventSystem();

        BuildSteps();   // 목록/카드가 제목·문구를 참조하므로 먼저 구성

        var canvas = NewCanvas("TutorialGuideCanvas", 30900);
        _guideRoot = canvas.gameObject;

        BuildMissionList(_guideRoot.transform);   // (1) 사이드 목록 (암전보다 먼저 → 뒤에 그려짐)

        // (2) 전체 화면 암전 오버레이
        var dimGo = NewChild("Dim", _guideRoot.transform);
        Stretch(dimGo.GetComponent<RectTransform>());
        _dim = dimGo.AddComponent<Image>();
        _dim.color = new Color(0f, 0f, 0f, 0f);
        _dim.raycastTarget = false;

        BuildBriefCard(_guideRoot.transform);     // (3) 중앙 브리핑 카드 (암전 위)

        EnsureMarker();
        SetCardContent();
        UpdateList();

        _guideRoot.SetActive(false);
    }

    // 중앙 브리핑 카드: 번호 배지 + 미션 문구 + 키 힌트. CanvasGroup 알파로 등장/퇴장.
    private void BuildBriefCard(Transform parent)
    {
        var panel = NewChild("MissionCard", parent);
        _cardRt = panel.GetComponent<RectTransform>();
        _cardRt.anchorMin = _cardRt.anchorMax = _cardRt.pivot = new Vector2(0.5f, 1f);
        _cardRt.sizeDelta = new Vector2(820f, 132f);
        _cardRt.anchoredPosition = CardCenterPos;
        _cardCg = panel.AddComponent<CanvasGroup>();
        _cardCg.alpha = 0f;

        var border = AddInset(panel.transform, "Border", 0f); border.color = WoodDark;
        var bevel  = AddInset(panel.transform, "Bevel", 4f);  bevel.color = WoodLight;
        var face   = AddInset(panel.transform, "Face", 8f);   face.color = new Color(0.15f, 0.11f, 0.07f, 0.98f);
        Transform P = face.transform;

        var badge = NewChild("Badge", P);
        var brt = badge.GetComponent<RectTransform>();
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0f, 0.5f);
        brt.sizeDelta = new Vector2(92f, 92f);
        brt.anchoredPosition = new Vector2(16f, 0f);
        badge.AddComponent<Image>().color = WoodDark;
        var badgeFace = AddInset(badge.transform, "BFace", 4f); badgeFace.color = Gold;
        _cardCounter = CenterLabel(badgeFace.transform, "1/4", _pixel, 34, Ink);

        _cardText = GuideText(P, new Vector2(126f, -12f), new Vector2(672f, 62f), "", _plain, 38, Gold, TextAnchor.MiddleLeft);
        _cardKey  = GuideText(P, new Vector2(126f, -76f), new Vector2(672f, 40f), "", _plain, 26, Parch, TextAnchor.MiddleLeft);
    }

    // 좌상단 미션 목록(퀘스트 로그): 체크박스 + 제목 한 줄씩.
    private void BuildMissionList(Transform parent)
    {
        int n = _steps.Count;
        const float rowH = 46f, top = 58f, padBottom = 16f;

        var panel = NewChild("MissionList", parent);
        var prt = panel.GetComponent<RectTransform>();
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0f, 1f);
        prt.sizeDelta = new Vector2(400f, top + n * rowH + padBottom);
        prt.anchoredPosition = new Vector2(40f, -40f);
        var border = AddInset(panel.transform, "Border", 0f); border.color = WoodDark;
        var bevel  = AddInset(panel.transform, "Bevel", 4f);  bevel.color = WoodLight;
        var face   = AddInset(panel.transform, "Face", 8f);   face.color = new Color(0.15f, 0.11f, 0.07f, 0.9f);
        Transform P = face.transform;

        GuideText(P, new Vector2(18f, -8f), new Vector2(360f, 38f), "미션", _plain, 28, Gold, TextAnchor.UpperLeft);

        _rowRt = new RectTransform[n];
        _rowCg = new CanvasGroup[n];
        _rowBox = new Image[n];
        _rowCheck = new Text[n];
        _rowLabel = new Text[n];
        _rowShown = new bool[n];
        _rowPop = new float[n];

        for (int i = 0; i < n; i++)
        {
            // 행 컨테이너(통째로 페이드/팝, 카드가 날아올 목표 지점)
            var row = NewChild("Row" + i, P);
            _rowRt[i] = row.GetComponent<RectTransform>();
            _rowRt[i].anchorMin = _rowRt[i].anchorMax = _rowRt[i].pivot = new Vector2(0f, 1f);
            _rowRt[i].sizeDelta = new Vector2(370f, rowH);
            _rowRt[i].anchoredPosition = new Vector2(10f, -(top - 6f) - i * rowH);
            _rowCg[i] = row.AddComponent<CanvasGroup>();
            _rowCg[i].alpha = 0f;   // 처음엔 숨김(미션 시작 시 등장)
            Transform R = row.transform;

            var boxBorder = NewChild("Box", R);
            var bbr = boxBorder.GetComponent<RectTransform>();
            bbr.anchorMin = bbr.anchorMax = bbr.pivot = new Vector2(0f, 1f);
            bbr.sizeDelta = new Vector2(28f, 28f);
            bbr.anchoredPosition = new Vector2(8f, -9f);
            boxBorder.AddComponent<Image>().color = WoodDark;
            _rowBox[i] = AddInset(boxBorder.transform, "In", 3f);
            _rowBox[i].color = new Color(0.10f, 0.08f, 0.05f, 1f);

            _rowCheck[i] = CenterLabel(_rowBox[i].transform, "✓", _plain, 24, Ink);
            _rowCheck[i].gameObject.SetActive(false);

            _rowLabel[i] = GuideText(R, new Vector2(46f, -7f), new Vector2(314f, 32f), _steps[i].title, _plain, 23, Parch, TextAnchor.MiddleLeft);
        }
    }

    // 월드 목표 마커: 지면 글로우(메이플 리스폰 느낌) + 그 위로 낮게 떠 있는 화살표.
    private void EnsureMarker()
    {
        if (_marker != null) return;
        if (_arrowSprite == null) _arrowSprite = MakeArrowSprite();
        if (_glowSprite == null) _glowSprite = MakeGlowSprite();

        var root = new GameObject("TutorialTargetMarker");
        DontDestroyOnLoad(root);
        _marker = root.transform;

        // 지면 글로우(납작한 빛 웅덩이)
        var glowGo = new GameObject("Glow");
        glowGo.transform.SetParent(root.transform, false);
        _markerGlow = glowGo.AddComponent<SpriteRenderer>();
        _markerGlow.sprite = _glowSprite;
        _markerGlow.color = new Color(1f, 0.86f, 0.45f, 0.6f);
        _markerGlow.sortingOrder = 31999;
        glowGo.transform.localPosition = Vector3.zero;   // 마커 루트(지면)에 위치
        glowGo.transform.localScale = new Vector3(1.9f, 0.8f, 1f);

        // 화살표(글로우 위에서 아래를 가리킴)
        var arrowGo = new GameObject("Arrow");
        arrowGo.transform.SetParent(root.transform, false);
        _markerArrow = arrowGo.AddComponent<SpriteRenderer>();
        _markerArrow.sprite = _arrowSprite;
        _markerArrow.sortingOrder = 32000;
        arrowGo.transform.localPosition = new Vector3(0f, 1.3f, 0f);
        arrowGo.transform.localScale = Vector3.one * 0.9f;

        root.SetActive(false);
    }

    // 중심이 밝고 가장자리로 투명해지는 둥근 글로우 스프라이트.
    private static Sprite MakeGlowSprite()
    {
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color32[S * S];
        float cx = (S - 1) * 0.5f, cy = (S - 1) * 0.5f, maxR = S * 0.5f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / maxR;
                float a = Mathf.Clamp01(1f - d);
                a = a * a;   // 가장자리로 부드럽게 감쇠
                px[y * S + x] = new Color(1f, 0.9f, 0.55f, a);
            }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 32f);
    }

    // 아래로 향하는 화살표 스프라이트를 코드로 생성(골드 몸통 + 어두운 외곽). pivot = 아래 꼭짓점.
    private static Sprite MakeArrowSprite()
    {
        const int S = 48;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var px = new Color32[S * S];
        Color32 clear = new Color(0f, 0f, 0f, 0f);
        Color32 body  = new Color(0.98f, 0.82f, 0.42f, 1f); // Gold
        Color32 edge  = new Color(0.13f, 0.08f, 0.02f, 1f); // GoldDeep
        for (int i = 0; i < px.Length; i++) px[i] = clear;

        int headTop = (int)(S * 0.60f);   // 이 높이까지가 삼각형 머리(아래쪽)
        float stemHalf = S * 0.15f;
        int cx = S / 2;
        for (int y = 0; y < S; y++)       // y=0 아래(꼭짓점), y=S-1 위
        {
            float half;
            if (y <= headTop) half = Mathf.Lerp(1.5f, S * 0.46f, (float)y / headTop); // 아래 꼭짓점 → 넓어짐
            else half = stemHalf;                                                      // 위쪽 줄기
            for (int x = 0; x < S; x++)
            {
                float d = Mathf.Abs(x - cx);
                if (d <= half) px[y * S + x] = (d >= half - 2.2f) ? edge : body;
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0f), 32f);
    }

    // 부모를 꽉 채우는 가운데 정렬 라벨.
    private Text CenterLabel(Transform parent, string content, Font font, int size, Color color)
    {
        var t = CreateLabel(parent, "T", new Vector2(0.5f, 0.5f), new Vector2(200f, 200f), content, font, size, color, TextAnchor.MiddleCenter);
        Stretch(t.GetComponent<RectTransform>());
        return t;
    }

    // 좌상단 기준(anchor 0,1) 픽셀 좌표 배치 라벨.
    private Text GuideText(Transform parent, Vector2 anchoredPos, Vector2 size, string text,
        Font font, int fontSize, Color color, TextAnchor align)
    {
        var go = NewChild("T", parent);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;
        var t = go.AddComponent<Text>();
        t.font = font != null ? font : _plain;
        t.fontSize = fontSize;
        t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.color = color;
        t.text = text;
        t.raycastTarget = false;
        return t;
    }

    // 키 재설정 시 현재 미션의 키 힌트를 즉시 갱신.
    private void RefreshGuide()
    {
        if (_steps.Count == 0 || _stepIndex >= _steps.Count) return;
        var step = _steps[_stepIndex];
        if (_cardKey != null) _cardKey.text = step.keyText != null ? step.keyText() : "";
    }

    private static string MoveKey(MovementKeybinds.Act a) => SkillKeybinds.KeyLabel(MovementKeybinds.Get(a, 0));

    private void EnsureConfirmUI()
    {
        if (_confirmRoot != null) return;
        EnsureEventSystem();

        var canvas = NewCanvas("TutorialConfirmCanvas", 32001);
        _confirmRoot = canvas.gameObject;

        // 어두운 배경
        var bg = NewChild("BG", _confirmRoot.transform);
        var bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.04f, 0.03f, 0.02f, 0.82f);
        Stretch(bg.GetComponent<RectTransform>());

        // 제목: 픽셀 폰트 영문 (테마), 골드 + 외곽선
        _titleText = CreateLabel(_confirmRoot.transform, "Title", new Vector2(0.5f, 0.68f),
            new Vector2(1500f, 180f), "TUTORIAL CLEAR!", _pixel, 110, Gold, TextAnchor.MiddleCenter);
        AddOutline(_titleText.gameObject, GoldDeep, 5f);

        // 부제: 한글
        _bodyText = CreateLabel(_confirmRoot.transform, "Body", new Vector2(0.5f, 0.53f),
            new Vector2(1500f, 120f), "본 스테이지에 도전하시겠어요?", _plain, 46, Parch, TextAnchor.MiddleCenter);
        AddOutline(_bodyText.gameObject, new Color(0.06f, 0.14f, 0.10f, 0.95f), 2f);

        _challengeBtn = MakeWoodButton(_confirmRoot.transform, "ChallengeButton",
            new Vector2(0.5f, 0.34f), new Vector2(215f, 0f), new Vector2(400f, 108f),
            Gold, "본 스테이지 도전하기", Ink, _plain, 34, OnChallenge, 1f);

        _lobbyBtn = MakeWoodButton(_confirmRoot.transform, "LobbyButton",
            new Vector2(0.5f, 0.34f), new Vector2(-215f, 0f), new Vector2(400f, 108f),
            Stone, "로비로", Parch, _plain, 34, OnLobby, 1f);

        _confirmRoot.SetActive(false);
    }

    // 우드 프레임(테두리/베벨/면) + 라벨을 가진 버튼. 반환값은 버튼 GameObject.
    // faceColor 가 골드/스톤이면 버튼 면색으로, 우드면 기본 우드룩으로 쓰인다.
    private GameObject MakeWoodButton(Transform parent, string name, Vector2 anchor, Vector2 offset, Vector2 size,
        Color faceColor, string label, Color labelColor, Font labelFont, int fontSize,
        UnityEngine.Events.UnityAction onClick, float alpha)
    {
        var go = NewChild(name, parent);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.sizeDelta = size;
        rt.anchoredPosition = offset;

        // 투명 히트영역 + 버튼 컴포넌트
        var hit = go.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        var btn = go.AddComponent<Button>();

        // 테두리(어두운) → 베벨(밝은) → 면
        var border = AddInset(go.transform, "Border", 0f);
        border.color = WithAlpha(WoodDark, alpha);
        var bevel = AddInset(go.transform, "Bevel", 4f);
        bevel.color = WithAlpha(WoodLight, alpha);
        var face = AddInset(go.transform, "Face", 8f);
        face.color = WithAlpha(faceColor, alpha);
        btn.targetGraphic = face;

        var cb = btn.colors;
        cb.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
        cb.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        cb.fadeDuration = 0.05f;
        btn.colors = cb;
        btn.onClick.AddListener(onClick);

        var lbl = CreateLabel(face.transform, "Label", new Vector2(0.5f, 0.5f), size, label, labelFont, fontSize, labelColor, TextAnchor.MiddleCenter);
        Stretch(lbl.GetComponent<RectTransform>());
        AddOutline(lbl.gameObject, GoldDeep, 2f);
        return go;
    }

    private static Color WithAlpha(Color c, float a) { c.a = a; return c; }

    private static GameObject NewChild(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static Image AddInset(Transform parent, string name, float pad)
    {
        var go = NewChild(name, parent);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(pad, pad); rt.offsetMax = new Vector2(-pad, -pad);
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    private Text CreateLabel(Transform parent, string name, Vector2 anchor, Vector2 size, string content,
        Font font, int fontSize, Color color, TextAnchor align)
    {
        var go = NewChild(name, parent);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;
        var t = go.AddComponent<Text>();
        t.font = font != null ? font : _plain;
        t.fontSize = fontSize;
        t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.color = color;
        t.text = content;
        t.raycastTarget = false;
        return t;
    }

    private static void AddOutline(GameObject go, Color color, float dist)
    {
        var o = go.AddComponent<Outline>();
        o.effectColor = color;
        o.effectDistance = new Vector2(dist, -dist);
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
