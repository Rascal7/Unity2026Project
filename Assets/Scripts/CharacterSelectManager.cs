using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CharacterSelectManager : NetworkBehaviour
{
    [SerializeField] private string gameSceneName = "savefile";

    public override void FixedUpdateNetwork()
    {
        // 호스트(서버)만 씬을 넘어갈 수 있습니다.
        if (!Runner.IsServer) return;

        // 씬에 있는 모든 SessionPlayerItem을 찾습니다.
        var players = FindObjectsByType<SessionPlayerItem>(UnityEngine.FindObjectsSortMode.None);

        if (players.Length > 0)
        {
            bool allReady = true;

            foreach (var p in players)
            {
                // 한 명이라도 준비를 안 했거나 캐릭터를 안 골랐다면 false
                if (!p.IsReady || p.SelectedCharacterIndex == -1)
                {
                    allReady = false;
                    break;
                }
            }

            // 모든 플레이어가 준비 완료되었다면 세이브 파일 선택 씬으로 이동!
            if (allReady)
            {
                int sceneIndex = SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{gameSceneName}.unity");
                if (sceneIndex >= 0)
                {
                    Runner.LoadScene(SceneRef.FromIndex(sceneIndex));
                }
                else
                {
                    Runner.LoadScene(gameSceneName, LoadSceneMode.Single);
                }
            }
        }
    }
}