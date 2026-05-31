using UnityEngine;

// Spawns a prefab on each of the 12 edges of every cell — flowers around
// edges, vines, runes, etc. The prefab is rotated so its local +Y aligns
// with the edge's "outward" direction (sum of the two adjacent face
// normals), so e.g. a flower stem grows out & up from a top edge.
//
// Filters:
//   • shared-edge culling: if two cells of the SAME piece share a face on
//     either side of the edge, that edge is interior — skip it
//   • per-face toggles: top / side / bottom (an edge counts as top if its
//     midpoint Y is above the cell center, etc.)
//
// Authored as ScriptableObject and dropped into SynergyRule.visualizer.
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Visualizers/Edge Prefab",
                 fileName = "EdgePrefabVisualizer")]
public class EdgePrefabVisualizer : SynergyVisualizer
{
    [Tooltip("Prefab spawned on each external edge of every claimed cell.")]
    public GameObject prefab;

    [Header("Placement")]
    [Tooltip("Additional offset along the edge's outward direction.")]
    public float outwardOffset = 0f;

    [Tooltip("Uniform scale multiplier.")]
    [Min(0.001f)] public float localScale = 1f;

    [Tooltip("If true, prefab's local +Y is aligned with the edge outward direction. If false, prefab keeps its authored rotation (parent-relative).")]
    public bool alignToOutward = true;

    [Tooltip("Extra Euler rotation applied after outward alignment (useful for fine-tuning).")]
    public Vector3 extraEuler = Vector3.zero;

    [Header("Color")]
    [Tooltip("Tint every Renderer in the spawned prefab. If `colorPalette` is empty, uses the synergy theme color; otherwise picks a random color from the palette per spawn.")]
    public bool tintToThemeColor = true;

    [Tooltip("Optional color pool. Non-empty → each spawn picks a random color from this list instead of the theme color. Use for natural-looking wildflower mixes.")]
    public Color[] colorPalette;

    [Header("Randomness")]
    [Tooltip("Random spin around the outward axis, ±this many degrees.")]
    [Range(0f, 180f)] public float rotationJitterDeg = 0f;

    [Tooltip("Random tilt away from the outward axis, ±this many degrees. Small values make flowers feel naturally placed.")]
    [Range(0f, 45f)] public float tiltJitterDeg = 0f;

    [Tooltip("Random scale multiplier range. (1, 1) = no jitter.")]
    public float scaleJitterMin = 1f;
    public float scaleJitterMax = 1f;

    [Tooltip("Random position jitter perpendicular to the edge (in cell units).")]
    [Range(0f, 0.3f)] public float positionJitter = 0f;

    [Header("Density")]
    [Tooltip("Probability that each eligible edge actually spawns a prefab. <1 = sparser, more natural look.")]
    [Range(0f, 1f)] public float spawnProbability = 0.4f;

    [Tooltip("Hard cap of spawns per cell. 0 = unlimited (only spawnProbability gates).")]
    [Min(0)] public int maxPerCell = 0;

    [Tooltip("If true and the probability roll spawns zero flowers on a cell, force at least one to keep cells from looking bare. (One random eligible edge.)")]
    public bool forceAtLeastOnePerCell = true;

    [Header("Which edges")]
    public bool topEdges    = true;
    public bool sideEdges   = true;
    public bool bottomEdges = false;

    [Tooltip("If true, edges shared with another cell of the same piece are skipped (cleaner look on multi-cell pieces).")]
    public bool skipInteriorEdges = true;

    // 12 cube edges (in unit cell, midpoint relative to cell center).
    // Each entry: midpoint, outward unit normal (sum of two adjacent face
    // normals, normalised), and the two face-normals that bound this edge
    // (used to look up neighbours when culling interior edges).
    struct EdgeDef
    {
        public Vector3 midpoint;
        public Vector3 outward;
        public Vector3Int neighbourA;   // cell offset of neighbour across face A
        public Vector3Int neighbourB;   // cell offset of neighbour across face B
        public Vector3Int neighbourAB;  // cell offset of diagonal neighbour
        public string tier;             // "top" / "bottom" / "side"
    }

    static readonly EdgeDef[] _edges = BuildEdges();

    static EdgeDef[] BuildEdges()
    {
        // 4 top edges (Y=+0.5), 4 bottom (Y=-0.5), 4 side (vertical).
        var list = new System.Collections.Generic.List<EdgeDef>(12);

        // Top 4 — along X or Z, at Y=+0.5
        list.Add(MakeEdge(new(0,    +0.5f, +0.5f), Vector3Int.up, Vector3Int.forward, "top"));   // top-front (along X)
        list.Add(MakeEdge(new(0,    +0.5f, -0.5f), Vector3Int.up, Vector3Int.back,    "top"));   // top-back
        list.Add(MakeEdge(new(+0.5f,+0.5f,  0  ), Vector3Int.up, Vector3Int.right,   "top"));   // top-right (along Z)
        list.Add(MakeEdge(new(-0.5f,+0.5f,  0  ), Vector3Int.up, Vector3Int.left,    "top"));   // top-left

        // Bottom 4
        list.Add(MakeEdge(new(0,    -0.5f, +0.5f), Vector3Int.down, Vector3Int.forward, "bottom"));
        list.Add(MakeEdge(new(0,    -0.5f, -0.5f), Vector3Int.down, Vector3Int.back,    "bottom"));
        list.Add(MakeEdge(new(+0.5f,-0.5f,  0  ), Vector3Int.down, Vector3Int.right,   "bottom"));
        list.Add(MakeEdge(new(-0.5f,-0.5f,  0  ), Vector3Int.down, Vector3Int.left,    "bottom"));

        // Side 4 (vertical, along Y)
        list.Add(MakeEdge(new(+0.5f, 0, +0.5f), Vector3Int.right, Vector3Int.forward, "side"));
        list.Add(MakeEdge(new(+0.5f, 0, -0.5f), Vector3Int.right, Vector3Int.back,    "side"));
        list.Add(MakeEdge(new(-0.5f, 0, +0.5f), Vector3Int.left,  Vector3Int.forward, "side"));
        list.Add(MakeEdge(new(-0.5f, 0, -0.5f), Vector3Int.left,  Vector3Int.back,    "side"));

        return list.ToArray();
    }

    static EdgeDef MakeEdge(Vector3 midpoint, Vector3Int faceA, Vector3Int faceB, string tier)
    {
        var outward = ((Vector3)faceA + (Vector3)faceB).normalized;
        return new EdgeDef
        {
            midpoint    = midpoint,
            outward     = outward,
            neighbourA  = faceA,
            neighbourB  = faceB,
            neighbourAB = faceA + faceB,
            tier        = tier,
        };
    }

    public override GameObject OnPieceClaimed(PlacedBlockInstance instance, ActiveSynergy active)
    {
        if (prefab == null || instance?.visualObject == null) return null;
        var grid = GridSystem.instance;
        if (grid == null) return null;

        var parent = instance.visualObject.transform;
        var root   = new GameObject("Synergy_EdgeDeco");
        root.transform.SetParent(parent, false);

        var tint = BlockColorPalette.Get(active.rule.color);

        // Build a set of this piece's cells for interior-edge culling.
        var sameCells = new System.Collections.Generic.HashSet<Vector3Int>(instance.occupiedCells);

        // Reusable buffers (per cell) for eligible-edge filter + random pick.
        var eligible = new System.Collections.Generic.List<int>(12);

        foreach (var worldCell in instance.occupiedCells)
        {
            var cellLocal = parent.InverseTransformPoint(grid.GridToWorld(worldCell));

            // Pass 1: collect eligible edge indices (tier + interior filter).
            eligible.Clear();
            for (int i = 0; i < _edges.Length; i++)
            {
                var e = _edges[i];
                if (e.tier == "top"    && !topEdges)    continue;
                if (e.tier == "bottom" && !bottomEdges) continue;
                if (e.tier == "side"   && !sideEdges)   continue;
                if (skipInteriorEdges)
                {
                    if (sameCells.Contains(worldCell + e.neighbourA))  continue;
                    if (sameCells.Contains(worldCell + e.neighbourB))  continue;
                    if (sameCells.Contains(worldCell + e.neighbourAB)) continue;
                }
                eligible.Add(i);
            }
            if (eligible.Count == 0) continue;

            // Pass 2: probability + cap.
            int spawnedThisCell = 0;
            for (int k = 0; k < eligible.Count; k++)
            {
                if (Random.value > spawnProbability) continue;
                if (maxPerCell > 0 && spawnedThisCell >= maxPerCell) break;

                SpawnEdge(root.transform, cellLocal, _edges[eligible[k]], tint);
                spawnedThisCell++;
            }

            // Fallback: cell ended up bare → force one random eligible edge.
            if (spawnedThisCell == 0 && forceAtLeastOnePerCell)
            {
                int pick = eligible[Random.Range(0, eligible.Count)];
                SpawnEdge(root.transform, cellLocal, _edges[pick], tint);
            }
        }

        return root;
    }

    void SpawnEdge(Transform parent, Vector3 cellLocal, EdgeDef e, Color tint)
    {
        SpawnOne(parent,
                 cellLocal + e.midpoint + e.outward * outwardOffset,
                 e.outward,
                 tint);
    }

    void SpawnOne(Transform parent, Vector3 localPos, Vector3 outward, Color tint)
    {
        var obj = Instantiate(prefab, parent);

        // Position jitter — random offset perpendicular to outward.
        if (positionJitter > 0f)
        {
            Vector3 perp = Vector3.Cross(outward, Random.onUnitSphere).normalized;
            localPos += perp * Random.Range(-positionJitter, positionJitter);
        }
        obj.transform.localPosition = localPos;

        // Rotation: align to outward with optional tilt + spin jitter.
        if (alignToOutward)
        {
            var baseRot = Quaternion.FromToRotation(Vector3.up, outward);

            if (tiltJitterDeg > 0f)
            {
                var tiltAxis = Vector3.Cross(outward, Random.onUnitSphere).normalized;
                if (tiltAxis.sqrMagnitude < 0.001f) tiltAxis = Vector3.right;
                baseRot = Quaternion.AngleAxis(Random.Range(-tiltJitterDeg, tiltJitterDeg), tiltAxis) * baseRot;
            }
            if (rotationJitterDeg > 0f)
            {
                baseRot *= Quaternion.AngleAxis(Random.Range(-rotationJitterDeg, rotationJitterDeg), Vector3.up);
            }

            obj.transform.localRotation = baseRot * Quaternion.Euler(extraEuler);
        }
        else
        {
            obj.transform.localRotation = Quaternion.Euler(extraEuler);
        }

        // Scale with jitter.
        float scale = localScale;
        if (scaleJitterMax > scaleJitterMin)
            scale *= Random.Range(scaleJitterMin, scaleJitterMax);
        obj.transform.localScale = Vector3.one * scale;

        if (tintToThemeColor)
        {
            Color pickedTint = (colorPalette != null && colorPalette.Length > 0)
                ? colorPalette[Random.Range(0, colorPalette.Length)]
                : tint;

            var rends = obj.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < rends.Length; i++)
                MpbColor.Set(rends[i], pickedTint);
        }
    }
}
