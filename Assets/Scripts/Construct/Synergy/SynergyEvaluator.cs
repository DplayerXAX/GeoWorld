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

        // Scrub the removed piece from every active claim so IsStillSatisfied
        // doesn't see a stale reference.
        for (int i = 0; i < _actives.Count; i++)
            _actives[i].claimedPieces.Remove(piece);

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

            if (!rule.IsStillSatisfied(_board, active.claimedPieces))
            {
                int oldTier = active.tier;
                SafeRevoke(rule, oldTier, game);
                _actives.RemoveAt(i);
                if (verboseLogging) Debug.Log($"[Synergy] ✗ {rule.displayName} deactivated");
                OnTierChanged?.Invoke(rule, oldTier, 0);
                OnClaimChanged?.Invoke(rule, null);
                continue;
            }

            if (rule.absorbAdditionalPieces)
                TryAbsorb(active, game);
        }

        // ── Pass 2: try to activate inactive rules ──────────────────────
        var inactive = CollectInactiveSorted();
        for (int i = 0; i < inactive.Count; i++)
        {
            var rule = inactive[i];
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
    // Only commit if the new result is strictly bigger or at a higher tier.
    void TryAbsorb(ActiveSynergy active, GameFlowManager game)
    {
        var extended = new HashSet<PlacedPiece>(active.claimedPieces);
        foreach (var p in ComputeUnclaimed()) extended.Add(p);

        if (!active.rule.TryEvaluate(_board, extended, out var newClaim, out var newTier)) return;
        if (newClaim == null) return;

        bool grew    = newClaim.Count > active.claimedPieces.Count;
        bool leveled = newTier > active.tier;
        if (!grew && !leveled) return;

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
