using UnityEngine;

// Saboteur enemy: the first block it walks over that ISN'T already sealed (or
// level-locked) gets sealed — the player can't pick it up or delete it any more.
// One-shot per enemy; it keeps walking and keeps looking until it finds one.
// Add alongside an EnemySurfaceUnit (same pattern as EnemySplitOnAlive).
//
// A sealed block can still be SOLD at the usual 50% refund. That's deliberate:
// pickup would be a free reposition (no punishment at all), and delete is
// undoable (Ctrl+Z would launder the seal away), but forbidding removal outright
// could let one bad seal permanently break a synergy loop — or wall off the only
// route — with zero counterplay. Losing half the block's value is the cost.
[RequireComponent(typeof(EnemySurfaceUnit))]
public class EnemyBlockSealer : MonoBehaviour
{
    [Header("Seal")]
    [Tooltip("Blocks to travel before it can seal. 0 = the first block it enters is fair game.")]
    [Min(0)] public int blocksBeforeSeal = 0;
    [Tooltip("Allow sealing turret blocks too? Off = it only seals plain blocks.")]
    public bool canSealTurrets = true;

    [Header("Visual")]
    [Tooltip("Tint of the iron chains locked onto the sealed block, so the player can see which one is stuck.")]
    public Color sealColor = new Color(0.55f, 0.15f, 0.75f);

    EnemySurfaceUnit _self;
    int  _blocksSeen;
    bool _hasSealed;

    public bool HasSealed => _hasSealed;

    void Awake() => _self = GetComponent<EnemySurfaceUnit>();

    void OnEnable()
    {
        if (_self == null) _self = GetComponent<EnemySurfaceUnit>();
        if (_self != null) _self.OnBlockTraveled += HandleBlockTraveled;
    }

    void OnDisable()
    {
        if (_self != null) _self.OnBlockTraveled -= HandleBlockTraveled;
    }

    void HandleBlockTraveled(EnemySurfaceUnit enemy, int totalBlocksTraveled)
    {
        if (_hasSealed || _self == null || _self.CurrentHealth <= 0) return;

        _blocksSeen++;
        if (_blocksSeen <= blocksBeforeSeal) return;

        var cell = _self.CurrentCell;
        if (!cell.HasValue) return;

        var grid = GridSystem.instance;
        var ins  = grid != null ? grid.GetInstanceAt(cell.Value) : null;
        if (ins == null || ins.data == null) return;

        // "The first block it passes that isn't already locked" — skip anything
        // already sealed or part of the level's fixed layout and keep looking.
        if (ins.locked || ins.sealedByEnemy) return;
        if (!canSealTurrets && TurretTypes.Is(ins.data.blockType)) return;

        ins.sealedByEnemy = true;
        _hasSealed = true;
        MarkSealed(ins);
    }

    // Chains stay until the block itself is sold/destroyed (they're parented to it).
    void MarkSealed(PlacedBlockInstance ins)
    {
        if (ins.visualObject == null) return;
        float cs = GridSystem.instance != null ? GridSystem.instance.cellSize : 1f;
        SealedBlockChains.Attach(ins.visualObject.transform, cs, sealColor);
    }
}
