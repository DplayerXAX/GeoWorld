using System.Collections.Generic;
using UnityEngine;

// Grows LineRenderer "vines" out of a few border blocks in a Harmony synergy,
// as a restrained in-world cue (replacing the per-block material swap).
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
    [Tooltip("Vine prefab. Needs a LineRenderer (+ VineEffect for the grow/wither animation).")]
    public GameObject vinePrefab;

    [Tooltip("Max vines per synergy cluster. Vines are sticky: once grown they stay until their block leaves the claim.")]
    [Range(1, 8)]
    public int maxVines = 4;

    [Header("Color")]
    [Tooltip("Use the synergy's theme color instead of branchColor below.")]
    public bool useThemeColor = false;

    [Tooltip("Branch / body color used when 'Use Theme Color' is off. Default is a woody brown.")]
    public Color branchColor = new Color(0.45f, 0.30f, 0.18f, 1f);

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
    }

    // ── Runtime state. NOT serialized, and reset on load, so it never leaks
    //    across editor Play sessions or holds stale ActiveSynergy references.
    [System.NonSerialized] private Dictionary<int, GameObject> _vines;    // pieceId -> live vine
    [System.NonSerialized] private Dictionary<int, VineTarget> _desired;  // pieceId -> what to grow (this tick)
    [System.NonSerialized] private List<int>                   _prune;    // scratch
    [System.NonSerialized] private HashSet<int>                _outlined;     // pieceId -> outline currently recolored
    [System.NonSerialized] private Dictionary<int, Color>      _outlineWant;  // pieceId -> outline rim color (this tick)

    private void OnEnable()
    {
        _vines       = new Dictionary<int, GameObject>();
        _desired     = new Dictionary<int, VineTarget>();
        _prune       = new List<int>();
        _outlined    = new HashSet<int>();
        _outlineWant = new Dictionary<int, Color>();
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
    }

    // Sticky + deterministic target selection for ONE cluster:
    //   * Keep every already-vined piece that's still claimed, so a grown vine
    //     never vanishes just because the cluster's shape changed.
    //   * Fill the remaining slots (up to maxVines) from border pieces, lowest
    //     pieceId first — deterministic, so selection doesn't jitter under churn.
    private void SelectTargets(ActiveSynergy a, GridSystem grid, Color rootColor, Color tipColor)
    {
        Vector3 center = ComputeClusterCenter(a, grid);
        var target = new VineTarget { rootColor = rootColor, tipColor = tipColor, center = center };

        int count = 0;

        // pass 1 — sticky: existing vines that are still claimed
        foreach (var p in a.claimedPieces)
        {
            if (p == null) continue;
            if (_vines.TryGetValue(p.id, out var go) && go != null)
            {
                _desired[p.id] = target;
                count++;
            }
        }

        // pass 2 — fill from border pieces in a stable order
        if (count < maxVines)
        {
            List<PlacedPiece> border = GetBorderPieces(a.claimedPieces);
            border.Sort((x, y) => x.id.CompareTo(y.id));
            for (int i = 0; i < border.Count && count < maxVines; i++)
            {
                int id = border[i].id;
                if (_desired.ContainsKey(id)) continue;
                _desired[id] = target;
                count++;
            }
        }
    }

    private void SpawnVine(int pieceId, VineTarget target, SynergyEvaluator evaluator, GridSystem grid)
    {
        var piece = evaluator.GetPieceById(pieceId);
        if (piece == null || piece.cells == null || piece.cells.Length == 0) return;

        var ins = grid.GetInstanceAt(piece.cells[0]);
        if (ins == null || ins.visualObject == null) return;

        Vector3 origin  = ComputeVineOrigin(piece, ins, grid);
        Vector3 outward = origin - target.center;   // points away from the cluster; VineEffect flattens to horizontal

        GameObject vine = Instantiate(vinePrefab, origin, Quaternion.identity, ins.visualObject.transform);
        _vines[pieceId] = vine;

        if (vine.TryGetComponent<VineEffect>(out var fx))
        {
            // Longer branches (outward mode): optionally extend the prefab's trunk.
            if (extraSegments != 0)
                fx.segments = Mathf.Max(2, fx.segments + extraSegments);
            if (!Mathf.Approximately(lengthScale, 1f))
                fx.segmentLength *= Mathf.Max(0.05f, lengthScale);

            // Per-block deterministic style: some vines coil up around the block,
            // the rest branch outward. Hashed by pieceId so a given block keeps
            // its style across reconciles.
            bool coil = coilChance > 0f && Hash01(pieceId) < coilChance;
            if (coil)
            {
                ComputeBlockBounds(piece, grid, out Vector3 bCenter, out float hX, out float hZ, out float cell);
                fx.coilAround     = true;
                fx.coilHalfX      = hX * Mathf.Max(0.1f, coilRadiusMul);
                fx.coilHalfZ      = hZ * Mathf.Max(0.1f, coilRadiusMul);
                fx.coilSurfaceGap = cell * Mathf.Max(0f, coilSurfaceGap);
                fx.coilTurns      = coilTurns;
                fx.coilHeight     = cell * Mathf.Max(0.1f, coilHeightMul);
                fx.Grow(target.rootColor, target.tipColor, outward, vinePrefab, bCenter);
            }
            else
            {
                fx.coilAround = false;
                fx.Grow(target.rootColor, target.tipColor, outward, vinePrefab);
            }
        }
    }

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

    // Top-center of the piece's footprint, so the branch sprouts from the top
    // face. Works for single- and multi-cell pieces.
    private static Vector3 ComputeVineOrigin(PlacedPiece piece, PlacedBlockInstance ins, GridSystem grid)
    {
        Vector3 sum = Vector3.zero;
        int n = 0;
        for (int i = 0; i < piece.cells.Length; i++) { sum += grid.GridToWorld(piece.cells[i]); n++; }
        Vector3 center = n > 0 ? sum / n : ins.visualObject.transform.position;
        return center + Vector3.up * (grid.cellSize * 0.5f);
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
    //    of its cells has a 4-neighbour (L/R/U/D) that is NOT a synergy cell.
    private static readonly Vector3Int[] _dirs =
    {
        Vector3Int.left, Vector3Int.right, Vector3Int.up, Vector3Int.down
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
