using System.Collections.Generic;
using UnityEngine;

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
    public Dictionary<PlacedPiece, GameObject> spawnedMap = new();
    // Bumped by SynergyEvaluator whenever this active's `claimedPieces` is
    // mutated (e.g. piece removed from board). If false at validation time
    // AND board.VersionFor(rule.color) hasn't changed, the IsStillSatisfied
    // check can be skipped (the claim is intact and pool is unchanged).
    public bool dirty;
    public int  lastVersionChecked;

    // Snapshot of ICellHighlightFilter.ShouldHighlight(...) results for THIS
    // claim, taken immediately after the rule successfully evaluated it (see
    // SynergyEvaluator.SnapshotHighlightCells). null = rule doesn't implement
    // the filter, so every claimed cell counts/decorates (matches the old
    // "filter == null" fallback used throughout).
    //
    // This snapshot exists because rules implementing ICellHighlightFilter
    // cache their filter state (e.g. AbundanceRule._loopCells) as mutable
    // fields on the RULE ASSET, which is a shared singleton — once multiple
    // simultaneous ActiveSynergy instances of the same rule can exist, a
    // later instance's TryEvaluate call would silently corrupt an earlier
    // instance's cached filter state if consumers queried the rule live.
    // Snapshotting right after each successful evaluate captures the
    // correct state before the next TryEvaluate call can stomp it.
    public HashSet<Vector3Int> highlightCells;

    // Stable per-instance id (monotonic, assigned by SynergyEvaluator) — lets
    // HUD hover-highlight target ONE specific active among several instances
    // of the same rule.
    public readonly int id;

    // How many EnemySynergyJammers are currently standing on this claim. >0 =
    // the effect is revoked (jammed) even though the claim itself still holds.
    // Ref-counted because several jammers can sit on the same synergy, and the
    // first one to leave must not un-jam it for the others.
    // Mutated only via SynergyEvaluator.SetSuppressed.
    public int suppressCount;
    public bool Suppressed => suppressCount > 0;

    public ActiveSynergy(int id, SynergyRule rule, HashSet<PlacedPiece> pieces, int tier)
    {
        this.id            = id;
        this.rule          = rule;
        this.claimedPieces = pieces ?? new HashSet<PlacedPiece>();
        this.tier          = tier;
        this.dirty         = true;
        this.lastVersionChecked = -1;
    }
}
