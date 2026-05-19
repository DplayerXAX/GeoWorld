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

    [Header("First-stage seed (no anchor yet)")]
    [Tooltip("Half-extent (in cells) of the random region used to place the very first start endpoint, centred on the origin.")]
    public int firstStartScatter = 2;

    [Header("Shell sampling")]
    [Tooltip("Vertical cap on endpoint cells. y is clamped into [0, yMax].")]
    public int yMax = 4;
    [Tooltip("Random shell sampling attempts before falling back to a deterministic offset.")]
    public int shellSampleAttempts = 60;

    [Header("Visual")]
    public GameObject startPrefab;
    public GameObject endPrefab;

    GameObject startObj;
    GameObject endObj;

    // First stage: no anchor yet. Start near origin, end shell-sampled from start.
    public void Generate()
    {
        startCell = SampleFirstStart();
        endCell   = ShellSample(startCell, minDistance, maxDistance);
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

    public Vector3Int GenerateSinglePoint(List<Vector3Int> existingPoints, bool isStart)
    {
        if (existingPoints == null || existingPoints.Count == 0) return Vector3Int.zero;

        var anchor = existingPoints[Random.Range(0, existingPoints.Count)];
        var cell   = ShellSample(anchor, minDistance, maxDistance);
        if (cell == anchor) return Vector3Int.zero;

        var prefab = isStart ? startPrefab : endPrefab;
        if (prefab != null)
        {
            var obj = Instantiate(prefab);
            obj.transform.position = gridSystem.GridToWorld(cell);
            gridSystem.SetOccupied(cell);
            obj.name = isStart ? "startBlock" : "endBlock";
            if (!obj.GetComponent<GridEndpoint>()) obj.AddComponent<GridEndpoint>();
            allEndpoints.Add(obj);
        }

        return cell;
    }

    Vector3Int SampleFirstStart()
    {
        int r = Mathf.Max(0, firstStartScatter);
        return new Vector3Int(
            Random.Range(-r, r + 1),
            0,
            Random.Range(-r, r + 1)
        );
    }

    // Y is clamped so endpoints don't drift underground or above the play area.
    Vector3Int ShellSample(Vector3Int center, float minD, float maxD)
    {
        for (int attempt = 0; attempt < shellSampleAttempts; attempt++)
        {
            Vector3 dir = Random.onUnitSphere;
            float   d   = Random.Range(minD, maxD);
            Vector3 raw = (Vector3)center + dir * d;
            var cell = new Vector3Int(
                Mathf.RoundToInt(raw.x),
                Mathf.Clamp(Mathf.RoundToInt(raw.y), 0, yMax),
                Mathf.RoundToInt(raw.z)
            );
            if (cell == center) continue;
            if (gridSystem.IsOccupied(cell)) continue;
            return cell;
        }

        int dist = Mathf.Max(1, Mathf.CeilToInt(maxD));
        for (int i = 0; i < 8; i++)
        {
            var c = center + new Vector3Int(dist + i, 0, 0);
            if (!gridSystem.IsOccupied(c)) return c;
        }
        return center + new Vector3Int(dist, 0, 0);
    }
}
