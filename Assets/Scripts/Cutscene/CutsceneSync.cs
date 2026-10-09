using System.Collections.Generic;
using Fusion;
using UnityEngine;

// 컷씬을 두 플레이어가 같은 상태일 때만 재생/종료하도록 맞추는 네트워크 오브젝트.
// CutsceneDirector 가 호스트에서 Resources/CutsceneSync 를 Spawn 한다.
//
//   0 Waiting : 각 피어가 영상 준비(Prepare)를 마치면 Ready 를 보고. 접속 중인 전원이 Ready 면
//               StartTick(지금 + 약 0.5초)을 정하고 Playing 으로. (readyTimeout 이 지나면 있는 인원으로 시작)
//   1 Playing : 각 피어는 Runner.Tick 이 StartTick 에 닿는 순간 동시에 재생을 시작한다.
//               전원이 Finished(영상 끝) 또는 전원이 Skip 을 보고하면 Done 으로.
//   2 Done    : 각 피어가 페이드 아웃 후 다음 씬(튜토리얼) 또는 로비로 이동.
public class CutsceneSync : NetworkBehaviour
{
    public static CutsceneSync Instance { get; private set; }

    public const int StateWaiting = 0;
    public const int StatePlaying = 1;
    public const int StateDone = 2;

    private const int ReportReady = 0;
    private const int ReportFinished = 1;
    private const int ReportSkip = 2;

    private const float StartLeadSeconds = 0.5f;   // 시작 신호가 모든 피어에 닿을 여유
    private const float FinishGraceSeconds = 5f;    // 영상 길이 + 이 시간이 지나면 강제 종료(응답 없는 피어 대비)

    [Networked] public int State { get; private set; }
    [Networked] public int StartTick { get; private set; }
    [Networked] public int SkipCount { get; private set; }
    [Networked] public int PlayerCount { get; private set; }

    // 호스트 전용 집계
    private readonly HashSet<PlayerRef> _ready = new HashSet<PlayerRef>();
    private readonly HashSet<PlayerRef> _finished = new HashSet<PlayerRef>();
    private readonly HashSet<PlayerRef> _skipped = new HashSet<PlayerRef>();
    private float _waitStartTime;
    private float _playStartTime;

    public bool HasStarted => State >= StatePlaying && Runner != null && (int)Runner.Tick >= StartTick;

    public override void Spawned()
    {
        Instance = this;
        _waitStartTime = Time.unscaledTime;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this) Instance = null;
    }

    // ---------- 피어 → 호스트 보고 ----------
    public void SendReady() => RPC_Report(ReportReady);
    public void SendFinished() => RPC_Report(ReportFinished);
    public void SendSkip() => RPC_Report(ReportSkip);

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_Report(int kind, RpcInfo info = default)
    {
        PlayerRef p = info.Source == PlayerRef.None ? Runner.LocalPlayer : info.Source;
        switch (kind)
        {
            case ReportReady: _ready.Add(p); break;
            case ReportFinished: _finished.Add(p); break;
            case ReportSkip: _skipped.Add(p); break;
        }
    }

    // ---------- 호스트 진행 ----------
    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        int active = 0, ready = 0, finished = 0, skipped = 0;
        foreach (var p in Runner.ActivePlayers)
        {
            active++;
            if (_ready.Contains(p)) ready++;
            if (_finished.Contains(p)) finished++;
            if (_skipped.Contains(p)) skipped++;
        }
        PlayerCount = active;
        SkipCount = skipped;
        if (active == 0) return;

        var director = CutsceneDirector.Instance;

        if (State == StateWaiting)
        {
            float timeout = director != null ? director.readyTimeout : 10f;
            bool allReady = ready >= active;
            bool timedOut = ready > 0 && Time.unscaledTime - _waitStartTime >= timeout;
            if (allReady || timedOut)
            {
                if (!allReady) Debug.LogWarning($"[CutsceneSync] 준비 대기 시간 초과 ({ready}/{active}). 준비된 인원으로 시작합니다.");
                StartTick = (int)Runner.Tick + Mathf.CeilToInt(StartLeadSeconds / Runner.DeltaTime);
                State = StatePlaying;
                _playStartTime = Time.unscaledTime + StartLeadSeconds;
            }
        }
        else if (State == StatePlaying)
        {
            float clipLength = director != null ? director.ClipLength : 30f;
            bool allFinished = finished >= active;
            bool allSkipped = skipped >= active;
            bool overtime = Time.unscaledTime - _playStartTime >= clipLength + FinishGraceSeconds;
            if (allFinished || allSkipped || overtime)
            {
                if (overtime && !allFinished && !allSkipped)
                    Debug.LogWarning($"[CutsceneSync] 재생 완료 응답 대기 시간 초과 ({finished}/{active}). 다음으로 진행합니다.");
                State = StateDone;
            }
        }
    }
}
