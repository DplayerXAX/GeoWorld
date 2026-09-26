using System.Collections.Generic;
using UnityEngine;

// Grows lit, shadow-casting vines out of the blocks in a Harmony synergy, as an
// in-world cue (replacing the per-block material swap). How much grows is a
// function of the cluster: see "How much grows" below.
//
// WHY THIS IS A RECONCILER (read before editing):
// SynergyVisualFX tears down + rebuilds the decoration of EVERY piece in a
// synergy whenever its claim count changes (i.e. whenever you place/remove any
// Harmony block). If we spawned a vine in OnPieceClaimed and let the dispatcher
// Destroy it in OnPieceReleased, every vine would restart growing on every
// add/remove. So instead:
//   * We OWN the vine lifecycle in a pieceId -> vine dictionary.
//   * OnPieceClaimed / OnPieceReleased just trigger Reconcile(), which reads the
//     LIVE claim state from SynergyEvaluator and makes the world match: spawn
//     missing vines, KEEP existing ones untouched, retire vines whose piece left
//     the claim. Reconcile is idempotent, so the dispatcher's churn is harmless.
//   * OnPieceClaimed returns null so the dispatcher never Destroys our vines.
// Vines are parented to the block's visualObject, so a destroyed block takes its
// vine with it automatically (Reconcile then prunes the dead dictionary entry).
[CreateAssetMenu(
    menuName = "GeoWorld/Synergy/Visualizers/Harmony Vine",
    fileName = "HarmonyVineVisualizer")]
public class HarmonyVineVisualizer : SynergyVisualizer
{
    [Tooltip("Vine prefab. Needs a VineEffect; it builds its own tube mesh and renderer, " +
             "so any LineRenderer left on the prefab is disabled at runtime.")]
    public GameObject vinePrefab;

    [Header("How much grows")]
    // A flat cap meant a two-block Harmony and a twenty-block Harmony grew the same
    // four vines, so the reward for building the synergy out was invisible. The
    // canopy is now a function of the cluster: more blocks, more vines, and each
    // vine lusher than the last.
    [Tooltip("Vines per claimed piece. 1 = every claimed block sprouts one.")]
    [Range(0.1f, 2f)] public float vinesPerPiece = 0.7f;

    [Tooltip("Floor, so even a minimal Harmony shows something.")]
    [Range(1, 8)] public int minVines = 2;

    [Tooltip("Ceiling, so a board-wide Harmony doesn't bury the board. Vines are sticky: once grown they stay until their block leaves the claim.")]
    [Range(2, 64)] public int vineCap = 28;

    [Tooltip("Cluster size at which the canopy is at its lushest — below this, forking and foliage scale up with the cluster.")]
    [Range(2, 40)] public int lushAtPieces = 16;

    [Tooltip("How much denser a vine gets between the smallest cluster and `Lush At Pieces`. 1 = no change, 2 = twice the forks and leaves.")]
    [Range(1f, 3f)] public float lushGain = 1.9f;

    [Tooltip("Fraction of vines that grow ALONG the cluster instead of away from it, so branches from neighbouring blocks cross and interlace rather than all radiating outward.")]
    [Range(0f, 1f)] public float weaveChance = 0.45f;

    // Runners are what tie the whole cluster into one plant. Without them each block
    // grows its own separate bush and the group reads as a row of shrubs; with them
    // the canopy arches from block to block and reads as one thing that has grown
    // over the structure.
    [Tooltip("Fraction of vines that arch across and LAND on another claimed block instead of ending in mid-air.")]
    [Range(0f, 1f)] public float bridgeChance = 0.4f;

    [Tooltip("Furthest a runner will reach, in cells. Beyond this the arch is so long it stops reading as a plant.")]
    [Range(1.5f, 8f)] public float bridgeReach = 4.5f;

    [Header("Creeper")]
    // The main look, and the one thing a free-growing branch can never produce:
    // a stem that CLINGS. It runs across a block's top face, turns down over the
    // rim, slides down the side, and picks up the next block's top — following the
    // cluster's actual geometry, because the route is computed FROM that geometry
    // rather than aimed at it. Only the ends of a run leave the surface.
    [Tooltip("Fraction of vines that crawl the cluster's surfaces instead of growing free.")]
    [Range(0f, 1f)] public float creeperChance = 0.8f;

    [Tooltip("How many cells one run crosses before it ends and throws its spray.")]
    [Range(2, 24)] public int creeperLength = 9;

    [Tooltip("How far the run wanders off the centre line of each face, in cells. 0 = a ruler-straight stripe down the middle.")]
    [Range(0f, 0.35f)] public float creeperMeander = 0.17f;

    [Tooltip("How far off the face the stem sits, in cells. Just enough to clear it — a creeper that floats stops being a creeper.")]
    [Range(0.01f, 0.2f)] public float creeperGap = 0.06f;

    [Tooltip("Side runs that leave the main one partway along. This is what makes a creeper SPREAD instead of being one cable laid across the board.")]
    [Range(0, 5)] public int creeperBranches = 2;

    [Tooltip("Length of a side run, in cells.")]
    [Range(2, 14)] public int creeperBranchLength = 5;

    // Real climbing plants run along EDGES, not down the middle of faces. An edge
    // gives a stem two surfaces to grip and a corner to turn on, and visually it is
    // where the silhouette is — a vine down the centre of a top face is hidden by
    // the block it is on from every angle except straight down.
    [Tooltip("How far toward an exposed edge the stem is pulled, in cells. 0.5 sits exactly on the rim; 0 runs down the middle of the face.")]
    [Range(0f, 0.48f)] public float creeperEdgeBias = 0.38f;

    [Tooltip("How far the surface normal tilts outward at a rim, 0 = straight up. At an edge the real surface normal is between the top face and the side, and this is what makes leaves drape OVER the edge and branches leave SIDEWAYS instead of all shooting up.")]
    [Range(0f, 1f)] public float creeperEdgeNormal = 0.65f;

    [Tooltip("How strongly the route prefers cells on the cluster's border.")]
    [Range(0, 60)] public int creeperEdgePull = 26;

    [Header("Rim trees — the wood the enemies walk through")]
    // Enemies walk ACROSS the tops of blocks. So the way to make a Harmony read as
    // a wood is not to decorate the blocks — it is to stand trees along the edges
    // of the path, so that crossing the cluster means walking between trunks under
    // a canopy. An avenue, not a hedge.
    //
    // WHERE the trees stand comes from the outline of the WHOLE cluster's walkable
    // top, never from each block's own four edges. Per-block edges would plant a
    // tree between every pair of neighbouring cells, and an I4 would come out as a
    // row of posts straight down the middle of the road. The cluster outline is
    // what makes this work for every shape: an I4 becomes an avenue down its
    // length, an O2x2 a ring round a clearing, an L or a T trees that follow the
    // corner — the shape is read, not assumed.
    public bool rimTrees = true;

    // CORNERS FIRST. The long sides of the cluster are the road — that is where
    // the enemies are, and a tree there stands between the player and the thing
    // they are trying to watch. A convex corner is the one spot on the rim that is
    // off every route across the top, so that is where the wood grows; the long
    // edges only get the occasional tree.
    [Tooltip("Fraction of the cluster's convex CORNERS that get a tree. The main planting.")]
    [Range(0f, 1f)] public float cornerDensity = 0.85f;

    [Tooltip("Fraction of plain (non-corner) open edges that get a tree. Low on purpose: these edges run alongside the enemies' route.")]
    [Range(0f, 1f)] public float treeDensity = 0.12f;

    [Tooltip("How far from the cell centre toward the rim a tree stands, in cells. 0.5 is the very edge; lower leaves less road between the two sides.")]
    [Range(0.1f, 0.48f)] public float treeInset = 0.38f;

    [Tooltip("How far a trunk leans OUT over its edge. A little keeps the trunks clear of the road and lets the crowns open over the drop; too much and it stops being a tree beside a path.")]
    [Range(0f, 1f)] public float treeLean = 0.38f;

    [Tooltip("Sideways jitter along the edge, in cells, so a straight rim does not plant a picket fence.")]
    [Range(0f, 0.3f)] public float treeJitter = 0.18f;

    [Tooltip("Ceiling per cluster.")]
    [Range(1, 120)] public int treeCap = 48;

    [Header("Color")]
    [Tooltip("Use the synergy's theme color instead of branchColor below.")]
    public bool useThemeColor = false;

    // Pale BLEACHED wood, not dark bark.
    //
    // A saturated dark brown has nowhere to go on its unlit side — it lands in the
    // near-black that made the old branches read as burnt wire. This sits in the
    // same warm desaturated family DepthFog already declares for this world
    // (_FogColor 0.70, 0.63, 0.56), so the branches read as sun-bleached wood, keep
    // their hue in shadow, and stay inside the game's existing palette instead of
    // introducing a new dark one.
    [Tooltip("Branch / body color used when 'Use Theme Color' is off. Pale, warm, desaturated — dark browns go black on their shaded side.")]
    public Color branchColor = new Color(0.74f, 0.67f, 0.57f, 1f);

    [Tooltip("New-growth color shown near each branch tip (green bud). Shown regardless of 'Use Theme Color'.")]
    public Color tipColor = new Color(0.45f, 0.78f, 0.32f, 1f);

    [Header("Block outline")]
    [Tooltip("Also recolor each claimed block's existing inverse-hull outline (GeoWorld/BlockOutline) while it's in the synergy, as a subtle rim cue. Restored on release.")]
    public bool highlightClaimedOutline = true;

    [Tooltip("Outline rim color used when 'Use Theme Color' is off.")]
    public Color outlineColor = new Color(0.35f, 0.62f, 0.30f, 1f);

    [Header("Length (outward mode)")]
    [Tooltip("Extra trunk segments added on top of the prefab's, for longer branches. 0 = use the prefab as-is.")]
    public int extraSegments = 0;

    [Tooltip("Multiplies the prefab's per-segment length (>1 = longer branches). 1 = unchanged.")]
    public float lengthScale = 1f;

    [Header("Lushness")]
    [Tooltip("Leaves per vine node. 0 = keep the prefab's own value. Higher = denser canopy.")]
    [Range(0, 8)] public int leafDensity = 5;

    [Tooltip("Multiplies the prefab's leaf size range (>1 = bigger foliage).")]
    public float leafSizeMul = 1.2f;

    [Tooltip("Sprout outward vines from the block's top EDGE (on the side facing away from the cluster) instead of the top-face center, and keep coils below the top rim — so foliage frames the block without covering the walkable top face.")]
    public bool keepOffTopFaces = true;

    [Header("Branch style mix")]
    [Tooltip("Fraction of vines that COIL up around their block (helix) instead of branching outward. 0 = all outward, 1 = all coil, ~0.4 = a mix. Hashed per block so each block keeps its style across reconciles.")]
    [Range(0f, 1f)] public float coilChance = 0.4f;

    [Tooltip("Full helix turns from base to top when a vine coils.")]
    public float coilTurns = 1.6f;

    [Tooltip("Scale of the hug box vs the block footprint (1 = exactly on the faces). Keep ~1 so the coil stays glued to the block.")]
    public float coilRadiusMul = 1f;

    [Tooltip("How far outside the faces the coil sits, in cell-size units. Small = hugs tightly.")]
    public float coilSurfaceGap = 0.05f;

    [Tooltip("Coil climb height as a multiple of cell size (≥1 climbs over the top).")]
    public float coilHeightMul = 1.5f;

    // What a chosen piece should grow this tick.
    private struct VineTarget
    {
        public Color   rootColor;  // branch / body color (theme color or branchColor)
        public Color   tipColor;   // new-growth bud color near the tip
        public Vector3 center;     // world center of the cluster (for outward lean)
        public float   lush;       // 0..1, how far this cluster is toward `lushAtPieces`
        public bool    interior;   // not on the cluster border — climbs its block instead

        // The cluster this piece belongs to. A creeper's route is a property of the
        // whole cluster's shape, not of one block, so the route builder needs it.
        // Only ever read inside the Reconcile that wrote it; _desired is cleared at
        // the top of the next one.
        public ActiveSynergy active;
    }

    // ── Runtime state. NOT serialized, and reset on load, so it never leaks
    //    across editor Play sessions or holds stale ActiveSynergy references.
    [System.NonSerialized] private Dictionary<int, GameObject> _vines;    // pieceId -> live vine
    [System.NonSerialized] private Dictionary<int, VineTarget> _desired;  // pieceId -> what to grow (this tick)
    [System.NonSerialized] private List<int>                   _prune;    // scratch
    [System.NonSerialized] private HashSet<int>                _outlined;     // pieceId -> outline currently recolored
    [System.NonSerialized] private Dictionary<int, Color>      _outlineWant;  // pieceId -> outline rim color (this tick)
    [System.NonSerialized] private Dictionary<int, Vector3>    _bridgeTo;     // pieceId -> world point its runner lands on

    // Rim trees, keyed by EDGE (cell + side) rather than by piece. A tree belongs to
    // a stretch of rim, not to a block: when a neighbouring block is placed and that
    // edge becomes interior, exactly that tree withers, and every other tree keeps
    // growing untouched — the same sticky reconcile the vines use, one level finer.
    [System.NonSerialized] private Dictionary<long, GameObject> _trees;
    [System.NonSerialized] private Dictionary<long, TreeSite>   _treeWant;
    [System.NonSerialized] private List<long>                   _treePrune;

    private struct TreeSite
    {
        public Vector3Int cell;
        public Vector3    pos;
        public Vector3    up;
        public Color      root, tip;
        public float      lush;
    }

    private void OnEnable()
    {
        _vines       = new Dictionary<int, GameObject>();
        _desired     = new Dictionary<int, VineTarget>();
        _prune       = new List<int>();
        _outlined    = new HashSet<int>();
        _outlineWant = new Dictionary<int, Color>();
        _bridgeTo    = new Dictionary<int, Vector3>();
        _trees       = new Dictionary<long, GameObject>();
        _treeWant    = new Dictionary<long, TreeSite>();
        _treePrune   = new List<long>();
    }

    private void OnDisable()
    {
        // Play stopped / asset unloaded: drop references (Unity is tearing the
        // scene objects down anyway).
        _vines?.Clear();
        _desired?.Clear();
        _prune?.Clear();
        _outlined?.Clear();
        _outlineWant?.Clear();
        _bridgeTo?.Clear();
        _trees?.Clear();
        _treeWant?.Clear();
        _treePrune?.Clear();
    }

    public override GameObject OnPieceClaimed(PlacedBlockInstance instance, ActiveSynergy active)
    {
        Reconcile();
        // Return null on purpose: WE own the vine lifecycle. If we returned the
        // vine, the dispatcher would Destroy it on the next claim-count change,
        // restarting every vine's growth.
        return null;
    }

    public override void OnPieceReleased(PlacedBlockInstance instance, ActiveSynergy active, GameObject spawned)
    {
        // Do NOT call base (it would Destroy `spawned`, but spawned is null).
        // Just reconcile against the live truth: a piece that truly left the
        // claim is no longer in any active, so its vine gets pruned here. During
        // dispatcher churn the piece is still claimed, so its vine is preserved.
        Reconcile();
    }

    // ── Core: make the world's vines match the live claim state ──────────────
    private void Reconcile()
    {
        if (_vines == null) OnEnable();      // lazy guard (OnEnable may not have run)

        var evaluator = SynergyEvaluator.Instance;
        var grid      = GridSystem.instance;
        if (evaluator == null || grid == null) return;

        // 1) Desired this tick: which pieces should grow a vine (selected border
        //    pieces) AND which claimed blocks should wear an outline rim (all of
        //    them), with the colors to use — across every active using THIS
        //    visualizer.
        _desired.Clear();
        _outlineWant.Clear();
        _bridgeTo.Clear();
        var actives = evaluator.Actives;
        for (int i = 0; i < actives.Count; i++)
        {
            var a = actives[i];
            if (a?.rule == null || a.rule.visualizer != this || a.claimedPieces == null) continue;

            Color root = useThemeColor ? BlockColorPalette.Get(a.rule.color) : branchColor;
            SelectTargets(a, grid, root, tipColor);

            if (highlightClaimedOutline)
            {
                Color rim = useThemeColor ? BlockColorPalette.Get(a.rule.color) : outlineColor;
                foreach (var p in a.claimedPieces)
                    if (p != null) _outlineWant[p.id] = rim;
            }
        }

        // 2) Prune: vines whose block was destroyed (null) or whose piece is no
        //    longer wanted (left the claim / cluster shrank).
        _prune.Clear();
        foreach (var kv in _vines)
        {
            if (kv.Value == null || !_desired.ContainsKey(kv.Key))
                _prune.Add(kv.Key);
        }
        for (int i = 0; i < _prune.Count; i++)
        {
            int id = _prune[i];
            var go = _vines[id];
            _vines.Remove(id);
            RetireVine(go);
        }

        // 3) Spawn: wanted ids that don't have a live vine yet. Existing vines
        //    are left untouched (they keep growing — no restart).
        if (vinePrefab != null)
        {
            foreach (var kv in _desired)
            {
                if (_vines.TryGetValue(kv.Key, out var go) && go != null) continue;
                SpawnVine(kv.Key, kv.Value, evaluator, grid);
            }
        }

        // 4) Outline rims: apply to newly-claimed blocks, restore departed ones.
        //    Independent of the vine prefab, so it works even with vines off.
        ReconcileOutlines(evaluator, grid);

        // 5) Rim trees.
        if (rimTrees) ReconcileTrees(evaluator, grid);
    }

    // Sticky + deterministic target selection for ONE cluster:
    //   * Keep every already-vined piece that's still claimed, so a grown vine
    //     never vanishes just because the cluster's shape changed.
    //   * Fill the remaining slots (up to the cluster-scaled count) from border
    //     pieces first, then interior ones, lowest
    //     pieceId first — deterministic, so selection doesn't jitter under churn.
    private void SelectTargets(ActiveSynergy a, GridSystem grid, Color rootColor, Color tipColor)
    {
        Vector3 center = ComputeClusterCenter(a, grid);

        int claimed = 0;
        foreach (var p in a.claimedPieces) if (p != null) claimed++;
        if (claimed == 0) return;

        int   want = Mathf.Clamp(Mathf.CeilToInt(claimed * vinesPerPiece), minVines, vineCap);
        float lush = Mathf.InverseLerp(2f, Mathf.Max(3, lushAtPieces), claimed);

        var target = new VineTarget
        {
            rootColor = rootColor, tipColor = tipColor, center = center, lush = lush,
            active = a,
        };

        int count = 0;

        // pass 1 — sticky: existing vines that are still claimed. Their `interior`
        // flag is re-read below, but the vine itself is never respawned, so a vine
        // that started as a coil stays a coil even if the cluster grows around it.
        foreach (var p in a.claimedPieces)
        {
            if (p == null) continue;
            if (_vines.TryGetValue(p.id, out var go) && go != null)
            {
                _desired[p.id] = target;
                count++;
            }
        }

        // pass 2 — border pieces first: their branches have open space to reach into.
        List<PlacedPiece> border = GetBorderPieces(a.claimedPieces);
        border.Sort((x, y) => x.id.CompareTo(y.id));
        for (int i = 0; i < border.Count && count < want; i++)
        {
            int id = border[i].id;
            if (_desired.ContainsKey(id)) continue;
            _desired[id] = target;
            count++;
        }

        // pass 3 — interior pieces, once the border is used up. A big cluster has far
        // more inside than edge, and this is what stops a large Harmony from being a
        // bare slab with a fringe: the middle blocks get overgrown too, climbing
        // their own faces rather than branching into ground that is already occupied.
        if (count < want)
        {
            var interior = new List<PlacedPiece>();
            var onBorder = new HashSet<int>();
            for (int i = 0; i < border.Count; i++) onBorder.Add(border[i].id);
            foreach (var p in a.claimedPieces)
                if (p != null && !onBorder.Contains(p.id)) interior.Add(p);
            interior.Sort((x, y) => x.id.CompareTo(y.id));

            var inner = target;
            inner.interior = true;
            for (int i = 0; i < interior.Count && count < want; i++)
            {
                int id = interior[i].id;
                if (_desired.ContainsKey(id)) continue;
                _desired[id] = inner;
                count++;
            }
        }

        PickBridges(a, grid);
    }

    // Give some of this cluster's vines another block to land on.
    //
    // Picks the FARTHEST block still within reach rather than the nearest. The
    // nearest is almost always the one touching it, and a runner spanning one cell
    // is a bump, not an arch — the span is what makes it read as something that grew
    // across the structure. Deterministic (distance, then lowest id) so the runner
    // network is identical on every one of the dispatcher's constant reconciles.
    private void PickBridges(ActiveSynergy a, GridSystem grid)
    {
        if (bridgeChance <= 0f) return;

        float maxD = bridgeReach * grid.cellSize;
        float minD = 1.5f * grid.cellSize;

        foreach (var p in a.claimedPieces)
        {
            if (p == null || !_desired.ContainsKey(p.id) || _bridgeTo.ContainsKey(p.id)) continue;
            if (Hash01(p.id * 2477 + 19) >= bridgeChance) continue;

            Vector3 from = PieceCentre(p, grid);
            PlacedPiece best = null;
            float bestD = -1f;

            foreach (var q in a.claimedPieces)
            {
                if (q == null || q.id == p.id) continue;
                float d = Vector3.Distance(from, PieceCentre(q, grid));
                if (d < minD || d > maxD) continue;
                if (d > bestD + 1e-4f || (Mathf.Abs(d - bestD) <= 1e-4f && q.id < best.id))
                { bestD = d; best = q; }
            }

            if (best != null)
                _bridgeTo[p.id] = PieceCentre(best, grid) + Vector3.up * (grid.cellSize * 0.5f);
        }
    }

    // ── Creeper routing ──────────────────────────────────────────────────────

    // Build a NETWORK over the cluster's exposed top cells: one main run plus a few
    // side runs that leave it partway along, each turned into a polyline that hugs
    // the faces, with the surface normal at every point.
    //
    // A network rather than a line because a single run across a cluster reads as a
    // cable someone laid there. Growth divides; that is most of what separates a
    // plant from a wire, and it is also what ties the whole cluster together into
    // one thing instead of a row of separate stems.
    //
    // Exposed cells only — a cell with something stacked on it has no top to crawl
    // along, and routing over one would bury the stem inside the block above.
    private List<Vector3> BuildRoute(ActiveSynergy a, GridSystem grid, PlacedPiece start,
                                     uint seed, List<Vector3> normals,
                                     List<VineEffect.CreeperBranch> branches)
    {
        var pts = new List<Vector3>();
        normals.Clear();
        branches?.Clear();

        var all = new HashSet<Vector3Int>();
        foreach (var p in a.claimedPieces)
        {
            if (p?.cells == null) continue;
            foreach (var c in p.cells) all.Add(c);
        }
        if (all.Count == 0) return pts;

        var surface = new HashSet<Vector3Int>();
        foreach (var c in all) if (!all.Contains(c + Vector3Int.up)) surface.Add(c);

        // Deterministic start: the lowest-keyed exposed cell of this piece. The
        // dispatcher reconciles constantly, so anything decided by Random here would
        // hand back a different route every time a block is placed anywhere.
        bool found = false;
        Vector3Int from = default;
        if (start?.cells != null)
            foreach (var c in start.cells)
                if (surface.Contains(c) && (!found || CellKey(c) < CellKey(from))) { from = c; found = true; }
        if (!found) return pts;

        var visited = new HashSet<Vector3Int>();
        var main = Walk(surface, all, visited, from, Mathf.Max(2, creeperLength), ref seed);
        if (main.Count < 2) return pts;

        // Main polyline, remembering where each cell's top landed so a side run can
        // be told which point of the parent it leaves from.
        var cellAt = new List<int>();
        Polyline(main, all, grid, pts, normals, cellAt);

        if (branches == null || creeperBranches <= 0) return pts;

        // Side runs leave from cells along the main one that still have somewhere to
        // go. They share `visited`, so branches never double back over the main run
        // or over each other — overlapping stems read as one thick smear rather than
        // as separate growth.
        int made = 0;
        for (int i = 1; i < main.Count - 1 && made < creeperBranches; i++)
        {
            if (Rand(ref seed) % 100 < 45) continue;
            if (!HasExit(surface, visited, main[i])) continue;

            var run = Walk(surface, all, visited, main[i], Mathf.Max(2, creeperBranchLength), ref seed);
            if (run.Count < 2) continue;

            var bp = new List<Vector3>();
            var bn = new List<Vector3>();
            Polyline(run, all, grid, bp, bn, null);
            if (bp.Count < 3) continue;

            branches.Add(new VineEffect.CreeperBranch { at = cellAt[i], points = bp, normals = bn });
            made++;
        }

        return pts;
    }

    // One run over the surface.
    //
    // Straight ahead is scored far above turning. A walk that picks uniformly from
    // its neighbours zigzags cell by cell and reads as scribble; a real runner
    // commits to a heading and turns only when it has to, which is what lets a long
    // diagonal read as a single continuous stem.
    private List<Vector3Int> Walk(HashSet<Vector3Int> surface, HashSet<Vector3Int> all,
                                  HashSet<Vector3Int> visited,
                                  Vector3Int from, int steps, ref uint seed)
    {
        var route = new List<Vector3Int> { from };
        visited.Add(from);

        var cur = from;
        var dir = Vector3Int.zero;

        for (int step = 0; step < steps; step++)
        {
            Vector3Int best = default;
            int bestScore = int.MinValue;
            bool any = false;

            for (int i = 0; i < _dirs.Length; i++)
            {
                var d = _dirs[i];
                for (int dy = 2; dy >= -2; dy--)
                {
                    var n = cur + d + Vector3Int.up * dy;
                    if (!surface.Contains(n) || visited.Contains(n)) continue;

                    int score = (d == dir ? 40 : 0)               // commit to a heading
                              + (dy == 0 ? 16 : 0)                // prefer staying level
                              - Mathf.Abs(dy) * 5                 // big drops are rare
                              + (Exposed(all, n) ? creeperEdgePull : 0)   // hug the rim
                              + (int)(Rand(ref seed) % 14);
                    if (score > bestScore) { bestScore = score; best = n; any = true; }
                }
            }

            if (!any) break;
            dir = new Vector3Int(best.x - cur.x, 0, best.z - cur.z);
            cur = best;
            route.Add(cur);
            visited.Add(cur);
        }
        return route;
    }

    // Does this cell touch open space in the ground plane? Cells that do are the
    // cluster's rim, and the rim is where a climbing stem belongs.
    private static bool Exposed(HashSet<Vector3Int> all, Vector3Int c)
    {
        for (int i = 0; i < _dirs.Length; i++) if (!all.Contains(c + _dirs[i])) return true;
        return false;
    }

    // Which way the open space is, from a cell on the rim. A corner cell has two
    // open sides, so the push comes out diagonal and the stem rounds the corner
    // instead of cutting across it.
    private static Vector3 EdgePush(HashSet<Vector3Int> all, Vector3Int c)
    {
        Vector3 push = Vector3.zero;
        for (int i = 0; i < _dirs.Length; i++)
            if (!all.Contains(c + _dirs[i])) push += new Vector3(_dirs[i].x, 0f, _dirs[i].z);
        return push.sqrMagnitude < 1e-4f ? Vector3.zero : push.normalized;
    }

    private bool HasExit(HashSet<Vector3Int> surface, HashSet<Vector3Int> visited, Vector3Int c)
    {
        for (int i = 0; i < _dirs.Length; i++)
            for (int dy = 2; dy >= -2; dy--)
            {
                var n = c + _dirs[i] + Vector3Int.up * dy;
                if (surface.Contains(n) && !visited.Contains(n)) return true;
            }
        return false;
    }

    // Turn a run of cells into a polyline that clings, plus a normal per point.
    // `cellAt`, when given, receives the index in `pts` of each cell's top point.
    private void Polyline(List<Vector3Int> route, HashSet<Vector3Int> all, GridSystem grid,
                          List<Vector3> pts, List<Vector3> normals, List<int> cellAt)
    {
        float cell = grid.cellSize;
        float gap  = cell * creeperGap;

        AddPoint(pts, normals, RimOf(route[0], all, grid, gap), RimNormal(route[0], all));
        cellAt?.Add(pts.Count - 1);

        for (int i = 1; i < route.Count; i++)
        {
            Vector3Int c0 = route[i - 1], c1 = route[i];
            Vector3 d = new Vector3(c1.x - c0.x, 0f, c1.z - c0.z).normalized;
            Vector3 side = Vector3.Cross(d, Vector3.up);
            int dy = c1.y - c0.y;

            // Across c0's top to the rim, wandering off the centre line. The
            // meander is a sine over the crossing, so it is zero at both the centre
            // and the rim: the stem still arrives square to the edge it turns on.
            Vector3 fromP = RimOf(c0, all, grid, gap);
            Vector3 n0    = RimNormal(c0, all);
            Vector3 rim   = TopOf(c0, grid, gap) + d * (cell * 0.5f);
            for (int k = 1; k <= 3; k++)
            {
                float t = k / 3f;
                AddPoint(pts, normals,
                         Vector3.Lerp(fromP, rim, t) + side * (Mathf.Sin(t * Mathf.PI) * cell * creeperMeander),
                         Vector3.Slerp(n0, Vector3.up, t));
            }

            // Over the rim and down (or up) the wall. Stepping DOWN, the wall is
            // c0's own face and its outward normal is +d; stepping UP, the wall is
            // the face of c1 that looks back at us, so the normal is -d.
            if (dy != 0)
            {
                Vector3 wallN = dy > 0 ? -d : d;
                Vector3 top = rim + wallN * gap;
                Vector3 bot = top + Vector3.up * (dy * cell);
                int vs = Mathf.Max(2, Mathf.Abs(dy) * 3);
                for (int k = 1; k <= vs; k++)
                    AddPoint(pts, normals, Vector3.Lerp(top, bot, k / (float)vs), wallN);
            }

            AddPoint(pts, normals, RimOf(c1, all, grid, gap), RimNormal(c1, all));
            cellAt?.Add(pts.Count - 1);
        }
    }

    private static Vector3 TopOf(Vector3Int c, GridSystem grid, float gap)
        => grid.GridToWorld(c) + Vector3.up * (grid.cellSize * 0.5f + gap);

    // The top point, pulled out toward whatever edge this cell has. On an interior
    // cell there is no edge and it stays in the middle.
    private Vector3 RimOf(Vector3Int c, HashSet<Vector3Int> all, GridSystem grid, float gap)
        => TopOf(c, grid, gap) + EdgePush(all, c) * (grid.cellSize * creeperEdgeBias);

    // At an edge the real surface normal is somewhere between the top face and the
    // side face. Using that instead of a flat up is what makes leaves drape OVER the
    // rim, and what lets branches leave SIDEWAYS — both of them read the normal.
    private Vector3 RimNormal(Vector3Int c, HashSet<Vector3Int> all)
    {
        Vector3 push = EdgePush(all, c);
        return push.sqrMagnitude < 1e-4f
             ? Vector3.up
             : Vector3.Slerp(Vector3.up, push, creeperEdgeNormal * 0.5f).normalized;
    }

    // Drop points that land on top of the previous one. A zero-length segment has no
    // tangent, and the tube's frame is built by transporting a vector along those
    // tangents — one degenerate segment and the rest of the branch twists.
    private static void AddPoint(List<Vector3> pts, List<Vector3> normals, Vector3 p, Vector3 n)
    {
        if (pts.Count > 0 && (pts[pts.Count - 1] - p).sqrMagnitude < 1e-6f) return;
        pts.Add(p);
        normals.Add(n);
    }

    private static int CellKey(Vector3Int c) => (c.y * 1000 + c.z) * 1000 + c.x;

    private static uint Rand(ref uint s)
    {
        s = s * 1664525u + 1013904223u;
        return s >> 8;
    }

    private static Vector3 PieceCentre(PlacedPiece p, GridSystem grid)
    {
        if (p?.cells == null || p.cells.Length == 0) return Vector3.zero;
        Vector3 sum = Vector3.zero;
        for (int i = 0; i < p.cells.Length; i++) sum += grid.GridToWorld(p.cells[i]);
        return sum / p.cells.Length;
    }

    private void SpawnVine(int pieceId, VineTarget target, SynergyEvaluator evaluator, GridSystem grid)
    {
        var piece = evaluator.GetPieceById(pieceId);
        if (piece == null || piece.cells == null || piece.cells.Length == 0) return;

        var ins = grid.GetInstanceAt(piece.cells[0]);
        if (ins == null || ins.visualObject == null) return;

        Vector3 pieceCenter = ComputePieceCenter(piece, ins, grid);
        Vector3 outward     = pieceCenter - target.center;   // points away from the cluster; VineEffect flattens to horizontal

        // Sprout point: top-face center by default; with keepOffTopFaces, bias it
        // toward the outward side of the TOP FACE (but stay well inside the visible
        // mesh — only ~half-way to the grid edge, since the cartoon block is inset
        // from its cell boundary and sprouting at the true edge leaves a gap in the
        // bevel). The branch itself then leans outward, so foliage still frames the
        // block without the trunk detaching from it.
        Vector3 origin;
        if (keepOffTopFaces)
        {
            ComputeBlockBounds(piece, grid, out Vector3 bc, out float bhX, out float bhZ, out float bCell);
            Vector3 dir = new Vector3(outward.x, 0f, outward.z);
            if (dir.sqrMagnitude < 1e-4f)
            {
                float a = Hash01(pieceId * 379 + 11) * Mathf.PI * 2f;
                dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            }
            dir.Normalize();
            float tx = bhX / Mathf.Max(1e-4f, Mathf.Abs(dir.x));
            float tz = bhZ / Mathf.Max(1e-4f, Mathf.Abs(dir.z));
            float border = Mathf.Min(tx, tz);
            origin = new Vector3(bc.x, pieceCenter.y, bc.z)
                   + dir * (border * 0.5f)                 // half-way to the edge → still on the visible top
                   + Vector3.up * (bCell * 0.5f);          // sit right on the top face
        }
        else
        {
            origin = pieceCenter + Vector3.up * (grid.cellSize * 0.5f);
        }

        GameObject vine = Instantiate(vinePrefab, origin, Quaternion.identity, ins.visualObject.transform);
        _vines[pieceId] = vine;

        if (vine.TryGetComponent<VineEffect>(out var fx))
        {
            // Longer branches (outward mode): optionally extend the prefab's trunk.
            if (extraSegments != 0)
                fx.segments = Mathf.Max(2, fx.segments + extraSegments);
            if (!Mathf.Approximately(lengthScale, 1f))
                fx.segmentLength *= Mathf.Max(0.05f, lengthScale);

            // Lushness: denser + bigger foliage than the prefab authored.
            if (leafDensity > 0) fx.leavesPerNode = leafDensity;
            if (!Mathf.Approximately(leafSizeMul, 1f))
                fx.leafSize *= Mathf.Max(0.1f, leafSizeMul);

            // Scale the PLANT with the cluster, not just the plant count.
            //
            // Fork depth is the lever that matters: each extra level roughly
            // multiplies the branch count, so +1 depth does more for "枝繁叶茂" than
            // any amount of extra leaves on the same skeleton. Leaves and fork
            // probability ride along so the silhouette fills in rather than just
            // getting spindlier.
            float k = Mathf.Lerp(1f, Mathf.Max(1f, lushGain), target.lush);
            fx.maxForkDepth      = Mathf.Min(5, Mathf.RoundToInt(fx.maxForkDepth * k));
            fx.maxForksPerBranch = Mathf.Min(6, Mathf.RoundToInt(fx.maxForksPerBranch * k));
            fx.forkProbability   = Mathf.Clamp01(fx.forkProbability * k);
            fx.leavesPerNode     = Mathf.Min(10, Mathf.RoundToInt(fx.leavesPerNode * k));
            fx.tipCanopyLeaves   = Mathf.Min(16, Mathf.RoundToInt(fx.tipCanopyLeaves * k));

            // Interior blocks always climb: branching outward from the middle of a
            // cluster only pushes twigs into blocks that are already there.
            bool coil = target.interior || (coilChance > 0f && Hash01(pieceId) < coilChance);

            // Weave: a share of the outward vines lean ALONG the cluster edge instead
            // of away from it, so branches from neighbouring blocks run into each
            // other and interlace. Every vine leaning straight out is what made the
            // old canopy read as a starburst instead of a thicket.
            if (!coil && Hash01(pieceId * 5701 + 3) < weaveChance)
            {
                float side = Hash01(pieceId * 131 + 7) < 0.5f ? -90f : 90f;
                outward = Quaternion.AngleAxis(side, Vector3.up) * outward;
            }

            // A creeper wins over everything: it is the only mode that touches the
            // structure along its whole length.
            if (target.active != null && creeperChance > 0f
                && Hash01(pieceId * 8419 + 5) < creeperChance)
            {
                var nrm   = new List<Vector3>();
                var brs   = new List<VineEffect.CreeperBranch>();
                var route = BuildRoute(target.active, grid, piece,
                                       (uint)(pieceId * 2654435761u + 17u), nrm, brs);
                if (route.Count >= 4)
                {
                    fx.coilAround = false;
                    fx.bridgeTo   = false;
                    fx.GrowAlong(route, nrm, target.rootColor, target.tipColor, vinePrefab, brs);
                    return;
                }
            }

            // A runner wins over a free branch: it has somewhere to be.
            if (!coil && _bridgeTo.TryGetValue(pieceId, out var landing))
            {
                fx.coilAround   = false;
                fx.bridgeTo     = true;
                fx.bridgeTarget = landing;
                fx.Grow(target.rootColor, target.tipColor, outward, vinePrefab);
                return;
            }

            if (coil)
            {
                ComputeBlockBounds(piece, grid, out Vector3 bCenter, out float hX, out float hZ, out float cell);
                fx.coilAround     = true;
                fx.coilHalfX      = hX * Mathf.Max(0.1f, coilRadiusMul);
                fx.coilHalfZ      = hZ * Mathf.Max(0.1f, coilRadiusMul);
                fx.coilSurfaceGap = cell * Mathf.Max(0f, coilSurfaceGap);
                fx.coilTurns      = coilTurns;
                // Coil tops out at +0.55×coilHeight above the block center — cap it
                // below the top rim (+0.5×cell) when the top face must stay clear.
                float heightMul   = keepOffTopFaces ? Mathf.Min(coilHeightMul, 0.88f) : coilHeightMul;
                fx.coilHeight     = cell * Mathf.Max(0.1f, heightMul);
                fx.Grow(target.rootColor, target.tipColor, outward, vinePrefab, bCenter);
            }
            else
            {
                fx.coilAround = false;
                fx.Grow(target.rootColor, target.tipColor, outward, vinePrefab);
            }
        }
    }

    // ── Rim trees ────────────────────────────────────────────────────────────

    private void ReconcileTrees(SynergyEvaluator evaluator, GridSystem grid)
    {
        if (_trees == null) OnEnable();

        _treeWant.Clear();
        var actives = evaluator.Actives;
        for (int i = 0; i < actives.Count; i++)
        {
            var a = actives[i];
            if (a?.rule == null || a.rule.visualizer != this || a.claimedPieces == null) continue;

            Color root = useThemeColor ? BlockColorPalette.Get(a.rule.color) : branchColor;
            CollectTreeSites(a, grid, root, tipColor);
        }

        _treePrune.Clear();
        foreach (var kv in _trees)
            if (kv.Value == null || !_treeWant.ContainsKey(kv.Key)) _treePrune.Add(kv.Key);
        for (int i = 0; i < _treePrune.Count; i++)
        {
            var go = _trees[_treePrune[i]];
            _trees.Remove(_treePrune[i]);
            RetireVine(go);
        }

        if (vinePrefab == null) return;
        foreach (var kv in _treeWant)
        {
            if (_trees.TryGetValue(kv.Key, out var go) && go != null) continue;
            SpawnTree(kv.Key, kv.Value, grid);
        }
    }

    // Every open edge of the cluster's walkable top, thinned to `treeDensity`.
    //
    // An edge is OPEN if nothing walkable stands beside it within a step up or down.
    // A neighbour one cell higher is a wall, and a tree against a wall is a tree in
    // a corner, not a tree beside a path; a neighbour one lower is still part of the
    // route the enemies take, and a tree there would stand in the road.
    private void CollectTreeSites(ActiveSynergy a, GridSystem grid, Color root, Color tip)
    {
        var all = new HashSet<Vector3Int>();
        int claimed = 0;
        foreach (var p in a.claimedPieces)
        {
            if (p?.cells == null) continue;
            claimed++;
            foreach (var c in p.cells) all.Add(c);
        }
        if (all.Count == 0) return;

        var top = new HashSet<Vector3Int>();
        foreach (var c in all) if (!all.Contains(c + Vector3Int.up)) top.Add(c);

        float lush = Mathf.InverseLerp(2f, Mathf.Max(3, lushAtPieces), claimed);
        float cs = grid.cellSize;

        // Sorted so the cap trims the same trees every time — HashSet order is not
        // stable, and a cap applied in arbitrary order would swap which trees exist
        // whenever anything else on the board changed.
        var cells = new List<Vector3Int>(top);
        cells.Sort((x, y) => CellKey(x).CompareTo(CellKey(y)));

        int made = 0;
        bool haveFallback = false;
        long fallback = 0;
        TreeSite fallbackSite = default;
        var open = new bool[4];

        foreach (var c in cells)
        {
            if (made >= treeCap) break;

            for (int i = 0; i < 4; i++) open[i] = IsOpenEdge(top, c, _dirs[i]);

            // ── Convex corners ──────────────────────────────────────────────
            // _dirs is (-x, +x, -z, +z): a corner is one open x side meeting one
            // open z side on the same cell. The tree stands out on the diagonal and
            // leans off it, so its crown opens over the drop, not over the road.
            for (int xi = 0; xi < 2; xi++)
                for (int zi = 2; zi < 4; zi++)
                {
                    if (!open[xi] || !open[zi] || made >= treeCap) continue;

                    long key = TreeKey(c, 4 + xi * 2 + (zi - 2));
                    var diag = new Vector3(_dirs[xi].x, 0f, _dirs[zi].z);
                    var site = new TreeSite
                    {
                        cell = c,
                        pos  = grid.GridToWorld(c) + Vector3.up * (cs * 0.5f) + diag * (cs * treeInset),
                        up   = (Vector3.up + diag.normalized * treeLean).normalized,
                        root = root, tip = tip, lush = lush,
                    };

                    if (!haveFallback) { fallback = key; fallbackSite = site; haveFallback = true; }
                    if (Hash01((int)(key ^ (key >> 32))) > cornerDensity) continue;
                    _treeWant[key] = site;
                    made++;
                }

            // ── Plain edges, sparsely ───────────────────────────────────────
            for (int i = 0; i < 4 && made < treeCap; i++)
            {
                if (!open[i]) continue;

                // A cell with an open corner on this side already has its tree.
                bool cornered = i < 2 ? (open[2] || open[3]) : (open[0] || open[1]);
                if (cornered) continue;

                long key = TreeKey(c, i);
                if (Hash01((int)(key ^ (key >> 32))) > treeDensity) continue;

                var d     = _dirs[i];
                var dw    = new Vector3(d.x, 0f, d.z);
                var along = new Vector3(-d.z, 0f, d.x);
                float slide = (Hash01((int)(key ^ (key >> 29)) * 31 + 7) * 2f - 1f) * treeJitter;

                _treeWant[key] = new TreeSite
                {
                    cell = c,
                    pos  = grid.GridToWorld(c) + Vector3.up * (cs * 0.5f)
                         + dw * (cs * treeInset) + along * (cs * slide),
                    up   = (Vector3.up + dw * treeLean).normalized,
                    root = root, tip = tip, lush = lush,
                };
                made++;
            }
        }

        // Never zero — a Harmony that grows nothing reads as not having activated.
        if (made == 0 && haveFallback) _treeWant[fallback] = fallbackSite;
    }

    // Open = nothing walkable beside this edge within a step up or down. A higher
    // neighbour is a wall (a tree against it is in a corner, not beside a path); a
    // lower one is still part of the enemies' route, and a tree there is in the road.
    private static bool IsOpenEdge(HashSet<Vector3Int> top, Vector3Int c, Vector3Int d)
    {
        for (int dy = -1; dy <= 1; dy++)
            if (top.Contains(c + d + Vector3Int.up * dy)) return false;
        return true;
    }

    private void SpawnTree(long key, TreeSite s, GridSystem grid)
    {
        var ins = grid.GetInstanceAt(s.cell);
        if (ins == null || ins.visualObject == null) return;

        var go = Instantiate(vinePrefab, s.pos, Quaternion.identity, ins.visualObject.transform);
        _trees[key] = go;

        if (!go.TryGetComponent<VineEffect>(out var fx)) return;

        // The same cluster-size scaling the vines get, so a big Harmony is a thicker
        // wood and not just a longer row of identical saplings.
        float k = Mathf.Lerp(1f, Mathf.Max(1f, lushGain), s.lush);
        fx.maxForkDepth      = Mathf.Min(5, Mathf.RoundToInt(fx.maxForkDepth * k));
        fx.forkProbability   = Mathf.Clamp01(fx.forkProbability * Mathf.Lerp(1f, 1.25f, s.lush));
        fx.leavesPerNode     = Mathf.Min(10, Mathf.RoundToInt(fx.leavesPerNode * k));
        fx.tipCanopyLeaves   = Mathf.Min(16, Mathf.RoundToInt(fx.tipCanopyLeaves * k));

        fx.GrowTree(s.root, s.tip, s.up, vinePrefab);
    }

    // x 20 bits | z 20 bits | y 12 bits | site 3 bits (0-3 edges, 4-7 corners).
    private static long TreeKey(Vector3Int c, int site) =>
        ((long)(c.x + 524288) << 35) | ((long)(c.z + 524288) << 15) | ((long)(c.y + 2048) << 3) | (uint)(site & 7);

    // Deterministic hash → [0,1] for stable per-block style selection.
    private static float Hash01(int h)
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

    // Footprint center + per-axis half-extents (world) of a piece, so the coil
    // can trace the block's rectangular cross-section instead of a circle.
    private static void ComputeBlockBounds(PlacedPiece piece, GridSystem grid,
                                           out Vector3 center, out float halfX, out float halfZ, out float cell)
    {
        cell = grid.cellSize;
        Vector3 sum = Vector3.zero;
        Vector3 mn  = grid.GridToWorld(piece.cells[0]);
        Vector3 mx  = mn;
        for (int i = 0; i < piece.cells.Length; i++)
        {
            Vector3 w = grid.GridToWorld(piece.cells[i]);
            sum += w;
            mn = Vector3.Min(mn, w);
            mx = Vector3.Max(mx, w);
        }
        center = sum / piece.cells.Length;
        halfX = (mx.x - mn.x) * 0.5f + cell * 0.5f;   // footprint half-extent + half a cell to the face
        halfZ = (mx.z - mn.z) * 0.5f + cell * 0.5f;
    }

    // Center of the piece's footprint (cell-average). Works for single- and
    // multi-cell pieces; SpawnVine derives the actual sprout point from this.
    private static Vector3 ComputePieceCenter(PlacedPiece piece, PlacedBlockInstance ins, GridSystem grid)
    {
        Vector3 sum = Vector3.zero;
        int n = 0;
        for (int i = 0; i < piece.cells.Length; i++) { sum += grid.GridToWorld(piece.cells[i]); n++; }
        return n > 0 ? sum / n : ins.visualObject.transform.position;
    }

    // World center of every claimed cell in the cluster. Each vine leans away
    // from this so the group spreads outward in all directions.
    private static Vector3 ComputeClusterCenter(ActiveSynergy a, GridSystem grid)
    {
        Vector3 sum = Vector3.zero;
        int n = 0;
        foreach (var p in a.claimedPieces)
        {
            if (p?.cells == null) continue;
            for (int i = 0; i < p.cells.Length; i++) { sum += grid.GridToWorld(p.cells[i]); n++; }
        }
        return n > 0 ? sum / n : Vector3.zero;
    }

    private static void RetireVine(GameObject go)
    {
        if (go == null) return;
        if (go.TryGetComponent<VineEffect>(out var fx)) fx.Retire();   // wither, then self-destroy
        else Object.Destroy(go);
    }

    // ── Claimed-block outline rim ────────────────────────────────────────────
    // Recolors each claimed block's EXISTING inverse-hull outline (the slot
    // whose shader is GeoWorld/BlockOutline) via MaterialPropertyBlock — the
    // same mechanism PlacementController uses for selection, so no material is
    // instantiated and GPU instancing stays intact. Restored to the material's
    // authored color when the piece leaves the claim.
    private static readonly int _OutlineColorID = Shader.PropertyToID("_OutlineColor");
    private static MaterialPropertyBlock _outlineMpb;

    private void ReconcileOutlines(SynergyEvaluator evaluator, GridSystem grid)
    {
        // Restore blocks that left the claim (or whose block vanished). Reuse
        // _prune as scratch — the vine prune above is already done with it.
        _prune.Clear();
        foreach (var id in _outlined)
            if (!_outlineWant.ContainsKey(id)) _prune.Add(id);
        for (int i = 0; i < _prune.Count; i++)
        {
            int id = _prune[i];
            _outlined.Remove(id);
            var go = ResolveBlock(id, evaluator, grid);
            if (go != null) SetOutline(go, restore: true, Color.white);
        }

        // Apply to newly-claimed blocks (already-outlined ones stay as-is).
        foreach (var kv in _outlineWant)
        {
            if (_outlined.Contains(kv.Key)) continue;
            var go = ResolveBlock(kv.Key, evaluator, grid);
            if (go == null) continue;
            SetOutline(go, restore: false, kv.Value);
            _outlined.Add(kv.Key);
        }
    }

    private static GameObject ResolveBlock(int pieceId, SynergyEvaluator evaluator, GridSystem grid)
    {
        var piece = evaluator.GetPieceById(pieceId);
        if (piece == null || piece.cells == null || piece.cells.Length == 0) return null;
        var ins = grid.GetInstanceAt(piece.cells[0]);
        return ins != null ? ins.visualObject : null;
    }

    // Walks a block's renderers, finds the outline slot, and sets _OutlineColor
    // via MPB. restore=true reads the shared material's authored color back.
    private static void SetOutline(GameObject block, bool restore, Color color)
    {
        if (block == null) return;
        if (_outlineMpb == null) _outlineMpb = new MaterialPropertyBlock();

        var rends = block.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++)
        {
            var r = rends[i];
            if (r == null) continue;

            Material outlineMat = null;
            var mats = r.sharedMaterials;
            for (int m = 0; m < mats.Length; m++)
            {
                var mat = mats[m];
                if (mat == null || mat.shader == null) continue;
                var n = mat.shader.name;
                if (n == "GeoWorld/BlockOutline" || n == "Custom/ObjectOutline") { outlineMat = mat; break; }
            }
            if (outlineMat == null) continue;

            r.GetPropertyBlock(_outlineMpb);
            _outlineMpb.SetColor(_OutlineColorID, restore ? outlineMat.GetColor(_OutlineColorID) : color);
            r.SetPropertyBlock(_outlineMpb);
        }
    }

    // ── Border detection (multi-cell aware): a piece is a border piece if any
    //    of its cells has a 4-neighbour in the GROUND PLANE that is not a synergy
    //    cell. Also the step set the creeper walks.
    //
    // These used to be Vector3Int.up/down, which on this grid is ±Y — height, not a
    // ground-plane neighbour. Since almost nothing is stacked, almost every cell had
    // empty space above it and so almost every piece counted as "border": the
    // distinction the selection code is built on was doing nothing.
    private static readonly Vector3Int[] _dirs =
    {
        new Vector3Int(-1, 0, 0), new Vector3Int(1, 0, 0),
        new Vector3Int(0, 0, -1), new Vector3Int(0, 0, 1),
    };

    private static List<PlacedPiece> GetBorderPieces(IEnumerable<PlacedPiece> pieces)
    {
        var result = new List<PlacedPiece>();
        if (pieces == null) return result;

        var all = new HashSet<Vector3Int>();
        foreach (var piece in pieces)
        {
            if (piece?.cells == null) continue;
            foreach (var c in piece.cells) all.Add(c);
        }

        foreach (var piece in pieces)
        {
            if (piece?.cells == null) continue;

            bool isBorder = false;
            foreach (var c in piece.cells)
            {
                foreach (var d in _dirs)
                {
                    if (!all.Contains(c + d)) { isBorder = true; break; }
                }
                if (isBorder) break;
            }

            if (isBorder) result.Add(piece);
        }
        return result;
    }
}
