using UnityEngine;

public class SceneInit : MonoBehaviour
{
    void Start()
    {
        // 씬이 시작되자마자 Lobby에서 온 타이머 매니저를 찾아서
        if (TimerManager.Instance != null)
        {
            // 이 씬에 있는 "TimerText"를 찾으라고 명령합니다.
            TimerManager.Instance.FindTimerUI();
            Debug.Log("<color=green>[Init]</color> 타이머 UI 연결 시도 완료!");
        }
    }
}