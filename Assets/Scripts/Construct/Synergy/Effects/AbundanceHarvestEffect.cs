using UnityEngine;

// Abundance — per-turn block income scaling with the closed-loop structure's
// size. Pays out on GameFlowManager.OnTurnStarted (start of each Build phase).
// Unlike the older time-based AbundanceIncomeEffect, this is turn-based and
// size-scaled.
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

    [Header("Visual")]
    [Tooltip("Colour of the harvest mote that floats up from the loop each turn.")]
    public Color moteColor = new Color(0.95f, 0.8f, 0.3f);

    // Last turn's payout + unit count, for the synergy HUD.
    public int LastUnitCount   { get; private set; }
    public int LastPayoutAmount { get; private set; }

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
        LastUnitCount = count;
        if (count <= 0) { LastPayoutAmount = 0; return; }

        int amount = count * Mathf.Max(0, blockPerUnit) + Mathf.Max(0, flatBonus);
        LastPayoutAmount = amount;
        if (amount <= 0) return;
        ResourceManager.Instance?.AddBlockCurrency(amount);

        if (SynergyEffectUtil.TryGetClaimedCellWorld(this, out var w))
        {
            SynergyBuffFx.Mote(w, moteColor,
                GridSystem.instance != null ? GridSystem.instance.cellSize * 0.8f : 0.8f);
            CurrencyFlyFx.Fly(w, isTurret: false, amount);
        }
    }
}
