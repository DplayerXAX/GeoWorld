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
        allFaces.Clear();
        cellFaces.Clear();

        foreach (var kv in blocks)
        {
            if (!kv.Value) continue;

            var allCellFaces = FaceBuilder.BuildFaces(kv.Key, gridSystem.cellSize);
            var exposedFaces = new List<FaceNode>();

            foreach (var face in allCellFaces)
            {
                Vector3Int neighborCell = face.cell + Vector3Int.RoundToInt(face.normal);
                if (!gridSystem.IsOccupied(neighborCell))
                    exposedFaces.Add(face);
            }

            if (exposedFaces.Count > 0)
            {
                cellFaces[kv.Key] = exposedFaces;
                allFaces.AddRange(exposedFaces);
            }
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

    public List<FaceNode> GetFaceNodes(Vector3Int cell)
    {
        if (!cellFaces.TryGetValue(cell, out var faces) || faces.Count == 0)
        {
            Debug.Log("Can't find exposed faces for: " + cell);
            return null;
        }
        return faces;
    }

    public FaceNode GetFaceNode(Vector3Int cell, Vector3 preferredNormal = default)
    {
        if (!cellFaces.TryGetValue(cell, out var faces) || faces.Count == 0)
        {
            Debug.Log("Can't find exposed faces for: " + cell);
            return null;
        }

        if (preferredNormal == default)
            return faces[0];

        FaceNode best = faces[0];
        float bestDot = Vector3.Dot(best.normal, preferredNormal);
        foreach (var f in faces)
        {
            float d = Vector3.Dot(f.normal, preferredNormal);
            if (d > bestDot) { bestDot = d; best = f; }
        }
        return best;
    }

    bool IsNeighbor(FaceNode a, FaceNode b)
    {
        float dist = Vector3.Distance(a.worldPos, b.worldPos);
        float eps = cachedSize * 0.05f;

        if (Mathf.Abs(dist - cachedSize) < eps)
            return Vector3.Dot(a.normal, b.normal) > 0.9f;

        if (Mathf.Abs(dist - cachedSize * 0.7072f) < eps)
            return Mathf.Abs(Vector3.Dot(a.normal, b.normal)) < 0.1f;

        return false;
    }
}