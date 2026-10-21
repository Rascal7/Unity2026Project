using Fusion;
using UnityEngine;

/// <summary>
/// Sniff 로 드러난 동안에만 먹을 수 있는 코인(구슬 조각). (Revealable 상속)
/// 드러난 상태에서 플레이어가 닿으면 호스트가 획득 처리 후 제거하며, 클리어 타임 단축 보너스(-5초) 및 수집 기록에 반영된다.
/// </summary>
public class CoinItem : Revealable
{
    public static readonly System.Collections.Generic.List<CoinItem> ActiveShards = new System.Collections.Generic.List<CoinItem>();

    [Tooltip("획득 조각 수")]
    public int value = 1;
    [Tooltip("켜면 Sniff 없이 처음부터 보이고 먹을 수 있다 (킁킁 스킬이 없는 1스테이지용)")]
    public bool alwaysVisible;

    private bool _collectedByPlayer;
    private bool _despawnSilently;

    private void Awake()
    {
        if (!ActiveShards.Contains(this))
            ActiveShards.Add(this);
    }

    private void OnDestroy()
    {
        ActiveShards.Remove(this);
    }

    protected override void Apply(bool revealed) => base.Apply(revealed || alwaysVisible);

    /// <summary>체크포인트 이어하기 시 이미 획득했던 조각을 효과음 없이 조용히 제거한다.</summary>
    public void DespawnAsAlreadyCollected(NetworkRunner runner)
    {
        _despawnSilently = true;
        _collectedByPlayer = true;
        if (runner != null && Object != null)
            runner.Despawn(Object);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!alwaysVisible && !IsRevealed) return;
        if (!other.CompareTag("Player")) return;
        if (Object == null || !Object.HasStateAuthority) return; // 획득 판정은 호스트
        if (_collectedByPlayer) return;

        _collectedByPlayer = true;
        RunManager.RecordShardCollected(value, transform.position);
        Debug.Log($"<color=yellow>[Coin]</color> 구슬 조각 획득! (+{value}, 클리어 타임 -{value * RunManager.ShardTimeBonusSeconds:0}초 보너스)");
        Runner.Despawn(Object);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        ActiveShards.Remove(this);
        // 인게임 중 조각이 획득되어 Despawn 될 때 모든 피어에서 코인 획득 효과음 + 반짝임 이펙트 재생
        if (!_despawnSilently && runner != null && runner.IsRunning && gameObject.scene.isLoaded)
        {
            SfxPlayer.Coin();
            var fx = SkillFxLibrary.Instance;
            if (fx != null && fx.revealShine != null && fx.revealShine.Length > 0)
            {
                SpriteEffect.Spawn(fx.revealShine, transform.position, fx.revealShineFps * 1.3f, fx.revealShineScale * 1.25f,
                    false, RevealFxSortingLayer(), fx.sortingOrder + 1, new Color(1f, 0.92f, 0.35f, 1f));
            }
        }
        base.Despawned(runner, hasState);
    }
}

