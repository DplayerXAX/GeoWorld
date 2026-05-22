using System.Collections.Generic;
using UnityEngine;

public class SurfaceGraphBuilder
{
    public Dictionary<Vector3Int, List<FaceNode>> cellFaces = new();
    public List<FaceNode> allFaces = new();
    GridSystem gridSystem;

    public void SetData(GridSystem grid)
    {
        this.gridSystem = grid;
    }

    public void Build()
    {
        var blocks = gridSystem.GetGrid();
        allFaces.Clear();
        cellFaces.Clear();

        foreach (var kv in blocks)
        {
            if (!kv.Value) continue;

            // Turrets are obstacles, not walkable. Their cells stay occupied
            // (so paths can't pass through), but we skip generating face nodes
            // for them — the unit can't walk onto a turret's surface.
            if (IsTurretCell(kv.Key)) continue;

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

    bool IsTurretCell(Vector3Int cell)
    {
        var ins = gridSystem.GetInstanceAt(cell);
        return ins?.data != null && ins.data.blockType == BlockType.Turret;
    }

    // Locality-based O(V × ~12) instead of O(V²). For each face check:
    //   Type A — parallel face on cell C+T (one cell over in a tangent direction)
    //   Type B — perpendicular face on cell C+N+T (wraps a convex edge across cubes)
    //   Type C — perpendicular sibling on the same cell (wraps a convex edge of this cube)
    // After collecting, sort by normal.y desc so BFS tie-breaks toward top faces.
    void BuildNeighbors()
    {
        foreach (var face in allFaces)
        {
            var n = Vector3Int.RoundToInt(face.normal);
            var tangents = GetTangents(n);

            foreach (var t in tangents)
            {
                if (cellFaces.TryGetValue(face.cell + t, out var facesA))
                    foreach (var f2 in facesA)
                        if (Vector3.Dot(f2.normal, face.normal) > 0.9f)
                        {
                            face.neighbors.Add(f2);
                            break;
                        }

                if (cellFaces.TryGetValue(face.cell + n + t, out var facesB))
                {
                    Vector3 negT = -(Vector3)t;
                    foreach (var f2 in facesB)
                        if (Vector3.Dot(f2.normal, negT) > 0.9f)
                        {
                            face.neighbors.Add(f2);
                            break;
                        }
                }
            }

            if (cellFaces.TryGetValue(face.cell, out var siblings))
                foreach (var sib in siblings)
                {
                    if (sib == face) continue;
                    if (Mathf.Abs(Vector3.Dot(face.normal, sib.normal)) < 0.1f)
                        face.neighbors.Add(sib);
                }

            face.neighbors.Sort((a, b) => b.normal.y.CompareTo(a.normal.y));
        }
    }

    static readonly Vector3Int[] _tangentsForX = {
        new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)
    };
    static readonly Vector3Int[] _tangentsForY = {
        new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1)
    };
    static readonly Vector3Int[] _tangentsForZ = {
        new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0)
    };

    static Vector3Int[] GetTangents(Vector3Int n)
    {
        if (n.x != 0) return _tangentsForX;
        if (n.y != 0) return _tangentsForY;
        return _tangentsForZ;
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
}