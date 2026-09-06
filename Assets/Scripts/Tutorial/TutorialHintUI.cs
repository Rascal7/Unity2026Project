using UnityEngine;
using TMPro;

/// <summary>
/// 튜토리얼 힌트를 화면에 표시하는 로컬 UI 매니저(싱글턴).
/// TutorialHintZone 이 플레이어 진입/이탈 시 Show/Hide 를 호출한다.
/// 지금은 텍스트 플레이스홀더를 쓰고, 나중에 방향키/스페이스 이미지로 교체하면 된다.
/// </summary>
public class TutorialHintUI : MonoBehaviour
{
    public static TutorialHintUI Instance { get; private set; }

    [Tooltip("힌트 배경 패널 (켜고 끔)")]
    public GameObject panel;
    [Tooltip("힌트 문구 텍스트 (추후 키 이미지로 교체 가능)")]
    public TMP_Text messageText;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (panel != null) panel.SetActive(false);
    }

    public void Show(string msg)
    {
        if (messageText != null) messageText.text = msg;
        if (panel != null) panel.SetActive(true);
    }

    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
    }
}
