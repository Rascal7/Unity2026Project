using UnityEngine;

/// <summary>
/// 스킬 2·3번 이펙트 프레임 모음. Resources/SkillFxLibrary.asset 한 개를 모두가 공유한다.
/// 씬마다 배치되는 오브젝트(숨김 발판 등)도 인스펙터 연결 없이 같은 이펙트를 쓰기 위함.
/// </summary>
[CreateAssetMenu(fileName = "SkillFxLibrary", menuName = "Game/Skill FX Library")]
public class SkillFxLibrary : ScriptableObject
{
    [Header("여우 2 · 킁킁")]
    public Sprite[] sniffRing;         // 냄새 파동 (흰 링 확산)
    public float sniffRingFps = 40f;
    [Tooltip("링 마지막 프레임의 지름(월드). 킁킁 반경에 맞춰 스케일을 계산한다")]
    public float sniffRingBaseDiameter = 2.5f;
    [Tooltip("링 최대 배율. 반경 그대로 키우면 도트가 너무 굵어져서 이 값에서 멈춘다")]
    public float sniffRingMaxScale = 4f;
    public Sprite[] sniffSmoke;        // 코 앞에서 피어오르는 냄새 연기
    public float sniffSmokeFps = 30f;
    public float sniffSmokeScale = 0.8f;
    public Color sniffTint = new Color(0.85f, 1f, 0.9f, 0.85f);
    public Sprite[] revealShine;       // 드러난 오브젝트마다 반짝
    public float revealShineFps = 14f;
    public float revealShineScale = 1.2f;

    [Header("여우 3 · 체인지")]
    public Sprite[] swapPortal;        // 포털 열림 → 유지 → 닫힘
    public float swapPortalFps = 24f;
    public float swapPortalScale = 1.3f;
    public Sprite[] swapPuff;          // 도착 순간 연기
    public float swapPuffFps = 20f;
    public float swapPuffScale = 1f;

    [Header("토끼 3 · 파워업")]
    public Sprite[] powerUpBurst;      // 당근 먹는 순간
    public float powerUpBurstFps = 30f;
    public float powerUpBurstScale = 0.7f;
    public Sprite[] powerUpAura;       // 강화 유지 중 발밑 불꽃 링 (반복)
    public float powerUpAuraFps = 30f;
    public float powerUpAuraScale = 0.9f;
    public Sprite[] powerUpRing;       // 강화 소모 순간 충격 링
    public float powerUpRingFps = 30f;
    public float powerUpRingScale = 0.3f;

    [Header("공통")]
    public int sortingOrder = 6;       // 플레이어(4) 앞

    private static SkillFxLibrary _instance;
    private static bool _loaded;

    public static SkillFxLibrary Instance
    {
        get
        {
            if (!_loaded)
            {
                _loaded = true;
                _instance = Resources.Load<SkillFxLibrary>("SkillFxLibrary");
                if (_instance == null) Debug.LogWarning("[SkillFxLibrary] Resources/SkillFxLibrary.asset 이 없습니다. 스킬 이펙트가 표시되지 않습니다.");
            }
            return _instance;
        }
    }
}
