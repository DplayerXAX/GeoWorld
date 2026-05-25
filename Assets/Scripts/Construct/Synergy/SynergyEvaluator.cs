using System;
using System.Collections.Generic;
using UnityEngine;

// Tracks placed-block counts per tag and activates SynergyDefinitions when
// thresholds are crossed.
//
// Integration contract:
//   PlacementController calls:
//     SynergyEvaluator.Instance.OnBlockPlaced(blockData);
//     SynergyEvaluator.Instance.OnBlockRemoved(blockData);
//
//   These are the only mutation entry points. Anything else (turret-only
//   sets, path-aware counts, shape patterns) should live as a separate
//   evaluator or sub-component — keep this one focused on tag counts.
//
// Resolution policy:
//   • At most one tier active per synergy at a time.
//   • When the active tier changes, the old tier's effect is Revoke'd and
//     the new tier's is Apply'd. This is the only place GameEffect lifecycle
//     is driven for synergies.
public class SynergyEvaluator : MonoBehaviour
{
    public static SynergyEvaluator Instance;

    [Header("Pool")]
    [Tooltip("All synergies in the game. Filtered at evaluation time by which tags actually have placed blocks.")]
    public List<SynergyDefinition> synergies = new();

    // Per-tag count of placed blocks.
    readonly Dictionary<BlockTag, int> _counts = new();

    // Currently-active tier for each synergy. Null = no tier active.
    readonly Dictionary<SynergyDefinition, SynergyTier> _activeTiers = new();

    public event Action<SynergyDefinition, SynergyTier, SynergyTier> OnTierChanged;
        // (synergy, oldTier, newTier) — either may be null. Fired AFTER Revoke/Apply.

    void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    // ── Block placement events ─────────────────────────────────────────

    public void OnBlockPlaced(BlockData data)
    {
        if (data == null || data.tags == null) return;
        for (int i = 0; i < data.tags.Count; i++)
            AdjustTagCount(data.tags[i], +1);
        Recompute();
    }

    public void OnBlockRemoved(BlockData data)
    {
        if (data == null || data.tags == null) return;
        for (int i = 0; i < data.tags.Count; i++)
            AdjustTagCount(data.tags[i], -1);
        Recompute();
    }

    void AdjustTagCount(BlockTag tag, int delta)
    {
        _counts.TryGetValue(tag, out int prev);
        int next = Mathf.Max(0, prev + delta);
        _counts[tag] = next;
    }

    // ── Public reads (HUD / tooltips) ──────────────────────────────────

    public int Count(BlockTag tag)
    {
        _counts.TryGetValue(tag, out int n);
        return n;
    }

    public SynergyTier ActiveTier(SynergyDefinition s)
    {
        if (s == null) return null;
        _activeTiers.TryGetValue(s, out var t);
        return t;
    }

    // ── Re-resolve all synergies, dispatching Revoke / Apply on changes ──

    void Recompute()
    {
        if (synergies == null) return;

        var gfm = GameFlowManager.Instance;
        for (int i = 0; i < synergies.Count; i++)
        {
            var syn = synergies[i];
            if (syn == null) continue;

            int count = Count(syn.tag);
            var newTier = syn.ResolveActive(count);
            _activeTiers.TryGetValue(syn, out var oldTier);

            if (newTier == oldTier) continue;

            try { oldTier?.effect?.Revoke(gfm); }
            catch (Exception e) { Debug.LogError($"[Synergy] Revoke threw for {syn.DisplayLabel}: {e}"); }

            try { newTier?.effect?.Apply(gfm); }
            catch (Exception e) { Debug.LogError($"[Synergy] Apply threw for {syn.DisplayLabel}: {e}"); }

            if (newTier != null) _activeTiers[syn] = newTier;
            else                 _activeTiers.Remove(syn);

            OnTierChanged?.Invoke(syn, oldTier, newTier);
        }
    }

    // Called by GameFlowManager.RestartGame so synergies start clean each run.
    public void ResetForNewRun()
    {
        var gfm = GameFlowManager.Instance;

        // Revoke any active tier before wiping state.
        foreach (var kv in _activeTiers)
        {
            try { kv.Value?.effect?.Revoke(gfm); }
            catch (Exception e) { Debug.LogError($"[Synergy] Revoke threw during reset: {e}"); }
        }

        _activeTiers.Clear();
        _counts.Clear();
    }
}
