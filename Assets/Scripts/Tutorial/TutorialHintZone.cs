using UnityEngine;
using Fusion;

/// <summary>
/// 플레이어(로컬)가 이 트리거 존에 들어오면 화면에 힌트를 띄우고, 나가면 숨긴다.
/// 네트워크 게임이므로 InputAuthority(로컬 플레이어)일 때만 반응한다.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class TutorialHintZone : MonoBehaviour
{
    [TextArea]
    [Tooltip("표시할 힌트 문구 (추후 키 이미지로 교체 가능)")]
    public string message = "이동:  < >";

    private void Reset()
    {
        var c = GetComponent<Collider2D>();
        if (c != null) c.isTrigger = true;
    }

    // 네트워크가 없으면(단독 테스트) 그냥 통과, 있으면 로컬 플레이어만 true
    private bool IsLocalPlayer(Collider2D other)
    {
        if (!other.CompareTag("Player")) return false;
        var no = other.GetComponentInParent<NetworkObject>();
        return no == null || no.HasInputAuthority;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (IsLocalPlayer(other) && TutorialHintUI.Instance != null)
            TutorialHintUI.Instance.Show(message);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (IsLocalPlayer(other) && TutorialHintUI.Instance != null)
            TutorialHintUI.Instance.Hide();
    }
}
