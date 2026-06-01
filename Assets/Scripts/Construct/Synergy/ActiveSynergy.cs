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

    // Bumped by SynergyEvaluator whenever this active's `claimedPieces` is
    // mutated (e.g. piece removed from board). If false at validation time
    // AND board.VersionFor(rule.color) hasn't changed, the IsStillSatisfied
    // check can be skipped (the claim is intact and pool is unchanged).
    public bool dirty;
    public int  lastVersionChecked;

    public ActiveSynergy(SynergyRule rule, HashSet<PlacedPiece> pieces, int tier)
    {
        this.rule          = rule;
        this.claimedPieces = pieces ?? new HashSet<PlacedPiece>();
        this.tier          = tier;
        this.dirty         = true;
        this.lastVersionChecked = -1;
    }
}
