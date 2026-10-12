using Fusion;
using UnityEngine;

public class CharacterSelectManager : NetworkBehaviour
{
    [SerializeField] private string gameSceneName = "savefile";
    private bool _hasTriggeredLoad = false;

    public override void FixedUpdateNetwork()
    {
        // 호스트(서버)만 씬 전환을 수행하며, SessionPlayerItem.TryProceedIfAllReady 로 단일화하여 중복 LoadScene 호출을 방지합니다.
        if (_hasTriggeredLoad || Runner == null || !Runner.IsServer) return;

        if (SessionPlayerItem.TryProceedIfAllReady(Runner, gameSceneName))
        {
            _hasTriggeredLoad = true;
        }
    }
}