using System.Collections.Generic;
using UnityEngine;
#if (UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN) && !UNITY_WSA
using UnityEngine.Windows.Speech;
#endif

/// <summary>
/// 마이크 키워드 인식으로 스킬 입력을 발생시키는 로컬 전용 컴포넌트.
/// - Windows 에서 KeywordRecognizer 로 등록된 키워드("dash", "jump" 등)를 인식하면
///   짧은 시간(activeWindow) 동안 IsSkillRequested 가 true 가 된다.
/// - Spawner.OnInput 이 이 값을 읽어 NetworkInputData.isSkillPressed 로 전달한다.
/// - 네트워크 입력은 각 클라이언트 로컬에서 샘플링되므로 음성 인식도 로컬에서만 돌아가면 된다.
/// - 씬 배선이 필요 없도록 런타임에 스스로 부트스트랩한다.
/// </summary>
public class VoiceSkillInput : MonoBehaviour
{
    public static VoiceSkillInput Instance { get; private set; }

    [Tooltip("인식할 스킬 키워드 목록. 앞으로 캐릭터/챕터별 키워드를 여기에 추가한다.")]
    public List<string> keywords = new List<string> { "dash", "jump", "대시", "점프" };

    [Tooltip("키워드 인식 후 스킬 입력이 유지되는 시간(초). 네트워크 틱 몇 개를 확실히 덮도록 짧게 준다.")]
    public float activeWindow = 0.2f;

    // 이 시각까지 스킬 입력이 활성 상태로 간주된다.
    private float _activeUntil = -1f;
    private string _lastKeyword = "";

    /// <summary>현재 음성으로 스킬이 요청된 상태인가.</summary>
    public static bool IsSkillRequested =>
        Instance != null && Time.time <= Instance._activeUntil;

    /// <summary>마지막으로 인식된 키워드(디버그/향후 캐릭터별 분기용).</summary>
    public static string LastKeyword => Instance != null ? Instance._lastKeyword : "";

#if (UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN) && !UNITY_WSA
    private KeywordRecognizer _recognizer;
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("VoiceSkillInput");
        DontDestroyOnLoad(go);
        go.AddComponent<VoiceSkillInput>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        StartRecognizer();
    }

    private void StartRecognizer()
    {
#if (UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN) && !UNITY_WSA
        if (!PhraseRecognitionSystem.isSupported)
        {
            Debug.LogWarning("[VoiceSkillInput] 이 환경에서는 음성 인식(Windows Speech)이 지원되지 않습니다. 키보드(LeftShift)로 스킬을 테스트하세요.");
            return;
        }

        if (keywords == null || keywords.Count == 0)
        {
            Debug.LogWarning("[VoiceSkillInput] 등록된 키워드가 없습니다.");
            return;
        }

        try
        {
            _recognizer = new KeywordRecognizer(keywords.ToArray());
            _recognizer.OnPhraseRecognized += OnPhraseRecognized;
            _recognizer.Start();
            Debug.Log($"<color=cyan>[VoiceSkillInput]</color> 음성 인식 시작. 키워드: {string.Join(", ", keywords)}");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[VoiceSkillInput] 음성 인식 시작 실패: {e.Message}. 키보드(LeftShift)로 테스트하세요.");
        }
#else
        Debug.Log("[VoiceSkillInput] 현재 플랫폼에서는 음성 인식을 사용할 수 없습니다. 키보드(LeftShift) 대체 입력만 동작합니다.");
#endif
    }

#if (UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN) && !UNITY_WSA
    private void OnPhraseRecognized(PhraseRecognizedEventArgs args)
    {
        _lastKeyword = args.text;
        _activeUntil = Time.time + activeWindow;
        Debug.Log($"<color=lime>[VoiceSkillInput]</color> 키워드 인식: '{args.text}' (신뢰도: {args.confidence})");
    }

    private void OnDestroy()
    {
        if (_recognizer != null)
        {
            if (_recognizer.IsRunning) _recognizer.Stop();
            _recognizer.OnPhraseRecognized -= OnPhraseRecognized;
            _recognizer.Dispose();
            _recognizer = null;
        }
        if (Instance == this) Instance = null;
    }
#else
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
#endif
}
