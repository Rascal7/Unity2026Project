using Fusion;
using UnityEngine;

// 클리어 타임의 "진짜 소스".
// 호스트가 tick 단위로 Runner.DeltaTime 을 누적하고, [Networked] 속성으로 양 피어에 동기화한다.
// 화면 표시는 TimerManager 가 이 값을 읽어서 처리한다. (TimerManager 는 UI 바인딩만 담당)
//
// Phase(단계)로 양 피어가 같은 결과 화면을 보게 한다:
//   0 Running       : 진행 중 (타이머 틱)
//   1 AwaitingChoice: 중간 스테이지 끝 도달 시 [로비]/[계속] 선택 대기 (호스트가 결정)
//   2 Finished      : 최종 끝 도달 시 합산 시간 확정, 자동 로비 복귀
public class NetworkTimer : NetworkBehaviour
{
    public static NetworkTimer Instance { get; private set; }

    public const int PhaseRunning = 0;
    public const int PhaseAwaitingChoice = 1;
    public const int PhaseFinished = 2;
    public const int PhaseIntro = 3;   // STAGE intro card showing; timer held until it ends

    [Networked] public float ElapsedTime { get; private set; }
    [Networked] public NetworkBool IsRunning { get; private set; }
    [Networked] public NetworkBool IsFinished { get; private set; }
    [Networked] public float FinalTime { get; private set; }   // Phase에 따라 구간시간(중간) 또는 합산시간(최종)
    [Networked] public int Phase { get; private set; }
    [Networked] public float IntroRemaining { get; private set; }   // seconds left on STAGE intro before the run starts

    // 최종 결과 애니메이션용: 스테이지별 구간 기록(초)을 네트워크로 전파 (양쪽 플레이어가 동일하게 표시)
    public const int MaxSegments = 12;
    [Networked, Capacity(MaxSegments)] public NetworkArray<float> SegmentTimes { get; }
    [Networked, Capacity(MaxSegments)] public NetworkArray<int> SegmentStages { get; }
    [Networked] public int SegmentCount { get; private set; }
    [Networked] public float RestoredBase { get; private set; } // 개별 내역 없는 이전 누적분(이어하기)

    public override void Spawned()
    {
        Instance = this;
        if (TimerManager.Instance != null)
            TimerManager.Instance.FindTimerUI();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this) Instance = null;
    }

    public override void FixedUpdateNetwork()
    {
        // 호스트만 값을 변경(누적). 클라이언트는 [Networked] 로 복제된 값을 그대로 사용.
        // STAGE intro: hold the timer at 0, count the intro down, then begin the run.
        if (Phase == PhaseIntro)
        {
            if (HasStateAuthority)
            {
                IntroRemaining -= Runner.DeltaTime;
                if (IntroRemaining <= 0f)
                {
                    IntroRemaining = 0f;
                    // Keep ElapsedTime at the start offset set by StartWithIntro (resume support); do not reset to 0.
                    IsRunning = true;
                    Phase = PhaseRunning;
                }
            }
            return;
        }

        if (IsRunning && Phase == PhaseRunning)
            ElapsedTime += Runner.DeltaTime;
    }

    // ---------- 호스트 전용 제어 ----------

    public void StartTimer()
    {
        if (!HasStateAuthority) return;
        ElapsedTime = 0f;
        FinalTime = 0f;
        IsFinished = false;
        IsRunning = true;
        Phase = PhaseRunning;
    }

    // Begin the stage with a short STAGE-card intro. Timer stays at 0 until IntroRemaining hits 0.
    // introSeconds <= 0 starts the run immediately (same as StartTimer).
    public void StartWithIntro(float introSeconds, float startElapsed = 0f)
    {
        if (!HasStateAuthority) return;
        ElapsedTime = startElapsed < 0f ? 0f : startElapsed;   // resume offset (0 for a fresh stage)
        FinalTime = 0f;
        IsFinished = false;
        if (introSeconds > 0f)
        {
            IsRunning = false;
            IntroRemaining = introSeconds;
            Phase = PhaseIntro;
        }
        else
        {
            IsRunning = true;
            IntroRemaining = 0f;
            Phase = PhaseRunning;
        }
    }

    // 중간 스테이지 끝: 타이머 멈춤 + 이 구간 시간 확정 + 선택 대기 단계로.
    // 반환값 = 이 스테이지 구간 시간.
    public float PauseForChoice()
    {
        if (!HasStateAuthority) return FinalTime;
        if (Phase != PhaseRunning) return FinalTime;
        IsRunning = false;
        FinalTime = ElapsedTime;   // 이 스테이지 구간 시간
        Phase = PhaseAwaitingChoice;
        return FinalTime;
    }

    // 중간 결과 확정 후 보너스 반영 동기화
    public void SyncStageChoiceBonus(float rawSegmentTime)
    {
        if (!HasStateAuthority) return;
        if (RunManager.Segments.Count > 0)
        {
            FinalTime = RunManager.Segments[RunManager.Segments.Count - 1];
        }
        RPC_SyncShardStats(
            RunManager.LastCompletedStageShards,
            RunManager.LastCompletedStageTotalShards,
            RunManager.TotalCollectedShards,
            rawSegmentTime);
    }

    public void FinishRun(float totalSeconds)
    {
        if (!HasStateAuthority) return;
        IsRunning = false;
        IsFinished = true;
        FinalTime = totalSeconds;  // 최종 스테이지: 합산 시간

        // 스테이지별 구간 기록 + 스테이지 번호 + 이전 누적분을 네트워크로 전파 (최종 결과 애니메이션에 사용)
        var segs = RunManager.Segments;
        var stgs = RunManager.SegmentStages;
        int n = Mathf.Min(segs.Count, MaxSegments);
        for (int i = 0; i < n; i++)
        {
            SegmentTimes.Set(i, segs[i]);
            SegmentStages.Set(i, i < stgs.Count ? stgs[i] : (i + 1));
        }
        SegmentCount = n;
        RestoredBase = RunManager.RestoredBase;

        RPC_SyncShardStats(
            RunManager.LastCompletedStageShards,
            RunManager.LastCompletedStageTotalShards,
            RunManager.TotalCollectedShards,
            RunManager.LastCompletedStageRawTime);

        Phase = PhaseFinished;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_SyncShardStats(int stageShards, int stageTotalShards, int totalRunShards, float rawSegmentTime)
    {
        RunManager.ApplySyncedResultStats(stageShards, stageTotalShards, totalRunShards, rawSegmentTime);
    }

    // (하위호환) 예전 코드가 부르면 단순 정지. 구간시간 확정만.
    public float StopTimer()
    {
        return PauseForChoice();
    }
}
