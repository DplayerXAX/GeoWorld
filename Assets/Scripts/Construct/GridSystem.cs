using System.Collections.Generic;
using UnityEngine;

public class GridSystem : MonoBehaviour
{
    public float cellSize = 1f;
    public Vector3Int size = new Vector3Int(10, 5, 10);
    public static GridSystem instance;
    private Dictionary<Vector3Int, bool> occupied = new();
    private Dictionary<Vector3Int, BlockData> cellToBlock = new();

    void Awake()
    {
        Init();
        instance = this;
    }

    public void Init()
    {
        occupied.Clear();
        cellToBlock.Clear();

        for (int x = 0; x < size.x; x++)
            for (int y = 0; y < size.y; y++)
                for (int z = 0; z < size.z; z++)
                {
                    var pos = new Vector3Int(x, y, z);
                    occupied[pos] = false;
                }
    }

    public Vector3 GridToWorld(Vector3Int gp)
    {
        return new Vector3(
            gp.x * cellSize + cellSize * 0.5f,
            gp.y * cellSize + cellSize * 0.5f,
            gp.z * cellSize + cellSize * 0.5f
        );
    }

    public Vector3Int WorldToGrid(Vector3 w)
    {
        return new Vector3Int(
            Mathf.FloorToInt(w.x / cellSize),
            Mathf.FloorToInt(w.y / cellSize),
            Mathf.FloorToInt(w.z / cellSize)
        );
    }

    public bool IsOccupied(Vector3Int pos)
    {
        return occupied.ContainsKey(pos) && occupied[pos];
    }

    public void SetOccupied(Vector3Int pos, BlockData block = null)
    {
        if (!occupied.ContainsKey(pos)) return;

        occupied[pos] = true;

        if (block != null)
            cellToBlock[pos] = block;
    }

    public void SetFree(Vector3Int pos)
    {
        if (!occupied.ContainsKey(pos)) return;

        occupied[pos] = false;
        cellToBlock.Remove(pos);
    }

    public BlockData GetBlock(Vector3Int pos)
    {
        if (cellToBlock.TryGetValue(pos, out var block))
            return block;

        return null;
    }

    public Dictionary<Vector3Int, bool> GetGrid()
    {
        return occupied;
    }
}