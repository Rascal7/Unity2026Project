using Fusion;
using UnityEngine;

// Ŭ���� Ÿ���� "��¥ �ҽ�".
// ȣ��Ʈ�� tick ������ Runner.DeltaTime �� �����ϰ�, [Networked] ������ �� �Ǿ ����ȭ�Ѵ�.
// ȭ�� ǥ�ô� TimerManager �� �� ���� �о ó���Ѵ�. (TimerManager �� UI ���ε��� ���)
//
// Phase(�ܰ�)�� �� �Ǿ ���� ���� ȭ���� ���� �Ѵ�:
//   0 Running       : ���� �� (Ÿ�̸� ��)
//   1 AwaitingChoice: �߰� �������� �� ���� �� [�κ�]/[����] ���� ��� (ȣ��Ʈ�� ����)
//   2 Finished      : ���� �� ���� �� �ջ� �ð� Ȯ��, �ڵ� �κ� ����
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
    [Networked] public float FinalTime { get; private set; }   // Phase�� ���� �����ð�(�߰�) �Ǵ� �ջ�ð�(����)
    [Networked] public int Phase { get; private set; }
    [Networked] public float IntroRemaining { get; private set; }   // seconds left on STAGE intro before the run starts

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
        // ȣ��Ʈ�� ���� ����(����). Ŭ���̾�Ʈ�� [Networked] �� ���� ���� �״�� ���.
        // STAGE intro: hold the timer at 0, count the intro down, then begin the run.
        if (Phase == PhaseIntro)
        {
            if (HasStateAuthority)
            {
                IntroRemaining -= Runner.DeltaTime;
                if (IntroRemaining <= 0f)
                {
                    IntroRemaining = 0f;
                    ElapsedTime = 0f;
                    IsRunning = true;
                    Phase = PhaseRunning;
                }
            }
            return;
        }

        if (IsRunning && Phase == PhaseRunning)
            ElapsedTime += Runner.DeltaTime;
    }

    // ---------- ȣ��Ʈ ���� ���� ----------

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
    public void StartWithIntro(float introSeconds)
    {
        if (!HasStateAuthority) return;
        ElapsedTime = 0f;
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

    // �߰� �������� ��: Ÿ�̸� ���� + �� ���� �ð� Ȯ�� + ���� ��� �ܰ��.
    // ��ȯ�� = �� �������� ���� �ð�.
    public float PauseForChoice()
    {
        if (!HasStateAuthority) return FinalTime;
        if (Phase != PhaseRunning) return FinalTime;
        IsRunning = false;
        FinalTime = ElapsedTime;   // �� �������� ���� �ð�
        Phase = PhaseAwaitingChoice;
        return FinalTime;
    }

    // ���� ��: �ջ� �ð�(totalSeconds)�� Ȯ���ϰ� ���� �ܰ��.
    public void FinishRun(float totalSeconds)
    {
        if (!HasStateAuthority) return;
        IsRunning = false;
        IsFinished = true;
        FinalTime = totalSeconds;  // �� �������� �ջ� �ð�
        Phase = PhaseFinished;
    }

    // (����ȣȯ) ���� �ڵ尡 �θ��� �ܼ� ����. �����ð� Ȯ����.
    public float StopTimer()
    {
        return PauseForChoice();
    }
}
