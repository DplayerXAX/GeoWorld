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
        SpawnEndpointAt(startCell, true);
        SpawnEndpointAt(endCell,   false);
    }

    // Fixed start/end (tutorials / hand-authored stages) — skips random sampling.
    public void GenerateFixed(Vector3Int start, Vector3Int end)
    {
        startCell = start;
        endCell   = end;
        SpawnVisual();
    }

    // Public spawn-at-known-cell. Used by snapshot restore.
    public void SpawnEndpointAt(Vector3Int cell, bool isStart)
    {
        if (gridSystem == null) return;
        var prefab = isStart ? startPrefab : endPrefab;
        if (prefab == null) return;

        var obj = Instantiate(prefab);
        obj.transform.position = gridSystem.GridToWorld(cell);
        gridSystem.SetOccupied(cell);
        obj.name = isStart ? "startBlock" : "endBlock";
        if (!obj.GetComponent<GridEndpoint>()) obj.AddComponent<GridEndpoint>();
        allEndpoints.Add(obj);
    }

    // Destroys every endpoint visual. Cells stay occupied (caller's job to clear).
    public void ClearAll()
    {
        foreach (var go in allEndpoints)
            if (go != null) Destroy(go);
        allEndpoints.Clear();
        startCell = Vector3Int.zero;
        endCell   = Vector3Int.zero;
    }

    // `reachEnds`: pass the level's end cells to constrain a NEW START to the
    // union of the existing starts' reach circles (see SampleStartInsideReach).
    // Null / empty (and every end-point call) keeps the plain shell sampling.
    public Vector3Int GenerateSinglePoint(List<Vector3Int> existingPoints, bool isStart,
                                          List<Vector3Int> reachEnds = null)
    {
        if (existingPoints == null || existingPoints.Count == 0) return Vector3Int.zero;

        Vector3Int cell;
        if (isStart && reachEnds != null && reachEnds.Count > 0)
        {
            cell = SampleStartInsideReach(existingPoints, reachEnds);
            if (cell == Vector3Int.zero) return Vector3Int.zero;
        }
        else
        {
            var anchor = existingPoints[Random.Range(0, existingPoints.Count)];
            cell = ShellSample(anchor, minDistance, maxDistance);
            if (cell == anchor) return Vector3Int.zero;
        }

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

    // ── Start-point reach circles ────────────────────────────────────────────
    // A new spawn point may only appear within "reach" of the existing ones:
    // every existing start draws a circle centred on itself whose radius is its
    // OWN distance to the end, and the new start has to land inside at least one
    // of them. Union, not intersection — the circles don't have to overlap, the
    // cell just has to be covered by one. Keeps later spawns from drifting out
    // past the frontier the player has actually had to defend.
    //
    // All distances are horizontal (XZ), matching ShellSample's metric.

    static float HorizDist(Vector3Int a, Vector3Int b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // Radius of `start`'s circle — its horizontal distance to the NEAREST end,
    // so a level with several ends measures each start against the one it
    // actually feeds. 0 when there are no ends (caller skips the constraint).
    static float ReachRadius(Vector3Int start, List<Vector3Int> ends)
    {
        float best = 0f;
        if (ends == null) return best;
        for (int i = 0; i < ends.Count; i++)
        {
            float d = HorizDist(start, ends[i]);
            if (best == 0f || d < best) best = d;
        }
        return best;
    }

    static bool InsideAnyReach(Vector3Int cell, List<Vector3Int> starts, List<Vector3Int> ends)
    {
        if (starts == null || ends == null || ends.Count == 0) return true;   // nothing to constrain against
        for (int i = 0; i < starts.Count; i++)
            if (HorizDist(cell, starts[i]) <= ReachRadius(starts[i], ends)) return true;
        return false;
    }

    // Returns Vector3Int.zero when no cell could be found (caller treats that as
    // "skip this round's endpoint", same as the other failure paths).
    Vector3Int SampleStartInsideReach(List<Vector3Int> starts, List<Vector3Int> ends)
    {
        // Pass 1: ordinary shell sampling around a random existing start, keeping
        // only candidates covered by SOME circle. Reaches the whole union region,
        // not just the anchor's own circle.
        for (int attempt = 0; attempt < shellSampleAttempts; attempt++)
        {
            var anchor = starts[Random.Range(0, starts.Count)];
            var cell   = ShellSample(anchor, minDistance, maxDistance);
            if (cell == anchor) continue;
            if (InsideAnyReach(cell, starts, ends)) return cell;
        }

        // Pass 2: build the cell INSIDE one chosen circle instead of testing after
        // the fact, so a valid answer still exists when the [minDistance,
        // maxDistance] shell mostly falls outside every circle (small radius or a
        // widened distance window late in a run). Distance is clamped into the
        // radius, so the reach rule wins over the distance window when they clash.
        for (int attempt = 0; attempt < shellSampleAttempts; attempt++)
        {
            var anchor = starts[Random.Range(0, starts.Count)];
            float r = ReachRadius(anchor, ends);
            if (r < 1f) continue;   // degenerate circle — a start sitting on an end

            float d   = Random.Range(Mathf.Min(minDistance, r), Mathf.Min(maxDistance, r));
            float ang = Random.Range(0f, Mathf.PI * 2f);
            var cell = new Vector3Int(
                anchor.x + Mathf.RoundToInt(Mathf.Cos(ang) * d),
                Mathf.Clamp(Random.Range(0, yMax + 1), 0, yMax),
                anchor.z + Mathf.RoundToInt(Mathf.Sin(ang) * d));

            if (cell == anchor) continue;
            if (gridSystem.IsOccupied(cell)) continue;
            if (!InsideAnyReach(cell, starts, ends)) continue;   // integer rounding can nudge it just outside
            return cell;
        }

        return Vector3Int.zero;
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

    // Samples a cell whose HORIZONTAL (XZ) distance from `center` lies in
    // [minD, maxD]. The previous version sampled on a 3D unit sphere and
    // then clamped Y to [0, yMax] — when a mostly-vertical direction got
    // picked, the Y clamp consumed almost all the distance budget and the
    // final cell ended up right next to the anchor.
    //
    // Fix:
    //   1. Pick the direction in the XZ plane only (the play area is mostly
    //      horizontal anyway).
    //   2. Pick Y independently inside [0, yMax].
    //   3. After integer rounding, VERIFY the final horizontal distance is
    //      still in [minD, maxD] — RoundToInt can shift cells around by ½
    //      a cell which matters at small minD.
    Vector3Int ShellSample(Vector3Int center, float minD, float maxD)
    {
        for (int attempt = 0; attempt < shellSampleAttempts; attempt++)
        {
            float ang = Random.Range(0f, Mathf.PI * 2f);
            float d   = Random.Range(minD, maxD);

            float dx = Mathf.Cos(ang) * d;
            float dz = Mathf.Sin(ang) * d;
            int   y  = Mathf.Clamp(Random.Range(0, yMax + 1), 0, yMax);

            var cell = new Vector3Int(
                center.x + Mathf.RoundToInt(dx),
                y,
                center.z + Mathf.RoundToInt(dz)
            );

            if (cell == center) continue;
            if (gridSystem.IsOccupied(cell)) continue;

            // Verify final horizontal distance — integer rounding can pull
            // the cell inside minD or push it past maxD.
            float fdx = cell.x - center.x;
            float fdz = cell.z - center.z;
            float horizDist = Mathf.Sqrt(fdx * fdx + fdz * fdz);
            if (horizDist < minD || horizDist > maxD) continue;

            return cell;
        }

        // Deterministic fallback: sweep +X at maxD until we find a free cell.
        // Guarantees the distance is at least maxD (not within range, but
        // never below minD).
        int dist = Mathf.Max(1, Mathf.CeilToInt(maxD));
        for (int i = 0; i < 8; i++)
        {
            var c = new Vector3Int(
                center.x + dist + i,
                Mathf.Clamp(center.y, 0, yMax),
                center.z);
            if (!gridSystem.IsOccupied(c)) return c;
        }
        return new Vector3Int(center.x + dist, Mathf.Clamp(center.y, 0, yMax), center.z);
    }
}
