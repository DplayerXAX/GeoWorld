using JetBrains.Annotations;
using System.Collections.Generic;
using UnityEngine;

public class SurfaceGraphBuilder
{
    public Dictionary<Vector3Int, List<FaceNode>> cellFaces = new();
    public List<FaceNode> allFaces = new();
    GridSystem gridSystem;
    float cachedSize;

    public void SetData(GridSystem grid)
    {
        this.gridSystem = grid;
        cachedSize = grid.cellSize;
    }

    public void Build()
    {
        var blocks = gridSystem.GetGrid();
        Debug.Log("Start building!");
        if (blocks == null)
        {
            Debug.LogError("No block data");
            return;
        }

        allFaces.Clear();
        cellFaces.Clear();

        foreach (var kv in blocks)
        {
            if (!kv.Value) continue;

            var faces = FaceBuilder.BuildFaces(kv.Key, gridSystem.cellSize);

            cellFaces[kv.Key] = faces;
            allFaces.AddRange(faces);
        }

        BuildNeighbors();
    }

    void BuildNeighbors()
    {
        foreach (var a in allFaces)
        {
            foreach (var b in allFaces)
            {
                if (a == b) continue;

                if (IsNeighbor(a, b))
                {
                    a.neighbors.Add(b);
                }
            }
        }
    }
    public FaceNode GetFaceNode(Vector3Int cell)
    {
        if (!cellFaces.ContainsKey(cell)) 
        {
            Debug.Log("Cant find!");
            return null;
        }

        return cellFaces[cell][0];
    }

    bool IsNeighbor(FaceNode a, FaceNode b)
    {
        return Vector3.Distance(a.worldPos, b.worldPos) < cachedSize * 1.1f;
    }
}