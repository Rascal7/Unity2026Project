using Fusion;
using UnityEngine;

// 클리어 타임의 "진짜 소스".
// 호스트가 tick 단위로 Runner.DeltaTime 을 누적하고, [Networked] 값으로 전 피어에 동기화한다.
// 화면 표시는 TimerManager 가 이 값을 읽어서 처리한다. (TimerManager 는 UI 바인딩만 담당)
public class NetworkTimer : NetworkBehaviour
{
    public static NetworkTimer Instance { get; private set; }

    [Networked] public float ElapsedTime { get; private set; }
    [Networked] public NetworkBool IsRunning { get; private set; }
    [Networked] public NetworkBool IsFinished { get; private set; }
    [Networked] public float FinalTime { get; private set; }

    public override void Spawned()
    {
        Instance = this;

        // 스테이지의 TimerText 에 다시 바인딩
        if (TimerManager.Instance != null)
            TimerManager.Instance.FindTimerUI();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this) Instance = null;
    }

    public override void FixedUpdateNetwork()
    {
        // 호스트만 실제 누적(권위). 클라이언트는 [Networked] 로 받은 값을 그대로 사용.
        if (IsRunning && !IsFinished)
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
    }

    // 정지하고 최종 기록을 확정. 반환값 = 확정된 클리어 타임.
    public float StopTimer()
    {
        if (!HasStateAuthority) return FinalTime;
        if (!IsRunning) return FinalTime;
        IsRunning = false;
        IsFinished = true;
        FinalTime = ElapsedTime;
        return FinalTime;
    }
}
