using System.Collections.Generic;
using UnityEngine;

// The two decor plots beyond the 1-1 farm. Both ride the shared machinery in
// LevelMapController.Decor.cs — ground, walkability, residents, sink-and-rise
// reveal — and add only their own props here.
//
//   1-2  Order workshop  — a machine house, gears everywhere, everything turning
//                          in lockstep. Order is the "connect the same colour"
//                          faction, so its monument is drivetrain: nothing moves
//                          alone, every wheel is meshed to the next one.
//   1-3  Observatory     — a slit dome with a telescope sweeping the sky, ringed
//                          by an armillary and a plaza of sight-lines. Reading
//                          the sky is what Enlightenment's cube-building is for.

[System.Serializable]
public class OrderWorkshopConfig : MapDecorConfig
{
    [Header("Machine house")]
    [Tooltip("House footprint in cells (x, z), centred on the plot.")]
    public Vector2Int houseSize = new Vector2Int(4, 3);
    [Tooltip("Wall height in world units.")]
    [Range(0.8f, 4f)] public float wallHeight = 1.6f;
    [Tooltip("Ridge height above the wall top.")]
    [Range(0.2f, 2f)] public float roofRise = 0.85f;
    public Color wallColor  = new Color(0.62f, 0.63f, 0.66f);
    public Color roofColor  = new Color(0.29f, 0.31f, 0.36f);
    public Color trimColor  = new Color(0.86f, 0.87f, 0.90f);

    [Header("Gears")]
    [Tooltip("Free-standing gears scattered across the yard, on top of the ground blocks.")]
    [Range(0, 24)] public int yardGears = 10;
    [Tooltip("Gears mounted flat on the house's long walls.")]
    [Range(0, 8)] public int wallGears = 4;
    [Range(0.25f, 1.5f)] public float gearSize = 0.62f;
    [Tooltip("Degrees per second for a size-1 gear. Bigger gears turn proportionally slower, like a real train — that's what sells them as MESHED rather than independently spinning.")]
    public float gearBaseSpin = 55f;
    [Tooltip("Gear colour. Accent is used for every other gear so the drivetrain reads as alternating.")]
    public Color gearColor   = new Color(0.55f, 0.57f, 0.62f);
    public Color gearAccent  = new Color(0.93f, 0.72f, 0.24f);

    [Header("Pipes & stacks")]
    [Range(0, 6)] public int chimneys = 2;
    [Tooltip("Upright pipes standing around the yard.")]
    [Range(0, 12)] public int pipes = 5;

    public override string RootName => "OrderWorkshop";

    public OrderWorkshopConfig()
    {
        enabled       = true;
        gateLevelId   = "1-2";
        origin        = new Vector3Int(6, 2, -2);
        size          = new Vector2Int(11, 10);
        soilColor     = new Color(0.32f, 0.33f, 0.36f);   // oil-stained plate, not earth
        soilJitter    = 0.10f;
        growYawOffset = -80f;
        growAsideText = "Where Order took hold, the ground learned to keep time — and a workshop rose to keep it.";
    }
}

[System.Serializable]
public class ObservatoryConfig : MapDecorConfig
{
    [Header("Dome")]
    [Range(0.8f, 4f)]  public float drumHeight = 1.7f;
    [Range(0.6f, 3f)]  public float domeRadius = 1.25f;
    [Tooltip("Degrees per second the dome (and the telescope inside it) sweeps the sky.")]
    public float scanSpeed = 9f;
    [Tooltip("How far the telescope rocks up and down over its sweep, in degrees.")]
    [Range(0f, 40f)] public float scanTilt = 14f;
    public Color drumColor  = new Color(0.83f, 0.82f, 0.79f);
    public Color domeColor  = new Color(0.36f, 0.42f, 0.58f);
    public Color brassColor = new Color(0.87f, 0.72f, 0.36f);

    [Header("Precinct wall")]
    [Tooltip("Low wall around the whole plot. This plot is deliberately FLAT and fully covered, so the wall is what gives it an edge — a frayed rim would read as ruins, not as a built precinct.")]
    public bool wallEnabled = true;
    [Range(0.2f, 2f)] public float wallHeight = 0.62f;
    [Tooltip("Cells of gateway left open on each side. 0 = unbroken wall.")]
    [Range(0, 4)] public int gateWidth = 2;
    [Tooltip("Corner posts stand taller than the wall run between them.")]
    [Range(1f, 2.5f)] public float cornerPostScale = 1.45f;
    public Color wallColor = new Color(0.72f, 0.71f, 0.70f);

    [Header("Grounds")]
    [Tooltip("Rings of the armillary sphere standing beside the dome. 0 = none.")]
    [Range(0, 5)] public int armillaryRings = 3;
    [Tooltip("Sight-line markers set into the plaza — low stones pointing at the dome.")]
    [Range(0, 20)] public int sightStones = 9;
    [Tooltip("Floating star motes above the plot.")]
    [Range(0, 60)] public int stars = 26;
    public Color starColor  = new Color(0.95f, 0.94f, 0.78f);

    public override string RootName => "Observatory";

    public ObservatoryConfig()
    {
        enabled       = true;
        gateLevelId   = "1-3";
        origin        = new Vector3Int(-6, 2, -13);
        size          = new Vector2Int(10, 10);
        soilColor     = new Color(0.26f, 0.27f, 0.33f);   // night slate
        soilJitter    = 0.06f;   // barely there — flagstone variation, not soil
        growYawOffset = 120f;
        growAsideText = "With Enlightenment came the patience to look up, and a place built for nothing else.";
    }
}

public partial class LevelMapController : MonoBehaviour
{
    // ═════════════════════════════════════════════════════════════════════════
    // 1-2 — Order workshop
    // ═════════════════════════════════════════════════════════════════════════

    void BuildOrderWorkshop(OrderWorkshopConfig cfg,
                            HashSet<Vector2Int> coveredCols,
                            Dictionary<Vector2Int, Vector3Int> colTop,
                            Vector2Int ext, float cs)
    {
        var root = new GameObject("Workshop").transform;
        root.SetParent(_buildingRoot.transform, false);

        // Centred on the plot, which is also the tallest thing here — everything
        // else is arranged around it.
        var centreCol = new Vector2Int(cfg.origin.x + ext.x / 2, cfg.origin.z + ext.y / 2);
        Vector3 housePos = ColumnSurface(colTop, centreCol, cfg, cs);

        float w = cfg.houseSize.x * cs;
        float d = cfg.houseSize.y * cs;
        float wall = cfg.wallHeight;

        // ── Shell ────────────────────────────────────────────────────────────
        MakeMeshProp(root, "Walls", RailMesh(), housePos + Vector3.up * (wall * 0.5f),
                     Quaternion.identity, new Vector3(w, wall, d), cfg.wallColor);

        // A plinth course under the walls, so the house meets the ground with a
        // deliberate edge rather than just resting on it.
        MakeMeshProp(root, "Base", RailMesh(), housePos + Vector3.up * (cs * 0.06f),
                     Quaternion.identity, new Vector3(w * 1.10f, cs * 0.14f, d * 1.10f), cfg.roofColor);

        MakeMeshProp(root, "Roof", GableMesh(), housePos + Vector3.up * wall,
                     Quaternion.identity, new Vector3(w * 1.12f, cfg.roofRise, d * 1.12f), cfg.roofColor);

        // Door and two windows on the +X face — one readable "front" is enough at
        // map-camera distance, and picking a face keeps the house oriented.
        float front = w * 0.5f + 0.01f;
        MakeMeshProp(root, "Door", RailMesh(), housePos + new Vector3(front, wall * 0.34f, 0f),
                     Quaternion.identity, new Vector3(0.06f, wall * 0.62f, d * 0.24f), cfg.roofColor);
        for (int i = 0; i < 2; i++)
        {
            float z = (i == 0 ? -1f : 1f) * d * 0.28f;
            MakeMeshProp(root, $"Window{i}", RailMesh(),
                         housePos + new Vector3(front, wall * 0.66f, z),
                         Quaternion.identity, new Vector3(0.06f, wall * 0.24f, d * 0.18f), cfg.gearAccent);
        }

        // ── Chimneys ─────────────────────────────────────────────────────────
        for (int i = 0; i < cfg.chimneys; i++)
        {
            float t = cfg.chimneys == 1 ? 0.5f : i / (float)(cfg.chimneys - 1);
            float x = Mathf.Lerp(-w * 0.28f, w * 0.28f, t);
            float h = cs * (0.55f + Hash01(DecorHash(i, 17)) * 0.35f);
            MakeMeshProp(root, $"Stack{i}", RailMesh(),
                         housePos + new Vector3(x, wall + cfg.roofRise * 0.5f + h * 0.5f, d * 0.18f),
                         Quaternion.identity, new Vector3(cs * 0.16f, h, cs * 0.16f), cfg.roofColor);
            MakeMeshProp(root, $"StackCap{i}", RailMesh(),
                         housePos + new Vector3(x, wall + cfg.roofRise * 0.5f + h, d * 0.18f),
                         Quaternion.identity, new Vector3(cs * 0.22f, cs * 0.06f, cs * 0.22f), cfg.trimColor);
        }

        // ── Wall gears ───────────────────────────────────────────────────────
        // Mounted flat against the long walls, axles pointing out along X, so they
        // read as driven BY the house rather than parked next to it.
        for (int i = 0; i < cfg.wallGears; i++)
        {
            bool  side = (i & 1) == 0;
            float z    = Mathf.Lerp(-d * 0.3f, d * 0.3f, cfg.wallGears <= 2 ? 0.5f : (i / 2) / Mathf.Max(1f, cfg.wallGears / 2f - 1f));
            float r    = cfg.gearSize * (0.55f + Hash01(DecorHash(i, 91)) * 0.5f);
            var pos    = housePos + new Vector3((side ? 1f : -1f) * (w * 0.5f + 0.05f), wall * 0.55f, z);
            SpawnGear(root, $"WallGear{i}", pos, Quaternion.Euler(0f, 0f, 90f), r, i, cfg);
        }

        // ── Yard gears ───────────────────────────────────────────────────────
        // Lying flat on the ground like millstones, so the yard reads as machinery
        // half-buried in the plate rather than as scattered props.
        var cols = new List<Vector2Int>(coveredCols);
        cols.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));   // deterministic, unlike HashSet order
        for (int i = 0; i < cfg.yardGears && cols.Count > 0; i++)
        {
            var col = cols[Mathf.FloorToInt(Hash01(DecorHash(i, 331)) * cols.Count) % cols.Count];
            if (col == centreCol) continue;
            float r = cfg.gearSize * (0.4f + Hash01(DecorHash(i, 733)) * 0.8f);
            var pos = ColumnSurface(colTop, col, cfg, cs) + Vector3.up * (cs * 0.06f);
            SpawnGear(root, $"YardGear{i}", pos, Quaternion.identity, r, i + 7, cfg);
        }

        // ── Pipes ────────────────────────────────────────────────────────────
        for (int i = 0; i < cfg.pipes && cols.Count > 0; i++)
        {
            var col = cols[Mathf.FloorToInt(Hash01(DecorHash(i, 1279)) * cols.Count) % cols.Count];
            if (col == centreCol) continue;
            float h = cs * (0.5f + Hash01(DecorHash(i, 977)) * 0.9f);
            var pos = ColumnSurface(colTop, col, cfg, cs);
            MakeMeshProp(root, $"Pipe{i}", TowerMesh(), pos + Vector3.up * (cs * 0.02f),
                         Quaternion.identity, new Vector3(cs * 0.18f, h, cs * 0.18f), cfg.wallColor);
            MakeMeshProp(root, $"PipeCollar{i}", RailMesh(), pos + Vector3.up * h,
                         Quaternion.identity, new Vector3(cs * 0.26f, cs * 0.07f, cs * 0.26f), cfg.gearAccent);
        }
    }

    // One gear, spinning. Speed is inversely proportional to radius and the
    // direction alternates with `index`: that's what a meshed train actually does,
    // and it's the difference between "a machine" and "some spinning discs".
    void SpawnGear(Transform parent, string name, Vector3 pos, Quaternion rot,
                   float radius, int index, OrderWorkshopConfig cfg)
    {
        // Uniform scale, halved: GearMeshFactory's cog has outer radius 1, where the
        // local mesh this replaced had 0.5. A gear also wants its thickness tied to
        // its size — the old separate Y term made big cogs look like sheet metal.
        var t = MakeMeshProp(parent, name, GearMeshFactory.Get(10), pos, rot,
                             Vector3.one * (radius * 0.5f),
                             (index & 1) == 0 ? cfg.gearColor : cfg.gearAccent);
        var spin = t.gameObject.AddComponent<DecorGearSpin>();
        spin.Init(cfg.gearBaseSpin / Mathf.Max(0.15f, radius) * ((index & 1) == 0 ? 1f : -1f));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 1-3 — Observatory
    // ═════════════════════════════════════════════════════════════════════════

    void BuildObservatory(ObservatoryConfig cfg,
                          HashSet<Vector2Int> coveredCols,
                          Dictionary<Vector2Int, Vector3Int> colTop,
                          Vector2Int ext, float cs)
    {
        var root = new GameObject("Observatory").transform;
        root.SetParent(_buildingRoot.transform, false);

        var centreCol = new Vector2Int(cfg.origin.x + ext.x / 2, cfg.origin.z + ext.y / 2);
        Vector3 basePos = ColumnSurface(colTop, centreCol, cfg, cs);

        if (cfg.wallEnabled) BuildPrecinctWall(root, cfg, colTop, ext, cs);

        float drum = cfg.drumHeight;
        float rad  = cfg.domeRadius * cs;

        // ── Drum ─────────────────────────────────────────────────────────────
        MakeMeshProp(root, "Steps", RailMesh(), basePos + Vector3.up * (cs * 0.05f),
                     Quaternion.identity, new Vector3(rad * 2.5f, cs * 0.12f, rad * 2.5f), cfg.drumColor);
        MakeMeshProp(root, "Drum", TowerMesh(), basePos + Vector3.up * (cs * 0.10f),
                     Quaternion.identity, new Vector3(rad * 1.9f, drum, rad * 1.9f), cfg.drumColor);
        // Brass band where drum meets dome — the join is the read, without it the
        // dome looks like it's resting on the tower rather than turning on it.
        MakeMeshProp(root, "Ring", RailMesh(), basePos + Vector3.up * (drum + cs * 0.06f),
                     Quaternion.identity, new Vector3(rad * 2.05f, cs * 0.10f, rad * 2.05f), cfg.brassColor);

        // ── Dome + telescope ─────────────────────────────────────────────────
        // Both under one pivot so the slit and the barrel sweep together. A dome
        // whose slit doesn't follow the telescope is the one thing that would make
        // the whole prop read as broken.
        var pivot = new GameObject("DomePivot").transform;
        pivot.SetParent(root, false);
        pivot.position = basePos + Vector3.up * (drum + cs * 0.10f);

        MakeMeshProp(pivot, "Dome", DomeMesh(), pivot.position, Quaternion.identity,
                     new Vector3(rad * 2f, rad * 1.15f, rad * 2f), cfg.domeColor);

        // The slit: two shutter cheeks, leaving a gap the barrel points through.
        for (int i = 0; i < 2; i++)
        {
            float s = i == 0 ? 1f : -1f;
            MakeMeshProp(pivot, $"Shutter{i}", RailMesh(),
                         pivot.position + new Vector3(0f, rad * 0.52f, s * rad * 0.30f),
                         Quaternion.identity, new Vector3(rad * 1.5f, rad * 0.95f, rad * 0.30f),
                         cfg.drumColor);
        }

        var barrel = new GameObject("TelescopePivot").transform;
        barrel.SetParent(pivot, false);
        barrel.position = pivot.position + Vector3.up * (rad * 0.35f);
        MakeMeshProp(barrel, "Barrel", TowerMesh(),
                     barrel.position, Quaternion.Euler(-58f, 0f, 0f),
                     new Vector3(rad * 0.34f, rad * 2.3f, rad * 0.34f), cfg.brassColor);
        MakeMeshProp(barrel, "Eyepiece", RailMesh(),
                     barrel.position, Quaternion.identity,
                     new Vector3(rad * 0.44f, rad * 0.22f, rad * 0.44f), cfg.domeColor);

        var scan = pivot.gameObject.AddComponent<DecorSkyScan>();
        scan.Init(barrel, cfg.scanSpeed, cfg.scanTilt);

        // ── Armillary ────────────────────────────────────────────────────────
        // Beside the dome, not on it: two landmarks at different heights give the
        // plot a silhouette instead of one lump in the middle.
        if (cfg.armillaryRings > 0)
        {
            var armCol = new Vector2Int(cfg.origin.x + ext.x / 4, cfg.origin.z + ext.y * 3 / 4);
            Vector3 armPos = ColumnSurface(colTop, armCol, cfg, cs) + Vector3.up * (cs * 0.55f);
            MakeMeshProp(root, "ArmillaryStand", TowerMesh(),
                         armPos - Vector3.up * (cs * 0.55f), Quaternion.identity,
                         new Vector3(cs * 0.22f, cs * 0.55f, cs * 0.22f), cfg.drumColor);

            var rings = new GameObject("Armillary").transform;
            rings.SetParent(root, false);
            rings.position = armPos;
            for (int i = 0; i < cfg.armillaryRings; i++)
            {
                float tilt = 90f * i / Mathf.Max(1, cfg.armillaryRings);
                var ring = MakeMeshProp(rings, $"Ring{i}", RingMesh(), armPos,
                                        Quaternion.Euler(tilt, i * 37f, 0f),
                                        Vector3.one * (cs * (0.62f - i * 0.07f)), cfg.brassColor);
                ring.gameObject.AddComponent<DecorGearSpin>()
                    .Init((i % 2 == 0 ? 11f : -8f) + i * 3f, Vector3.up);
            }
        }

        // ── Sight stones ─────────────────────────────────────────────────────
        // Low markers around the plaza, each turned to face the dome — the plot
        // then reads as a place FOR observing, not just a building that observes.
        // Interior columns only — the rim belongs to the wall, and a stone sharing
        // a cell with a wall segment just clips through it.
        int wx0 = cfg.origin.x, wx1 = cfg.origin.x + ext.x - 1;
        int wz0 = cfg.origin.z, wz1 = cfg.origin.z + ext.y - 1;
        var cols = new List<Vector2Int>();
        foreach (var c in coveredCols)
            if (c.x > wx0 && c.x < wx1 && c.y > wz0 && c.y < wz1) cols.Add(c);
        cols.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

        for (int i = 0; i < cfg.sightStones && cols.Count > 0; i++)
        {
            var col = cols[Mathf.FloorToInt(Hash01(DecorHash(i, 613)) * cols.Count) % cols.Count];
            if (col == centreCol) continue;
            Vector3 pos = ColumnSurface(colTop, col, cfg, cs);
            Vector3 toDome = basePos - pos; toDome.y = 0f;
            if (toDome.sqrMagnitude < 0.0001f) continue;

            float h = cs * (0.28f + Hash01(DecorHash(i, 149)) * 0.34f);
            MakeMeshProp(root, $"Stone{i}", RailMesh(), pos + Vector3.up * (h * 0.5f),
                         Quaternion.LookRotation(toDome.normalized, Vector3.up),
                         new Vector3(cs * 0.16f, h, cs * 0.30f), cfg.drumColor);
            MakeMeshProp(root, $"StoneTip{i}", RailMesh(), pos + Vector3.up * h,
                         Quaternion.LookRotation(toDome.normalized, Vector3.up),
                         new Vector3(cs * 0.10f, cs * 0.06f, cs * 0.20f), cfg.brassColor);
        }

        // ── Star motes ───────────────────────────────────────────────────────
        // Deliberately ABOVE the plot rather than in the skybox: they have to move
        // with the camera's parallax to read as belonging to this place.
        if (cfg.stars > 0)
        {
            var field = new GameObject("Stars").transform;
            field.SetParent(root, false);
            var twinkle = field.gameObject.AddComponent<DecorStarField>();

            for (int i = 0; i < cfg.stars; i++)
            {
                float a = Hash01(DecorHash(i, 8191)) * Mathf.PI * 2f;
                float r = cs * (1.2f + Hash01(DecorHash(i, 3557)) * ext.x * 0.42f);
                float y = cs * (1.6f + Hash01(DecorHash(i, 2411)) * 2.8f);
                var pos = basePos + new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                float sz = cs * (0.05f + Hash01(DecorHash(i, 5507)) * 0.06f);

                var star = MakeMeshProp(field, $"Star{i}", RailMesh(), pos,
                                        Quaternion.Euler(45f, Hash01(DecorHash(i, 907)) * 90f, 45f),
                                        Vector3.one * sz, cfg.starColor);
                twinkle.Add(star, Hash01(DecorHash(i, 1223)) * Mathf.PI * 2f);
            }
        }
    }

    // A continuous low wall around the plot's rim, with a gateway centred on each
    // side and taller posts at the corners.
    //
    // Built from the FOOTPRINT rectangle: the wall is what makes this a precinct,
    // so it has to be a clean rectangle. Walking through it still works — the
    // walkable proxy is the ground blocks, and this is decoration standing on top.
    void BuildPrecinctWall(Transform root, ObservatoryConfig cfg,
                           Dictionary<Vector2Int, Vector3Int> colTop,
                           Vector2Int ext, float cs)
    {
        var wall = new GameObject("PrecinctWall").transform;
        wall.SetParent(root, false);

        int x0 = cfg.origin.x, x1 = cfg.origin.x + ext.x - 1;
        int z0 = cfg.origin.z, z1 = cfg.origin.z + ext.y - 1;
        float h = cfg.wallHeight;

        // Gateway cells, centred on each run. Half-open on even spans, which is
        // fine — a gate that's off-centre by half a cell isn't readable at this
        // camera distance, and forcing symmetry would need an odd footprint.
        int gx0 = cfg.origin.x + (ext.x - cfg.gateWidth) / 2;
        int gz0 = cfg.origin.z + (ext.y - cfg.gateWidth) / 2;

        bool IsCorner(int x, int z) => (x == x0 || x == x1) && (z == z0 || z == z1);

        for (int x = x0; x <= x1; x++)
        for (int z = z0; z <= z1; z++)
        {
            bool edge = x == x0 || x == x1 || z == z0 || z == z1;
            if (!edge) continue;

            bool corner = IsCorner(x, z);
            if (!corner)
            {
                // Gate openings on the two runs that cross the centre line.
                if ((z == z0 || z == z1) && x >= gx0 && x < gx0 + cfg.gateWidth) continue;
                if ((x == x0 || x == x1) && z >= gz0 && z < gz0 + cfg.gateWidth) continue;
            }

            var col = new Vector2Int(x, z);
            Vector3 pos = ColumnSurface(colTop, col, cfg, cs);
            float ph = corner ? h * cfg.cornerPostScale : h;

            MakeMeshProp(wall, corner ? $"Post_{x}_{z}" : $"Wall_{x}_{z}", RailMesh(),
                         pos + Vector3.up * (ph * 0.5f), Quaternion.identity,
                         new Vector3(cs * (corner ? 0.44f : 0.30f), ph,
                                     cs * (corner ? 0.44f : 0.30f)),
                         cfg.wallColor);

            // A capstone course along the run ties the segments into one wall
            // instead of a row of separate blocks.
            if (!corner)
                MakeMeshProp(wall, $"Cap_{x}_{z}", RailMesh(),
                             pos + Vector3.up * ph, Quaternion.identity,
                             new Vector3(cs * 0.42f, cs * 0.08f, cs * 0.42f), cfg.brassColor);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Shared helpers
    // ═════════════════════════════════════════════════════════════════════════

    // Top surface of a plot column, falling back to the plot floor when coverage
    // dropped that exact cell — props must never end up hanging in the air just
    // because the coverage roll frayed the spot they were aimed at.
    Vector3 ColumnSurface(Dictionary<Vector2Int, Vector3Int> colTop, Vector2Int col,
                          MapDecorConfig cfg, float cs)
    {
        if (colTop.TryGetValue(col, out var top)) return BlockTop(top);
        return BlockTop(new Vector3Int(col.x, cfg.origin.y, col.y));
    }

    // ── Meshes ───────────────────────────────────────────────────────────────

    static Mesh _domeMesh, _ringMesh, _gableMesh;

    // Hemisphere, unit diameter, sitting on y = 0.
    static Mesh DomeMesh()
    {
        if (_domeMesh != null) return _domeMesh;
        if (TryLoadBaked("DecorDome", ref _domeMesh)) return _domeMesh;
        var v = new List<Vector3>();
        var t = new List<int>();

        const int seg = 12, rings = 5;
        for (int r = 0; r < rings; r++)
        {
            float p0 = r / (float)rings * Mathf.PI * 0.5f;
            float p1 = (r + 1) / (float)rings * Mathf.PI * 0.5f;
            float y0 = Mathf.Sin(p0), y1 = Mathf.Sin(p1);
            float r0 = Mathf.Cos(p0) * 0.5f, r1 = Mathf.Cos(p1) * 0.5f;

            for (int i = 0; i < seg; i++)
            {
                float a0 = i / (float)seg * Mathf.PI * 2f;
                float a1 = (i + 1) / (float)seg * Mathf.PI * 2f;
                Quad(v, t,
                     new Vector3(Mathf.Cos(a0) * r0, y0, Mathf.Sin(a0) * r0),
                     new Vector3(Mathf.Cos(a1) * r0, y0, Mathf.Sin(a1) * r0),
                     new Vector3(Mathf.Cos(a1) * r1, y1, Mathf.Sin(a1) * r1),
                     new Vector3(Mathf.Cos(a0) * r1, y1, Mathf.Sin(a0) * r1));
            }
        }
        _domeMesh = Finish("DecorDome", v, t);
        return _domeMesh;
    }

    // Thin torus in the XZ plane, unit outer diameter — the armillary bands.
    static Mesh RingMesh()
    {
        if (_ringMesh != null) return _ringMesh;
        if (TryLoadBaked("DecorRing", ref _ringMesh)) return _ringMesh;
        var v = new List<Vector3>();
        var t = new List<int>();

        const int seg = 20;
        const float rOut = 0.50f, rIn = 0.455f, half = 0.022f;
        for (int i = 0; i < seg; i++)
        {
            float a0 = i / (float)seg * Mathf.PI * 2f;
            float a1 = (i + 1) / (float)seg * Mathf.PI * 2f;
            Vector3 o0 = new(Mathf.Cos(a0) * rOut, 0f, Mathf.Sin(a0) * rOut);
            Vector3 o1 = new(Mathf.Cos(a1) * rOut, 0f, Mathf.Sin(a1) * rOut);
            Vector3 i0 = new(Mathf.Cos(a0) * rIn,  0f, Mathf.Sin(a0) * rIn);
            Vector3 i1 = new(Mathf.Cos(a1) * rIn,  0f, Mathf.Sin(a1) * rIn);
            Vector3 up = Vector3.up * half, dn = Vector3.down * half;

            Quad(v, t, i0 + up, i1 + up, o1 + up, o0 + up);
            Quad(v, t, o0 + dn, o1 + dn, i1 + dn, i0 + dn);
            Quad(v, t, o0 + dn, o0 + up, o1 + up, o1 + dn);
            Quad(v, t, i1 + dn, i1 + up, i0 + up, i0 + dn);
        }
        _ringMesh = Finish("DecorRing", v, t);
        return _ringMesh;
    }

    // Gable roof: a triangular prism, ridge running along X, base at y = 0.
    static Mesh GableMesh()
    {
        if (_gableMesh != null) return _gableMesh;
        if (TryLoadBaked("DecorGable", ref _gableMesh)) return _gableMesh;
        var v = new List<Vector3>();
        var t = new List<int>();
        const float h = 0.5f;

        Vector3 a = new(-h, 0f, -h), b = new(h, 0f, -h);
        Vector3 c = new(h, 0f, h),   d = new(-h, 0f, h);
        Vector3 r0 = new(-h, 1f, 0f), r1 = new(h, 1f, 0f);

        Quad(v, t, d, c, r1, r0);   // +Z slope
        Quad(v, t, b, a, r0, r1);   // -Z slope
        Tri(v, t, a, d, r0);        // -X gable end
        Tri(v, t, c, b, r1);        // +X gable end
        Quad(v, t, a, b, c, d);     // underside

        _gableMesh = Finish("DecorGable", v, t);
        return _gableMesh;
    }

    // ── Behaviours ───────────────────────────────────────────────────────────

    // Constant spin about a local axis. Used by both the workshop's gears and the
    // observatory's armillary rings.
    class DecorGearSpin : MonoBehaviour
    {
        float   _speed;
        Vector3 _axis = Vector3.up;

        public void Init(float degreesPerSecond, Vector3? axis = null)
        {
            _speed = degreesPerSecond;
            if (axis.HasValue) _axis = axis.Value;
        }

        void Update() => transform.Rotate(_axis, _speed * Time.deltaTime, Space.Self);
    }

    // Dome sweeps the horizon; the telescope inside rocks up and down as it goes,
    // so the pair never settles into one readable loop.
    class DecorSkyScan : MonoBehaviour
    {
        Transform _barrel;
        float     _speed, _tilt, _t;

        public void Init(Transform barrel, float speed, float tilt)
        {
            _barrel = barrel; _speed = speed; _tilt = tilt;
        }

        void Update()
        {
            _t += Time.deltaTime;
            transform.Rotate(Vector3.up, _speed * Time.deltaTime, Space.Self);
            if (_barrel != null)
                _barrel.localRotation = Quaternion.Euler(Mathf.Sin(_t * 0.35f) * _tilt, 0f, 0f);
        }
    }

    // Star motes: slow brightness breathing on a per-star phase, plus a lazy drift
    // so the field never looks pinned to the geometry under it.
    class DecorStarField : MonoBehaviour
    {
        struct Star { public Transform t; public Vector3 home; public float phase; public Vector3 scale; }
        readonly List<Star> _stars = new();

        public void Add(Transform t, float phase)
        {
            if (t == null) return;
            _stars.Add(new Star { t = t, home = t.position, phase = phase, scale = t.localScale });
        }

        void Update()
        {
            float time = Time.time;
            for (int i = 0; i < _stars.Count; i++)
            {
                var s = _stars[i];
                if (s.t == null) continue;
                float k = 0.65f + 0.35f * Mathf.Sin(time * 1.7f + s.phase);
                s.t.localScale = s.scale * k;
                s.t.position   = s.home + Vector3.up * (Mathf.Sin(time * 0.4f + s.phase) * 0.06f);
                s.t.Rotate(Vector3.up, 22f * Time.deltaTime, Space.Self);
            }
        }
    }
}
