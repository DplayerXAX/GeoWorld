using System.Collections.Generic;
using UnityEngine;

public class PlacedBlockInstance
{
    public BlockData data;
    public List<Vector3Int> occupiedCells = new();
    public GameObject visualObject;

    // Synergy theme color this piece was assigned at spawn time. Copied from
    // SelectableBlock.color (or BlockColor.None for direct/legacy placements).
    public BlockColor color = BlockColor.None;

    // Reference returned by SynergyEvaluator.OnPiecePlaced. Stashed here so
    // OnPieceRemoved can hand the exact same instance back. Null until the
    // synergy hook fires (or if no evaluator is in the scene).
    public PlacedPiece placedPiece;

    // Per-turret upgrade state. Currently used by Basic Turret's two upgrade
    // branches so each placed turret can progress independently.
    public int basicPowerUpgradeLevel;
    public int basicBurstUpgradeLevel;
}

public class GridSystem : MonoBehaviour
{
    public float cellSize = 1f;
    // Unused at runtime — the grid is unbounded. Kept as an inspector hint.
    public Vector3Int size = new Vector3Int(10, 5, 10);
    public static GridSystem instance;
    // Sparse: only occupied cells are stored. Missing keys ⇒ unoccupied.
    private Dictionary<Vector3Int, bool> occupied = new();
    private Dictionary<Vector3Int, PlacedBlockInstance> cellToInstance = new();

    void Awake()
    {
        instance = this;
        Init();
    }

    public void Init()
    {
        occupied.Clear();
        cellToInstance.Clear();
    }

    public Vector3 GridToWorld(Vector3Int gp)
    {
        return new Vector3(gp.x * cellSize + cellSize * 0.5f, gp.y * cellSize + cellSize * 0.5f, gp.z * cellSize + cellSize * 0.5f);
    }

    public Vector3Int WorldToGrid(Vector3 w)
    {
        return new Vector3Int(Mathf.FloorToInt(w.x / cellSize), Mathf.FloorToInt(w.y / cellSize), Mathf.FloorToInt(w.z / cellSize));
    }

    public bool IsOccupied(Vector3Int pos)
    {
        return occupied.TryGetValue(pos, out bool v) && v;
    }

    public void SetOccupied(Vector3Int pos)
    {
        occupied[pos] = true;
    }

    public void RegisterInstance(PlacedBlockInstance instance)
    {
        foreach (var pos in instance.occupiedCells)
        {
            occupied[pos] = true;
            cellToInstance[pos] = instance;
        }

        // Put the block's colliders on the PlacedBlock layer (if it's defined) so
        // turret line-of-sight can raycast that layer ONLY — every hit is a block,
        // no per-hit "is this collider a placed block?" scan over all instances.
        if (instance.visualObject != null && BlockLayer >= 0)
        {
            EnsureCamerasRenderLayer(BlockLayer);   // guarantee the new layer is rendered
            SetLayerRecursive(instance.visualObject, BlockLayer);
        }
    }

    // OR the block layer into every camera's culling mask once, so moving blocks
    // to a dedicated layer can never make them invisible (a camera whose mask
    // isn't "Everything" would otherwise stop rendering them).
    static bool _cameraMaskEnsured;
    static void EnsureCamerasRenderLayer(int layer)
    {
        if (_cameraMaskEnsured) return;
        _cameraMaskEnsured = true;
        int bit = 1 << layer;
        var cams = Camera.allCameras;
        for (int i = 0; i < cams.Length; i++)
            if (cams[i] != null) cams[i].cullingMask |= bit;
    }

    // Resolved once. -2 = unresolved, -1 = layer not set up (LOS falls back to the
    // old scan), >= 0 = the PlacedBlock layer index.
    static int _blockLayer = -2;
    public static int BlockLayer
    {
        get
        {
            if (_blockLayer == -2) _blockLayer = LayerMask.NameToLayer("PlacedBlock");
            return _blockLayer;
        }
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        var t = go.transform;
        for (int i = 0; i < t.childCount; i++)
            SetLayerRecursive(t.GetChild(i).gameObject, layer);
    }

    public PlacedBlockInstance GetInstanceAt(Vector3Int pos)
    {
        return cellToInstance.TryGetValue(pos, out var instance) ? instance : null;
    }

    public void RemoveInstance(PlacedBlockInstance instance)
    {
        foreach (var pos in instance.occupiedCells)
        {
            occupied.Remove(pos);
            cellToInstance.Remove(pos);
        }
        if (instance.visualObject != null) Destroy(instance.visualObject);
    }

    public BlockData GetBlock(Vector3Int pos)
    {
        return cellToInstance.TryGetValue(pos, out var inst) ? inst.data : null;
    }

    public Dictionary<Vector3Int, bool> GetGrid() => occupied;

    // 26-neighbor (face / edge / corner) adjacency. Cells within newCells
    // don't count as neighbors of each other.
    public bool HasOccupiedNeighbor26(IList<Vector3Int> newCells)
    {
        if (newCells == null || newCells.Count == 0) return false;

        var newSet = new HashSet<Vector3Int>(newCells);
        foreach (var c in newCells)
        {
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dy == 0 && dz == 0) continue;
                var n = new Vector3Int(c.x + dx, c.y + dy, c.z + dz);
                if (newSet.Contains(n)) continue;
                if (IsOccupied(n)) return true;
            }
        }
        return false;
    }

    /// <summary>Returns every distinct placed instance currently on the grid.</summary>
    public List<PlacedBlockInstance> GetAllInstances()
    {
        var seen   = new HashSet<PlacedBlockInstance>();
        var result = new List<PlacedBlockInstance>();
        foreach (var ins in cellToInstance.Values)
            if (ins != null && seen.Add(ins))
                result.Add(ins);
        return result;
    }
}
