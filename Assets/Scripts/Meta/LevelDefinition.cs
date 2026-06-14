using System.Collections.Generic;
using UnityEngine;

// One playable level. Authored as an asset and referenced by LevelNode on the
// level-select map. Reuses the existing wave / pacing systems — GameFlowManager
// applies these on Start when RunConfig.Mode == Level.
[CreateAssetMenu(menuName = "GeoWorld/Level", fileName = "Level")]
public class LevelDefinition : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Stable key used in the save file (unlocks / records). Don't rename casually.")]
    public string levelId;
    public string displayName;
    [TextArea] public string description;

    [Header("Run setup")]
    [Tooltip("Fixed seed so the level plays the same each attempt. 0 = randomize.")]
    public ulong runSeed;
    [Min(1)] public int blocksPerTurn  = 8;
    [Min(0)] public int turretsPerTurn = 3;

    [Tooltip("Authored waves (fed into GameFlowManager.waves). Leave empty to use the procedural generator.")]
    public List<WaveDefinition> waves = new();

    [Header("Victory")]
    [Tooltip("Waves the player must survive to clear the level. 0 = endless within this level.")]
    [Min(0)] public int wavesToClear = 5;

    [Header("Progression")]
    [Tooltip("This level starts unlocked (e.g. the first level).")]
    public bool unlockedByDefault;

    [Tooltip("Levels unlocked when this one is first cleared.")]
    public LevelDefinition[] unlocks;

    [Tooltip("Tech points granted on first clear.")]
    [Min(0)] public int techReward = 1;
}
