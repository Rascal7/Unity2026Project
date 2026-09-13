using Fusion;
using UnityEngine;

/// <summary>
/// Sniff 로 드러난 동안에만 먹을 수 있는 코인. (Revealable 상속)
/// 드러난 상태에서 플레이어가 닿으면 호스트가 획득 처리 후 제거.
/// </summary>
public class CoinItem : Revealable
{
    [Tooltip("획득 점수(임시)")]
    public int value = 1;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsRevealed) return;
        if (!other.CompareTag("Player")) return;
        if (Object == null || !Object.HasStateAuthority) return; // 획득 판정은 호스트

        Debug.Log($"<color=yellow>[Coin]</color> 획득! (+{value})");
        // TODO: 점수/수집 카운트 연결
        Runner.Despawn(Object);
    }
}
