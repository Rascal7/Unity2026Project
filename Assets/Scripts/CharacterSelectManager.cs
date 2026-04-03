// CharacterSelectManager.cs (호스트가 관리)
public override void FixedUpdateNetwork()
{
    if (!Runner.IsServer) return;

    // 모든 플레이어 아이템을 찾아서 Ready 확인
    var players = FindObjectsOfType<SessionPlayerItem>();
    if (players.Length > 0)
    {
        bool allReady = true;
        foreach (var p in players)
        {
            if (!p.IsReady || p.SelectedCharacterIndex == -1)
            {
                allReady = false;
                break;
            }
        }

        if (allReady)
        {
            // 모든 플레이어가 준비됨! 게임 씬으로 이동
            Runner.SetActiveScene("TestStage");
        }
    }
}