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
