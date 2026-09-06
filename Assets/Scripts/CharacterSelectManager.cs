using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

// 반드시 class 선언이 있어야 합니다!
public class CharacterSelectManager : NetworkBehaviour
{
    [SerializeField] private string gameSceneName = "TutorialStage";

    public override void FixedUpdateNetwork()
    {
        // 호스트(서버)만 다음 씬으로 넘어갈 권한이 있습니다.
        if (!Runner.IsServer) return;

        // 현재 접속한 모든 SessionPlayerItem을 찾습니다.
        var players = FindObjectsByType<SessionPlayerItem>(UnityEngine.FindObjectsSortMode.None);

        if (players.Length > 0)
        {
            bool allReady = true;

            foreach (var p in players)
            {
                // 한 명이라도 레디를 안 했거나 캐릭터를 안 골랐다면 false
                if (!p.IsReady || p.SelectedCharacterIndex == -1)
                {
                    allReady = false;
                    break;
                }
            }

            // 모든 플레이어가 준비 완료되었다면 게임 씬으로 이동!
            if (allReady)
            {
                // 씬 이름(string)을 Fusion이 인식할 수 있는 SceneRef로 변환하여 로드합니다.
                Runner.LoadScene(SceneRef.FromIndex(SceneManager.GetSceneByName(gameSceneName).buildIndex));

                // 또는 가장 확실하고 간단한 방법 (빌드 세팅의 인덱스나 이름을 직접 넣기):
                // Runner.LoadScene(gameSceneName); 
            }
        }
    }
}