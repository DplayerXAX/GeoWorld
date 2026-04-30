using System.Collections.Generic;
using UnityEngine;

public class LevelEndpointGenerator : MonoBehaviour
{
    public GridSystem gridSystem;

    [Header("Logic")]
    public Vector3Int startCell;
    public Vector3Int endCell;
    public List<GameObject> allEndpoints = new();
    public float minDistance = 5f;
    public float maxDistance = 10f;

    [Header("Visual")]
    public GameObject startPrefab;
    public GameObject endPrefab;

    GameObject startObj;
    GameObject endObj;

    public void Generate()
    {
        List<Vector3Int> candidates = new();

        var grid = gridSystem.GetGrid();

        foreach (var kv in grid)
        {
            if (!kv.Value)
                candidates.Add(kv.Key);
        }

        if (candidates.Count < 2)
        {
            Debug.LogError("Not enough free cells");
            return;
        }

        startCell = candidates[Random.Range(0, candidates.Count)];
        endCell = PickEnd(candidates, startCell);

        SpawnVisual();
    }

    void SpawnVisual()
    {
        if (gridSystem == null) return;

        if (startPrefab != null)
        {
            var obj = Instantiate(startPrefab);
            obj.transform.position = gridSystem.GridToWorld(startCell);
            gridSystem.SetOccupied(startCell);
            obj.name = "startBlock";
            if (!obj.GetComponent<GridEndpoint>()) obj.AddComponent<GridEndpoint>();

            allEndpoints.Add(obj);
        }

        if (endPrefab != null)
        {
            var obj = Instantiate(endPrefab);
            obj.transform.position = gridSystem.GridToWorld(endCell);
            gridSystem.SetOccupied(endCell);
            obj.name = "endBlock";
            if (!obj.GetComponent<GridEndpoint>()) obj.AddComponent<GridEndpoint>();

            allEndpoints.Add(obj);
        }
    }

    Vector3Int PickEnd(List<Vector3Int> candidates, Vector3Int start)
    {
        Vector3Int best      = Vector3Int.zero;
        float      bestScore = -1f;
        bool       found     = false;

        // Phase 1: strict distance window (30 random samples).
        for (int i = 0; i < 30; i++)
        {
            var c = candidates[Random.Range(0, candidates.Count)];
            if (c == start) continue;

            float dist = Vector3Int.Distance(c, start);
            if (dist >= minDistance && dist <= maxDistance && dist > bestScore)
            {
                best = c; bestScore = dist; found = true;
            }
        }

        if (found) return best;

        // Phase 2: relax — scan all candidates for the one closest to maxDistance
        // so the puzzle is at least as hard as possible within the grid.
        float closestDelta = float.MaxValue;
        foreach (var c in candidates)
        {
            if (c == start) continue;
            float delta = Mathf.Abs(Vector3Int.Distance(c, start) - maxDistance);
            if (delta < closestDelta) { closestDelta = delta; best = c; }
        }

        return best;
    }
}