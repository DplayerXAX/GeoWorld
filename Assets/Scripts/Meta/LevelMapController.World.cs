using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The overworld as ONE place, not a set of plots floating in the fog.
//
// Each decor plot (farm, wood, workshop, observatory, volcano, ocean, desert) and
// each level block was built on its own: its own height, its own colours, its own
// edge, and between them nothing. That is what made the map feel in pieces. Four
// things tie it back together, none of which changes how anything is walked:
//
//   1  THE CONTINENT   a skin of rock under every region and across the gaps between
//                      them — one landmass the plots stand on, the moats a trench in
//                      it, the cliffs plunging into the fog. Built as a few merged
//                      meshes; not walkable, not part of the grid.
//   2  THE ROAD        one path from the start through every level and region, its
//                      surface changing with the ground it crosses. Where the road
//                      has not been built yet it is drawn as a dotted ghost, so a
//                      gap reads as an unfinished road; the bridge the player builds
//                      takes the road's own look and closes it.
//   3  BLENDED EDGES   a plot's ground eases into the colour of the land around it
//                      over a few columns, and that land is a blend of the biomes
//                      near it — so colour carries across a border.
//   4  ONE SEA OF FOG  the banks of mist are one cloud (same noise everywhere) that
//                      also lies over the land between hidden regions, so the front
//                      that recedes with each clear is a line, not a set of puffs.
//
// Switch the whole thing off with `worldIntegration`.
public partial class LevelMapController : MonoBehaviour
{
    [Header("World integration (one landmass, one road, one sea of fog)")]
    [Tooltip("Master switch for everything in this section. Off = the map is exactly as it was: separate plots, no road, no bedrock, a bank of mist per gate.")]
    public bool worldIntegration = true;

    [Header("World · the continent")]
    public bool bedrockEnabled = true;
    [Tooltip("Gaps between regions up to twice this many columns are filled with land.")]
    [Range(1, 8)] public int bedrockClose = 5;
    [Tooltip("How far the cliffs go down below the lowest ground, in cells — into the fog.")]
    [Range(2, 16)] public int bedrockDepth = 8;
    [Tooltip("How much deeper than the land around it the moat round a level is sunk.")]
    [Range(0, 3)] public int moatTrench = 1;
    public Color bedrockCliff = new Color(0.46f, 0.43f, 0.40f);
    [Tooltip("Brightness of the lowland against the blended biome colour.")]
    [Range(0.5f, 1.2f)] public float lowlandShade = 0.8f;

    [Header("World · blended edges")]
    [Tooltip("Columns, inside a plot's edge, over which its ground eases into the land colour around it. 0 = hard edges.")]
    [Range(0, 8)] public int blendBand = 4;
    [Range(0f, 1f)] public float blendStrength = 0.6f;

    [Header("World · the road")]
    public bool spineRoad = true;
    [Tooltip("Road width as a fraction of a cell.")]
    [Range(0.3f, 1f)] public float roadWidth = 0.62f;
    [Tooltip("Levels the road does not go through (the creative space).")]
    public List<string> roadSkipLevels = new() { "Test" };
    [Tooltip("After the last level, the road goes on through these regions, in order (a decor plot's RootName).")]
    public List<string> roadRegions = new() { "OrderWorkshop", "Observatory", "Ocean", "Volcano" };
    [Tooltip("The dotted road over a gap that has not been bridged. Alpha is its brightest.")]
    public Color roadGhostColor = new Color(1f, 0.93f, 0.70f, 0.60f);

    // ── Biomes ───────────────────────────────────────────────────────────────

    struct Biome
    {
        public MapDecorConfig cfg;
        public int x0, z0, x1, z1;     // footprint, inclusive, in columns
        public Color land;             // the colour of the ground round here
        public Color road;             // the road's surface
    }

    readonly List<Biome> _biomes = new();

    static readonly Color HubRoad = new Color(0.86f, 0.84f, 0.78f);

    // From the configs alone, so it can run before a single plot is built — the
    // road colour of a restored bridge needs it. Run again once the plots stand: the
    // wood is placed round the farm at build time and moves.
    void PrepareBiomes()
    {
        _biomes.Clear();
        if (!worldIntegration) return;
        foreach (var cfg in AllDecorConfigs())
        {
            if (cfg == null || !cfg.enabled) continue;
            var e = cfg.Extent;
            _biomes.Add(new Biome
            {
                cfg  = cfg,
                x0   = cfg.origin.x,           z0 = cfg.origin.z,
                x1   = cfg.origin.x + e.x - 1, z1 = cfg.origin.z + e.y - 1,
                land = LandColor(cfg),
                road = RoadColor(cfg),
            });
        }
    }

    static Color LandColor(MapDecorConfig cfg)
    {
        switch (cfg)
        {
            case AbundanceFarmConfig f: return Color.Lerp(f.soilColor, new Color(0.46f, 0.52f, 0.28f), 0.45f);
            case HarmonyGroveConfig g:  return Color.Lerp(g.soilColor, new Color(0.30f, 0.44f, 0.26f), 0.50f);
            case ObservatoryConfig s:   return Color.Lerp(s.soilColor, s.gardenColor, 0.5f);
            case VolcanoConfig v:       return v.ashColor;
            case OceanConfig o:         return o.sandColor;
            default:                    return cfg.soilColor;
        }
    }

    static Color RoadColor(MapDecorConfig cfg)
    {
        switch (cfg)
        {
            case AbundanceFarmConfig _: return new Color(0.80f, 0.70f, 0.52f);   // packed earth
            case HarmonyGroveConfig _:  return new Color(0.74f, 0.66f, 0.48f);   // leaf-litter path
            case OrderWorkshopConfig _: return new Color(0.60f, 0.62f, 0.66f);   // steel plate
            case ObservatoryConfig _:   return new Color(0.78f, 0.77f, 0.74f);   // flagstone
            case OceanConfig _:         return new Color(0.72f, 0.54f, 0.33f);   // boardwalk
            case VolcanoConfig _:       return new Color(0.26f, 0.21f, 0.23f);   // obsidian
            case DesertConfig _:        return new Color(0.95f, 0.85f, 0.60f);   // pale sand
            default:                    return HubRoad;
        }
    }

    static float RectDist(Biome b, Vector2Int c)
    {
        float dx = Mathf.Max(0, Mathf.Max(b.x0 - c.x, c.x - b.x1));
        float dz = Mathf.Max(0, Mathf.Max(b.z0 - c.y, c.y - b.z1));
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // The land colour at a column: the biomes near it, weighted by how near.
    Color LowlandAt(Vector2Int col)
    {
        if (_biomes.Count == 0) return new Color(0.52f, 0.50f, 0.46f);
        float wsum = 0f; float r = 0f, g = 0f, b = 0f;
        foreach (var bm in _biomes)
        {
            float w = Mathf.Exp(-RectDist(bm, col) / 5f);
            r += bm.land.r * w; g += bm.land.g * w; b += bm.land.b * w; wsum += w;
        }
        if (wsum < 1e-6f) return _biomes[0].land;
        return new Color(r / wsum, g / wsum, b / wsum, 1f);
    }

    Color RoadColorAt(Vector2Int col)
    {
        float best = float.MaxValue;
        Color c = HubRoad;
        foreach (var bm in _biomes)
        {
            float d = RectDist(bm, col);
            if (d < best) { best = d; c = bm.road; }
        }
        return best > 8f ? HubRoad : c;
    }

    // What a bridge the player builds looks like: the road under it, a touch lifted.
    Color BuiltPieceColor(Vector3Int[] cells)
    {
        if (!worldIntegration || !spineRoad || cells == null || cells.Length == 0) return playerBuiltColor;
        return Color.Lerp(RoadColorAt(new Vector2Int(cells[0].x, cells[0].z)), Color.white, 0.12f);
    }

    // ── 3 · Blended edges ────────────────────────────────────────────────────

    // Columns in from a plot's edge (0 = the outermost), up to the blend band.
    Dictionary<Vector2Int, int> EdgeDistances(HashSet<Vector2Int> cols)
    {
        if (!worldIntegration || blendBand <= 0 || cols == null || cols.Count == 0) return null;

        var dist = new Dictionary<Vector2Int, int>();
        var q = new Queue<Vector2Int>();
        foreach (var c in cols)
            for (int s = 0; s < 4; s++)
                if (!cols.Contains(c + Side4[s])) { dist[c] = 0; q.Enqueue(c); break; }

        while (q.Count > 0)
        {
            var c = q.Dequeue();
            int d = dist[c];
            if (d >= blendBand) continue;
            for (int s = 0; s < 4; s++)
            {
                var n = c + Side4[s];
                if (!cols.Contains(n) || dist.ContainsKey(n)) continue;
                dist[n] = d + 1;
                q.Enqueue(n);
            }
        }
        return dist;
    }

    Color BlendEdge(Color c, Vector2Int col, Dictionary<Vector2Int, int> edge)
    {
        if (edge == null || !edge.TryGetValue(col, out int d)) return c;
        float t = 1f - (d + 0.5f) / (blendBand + 0.5f);
        if (t <= 0f) return c;
        t = t * t * (3f - 2f * t) * blendStrength;
        var land = LowlandAt(col);
        return new Color(Mathf.Lerp(c.r, land.r, t), Mathf.Lerp(c.g, land.g, t), Mathf.Lerp(c.b, land.b, t), c.a);
    }

    // ── 1 · The continent ────────────────────────────────────────────────────

    GameObject _worldRoot;
    readonly Dictionary<Vector2Int, int> _bedrockTop  = new();   // every continent column -> its top cell y
    readonly HashSet<Vector2Int>         _bedrockLand = new();   // the columns that carry real ground

    // Faces that share a colour, merged into one mesh.
    class FaceBatch
    {
        public Color color;
        public readonly List<Vector3> v = new();
        public readonly List<Vector3> n = new();
        public readonly List<int>     t = new();
    }

    static void AddQuad(FaceBatch b, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 normal)
    {
        // Wound so the front faces `normal`, whichever way the corners were given.
        if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), normal) < 0f) { var s = p1; p1 = p3; p3 = s; }
        int o = b.v.Count;
        b.v.Add(p0); b.v.Add(p1); b.v.Add(p2); b.v.Add(p3);
        for (int i = 0; i < 4; i++) b.n.Add(normal);
        b.t.Add(o); b.t.Add(o + 1); b.t.Add(o + 2);
        b.t.Add(o); b.t.Add(o + 2); b.t.Add(o + 3);
    }

    // Colours are snapped to a coarse grid so a few dozen meshes cover the lot — the
    // snapping reads as the same per-block variation the plots' own ground has.
    const int Levels = 20;
    static int ColorKey(Color c)
    {
        int r = Mathf.Clamp(Mathf.RoundToInt(c.r * (Levels - 1)), 0, Levels - 1);
        int g = Mathf.Clamp(Mathf.RoundToInt(c.g * (Levels - 1)), 0, Levels - 1);
        int b = Mathf.Clamp(Mathf.RoundToInt(c.b * (Levels - 1)), 0, Levels - 1);
        return (r * Levels + g) * Levels + b;
    }
    static Color FromKey(int k)
    {
        int b = k % Levels, g = (k / Levels) % Levels, r = k / (Levels * Levels);
        return new Color(r / (float)(Levels - 1), g / (float)(Levels - 1), b / (float)(Levels - 1), 1f);
    }

    static FaceBatch BatchFor(Dictionary<int, FaceBatch> map, Color c)
    {
        int k = ColorKey(c);
        if (!map.TryGetValue(k, out var b)) map[k] = b = new FaceBatch { color = FromKey(k) };
        return b;
    }

    float CubeSize()
    {
        var mf = cubePrefab != null ? cubePrefab.GetComponentInChildren<MeshFilter>(true) : null;
        if (mf != null && mf.sharedMesh != null)
        {
            float v = mf.sharedMesh.bounds.size.x * Mathf.Abs(mf.transform.lossyScale.x);
            if (v > 0.05f) return v;
        }
        return gridSystem != null ? gridSystem.cellSize : 1f;
    }

    // The block material itself — so the rock is lit, fogged and dithered by the
    // pawn exactly as the blocks above it are.
    Material CubeMaterial()
    {
        var r = cubePrefab != null ? cubePrefab.GetComponentInChildren<Renderer>(true) : null;
        if (r != null)
        {
            var ms = r.sharedMaterials;
            if (ms != null && ms.Length > 0 && ms[0] != null) return ms[0];
        }
        return new Material(Shader.Find("Universal Render Pipeline/Lit"));
    }

    // Every batch becomes one renderer under `root`; vertices are in world space.
    void EmitBatches(Transform root, Dictionary<int, FaceBatch> batches, string name, bool castShadows = true)
    {
        var mat = CubeMaterial();
        foreach (var b in batches.Values)
        {
            if (b.v.Count == 0) continue;
            var mesh = new Mesh
            {
                name = name,
                indexFormat = b.v.Count > 60000 ? UnityEngine.Rendering.IndexFormat.UInt32
                                                 : UnityEngine.Rendering.IndexFormat.UInt16,
            };
            mesh.SetVertices(b.v);
            mesh.SetNormals(b.n);
            mesh.SetTriangles(b.t, 0);
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(root, true);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            if (!castShadows) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            MpbColor.Set(mr, b.color);
        }
    }

    Transform NewWorldRoot(string name)
    {
        var go = new GameObject(name);
        go.transform.position = Vector3.zero;       // vertices are world positions
        go.transform.rotation = Quaternion.identity;
        go.transform.SetParent(transform, true);
        return go.transform;
    }

    void BuildBedrock()
    {
        if (_worldRoot != null) { Destroy(_worldRoot); _worldRoot = null; }
        _bedrockTop.Clear();
        _bedrockLand.Clear();
        if (!worldIntegration || !bedrockEnabled || gridSystem == null || cubePrefab == null) return;

        // ── Land: every column that has, or will have, ground — and the cell just
        //    under that ground, where the rock stops.
        var land = new Dictionary<Vector2Int, int>();
        void Land(Vector2Int col, int t)
        {
            if (!land.TryGetValue(col, out var e) || t < e) land[col] = t;
        }

        foreach (var b in _biomes)
        {
            var cfg = b.cfg;
            // The wood is laid round the farm when it is built; before that it has no
            // footprint to speak of.
            if (cfg is HarmonyGroveConfig && !_plots.Exists(p => p.cfg == cfg)) continue;
            int t = cfg.origin.y - cfg.foundation - 1;
            for (int x = b.x0; x <= b.x1; x++)
                for (int z = b.z0; z <= b.z1; z++)
                {
                    var col = new Vector2Int(x, z);
                    if (KeptClear(col)) continue;               // a moat or a carved court stays a gap
                    if (cfg.CoverageAt(col) <= 0f) continue;
                    Land(col, t);
                }
        }

        void FromNode(LevelNode n)
        {
            if (n == null || n.cells == null || n.cells.Length == 0) return;
            if (_plotNodes.Contains(n)) return;
            if (n.gameObject.name.StartsWith("PlayerBuilt_")) return;   // bridges are not land
            var low = new Dictionary<Vector2Int, int>();
            foreach (var c in n.cells)
            {
                var col = new Vector2Int(c.x, c.z);
                if (!low.TryGetValue(col, out var y) || c.y < y) low[col] = c.y;
            }
            foreach (var kv in low) Land(kv.Key, kv.Value - 1);
        }
        foreach (var n in _nodes)  FromNode(n);
        foreach (var n in _veiled) FromNode(n);

        if (land.Count == 0) return;

        // ── The grid the fill runs on.
        int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
        foreach (var c in land.Keys)
        {
            minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x);
            minZ = Mathf.Min(minZ, c.y); maxZ = Mathf.Max(maxZ, c.y);
        }
        int pad = bedrockClose + 2;
        minX -= pad; minZ -= pad; maxX += pad; maxZ += pad;
        int W = maxX - minX + 1, D = maxZ - minZ + 1;
        int N = W * D;

        var isLand = new bool[N];
        var landT  = new float[N];
        var nearT  = new float[N];
        var dist   = new int[N];
        for (int i = 0; i < N; i++) dist[i] = -1;

        var q = new Queue<int>();
        foreach (var kv in land)
        {
            int i = (kv.Key.y - minZ) * W + (kv.Key.x - minX);
            isLand[i] = true; landT[i] = kv.Value; nearT[i] = kv.Value; dist[i] = 0;
            q.Enqueue(i);
        }

        int[] dxs = { 1, -1, 0, 0 }, dzs = { 0, 0, 1, -1 };

        // Close the gaps: grow the land by `bedrockClose`, carrying along the height
        // of the land each new column grew from…
        while (q.Count > 0)
        {
            int i = q.Dequeue();
            if (dist[i] >= bedrockClose) continue;
            int x = i % W, z = i / W;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + dxs[k], nz = z + dzs[k];
                if (nx < 0 || nz < 0 || nx >= W || nz >= D) continue;
                int j = nz * W + nx;
                if (dist[j] >= 0) continue;
                dist[j] = dist[i] + 1;
                nearT[j] = nearT[i];
                q.Enqueue(j);
            }
        }

        // …then take the outer shell back, all but one column of it: what is left is
        // the land plus every gap narrower than twice that.
        var outD = new int[N];
        for (int i = 0; i < N; i++)
        {
            if (dist[i] < 0) { outD[i] = 0; q.Enqueue(i); }
            else outD[i] = -1;
        }
        while (q.Count > 0)
        {
            int i = q.Dequeue();
            int x = i % W, z = i / W;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + dxs[k], nz = z + dzs[k];
                if (nx < 0 || nz < 0 || nx >= W || nz >= D) continue;
                int j = nz * W + nx;
                if (outD[j] >= 0) continue;
                outD[j] = outD[i] + 1;
                q.Enqueue(j);
            }
        }

        var keep = new bool[N];
        for (int i = 0; i < N; i++)
            keep[i] = isLand[i] || (dist[i] >= 0 && outD[i] >= bedrockClose);

        // ── Heights: land keeps its own; a gap takes a smooth blend of the land round
        //    it (relaxed), then sinks one below it so the plots stand on a step.
        var H = new float[N];
        for (int i = 0; i < N; i++) H[i] = isLand[i] ? landT[i] : nearT[i];
        for (int it = 0; it < 10; it++)
        {
            var nh = (float[])H.Clone();
            for (int i = 0; i < N; i++)
            {
                if (!keep[i] || isLand[i]) continue;
                int x = i % W, z = i / W;
                float s = 0f; int c = 0;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + dxs[k], nz = z + dzs[k];
                    if (nx < 0 || nz < 0 || nx >= W || nz >= D) continue;
                    int j = nz * W + nx;
                    if (!keep[j]) continue;
                    s += H[j]; c++;
                }
                if (c > 0) nh[i] = s / c;
            }
            H = nh;
        }

        int minH = int.MaxValue;
        for (int i = 0; i < N; i++)
        {
            if (!keep[i]) continue;
            var col = new Vector2Int(minX + i % W, minZ + i / W);
            int h;
            if (isLand[i]) h = Mathf.RoundToInt(landT[i]);
            else
            {
                h = Mathf.FloorToInt(H[i]) - 1;
                if (_moatCols.Contains(col)) h -= moatTrench;
            }
            _bedrockTop[col] = h;
            if (isLand[i]) _bedrockLand.Add(col);
            minH = Mathf.Min(minH, h);
        }
        if (_bedrockTop.Count == 0) return;

        // ── The skin: a top face per column, and a wall wherever the ground steps
        //    down — or ends, when it runs on down into the fog.
        float half = CubeSize() * 0.5f;
        int bottomY = minH - bedrockDepth;
        var batches = new Dictionary<int, FaceBatch>();

        foreach (var kv in _bedrockTop)
        {
            var col = kv.Key;
            int h = kv.Value;
            bool isL = _bedrockLand.Contains(col);

            Vector3 c = gridSystem.GridToWorld(new Vector3Int(col.x, h, col.y));
            float yTop = c.y + half;

            Color top = LowlandAt(col);
            top = new Color(top.r * lowlandShade, top.g * lowlandShade, top.b * lowlandShade, 1f);
            float jit = 0.94f + 0.12f * Hash01(DecorHash(col.x, col.y));
            top = Tint(top, jit);
            Color wall = Tint(Color.Lerp(bedrockCliff, top, 0.25f), 0.92f);

            // Under real ground the top face is never seen.
            bool covered = isL && _allCells.Contains(new Vector3Int(col.x, h + 1, col.y));
            if (!covered)
            {
                AddQuad(BatchFor(batches, top),
                        new Vector3(c.x - half, yTop, c.z - half), new Vector3(c.x - half, yTop, c.z + half),
                        new Vector3(c.x + half, yTop, c.z + half), new Vector3(c.x + half, yTop, c.z - half),
                        Vector3.up);
            }

            for (int s = 0; s < 4; s++)
            {
                var d = Side4[s];
                var nc = col + d;
                float yBot;
                if (_bedrockTop.TryGetValue(nc, out int nh))
                {
                    if (nh >= h) continue;
                    yBot = gridSystem.GridToWorld(new Vector3Int(col.x, nh, col.y)).y + half;
                }
                else
                {
                    yBot = gridSystem.GridToWorld(new Vector3Int(col.x, bottomY, col.y)).y - half;
                }

                var nrm = new Vector3(d.x, 0f, d.y);
                var ctr = new Vector3(c.x + d.x * half, 0f, c.z + d.y * half);
                var per = new Vector3(-d.y, 0f, d.x) * half;   // along the wall
                AddQuad(BatchFor(batches, wall),
                        new Vector3(ctr.x - per.x, yTop, ctr.z - per.z), new Vector3(ctr.x + per.x, yTop, ctr.z + per.z),
                        new Vector3(ctr.x + per.x, yBot, ctr.z + per.z), new Vector3(ctr.x - per.x, yBot, ctr.z - per.z),
                        nrm);
            }
        }

        _worldRoot = NewWorldRoot("WorldBedrock").gameObject;
        EmitBatches(_worldRoot.transform, batches, "Bedrock");
    }

    // ── 4 · One sea of fog ───────────────────────────────────────────────────

    // The land between hidden regions goes under the mist of the gate that will
    // reveal its nearest region. So adjacent banks touch, and with the same noise
    // everywhere they are one cloud; a clear then burns off a region AND the ground
    // up to its neighbour, and the front moves on to the next.
    void ExtendMistCover(Dictionary<string, List<Vector3>> cover)
    {
        if (!worldIntegration || _bedrockTop.Count == 0 || gridSystem == null) return;

        var label = new Dictionary<Vector2Int, string>();   // "" = on show
        foreach (var kv in cover)
            foreach (var p in kv.Value)
                label[ColumnOf(p)] = kv.Key;

        var q = new Queue<Vector2Int>();
        foreach (var col in _bedrockLand)
        {
            if (!label.ContainsKey(col)) label[col] = "";
            q.Enqueue(col);
        }

        while (q.Count > 0)
        {
            var c = q.Dequeue();
            string lab = label[c];
            for (int s = 0; s < 4; s++)
            {
                var n = c + Side4[s];
                if (!_bedrockTop.ContainsKey(n) || label.ContainsKey(n)) continue;
                label[n] = lab;
                q.Enqueue(n);
            }
        }

        foreach (var kv in label)
        {
            if (string.IsNullOrEmpty(kv.Value) || _bedrockLand.Contains(kv.Key)) continue;
            if (!_bedrockTop.TryGetValue(kv.Key, out int h)) continue;
            if (!cover.TryGetValue(kv.Value, out var list)) continue;
            list.Add(BlockTop(new Vector3Int(kv.Key.x, h, kv.Key.y)));
        }
    }

    // ── 2 · The road ─────────────────────────────────────────────────────────

    bool       _roadDirty;
    GameObject _roadRoot;
    static Material _roadGhostMat;

    struct RoadStop
    {
        public Vector2Int col;
        public bool hidden;   // in a region that has not been revealed
    }

    // Waits for the map to settle, then rebuilds the road whenever it changed — once
    // per change however many nodes the change touched, and not while a reveal is
    // raising ground the road would otherwise be laid in the air over.
    IEnumerator RoadRoutine()
    {
        bool wasCut = false;
        while (true)
        {
            bool cut = _decorCutscenePlaying;
            if (wasCut && !cut) _roadDirty = true;
            wasCut = cut;

            if (_roadDirty && _mapReady && !cut)
            {
                _roadDirty = false;
                RebuildRoad();
            }
            yield return null;
        }
    }

    List<RoadStop> CollectRoadStops()
    {
        var list = new List<RoadStop>();
        void Add(Vector2Int col, bool hidden)
        {
            if (list.Count > 0 && list[list.Count - 1].col == col) return;
            list.Add(new RoadStop { col = col, hidden = hidden });
        }

        if (_startNode != null && _startNode.cells != null && _startNode.cells.Length > 0)
        {
            var t = TopCellOf(_startNode);
            Add(new Vector2Int(t.x, t.z), false);
        }

        if (database != null && database.levels != null)
        {
            foreach (var lv in database.levels)
            {
                if (lv == null || roadSkipLevels.Contains(lv.levelId)) continue;
                bool hid = false;
                var n = _nodes.Find(x => x != null && x.level == lv);
                if (n == null) { n = _veiled.Find(x => x != null && x.level == lv); hid = n != null; }
                if (n == null || n.cells == null || n.cells.Length == 0) continue;
                var t = TopCellOf(n);
                Add(new Vector2Int(t.x, t.z), hid);
            }
        }

        foreach (var key in roadRegions)
            foreach (var b in _biomes)
            {
                if (b.cfg == null || b.cfg.RootName != key) continue;
                var e = b.cfg.Extent;
                var centre = new Vector2Int(b.cfg.origin.x + e.x / 2, b.cfg.origin.z + e.y / 2);
                bool built = _plots.Exists(p => p.cfg == b.cfg);
                Add(built ? NearestWalkable(centre, 8) : centre, !built);
            }

        return list;
    }

    bool Walkable(Vector2Int col, out Vector3Int top)
        => _columnTop.TryGetValue(col, out top) && _surface.Contains(top);

    Vector2Int NearestWalkable(Vector2Int c, int radius)
    {
        if (Walkable(c, out _)) return c;
        for (int r = 1; r <= radius; r++)
            for (int x = -r; x <= r; x++)
                for (int z = -r; z <= r; z++)
                {
                    if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) != r) continue;
                    var n = new Vector2Int(c.x + x, c.y + z);
                    if (Walkable(n, out _)) return n;
                }
        return c;
    }

    // Cheapest way along the ground, crossing a gap only where it must — and a gap
    // inside the continent only; a prop in the way is a wall.
    List<Vector2Int> FindRoad(Vector2Int from, Vector2Int to)
    {
        if (!_bedrockTop.ContainsKey(from) || !_bedrockTop.ContainsKey(to)) return null;
        if (from == to) return new List<Vector2Int> { from };

        float Heur(Vector2Int a) => Mathf.Abs(a.x - to.x) + Mathf.Abs(a.y - to.y);

        var g    = new Dictionary<Vector2Int, float>();
        var prev = new Dictionary<Vector2Int, Vector2Int>();
        var open = new SortedSet<(float f, int x, int z)>();
        g[from] = 0f;
        open.Add((Heur(from), from.x, from.y));

        int guard = 0;
        bool found = false;
        while (open.Count > 0 && guard++ < 60000)
        {
            var top = open.Min;
            open.Remove(top);
            var c = new Vector2Int(top.x, top.z);
            if (c == to) { found = true; break; }
            float gc = g[c];
            if (top.f > gc + Heur(c) + 0.001f) continue;   // a stale entry

            for (int s = 0; s < 4; s++)
            {
                var n = c + Side4[s];
                float step;
                if (n == to)                              step = 1f;
                else if (_decorSolid.Contains(n))         continue;
                else if (Walkable(n, out _))              step = 1f;
                else if (_bedrockTop.ContainsKey(n))      step = 2.2f;
                else                                      continue;

                float ng = gc + step;
                if (g.TryGetValue(n, out var og) && og <= ng) continue;
                g[n] = ng;
                prev[n] = c;
                open.Add((ng + Heur(n), n.x, n.y));
            }
        }
        if (!found) return null;

        var path = new List<Vector2Int>();
        var cur = to;
        int safety = 0;
        while (safety++ < 20000)
        {
            path.Add(cur);
            if (cur == from || !prev.TryGetValue(cur, out cur)) break;
        }
        path.Reverse();
        return path;
    }

    void RebuildRoad()
    {
        if (_roadRoot != null) { Destroy(_roadRoot); _roadRoot = null; }
        if (!worldIntegration || !spineRoad || gridSystem == null || cubePrefab == null || _bedrockTop.Count == 0) return;

        var stops = CollectRoadStops();
        if (stops.Count < 2) return;

        var solid = new Dictionary<int, FaceBatch>();
        var ghost = new FaceBatch { color = roadGhostColor };

        for (int s = 0; s + 1 < stops.Count; s++)
        {
            var a = stops[s]; var b = stops[s + 1];
            if (a.hidden) continue;                       // nothing of it shows
            var path = FindRoad(a.col, b.col);
            if (path == null) continue;
            // A road into the fog goes only a little way in — enough to say it goes on.
            DrawRoad(path, b.hidden ? 6 : int.MaxValue, b.hidden, solid, ghost);
        }

        _roadRoot = NewWorldRoot("WorldRoad").gameObject;
        EmitBatches(_roadRoot.transform, solid, "Road", castShadows: false);

        if (ghost.v.Count > 0)
        {
            var mesh = new Mesh { name = "RoadGhost" };
            mesh.SetVertices(ghost.v);
            mesh.SetNormals(ghost.n);
            mesh.SetTriangles(ghost.t, 0);
            mesh.RecalculateBounds();

            var go = new GameObject("RoadGhost");
            go.transform.SetParent(_roadRoot.transform, true);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial    = RoadGhostMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows    = false;
            go.AddComponent<RoadGhostPulse>().Init(mr, roadGhostColor);
        }
    }

    // One path as a ribbon laid just over the ground: a square on each column, an arm
    // to each neighbour, and a riser where the ground steps. A column with no ground
    // gets a dot of ghost at the height of the last real one.
    void DrawRoad(List<Vector2Int> path, int maxTiles, bool allGhost,
                  Dictionary<int, FaceBatch> solid, FaceBatch ghost)
    {
        int n = Mathf.Min(path.Count, maxTiles);
        if (n <= 0) return;

        float cs   = gridSystem.cellSize;
        float half = CubeSize() * 0.5f;
        float lift = cs * 0.02f;
        float w    = cs * roadWidth * 0.5f;

        var ys   = new float[n];
        var real = new bool[n];
        var skip = new bool[n];   // a level block or the start: they have their own mark
        for (int i = 0; i < n; i++)
        {
            if (Walkable(path[i], out var top) && !allGhost)
            {
                ys[i] = gridSystem.GridToWorld(top).y + half;
                real[i] = true;
                if (_cellToNode.TryGetValue(top, out var nd) && nd != null && (nd.level != null || nd.isStart)) skip[i] = true;
            }
        }

        // Ghosts borrow the height of the nearest real column before them, or after.
        var have = (bool[])real.Clone();
        float carry = 0f; bool ok = false;
        for (int i = 0; i < n; i++)
        {
            if (real[i]) { carry = ys[i]; ok = true; }
            else if (ok) { ys[i] = carry; have[i] = true; }
        }
        ok = false;
        for (int i = n - 1; i >= 0; i--)
        {
            if (real[i]) { carry = ys[i]; ok = true; }
            else if (!have[i] && ok) { ys[i] = carry; have[i] = true; }
        }
        for (int i = 0; i < n; i++)
        {
            if (have[i]) continue;
            // No real ground anywhere on this stretch: ride a little over the rock.
            if (_bedrockTop.TryGetValue(path[i], out int bh))
                ys[i] = gridSystem.GridToWorld(new Vector3Int(path[i].x, bh, path[i].y)).y + half + cs * 0.9f;
        }

        for (int i = 0; i < n; i++)
        {
            var c = path[i];
            var p = gridSystem.GridToWorld(new Vector3Int(c.x, 0, c.y));
            p.y = 0f;                       // only x and z are used: heights are absolute below
            float y = ys[i] + lift;

            if (!real[i])
            {
                float gw = cs * 0.2f;
                float gy = y + cs * 0.04f;
                AddQuad(ghost,
                        new Vector3(p.x - gw, gy, p.z - gw), new Vector3(p.x - gw, gy, p.z + gw),
                        new Vector3(p.x + gw, gy, p.z + gw), new Vector3(p.x + gw, gy, p.z - gw),
                        Vector3.up);
                continue;
            }
            if (skip[i]) continue;

            var batch = BatchFor(solid, RoadColorAt(c));

            // The square under the path.
            AddQuad(batch,
                    new Vector3(p.x - w, y, p.z - w), new Vector3(p.x - w, y, p.z + w),
                    new Vector3(p.x + w, y, p.z + w), new Vector3(p.x + w, y, p.z - w),
                    Vector3.up);

            // Arms to the neighbours on the road, and a riser where the level changes.
            for (int dirI = -1; dirI <= 1; dirI += 2)
            {
                int j = i + dirI;
                if (j < 0 || j >= n) continue;
                var d = path[j] - c;
                if (Mathf.Abs(d.x) + Mathf.Abs(d.y) != 1) continue;

                var axis = new Vector3(d.x, 0f, d.y);
                var perp = new Vector3(-d.y, 0f, d.x);
                Vector3 a0 = p + axis * w, a1 = p + axis * half;

                AddQuad(batch,
                        a0 - perp * w + Vector3.up * y, a0 + perp * w + Vector3.up * y,
                        a1 + perp * w + Vector3.up * y, a1 - perp * w + Vector3.up * y,
                        Vector3.up);

                if (j > i && real[j] && !skip[j] && Mathf.Abs(ys[j] - ys[i]) > 0.01f)
                {
                    float yj = ys[j] + lift;
                    float hi = Mathf.Max(y, yj), lo = Mathf.Min(y, yj);
                    var nrm = y > yj ? axis : -axis;
                    var edge = p + axis * half;
                    AddQuad(batch,
                            new Vector3(edge.x - perp.x * w, hi, edge.z - perp.z * w), new Vector3(edge.x + perp.x * w, hi, edge.z + perp.z * w),
                            new Vector3(edge.x + perp.x * w, lo, edge.z + perp.z * w), new Vector3(edge.x - perp.x * w, lo, edge.z - perp.z * w),
                            nrm);
                }
            }
        }
    }

    static Material RoadGhostMaterial()
    {
        if (_roadGhostMat != null) return _roadGhostMat;
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        _roadGhostMat = new Material(sh) { name = "RoadGhost" };
        if (_roadGhostMat.HasProperty("_Surface"))
        {
            _roadGhostMat.SetFloat("_Surface", 1f);
            _roadGhostMat.SetFloat("_ZWrite", 0f);
            _roadGhostMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _roadGhostMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _roadGhostMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _roadGhostMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        return _roadGhostMat;
    }

    // The dotted road breathes, slowly — an invitation, not a warning.
    class RoadGhostPulse : MonoBehaviour
    {
        Renderer _r;
        Color _c;

        public void Init(Renderer r, Color c) { _r = r; _c = c; }

        void Update()
        {
            if (_r == null) return;
            float a = _c.a * (0.62f + 0.38f * Mathf.Sin(Time.time * 2.4f));
            MpbColor.Set(_r, new Color(_c.r, _c.g, _c.b, a));
        }
    }
}
