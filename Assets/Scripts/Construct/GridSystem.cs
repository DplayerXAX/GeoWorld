using System.Collections.Generic;
using UnityEngine;

public class PlacedBlockInstance
{
    public BlockData data;
    public List<Vector3Int> occupiedCells = new();
    public GameObject visualObject;
}

public class GridSystem : MonoBehaviour
{
    public float cellSize = 1f;
    public Vector3Int size = new Vector3Int(10, 5, 10);
    public static GridSystem instance;
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
        for (int x = 0; x < size.x; x++)
            for (int y = 0; y < size.y; y++)
                for (int z = 0; z < size.z; z++)
                    occupied[new Vector3Int(x, y, z)] = false;
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
        return occupied.ContainsKey(pos) && occupied[pos];
    }

    public void SetOccupied(Vector3Int pos)
    {
        if (occupied.ContainsKey(pos)) occupied[pos] = true;
    }

    public void RegisterInstance(PlacedBlockInstance instance)
    {
        foreach (var pos in instance.occupiedCells)
        {
            occupied[pos] = true;
            cellToInstance[pos] = instance;
        }
    }

    public PlacedBlockInstance GetInstanceAt(Vector3Int pos)
    {
        return cellToInstance.TryGetValue(pos, out var instance) ? instance : null;
    }

    public void RemoveInstance(PlacedBlockInstance instance)
    {
        foreach (var pos in instance.occupiedCells)
        {
            occupied[pos] = false;
            cellToInstance.Remove(pos);
        }
        if (instance.visualObject != null) Destroy(instance.visualObject);
    }

    public BlockData GetBlock(Vector3Int pos)
    {
        return cellToInstance.TryGetValue(pos, out var inst) ? inst.data : null;
    }

    public Dictionary<Vector3Int, bool> GetGrid() => occupied;

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