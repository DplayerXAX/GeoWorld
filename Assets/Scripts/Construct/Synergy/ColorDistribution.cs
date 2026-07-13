using System;
using System.Collections.Generic;
using UnityEngine;

// Per-token color assignment policy.
//
// Supports two design modes (switchable in inspector):
//
//   B. Pure weighted distribution
//      • useRoundPool = false
//      • Every token sampled independently from `weights`
//      • Simplest, most stable per-round variety
//
//   C. Per-round subset pool
//      • useRoundPool = true
//      • At round start, sample `poolSize` colors (weighted-without-
//        replacement) into the active pool; tokens for that round draw
//        only from that subset
//      • Lets some rounds emphasize specific themes ("orange-heavy round
//        → build for Abundance"), more roguelike texture
//
// Call BeginRound(rng) at the start of every Build phase to refresh the
// pool (no-op in B mode). Call Pick(rng) once per token.
//
// All randomness goes through the caller-supplied Xoshiro256StarStar
// (GameFlowManager.Instance.Rng) so the color sequence is reproducible
// from the run seed.
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Color Distribution",
                 fileName = "ColorDistribution")]
public class ColorDistribution : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public BlockColor color;
        [Min(0f)] public float weight;
    }

    [Header("Per-color base weights")]
    [Tooltip("Relative pick weight. Set to 0 to exclude entirely. Default seed: 6 themes equal + Universal at 0.3 (10% in 7-entry pool).")]
    public Entry[] weights = {
        new() { color = BlockColor.Order,         weight = 1.0f },
        new() { color = BlockColor.Harmony,       weight = 1.0f },
        new() { color = BlockColor.Abundance,     weight = 1.0f },
        new() { color = BlockColor.Heresy,        weight = 1.0f },
        new() { color = BlockColor.Enlightenment, weight = 1.0f },
        new() { color = BlockColor.Exploration,   weight = 1.0f },
        new() { color = BlockColor.Universal,     weight = 0.3f },
    };

    [Header("Round pool (mode C)")]
    [Tooltip("If true, each round samples a subset of colors from `weights` and tokens that round only draw from the subset. If false (mode B), every token uses the full `weights` list.")]
    public bool useRoundPool = false;

    [Tooltip("How many colors to include in the per-round subset.")]
    [Range(2, 8)] public int poolSize = 3;

    [Tooltip("If true, Universal is always added to the round pool (doesn't count against poolSize). Recommended on so jokers are always available.")]
    public bool alwaysIncludeUniversal = true;

    // Current round's pool (== `weights` in B mode, subset in C mode).
    Entry[] _active;

    // This round's LEVEL-fixed restriction (LevelDefinition.allowedColors, passed
    // in by BeginRound) — null/empty means no restriction. Kept separate from
    // `_active`/round-pool sampling (mode C) so the two filters compose cleanly:
    // round-pool sampling only ever draws from colors the level allows.
    HashSet<BlockColor> _allowedThisLevel;

    public IReadOnlyList<Entry> ActivePool => _active ?? weights;

    // Call at start of each Build phase so C mode can refresh its subset.
    // `allowedColors` is the CURRENT LEVEL's fixed pool (LevelDefinition.allowedColors) —
    // null/empty means no restriction. Re-passed every call (not sticky) so a level
    // change always takes effect immediately, no stale state across scenes/runs.
    public void BeginRound(Xoshiro256StarStar rng, IReadOnlyCollection<BlockColor> allowedColors = null)
    {
        _allowedThisLevel = (allowedColors != null && allowedColors.Count > 0)
            ? new HashSet<BlockColor>(allowedColors) : null;

        if (!useRoundPool || rng == null)
        {
            _active = weights;
            return;
        }

        var available = new List<Entry>(weights.Length);
        for (int i = 0; i < weights.Length; i++)
            if (weights[i].weight > 0f && IsAllowed(weights[i].color)) available.Add(weights[i]);

        var chosen = new List<Entry>(poolSize + 1);

        // Always-include Universal: pull it out first if requested.
        if (alwaysIncludeUniversal)
        {
            for (int i = available.Count - 1; i >= 0; i--)
            {
                if (available[i].color == BlockColor.Universal)
                {
                    chosen.Add(available[i]);
                    available.RemoveAt(i);
                    break;
                }
            }
        }

        // Sample `poolSize` non-Universal colors weighted-without-replacement.
        // Universal (if alwaysIncludeUniversal) is in addition to this count.
        for (int i = 0; i < poolSize && available.Count > 0; i++)
        {
            float total = 0f;
            for (int j = 0; j < available.Count; j++) total += available[j].weight;
            if (total <= 0f) break;

            float r = rng.NextFloat() * total;
            float acc = 0f;
            int picked = available.Count - 1;
            for (int j = 0; j < available.Count; j++)
            {
                acc += available[j].weight;
                if (r < acc) { picked = j; break; }
            }
            chosen.Add(available[picked]);
            available.RemoveAt(picked);
        }

        _active = chosen.ToArray();
    }

    // Pick one color from the active pool. Returns BlockColor.None if the
    // pool is empty / all weights are zero — caller should treat None as
    // "no synergy participation" (matches BlockColor.None semantics).
    public BlockColor Pick(Xoshiro256StarStar rng)
    {
        var pool = _active ?? weights;
        if (pool == null || pool.Length == 0 || rng == null) return BlockColor.None;

        // Colors this LEVEL doesn't allow (LevelDefinition.allowedColors, set via
        // BeginRound) are treated as weight 0 here regardless of what's authored
        // above — the single choke point every token color goes through.
        float total = 0f;
        for (int i = 0; i < pool.Length; i++)
            if (IsAllowed(pool[i].color)) total += Mathf.Max(0f, pool[i].weight);
        if (total <= 0f) return BlockColor.None;

        float r = rng.NextFloat() * total;
        float acc = 0f;
        for (int i = 0; i < pool.Length; i++)
        {
            if (!IsAllowed(pool[i].color)) continue;
            acc += Mathf.Max(0f, pool[i].weight);
            if (r < acc) return pool[i].color;
        }
        // Fallback: last allowed entry (float rounding edge case).
        for (int i = pool.Length - 1; i >= 0; i--)
            if (IsAllowed(pool[i].color)) return pool[i].color;
        return BlockColor.None;
    }

    // Universal is a joker, not a themed color — never restricted by a level's pool.
    bool IsAllowed(BlockColor c) =>
        c == BlockColor.Universal || _allowedThisLevel == null || _allowedThisLevel.Contains(c);
}
