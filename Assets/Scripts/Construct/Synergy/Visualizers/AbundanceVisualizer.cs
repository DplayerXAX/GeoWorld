using System.Collections.Generic;
using UnityEngine;

// Abundance — "harvest field" visualizer. AbundanceRule has no cell filter,
// so every claimed cell blooms. Plants geometric mesh flowers on the top
// face of each claimed block.
//
// Same reconciler contract as HarmonyVineVisualizer: SynergyVisualFX rebuilds
// every claimed piece's decoration on any claim-count change, so we own each
// patch ourselves (keyed by pieceId) via Reconcile() rather than letting the
// dispatcher spawn/destroy it, so existing patches don't re-bloom on churn.
[CreateAssetMenu(
    menuName = "GeoWorld/Synergy/Visualizers/Abundance Bloom",
    fileName = "AbundanceVisualizer")]
public class AbundanceVisualizer : SynergyVisualizer
{
    [Header("Color")]
    [Tooltip("Tint petals with the synergy's theme color (blended toward white). If off, uses Bloom Color below.")]
    public bool useThemeColor = true;

    [Tooltip("How far the theme color is blended toward white for the petals (0 = pure bold theme, 1 = white). Keep low for a silkscreen look.")]
    [Range(0f, 1f)] public float themeWhiten = 0.2f;

    [Tooltip("Petal color when 'Use Theme Color' is off.")]
    public Color bloomColor = new Color(0.86f, 0.30f, 0.34f, 1f);

    [Tooltip("Core color — the golden 'fruit / grain' at each flower's heart. Always used.")]
    public Color accentColor = new Color(0.98f, 0.80f, 0.30f, 1f);

    [Tooltip("Petal brightness multiplier (>1 reads brighter; blooms under a URP Bloom override).")]
    [Range(0.5f, 3f)] public float flowerBrightness = 1f;

    [Tooltip("How far petal colors spread in hue from the base color (0 = single color, ~0.08 subtle multi-color, ~0.15 lively). Stays analogous so it never goes garish.")]
    [Range(0f, 0.3f)] public float hueSpread = 0.08f;

    [Header("Field density")]
    [Tooltip("Each claimed cell grows a random 1..this many flowers.")]
    [Range(1, 6)] public int flowersPerCell = 2;

    [Tooltip("Chance each claimed cell grows flowers at all (the rest stay bare → a scattered field, not a uniform carpet).")]
    [Range(0f, 1f)] public float cellCoverChance = 0.85f;

    [Tooltip("Hard cap on flowers per block, so a big multi-cell piece can't spawn a forest.")]
    public int maxFlowersPerPatch = 18;

    [Tooltip("Flower outer radius, in cell-size units.")]
    public float flowerSize = 0.34f;

    [Tooltip("How far flowers scatter across a cell's top face, in cell-size units.")]
    public float scatterRadius = 0.26f;

    [Tooltip("Stalk height the flower head rides on, in cell-size units. Raises the whole field so it reads taller and clears the block face. 0 = flowers sit flat on the face.")]
    [Range(0f, 1f)] public float stemHeight = 0.18f;

    [Tooltip("Stalk color (the green stem under each flower head).")]
    public Color stemColor = new Color(0.20f, 0.42f, 0.16f, 1f);

    [Header("Bloom timing")]
    [Tooltip("Seconds for one flower to pop open.")]
    public float bloomDuration = 0.45f;

    [Tooltip("Delay added per flower so the patch blooms in a wave.")]
    public float bloomStagger = 0.05f;

    [Tooltip("Seconds to wilt (scale → 0) before a released patch is destroyed.")]
    public float witherDuration = 0.35f;

    [Header("Motion")]
    [Tooltip("Slow turn of each flower around its stem, deg/sec (shows off the radial geometry).")]
    public float spinSpeed    = 16f;

    public float swaySpeed    = 1.5f;
    [Tooltip("Max wind sway tilt, degrees.")]
    public float swayAngleDeg = 7f;

    [Tooltip("Vertical bob amplitude, in cell-size units.")]
    public float bobAmplitude = 0.04f;
    public float bobSpeed     = 1.2f;

    // What one claimed piece should bloom this tick.
    private struct PatchTarget
    {
        public Color[] palette;
        public Color   center;
    }

    [System.NonSerialized] private Dictionary<int, GameObject>  _patches;  // pieceId -> live patch
    [System.NonSerialized] private Dictionary<int, PatchTarget> _desired;  // pieceId -> what to bloom
    [System.NonSerialized] private List<int>                    _prune;

    private void OnEnable()
    {
        _patches = new Dictionary<int, GameObject>();
        _desired = new Dictionary<int, PatchTarget>();
        _prune   = new List<int>();
    }

    private void OnDisable()
    {
        _patches?.Clear();
        _desired?.Clear();
        _prune?.Clear();
    }

    public override GameObject OnPieceClaimed(PlacedBlockInstance instance, ActiveSynergy active)
    {
        Reconcile();
        return null;   // WE own the patch lifecycle; dispatcher must not destroy it.
    }

    public override void OnPieceReleased(PlacedBlockInstance instance, ActiveSynergy active, GameObject spawned)
    {
        // Do NOT call base (it would Destroy `spawned`, but spawned is null).
        Reconcile();
    }

    // Make the world's patches match the live claim state.
    private void Reconcile()
    {
        if (_patches == null) OnEnable();   // lazy guard (OnEnable may not have run)

        var evaluator = SynergyEvaluator.Instance;
        var grid      = GridSystem.instance;
        if (evaluator == null || grid == null) return;

        // Every claimed piece across every active using this visualizer.
        _desired.Clear();
        var actives = evaluator.Actives;
        for (int i = 0; i < actives.Count; i++)
        {
            var a = actives[i];
            if (a?.rule == null || a.rule.visualizer != this || a.claimedPieces == null) continue;

            Color theme   = BlockColorPalette.Get(a.rule.color);
            Color baseCol = useThemeColor ? Color.Lerp(theme, Color.white, themeWhiten) : bloomColor;

            var target = new PatchTarget { palette = BuildPalette(baseCol), center = accentColor };
            foreach (var p in a.claimedPieces)
                if (p != null) _desired[p.id] = target;
        }

        // Prune patches whose block was destroyed or piece is no longer claimed.
        _prune.Clear();
        foreach (var kv in _patches)
            if (kv.Value == null || !_desired.ContainsKey(kv.Key)) _prune.Add(kv.Key);
        for (int i = 0; i < _prune.Count; i++)
        {
            int id = _prune[i];
            var go = _patches[id];
            _patches.Remove(id);
            RetirePatch(go);
        }

        // Spawn wanted pieces without a live patch; existing ones keep blooming.
        foreach (var kv in _desired)
        {
            if (_patches.TryGetValue(kv.Key, out var go) && go != null) continue;
            SpawnPatch(kv.Key, kv.Value, evaluator, grid);
        }
    }

    private void SpawnPatch(int pieceId, PatchTarget target, SynergyEvaluator evaluator, GridSystem grid)
    {
        var piece = evaluator.GetPieceById(pieceId);
        if (piece == null || piece.cells == null || piece.cells.Length == 0) return;

        var ins = grid.GetInstanceAt(piece.cells[0]);
        if (ins == null || ins.visualObject == null) return;

        float cs = grid.cellSize;

        // Plant on top face centers (world-axis-aligned even if the block is
        // rotated). Each cell is randomly covered or left bare, hashed by
        // cell so coverage is stable.
        var tops = new List<Vector3>(piece.cells.Length);
        for (int i = 0; i < piece.cells.Length; i++)
        {
            var cell = piece.cells[i];
            if (Hash01(CellHash(cell) ^ 0x5bd1e995) <= cellCoverChance)
                tops.Add(grid.GridToWorld(cell) + Vector3.up * (cs * 0.5f));
        }

        // Free world-space object, not parented to the block: a rotated block
        // would tip the flower geometry and a scale-0 GrowIn would collapse it.
        // Created even when empty so Reconcile doesn't respawn it every tick.
        var go = new GameObject($"AbundanceBloom_{pieceId}");

        var patch = go.AddComponent<BloomPatch>();
        patch.bloomDuration  = bloomDuration;
        patch.bloomStagger   = bloomStagger;
        patch.spinSpeed      = spinSpeed;
        patch.swaySpeed      = swaySpeed;
        patch.swayAngleDeg   = swayAngleDeg;
        patch.bobAmplitude   = bobAmplitude * cs;
        patch.bobSpeed       = bobSpeed;
        patch.witherDuration = witherDuration;
        patch.stemHeight     = stemHeight * cs;
        patch.stemColor      = stemColor;

        patch.Grow(tops.ToArray(), target.palette, target.center,
                   flowersPerCell, flowerSize * cs, scatterRadius * cs, maxFlowersPerPatch);

        _patches[pieceId] = go;
    }

    private static void RetirePatch(GameObject go)
    {
        if (go == null) return;
        if (go.TryGetComponent<BloomPatch>(out var patch)) patch.Retire();   // wilt, then self-destroy
        else Object.Destroy(go);
    }

    // Small analogous color set around the base hue, clamped to a
    // vivid-but-not-muddy range so flowers vary without going garish.
    private Color[] BuildPalette(Color baseCol)
    {
        Color.RGBToHSV(baseCol, out float h, out float s, out float v);
        s = Mathf.Clamp(s, 0.45f, 0.92f);
        v = Mathf.Clamp(v, 0.70f, 1f);
        float sp = Mathf.Max(0f, hueSpread);

        var cols = new[]
        {
            FromHSV(h,         s,                       v),
            FromHSV(h + sp,    Mathf.Clamp01(s * 0.88f), Mathf.Clamp01(v * 1.03f)),
            FromHSV(h - sp,    Mathf.Clamp01(s),         Mathf.Clamp01(v * 0.94f)),
            FromHSV(h + sp * 2f, Mathf.Clamp01(s * 0.78f), Mathf.Clamp01(v)),
        };
        for (int i = 0; i < cols.Length; i++)
        {
            cols[i].r *= flowerBrightness;
            cols[i].g *= flowerBrightness;
            cols[i].b *= flowerBrightness;
            cols[i].a = 1f;
        }
        return cols;
    }

    private static Color FromHSV(float h, float s, float v)
    {
        h -= Mathf.Floor(h);   // wrap hue into [0,1)
        var c = Color.HSVToRGB(h, s, v);
        c.a = 1f;
        return c;
    }

    private static int CellHash(Vector3Int c)
    {
        unchecked { return c.x * 73856093 ^ c.y * 19349663 ^ c.z * 83492791; }
    }

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
}
