using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Runtime half of RandomBlockDestructionMechanicConfig. It creates itself only
// for levels that opt into the mechanic, so gameplay scenes need no wiring.
[DisallowMultipleComponent]
public class RandomBlockDestructionController : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TrySpawn();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TrySpawn();

    static void TrySpawn()
    {
        if (RunConfig.Mode != GameMode.Level || RunConfig.Level == null) return;
        if (RunConfig.Level.GetMechanic<RandomBlockDestructionMechanicConfig>() == null) return;
        if (PlacementController.Instance == null) return;   // gameplay scene only
        if (FindFirstObjectByType<RandomBlockDestructionController>() != null) return;
        new GameObject("RandomBlockDestructionController")
            .AddComponent<RandomBlockDestructionController>();
    }

    LevelDefinition _level;
    RandomBlockDestructionMechanicConfig _config;
    Xoshiro256StarStar _rng;
    int _lastTriggeredAfterTurn = -1;

    void Start()
    {
        _level  = RunConfig.Level;
        _config = _level != null
            ? _level.GetMechanic<RandomBlockDestructionMechanicConfig>()
            : null;
        if (_config == null) { Destroy(gameObject); return; }

        GameFlowManager.OnTurnStarted += HandleTurnStarted;
    }

    void OnDestroy()
    {
        GameFlowManager.OnTurnStarted -= HandleTurnStarted;
    }

    void HandleTurnStarted()
    {
        var flow      = GameFlowManager.Instance;
        var placement = PlacementController.Instance;
        var grid      = GridSystem.instance;
        if (flow == null || placement == null || grid == null) return;

        int completed = flow.WavesCleared;
        int first     = Mathf.Max(1, _config.firstTriggerAfterTurn);
        int interval  = Mathf.Max(1, _config.turnInterval);
        if (completed < first || (completed - first) % interval != 0) return;
        if (_lastTriggeredAfterTurn == completed) return;   // defensive: StartTurn should fire once
        _lastTriggeredAfterTurn = completed;

        var candidates = new List<PlacedBlockInstance>();
        foreach (var instance in grid.GetAllInstances())
        {
            if (!CanDestroy(instance, grid)) continue;
            if (!_config.canDestroyTurrets && TurretTypes.Is(instance.data.blockType))
                continue;
            candidates.Add(instance);
        }
        if (candidates.Count == 0) return;

        // Dictionary iteration order is not a gameplay contract. Sort first so a
        // fixed run seed produces the same victim on every machine and playthrough.
        candidates.Sort(CompareByGridCell);
        EnsureRng(flow);

        var victim = candidates[_rng.NextIntInclusive(0, candidates.Count - 1)];
        string blockName = victim.data != null ? victim.data.DisplayName : "block";

        var removedCells = victim.occupiedCells.ToArray();
        placement.ClearBoardSelection();
        ResourceManager.Instance?.OnBlockRemoved(victim.data.blockType);
        SynergyEvaluator.Instance?.OnPieceRemoved(victim.placedPiece);
        PathFlowManager.Instance?.RemoveFlowsOverlapping(removedCells);
        LoopManager.Instance?.RemoveLoopsOverlapping(removedCells);
        grid.RemoveInstance(victim);
        flow.EvaluateGrid();

        placement.ShowPlacementPopup($"Instability destroyed {blockName}.", 2.5f);
    }

    bool CanDestroy(PlacedBlockInstance instance, GridSystem grid)
    {
        if (instance == null || instance.data == null || instance.visualObject == null
            || instance.locked || instance.occupiedCells == null
            || instance.occupiedCells.Count == 0)
            return false;

        // Reject stale instances that have already been lifted or destroyed.
        bool registered = false;
        foreach (var cell in instance.occupiedCells)
        {
            if (grid.GetInstanceAt(cell) != instance) continue;
            registered = true;
            break;
        }
        if (!registered) return false;

        return !_config.preserveTurretSupport || !WouldOrphanTurret(instance, grid);
    }

    // Same structural invariant as player pickup/sell: removing a foundation
    // must not leave some other turret with no occupied 26-neighbour.
    static bool WouldOrphanTurret(PlacedBlockInstance toRemove, GridSystem grid)
    {
        foreach (var other in grid.GetAllInstances())
        {
            if (other == null || other == toRemove || other.data == null) continue;
            if (!TurretTypes.Is(other.data.blockType)) continue;
            if (!HasExternalSupport(other, toRemove.occupiedCells, grid)) return true;
        }
        return false;
    }

    static bool HasExternalSupport(PlacedBlockInstance instance,
                                   IList<Vector3Int> excludedCells,
                                   GridSystem grid)
    {
        foreach (var cell in instance.occupiedCells)
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dz = -1; dz <= 1; dz++)
        {
            if (dx == 0 && dy == 0 && dz == 0) continue;
            var neighbour = new Vector3Int(cell.x + dx, cell.y + dy, cell.z + dz);
            if (!grid.IsOccupied(neighbour)) continue;
            if (excludedCells.Contains(neighbour)) continue;
            if (instance.occupiedCells.Contains(neighbour)) continue;
            return true;
        }
        return false;
    }

    void EnsureRng(GameFlowManager flow)
    {
        if (_rng != null) return;

        // Accessing Rng resolves a randomized run seed when the level asset uses
        // runSeed == 0. The mechanic then gets its own salted stream and never
        // perturbs wave, shop, or synergy rolls.
        _ = flow.Rng;
        _rng = new Xoshiro256StarStar(flow.runSeed ^ 0x434F4C4C41505345UL); // "COLLAPSE"
    }

    static int CompareByGridCell(PlacedBlockInstance a, PlacedBlockInstance b)
    {
        Vector3Int ca = SmallestCell(a);
        Vector3Int cb = SmallestCell(b);
        int x = ca.x.CompareTo(cb.x);
        if (x != 0) return x;
        int y = ca.y.CompareTo(cb.y);
        return y != 0 ? y : ca.z.CompareTo(cb.z);
    }

    static Vector3Int SmallestCell(PlacedBlockInstance instance)
    {
        var best = instance.occupiedCells[0];
        for (int i = 1; i < instance.occupiedCells.Count; i++)
        {
            var cell = instance.occupiedCells[i];
            if (cell.x < best.x
                || (cell.x == best.x && cell.y < best.y)
                || (cell.x == best.x && cell.y == best.y && cell.z < best.z))
                best = cell;
        }
        return best;
    }
}
