using System.Collections.Generic;
using UnityEngine;

// The rest of the world past the industrial middle: the volcano, and beyond it
// the ocean and the desert. Same machinery as every other plot: ground,
// walkability, the sink-and-rise reveal, and mist over it until its gate is
// cleared. These three shape their GROUND as well as dressing it. HeightAt
// raises a cone, dunes, mesas, a stepped pyramid and islands; CellColor paints
// them in strata.
//
// Gated on chapters that don't exist yet, so each waits under its own mist
// bank. LevelMapController.previewAllRegions stands every region up at once to
// look at the whole map.

[System.Serializable]
public class VolcanoConfig : MapDecorConfig
{
    [Header("Cone")]
    [Tooltip("Height of the crater rim above the plot floor, in cells.")]
    [Range(2, 16)] public int coneHeight = 12;
    [Tooltip("Where the slope meets the plain, as a fraction of the plot's half-size.")]
    [Range(0.3f, 1f)] public float coneRadius = 0.78f;
    [Tooltip("The crater's opening, same units.")]
    [Range(0.05f, 0.4f)] public float craterRadius = 0.17f;
    [Tooltip("How far the crater floor sits below the rim, in cells.")]
    [Range(0, 4)] public int craterDepth = 2;
    [Tooltip("Slope profile. 1 = a straight cone; higher = concave, gentle at the foot and steepening to the rim.")]
    [Range(0.6f, 2.5f)] public float slopeCurve = 1.35f;

    [Header("Lava")]
    [Range(0, 8)] public int lavaRivers = 5;
    [Tooltip("River width, in cells.")]
    [Range(0.3f, 2.5f)] public float riverWidth = 1.1f;
    [ColorUsage(false, true)] public Color lavaColor = new Color(2.6f, 0.75f, 0.12f);
    [ColorUsage(false, true)] public Color lavaDim   = new Color(1.3f, 0.28f, 0.06f);
    public Color crustColor = new Color(0.28f, 0.12f, 0.08f);

    [Header("Rock")]
    public Color rockColor   = new Color(0.24f, 0.21f, 0.21f);
    public Color ashColor    = new Color(0.40f, 0.37f, 0.36f);
    public Color scorchColor = new Color(0.33f, 0.25f, 0.19f);

    [Header("Dressing")]
    [Range(0, 32)] public int smokePuffs     = 16;
    [Range(0, 60)] public int embers         = 34;
    [Range(0, 48)] public int boulders       = 28;
    [Range(0, 24)] public int basaltClusters = 10;
    [Range(0, 40)] public int charredTrees   = 20;
    [Range(0, 24)] public int obsidianShards = 12;
    [Tooltip("A stone stair up the one flank the lava never takes, to a gate on the rim.")]
    public bool pilgrimStair = true;
    public Color smokeColor    = new Color(0.30f, 0.28f, 0.29f);
    public Color obsidianColor = new Color(0.12f, 0.09f, 0.16f);
    public Color gateColor     = new Color(0.58f, 0.13f, 0.10f);

    public override string RootName => "Volcano";

    public VolcanoConfig()
    {
        enabled       = true;
        gateLevelId   = "2-1";
        origin        = new Vector3Int(23, 2, 9);
        size          = new Vector2Int(29, 29);
        organic       = 0.7f;
        outlineSeed   = 4;
        soilColor     = new Color(0.24f, 0.21f, 0.21f);
        soilJitter    = 0.12f;
        growYawOffset = 30f;
        growRiseHeight = 14f;
        growAsideText = "Past the last machine the ground runs hot underfoot. Something below is still being made.";
    }

    public float CraterR => craterRadius / Mathf.Max(0.05f, coneRadius);

    // Distance from the vent as a fraction of the cone (0 = the vent, 1 = the
    // foot), measured against a lumpy base so the foot isn't a perfect circle.
    public float ConeR(Vector2Int c)
    {
        var p = Local(c);
        float a = Mathf.Atan2(p.y, p.x);
        float r = coneRadius * (1f + 0.1f * Mathf.Sin(a * 3f + outlineSeed) + 0.06f * Mathf.Sin(a * 5f + outlineSeed * 2.1f));
        return p.magnitude / Mathf.Max(0.05f, r);
    }

    public bool InCrater(Vector2Int c) => ConeR(c) < CraterR;

    public float RiverBearing(int i) =>
        (i + 0.3f * Mathf.Sin(outlineSeed * 1.7f + i * 2.3f)) / Mathf.Max(1, lavaRivers) * Mathf.PI * 2f + outlineSeed;

    // Whether a lava river runs through this column, and how far down it (0 at
    // the rim, 1 at its end). Rivers wind down from the rim on fixed bearings,
    // narrow as they go, and stop at different heights.
    public bool OnRiver(Vector2Int c, out float along)
    {
        along = 0f;
        if (lavaRivers <= 0) return false;
        float r = ConeR(c);
        if (r < CraterR || r > 0.94f) return false;
        var p = Local(c);
        float a = Mathf.Atan2(p.y, p.x);
        var e = Extent;
        float coneCells = (e.x + e.y) * 0.25f * coneRadius;
        for (int i = 0; i < lavaRivers; i++)
        {
            float rEnd = 0.68f + 0.24f * Mathf.Repeat(i * 0.618f + outlineSeed * 0.31f, 1f);
            if (r > rEnd) continue;
            float wind = RiverBearing(i) + 0.35f * Mathf.Sin(r * 7f + i * 1.9f);
            float d = Mathf.DeltaAngle(a * Mathf.Rad2Deg, wind * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            float t = (r - CraterR) / Mathf.Max(0.01f, rEnd - CraterR);
            if (Mathf.Abs(d) * r * coneCells <= riverWidth * (1f - 0.35f * t))
            {
                along = t;
                return true;
            }
        }
        return false;
    }

    public override int HeightAt(Vector2Int c)
    {
        float r = ConeR(c);
        if (r >= 1f) return 0;
        if (r < CraterR) return Mathf.Max(1, coneHeight - craterDepth);
        float k = (1f - r) / Mathf.Max(0.01f, 1f - CraterR);   // 0 at the foot, 1 at the rim
        return Mathf.Clamp(Mathf.RoundToInt(Mathf.Pow(k, slopeCurve) * coneHeight), 0, coneHeight);
    }

    public override Color CellColor(Vector3Int cell)
    {
        var c = new Vector2Int(cell.x, cell.z);
        bool surface = cell.y == origin.y + HeightAt(c);
        if (InCrater(c)) return surface ? lavaColor : rockColor;
        if (surface && OnRiver(c, out float along)) return Color.Lerp(lavaColor, lavaDim, along);
        if (ConeR(c) >= 1f) return surface ? Shade(scorchColor, 0.94f + 0.12f * Noise01(c.x, c.y)) : Shade(scorchColor, 0.82f);

        // Strata two cells deep, greying to ash toward the rim.
        int band = (cell.y - origin.y) / 2;
        var rock = (band & 1) == 0 ? rockColor : Shade(rockColor, 1.18f);
        float f  = (cell.y - origin.y) / (float)Mathf.Max(1, coneHeight);
        var col  = Color.Lerp(rock, ashColor, Mathf.SmoothStep(0.45f, 0.95f, f));
        return surface ? col : Shade(col, 0.86f);
    }

    public override Color SoilAt(Vector2Int c) => CellColor(new Vector3Int(c.x, origin.y + HeightAt(c), c.y));
}

[System.Serializable]
public class DesertConfig : MapDecorConfig
{
    [Header("Sand")]
    public Color sandColor = new Color(0.90f, 0.74f, 0.49f);
    public Color sandShade = new Color(0.80f, 0.60f, 0.38f);
    [Tooltip("Dune crest height above the sand floor, in cells.")]
    [Range(0, 4)] public int duneHeight = 2;
    [Tooltip("Distance between dune crests, in cells.")]
    [Range(3f, 16f)] public float duneSpacing = 6.5f;

    [Header("Mesas")]
    [Range(0, 4)] public int mesas = 2;
    [Range(2, 8)] public int mesaHeight = 4;
    [Tooltip("Mesa radius, as a fraction of the plot's half-size.")]
    [Range(0.08f, 0.35f)] public float mesaRadius = 0.16f;
    public Color mesaColor   = new Color(0.74f, 0.40f, 0.26f);
    public Color strataColor = new Color(0.86f, 0.58f, 0.38f);

    [Header("Pyramid")]
    public bool pyramid = true;
    [Tooltip("Steps of the pyramid: it is 2×steps+1 cells across and steps+1 cells tall above the sand.")]
    [Range(2, 6)] public int pyramidSteps = 3;
    [Tooltip("Footprint-local position (-1..1).")]
    public Vector2 pyramidAt = new Vector2(0.42f, -0.30f);
    public Color stoneColor = new Color(0.92f, 0.82f, 0.60f);
    [ColorUsage(false, true)] public Color capColor = new Color(1.0f, 0.82f, 0.32f);

    [Header("Oasis")]
    public bool oasis = true;
    public Vector2 oasisAt = new Vector2(-0.38f, 0.30f);
    [Range(0.08f, 0.35f)] public float oasisRadius = 0.17f;
    public Color waterColor = new Color(0.22f, 0.58f, 0.64f);
    public Color grassColor = new Color(0.45f, 0.58f, 0.26f);

    [Header("Dressing")]
    [Range(0, 30)] public int cacti  = 14;
    [Range(0, 12)] public int palms  = 6;
    [Range(0, 12)] public int ruins  = 5;
    [Range(0, 16)] public int scrub  = 8;
    [Tooltip("A caravan walking the trail between the oasis and the pyramid.")]
    [Range(0, 5)]  public int camels = 3;
    public Color cactusColor = new Color(0.30f, 0.52f, 0.30f);
    public Color palmLeaf    = new Color(0.30f, 0.56f, 0.24f);
    public Color palmTrunk   = new Color(0.52f, 0.38f, 0.24f);
    public Color camelColor  = new Color(0.76f, 0.56f, 0.34f);

    public override string RootName => "Desert";

    // Sand floor, in cells over origin.y. The oasis pool sits a step below it.
    public const int Floor = 1;

    static readonly Vector2[] MesaSpots =
    {
        new Vector2(-0.58f, -0.42f), new Vector2(0.12f, 0.62f), new Vector2(0.68f, 0.36f), new Vector2(-0.12f, -0.72f),
    };

    public DesertConfig()
    {
        enabled       = true;
        gateLevelId   = "4-1";
        origin        = new Vector3Int(39, 2, -2);
        size          = new Vector2Int(19, 16);
        organic       = 0.55f;
        outlineSeed   = 5;
        soilColor     = new Color(0.90f, 0.74f, 0.49f);
        soilJitter    = 0.05f;
        growYawOffset = -40f;
        growRiseHeight = 6f;
        growAsideText = "Beyond the fire the land dried to sand, and the sand kept everything it was given.";
    }

    public float OasisR(Vector2Int c) =>
        oasis ? (Local(c) - oasisAt).magnitude / Mathf.Max(0.01f, oasisRadius) : 99f;

    // Chebyshev distance to the pyramid's apex column, in cells.
    public int PyramidRing(Vector2Int c)
    {
        if (!pyramid) return int.MaxValue;
        var p = ColumnAt(pyramidAt);
        return Mathf.Max(Mathf.Abs(c.x - p.x), Mathf.Abs(c.y - p.y));
    }

    public bool OnMesa(Vector2Int c)
    {
        var p = Local(c);
        for (int i = 0; i < Mathf.Min(mesas, MesaSpots.Length); i++)
        {
            var d = p - MesaSpots[i];
            float a = Mathf.Atan2(d.y, d.x);
            float r = mesaRadius * (1f + 0.18f * Mathf.Sin(a * 3f + i * 2.1f + outlineSeed) + 0.1f * Mathf.Sin(a * 5f + i));
            if (d.magnitude < r) return true;
        }
        return false;
    }

    // Distance in cells to the trail from the oasis to the pyramid.
    public float TrailDistance(Vector2Int c)
    {
        if (!oasis || !pyramid) return 99f;
        Vector2 a = ColumnAt(oasisAt), b = ColumnAt(pyramidAt), p = c;
        var ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.01f, ab.sqrMagnitude));
        return (a + ab * t - p).magnitude;
    }

    public bool OnTrail(Vector2Int c) => TrailDistance(c) <= 0.75f;

    // Crests slanting across the plot, meandering.
    public int Dune(Vector2Int c)
    {
        if (duneHeight <= 0) return 0;
        float u = (c.x * 0.8f + c.y * 0.45f) / duneSpacing * Mathf.PI * 2f + Mathf.Sin(c.y * 0.35f + outlineSeed) * 1.3f;
        float s = Mathf.Max(0f, Mathf.Sin(u));
        return Mathf.RoundToInt(s * s * duneHeight);
    }

    public override int HeightAt(Vector2Int c)
    {
        float o = OasisR(c);
        if (o < 1f) return 0;
        int ring = PyramidRing(c);
        if (ring <= pyramidSteps) return Floor + 1 + (pyramidSteps - ring);
        // The bank, the pyramid's forecourt and the trail are level ground.
        if (o < 1.7f || ring <= pyramidSteps + 2 || OnTrail(c)) return Floor;
        if (OnMesa(c)) return Floor + mesaHeight;
        return Floor + Dune(c);
    }

    public override Color CellColor(Vector3Int cell)
    {
        var c = new Vector2Int(cell.x, cell.z);
        int h = HeightAt(c);
        bool surface = cell.y == origin.y + h;
        float o = OasisR(c);
        if (o < 1f) return surface ? waterColor : sandShade;
        if (PyramidRing(c) <= pyramidSteps)
            return ((cell.y - origin.y) & 1) == 0 ? stoneColor : Shade(stoneColor, 0.88f);
        if (h == Floor + mesaHeight && OnMesa(c))
        {
            if (surface) return Color.Lerp(mesaColor, sandColor, 0.35f);
            int band = cell.y - origin.y;
            return band % 3 == 1 ? strataColor : band % 3 == 2 ? Shade(mesaColor, 0.88f) : mesaColor;
        }
        if (surface && o < 1.7f) return grassColor;
        if (surface && OnTrail(c)) return Color.Lerp(sandShade, stoneColor, 0.25f);

        // Sand: pale on the crests, darker in the troughs.
        float k = duneHeight > 0 ? Mathf.Clamp01((cell.y - origin.y - Floor) / (float)duneHeight) : 0f;
        var sand = Shade(Color.Lerp(sandShade, sandColor, 0.45f + 0.55f * k), 0.96f + 0.08f * Noise01(c.x, c.y));
        return surface ? sand : Shade(sand, 0.9f);
    }

    public override Color SoilAt(Vector2Int c) => CellColor(new Vector3Int(c.x, origin.y + HeightAt(c), c.y));
}

[System.Serializable]
public class OceanConfig : MapDecorConfig
{
    [Header("Water")]
    public Color deepColor    = new Color(0.10f, 0.30f, 0.52f);
    public Color shallowColor = new Color(0.22f, 0.60f, 0.70f);
    public Color foamColor    = new Color(0.92f, 0.97f, 1.00f);

    [Header("Islands")]
    [Tooltip("Each island: x, y = footprint-local centre (-1..1), z = radius in the same units. The first holds the lighthouse.")]
    public Vector3[] islands =
    {
        new Vector3(-0.30f, 0.05f, 0.24f), new Vector3(0.42f, 0.42f, 0.14f), new Vector3(0.55f, -0.45f, 0.11f),
        new Vector3(-0.02f, -0.60f, 0.10f),
    };
    public Color sandColor  = new Color(0.92f, 0.84f, 0.62f);
    public Color grassColor = new Color(0.40f, 0.62f, 0.32f);
    public Color cliffColor = new Color(0.52f, 0.50f, 0.48f);

    [Header("Dressing")]
    public bool lighthouse = true;
    public Color stripeA = new Color(0.92f, 0.92f, 0.90f);
    public Color stripeB = new Color(0.78f, 0.20f, 0.18f);
    [ColorUsage(false, true)] public Color lampColor = new Color(2.2f, 1.9f, 1.1f);
    [Tooltip("One is moored at the dock; the rest sail slow circles out in the bay.")]
    [Range(0, 8)]  public int boats       = 4;
    [Range(0, 16)] public int buoys       = 8;
    [Range(0, 40)] public int foam        = 26;
    [Range(0, 16)] public int seaRocks    = 9;
    [Range(0, 10)] public int gulls       = 5;
    [Range(0, 16)] public int islandPalms = 8;
    public Color hullColor = new Color(0.55f, 0.36f, 0.22f);
    public Color sailColor = new Color(0.96f, 0.94f, 0.86f);

    public override string RootName => "Ocean";

    public OceanConfig()
    {
        enabled       = true;
        gateLevelId   = "3-1";
        origin        = new Vector3Int(12, 1, -5);
        size          = new Vector2Int(32, 18);
        organic       = 0.5f;
        outlineSeed   = 6;
        soilColor     = new Color(0.10f, 0.30f, 0.52f);
        soilJitter    = 0.04f;
        growYawOffset = 150f;
        growAsideText = "At the foot of the fire the land ran out, and the sea began.";
    }

    // Distance to the nearest island's edge, in that island's radii (< 1 = on land).
    public float IslandR(Vector2Int c, out int which)
    {
        which = -1;
        float best = 99f;
        if (islands == null) return best;
        var p = Local(c);
        for (int i = 0; i < islands.Length; i++)
        {
            var d = p - new Vector2(islands[i].x, islands[i].y);
            float a = Mathf.Atan2(d.y, d.x);
            float r = Mathf.Max(0.02f, islands[i].z) * (1f + 0.16f * Mathf.Sin(a * 3f + i * 1.7f + outlineSeed) + 0.08f * Mathf.Sin(a * 5f + i));
            float k = d.magnitude / r;
            if (k < best) { best = k; which = i; }
        }
        return best;
    }

    public override int HeightAt(Vector2Int c)
    {
        float k = IslandR(c, out int i);
        if (k >= 1f) return 0;
        if (i == 0 && k < 0.45f) return 3;   // the lighthouse headland
        return k < 0.62f ? 2 : 1;            // grass, then a ring of beach
    }

    public override Color CellColor(Vector3Int cell)
    {
        var c = new Vector2Int(cell.x, cell.z);
        float k = IslandR(c, out _);
        int h = HeightAt(c);
        bool surface = cell.y == origin.y + h;
        if (k >= 1f)
        {
            // Shallows ring the islands and the rest deepens, with a little noise
            // so the water doesn't band.
            float shallow = Mathf.Clamp01((2.2f - k) / 1.2f) + (Noise01(c.x, c.y) - 0.5f) * 0.14f;
            var w = Color.Lerp(deepColor, shallowColor, Mathf.Clamp01(shallow));
            return surface ? w : Shade(deepColor, 0.8f);
        }
        if (!surface) return cell.y - origin.y >= 1 && h >= 2 ? cliffColor : sandColor;
        if (h == 1) return sandColor;
        if (h == 3) return Color.Lerp(grassColor, cliffColor, 0.25f);
        return grassColor;
    }

    public override Color SoilAt(Vector2Int c) => CellColor(new Vector3Int(c.x, origin.y + HeightAt(c), c.y));
}

public partial class LevelMapController : MonoBehaviour
{
    // ═════════════════════════════════════════════════════════════════════════
    // Volcano
    // ═════════════════════════════════════════════════════════════════════════

    void BuildVolcano(VolcanoConfig cfg, HashSet<Vector2Int> covered,
                      Dictionary<Vector2Int, Vector3Int> colTop, Vector2Int ext, float cs)
    {
        var root = new GameObject("VolcanoProps").transform;
        root.SetParent(_buildingRoot.transform, false);

        var cols  = SortedCols(covered);
        var taken = new HashSet<Vector2Int>();
        Reserve(NpcColumn(cfg, ext), 1, taken);
        Reserve(GameColumn(cfg, ext), 1, taken);

        // ── Lava ─────────────────────────────────────────────────────────────
        // One driver for every molten top, its phase running downhill, so the
        // flow reads as pouring rather than blinking.
        var lava = PulseOn(root, 1.4f);
        var vent = Vector3.zero;
        int ventN = 0;
        foreach (var c in cols)
        {
            bool crater = cfg.InCrater(c);
            float along = 0f;
            if (!crater && !cfg.OnRiver(c, out along)) continue;
            taken.Add(c);
            var p = ColumnSurface(colTop, c, cfg, cs);
            if (crater) { vent += p; ventN++; }
            var skin = MakeMeshProp(root, $"Lava_{c.x}_{c.y}", RailMesh(), p + Vector3.up * (cs * 0.02f), Quaternion.identity,
                                    new Vector3(cs * 1.002f, cs * 0.04f, cs * 1.002f), cfg.lavaColor);
            var hot  = Color.Lerp(cfg.lavaColor, cfg.lavaDim, along);
            var cool = Color.Lerp(cfg.lavaDim, cfg.crustColor, along * 0.8f);
            lava.Add(skin.GetComponent<Renderer>(), cool, hot, -along * 5f + Hash01(DecorHash(c.x, c.y)) * 0.6f);
        }

        if (ventN > 0)
        {
            vent /= ventN;
            SpawnSmoke(root, vent, 5, cfg.lavaColor, cs, 71, size: 0.3f, rise: 0.35f, life: 1.8f, spread: 0.7f, drift: Vector3.zero);
            SpawnSmoke(root, vent + Vector3.up * (cs * 0.3f), cfg.smokePuffs, cfg.smokeColor, cs, 73,
                       size: 1.1f, rise: 7f, life: 9f, spread: 0.8f, drift: new Vector3(2.2f, 0f, 1.0f));
            SpawnSmoke(root, vent, cfg.embers, cfg.lavaColor, cs, 79,
                       size: 0.09f, rise: 4.5f, life: 3.2f, spread: 2.2f, drift: new Vector3(0.6f, 0f, 0.3f));
        }

        // ── Pilgrim stair ────────────────────────────────────────────────────
        if (cfg.pilgrimStair) BuildPilgrimStair(root, cfg, covered, colTop, cs, taken);

        // Steam where the ground still breathes: a few vents on the flanks.
        var flank = cols.FindAll(c => !taken.Contains(c) && cfg.ConeR(c) > 0.35f && cfg.ConeR(c) < 0.85f);
        int si = 0;
        foreach (var c in Scatter(flank, 3, 3307, 3f, taken))
            SpawnSmoke(root, ColumnSurface(colTop, c, cfg, cs), 4, new Color(0.9f, 0.9f, 0.9f), cs, 90 + si++,
                       size: 0.35f, rise: 1.6f, life: 3.5f, spread: 0.15f, drift: new Vector3(0.5f, 0f, 0.2f));

        var free = cols.FindAll(c => !taken.Contains(c));

        // ── Boulders ─────────────────────────────────────────────────────────
        int bi = 0;
        foreach (var c in Scatter(free, cfg.boulders, 3301, 1.5f, taken))
        {
            var p = ColumnSurface(colTop, c, cfg, cs);
            int h = DecorHash(c.x, c.y);
            float s = cs * (0.35f + 0.4f * Hash01(h ^ 91));
            MakeMeshProp(root, $"Boulder{bi++}", PuffMesh(), p + Vector3.up * (s * 0.22f),
                         Quaternion.Euler(Hash01(h) * 40f, Hash01(h + 1) * 360f, Hash01(h + 2) * 30f),
                         new Vector3(s, s * 0.7f, s * 0.9f), Tint(cfg.rockColor, 0.8f + 0.3f * Hash01(h + 3)));
        }

        // ── Basalt columns, down on the plain ────────────────────────────────
        var low = free.FindAll(c => !taken.Contains(c) && cfg.ConeR(c) > 0.7f);
        int ci = 0;
        foreach (var c in Scatter(low, cfg.basaltClusters, 3319, 2.5f, taken))
        {
            var p = ColumnSurface(colTop, c, cfg, cs);
            int n = 3 + Mathf.FloorToInt(Hash01(DecorHash(c.x, c.y ^ 7)) * 4f);
            for (int k = 0; k < n; k++)
            {
                float a  = k / (float)n * Mathf.PI * 2f + Hash01(DecorHash(c.x, k));
                float rr = k == 0 ? 0f : cs * 0.22f;
                float hh = cs * (0.35f + 0.9f * Hash01(DecorHash(c.y, k * 13 + 5)));
                MakeMeshProp(root, $"Basalt{ci}_{k}", HexMesh(), p + new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr),
                             Quaternion.Euler(0f, k * 23f, 0f), new Vector3(cs * 0.22f, hh, cs * 0.22f),
                             Tint(cfg.rockColor, 0.7f + 0.25f * Hash01(DecorHash(k, c.x))));
            }
            ci++;
        }

        // ── Obsidian ─────────────────────────────────────────────────────────
        int oi = 0;
        foreach (var c in Scatter(free.FindAll(c => !taken.Contains(c)), cfg.obsidianShards, 3329, 2f, taken))
        {
            var p = ColumnSurface(colTop, c, cfg, cs);
            int h = DecorHash(c.y, c.x);
            MakeMeshProp(root, $"Obsidian{oi++}", PyramidMesh(), p - Vector3.up * (cs * 0.05f),
                         Quaternion.Euler((Hash01(h) - 0.5f) * 40f, Hash01(h + 1) * 360f, (Hash01(h + 2) - 0.5f) * 40f),
                         new Vector3(cs * 0.2f, cs * (0.45f + 0.4f * Hash01(h + 3)), cs * 0.16f), cfg.obsidianColor);
        }

        // ── Charred trees, round the foot ────────────────────────────────────
        var mat = GroveMaterial();
        if (mat != null && cfg.charredTrees > 0)
        {
            var dead = TreeMesh.Recipe.Tree();
            dead.leafSize = 0f; dead.leavesPerTip = 0; dead.innerLeaves = false;
            dead.bark = new Color(0.13f, 0.11f, 0.11f);
            dead.height = 2.6f; dead.levels = 3; dead.spreadDeg = 46f; dead.droop = 0.2f;
            var rng = new System.Random(cfg.outlineSeed * 1543 + cfg.origin.x);
            var meshes = new[] { TreeMesh.Build(dead, rng.Next()), TreeMesh.Build(dead, rng.Next()) };
            var ring = free.FindAll(c => !taken.Contains(c) && cfg.ConeR(c) > 0.8f);
            int ti = 0;
            foreach (var c in Scatter(ring, cfg.charredTrees, 3331, 1.8f, taken))
            {
                PlantGrove(root, meshes[ti % 2], mat, ColumnSurface(colTop, c, cfg, cs),
                           0.35f + 0.2f * Hash01(DecorHash(c.x, c.y ^ 3)), Hash01(DecorHash(c.y, c.x)) * 360f, false, $"Charred{ti}");
                ti++;
            }
        }
    }

    // Slabs up the flank halfway between the first two rivers, to a gate on the rim.
    void BuildPilgrimStair(Transform root, VolcanoConfig cfg, HashSet<Vector2Int> covered,
                           Dictionary<Vector2Int, Vector3Int> colTop, float cs, HashSet<Vector2Int> taken)
    {
        float bearing = cfg.RiverBearing(0) + Mathf.PI / Mathf.Max(1, cfg.lavaRivers);
        var dir = new Vector2(Mathf.Cos(bearing), Mathf.Sin(bearing));
        var steps = new List<Vector2Int>();
        for (int k = 0; k <= 48; k++)
        {
            float r = Mathf.Lerp(1.02f, cfg.CraterR + 0.03f, k / 48f) * cfg.coneRadius;
            var c = cfg.ColumnAt(dir * r);
            if (steps.Count > 0 && steps[steps.Count - 1] == c) continue;
            if (!covered.Contains(c) || cfg.InCrater(c) || cfg.OnRiver(c, out _)) continue;
            steps.Add(c);
        }
        if (steps.Count < 3) return;

        var stair = new GameObject("PilgrimStair").transform;
        stair.SetParent(root, false);
        var slab = Color.Lerp(cfg.ashColor, Color.white, 0.25f);
        var outward = new Vector3(dir.x, 0f, dir.y);
        foreach (var c in steps)
        {
            taken.Add(c);
            MakeMeshProp(stair, $"Slab_{c.x}_{c.y}", RailMesh(), ColumnSurface(colTop, c, cfg, cs) + Vector3.up * (cs * 0.03f),
                         Quaternion.LookRotation(outward, Vector3.up), new Vector3(cs * 0.62f, cs * 0.06f, cs * 0.7f),
                         Tint(slab, 0.9f + 0.15f * Hash01(DecorHash(c.x, c.y))));
        }

        // The gate on the rim, facing down the stair: two posts and a doubled lintel.
        var top  = ColumnSurface(colTop, steps[steps.Count - 1], cfg, cs);
        var gate = new GameObject("RimGate").transform;
        gate.SetParent(stair, false);
        gate.position = top;
        gate.rotation = Quaternion.LookRotation(outward, Vector3.up);
        for (int s = -1; s <= 1; s += 2)
            MakeMeshProp(gate, $"Post{s}", DrumMesh(), new Vector3(s * cs * 0.36f, 0f, 0f), Quaternion.identity,
                         new Vector3(cs * 0.1f, cs * 0.95f, cs * 0.1f), cfg.gateColor, true);
        MakeMeshProp(gate, "Lintel", RailMesh(), new Vector3(0f, cs * 0.98f, 0f), Quaternion.identity,
                     new Vector3(cs * 1.05f, cs * 0.08f, cs * 0.14f), cfg.gateColor, true);
        MakeMeshProp(gate, "Tie", RailMesh(), new Vector3(0f, cs * 0.8f, 0f), Quaternion.identity,
                     new Vector3(cs * 0.82f, cs * 0.06f, cs * 0.1f), cfg.gateColor, true);
        MakeMeshProp(gate, "Cap", RailMesh(), new Vector3(0f, cs * 1.04f, 0f), Quaternion.identity,
                     new Vector3(cs * 1.15f, cs * 0.04f, cs * 0.18f), new Color(0.14f, 0.12f, 0.12f), true);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Desert
    // ═════════════════════════════════════════════════════════════════════════

    void BuildDesert(DesertConfig cfg, HashSet<Vector2Int> covered,
                     Dictionary<Vector2Int, Vector3Int> colTop, Vector2Int ext, float cs)
    {
        var root = new GameObject("DesertProps").transform;
        root.SetParent(_buildingRoot.transform, false);

        var cols  = SortedCols(covered);
        var taken = new HashSet<Vector2Int>();
        Reserve(NpcColumn(cfg, ext), 1, taken);
        Reserve(GameColumn(cfg, ext), 1, taken);
        foreach (var c in cols)
            if (cfg.PyramidRing(c) <= cfg.pyramidSteps || cfg.OasisR(c) < 1f || cfg.OnTrail(c)) taken.Add(c);

        var O = cfg.ColumnAt(cfg.oasisAt);
        var P = cfg.ColumnAt(cfg.pyramidAt);
        int floorY = cfg.origin.y + DesertConfig.Floor;
        var Ow = BlockTop(new Vector3Int(O.x, floorY, O.y));
        var Pw = BlockTop(new Vector3Int(P.x, floorY, P.y));
        float span = Vector2.Distance(O, P);

        // ── Pyramid ──────────────────────────────────────────────────────────
        // The steps are the ground itself (HeightAt); only the gilded capstone and
        // the obelisks flanking its forecourt are props.
        if (cfg.pyramid && covered.Contains(P))
        {
            var cap = MakeMeshProp(root, "Capstone", PyramidMesh(), ColumnSurface(colTop, P, cfg, cs), Quaternion.identity,
                                   new Vector3(cs, cs * 0.8f, cs), cfg.capColor);
            PulseOn(root, 0.7f).Add(cap.GetComponent<Renderer>(), cfg.capColor, Color.Lerp(cfg.capColor, Color.white, 0.45f));

            if (cfg.oasis && span > cfg.pyramidSteps + 2f)
            {
                var dir  = ((Vector2)(O - P)).normalized;
                var perp = new Vector2(-dir.y, dir.x);
                var fore = (Vector2)P + dir * (cfg.pyramidSteps + 1.5f);
                for (int s = -1; s <= 1; s += 2)
                {
                    var want = fore + perp * (1.6f * s);
                    var col  = NearestCovered(covered, new Vector2Int(Mathf.RoundToInt(want.x), Mathf.RoundToInt(want.y)), taken);
                    taken.Add(col);
                    var p = ColumnSurface(colTop, col, cfg, cs);
                    MakeMeshProp(root, $"Obelisk{s}", ObeliskMesh(), p, Quaternion.identity,
                                 new Vector3(cs * 0.3f, cs * 1.7f, cs * 0.3f), cfg.stoneColor);
                    MakeMeshProp(root, $"ObeliskTip{s}", PyramidMesh(), p + Vector3.up * (cs * 1.7f), Quaternion.identity,
                                 new Vector3(cs * 0.19f, cs * 0.2f, cs * 0.19f), cfg.capColor);
                }
            }
        }

        // ── Caravan ──────────────────────────────────────────────────────────
        // Along the level trail (HeightAt flattens it), so the camels never wade
        // through a dune. Each follows the one ahead a beat behind, and they turn
        // round one after another at each end.
        if (cfg.oasis && cfg.pyramid && cfg.camels > 0 && span > 4f)
        {
            float rO = cfg.oasisRadius * (ext.x + ext.y) * 0.25f;
            float tA = Mathf.Clamp01((rO * 1.8f + 0.4f) / span);
            float tB = Mathf.Clamp01(1f - (cfg.pyramidSteps + 2.2f) / span);
            if (tB - tA > 0.15f)
            {
                var A = Vector3.Lerp(Ow, Pw, tA);
                var B = Vector3.Lerp(Ow, Pw, tB);
                var caravan = new GameObject("Caravan").transform;
                caravan.SetParent(root, false);
                for (int i = 0; i < cfg.camels; i++)
                {
                    var camel = BuildCamel(caravan, cfg, cs, i);
                    camel.position = A;
                    camel.rotation = Quaternion.LookRotation(B - A, Vector3.up);
                    camel.gameObject.AddComponent<DecorShuttle>().Init(caravan.InverseTransformPoint(A), caravan.InverseTransformPoint(B),
                                                                       cs * 0.35f, 2.5f, -i * 1.7f, faceTravel: true);
                }
            }
        }

        // ── Oasis ────────────────────────────────────────────────────────────
        if (cfg.oasis)
        {
            var bank = cols.FindAll(c => { float o = cfg.OasisR(c); return o >= 1f && o < 1.7f && !taken.Contains(c); });
            int pi = 0;
            foreach (var c in Scatter(bank, cfg.palms, 6121, 1.5f, taken))
                BuildPalm(root, ColumnSurface(colTop, c, cfg, cs), cs, pi++, cfg.palmLeaf, cfg.palmTrunk);

            var shore = cols.FindAll(c => { float o = cfg.OasisR(c); return o >= 0.6f && o < 1f; });
            int ri = 0;
            foreach (var c in Scatter(shore, 5, 6133, 1.2f, null))
                BuildReeds(root, ColumnSurface(colTop, c, cfg, cs), cs, ri++);

            var pond = cols.FindAll(c => cfg.OasisR(c) < 0.65f);
            int li = 0;
            foreach (var c in Scatter(pond, 4, 6143, 1f, null))
            {
                var pad = new GameObject($"LilyPad{li}").transform;
                pad.SetParent(root, false);
                pad.position = ColumnSurface(colTop, c, cfg, cs) + new Vector3((Hash01(DecorHash(c.x, 5)) - 0.5f) * cs * 0.4f, cs * 0.01f,
                                                                               (Hash01(DecorHash(c.y, 7)) - 0.5f) * cs * 0.4f);
                MakeMeshProp(pad, "Leaf", DrumMesh(), Vector3.zero, Quaternion.Euler(0f, li * 40f, 0f),
                             new Vector3(cs * 0.34f, cs * 0.02f, cs * 0.34f), new Color(0.3f, 0.58f, 0.3f), true);
                if (li % 2 == 0)
                    MakeMeshProp(pad, "Bloom", PuffMesh(), new Vector3(cs * 0.05f, cs * 0.05f, 0f), Quaternion.identity,
                                 Vector3.one * (cs * 0.1f), new Color(1f, 0.72f, 0.82f), true);
                pad.gameObject.AddComponent<DecorBob>().Init(cs * 0.012f, 2f, 1.1f, li * 1.3f);
                li++;
            }

            var tentCol = ColAtLocal(cfg, covered, cfg.oasisAt.x - 0.05f, cfg.oasisAt.y + cfg.oasisRadius * 1.9f, taken);
            Reserve(tentCol, 1, taken);
            var tp = ColumnSurface(colTop, tentCol, cfg, cs);
            BuildTent(root, tp, Ow - tp, cs);
        }

        // ── Cacti, ruins, scrub ──────────────────────────────────────────────
        var sand = cols.FindAll(c => !taken.Contains(c) && !cfg.OnMesa(c) && cfg.OasisR(c) >= 1.7f);
        int ci = 0;
        foreach (var c in Scatter(sand, cfg.cacti, 6151, 1.6f, taken))
            BuildCactus(root, cfg, ColumnSurface(colTop, c, cfg, cs), cs, ci++);

        var flank = cols.FindAll(c => !taken.Contains(c) && !cfg.OnMesa(c) && cfg.OasisR(c) >= 1.7f
                                      && cfg.TrailDistance(c) > 1.1f && cfg.TrailDistance(c) < 3.2f);
        int ui = 0;
        foreach (var c in Scatter(flank, cfg.ruins, 6163, 2.4f, taken))
        {
            var p = ColumnSurface(colTop, c, cfg, cs);
            BuildRuin(root, cfg, p, Pw - p, cs, ui++);
        }

        var mat = GroveMaterial();
        if (mat != null && cfg.scrub > 0)
        {
            var dry = TreeMesh.Recipe.Shrub();
            dry.bark  = new Color(0.50f, 0.40f, 0.30f);
            dry.leafA = new Color(0.55f, 0.52f, 0.30f);
            dry.leafB = new Color(0.68f, 0.62f, 0.36f);
            dry.leafSize = 0.14f;
            var rng = new System.Random(cfg.outlineSeed * 911 + cfg.origin.z);
            var meshes = new[] { TreeMesh.Build(dry, rng.Next()), TreeMesh.Build(dry, rng.Next()) };
            var pool = cols.FindAll(c => !taken.Contains(c) && cfg.OasisR(c) >= 1f);
            int si = 0;
            foreach (var c in Scatter(pool, cfg.scrub, 6173, 1.4f, taken))
            {
                PlantGrove(root, meshes[si % 2], mat, ColumnSurface(colTop, c, cfg, cs),
                           0.45f + 0.2f * Hash01(DecorHash(c.x, c.y)), Hash01(DecorHash(c.y, c.x)) * 360f, false, $"Scrub{si}");
                si++;
            }
        }
    }

    Transform BuildCamel(Transform parent, DesertConfig cfg, float cs, int i)
    {
        var camel = new GameObject($"Camel{i}").transform;
        camel.SetParent(parent, false);
        var body = new GameObject("Body").transform;
        body.SetParent(camel, false);

        var c = Tint(cfg.camelColor, 0.9f + 0.2f * Hash01(DecorHash(i, 3)));
        float s = cs * 0.55f;
        MakeMeshProp(body, "Torso", RailMesh(), new Vector3(0f, s * 0.62f, 0f), Quaternion.identity,
                     new Vector3(s * 0.34f, s * 0.26f, s * 0.72f), c, true);
        MakeMeshProp(body, "Hump", PuffMesh(), new Vector3(0f, s * 0.8f, -s * 0.04f), Quaternion.identity,
                     new Vector3(s * 0.3f, s * 0.3f, s * 0.38f), c, true);
        MakeMeshProp(body, "Blanket", RailMesh(), new Vector3(0f, s * 0.77f, -s * 0.04f), Quaternion.identity,
                     new Vector3(s * 0.38f, s * 0.05f, s * 0.3f),
                     i % 2 == 0 ? new Color(0.72f, 0.20f, 0.18f) : new Color(0.20f, 0.36f, 0.62f), true);
        MakeMeshProp(body, "Neck", RailMesh(), new Vector3(0f, s * 0.84f, s * 0.42f), Quaternion.Euler(35f, 0f, 0f),
                     new Vector3(s * 0.12f, s * 0.42f, s * 0.12f), c, true);
        MakeMeshProp(body, "Head", RailMesh(), new Vector3(0f, s * 1.04f, s * 0.58f), Quaternion.identity,
                     new Vector3(s * 0.13f, s * 0.12f, s * 0.26f), c, true);
        if (i == 0)   // the lead camel carries the packs
            for (int k = -1; k <= 1; k += 2)
                MakeMeshProp(body, $"Pack{k}", RailMesh(), new Vector3(k * s * 0.22f, s * 0.62f, -s * 0.12f), Quaternion.identity,
                             new Vector3(s * 0.1f, s * 0.2f, s * 0.24f), new Color(0.56f, 0.44f, 0.3f), true);

        // Legs from the hip, swinging in diagonal pairs.
        for (int k = 0; k < 4; k++)
        {
            float lx = (k & 1) == 0 ? -0.11f : 0.11f, lz = (k & 2) == 0 ? -0.26f : 0.26f;
            var hip = new GameObject($"Hip{k}").transform;
            hip.SetParent(camel, false);
            hip.localPosition = new Vector3(lx * s, s * 0.52f, lz * s);
            MakeMeshProp(hip, "Leg", RailMesh(), new Vector3(0f, -s * 0.26f, 0f), Quaternion.identity,
                         new Vector3(s * 0.07f, s * 0.52f, s * 0.07f), Tint(c, 0.9f), true);
            hip.gameObject.AddComponent<DecorSwing>().Init(Vector3.right, 16f, 5f, (k == 0 || k == 3) ? 0f : Mathf.PI);
        }
        body.gameObject.AddComponent<DecorBob>().Init(s * 0.03f, 1.5f, 10f, i);
        return camel;
    }

    // A palm: a ringed trunk curving up in segments, a crown of drooping fronds
    // that sways, and a few coconuts. Used by the desert oasis and the islands.
    void BuildPalm(Transform parent, Vector3 at, float cs, int i, Color leaf, Color trunk)
    {
        var palm = new GameObject($"Palm{i}").transform;
        palm.SetParent(parent, false);
        palm.position = at;
        palm.rotation = Quaternion.Euler(0f, Hash01(DecorHash(i, 13)) * 360f, 0f);

        float lean = 10f + 16f * Hash01(DecorHash(i, 11));
        const int segs = 5;
        float segH = cs * (0.28f + 0.06f * Hash01(DecorHash(i, 17)));
        var p = Vector3.zero;
        var rot = Quaternion.identity;
        for (int k = 0; k < segs; k++)
        {
            rot = Quaternion.Euler(lean * (k + 1) / segs, 0f, 0f);
            float w = cs * (0.15f - 0.012f * k);
            MakeMeshProp(palm, $"Trunk{k}", DrumMesh(), p, rot, new Vector3(w, segH * 1.06f, w),
                         Tint(trunk, (k & 1) == 0 ? 1f : 0.86f), true);
            p += rot * Vector3.up * segH;
        }

        var crown = new GameObject("Crown").transform;
        crown.SetParent(palm, false);
        crown.localPosition = p;
        crown.localRotation = rot;
        for (int j = 0; j < 7; j++)
        {
            var f = new GameObject($"Frond{j}").transform;
            f.SetParent(crown, false);
            f.localRotation = Quaternion.Euler(0f, j * 360f / 7f + i * 13f, 0f)
                            * Quaternion.Euler(18f + 14f * Hash01(DecorHash(i, j)), 0f, 0f);
            MakeMeshProp(f, "Inner", RailMesh(), new Vector3(0f, 0f, cs * 0.24f), Quaternion.identity,
                         new Vector3(cs * 0.17f, cs * 0.02f, cs * 0.5f), leaf, true);
            MakeMeshProp(f, "Outer", RailMesh(), new Vector3(0f, -cs * 0.09f, cs * 0.62f), Quaternion.Euler(28f, 0f, 0f),
                         new Vector3(cs * 0.13f, cs * 0.02f, cs * 0.34f), Tint(leaf, 1.12f), true);
        }
        for (int k = 0; k < 3; k++)
        {
            float a = k * 2.1f + i;
            MakeMeshProp(crown, $"Coconut{k}", PuffMesh(), new Vector3(Mathf.Cos(a) * cs * 0.07f, -cs * 0.06f, Mathf.Sin(a) * cs * 0.07f),
                         Quaternion.identity, Vector3.one * (cs * 0.08f), new Color(0.36f, 0.26f, 0.16f), true);
        }
        crown.gameObject.AddComponent<DecorSwing>().Init(Vector3.right, 3.5f, 0.9f + 0.2f * Hash01(DecorHash(i, 19)), i);
    }

    void BuildCactus(Transform parent, DesertConfig cfg, Vector3 at, float cs, int i)
    {
        var c = new GameObject($"Cactus{i}").transform;
        c.SetParent(parent, false);
        c.position = at;
        c.rotation = Quaternion.Euler(0f, Hash01(DecorHash(i, 5)) * 360f, 0f);

        var col = Tint(cfg.cactusColor, 0.88f + 0.24f * Hash01(DecorHash(i, 7)));
        float h = cs * (0.55f + 0.6f * Hash01(DecorHash(i, 9)));
        float r = cs * 0.16f;
        MakeMeshProp(c, "Trunk", DrumMesh(), Vector3.zero, Quaternion.identity, new Vector3(r, h, r), col, true);
        MakeMeshProp(c, "Top", PuffMesh(), Vector3.up * h, Quaternion.identity, new Vector3(r, r * 0.8f, r), col, true);

        int arms = Hash01(DecorHash(i, 15)) < 0.3f ? 0 : (Hash01(DecorHash(i, 17)) < 0.5f ? 1 : 2);
        for (int k = 0; k < arms; k++)
        {
            float s   = k == 0 ? 1f : -1f;
            float y   = h * (0.35f + 0.2f * Hash01(DecorHash(i, 19 + k)));
            float out_ = cs * 0.2f;
            float up  = h * (0.3f + 0.15f * Hash01(DecorHash(i, 23 + k)));
            float ax  = s * (r * 0.3f + out_);
            MakeMeshProp(c, $"ArmOut{k}", DrumMesh(), new Vector3(s * r * 0.3f, y, 0f), Quaternion.Euler(0f, 0f, -s * 90f),
                         new Vector3(r * 0.7f, out_, r * 0.7f), col, true);
            MakeMeshProp(c, $"ArmUp{k}", DrumMesh(), new Vector3(ax, y - r * 0.35f, 0f), Quaternion.identity,
                         new Vector3(r * 0.7f, up, r * 0.7f), col, true);
            MakeMeshProp(c, $"ArmTip{k}", PuffMesh(), new Vector3(ax, y - r * 0.35f + up, 0f), Quaternion.identity,
                         Vector3.one * (r * 0.7f), col, true);
        }
        if (Hash01(DecorHash(i, 29)) < 0.35f)
            MakeMeshProp(c, "Flower", PuffMesh(), Vector3.up * (h + r * 0.35f), Quaternion.identity,
                         Vector3.one * (r * 0.45f), new Color(1f, 0.45f, 0.6f), true);
    }

    // Two columns, one standing with its capital and one snapped short, a lintel
    // fallen in front and a drum rolled aside.
    void BuildRuin(Transform parent, DesertConfig cfg, Vector3 at, Vector3 facing, float cs, int i)
    {
        var r = new GameObject($"Ruin{i}").transform;
        r.SetParent(parent, false);
        r.position = at;
        facing.y = 0f;
        r.rotation = Quaternion.LookRotation(facing.sqrMagnitude > 1e-4f ? facing.normalized : Vector3.forward, Vector3.up)
                   * Quaternion.Euler(0f, (Hash01(DecorHash(i, 31)) - 0.5f) * 50f, 0f);

        var stone = Tint(cfg.stoneColor, 0.9f + 0.1f * Hash01(DecorHash(i, 37)));
        float h0 = cs * 1.2f, h1 = cs * (0.35f + 0.4f * Hash01(DecorHash(i, 41)));
        for (int k = 0; k < 2; k++)
        {
            float x  = (k == 0 ? -1f : 1f) * cs * 0.34f;
            float hh = k == 0 ? h0 : h1;
            MakeMeshProp(r, $"Base{k}", RailMesh(), new Vector3(x, cs * 0.05f, 0f), Quaternion.identity,
                         new Vector3(cs * 0.3f, cs * 0.1f, cs * 0.3f), stone, true);
            MakeMeshProp(r, $"Shaft{k}", DrumMesh(), new Vector3(x, cs * 0.1f, 0f), Quaternion.identity,
                         new Vector3(cs * 0.2f, hh, cs * 0.2f), stone, true);
            if (k == 0)
                MakeMeshProp(r, "Capital", RailMesh(), new Vector3(x, cs * 0.15f + hh, 0f), Quaternion.identity,
                             new Vector3(cs * 0.3f, cs * 0.1f, cs * 0.3f), stone, true);
        }
        MakeMeshProp(r, "Lintel", RailMesh(), new Vector3(cs * 0.1f, cs * 0.07f, cs * 0.45f), Quaternion.Euler(0f, 15f, 4f),
                     new Vector3(cs * 0.9f, cs * 0.14f, cs * 0.22f), Tint(stone, 0.92f), true);
        MakeMeshProp(r, "Drum", DrumMesh(), new Vector3(cs * 0.55f, cs * 0.1f, -cs * 0.3f), Quaternion.Euler(0f, 30f, 90f),
                     new Vector3(cs * 0.2f, cs * 0.34f, cs * 0.2f), stone, true);
        MakeMeshProp(r, "Drift", PuffMesh(), new Vector3(-cs * 0.2f, 0f, -cs * 0.05f), Quaternion.identity,
                     new Vector3(cs * 0.7f, cs * 0.16f, cs * 0.5f), Tint(cfg.sandColor, 0.97f), true);
    }

    void BuildReeds(Transform parent, Vector3 at, float cs, int i)
    {
        var r = new GameObject($"Reeds{i}").transform;
        r.SetParent(parent, false);
        r.position = at;
        for (int k = 0; k < 5; k++)
        {
            float a = k * 1.3f + i;
            float h = cs * (0.35f + 0.3f * Hash01(DecorHash(i, k)));
            MakeMeshProp(r, $"Reed{k}", RailMesh(), new Vector3(Mathf.Cos(a) * cs * 0.14f, h * 0.5f, Mathf.Sin(a) * cs * 0.14f),
                         Quaternion.Euler((Hash01(DecorHash(k, i)) - 0.5f) * 16f, 0f, (Hash01(DecorHash(i, k + 9)) - 0.5f) * 16f),
                         new Vector3(cs * 0.025f, h, cs * 0.025f), new Color(0.42f, 0.56f, 0.28f), true);
            if (k % 2 == 0)
                MakeMeshProp(r, $"Head{k}", RailMesh(), new Vector3(Mathf.Cos(a) * cs * 0.14f, h, Mathf.Sin(a) * cs * 0.14f),
                             Quaternion.identity, new Vector3(cs * 0.05f, cs * 0.12f, cs * 0.05f), new Color(0.46f, 0.32f, 0.2f), true);
        }
        r.gameObject.AddComponent<DecorSwing>().Init(Vector3.forward, 4f, 1.4f, i);
    }

    // A striped tent by the water, a rug before it and a small fire smoking.
    void BuildTent(Transform parent, Vector3 at, Vector3 facing, float cs)
    {
        var t = new GameObject("Tent").transform;
        t.SetParent(parent, false);
        t.position = at;
        facing.y = 0f;
        t.rotation = Quaternion.LookRotation(facing.sqrMagnitude > 1e-4f ? facing.normalized : Vector3.forward, Vector3.up);

        var canvas = new Color(0.92f, 0.86f, 0.72f);
        var stripe = new Color(0.66f, 0.22f, 0.18f);
        // Ridge runs front to back (the gable is built with its ridge along X).
        MakeMeshProp(t, "Canvas", GableMesh(), Vector3.zero, Quaternion.Euler(0f, 90f, 0f),
                     new Vector3(cs * 0.9f, cs * 0.6f, cs * 0.8f), canvas, true);
        MakeMeshProp(t, "Stripe", GableMesh(), Vector3.up * 0.001f, Quaternion.Euler(0f, 90f, 0f),
                     new Vector3(cs * 0.3f, cs * 0.605f, cs * 0.81f), stripe, true);
        for (int s = -1; s <= 1; s += 2)
            MakeMeshProp(t, $"Pole{s}", RailMesh(), new Vector3(0f, cs * 0.34f, s * cs * 0.47f), Quaternion.identity,
                         new Vector3(cs * 0.03f, cs * 0.68f, cs * 0.03f), new Color(0.4f, 0.3f, 0.2f), true);
        MakeMeshProp(t, "Rug", RailMesh(), new Vector3(0f, cs * 0.01f, cs * 0.75f), Quaternion.identity,
                     new Vector3(cs * 0.5f, cs * 0.02f, cs * 0.4f), stripe, true);

        var fire = new Vector3(cs * 0.5f, 0f, cs * 0.9f);
        for (int k = 0; k < 5; k++)
        {
            float a = k / 5f * Mathf.PI * 2f;
            MakeMeshProp(t, $"FireStone{k}", PuffMesh(), fire + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (cs * 0.13f), Quaternion.identity,
                         Vector3.one * (cs * 0.08f), new Color(0.45f, 0.42f, 0.4f), true);
        }
        var flame = MakeMeshProp(t, "Flame", PyramidMesh(), fire, Quaternion.Euler(0f, 45f, 0f),
                                 new Vector3(cs * 0.12f, cs * 0.2f, cs * 0.12f), new Color(2f, 0.9f, 0.3f), true);
        PulseOn(t, 7f).Add(flame.GetComponent<Renderer>(), new Color(1.6f, 0.6f, 0.2f), new Color(2.4f, 1.2f, 0.35f));
        SpawnSmoke(t, t.TransformPoint(fire) + Vector3.up * (cs * 0.2f), 4, new Color(0.75f, 0.74f, 0.72f), cs, 5,
                   size: 0.16f, rise: 1.4f, life: 3f, spread: 0.05f, drift: new Vector3(0.3f, 0f, 0.1f));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Ocean
    // ═════════════════════════════════════════════════════════════════════════

    void BuildOcean(OceanConfig cfg, HashSet<Vector2Int> covered,
                    Dictionary<Vector2Int, Vector3Int> colTop, Vector2Int ext, float cs)
    {
        var root = new GameObject("OceanProps").transform;
        root.SetParent(_buildingRoot.transform, false);

        var cols  = SortedCols(covered);
        var rim   = RimCols(covered);
        var taken = new HashSet<Vector2Int>();
        Reserve(NpcColumn(cfg, ext), 1, taken);
        Reserve(GameColumn(cfg, ext), 1, taken);

        var water = new List<Vector2Int>();
        var land  = new List<Vector2Int>();
        foreach (var c in cols)
            (cfg.IslandR(c, out _) >= 1f ? water : land).Add(c);

        // ── Lighthouse, keeper's cottage ─────────────────────────────────────
        Vector3 beacon = Vector3.zero;
        bool hasBeacon = false;
        if (cfg.lighthouse && cfg.islands != null && cfg.islands.Length > 0)
        {
            var head = NearestCovered(covered, cfg.ColumnAt(new Vector2(cfg.islands[0].x, cfg.islands[0].y)));
            Reserve(head, 1, taken);
            var hp = ColumnSurface(colTop, head, cfg, cs);
            beacon = BuildLighthouse(root, cfg, hp, cs);
            hasBeacon = true;

            var grass = land.FindAll(c => !taken.Contains(c) && cfg.HeightAt(c) >= 2 && cfg.IslandR(c, out int w) < 1f && w == 0);
            if (grass.Count > 0)
            {
                var cot = NearestCovered(new HashSet<Vector2Int>(grass), head + new Vector2Int(2, -1));
                Reserve(cot, 1, taken);
                var cp = ColumnSurface(colTop, cot, cfg, cs);
                BuildCottage(root, cp, hp - cp, cfg.stripeA, cfg.stripeB, cs, "KeepersCottage");
            }
        }

        // ── Dock, with a boat tied up ────────────────────────────────────────
        int sailing = cfg.boats;
        if (cfg.boats > 0 && FindDock(cfg, covered, cols, out var dockStart, out var dockDir))
        {
            BuildDock(root, cfg, colTop, dockStart, dockDir, cs, taken);
            var side = dockStart + dockDir * 3 + new Vector2Int(dockDir.y, -dockDir.x);
            var moor = new GameObject("MooredBoat").transform;
            moor.SetParent(root, false);
            moor.position = ColumnSurface(colTop, side, cfg, cs);
            moor.rotation = Quaternion.LookRotation(new Vector3(dockDir.x, 0f, dockDir.y), Vector3.up);
            BuildBoat(moor, cfg, cs, 0);
            moor.gameObject.AddComponent<DecorBob>().Init(cs * 0.03f, 3f, 1.3f, 0.4f);
            sailing--;
        }

        // ── Boats sailing circles out in the bay ─────────────────────────────
        const float loop = 1.8f;
        var open = water.FindAll(c => !rim.Contains(c) && cfg.IslandR(c, out _) > 1.8f && WaterRing(cfg, covered, c, loop));
        int bi = 1;
        foreach (var c in Scatter(open, Mathf.Max(0, sailing), 7103, 5f, null))
        {
            var pivot = new GameObject($"BoatRoute{bi}").transform;
            pivot.SetParent(root, false);
            pivot.position = ColumnSurface(colTop, c, cfg, cs);
            bool cw = (bi & 1) == 1;
            var boat = new GameObject($"Boat{bi}").transform;
            boat.SetParent(pivot, false);
            boat.localPosition = new Vector3(loop * cs, 0f, 0f);
            // A positive spin carries +X toward -Z, so a clockwise boat faces -Z.
            boat.localRotation = Quaternion.Euler(0f, cw ? 180f : 0f, 0f);
            var hull = new GameObject("Hull").transform;
            hull.SetParent(boat, false);
            BuildBoat(hull, cfg, cs, bi);
            hull.gameObject.AddComponent<DecorBob>().Init(cs * 0.04f, 4f, 1.2f, bi * 1.1f);
            pivot.gameObject.AddComponent<DecorGearSpin>().Init((cw ? 1f : -1f) * (9f + 3f * Hash01(DecorHash(bi, 3))), Vector3.up);
            bi++;
        }

        // ── Buoys, foam, rocks ───────────────────────────────────────────────
        var channel = water.FindAll(c => !taken.Contains(c) && cfg.IslandR(c, out _) is float k && k > 1.3f && k < 3f);
        int ui = 0;
        foreach (var c in Scatter(channel, cfg.buoys, 7109, 2.5f, taken))
            BuildBuoy(root, cfg, ColumnSurface(colTop, c, cfg, cs), cs, ui++);

        var deep = water.FindAll(c => !taken.Contains(c) && !rim.Contains(c) && cfg.IslandR(c, out _) > 1.6f);
        int ri = 0;
        foreach (var c in Scatter(deep, cfg.seaRocks, 7121, 3f, taken))
        {
            var p = ColumnSurface(colTop, c, cfg, cs);
            int h = DecorHash(c.x, c.y);
            float s = cs * (0.5f + 0.35f * Hash01(h));
            MakeMeshProp(root, $"SeaRock{ri}", PuffMesh(), p + Vector3.up * (s * 0.12f),
                         Quaternion.Euler(Hash01(h + 1) * 30f, Hash01(h + 2) * 360f, 0f), new Vector3(s, s * 0.8f, s * 0.9f),
                         Tint(cfg.cliffColor, 0.8f + 0.2f * Hash01(h + 3)));
            var ring = MakeMeshProp(root, $"SeaRockFoam{ri}", RingMesh(), p + Vector3.up * (cs * 0.02f), Quaternion.identity,
                                    new Vector3(s * 1.5f, s * 0.6f, s * 1.5f), cfg.foamColor);
            ring.gameObject.AddComponent<DecorBob>().Init(cs * 0.015f, 0f, 1.6f, ri);
            ri++;
        }

        // Foam flecks on the shallows, where the water breaks round the islands.
        var shallows = water.FindAll(c => !taken.Contains(c) && cfg.IslandR(c, out _) < 1.6f);
        int fi = 0;
        foreach (var c in Scatter(shallows, GraphicsQuality.AmbientMotion ? GraphicsQuality.Scaled(cfg.foam) : 0, 7129, 1.2f, null))
        {
            var p = ColumnSurface(colTop, c, cfg, cs);
            int h = DecorHash(c.y, c.x);
            var fleck = MakeMeshProp(root, $"Foam{fi}", RailMesh(),
                                     p + new Vector3((Hash01(h) - 0.5f) * cs * 0.5f, cs * 0.015f, (Hash01(h + 1) - 0.5f) * cs * 0.5f),
                                     Quaternion.Euler(0f, Hash01(h + 2) * 180f, 0f),
                                     new Vector3(cs * (0.3f + 0.3f * Hash01(h + 3)), cs * 0.02f, cs * 0.1f), cfg.foamColor);
            fleck.gameObject.AddComponent<DecorBob>().Init(cs * 0.02f, 0f, 1.8f, fi * 0.7f);
            fi++;
        }

        // ── Island palms ─────────────────────────────────────────────────────
        var shore = land.FindAll(c => !taken.Contains(c));
        int pi = 0;
        foreach (var c in Scatter(shore, cfg.islandPalms, 7133, 1.8f, taken))
            BuildPalm(root, ColumnSurface(colTop, c, cfg, cs), cs, 100 + pi++,
                      new Color(0.30f, 0.56f, 0.24f), new Color(0.52f, 0.38f, 0.24f));

        // ── Gulls, wheeling round the light ──────────────────────────────────
        if (cfg.gulls > 0 && GraphicsQuality.AmbientMotion)
        {
            var centre = hasBeacon ? beacon : ColumnSurface(colTop, cfg.ColumnAt(Vector2.zero), cfg, cs);
            for (int g = 0; g < GraphicsQuality.Scaled(cfg.gulls, 1); g++)
            {
                var pivot = new GameObject($"GullRoute{g}").transform;
                pivot.SetParent(root, false);
                pivot.position = centre + Vector3.up * (cs * (1.2f + 0.8f * Hash01(DecorHash(g, 5))));
                pivot.rotation = Quaternion.Euler(0f, g * 97f, 0f);
                var gull = BuildGull(pivot, cs, g);
                gull.localPosition = new Vector3(cs * (1.4f + 1.2f * Hash01(DecorHash(g, 7))), 0f, 0f);
                gull.localRotation = Quaternion.Euler(0f, 180f, -18f);   // banked into the turn
                pivot.gameObject.AddComponent<DecorGearSpin>().Init(22f + 10f * Hash01(DecorHash(g, 9)), Vector3.up);
            }
        }
    }

    // Twelve points round a circle all open water inside the plot.
    static bool WaterRing(OceanConfig cfg, HashSet<Vector2Int> covered, Vector2Int centre, float r)
    {
        for (int k = 0; k < 12; k++)
        {
            float a = k / 12f * Mathf.PI * 2f;
            var c = new Vector2Int(Mathf.RoundToInt(centre.x + Mathf.Cos(a) * r), Mathf.RoundToInt(centre.y + Mathf.Sin(a) * r));
            if (!covered.Contains(c) || cfg.IslandR(c, out _) < 1.15f) return false;
        }
        return true;
    }

    // A beach cell on the lighthouse island with three cells of open water
    // straight out from it, plus room beside the end for a boat.
    static bool FindDock(OceanConfig cfg, HashSet<Vector2Int> covered, List<Vector2Int> cols,
                         out Vector2Int start, out Vector2Int dir)
    {
        var dirs = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        foreach (var c in cols)
        {
            if (cfg.IslandR(c, out int island) >= 1f || island != 0 || cfg.HeightAt(c) != 1) continue;
            foreach (var d in dirs)
            {
                bool ok = true;
                for (int k = 1; k <= 3 && ok; k++)
                {
                    var n = c + d * k;
                    ok = covered.Contains(n) && cfg.IslandR(n, out _) >= 1f;
                }
                var side = c + d * 3 + new Vector2Int(d.y, -d.x);
                if (ok && covered.Contains(side) && cfg.IslandR(side, out _) >= 1f)
                {
                    start = c;
                    dir   = d;
                    return true;
                }
            }
        }
        start = dir = Vector2Int.zero;
        return false;
    }

    void BuildDock(Transform root, OceanConfig cfg, Dictionary<Vector2Int, Vector3Int> colTop,
                   Vector2Int start, Vector2Int dir, float cs, HashSet<Vector2Int> taken)
    {
        var dock = new GameObject("Dock").transform;
        dock.SetParent(root, false);
        var wood = new Color(0.48f, 0.34f, 0.22f);
        var fwd  = new Vector3(dir.x, 0f, dir.y);
        var rot  = Quaternion.LookRotation(fwd, Vector3.up);
        float deckY = ColumnSurface(colTop, start, cfg, cs).y + cs * 0.04f;

        for (int k = 1; k <= 3; k++)
        {
            var c = start + dir * k;
            taken.Add(c);
            var w = ColumnSurface(colTop, c, cfg, cs);
            for (int j = 0; j < 4; j++)
            {
                var along = fwd * ((j - 1.5f) * cs * 0.25f);
                MakeMeshProp(dock, $"Plank{k}_{j}", RailMesh(), new Vector3(w.x, deckY, w.z) + along, rot,
                             new Vector3(cs * 0.62f, cs * 0.05f, cs * 0.22f), Tint(wood, 0.9f + 0.2f * Hash01(DecorHash(k, j))));
            }
            for (int s = -1; s <= 1; s += 2)
            {
                var side = rot * new Vector3(s * cs * 0.3f, 0f, cs * 0.45f);
                float h = deckY - w.y + cs * 0.25f;
                MakeMeshProp(dock, $"Pile{k}_{s}", DrumMesh(), new Vector3(w.x, w.y - cs * 0.1f, w.z) + side, Quaternion.identity,
                             new Vector3(cs * 0.08f, h + cs * 0.1f, cs * 0.08f), Tint(wood, 0.75f));
            }
        }
        var endC = start + dir * 3;
        var end  = ColumnSurface(colTop, endC, cfg, cs);
        MakeMeshProp(dock, "Lamp", DrumMesh(), new Vector3(end.x, deckY, end.z) + fwd * (cs * 0.4f), Quaternion.identity,
                     new Vector3(cs * 0.05f, cs * 0.6f, cs * 0.05f), new Color(0.2f, 0.2f, 0.22f));
        var bulb = MakeMeshProp(dock, "LampLight", PuffMesh(), new Vector3(end.x, deckY + cs * 0.64f, end.z) + fwd * (cs * 0.4f),
                                Quaternion.identity, Vector3.one * (cs * 0.12f), cfg.lampColor);
        PulseOn(dock, 1.5f).Add(bulb.GetComponent<Renderer>(), Tint(cfg.lampColor, 0.7f), cfg.lampColor);
    }

    // Striped tower on a stone base, a gallery, an open iron lantern room under a
    // red cap. The light in it is light, not a model: a soft glow and a real
    // point light where the lamp is, and two beams of lit air sweeping round,
    // each with a spotlight that washes the sea and the islands as it passes.
    // Returns the lamp's position.
    Vector3 BuildLighthouse(Transform root, OceanConfig cfg, Vector3 at, float cs)
    {
        var lh = new GameObject("Lighthouse").transform;
        lh.SetParent(root, false);
        lh.position = at;
        var iron = new Color(0.2f, 0.2f, 0.22f);

        MakeMeshProp(lh, "Base", HexMesh(), Vector3.zero, Quaternion.identity,
                     new Vector3(cs * 0.95f, cs * 0.25f, cs * 0.95f), cfg.cliffColor, true);
        float y = cs * 0.25f;
        const int bands = 6;
        float bandH = cs * 0.34f;
        for (int k = 0; k < bands; k++)
        {
            float w = cs * Mathf.Lerp(0.72f, 0.5f, k / (float)(bands - 1));
            MakeMeshProp(lh, $"Band{k}", DrumMesh(), Vector3.up * y, Quaternion.identity,
                         new Vector3(w, bandH, w), (k & 1) == 0 ? cfg.stripeA : cfg.stripeB, true);
            y += bandH;
        }
        MakeMeshProp(lh, "Door", RailMesh(), new Vector3(0f, cs * 0.45f, -cs * 0.36f), Quaternion.identity,
                     new Vector3(cs * 0.18f, cs * 0.34f, cs * 0.04f), iron, true);
        for (int k = 1; k < bands; k += 2)
            MakeMeshProp(lh, $"Window{k}", RailMesh(), new Vector3(0f, cs * 0.25f + bandH * (k + 0.5f), -cs * (0.35f - k * 0.02f)),
                         Quaternion.identity, new Vector3(cs * 0.08f, cs * 0.12f, cs * 0.04f), iron, true);

        MakeMeshProp(lh, "Gallery", DrumMesh(), Vector3.up * y, Quaternion.identity,
                     new Vector3(cs * 0.74f, cs * 0.06f, cs * 0.74f), iron, true);
        MakeMeshProp(lh, "Railing", RingMesh(), Vector3.up * (y + cs * 0.16f), Quaternion.identity,
                     Vector3.one * (cs * 0.74f), iron, true);
        y += cs * 0.06f;

        const int mullions = 6;
        for (int k = 0; k < mullions; k++)
        {
            float a = k / (float)mullions * Mathf.PI * 2f;
            MakeMeshProp(lh, $"Mullion{k}", RailMesh(), new Vector3(Mathf.Cos(a) * cs * 0.19f, y + cs * 0.17f, Mathf.Sin(a) * cs * 0.19f),
                         Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), new Vector3(cs * 0.03f, cs * 0.34f, cs * 0.03f), iron, true);
        }
        MakeMeshProp(lh, "Roof", ConeMesh(), Vector3.up * (y + cs * 0.34f), Quaternion.identity,
                     new Vector3(cs * 0.52f, cs * 0.3f, cs * 0.52f), cfg.stripeB, true);
        MakeMeshProp(lh, "Finial", PuffMesh(), Vector3.up * (y + cs * 0.68f), Quaternion.identity,
                     Vector3.one * (cs * 0.08f), iron, true);

        var sweep = new GameObject("Sweep").transform;
        sweep.SetParent(lh, false);
        sweep.localPosition = Vector3.up * (y + cs * 0.17f);
        LampGlow(lh, sweep.localPosition, cfg.lampColor, cs);

        // Tipped a little toward the sea, so the spotlights catch the water.
        bool lights = GraphicsQuality.Current >= GraphicsQuality.Tier.Medium;
        for (int s = 0; s < 2; s++)
        {
            var arm = new GameObject($"Beam{s}").transform;
            arm.SetParent(sweep, false);
            arm.localRotation = Quaternion.Euler(6f, s * 180f, 0f);
            LightShaft(arm, cs * 7f, cs * 0.16f, cs * 1.5f, cfg.lampColor * 0.45f);
            if (!lights) continue;
            var spot = arm.gameObject.AddComponent<Light>();
            spot.type           = LightType.Spot;
            spot.range          = cs * 9f;
            spot.spotAngle      = 24f;
            spot.innerSpotAngle = 8f;
            spot.intensity      = 3f;
            spot.color          = LightColour(cfg.lampColor);
            spot.shadows        = LightShadows.None;
        }
        sweep.gameObject.AddComponent<DecorGearSpin>().Init(45f, Vector3.up);

        return lh.TransformPoint(sweep.localPosition);
    }

    // A beam of lit air along the parent's +Z: `length` long, `r0` wide at the
    // lamp, spreading to `r1` (GeoWorld/LightShaft: additive, soft, no outline).
    static Material _shaftMat;
    static Mesh     _shaftMesh;
    static MaterialPropertyBlock _shaftMpb;

    void LightShaft(Transform parent, float length, float r0, float r1, Color color)
    {
        if (_shaftMat == null)
        {
            var sh = Shader.Find("GeoWorld/LightShaft");
            if (sh == null) return;
            _shaftMat = new Material(sh) { name = "LightShaft (runtime)", hideFlags = HideFlags.DontSave };
        }
        var go = new GameObject("Shaft");
        go.transform.SetParent(parent, false);
        go.transform.localScale = new Vector3(r1 * 2f, r1 * 2f, length);
        go.AddComponent<MeshFilter>().sharedMesh = ShaftMesh(r0 / Mathf.Max(1e-3f, r1));
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial    = _shaftMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows    = false;
        _shaftMpb ??= new MaterialPropertyBlock();
        _shaftMpb.SetColor("_Color", color);
        mr.SetPropertyBlock(_shaftMpb);
    }

    // An open cone along +Z, radius 0.5 × taper at z = 0 widening to 0.5 at
    // z = 1. uv.y runs along it, which is what the shader fades by.
    static Mesh ShaftMesh(float taper)
    {
        if (_shaftMesh != null) return _shaftMesh;
        const int sides = 16, rings = 6;
        var v  = new List<Vector3>();
        var n  = new List<Vector3>();
        var uv = new List<Vector2>();
        var t  = new List<int>();
        for (int r = 0; r <= rings; r++)
        {
            float z = r / (float)rings;
            float rad = 0.5f * Mathf.Lerp(taper, 1f, z);
            for (int i = 0; i <= sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                v.Add(d * rad + Vector3.forward * z);
                n.Add(d);
                uv.Add(new Vector2(i / (float)sides, z));
            }
        }
        for (int r = 0; r < rings; r++)
            for (int i = 0; i < sides; i++)
            {
                int a0 = r * (sides + 1) + i, a1 = a0 + 1, b0 = a0 + sides + 1, b1 = b0 + 1;
                t.Add(a0); t.Add(b0); t.Add(a1);
                t.Add(a1); t.Add(b0); t.Add(b1);
            }
        _shaftMesh = new Mesh { name = "LightShaft", hideFlags = HideFlags.DontSave };
        _shaftMesh.SetVertices(v);
        _shaftMesh.SetNormals(n);
        _shaftMesh.SetUVs(0, uv);
        _shaftMesh.SetTriangles(t, 0);
        _shaftMesh.RecalculateBounds();
        return _shaftMesh;
    }

    // Where a lamp burns: a soft halo that always faces the camera (GeoWorld/
    // SoftDot, additive) and, on Medium and up, a real point light.
    static Material _haloMat;
    static Mesh     _haloQuad;

    void LampGlow(Transform parent, Vector3 localPos, Color color, float cs)
    {
        if (_haloMat == null)
        {
            var sh = Shader.Find("GeoWorld/SoftDot");
            if (sh != null)
            {
                _haloMat = new Material(sh) { name = "LampHalo (runtime)", hideFlags = HideFlags.DontSave };
                _haloMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _haloMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                _haloMat.SetFloat("_Softness", 1f);
            }
        }
        if (_haloMat != null)
        {
            var halo = new GameObject("Halo");
            halo.transform.SetParent(parent, false);
            halo.transform.localPosition = localPos;
            halo.transform.localScale    = Vector3.one * (cs * 1.2f);
            halo.AddComponent<MeshFilter>().sharedMesh = HaloQuad();
            var mr = halo.AddComponent<MeshRenderer>();
            mr.sharedMaterial    = _haloMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows    = false;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_Color", new Color(color.r, color.g, color.b, 0.9f));
            mr.SetPropertyBlock(mpb);
            halo.AddComponent<DecorFaceCamera>();
        }
        if (GraphicsQuality.Current >= GraphicsQuality.Tier.Medium)
        {
            var go = new GameObject("LampLight");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var l = go.AddComponent<Light>();
            l.type      = LightType.Point;
            l.range     = cs * 3f;
            l.intensity = 2f;
            l.color     = LightColour(color);
            l.shadows   = LightShadows.None;
        }
    }

    static Mesh HaloQuad()
    {
        if (_haloQuad != null) return _haloQuad;
        _haloQuad = new Mesh { name = "LampHalo", hideFlags = HideFlags.DontSave };
        _haloQuad.SetVertices(new List<Vector3> { new(-0.5f, -0.5f, 0f), new(0.5f, -0.5f, 0f), new(0.5f, 0.5f, 0f), new(-0.5f, 0.5f, 0f) });
        _haloQuad.SetUVs(0, new List<Vector2> { new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f) });
        _haloQuad.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
        _haloQuad.SetTriangles(new List<int> { 0, 2, 1, 0, 3, 2 }, 0);
        _haloQuad.RecalculateBounds();
        return _haloQuad;
    }

    // A glowing (HDR) colour as a light's colour: its hue at full strength.
    static Color LightColour(Color c)
    {
        float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        return m > 1e-4f ? new Color(c.r / m, c.g / m, c.b / m, 1f) : Color.white;
    }

    class DecorFaceCamera : MonoBehaviour
    {
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null) transform.rotation = cam.transform.rotation;
        }
    }

    void BuildCottage(Transform root, Vector3 at, Vector3 face, Color wall, Color roof, float cs, string name)
    {
        var h = new GameObject(name).transform;
        h.SetParent(root, false);
        h.position = at;
        face.y = 0f;
        h.rotation = Quaternion.LookRotation(face.sqrMagnitude > 1e-4f ? face.normalized : Vector3.forward, Vector3.up);

        MakeMeshProp(h, "Walls", RailMesh(), new Vector3(0f, cs * 0.3f, 0f), Quaternion.identity,
                     new Vector3(cs * 0.8f, cs * 0.6f, cs * 0.6f), wall, true);
        MakeMeshProp(h, "Roof", GableMesh(), new Vector3(0f, cs * 0.6f, 0f), Quaternion.identity,
                     new Vector3(cs * 0.92f, cs * 0.4f, cs * 0.72f), roof, true);
        MakeMeshProp(h, "Door", RailMesh(), new Vector3(cs * 0.12f, cs * 0.18f, cs * 0.301f), Quaternion.identity,
                     new Vector3(cs * 0.16f, cs * 0.34f, cs * 0.02f), Tint(roof, 0.6f), true);
        MakeMeshProp(h, "Window", RailMesh(), new Vector3(-cs * 0.2f, cs * 0.36f, cs * 0.301f), Quaternion.identity,
                     new Vector3(cs * 0.14f, cs * 0.12f, cs * 0.02f), new Color(1.2f, 1.0f, 0.6f), true);
        MakeMeshProp(h, "Chimney", RailMesh(), new Vector3(cs * 0.26f, cs * 0.86f, -cs * 0.1f), Quaternion.identity,
                     new Vector3(cs * 0.1f, cs * 0.3f, cs * 0.1f), Tint(wall, 0.7f), true);
        SpawnSmoke(h, h.TransformPoint(new Vector3(cs * 0.26f, cs * 1.03f, -cs * 0.1f)), 4, new Color(0.85f, 0.85f, 0.87f), cs, 11,
                   size: 0.18f, rise: 1.5f, life: 3.5f, spread: 0.05f, drift: new Vector3(0.5f, 0f, 0.2f));
    }

    // Hull with a pointed bow, a cabin, a mast and a triangular sail. Faces local +Z.
    void BuildBoat(Transform parent, OceanConfig cfg, float cs, int i)
    {
        var hull = Tint(cfg.hullColor, 0.9f + 0.2f * Hash01(DecorHash(i, 3)));
        MakeMeshProp(parent, "Hull", RailMesh(), new Vector3(0f, cs * 0.03f, -cs * 0.05f), Quaternion.identity,
                     new Vector3(cs * 0.3f, cs * 0.14f, cs * 0.7f), hull, true);
        MakeMeshProp(parent, "Bow", PyramidMesh(), new Vector3(0f, cs * 0.03f, cs * 0.3f), Quaternion.Euler(90f, 0f, 0f),
                     new Vector3(cs * 0.3f, cs * 0.26f, cs * 0.14f), hull, true);
        MakeMeshProp(parent, "Rail", RailMesh(), new Vector3(0f, cs * 0.105f, -cs * 0.05f), Quaternion.identity,
                     new Vector3(cs * 0.31f, cs * 0.02f, cs * 0.7f), Tint(hull, 1.3f), true);
        MakeMeshProp(parent, "Cabin", RailMesh(), new Vector3(0f, cs * 0.17f, -cs * 0.22f), Quaternion.identity,
                     new Vector3(cs * 0.2f, cs * 0.12f, cs * 0.2f), cfg.stripeA, true);
        MakeMeshProp(parent, "Mast", RailMesh(), new Vector3(0f, cs * 0.5f, cs * 0.06f), Quaternion.identity,
                     new Vector3(cs * 0.03f, cs * 0.8f, cs * 0.03f), Tint(hull, 0.7f), true);
        MakeMeshProp(parent, "Sail", GableMesh(), new Vector3(0f, cs * 0.14f, -cs * 0.02f), Quaternion.identity,
                     new Vector3(cs * 0.02f, cs * 0.66f, cs * 0.44f), cfg.sailColor, true);
        MakeMeshProp(parent, "Flag", RailMesh(), new Vector3(0f, cs * 0.88f, cs * 0.0f), Quaternion.identity,
                     new Vector3(cs * 0.01f, cs * 0.06f, cs * 0.12f), cfg.stripeB, true);
    }

    void BuildBuoy(Transform root, OceanConfig cfg, Vector3 at, float cs, int i)
    {
        var b = new GameObject($"Buoy{i}").transform;
        b.SetParent(root, false);
        b.position = at;
        var red = (i & 1) == 0 ? cfg.stripeB : new Color(0.22f, 0.5f, 0.3f);
        MakeMeshProp(b, "Float", DrumMesh(), Vector3.down * (cs * 0.05f), Quaternion.identity,
                     new Vector3(cs * 0.2f, cs * 0.18f, cs * 0.2f), red, true);
        MakeMeshProp(b, "Band", DrumMesh(), Vector3.up * (cs * 0.05f), Quaternion.identity,
                     new Vector3(cs * 0.21f, cs * 0.05f, cs * 0.21f), cfg.stripeA, true);
        MakeMeshProp(b, "Cone", ConeMesh(), Vector3.up * (cs * 0.13f), Quaternion.identity,
                     new Vector3(cs * 0.14f, cs * 0.2f, cs * 0.14f), red, true);
        var light = MakeMeshProp(b, "Light", PuffMesh(), Vector3.up * (cs * 0.35f), Quaternion.identity,
                                 Vector3.one * (cs * 0.06f), cfg.lampColor, true);
        PulseOn(b, 2.5f).Add(light.GetComponent<Renderer>(), Tint(cfg.lampColor, 0.3f), cfg.lampColor, i * 1.3f);
        b.gameObject.AddComponent<DecorBob>().Init(cs * 0.04f, 7f, 1.5f, i * 0.9f);
    }

    Transform BuildGull(Transform parent, float cs, int i)
    {
        var g = new GameObject($"Gull{i}").transform;
        g.SetParent(parent, false);
        var white = new Color(0.96f, 0.96f, 0.97f);
        MakeMeshProp(g, "Body", PuffMesh(), Vector3.zero, Quaternion.identity,
                     new Vector3(cs * 0.08f, cs * 0.06f, cs * 0.18f), white, true);
        MakeMeshProp(g, "Beak", PyramidMesh(), new Vector3(0f, 0f, cs * 0.09f), Quaternion.Euler(90f, 0f, 0f),
                     new Vector3(cs * 0.025f, cs * 0.05f, cs * 0.025f), new Color(0.95f, 0.7f, 0.2f), true);
        for (int s = -1; s <= 1; s += 2)
        {
            var hinge = new GameObject($"Wing{s}").transform;
            hinge.SetParent(g, false);
            hinge.localPosition = new Vector3(s * cs * 0.03f, cs * 0.01f, 0f);
            MakeMeshProp(hinge, "Feather", RailMesh(), new Vector3(s * cs * 0.15f, 0f, 0f), Quaternion.identity,
                         new Vector3(cs * 0.3f, cs * 0.012f, cs * 0.09f), white, true);
            MakeMeshProp(hinge, "Tip", RailMesh(), new Vector3(s * cs * 0.29f, 0f, -cs * 0.01f), Quaternion.identity,
                         new Vector3(cs * 0.06f, cs * 0.013f, cs * 0.07f), new Color(0.2f, 0.2f, 0.22f), true);
            hinge.gameObject.AddComponent<DecorSwing>().Init(Vector3.forward, 28f * s, 7f + Hash01(DecorHash(i, 3)), i * 0.8f);
        }
        return g;
    }
}
