using System;
using System.Collections.Generic;
using UnityEngine;

// Procedural roguelite wave builder.
//
// Each round it builds a List<SpawnGroup> by spending a per-round budget on
// entries drawn from the pool by weighted-random. Authored WaveDefinition
// assets can still be used directly for tutorial / boss / scripted rounds —
// see GameFlowManager.PickWaveForThisRound.
//
// `SpawnEntry` holds wave-system metadata about how to USE an enemy prefab
// (cost / weight / unlock round / group shape) — NOT what the enemy IS.
// The enemy prefab itself is owned by whoever maintains EnemySurfaceUnit.
// Drag a prefab into each entry to teach this generator how to spend it.
//
// All randomness goes through the Xoshiro256StarStar passed in by the caller
// (GameFlowManager._rng) so generated waves are fully deterministic from
// the run seed.
[CreateAssetMenu(menuName = "GeoWorld/Wave Generator", fileName = "WaveGenerator")]
public class WaveGenerator : ScriptableObject
{
    [Header("Pool")]
    [Tooltip("Each entry: pick a prefab and the wave-system metadata for using it. Leave prefab null to fall back to EnemyBaseManager's default dark orb.")]
    public List<SpawnEntry> entries = new();

    [Header("Budget curve")]
    [Tooltip("Budget at round 0.")]
    [Min(0f)] public float baseBudget = 6f;

    [Tooltip("Extra budget added per round.")]
    [Min(0f)] public float growthPerRound = 4f;

    [Tooltip("±this fraction of total budget, applied multiplicatively (0.15 → ±15%).")]
    [Range(0f, 0.5f)] public float variance = 0.15f;

    [Tooltip("Hard cap so late-game budgets don't explode.")]
    [Min(0f)] public float maxBudget = 80f;

    [Header("Pacing")]
    [Tooltip("Seconds between sub-groups within a generated wave.")]
    [Min(0f)] public float groupSpacing = 1.5f;

    [Tooltip("Allow buying an entry whose cost slightly exceeds the remaining budget. 1.0 = strict, 1.5 = up to 50% overshoot.")]
    [Range(1f, 2f)] public float affordSlack = 1.2f;

    // Returns a fresh list of SpawnGroups for the given round.
    // Safe to pass to EnemyBaseManager.BeginWave.
    public List<SpawnGroup> Generate(int round, Xoshiro256StarStar rng)
    {
        var groups = new List<SpawnGroup>();
        if (rng == null || entries == null || entries.Count == 0) return groups;

        // ── Compute total budget for this wave ─────────────────────────
        float raw  = baseBudget + growthPerRound * round;
        float jit  = 1f + (rng.NextFloat() * 2f - 1f) * variance;
        float budget = Mathf.Min(raw * jit, maxBudget);

        // ── Filter entries eligible for this round ─────────────────────
        var pool = new List<SpawnEntry>();
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e != null && e.weight > 0f && round >= e.minRound)
                pool.Add(e);
        }
        if (pool.Count == 0) return groups;

        // ── Spend budget ───────────────────────────────────────────────
        bool first = true;
        int safety = 32;   // hard cap against pathological loops

        while (budget > 0f && safety-- > 0)
        {
            // Affordable subset
            float ceiling = budget * affordSlack;
            float totalW = 0f;
            for (int i = 0; i < pool.Count; i++)
                if (pool[i].cost <= ceiling) totalW += pool[i].weight;
            if (totalW <= 0f) break;

            // Weighted pick
            float r = rng.NextFloat() * totalW;
            float acc = 0f;
            SpawnEntry picked = null;
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i].cost > ceiling) continue;
                acc += pool[i].weight;
                if (r < acc) { picked = pool[i]; break; }
            }
            if (picked == null) break;

            int size = rng.NextIntInclusive(
                Mathf.Max(1, picked.groupSizeMin),
                Mathf.Max(picked.groupSizeMin, picked.groupSizeMax));

            // Pre-flight clamp so the full group doesn't massively overshoot.
            int maxAffordableSize = Mathf.FloorToInt(ceiling / Mathf.Max(1, picked.cost));
            if (maxAffordableSize > 0 && size > maxAffordableSize) size = maxAffordableSize;
            if (size <= 0) break;

            groups.Add(new SpawnGroup
            {
                prefab   = picked.prefab,
                count    = size,
                interval = picked.interval,
                preDelay = first ? 0f : groupSpacing,
            });

            budget -= picked.cost * size;
            first   = false;
        }

        return groups;
    }
}

// Wave-system metadata about how to use a single enemy prefab.
// Lives inside the WaveGenerator asset (not as a separate file / SO) so the
// enemy folder stays untouched — this is purely about wave construction.
[Serializable]
public class SpawnEntry
{
    [Tooltip("Enemy prefab to spawn. Null falls back to EnemyBaseManager's default dark orb.")]
    public EnemySurfaceUnit prefab;

    [Header("Budget")]
    [Tooltip("Budget cost per enemy. Group size × cost is deducted from the wave budget.")]
    [Min(1)] public int cost = 2;

    [Tooltip("First round (inclusive) at which this entry may be picked.")]
    [Min(0)] public int minRound = 0;

    [Tooltip("Relative selection probability among eligible entries. 0 = disabled.")]
    [Min(0f)] public float weight = 1f;

    [Header("Group shape")]
    [Tooltip("Group size for one selection is a random int in [min, max].")]
    [Min(1)] public int groupSizeMin = 3;
    [Min(1)] public int groupSizeMax = 6;

    [Tooltip("Seconds between spawns within the group. Use multiples of 60/BPM for on-beat feel.")]
    [Min(0f)] public float interval = 0.5f;
}
