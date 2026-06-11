using System.Collections.Generic;
using UnityEngine;

// 启发 (Enlightenment) — star-sphere visualizer.
//
// When an EnlightenmentRule forms its N×N×N cube, this wraps the blocks in a
// glowing sphere of "tech stars": points sampled evenly over a sphere centered
// on the cube, linked into a nearest-neighbour mesh, slowly tumbling around the
// blocks. Reads as an energy/constellation sphere orbiting the cube.
//
// RECONCILER (read before editing) — same contract as HarmonyVineVisualizer:
// SynergyVisualFX tears down + rebuilds EVERY claimed piece's decoration
// whenever a synergy's claim count changes (i.e. on any block add/remove). If
// we spawned in OnPieceClaimed and let the dispatcher Destroy in
// OnPieceReleased, the sphere would restart on every churn. So instead:
//   • WE own each sphere in a per-ActiveSynergy dictionary.
//   • OnPieceClaimed / OnPieceReleased just call Reconcile(), which reads the
//     LIVE claim state and makes the world match.
//   • OnPieceClaimed returns null so the dispatcher never Destroys our views.
//   • A view is only rebuilt when its cube actually changes (tracked by a cell
//     signature), so it doesn't restart its fade/twinkle on unrelated churn.
//
// ActiveSynergy instances are stable across ticks (created once on activation,
// mutated in place on absorb, removed on deactivation), so they make safe
// dictionary keys: a 2³→3³ tier-up keeps the same key but changes the
// signature, which triggers exactly one rebuild into the larger sphere.
[CreateAssetMenu(
    menuName = "GeoWorld/Synergy/Visualizers/Enlightenment Constellation",
    fileName = "EnlightenmentConstellationVisualizer")]
public class EnlightenmentConstellationVisualizer : SynergyVisualizer
{
    [Header("Color")]
    [Tooltip("Tint stars + links with the synergy's theme color. If off, uses Star/Line Color below.")]
    public bool useThemeColor = true;

    [Tooltip("Star color when 'Use Theme Color' is off (alpha reused as star opacity either way).")]
    public Color starColor = new Color(1f, 0.96f, 0.85f, 1f);

    [Tooltip("Link color when 'Use Theme Color' is off (alpha reused as link opacity either way).")]
    public Color lineColor = new Color(0.55f, 0.70f, 1f, 0.5f);

    [Header("Sphere")]
    [Tooltip("Number of stars spread over the sphere around the cube.")]
    [Range(2, 40)] public int maxStars = 32;

    [Tooltip("Sphere radius as a multiple of the cube's circumscribed radius. 1 = touches the cube corners, >1 = floats outside.")]
    public float radiusMul = 1.15f;

    [Tooltip("Lift the sphere center above the cube center, in cell-size units (0 = wraps the cube).")]
    public float centerLift = 0f;

    [Tooltip("Tiny random scatter of each star off the perfect sphere (fraction of the radius).")]
    public float jitterAmount = 0.02f;

    [Tooltip("Star sprite size, in cell-size units.")]
    public float starSize = 0.35f;

    [Header("Links")]
    [Tooltip("How many nearest neighbours each star links to (mesh density of the sphere).")]
    [Range(1, 6)] public int linksPerStar = 3;

    [Header("Brightness")]
    [Tooltip("Star color multiplier. Material is additive, so >1 reads as a bright glow.")]
    [Range(0.5f, 4f)] public float starBrightness = 2f;

    [Tooltip("Link color multiplier (additive). Keep lower than stars so the mesh stays subtle.")]
    [Range(0.1f, 4f)] public float lineBrightness = 1.2f;

    [Header("Twinkle")]
    public float twinkleSpeed = 3f;
    [Range(0f, 1f)] public float twinkleDepth = 0.5f;

    [Header("Motion")]
    [Tooltip("Orbit speed around Y, deg/sec.")]
    public float spinSpeed = 24f;

    [Tooltip("Tumble speed around X, deg/sec (gives the sphere a 3-D roll).")]
    public float tiltSpeed = 6f;

    [Tooltip("Vertical bob amplitude, in cell-size units (0 = none).")]
    public float bobAmplitude = 0f;
    public float bobSpeed = 1f;

    [Tooltip("Seconds for a new sphere to fade in.")]
    public float fadeInDuration = 0.6f;

    // Snapshot of what one synergy's sphere should look like this tick.
    struct Built
    {
        public Vector3[]        stars;
        public List<Vector2Int> edges;
        public Vector3          center;
        public Color            star;
        public Color            line;
        public int              sig;   // cell-set signature; rebuild only when it changes
    }

    // ── Owned lifecycle (NOT serialized; reset per load) ────────────────────
    [System.NonSerialized] Dictionary<ActiveSynergy, ConstellationView> _views;
    [System.NonSerialized] Dictionary<ActiveSynergy, int>               _sig;
    [System.NonSerialized] Dictionary<ActiveSynergy, Built>             _desired;
    [System.NonSerialized] List<ActiveSynergy>                          _prune;

    void OnEnable()
    {
        _views   = new Dictionary<ActiveSynergy, ConstellationView>();
        _sig     = new Dictionary<ActiveSynergy, int>();
        _desired = new Dictionary<ActiveSynergy, Built>();
        _prune   = new List<ActiveSynergy>();
    }

    void OnDisable()
    {
        _views?.Clear();
        _sig?.Clear();
        _desired?.Clear();
        _prune?.Clear();
    }

    public override GameObject OnPieceClaimed(PlacedBlockInstance instance, ActiveSynergy active)
    {
        Reconcile();
        return null;   // WE own the sphere; the dispatcher must not Destroy it.
    }

    public override void OnPieceReleased(PlacedBlockInstance instance, ActiveSynergy active, GameObject spawned)
    {
        // Do NOT call base (it would Destroy `spawned`, but spawned is null).
        Reconcile();
    }

    // ── Core: make the live spheres match the current claim state ────────────
    void Reconcile()
    {
        if (_views == null) OnEnable();   // lazy guard (OnEnable may not have run)

        var evaluator = SynergyEvaluator.Instance;
        var grid      = GridSystem.instance;
        if (evaluator == null || grid == null) return;

        // 1) Desired this tick: one Built per active synergy that uses THIS
        //    visualizer and currently highlights at least one cell.
        _desired.Clear();
        var actives = evaluator.Actives;
        for (int i = 0; i < actives.Count; i++)
        {
            var a = actives[i];
            if (a?.rule == null || a.rule.visualizer != this || a.claimedPieces == null) continue;
            if (TryBuild(a, grid, out var built))
                _desired[a] = built;
        }

        // 2) Prune: views whose synergy is gone / no longer desired / destroyed.
        _prune.Clear();
        foreach (var kv in _views)
            if (kv.Value == null || !_desired.ContainsKey(kv.Key)) _prune.Add(kv.Key);
        for (int i = 0; i < _prune.Count; i++)
        {
            var key = _prune[i];
            if (_views.TryGetValue(key, out var v) && v != null) Destroy(v.gameObject);
            _views.Remove(key);
            _sig.Remove(key);
        }

        // 3) Spawn / refresh: keep an existing view when its cube signature is
        //    unchanged (don't restart its fade/twinkle on unrelated churn);
        //    otherwise build fresh (new synergy, or cube grew / moved).
        foreach (var kv in _desired)
        {
            var a = kv.Key;
            var b = kv.Value;

            bool haveView = _views.TryGetValue(a, out var view) && view != null;
            bool sameSig  = haveView && _sig.TryGetValue(a, out var s) && s == b.sig;
            if (haveView && sameSig) continue;

            if (haveView) Destroy(view.gameObject);

            var go = new GameObject("EnlightenmentStarSphere");
            view = go.AddComponent<ConstellationView>();
            view.twinkleSpeed   = twinkleSpeed;
            view.twinkleDepth   = twinkleDepth;
            view.spinSpeed      = spinSpeed;
            view.tiltSpeed      = tiltSpeed;
            view.bobAmplitude   = bobAmplitude * grid.cellSize;
            view.bobSpeed       = bobSpeed;
            view.fadeInDuration = fadeInDuration;
            view.Build(b.center, b.stars, b.edges, b.star, b.line, starSize * grid.cellSize);

            _views[a] = view;
            _sig[a]   = b.sig;
        }
    }

    // Wrap the cube in a sphere of stars: find the in-cube cells → their world
    // bounding box → a circumscribed sphere → Fibonacci-sample stars on it →
    // link nearest neighbours. Returns false if there's nothing to draw.
    bool TryBuild(ActiveSynergy a, GridSystem grid, out Built built)
    {
        built = default;

        var   filter = a.rule as ICellHighlightFilter;
        float cs     = grid.cellSize;

        // Unique in-cube cells (overhang cells, which the filter rejects, are
        // skipped so the sphere wraps only the cube itself).
        var cells = new List<Vector3Int>();
        var seen  = new HashSet<Vector3Int>();
        foreach (var p in a.claimedPieces)
        {
            if (p?.cells == null) continue;
            for (int i = 0; i < p.cells.Length; i++)
            {
                var c = p.cells[i];
                if (filter != null && !filter.ShouldHighlight(c)) continue;
                if (seen.Add(c)) cells.Add(c);
            }
        }
        if (cells.Count == 0) return false;

        // Cube center + size from the cell bounding box; signature from the set.
        Vector3Int mn = cells[0], mx = cells[0];
        int sig = 17;
        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            mn = Vector3Int.Min(mn, c);
            mx = Vector3Int.Max(mx, c);
            sig = unchecked(sig * 31 + CellHash(c));
        }

        Vector3 center = (grid.GridToWorld(mn) + grid.GridToWorld(mx)) * 0.5f
                       + Vector3.up * (centerLift * cs);
        int   sideCells = Mathf.Max(mx.x - mn.x, Mathf.Max(mx.y - mn.y, mx.z - mn.z)) + 1;
        float worldSide = sideCells * cs;
        // Circumscribed-sphere radius of a cube of side S is S·√3/2.
        float radius    = worldSide * 0.5f * 1.7320508f * Mathf.Max(0.01f, radiusMul);

        // Even points on the sphere (golden-angle Fibonacci spiral) + jitter.
        int n = Mathf.Clamp(maxStars, 2, 40);
        var stars = new Vector3[n];
        const float GA = 2.399963229728653f;
        for (int i = 0; i < n; i++)
        {
            float t   = (i + 0.5f) / n;
            float y   = 1f - 2f * t;
            float r   = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float phi = GA * i;

            Vector3 dir = new Vector3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r);
            dir += JitterFor(i * 92821 + 7) * jitterAmount;
            dir.Normalize();

            stars[i] = center + dir * radius;
        }

        var edges = BuildSphereLinks(stars, linksPerStar);

        Color theme   = BlockColorPalette.Get(a.rule.color);
        Color starCol = useThemeColor ? Color.Lerp(theme, Color.white, 0.5f) : starColor;
        starCol *= Mathf.Max(0.01f, starBrightness);       // additive glow (rgb may exceed 1)
        starCol.a = useThemeColor ? 1f : starColor.a;
        Color lineCol = useThemeColor ? theme : lineColor;
        lineCol *= Mathf.Max(0.01f, lineBrightness);
        lineCol.a = lineColor.a;

        built = new Built
        {
            stars  = stars,
            edges  = edges,
            center = center,
            star   = starCol,
            line   = lineCol,
            sig    = sig,
        };
        return true;
    }

    // ── Links: connect each star to its `k` nearest neighbours (deduped), so
    //    the points read as a faceted mesh sphere rather than random lines.
    static List<Vector2Int> BuildSphereLinks(Vector3[] pts, int k)
    {
        var edges = new List<Vector2Int>();
        int n = pts.Length;
        if (n < 2) return edges;
        k = Mathf.Clamp(k, 1, n - 1);

        var have  = new HashSet<long>();
        var order = new int[n];
        var dist  = new float[n];

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++) { order[j] = j; dist[j] = (pts[j] - pts[i]).sqrMagnitude; }
            System.Array.Sort(order, (x, y) => dist[x].CompareTo(dist[y]));

            int added = 0;
            for (int m = 0; m < n && added < k; m++)
            {
                int j = order[m];
                if (j == i) continue;            // skip self (distance 0)
                if (have.Add(Key(i, j))) edges.Add(new Vector2Int(i, j));
                added++;
            }
        }
        return edges;
    }

    static long Key(int a, int b)
    {
        if (a > b) { int t = a; a = b; b = t; }
        return ((long)a << 32) | (uint)b;
    }

    // ── Deterministic hashing for stable scatter + signature ────────────────
    static int CellHash(Vector3Int c)
    {
        unchecked { return c.x * 73856093 ^ c.y * 19349663 ^ c.z * 83492791; }
    }

    static float Hash01(int h)
    {
        unchecked
        {
            h = (h ^ 61) ^ (h >> 16);
            h += h << 3;
            h ^= h >> 4;
            h *= 0x27d4eb2d;
            h ^= h >> 15;
        }
        return (h & 0x7fffffff) / (float)0x7fffffff;
    }

    static Vector3 JitterFor(int h)
    {
        float jx = Hash01(h)             * 2f - 1f;
        float jy = Hash01(h * 7   + 13)  * 2f - 1f;
        float jz = Hash01(h * 131 + 977) * 2f - 1f;
        return new Vector3(jx, jy, jz);
    }
}
