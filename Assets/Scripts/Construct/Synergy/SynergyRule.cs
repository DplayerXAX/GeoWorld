using System.Collections.Generic;
using UnityEngine;

// Abstract base for one synergy theme (Order, Harmony, Abundance, …).
//
// Each concrete subclass is authored as a ScriptableObject asset and dropped
// into SynergyEvaluator.rules. The evaluator calls TryEvaluate on each
// inactive rule once per re-evaluation (placement/removal event) with the
// current pool of UNCLAIMED pieces — pieces already locked by another
// active synergy never appear in the pool (Q5).
//
// Tier semantics:
//   • tier 0  = inactive
//   • tier >0 = active, scalar rank (Enlightenment uses 1/2/3 for 2³/3³/4³,
//                Order can return tier 1 always)
//
// Effect dispatch:
//   • Default ApplyAt/RevokeAt forward to the single `effect` field.
//   • Tiered rules (Enlightenment) override to dispatch to per-tier effects.
public abstract class SynergyRule : ScriptableObject
{
    [Header("Identity")]
    public string displayName;

    [Tooltip("The block color this rule scans for. Universal/joker pieces are pulled in automatically when relevant.")]
    public BlockColor color = BlockColor.None;

    [Header("Evaluation")]
    [Tooltip("Higher priority is evaluated first when grabbing pieces from the unclaimed pool. Use to bias which synergy wins when overlapping patterns exist.")]
    public int priority = 0;

    [Tooltip("If true, an active claim tries to grow by absorbing newly-unclaimed pieces every re-evaluation. Use for cube growth (Enlightenment) and any 'more is better' rule.")]
    public bool absorbAdditionalPieces = false;

    [Header("Single-tier effect")]
    [Tooltip("Default effect applied on activation. Tiered rules can ignore this and override ApplyAt/RevokeAt to dispatch per-tier effects.")]
    public GameEffect effect;

    [Header("Visual")]
    [Tooltip("Per-piece visual overlay applied to claimed pieces (flowers, flowing texture, etc.). Leave null for no in-world visual — SynergyVisualFX will skip this rule.")]
    public SynergyVisualizer visualizer;

    [Header("Flavor")]
    [TextArea] public string flavorText;

    // Cache: last BoardSnapshot.VersionFor(this.color) seen when the
    // evaluator tried to (re-)activate this rule. If the version hasn't
    // changed since, no relevant piece moved → skip TryEvaluate.
    [System.NonSerialized] public int LastEvalVersion = -1;

    // Try to satisfy the rule using ONLY pieces in `pool` (which is the
    // current unclaimed set, or claimed-plus-unclaimed during absorption).
    // On success, populate `claimed` with the pieces this rule wants to
    // lock, and `tier` with the activation rank (>= 1).
    public abstract bool TryEvaluate(BoardSnapshot board,
                                     HashSet<PlacedPiece> pool,
                                     out HashSet<PlacedPiece> claimed,
                                     out int tier);

    // Cheap re-check used by SynergyEvaluator to validate an existing claim
    // after a placement/removal. Default falls back to TryEvaluate on the
    // claimed set; override for a faster path if the structure allows it.
    public virtual bool IsStillSatisfied(BoardSnapshot board, HashSet<PlacedPiece> claimedPieces)
    {
        return TryEvaluate(board, new HashSet<PlacedPiece>(claimedPieces), out _, out _);
    }

    // Effect dispatch. Tier is supplied so tiered rules can branch.
    public virtual void ApplyAt(GameFlowManager game, int tier)
    {
        effect?.Apply(game);
    }

    public virtual void RevokeAt(GameFlowManager game, int tier)
    {
        effect?.Revoke(game);
    }

    // Count-based progress toward activation, for the selection HUD (e.g. "2/3").
    // `current`/`required` express how close the piece's colour is to firing this
    // rule. Return false when the rule has no simple count to show (e.g. the
    // cycle-based Abundance). Default: none.
    public virtual bool TryGetActivationProgress(BoardSnapshot board, PlacedPiece piece,
                                                 out int current, out int required)
    {
        current = 0;
        required = 0;
        return false;
    }
}
