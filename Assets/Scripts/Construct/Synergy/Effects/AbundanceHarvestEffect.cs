using UnityEngine;

// 丰饶 (Abundance) — per-TURN block income that scales with the size of the
// closed-loop structure.
//
// AbundanceRule claims the whole connected component that contains the cycle, so
// "the closed-loop blocks" = that claim. Each turn (GameFlowManager.OnTurnStarted,
// i.e. the start of every Build phase) this pays out block currency proportional
// to the synergy's size. Subscribes on Apply, unsubscribes on Revoke.
//
// (Unlike the older time-based AbundanceIncomeEffect, this is turn-based and
// size-scaled. Assign THIS to AbundanceRule.effect to use it.)
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Effects/Abundance Harvest",
                 fileName = "AbundanceHarvestEffect")]
public class AbundanceHarvestEffect : GameEffect
{
    public enum CountMode { Pieces, Cells }

    [Header("Per-turn block income (scales with loop size)")]
    [Tooltip("Count the synergy's PIECES (placed modules) or CELLS (individual cubes) as the block count.")]
    public CountMode countMode = CountMode.Pieces;

    [Tooltip("Block currency granted per counted unit, each turn.")]
    [Min(0)] public int blockPerUnit = 1;

    [Tooltip("Flat block currency added on top each turn, regardless of size.")]
    [Min(0)] public int flatBonus = 0;

    bool _subscribed;

    public override void Apply(GameFlowManager game)
    {
        if (_subscribed) return;
        GameFlowManager.OnTurnStarted += OnTurn;
        _subscribed = true;
    }

    public override void Revoke(GameFlowManager game)
    {
        if (!_subscribed) return;
        GameFlowManager.OnTurnStarted -= OnTurn;
        _subscribed = false;
    }

    void OnTurn()
    {
        int count = countMode == CountMode.Pieces
            ? SynergyEffectUtil.CountClaimedPieces(this)
            : SynergyEffectUtil.CountClaimedCells(this);
        if (count <= 0) return;

        int amount = count * Mathf.Max(0, blockPerUnit) + Mathf.Max(0, flatBonus);
        if (amount > 0) ResourceManager.Instance?.AddBlockCurrency(amount);
    }
}
