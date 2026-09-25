using System.Collections.Generic;
using UnityEngine;

// The Harmony grove — a wood that half-wraps the back of the farm.
//
// It rides the shared machinery in LevelMapController.Decor.cs (ground, walkability,
// residents, reveal) and adds only its own floor, outline and planting.
//
// ── A SHAPE AROUND THE FARM, NOT A PLOT BESIDE IT ────────────────────────────
//
// Every earlier version placed the wood as a separate patch and then tried to get
// it to the right spot next to the farm — anchored to the windmill, offset by the
// camera, pushed toward the fields. Each of those left a gap somewhere, because
// "next to" was being computed, and anything computed can be off by one.
//
// So the wood is now DEFINED by the farm: a crescent whose inner edge is the farm's
// own boundary. Its ground is measured as distance out from the farm's rectangle,
// starting at the farm's outermost row. There is no position to get wrong and no
// gap to leave — the wood cannot not touch the farm, by construction.
//
// The crescent opens toward the rest of the map (the other plots, the level nodes)
// and wraps the farm's far side, so from the map the farm sits in the curve of a
// wood with its fields facing everything else.
//
// ── COMPOSED, NOT SCATTERED ──────────────────────────────────────────────────
//
// Harmony is the synergy of "all of one colour, connected". Its plot says the same:
// one continuous ground, one continuous path, trees placed in relation to the path.
//   1. The crescent: smooth irregular outer edge, hard threshold, no speckle.
//   2. A path running round through the middle of the crescent, UNBROKEN, with a
//      spur that leads straight onto the farm.
//   3. An avenue lining both sides of it, evenly spaced and staggered.
//   4. The elder where the spur meets the path, in its own clearing.
//   5. Small groves and undergrowth, on the ground only.
[System.Serializable]
public class HarmonyGroveConfig : MapDecorConfig
{
    [Header("Crescent")]
    [Tooltip("How deep the wood is at its thickest — straight behind the farm — in cells.")]
    [Range(3f, 16f)] public float depth = 9f;

    [Tooltip("How far round the farm it wraps, each way from straight behind, in degrees. 90 is a half-ring; more starts to close in on the farm's front.")]
    [Range(30f, 170f)] public float arcHalf = 100f;

    [Tooltip("How far the outer edge wanders in and out. Three low-frequency waves: bays and headlands, not a wobble on every cell.")]
    [Range(0f, 0.4f)] public float irregularity = 0.24f;

    [Header("Path")]
    [Tooltip("Pale track cut through the green.")]
    public Color pathColor = new(0.66f, 0.55f, 0.41f);

    [Tooltip("Track width in cells.")]
    [Range(1, 4)] public int pathWidth = 2;

    [Tooltip("How far the track wanders in and out of the middle of the crescent, in cells.")]
    [Range(0f, 5f)] public float pathWander = 1.6f;

    [Tooltip("Bends along its length.")]
    [Range(0.5f, 3f)] public float pathWaves = 1.6f;

    [Tooltip("A second track leading from the wood straight onto the farm.")]
    public bool pathSpur = true;

    [Header("Avenue")]
    [Tooltip("Distance between trees along each side of the path, in cells. The two sides are staggered by half of this.")]
    [Range(1f, 4f)] public float avenueSpacing = 1.7f;

    [Tooltip("Distance from the path's edge to the trunks, in cells.")]
    [Range(0.3f, 3f)] public float avenueOffset = 0.9f;

    [Tooltip("Sideways wander of each avenue tree, in cells.")]
    [Range(0f, 0.6f)] public float avenueJitter = 0.18f;

    [Header("Elder tree")]
    [Tooltip("The landmark, where the spur meets the path. A different shape entirely, not a big ordinary tree: see TreeMesh.Recipe.Elder.")]
    public bool  elderTree = true;
    [Range(1f, 5f)] public float elderScale = 2.2f;
    [Tooltip("Cells kept clear around it.")]
    [Range(1f, 8f)] public float clearing = 2.8f;

    [Header("Groves and undergrowth")]
    [Range(0, 16)] public int groves = 6;
    [Range(1, 6)]  public int treesPerGrove = 3;
    [Range(0, 100)] public int shrubs = 30;
    [Range(0, 200)] public int tufts = 70;

    [Tooltip("Closest two props of the same kind may stand, in cells.")]
    [Range(0.2f, 3f)] public float spacing = 1.1f;

    [Header("Path dressing")]
    [Range(0, 30)] public int wayMarkers = 10;
    public Color stoneColor = new(0.63f, 0.63f, 0.60f);

    [Header("Colour")]
    [Tooltip("Pale, warm, desaturated. A dark bark loses its hue entirely on its shaded side.")]
    public Color bark = new(0.74f, 0.67f, 0.57f);

    [Tooltip("Harmony is the jade-green synergy, so its wood leans into that hue the way the workshop leans into Order's grey and gold.")]
    public Color leafDeep  = new(0.20f, 0.52f, 0.30f);
    public Color leafLight = new(0.47f, 0.80f, 0.42f);

    [Header("Ground mist")]
    [Tooltip("A low, thin mist lying in the wood. It is lit through the shadow map, so the trees throw shafts of shade through it and the sun between them stands out as light beams — the Tyndall look. Needs Cast Shadows on.")]
    public bool groundMist = true;
    public MistBank.Settings groundMistStyle = new()
    {
        color = new Color(0.86f, 0.90f, 0.84f), strength = 0.7f, density = 0.7f,
        height = 5f, scatter = 2.6f, anisotropy = 0.6f, steps = 24,
        edge = 2.5f, clearance = 0f, edgeWarp = 1.2f,
    };

    [Header("Render")]
    [Range(1, 10)] public int variants = 6;

    [Tooltip("Trees cast shadows. Every OTHER prop on this map has casting off — but a wood without shadows on its own floor reads as cardboard standing on a plane.")]
    public bool castShadows = true;

    public override string RootName => "HarmonyGrove";

    public HarmonyGroveConfig()
    {
        enabled     = true;
        // With the farm: the wood half-rings it, and a wood around an empty
        // clearing — or a farm with its wood still missing — is neither picture.
        gateLevelId = "1-1";

        // Placeholder only: WrapFarm replaces origin and size with the crescent's
        // own bounds, derived from wherever the farm actually is.
        origin      = new Vector3Int(-30, 2, -14);
        size        = new Vector2Int(40, 40);

        // Deep, cool, early-morning green. Dark enough that the pale track reads as
        // cut INTO it, and cool enough that the warm bark sits against it.
        soilColor   = new Color(0.16f, 0.29f, 0.21f);
        soilJitter  = 0.12f;

        growYawOffset = 60f;
        growAsideText = "";
    }

    // ── Set by WrapFarm before the ground is laid ────────────────────────────
    // NonSerialized: all of it is derived from the scene at build time, and none of
    // it may be written into the scene or go stale against a farm that moved.

    [System.NonSerialized] public HashSet<Vector2Int> Blocked;   // columns that already have ground
    [System.NonSerialized] public bool    HasFarm;
    [System.NonSerialized] public Vector2 FarmCentre;             // cell space
    [System.NonSerialized] public Vector2 FarmHalf;               // half-extent to the cell EDGES
    [System.NonSerialized] public Vector2 Outward;                // unit: the farm's far side

    [System.NonSerialized] HashSet<Vector2Int> _paths;
    [System.NonSerialized] List<Vector2> _mainLine;
    [System.NonSerialized] List<Vector2> _spurLine;
    [System.NonSerialized] int _junction;

    public HashSet<Vector2Int> Paths    { get { Layout(); return _paths;    } }
    public List<Vector2>       MainLine { get { Layout(); return _mainLine; } }
    public List<Vector2>       SpurLine { get { Layout(); return _spurLine; } }
    public int                 Junction { get { Layout(); return _junction; } }

    public void Invalidate() { _paths = null; _mainLine = null; _spurLine = null; }

    // Distance from the farm's centre to its boundary along unit direction u.
    float ToEdge(Vector2 u) =>
        Mathf.Min(FarmHalf.x / Mathf.Max(1e-4f, Mathf.Abs(u.x)),
                  FarmHalf.y / Mathf.Max(1e-4f, Mathf.Abs(u.y)));

    // Signed angle from the farm's far side, in degrees.
    float FromBack(Vector2 u) =>
        Mathf.Atan2(Outward.x * u.y - Outward.y * u.x, Vector2.Dot(Outward, u)) * Mathf.Rad2Deg;

    // How deep the wood is in this direction. Full straight behind, tapering to
    // nothing at the ends of the arc — so the crescent's horns thin out round the
    // farm's sides instead of stopping on a line.
    float DepthAt(Vector2 u)
    {
        float phi = FromBack(u);
        if (Mathf.Abs(phi) >= arcHalf) return 0f;

        float w = Mathf.Cos(phi / arcHalf * Mathf.PI * 0.5f);
        float a = Mathf.Atan2(u.y, u.x);
        float ph = FarmCentre.x * 0.71f + FarmCentre.y * 1.37f;

        // Three low harmonics with their own phases: bays and headlands a few
        // cells across, and not a regular flower.
        float wave = 0.55f * Mathf.Sin(2f * a + ph)
                   + 0.30f * Mathf.Sin(3f * a - ph * 1.7f)
                   + 0.15f * Mathf.Sin(5f * a + ph * 0.6f);

        return depth * Mathf.Pow(w, 0.55f) * (1f + irregularity * wave);
    }

    // Is this column part of the wood's ground?
    //
    // Measured as distance out from the farm's boundary along the ray from its
    // centre. Everything from the farm's own OUTERMOST row (just inside the
    // boundary, -1 < d <= 0) out to the crescent's depth. That outermost row is what
    // makes the join flush: where the farm has ground there it is Blocked and stays
    // the farm's; where the farm's frayed edge left a hole, the wood fills it, so
    // there is never an empty cell between the two.
    public bool InLand(Vector2Int c)
    {
        if (!HasFarm) return false;
        if (Blocked != null && Blocked.Contains(c)) return false;

        Vector2 v = new Vector2(c.x, c.y) - FarmCentre;
        float dist = v.magnitude;
        if (dist < 1e-3f) return false;
        Vector2 u = v / dist;

        float d = dist - ToEdge(u);
        if (d <= -1f) return false;               // deeper inside the farm than its rim

        float reach = DepthAt(u);
        return reach > 0.5f && d <= reach;
    }

    void Layout()
    {
        if (_paths != null) return;

        _paths    = new HashSet<Vector2Int>();
        _mainLine = new List<Vector2>();
        _spurLine = new List<Vector2>();
        if (!HasFarm) return;

        // The main track runs ROUND the farm through the middle of the crescent,
        // wandering in and out of that middle on a sine. A sine, never a random
        // walk: a random walk doubles back on itself and reads as damage.
        float span  = arcHalf * 0.8f;
        float ph    = FarmCentre.x * 0.37f + FarmCentre.y * 0.61f;
        float ring  = Mathf.Max(FarmHalf.x, FarmHalf.y) + depth * 0.5f;
        int   steps = Mathf.Max(16, Mathf.CeilToInt(span * 2f * Mathf.Deg2Rad * ring * 4f));

        for (int k = 0; k <= steps; k++)
        {
            float t   = k / (float)steps;
            float phi = Mathf.Lerp(-span, span, t);
            Vector2 u = Rotate(Outward, phi);
            float reach = DepthAt(u);
            float mid   = reach * 0.5f + Mathf.Sin(t * Mathf.PI * 2f * pathWaves + ph)
                                       * Mathf.Min(pathWander, reach * 0.22f);
            _mainLine.Add(FarmCentre + u * (ToEdge(u) + mid));
        }
        Rasterise(_mainLine);

        // The spur leaves the main track straight behind the farm and runs in
        // until it reaches the farm's ground — where InLand stops it. This is the
        // way from the wood onto the fields.
        _junction = steps / 2;
        if (pathSpur)
        {
            Vector2 u0 = Outward;
            Vector2 side = new(-u0.y, u0.x);
            float from = (_mainLine[_junction] - FarmCentre).magnitude - ToEdge(u0);
            int n = Mathf.Max(4, Mathf.CeilToInt((from + 1f) * 4f));
            for (int k = 0; k <= n; k++)
            {
                float s = Mathf.Lerp(from, -0.6f, k / (float)n);
                float wig = Mathf.Sin(k / (float)n * Mathf.PI * 1.5f) * 0.6f;
                _spurLine.Add(FarmCentre + u0 * (ToEdge(u0) + s) + side * wig);
            }
            Rasterise(_spurLine);
        }

        KeepConnected();
    }

    static Vector2 Rotate(Vector2 v, float deg)
    {
        float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    // Stamp a square brush along the line, filling the corner cell on every
    // diagonal step. That corner fill is the fix for "broken": a one-cell line that
    // steps diagonally only touches its neighbours at their CORNERS, and on a grid of
    // blocks that reads as a dotted line.
    void Rasterise(List<Vector2> line)
    {
        int W  = Mathf.Max(1, pathWidth);
        int lo = -((W - 1) / 2), hi = W / 2;

        Vector2Int? prev = null;
        foreach (var p in line)
        {
            var c = new Vector2Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y));
            if (prev.HasValue && prev.Value.x != c.x && prev.Value.y != c.y)
                Stamp(new Vector2Int(c.x, prev.Value.y), lo, hi);
            Stamp(c, lo, hi);
            prev = c;
        }
    }

    void Stamp(Vector2Int c, int lo, int hi)
    {
        for (int dx = lo; dx <= hi; dx++)
            for (int dz = lo; dz <= hi; dz++)
            {
                var q = new Vector2Int(c.x + dx, c.y + dz);
                if (InLand(q)) _paths.Add(q);   // clipped to the ground, never out into air
            }
    }

    // Keep only the path cells 4-connected to the middle of the main track. The
    // crescent's edge has bays, and a bay that cuts the track would otherwise leave
    // a stranded fragment of path beyond it.
    void KeepConnected()
    {
        if (_paths.Count == 0 || _mainLine.Count == 0) return;

        Vector2 mid = _mainLine[_mainLine.Count / 2];
        var seed = new Vector2Int(Mathf.RoundToInt(mid.x), Mathf.RoundToInt(mid.y));
        if (!_paths.Contains(seed))
        {
            float best = float.MaxValue;
            foreach (var c in _paths)
            {
                float d = (c - seed).sqrMagnitude;
                if (d < best) { best = d; seed = c; }
            }
        }

        var keep  = new HashSet<Vector2Int> { seed };
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(seed);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            foreach (var n in new[] { c + Vector2Int.right, c + Vector2Int.left,
                                      c + Vector2Int.up,    c + Vector2Int.down })
                if (_paths.Contains(n) && keep.Add(n)) queue.Enqueue(n);
        }
        _paths = keep;
    }

    public override Color SoilAt(Vector2Int column) =>
        Paths.Contains(column) ? pathColor : soilColor;

    // Hard edge: ground or not, no dice roll.
    public override float CoverageAt(Vector2Int column) => InLand(column) ? 1f : 0f;
}

public partial class LevelMapController : MonoBehaviour
{
    enum GroveKind { Tree, Shrub, Tuft }

    // Fit the wood around the farm, from wherever the farm actually is.
    //
    // Reads the farm's footprint from its config (the scene's values, not the C#
    // defaults), finds which side of it faces away from the rest of the map, and
    // sizes the grove's plot to the crescent's bounds. Runs right before the grove
    // is built, which is after every other plot — so all of them are in
    // _columnTop and none of them can be overlapped.
    void AnchorGrove(HarmonyGroveConfig g)
    {
        if (g == null) return;

        var f   = decor;
        var fe  = (f.rotationSteps & 1) == 1 ? new Vector2Int(f.size.y, f.size.x) : f.size;
        var fo  = new Vector2Int(f.origin.x, f.origin.z);
        bool farmUp = _plots.Exists(p => p.cfg == decor);

        g.HasFarm    = f.enabled;
        g.FarmCentre = new Vector2(fo.x + (fe.x - 1) * 0.5f, fo.y + (fe.y - 1) * 0.5f);
        g.FarmHalf   = new Vector2(fe.x * 0.5f, fe.y * 0.5f);

        // Off limits: every column that already has ground, and each plot's
        // interior. The farm's interior only — its outermost row is left for the
        // wood to fill wherever the farm frayed, which is what closes the gap.
        // If the farm has not grown yet, its WHOLE footprint is kept clear instead,
        // so the wood rings an empty clearing that the farm will later rise into.
        var blocked = new HashSet<Vector2Int>(_columnTop.Keys);
        foreach (var p in _plots)
        {
            if (p?.cfg == null || p.cfg == g) continue;
            var e = (p.cfg.rotationSteps & 1) == 1 ? new Vector2Int(p.cfg.size.y, p.cfg.size.x) : p.cfg.size;
            for (int x = 1; x < e.x - 1; x++)
                for (int z = 1; z < e.y - 1; z++)
                    blocked.Add(new Vector2Int(p.cfg.origin.x + x, p.cfg.origin.z + z));
        }
        if (!farmUp)
            for (int x = 0; x < fe.x; x++)
                for (int z = 0; z < fe.y; z++)
                    blocked.Add(new Vector2Int(fo.x + x, fo.y + z));
        g.Blocked = blocked;

        // The far side: away from the centroid of everything else on the map — the
        // level nodes and the other plots. That is the side nothing needs to reach,
        // so it is the side a wood can close.
        Vector2 rest = Vector2.zero;
        int n = 0;
        foreach (var c in _columnTop.Keys)
        {
            bool inFarm = c.x >= fo.x && c.x < fo.x + fe.x && c.y >= fo.y && c.y < fo.y + fe.y;
            if (inFarm) continue;
            rest += new Vector2(c.x, c.y);
            n++;
        }
        Vector2 outward = n > 0 ? g.FarmCentre - rest / n : Vector2.up;
        g.Outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : Vector2.up;

        // The grove's plot is the crescent's bounding box. rotationSteps 0: the
        // shape is already in world columns and must not be turned again.
        int pad = Mathf.CeilToInt(g.depth * (1f + g.irregularity)) + 2;
        g.rotationSteps = 0;
        g.origin = new Vector3Int(fo.x - pad, f.origin.y, fo.y - pad);
        g.size   = new Vector2Int(fe.x + pad * 2, fe.y + pad * 2);

        g.Invalidate();
    }

    void BuildHarmonyGrove(HarmonyGroveConfig cfg,
                           HashSet<Vector2Int> coveredCols,
                           Dictionary<Vector2Int, Vector3Int> colTop,
                           Vector2Int ext, float cs)
    {
        var mat = GroveMaterial();
        if (mat == null) return;

        var root = new GameObject("Grove").transform;
        root.SetParent(_buildingRoot.transform, false);

        // Seeded from the farm, so the wood is the same every time the map is
        // rebuilt. A wood that reshuffles when you walk back onto the screen stops
        // feeling like a place.
        var rng = new System.Random(Mathf.RoundToInt(cfg.FarmCentre.x * 7919 + cfg.FarmCentre.y * 104729));

        var paths = cfg.Paths;
        var taken = new List<Vector2>();

        // A fixed palette of meshes per kind. Colour is baked into the mesh, so a
        // per-instance tint would need a MaterialPropertyBlock, and an MPB disables
        // the SRP Batcher on every renderer it touches.
        var trees  = Variants(cfg, GroveKind.Tree,  rng);
        var shrubs = Variants(cfg, GroveKind.Shrub, rng);
        var tufts  = Variants(cfg, GroveKind.Tuft,  rng);

        // ── 1. The elder, where the spur meets the path ──────────────────────
        // On the far side of the main track from the spur, so it faces the farm
        // across the junction — the first thing you see walking in from the fields.
        Vector2? elder = null;
        var main = cfg.MainLine;
        if (cfg.elderTree && main.Count > 2)
        {
            int j = Mathf.Clamp(cfg.Junction, 1, main.Count - 2);
            Vector2 at  = main[j];
            Vector2 tan = (main[j + 1] - main[j - 1]).normalized;
            Vector2 nrm = new(-tan.y, tan.x);
            if (Vector2.Dot(nrm, cfg.Outward) < 0f) nrm = -nrm;    // away from the farm

            Vector2 p = at + nrm * (cfg.pathWidth * 0.5f + cfg.clearing * 0.75f);
            if (OnGround(p, colTop) && !OnPath(paths, p, 0))
            {
                var mesh = TreeMesh.Build(GroveTint(TreeMesh.Recipe.Elder(), cfg), rng.Next());
                PlantGrove(root, mesh, mat, GroveGround(p, colTop, cfg, cs),
                           cfg.elderScale, (float)rng.NextDouble() * 360f, cfg.castShadows, "ElderTree");
                taken.Add(p);
                elder = p;
            }
        }

        // ── 2. The avenue ────────────────────────────────────────────────────
        Avenue(root, mat, cfg, colTop, paths, cs, rng, taken, elder, trees, main);
        Avenue(root, mat, cfg, colTop, paths, cs, rng, taken, elder, trees, cfg.SpurLine);

        // ── 3. Groves ────────────────────────────────────────────────────────
        // In small GROUPS rather than evenly spread. Evenly spread is how a
        // scatter looks; groups are how trees actually stand.
        var ground = new List<Vector2Int>();
        foreach (var c in coveredCols) if (!OnPath(paths, c, 2)) ground.Add(c);
        ground.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

        for (int g = 0; g < cfg.groves && ground.Count > 0; g++)
        {
            var c = ground[rng.Next(ground.Count)];
            Vector2 hub = new(c.x, c.y);
            if (elder.HasValue && Vector2.Distance(hub, elder.Value) < cfg.clearing + 1f) continue;

            for (int k = 0; k < cfg.treesPerGrove; k++)
                TryPlant(root, mat, cfg, colTop, paths, cs, rng, taken, elder, trees,
                         hub + Random2(rng) * 1.4f, 1, cfg.spacing,
                         new Vector2(0.8f, 1.35f), cfg.castShadows, "Tree");
        }

        // ── 4. Undergrowth ───────────────────────────────────────────────────
        Fill(root, mat, cfg, colTop, paths, cs, rng, taken, elder, shrubs, coveredCols,
             cfg.shrubs, verge: true,  cfg.spacing * 0.6f,  new Vector2(0.8f, 1.4f), "Shrub");
        Fill(root, mat, cfg, colTop, paths, cs, rng, taken, elder, tufts, coveredCols,
             cfg.tufts,  verge: false, cfg.spacing * 0.35f, new Vector2(0.7f, 1.3f), "Tuft");

        BuildWayMarkers(root, cfg, colTop, paths, cs, rng);

        // ── 5. Ground mist ───────────────────────────────────────────────────
        // Parented to the grove, which is still at rest here — so on the visit it
        // grows in, the mist sinks and rises with the wood like everything else.
        if (cfg.groundMist)
        {
            var pts = new List<Vector3>(coveredCols.Count);
            foreach (var c in coveredCols) pts.Add(GroveGround(new Vector2(c.x, c.y), colTop, cfg, cs));
            MistBank.Create(root, "GroundMist", pts, cs, cfg.groundMistStyle, rng.Next());
        }
    }

    // Trees down BOTH sides of a line, evenly spaced and staggered by half a step.
    // Two sides planted opposite each other read as a fence with a gap in it;
    // offset, they read as an avenue you walk down.
    void Avenue(Transform root, Material mat, HarmonyGroveConfig cfg,
                Dictionary<Vector2Int, Vector3Int> colTop, HashSet<Vector2Int> paths,
                float cs, System.Random rng, List<Vector2> taken, Vector2? elder,
                Mesh[] meshes, List<Vector2> line)
    {
        if (line == null || line.Count < 3) return;

        float step   = Mathf.Max(0.5f, cfg.avenueSpacing);
        float offset = cfg.pathWidth * 0.5f + cfg.avenueOffset;

        for (int side = -1; side <= 1; side += 2)
        {
            float next = side < 0 ? step * 0.5f : step;   // the stagger
            float run  = 0f;

            for (int i = 1; i < line.Count - 1; i++)
            {
                run += Vector2.Distance(line[i], line[i - 1]);
                if (run < next) continue;
                next += step;

                Vector2 tan = (line[i + 1] - line[i - 1]).normalized;
                Vector2 nrm = new(-tan.y, tan.x);
                Vector2 p = line[i] + nrm * (side * offset)
                          + tan * (((float)rng.NextDouble() * 2f - 1f) * cfg.avenueJitter);

                // The avenue parts around the elder rather than crowding it.
                if (elder.HasValue && Vector2.Distance(p, elder.Value) < cfg.clearing * 0.7f) continue;

                TryPlant(root, mat, cfg, colTop, paths, cs, rng, taken, null, meshes,
                         p, 0, step * 0.6f, new Vector2(0.95f, 1.12f), cfg.castShadows, "AvenueTree");
            }
        }
    }

    void Fill(Transform root, Material mat, HarmonyGroveConfig cfg,
              Dictionary<Vector2Int, Vector3Int> colTop, HashSet<Vector2Int> paths,
              float cs, System.Random rng, List<Vector2> taken, Vector2? elder,
              Mesh[] meshes, HashSet<Vector2Int> covered, int count, bool verge,
              float spacing, Vector2 scale, string label)
    {
        if (count <= 0 || covered.Count == 0) return;

        var cols = new List<Vector2Int>(covered);
        cols.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

        int placed = 0;
        for (int attempt = 0; attempt < count * 12 && placed < count; attempt++)
        {
            var c = cols[rng.Next(cols.Count)];
            if (verge && (!OnPath(paths, c, 2) || OnPath(paths, c, 0))) continue;

            Vector2 p = new Vector2(c.x, c.y) + Random2(rng) * 0.45f;
            if (TryPlant(root, mat, cfg, colTop, paths, cs, rng, taken, elder, meshes,
                         p, 0, spacing, scale, false, label))
                placed++;
        }
    }

    // One plant, if the spot is legal: on this plot's ground, off the track, clear of
    // the elder, and not crowding anything already standing.
    bool TryPlant(Transform root, Material mat, HarmonyGroveConfig cfg,
                  Dictionary<Vector2Int, Vector3Int> colTop, HashSet<Vector2Int> paths,
                  float cs, System.Random rng, List<Vector2> taken, Vector2? elder,
                  Mesh[] meshes, Vector2 p, int pathMargin, float spacing,
                  Vector2 scale, bool shadows, string label)
    {
        if (!OnGround(p, colTop)) return false;           // never on nothing
        if (OnPath(paths, p, pathMargin)) return false;
        if (elder.HasValue && Vector2.Distance(p, elder.Value) < cfg.clearing) return false;

        for (int i = 0; i < taken.Count; i++)
            if ((taken[i] - p).sqrMagnitude < spacing * spacing) return false;

        taken.Add(p);
        PlantGrove(root, meshes[rng.Next(meshes.Length)], mat, GroveGround(p, colTop, cfg, cs),
                   Mathf.Lerp(scale.x, scale.y, (float)rng.NextDouble()),
                   (float)rng.NextDouble() * 360f, shadows, label);
        return true;
    }

    // Stumps and stones along the verges, BESIDE a track and never on one.
    void BuildWayMarkers(Transform root, HarmonyGroveConfig cfg,
                         Dictionary<Vector2Int, Vector3Int> colTop,
                         HashSet<Vector2Int> paths, float cs, System.Random rng)
    {
        if (cfg.wayMarkers <= 0 || paths.Count == 0) return;

        var edgeSet = new HashSet<Vector2Int>();
        foreach (var c in paths)
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    var q = new Vector2Int(c.x + dx, c.y + dz);
                    if (!paths.Contains(q) && colTop.ContainsKey(q)) edgeSet.Add(q);
                }
        if (edgeSet.Count == 0) return;
        var edge = new List<Vector2Int>(edgeSet);
        edge.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

        // A cut stump: the ordinary tree with one level and no leaves, keeping its
        // bark and root flare, so it reads as one of these trees with its top gone.
        var stump = TreeMesh.Recipe.Tree();
        stump.height = 0.42f; stump.trunkRadius = 0.20f; stump.levels = 1;
        stump.leafSize = 0f;  stump.leavesPerTip = 0;    stump.rootFlare = 4;
        stump.trunkLean = 0f; stump.trunkBend = 0f;      stump.trunkNodes = 2;
        stump.bark = cfg.bark;

        var mat = GroveMaterial();

        for (int i = 0; i < cfg.wayMarkers; i++)
        {
            var col = edge[rng.Next(edge.Count)];
            Vector2 at = new Vector2(col.x, col.y) + Random2(rng) * 0.25f;
            Vector3 pos = GroveGround(at, colTop, cfg, cs);

            if (rng.Next(3) == 0)
            {
                PlantGrove(root, TreeMesh.Build(stump, rng.Next()), mat, pos,
                           Mathf.Lerp(0.7f, 1.2f, (float)rng.NextDouble()),
                           (float)rng.NextDouble() * 360f, cfg.castShadows, "Stump");
            }
            else
            {
                // A boulder is the observatory's dome mesh, squashed — one silhouette
                // vocabulary for the map's props instead of a new primitive.
                float r = Mathf.Lerp(0.16f, 0.32f, (float)rng.NextDouble()) * cs;
                MakeMeshProp(root, "Stone", DomeMesh(), pos,
                             Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f),
                             new Vector3(r * 2f, r * Mathf.Lerp(0.7f, 1.2f, (float)rng.NextDouble()), r * 2f),
                             Tint(cfg.stoneColor, Mathf.Lerp(0.88f, 1.12f, (float)rng.NextDouble())));
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    static bool OnGround(Vector2 p, Dictionary<Vector2Int, Vector3Int> colTop) =>
        colTop.ContainsKey(new Vector2Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y)));

    static bool OnPath(HashSet<Vector2Int> paths, Vector2 p, int margin)
    {
        int cx = Mathf.RoundToInt(p.x), cz = Mathf.RoundToInt(p.y);
        for (int dx = -margin; dx <= margin; dx++)
            for (int dz = -margin; dz <= margin; dz++)
                if (paths.Contains(new Vector2Int(cx + dx, cz + dz))) return true;
        return false;
    }

    static Vector2 Random2(System.Random rng) =>
        new((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f);

    Mesh[] Variants(HarmonyGroveConfig cfg, GroveKind kind, System.Random rng)
    {
        var m = new Mesh[Mathf.Clamp(cfg.variants, 1, 10)];
        for (int v = 0; v < m.Length; v++) m[v] = TreeMesh.Build(GroveTint(GroveRecipe(kind), cfg), rng.Next());
        return m;
    }

    // Height from the column; sub-cell position kept exactly. Snapping trees to cell
    // centres would put a wood on a chessboard. GridToWorld puts cell c at
    // (c + 0.5) * cellSize, so a continuous column coordinate maps the same way.
    Vector3 GroveGround(Vector2 p, Dictionary<Vector2Int, Vector3Int> colTop,
                        MapDecorConfig cfg, float cs)
    {
        var col = new Vector2Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y));
        float y = ColumnSurface(colTop, col, cfg, cs).y;
        var o = gridSystem.Origin;   // the map may sit on a shifted grid (GridSystem.originCells)
        return new Vector3(o.x + (p.x + 0.5f) * cs, y, o.z + (p.y + 0.5f) * cs);
    }

    // Trees are NOT built with MakeMeshProp. That helper tints a prop with MpbColor on
    // one shared material — right for a gear or a stone, one flat colour each. A
    // tree is bark AND foliage in one mesh, so its colour is in its vertices, and an
    // MPB would both override that and disable the SRP Batcher.
    void PlantGrove(Transform root, Mesh mesh, Material mat, Vector3 pos,
                    float scale, float yaw, bool shadows, string label)
    {
        var go = new GameObject(label);
        go.transform.SetParent(root, false);
        go.transform.SetPositionAndRotation(
            pos, Quaternion.Euler(Random.Range(-3f, 3f), yaw, Random.Range(-3f, 3f)));
        go.transform.localScale = Vector3.one * scale;

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = shadows
            ? UnityEngine.Rendering.ShadowCastingMode.TwoSided
            : UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows       = true;
        mr.lightProbeUsage      = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    static TreeMesh.Recipe GroveRecipe(GroveKind kind) => kind switch
    {
        GroveKind.Shrub => TreeMesh.Recipe.Shrub(),
        GroveKind.Tuft  => TreeMesh.Recipe.Tuft(),
        _               => TreeMesh.Recipe.Tree(),
    };

    static TreeMesh.Recipe GroveTint(TreeMesh.Recipe r, HarmonyGroveConfig cfg)
    {
        r.bark  = cfg.bark;
        r.leafA = cfg.leafDeep;
        r.leafB = cfg.leafLight;
        return r;
    }

    static Material _groveMat;
    static Material GroveMaterial()
    {
        if (_groveMat != null) return _groveMat;
        Shader sh = Shader.Find("GeoWorld/Foliage");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) return null;
        _groveMat = new Material(sh) { name = "Grove Foliage (runtime, shared)" };
        if (_groveMat.HasProperty("_Cull")) _groveMat.SetFloat("_Cull", 0f);
        return _groveMat;
    }
}
