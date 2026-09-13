using Fusion;
using UnityEngine;

/// <summary>
/// Sniff 로 드러난 동안에만 당길 수 있는 레버. (Revealable 상속)
/// 드러난 상태에서 플레이어가 닿으면 On 토글 → 연결된 대상(문/발판)을 활성/비활성.
/// </summary>
public class LeverSwitch : Revealable
{
    [Networked] public NetworkBool On { get; set; }

    [Tooltip("레버 ON 일 때 활성화할 대상들")]
    public GameObject[] activateOnTargets;
    [Tooltip("레버 ON 일 때 비활성화할 대상들")]
    public GameObject[] deactivateOnTargets;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsRevealed) return;
        if (!other.CompareTag("Player")) return;
        if (Object == null || !Object.HasStateAuthority) return; // 토글은 호스트

        On = !On;
        Debug.Log($"<color=orange>[Lever]</color> 토글 → On={On}");
    }

    public override void Render()
    {
        base.Render();
        // 네트워크 On 상태를 대상들에 반영 (각 클라이언트 로컬)
        if (activateOnTargets != null)
            foreach (var t in activateOnTargets) if (t != null) t.SetActive(On);
        if (deactivateOnTargets != null)
            foreach (var t in deactivateOnTargets) if (t != null) t.SetActive(!On);
    }
}
