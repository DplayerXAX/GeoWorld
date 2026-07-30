using System;
using System.Collections.Generic;
using UnityEngine;

// Owns the live BoardSnapshot, tracks active synergies, and dispatches
// GameEffect Apply/Revoke calls.
//
// Public API:
//   OnPiecePlaced(BlockData, cells)  → registers a new piece, returns it
//   OnPieceRemoved(PlacedPiece)      → unregisters a piece
//   ResetForNewRun()                 → wipes everything, revokes actives
//   Actives                          → read-only view of current activations
//   OnTierChanged                    → event for HUD/FX listeners
//
// Re-evaluation order (called after every placement/removal):
//   1. For each active synergy (in claim age order):
//        • Drop removed pieces from its claim
//        • IsStillSatisfied? No → Revoke + drop active
//        • Yes + absorbAdditionalPieces? Try grow claim from unclaimed.
//          If new claim is strictly bigger / higher tier → Revoke old, Apply new
//   2. Compute unclaimed = AllPieces - all active claims
//   3. For each rule sorted by priority desc, repeatedly:
//        • TryEvaluate against the (freshly recomputed) unclaimed pool
//        • Activate + Apply if satisfied, subtract pieces from unclaimed
//        • Loop again — the SAME rule can claim a SECOND, disjoint set of
//          pieces this pass (e.g. two separate Abundance loops), stopping
//          only once TryEvaluate fails against what's left.
//
// First-locked semantics (Q5): once a rule claims pieces, no other rule —
// and no OTHER instance of the same rule — sees them. Active claims survive
// as long as their pieces still satisfy the rule; deactivating them releases
// pieces back to the unclaimed pool. Multiple ActiveSynergy instances of the
// SAME rule can coexist simultaneously (each an independent, non-overlapping
// claim); GameEffect Apply/Revoke calls are ref-counted per (rule, tier) so
// a shared effect asset is still only Applied once no matter how many
// simultaneous instances hold it, and Revoked only once the last one drops.
public class SynergyEvaluator : MonoBehaviour
{
    public static SynergyEvaluator Instance;

    // Set by GameFlowManager.ApplyRunConfig from LevelDefinition.synergyEnabled
    // (true in Endless, since there's no level asset to read). The single choke
    // point both OnPiecePlaced/OnPieceRemoved gate on — when false, placing or
    // picking up a block never touches the board, never runs a rule, never fires
    // an activation. BoardSnapshot has no consumers outside this file, so there's
    // nothing else that needs to keep tracking pieces when synergies are off.
    public static bool Enabled = true;

    [Header("Rules")]
    [Tooltip("All synergy rule assets. Evaluator sorts by .priority each pass.")]
    public List<SynergyRule> rules = new();

    [Header("Debug")]
    public bool verboseLogging = false;

    readonly BoardSnapshot              _board      = new();
    readonly List<ActiveSynergy>        _actives    = new();
    readonly Dictionary<int, PlacedPiece> _byId     = new();
    readonly Dictionary<(SynergyRule rule, int tier), int> _effectRefCounts = new();
    int _nextId;
    int _nextActiveId;

    public BoardSnapshot Board   => _board;
    public IReadOnlyList<ActiveSynergy> Actives => _actives;

    // (rule, oldTier, newTier) — fires after Revoke/Apply have run.
    // tier 0 in either slot means "inactive".
    public event Action<SynergyRule, int, int> OnTierChanged;

    // (rule, active) — fires whenever an active synergy's CLAIMED PIECES
    // change. `active` is null on deactivation. Fires for:
    //   • Activation
    //   • Absorb (claim grew or tier changed)
    //   • Deactivation
    // Use this from FX listeners that need to redraw on any claim mutation.
    public event Action<SynergyRule, ActiveSynergy> OnClaimChanged;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) RevokeAll();
        Instance = null;
    }

    // ── Public placement events ─────────────────────────────────────────

    public PlacedPiece OnPiecePlaced(BlockData data, BlockColor color, Vector3Int[] worldCells)
    {
        if (!Enabled) return null;   // caller stores the null and hands it right back to OnPieceRemoved — harmless

        var piece = new PlacedPiece(_nextId++, data, color, worldCells);
        if (!_board.AddPiece(piece))
        {
            Debug.LogError($"[Synergy] Cell collision adding {piece}; aborting.");
            return null;
        }
        _byId[piece.id] = piece;
        if (verboseLogging) Debug.Log($"[Synergy] +{piece}");
        ReEvaluate();
        return piece;
    }

    public void OnPieceRemoved(PlacedPiece piece)
    {
        if (piece == null) return;
        if (!_board.RemovePiece(piece)) return;
        _byId.Remove(piece.id);

        // Scrub the removed piece from every active claim. If the active
        // contained it, mark dirty so the version-skip in Pass 1 still
        // re-validates (the claim shape has changed).
        for (int i = 0; i < _actives.Count; i++)
        {
            if (_actives[i].claimedPieces.Remove(piece))
                _actives[i].dirty = true;
        }

        if (verboseLogging) Debug.Log($"[Synergy] -{piece}");
        ReEvaluate();
    }

    public PlacedPiece GetPieceById(int id) => _byId.TryGetValue(id, out var p) ? p : null;

    // ── Jamming (EnemySynergyJammer) ────────────────────────────────────────

    // The active synergy claiming `cell`, or null. Used by jammer enemies to
    // find what they're standing on.
    public ActiveSynergy FindActiveAtCell(Vector3Int cell)
    {
        for (int i = 0; i < _actives.Count; i++)
        {
            var a = _actives[i];
            if (a?.claimedPieces == null) continue;
            foreach (var p in a.claimedPieces)
            {
                if (p?.cells == null) continue;
                for (int k = 0; k < p.cells.Length; k++)
                    if (p.cells[k] == cell) return a;
            }
        }
        return null;
    }

    // Temporarily kill/restore an active's EFFECT without touching its claim, so
    // the synergy stays on the board (and keeps its pieces locked) while a jammer
    // stands on it. Ref-counted: only the first jammer revokes, only the last one
    // to leave re-applies.
    public void SetSuppressed(ActiveSynergy active, bool on)
    {
        if (active == null || active.rule == null) return;

        int before = active.suppressCount;
        active.suppressCount = Mathf.Max(0, before + (on ? 1 : -1));
        if ((before > 0) == (active.suppressCount > 0)) return;   // no edge crossed

        var game = GameFlowManager.Instance;
        if (active.suppressCount > 0) RefRevoke(active.rule, active.tier, game);
        else                          RefApply(active.rule, active.tier, game);

        OnTierChanged?.Invoke(active.rule, active.suppressCount > 0 ? active.tier : 0,
                                           active.suppressCount > 0 ? 0 : active.tier);
        OnClaimChanged?.Invoke(active.rule, active);
    }

    public void ResetForNewRun()
    {
        RevokeAll();
        _board.Clear();
        _byId.Clear();
        _actives.Clear();
        _effectRefCounts.Clear();
        _nextId = 0;
        _nextActiveId = 0;

        // Clear cached version on every rule so the next run starts fresh.
        if (rules != null)
            for (int i = 0; i < rules.Count; i++)
                if (rules[i] != null) rules[i].LastEvalVersion = -1;
    }

    // ── Re-evaluation pipeline ──────────────────────────────────────────

    void ReEvaluate()
    {
        var game = GameFlowManager.Instance;

        // ── Pass 1: validate / absorb existing actives ──────────────────
        // Iterate backwards so we can remove without index issues.
        for (int i = _actives.Count - 1; i >= 0; i--)
        {
            var active = _actives[i];
            var rule   = active.rule;
            if (rule == null) { _actives.RemoveAt(i); continue; }

            int curVer = _board.VersionFor(rule.color);

            // Skip validation when: claim wasn't shrunk by a removal AND no
            // piece of this rule's color (or Universal) has been added/removed
            // since last check. Nothing relevant could have changed.
            bool needCheck = active.dirty || active.lastVersionChecked != curVer;
            active.lastVersionChecked = curVer;
            active.dirty = false;

            if (needCheck && !rule.IsStillSatisfied(_board, active.claimedPieces))
            {
                int oldTier = active.tier;
                // A suppressed active's effect is ALREADY revoked (a jammer is
                // sitting on it) — revoking again would drop the shared refcount
                // twice and silently kill a sibling instance's effect.
                if (!active.Suppressed) RefRevoke(rule, oldTier, game);
                _actives.RemoveAt(i);
                // After revoke, Pass 2 should be able to re-try this rule
                // immediately at the current version (claim just collapsed,
                // remaining pool may form a different valid claim).
                rule.LastEvalVersion = -1;
                if (verboseLogging) Debug.Log($"[Synergy] ✗ {rule.displayName} deactivated");
                OnTierChanged?.Invoke(rule, oldTier, 0);
                OnClaimChanged?.Invoke(rule, null);
                continue;
            }

            if (needCheck)
            {
                bool absorbed = rule.absorbAdditionalPieces && TryAbsorb(active, game);
                // TryAbsorb only notices GROWTH (or a same-count set shift for
                // filter rules). A pure SHRINK — a piece scrubbed out of this
                // claim by OnPieceRemoved just before this pass ran — leaves
                // both sides of TryAbsorb's comparison already shrunk, so it
                // can never detect "we just lost a piece" and never notifies.
                // Fire unconditionally here so FX listeners (e.g. flower
                // decorations) still learn the claim changed and can tear
                // down decorations for the piece that left.
                if (!absorbed) OnClaimChanged?.Invoke(rule, active);
            }
        }

        // ── Pass 2: try to activate/grow every rule, priority order ─────
        // Unlike before, a rule is no longer skipped just because it already
        // has an active claim — the SAME rule can pick up a second, disjoint
        // cluster of pieces this pass (e.g. two separate Abundance loops),
        // each becoming its own ActiveSynergy. ComputeUnclaimed() already
        // excludes every existing claim (including sibling instances of the
        // same rule), so a piece locked into one loop can never be pulled
        // into another loop of the same rule.
        var sorted = SortedRules();
        for (int i = 0; i < sorted.Count; i++)
        {
            var rule = sorted[i];

            // Rules that opt out of multi-instance (Enlightenment: one cube
            // levels up in place, it doesn't fire from a second unrelated
            // cube) never get a NEW instance while one is already active —
            // growth for that existing instance is Pass 1's TryAbsorb job.
            if (!rule.allowMultipleInstances && HasActive(rule)) continue;

            // Skip rules whose relevant pool hasn't changed since last try.
            int curVer = _board.VersionFor(rule.color);
            if (rule.LastEvalVersion == curVer) continue;
            rule.LastEvalVersion = curVer;

            const int maxInstancesPerRulePerPass = 32; // safety cap, not a design limit
            for (int guard = 0; guard < maxInstancesPerRulePerPass; guard++)
            {
                var unclaimed = ComputeUnclaimed();
                if (unclaimed.Count == 0) break;

                if (!rule.TryEvaluate(_board, unclaimed, out var claim, out var tier) || claim == null || claim.Count == 0)
                    break;

                var newActive = new ActiveSynergy(_nextActiveId++, rule, claim, tier);
                SnapshotHighlightCells(rule, newActive);
                _actives.Add(newActive);
                RefApply(rule, tier, game);
                if (verboseLogging) Debug.Log($"[Synergy] ✓ {rule.displayName} activated at tier {tier} ({claim.Count} pieces)");
                OnTierChanged?.Invoke(rule, 0, tier);
                OnClaimChanged?.Invoke(rule, newActive);

                if (!rule.allowMultipleInstances) break; // exactly one instance for this rule
            }
        }
    }

    // Try to grow an active claim by re-evaluating against (claim ∪ unclaimed).
    // Commits when the result is strictly bigger, higher tier, OR a different
    // set of pieces (rule may have shifted its "best fit" to a different
    // configuration of the same count — filter-state-dependent visualizers
    // need to refresh in that case). Returns true iff it committed a change
    // (and therefore already fired OnClaimChanged) — false means the caller
    // should still notify listeners itself if the claim shrank.
    bool TryAbsorb(ActiveSynergy active, GameFlowManager game)
    {
        var extended = new HashSet<PlacedPiece>(active.claimedPieces);
        foreach (var p in ComputeUnclaimed()) extended.Add(p);

        if (!active.rule.TryEvaluate(_board, extended, out var newClaim, out var newTier)) return false;
        if (newClaim == null) return false;

        bool grew    = newClaim.Count > active.claimedPieces.Count;
        bool leveled = newTier > active.tier;
        // For ICellHighlightFilter rules, also re-commit when the claimed SET
        // changes at the same count — the cube origin may have moved to a
        // different valid position, which changes which cells should glow
        // even though the piece count stays the same.
        bool setMoved = active.rule is ICellHighlightFilter
                     && !newClaim.SetEquals(active.claimedPieces);
        if (!grew && !leveled && !setMoved) return false;

        int oldTier = active.tier;
        // While jammed the effect is off; just move the claim and let the jammer's
        // release re-apply at whatever tier it ends up on.
        if (!active.Suppressed) RefRevoke(active.rule, oldTier, game);
        active.claimedPieces = newClaim;
        active.tier          = newTier;
        SnapshotHighlightCells(active.rule, active);
        if (!active.Suppressed) RefApply(active.rule, newTier, game);

        if (verboseLogging)
            Debug.Log($"[Synergy] ↑ {active.rule.displayName} absorbed → tier {oldTier}→{newTier}, {newClaim.Count} pieces");

        if (oldTier != newTier)
            OnTierChanged?.Invoke(active.rule, oldTier, newTier);

        // Always fire for absorb — claimed pieces changed even if tier didn't.
        OnClaimChanged?.Invoke(active.rule, active);
        return true;
    }

    // Snapshot ICellHighlightFilter.ShouldHighlight(...) results for this
    // claim's cells, right after the rule evaluated it — before any later
    // TryEvaluate call (for another instance of the same rule, or another
    // absorb attempt) can overwrite the rule's own cached filter state.
    // Rules that don't implement the filter leave `highlightCells` null,
    // which every consumer treats as "everything in the claim counts".
    static void SnapshotHighlightCells(SynergyRule rule, ActiveSynergy active)
    {
        if (rule is not ICellHighlightFilter filter) { active.highlightCells = null; return; }

        var cells = new HashSet<Vector3Int>();
        foreach (var p in active.claimedPieces)
        {
            if (p?.cells == null) continue;
            for (int k = 0; k < p.cells.Length; k++)
                if (filter.ShouldHighlight(p.cells[k])) cells.Add(p.cells[k]);
        }
        active.highlightCells = cells;
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    HashSet<PlacedPiece> ComputeUnclaimed()
    {
        var u = new HashSet<PlacedPiece>(_board.AllPieces);
        for (int i = 0; i < _actives.Count; i++)
            u.ExceptWith(_actives[i].claimedPieces);
        return u;
    }

    bool HasActive(SynergyRule rule)
    {
        for (int i = 0; i < _actives.Count; i++)
            if (_actives[i].rule == rule) return true;
        return false;
    }

    List<SynergyRule> SortedRules()
    {
        var list = new List<SynergyRule>();
        for (int i = 0; i < rules.Count; i++)
            if (rules[i] != null) list.Add(rules[i]);
        list.Sort((a, b) => b.priority.CompareTo(a.priority));
        return list;
    }

    void RevokeAll()
    {
        var game = GameFlowManager.Instance;
        for (int i = 0; i < _actives.Count; i++)
        {
            var a = _actives[i];
            RefRevoke(a.rule, a.tier, game);
        }
        _actives.Clear();
        _effectRefCounts.Clear();
    }

    // ── Ref-counted Apply/Revoke ────────────────────────────────────────
    // Multiple simultaneous ActiveSynergy instances of the same rule (and
    // same tier) must only Apply the shared effect asset ONCE — it's a
    // singleton with its own internal held/subscribed state, and downstream
    // payout/aggregation (SynergyEffectUtil) already sums across every
    // matching active on its own. Revoke only fires once the LAST instance
    // at that (rule, tier) drops. Mirrors TowerUpgradeGate's multiset model.
    void RefApply(SynergyRule rule, int tier, GameFlowManager game)
    {
        var key = (rule, tier);
        _effectRefCounts.TryGetValue(key, out int count);
        _effectRefCounts[key] = count + 1;
        if (count == 0) SafeApply(rule, tier, game);
    }

    void RefRevoke(SynergyRule rule, int tier, GameFlowManager game)
    {
        var key = (rule, tier);
        if (!_effectRefCounts.TryGetValue(key, out int count) || count <= 0) return;
        count--;
        if (count <= 0)
        {
            _effectRefCounts.Remove(key);
            SafeRevoke(rule, tier, game);
        }
        else
        {
            _effectRefCounts[key] = count;
        }
    }

    static void SafeApply(SynergyRule rule, int tier, GameFlowManager game)
    {
        try { rule.ApplyAt(game, tier); }
        catch (Exception e) { Debug.LogError($"[Synergy] ApplyAt {rule.name} t{tier}: {e}"); }
    }

    static void SafeRevoke(SynergyRule rule, int tier, GameFlowManager game)
    {
        try { rule.RevokeAt(game, tier); }
        catch (Exception e) { Debug.LogError($"[Synergy] RevokeAt {rule.name} t{tier}: {e}"); }
    }
}
