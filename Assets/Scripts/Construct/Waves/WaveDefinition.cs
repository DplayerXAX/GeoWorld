using System;
using System.Collections.Generic;
using UnityEngine;

// Data-driven wave description. Each wave is a sequence of SpawnGroups —
// each group can have its own enemy prefab, count, spacing, and lead-in
// delay so a single wave can read as "8 runners → pause → 3 tanks".
//
// EnemyBaseManager.BeginWave(path, wave) iterates these groups in order.
// If `prefab` is null on a group, the manager falls back to its default
// enemyPrefab (or the procedural dark orb).
//
// Interval guidance for the music-locked feel: since enemy movement is
// BPM-locked (EnemyBaseManager.enemyBpm), prefer interval values that are
// integer multiples of 60/bpm so spawns land on the beat.
//   • 60/bpm   → one per beat   (default cadence)
//   • 30/bpm   → one per half-beat (swarm)
//   • 120/bpm  → one per two beats (heavy)
[CreateAssetMenu(menuName = "GeoWorld/Wave Definition", fileName = "Wave")]
public class WaveDefinition : ScriptableObject
{
    [Tooltip("Optional human-readable label shown in logs / UI.")]
    public string displayName;

    public List<SpawnGroup> groups = new();
    [Tooltip("Manual designer override — set this yourself if you want a specific number regardless of the auto-estimate below.")]
    public int difficulty;

    [Header("Auto-computed (recalculated whenever a field above changes — don't hand-edit)")]
    [Tooltip("Mass^0.7 × Rate^0.5, where Mass = total enemy HP and Rate = Mass / spawn window (seconds). " +
             "Mirrors EstimatedDifficulty below — kept as a serialized field purely so it's visible in the Inspector.")]
    public float estimatedDifficulty;

    public int TotalSpawnCount
    {
        get
        {
            int total = 0;
            if (groups != null)
                foreach (var g in groups)
                    if (g != null) total += Mathf.Max(0, g.count);
            return total;
        }
    }

    // Difficulty estimate from raw wave shape: how much total enemy HP arrives (Mass),
    // and how densely-packed in time it is (Rate = Mass / spawn window). Both terms use
    // sub-linear exponents so neither pure quantity nor pure tempo can blow the score up
    // on their own — matches how AOE/turret-coverage actually damps raw swarm size.
    public float EstimatedDifficulty
    {
        get
        {
            if (groups == null) return 0f;
            float mass = 0f, window = 0f;
            foreach (var g in groups)
            {
                if (g == null || g.count <= 0) continue;
                int hp = g.prefab != null ? g.prefab.maxHealth : 1;   // no prefab override → assume 1
                mass   += g.count * hp;
                window += g.preDelay + g.interval * Mathf.Max(0, g.count - 1);
            }
            float rate = mass / Mathf.Max(window, 0.01f);
            return Mathf.Pow(mass, 0.7f) * Mathf.Pow(rate, 0.5f);
        }
    }

#if UNITY_EDITOR
    // Fires on every Inspector edit — keeps the visible estimatedDifficulty field in
    // sync with the live EstimatedDifficulty property (same pattern BlockData.cs uses
    // to auto-fill `cells` from `blockShape`).
    void OnValidate()
    {
        estimatedDifficulty = EstimatedDifficulty;
    }
#endif
}

[Serializable]
public class SpawnGroup
{
    [Tooltip("Optional override. If null, EnemyBaseManager.enemyPrefab (or the procedural dark orb) is used.")]
    public EnemySurfaceUnit prefab;

    [Min(0)] public int count = 5;

    [Tooltip("Seconds between spawns inside this group. Use multiples of 60/BPM for on-beat feel.")]
    [Min(0f)] public float interval = 0.5f;

    [Tooltip("Seconds to wait before the first spawn of this group. Lets you stagger sub-groups within a wave.")]
    [Min(0f)] public float preDelay = 0f;
}
