using System.Collections.Generic;

// Live record of one rule currently fired.
//
// Owned by SynergyEvaluator. `claimedPieces` is mutated when the rule
// absorbs new pieces or a piece is removed from the board (the evaluator
// scrubs removed pieces from every active claim before re-evaluating).
//
// Exposed to outside readers (HUD, FX) read-only via SynergyEvaluator.Actives.
public sealed class ActiveSynergy
{
    public readonly SynergyRule rule;
    public HashSet<PlacedPiece> claimedPieces;
    public int tier;

    public ActiveSynergy(SynergyRule rule, HashSet<PlacedPiece> pieces, int tier)
    {
        this.rule          = rule;
        this.claimedPieces = pieces ?? new HashSet<PlacedPiece>();
        this.tier          = tier;
    }
}
