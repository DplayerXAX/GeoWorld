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

    public IReadOnlyList<Entry> ActivePool => _active ?? weights;

    // Call at start of each Build phase so C mode can refresh its subset.
    // No-op in B mode (just snapshots `weights`).
    public void BeginRound(Xoshiro256StarStar rng)
    {
        if (!useRoundPool || rng == null)
        {
            _active = weights;
            return;
        }

        var available = new List<Entry>(weights.Length);
        for (int i = 0; i < weights.Length; i++)
            if (weights[i].weight > 0f) available.Add(weights[i]);

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

        float total = 0f;
        for (int i = 0; i < pool.Length; i++) total += Mathf.Max(0f, pool[i].weight);
        if (total <= 0f) return BlockColor.None;

        float r = rng.NextFloat() * total;
        float acc = 0f;
        for (int i = 0; i < pool.Length; i++)
        {
            acc += Mathf.Max(0f, pool[i].weight);
            if (r < acc) return pool[i].color;
        }
        return pool[pool.Length - 1].color;
    }

}
