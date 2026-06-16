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

    [Header("Tutorial")]
    [Tooltip("Marks this level as a tutorial — TutorialDirector takes over (fixed endpoints + ghost-guided placement).")]
    public bool isTutorial;
    [Tooltip("Use the fixed start/end cells below instead of the random endpoint generator.")]
    public bool fixedEndpoints;
    public Vector3Int startCell;
    public Vector3Int endCell;
    [Tooltip("Ordered guided placements. Each shows a ghost the player must match exactly before they can place.")]
    public List<TutorialStep> tutorialSteps = new();
}

public enum TutorialStepKind
{
    Place,    // place a block so it covers `cells` (shape + position)
    Rotate,   // wait for the player to rotate the held block
    Run,      // wait for the player to start the wave (combat begins)
}

// One tutorial step. For Place: the player must place `block` at `origin` — the
// block's WHOLE shape is shown as a ghost. (Advanced: set `cellsOverride` to an
// arbitrary absolute cell set instead.) Rotate / Run steps just wait for the action.
[System.Serializable]
public class TutorialStep
{
    public TutorialStepKind kind = TutorialStepKind.Place;

    [Header("Place step")]
    [Tooltip("The block whose SHAPE the player must place — the ghost shows this whole shape.")]
    public BlockData block;
    [Tooltip("Grid cell the block's origin (its 0,0,0 cell) lands on. Ghost = (rotated) block.cells + this.")]
    public Vector3Int origin;
    [Tooltip("Required rotation in 90° turns around X / Y / Z. (0,0,0) = default. The ghost shows this orientation; the player must rotate (1/2/3) to match.")]
    public Vector3Int rotation90;
    [Tooltip("Advanced: explicit absolute cells; overrides block+origin+rotation when set.")]
    public Vector3Int[] cellsOverride;

    [TextArea] public string hint;

    // Absolute target cells = the ghost shape (rotated) AND the required placement.
    public Vector3Int[] TargetCells()
    {
        if (cellsOverride != null && cellsOverride.Length > 0) return cellsOverride;
        if (block != null && block.cells != null)
        {
            var rot = Quaternion.Euler(90f * rotation90.x, 90f * rotation90.y, 90f * rotation90.z);
            var r = new Vector3Int[block.cells.Length];
            for (int i = 0; i < block.cells.Length; i++)
                r[i] = origin + Vector3Int.RoundToInt(rot * (Vector3)block.cells[i]);
            return r;
        }
        return System.Array.Empty<Vector3Int>();
    }
}
