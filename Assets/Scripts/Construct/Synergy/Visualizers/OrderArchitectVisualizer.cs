using System.Collections.Generic;
using UnityEngine;

// 秩序 (Order) — "architect" visualizer.
//
// OrderRule claims a whole face-connected same-color component and (like
// Abundance) implements NO ICellHighlightFilter, so every claimed cell is fair
// game. This visualizer reads Order as PRECISION / STRUCTURE: it overlays each
// claimed block with a technical-blueprint line drawing — cube-edge wireframe,
// corner registration brackets, dimension ticks — and sets slowly-turning gear
// schematics on top, neighbours meshing in opposite directions. The structure
// "snaps into place" and breathes a calibration pulse. Architect's plan, gears,
// calibration, becoming structure.
//
// RECONCILER (read before editing) — same contract as HarmonyVineVisualizer /
// AbundanceVisualizer:
//   • WE own each rig in a pieceId → OrderRig-GameObject dictionary.
//   • OnPieceClaimed / OnPieceReleased just call Reconcile(), which reads the
//     LIVE claim state and makes the world match: spawn missing rigs, KEEP
//     existing ones untouched (no re-assemble on churn), fade out departed ones.
//   • OnPieceClaimed returns null so the dispatcher never Destroys our rigs.
// Rigs are FREE-STANDING world-space objects (not parented), built from the grid
// cells, so they're immune to the block's rotation and GrowIn scale.
[CreateAssetMenu(
    menuName = "GeoWorld/Synergy/Visualizers/Order Architect",
    fileName = "OrderArchitectVisualizer")]
public class OrderArchitectVisualizer : SynergyVisualizer
{
    [Header("Color")]
    [Tooltip("Tint the blueprint lines with the synergy's theme color (blended toward white). If off, uses Line Color below.")]
    public bool useThemeColor = true;

    [Tooltip("How far the theme color is blended toward white for the lines (0 = pure theme, 1 = white).")]
    [Range(0f, 1f)] public float themeWhiten = 0.35f;

    [Tooltip("Line color when 'Use Theme Color' is off (alpha reused as line opacity). Default is a blueprint cyan.")]
    public Color lineColor = new Color(0.40f, 0.90f, 1f, 1f);

    [Header("Gear color (flat silkscreen)")]
    [Tooltip("Base printed color of the cogs (flat, matches the cube silkscreen look).")]
    public Color metalColor = new Color(0.22f, 0.24f, 0.28f, 1f);

    [Tooltip("Tint the cog color toward the synergy theme color (0 = pure ink).")]
    [Range(0f, 1f)] public float metalThemeTint = 0.18f;

    [Header("Gear outline & beat flash")]
    [Tooltip("Bold cartoon outline color (same inverse-hull the blocks wear).")]
    public Color outlineColor = new Color(0.04f, 0.04f, 0.06f, 1f);

    [Range(0f, 0.2f)] public float outlineWidth = 0.06f;

    [Tooltip("How hard each cog flashes (toward Flash Color) on its beat-tick. 0 = no flash.")]
    [Range(0f, 1f)] public float beatFlash = 0.45f;

    [Tooltip("Color each cog stamps toward on the beat.")]
    public Color flashColor = new Color(0.85f, 1f, 1f, 1f);

    [Header("Structure")]
    [Tooltip("Draw L-shaped registration brackets at the bounding-box corners (calibration cue).")]
    public bool showBrackets = true;

    [Tooltip("Corner bracket arm length, in cell-size units.")]
    [Range(0.05f, 0.4f)] public float bracketFrac = 0.2f;

    [Tooltip("Draw dimension ticks along the bottom edge (a ruler / measuring cue).")]
    public bool showTicks = true;

    [Range(0, 8)] public int tickCount = 4;

    [Header("Gear shape & placement")]
    public bool showGears = true;

    [Range(6, 24)] public int gearTeeth = 12;

    [Tooltip("Smallest cog radius, in cell-size units.")]
    [Range(0.1f, 0.6f)] public float gearSizeMinFrac = 0.20f;

    [Tooltip("Largest cog radius, in cell-size units (sizes vary between min and max).")]
    [Range(0.1f, 0.8f)] public float gearSizeMaxFrac = 0.50f;

    [Tooltip("Chance each OUTER block face gets a cog bolted on (random coverage). Cogs spin around the face normal, so different faces spin in different planes.")]
    [Range(0f, 1f)] public float faceCoverChance = 0.5f;

    [Tooltip("Max cogs per claimed piece.")]
    public int maxGears = 12;

    [Tooltip("How far the cog sits off the block face, in cell-size units.")]
    [Range(0f, 0.3f)] public float faceOffsetFrac = 0.05f;

    [Header("Gear rotation (musical, stepped)")]
    [Tooltip("Beats per minute the cogs tick to. 30 = stately clockwork.")]
    public float bpm = 30f;

    [Tooltip("Degrees the smallest cog steps per beat. Bigger cogs step proportionally less.")]
    public float degreesPerBeat = 90f;

    [Tooltip("Ratchet: snap one step per beat then hold. Off = smooth continuous.")]
    public bool steppedRotation = true;

    [Tooltip("Portion of each beat spent snapping (rest is the hold). Lower = snappier tick.")]
    [Range(0.02f, 1f)] public float snapFraction = 0.22f;

    [Tooltip("Per-cog beat offset so the ticks aren't perfectly lockstep.")]
    [Range(0f, 0.5f)] public float phaseJitter = 0.15f;

    [Header("Animation")]
    [Tooltip("Seconds for a rig to assemble / snap into place.")]
    public float fadeInDuration = 0.5f;

    [Tooltip("Seconds to fade out a released rig.")]
    public float witherDuration = 0.3f;

    [Tooltip("Calibration breathing speed / depth.")]
    public float pulseSpeed = 2f;
    [Range(0f, 0.5f)] public float pulseDepth = 0.12f;

    // What one claimed piece should draw this tick.
    private struct Target
    {
        public Color line;
        public Color gear;
    }

    [System.NonSerialized] private Dictionary<int, GameObject> _rigs;     // pieceId -> live rig
    [System.NonSerialized] private Dictionary<int, Target>     _desired;  // pieceId -> colors (this tick)
    [System.NonSerialized] private List<int>                   _prune;    // scratch

    private void OnEnable()
    {
        _rigs    = new Dictionary<int, GameObject>();
        _desired = new Dictionary<int, Target>();
        _prune   = new List<int>();
    }

    private void OnDisable()
    {
        _rigs?.Clear();
        _desired?.Clear();
        _prune?.Clear();
    }

    public override GameObject OnPieceClaimed(PlacedBlockInstance instance, ActiveSynergy active)
    {
        Reconcile();
        return null;   // WE own the rig; the dispatcher must not Destroy it.
    }

    public override void OnPieceReleased(PlacedBlockInstance instance, ActiveSynergy active, GameObject spawned)
    {
        // Do NOT call base (it would Destroy `spawned`, but spawned is null).
        Reconcile();
    }

    // ── Core: make the world's rigs match the live claim state ───────────────
    private void Reconcile()
    {
        if (_rigs == null) OnEnable();   // lazy guard

        var evaluator = SynergyEvaluator.Instance;
        var grid      = GridSystem.instance;
        if (evaluator == null || grid == null) return;

        // 1) Desired this tick: every claimed piece across every active using
        //    THIS visualizer (Order has no cell filter → all cells decorated).
        _desired.Clear();
        var actives = evaluator.Actives;
        for (int i = 0; i < actives.Count; i++)
        {
            var a = actives[i];
            if (a?.rule == null || a.rule.visualizer != this || a.claimedPieces == null) continue;

            Color theme = BlockColorPalette.Get(a.rule.color);
            Color line  = useThemeColor ? Color.Lerp(theme, Color.white, themeWhiten) : lineColor;
            Color metal = Color.Lerp(metalColor, theme, metalThemeTint);

            var target = new Target { line = line, gear = metal };
            foreach (var p in a.claimedPieces)
                if (p != null) _desired[p.id] = target;
        }

        // 2) Prune: rigs whose block was destroyed (null) or whose piece left.
        _prune.Clear();
        foreach (var kv in _rigs)
            if (kv.Value == null || !_desired.ContainsKey(kv.Key)) _prune.Add(kv.Key);
        for (int i = 0; i < _prune.Count; i++)
        {
            int id = _prune[i];
            var go = _rigs[id];
            _rigs.Remove(id);
            RetireRig(go);
        }

        // 3) Spawn: wanted pieces with no live rig yet. Existing rigs untouched.
        foreach (var kv in _desired)
        {
            if (_rigs.TryGetValue(kv.Key, out var go) && go != null) continue;
            SpawnRig(kv.Key, kv.Value, evaluator, grid);
        }
    }

    private void SpawnRig(int pieceId, Target target, SynergyEvaluator evaluator, GridSystem grid)
    {
        var piece = evaluator.GetPieceById(pieceId);
        if (piece == null || piece.cells == null || piece.cells.Length == 0) return;

        var ins = grid.GetInstanceAt(piece.cells[0]);
        if (ins == null || ins.visualObject == null) return;

        float cs = grid.cellSize;
        var centers = new Vector3[piece.cells.Length];
        for (int i = 0; i < piece.cells.Length; i++)
            centers[i] = grid.GridToWorld(piece.cells[i]);

        // FREE world-space object (NOT parented): a rotated block would tip the
        // upright structure and a scale-0 GrowIn block would collapse it. The grid
        // cells are axis-aligned in world, so the rig always frames them squarely.
        var go = new GameObject($"OrderRig_{pieceId}");

        var rig = go.AddComponent<OrderRig>();
        rig.showGears        = showGears;
        rig.gearTeeth        = gearTeeth;
        rig.gearSizeMinFrac  = gearSizeMinFrac;
        rig.gearSizeMaxFrac  = gearSizeMaxFrac;
        rig.faceCoverChance  = faceCoverChance;
        rig.maxGears         = maxGears;
        rig.faceOffsetFrac   = faceOffsetFrac;
        rig.outlineColor     = outlineColor;
        rig.outlineWidth     = outlineWidth;
        rig.beatFlash        = beatFlash;
        rig.flashColor       = flashColor;
        rig.bpm              = bpm;
        rig.degreesPerBeat   = degreesPerBeat;
        rig.steppedRotation  = steppedRotation;
        rig.snapFraction     = snapFraction;
        rig.phaseJitter      = phaseJitter;
        rig.showBrackets     = showBrackets;
        rig.bracketFrac      = bracketFrac;
        rig.showTicks        = showTicks;
        rig.tickCount        = tickCount;
        rig.fadeInDuration   = fadeInDuration;
        rig.witherDuration   = witherDuration;
        rig.pulseSpeed       = pulseSpeed;
        rig.pulseDepth       = pulseDepth;

        rig.Build(centers, cs, target.line, target.gear);

        _rigs[pieceId] = go;
    }

    private static void RetireRig(GameObject go)
    {
        if (go == null) return;
        if (go.TryGetComponent<OrderRig>(out var rig)) rig.Retire();   // fade out, then self-destroy
        else Object.Destroy(go);
    }
}
