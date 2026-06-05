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
//   3. For each inactive rule sorted by priority desc:
//        • TryEvaluate against unclaimed
//        • Activate + Apply if satisfied, subtract pieces from unclaimed
//
// First-locked semantics (Q5): once a rule claims pieces, no other rule
// sees them. Active claims survive as long as their pieces still satisfy
// the rule; deactivating them releases pieces back to the unclaimed pool.
public class SynergyEvaluator : MonoBehaviour
{
    public static SynergyEvaluator Instance;

    [Header("Rules")]
    [Tooltip("All synergy rule assets. Evaluator sorts by .priority each pass.")]
    public List<SynergyRule> rules = new();

    [Header("Debug")]
    public bool verboseLogging = false;

    readonly BoardSnapshot              _board      = new();
    readonly List<ActiveSynergy>        _actives    = new();
    readonly Dictionary<int, PlacedPiece> _byId     = new();
    int _nextId;

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

    public void ResetForNewRun()
    {
        RevokeAll();
        _board.Clear();
        _byId.Clear();
        _actives.Clear();
        _nextId = 0;

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
                SafeRevoke(rule, oldTier, game);
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

            // Only try absorbing when this rule's pool actually changed.
            if (needCheck && rule.absorbAdditionalPieces)
                TryAbsorb(active, game);
        }

        // ── Pass 2: try to activate inactive rules ──────────────────────
        var inactive = CollectInactiveSorted();
        for (int i = 0; i < inactive.Count; i++)
        {
            var rule = inactive[i];

            // Skip rules whose relevant pool hasn't changed since last try.
            // (If a previous attempt already returned false, nothing new is
            // there to satisfy it.)
            int curVer = _board.VersionFor(rule.color);
            if (rule.LastEvalVersion == curVer) continue;
            rule.LastEvalVersion = curVer;

            var unclaimed = ComputeUnclaimed();
            if (unclaimed.Count == 0) break;

            if (rule.TryEvaluate(_board, unclaimed, out var claim, out var tier) && claim != null && claim.Count > 0)
            {
                var newActive = new ActiveSynergy(rule, claim, tier);
                _actives.Add(newActive);
                SafeApply(rule, tier, game);
                if (verboseLogging) Debug.Log($"[Synergy] ✓ {rule.displayName} activated at tier {tier} ({claim.Count} pieces)");
                OnTierChanged?.Invoke(rule, 0, tier);
                OnClaimChanged?.Invoke(rule, newActive);
            }
        }
    }

    // Try to grow an active claim by re-evaluating against (claim ∪ unclaimed).
    // Commits when the result is strictly bigger, higher tier, OR a different
    // set of pieces (rule may have shifted its "best fit" to a different
    // configuration of the same count — filter-state-dependent visualizers
    // need to refresh in that case).
    void TryAbsorb(ActiveSynergy active, GameFlowManager game)
    {
        var extended = new HashSet<PlacedPiece>(active.claimedPieces);
        foreach (var p in ComputeUnclaimed()) extended.Add(p);

        if (!active.rule.TryEvaluate(_board, extended, out var newClaim, out var newTier)) return;
        if (newClaim == null) return;

        bool grew    = newClaim.Count > active.claimedPieces.Count;
        bool leveled = newTier > active.tier;
        // For ICellHighlightFilter rules, also re-commit when the claimed SET
        // changes at the same count — the cube origin may have moved to a
        // different valid position, which changes which cells should glow
        // even though the piece count stays the same.
        bool setMoved = active.rule is ICellHighlightFilter
                     && !newClaim.SetEquals(active.claimedPieces);
        if (!grew && !leveled && !setMoved) return;

        int oldTier = active.tier;
        SafeRevoke(active.rule, oldTier, game);
        active.claimedPieces = newClaim;
        active.tier          = newTier;
        SafeApply(active.rule, newTier, game);

        if (verboseLogging)
            Debug.Log($"[Synergy] ↑ {active.rule.displayName} absorbed → tier {oldTier}→{newTier}, {newClaim.Count} pieces");

        if (oldTier != newTier)
            OnTierChanged?.Invoke(active.rule, oldTier, newTier);

        // Always fire for absorb — claimed pieces changed even if tier didn't.
        OnClaimChanged?.Invoke(active.rule, active);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    HashSet<PlacedPiece> ComputeUnclaimed()
    {
        var u = new HashSet<PlacedPiece>(_board.AllPieces);
        for (int i = 0; i < _actives.Count; i++)
            u.ExceptWith(_actives[i].claimedPieces);
        return u;
    }

    List<SynergyRule> CollectInactiveSorted()
    {
        var list = new List<SynergyRule>();
        for (int i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            if (r == null) continue;
            if (IsActive(r)) continue;
            list.Add(r);
        }
        list.Sort((a, b) => b.priority.CompareTo(a.priority));
        return list;
    }

    bool IsActive(SynergyRule r)
    {
        for (int i = 0; i < _actives.Count; i++)
            if (_actives[i].rule == r) return true;
        return false;
    }

    void RevokeAll()
    {
        var game = GameFlowManager.Instance;
        for (int i = 0; i < _actives.Count; i++)
        {
            var a = _actives[i];
            SafeRevoke(a.rule, a.tier, game);
        }
        _actives.Clear();
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
