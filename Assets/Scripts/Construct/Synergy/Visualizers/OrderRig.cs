using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// One "architect rig" for a claimed Order (秩序) piece: a technical-blueprint
// LINE drawing that frames the block(s) as a precise structure (cube-edge
// wireframe + corner registration brackets + dimension ticks), RINGED by solid
// METAL gear cogs of varying sizes that turn around it in time with the music.
// Spawned + owned by OrderArchitectVisualizer.
//
// FREE-STANDING in world space (like ConstellationView), built from the grid
// CELLS (axis-aligned), so it's immune to the block's rotation and GrowIn scale.
// The frame is crisp unlit lines; the gears are extruded, lit, metallic meshes
// placed around the footprint perimeter — different radii, neighbours counter-
// rotating, bigger cogs turning proportionally slower (a meshed-clockwork read).
// Rotation is BPM-locked (degreesPerBeat × bpm). Build() draws it; Retire() fades.
[DisallowMultipleComponent]
public class OrderRig : MonoBehaviour
{
    // ── Frame (wireframe) config ─────────────────────────────────────────────
    public bool  showBrackets   = true;
    public float bracketFrac    = 0.20f;   // corner bracket arm length / cellSize
    public bool  showTicks      = true;
    public int   tickCount      = 4;

    // ── Gear config (cogs bolted onto the structure's OUTER faces) ───────────
    public bool  showGears       = true;
    public int   gearTeeth       = 12;
    public float gearSizeMinFrac = 0.20f;   // smallest cog radius / cellSize
    public float gearSizeMaxFrac = 0.50f;   // largest  cog radius / cellSize
    [Range(0f, 1f)] public float faceCoverChance = 0.5f;   // chance each outer face gets a cog
    public int   maxGears        = 12;      // cap per piece
    public float faceOffsetFrac  = 0.05f;   // lift off the face / cellSize
    public Color outlineColor    = new Color(0.04f, 0.04f, 0.06f, 1f);  // bold cartoon rim (matches blocks)
    public float outlineWidth    = 0.06f;
    [Range(0f, 1f)] public float beatFlash = 0.45f;       // cog ink-stamp flash on each tick
    public Color flashColor      = new Color(0.85f, 1f, 1f, 1f);

    // ── Hologram (translucent, floating, flickering cogs) ────────────────────
    public bool  holographic     = true;    // translucent additive cogs instead of solid ink
    [Range(0.05f, 1f)] public float holoAlpha = 0.4f;   // base opacity of the holo cogs
    public float floatDistanceFrac = 0.35f;  // how far cogs float OFF the faces / cellSize (don't hug the block)
    public float hoverAmplitudeFrac = 0f;     // vertical/normal hover amplitude / cellSize (0 = no bob)
    public float hoverSpeed        = 1.1f;    // hover oscillations per second-ish
    [Range(0f, 0.6f)] public float flickerAmount = 0.18f;   // holo opacity jitter
    public bool  sideFacesOnly     = true;    // ring the block on its SIDE faces only — keeps top/bottom (the walkable road) clear

    // ── Rotation (musical, stepped) ──────────────────────────────────────────
    public float bpm             = 30f;
    public float degreesPerBeat  = 90f;     // reference cog's per-beat STEP (bigger cog → smaller)
    public bool  steppedRotation = true;    // snap one step per beat (ratchet) vs smooth
    [Range(0.02f, 1f)] public float snapFraction = 0.22f;   // portion of the beat spent snapping
    [Range(0f, 0.5f)]  public float phaseJitter  = 0.15f;   // per-cog beat offset (un-lockstep the ticks)

    // ── Living-machine behaviors ─────────────────────────────────────────────
    // The structure isn't a static monument with gears bolted on — it's a
    // MACHINE AT WORK. Three quiet life signs:
    //   • Scan sweep — a bright cross-section outline periodically travels up
    //     the frame, like a blueprint being re-verified layer by layer.
    //   • Circuit runners — small bright nodes trace the base perimeter in a
    //     loop, current flowing through the structure.
    //   • Gear shift — every N beats the whole clockwork reverses direction in
    //     one synchronized "clunk" (with the ink-stamp flash), so the ticking
    //     reads as a deliberate machine cycle instead of a metronome.
    public bool  showScan       = true;
    public float scanInterval   = 5f;      // seconds between sweeps
    public float scanDuration   = 0.9f;    // seconds one sweep takes (bottom → top)

    public bool  showRunners    = true;
    public int   runnerCount    = 2;       // nodes tracing the base perimeter
    public float runnerSpeed    = 1.6f;    // cells per second along the loop
    public float runnerSizeFrac = 0.07f;   // node size / cellSize

    public int   shiftEveryBeats = 8;      // gears reverse every N beats (0 = never)

    // ── Animation ────────────────────────────────────────────────────────────
    public float fadeInDuration = 0.5f;
    public float witherDuration = 0.3f;
    public float pulseSpeed     = 2f;
    public float pulseDepth     = 0.12f;

    sealed class Gear
    {
        public Transform             t;
        public MeshRenderer          mr;
        public MaterialPropertyBlock mpb;
        public Color                 baseCol;    // its printed colour (flashed toward flashColor on tick)
        public float      radius;     // world radius (the cog's outer radius)
        public float      baseAngle;  // starting azimuth, deg
        public float      dir;        // +1 / -1 (neighbours counter-rotate)
        public float      degPerBeat; // its own per-beat step (bigger cog → smaller)
        public Quaternion tilt;       // mounts the cog flat on its face (axis = face normal)
        public float      phase;      // per-cog beat offset so ticks aren't lockstep
        public float      turnsAtFlip;// beats consumed before the last direction shift (angle rebase)
        public Vector3    basePos;    // local rest position (hover oscillates around it)
        public Vector3    hoverAxis;  // face normal, in local space (hover direction)
        public float      hoverPhase; // per-cog hover offset
    }

    readonly List<Gear>   _gears = new();
    MeshRenderer          _wireMr;
    MaterialPropertyBlock _wireMpb;
    Mesh                  _wireMesh;

    // Scan sweep
    Transform             _scan;
    MeshRenderer          _scanMr;
    MaterialPropertyBlock _scanMpb;
    Mesh                  _scanMesh;
    float                 _scanMinY, _scanMaxY;

    // Circuit runners
    readonly List<Transform> _runners = new();
    Vector3[]                _loop;        // base-perimeter waypoints (local)
    float                    _loopLength;
    float                    _runnerWorldSize;
    float                    _runnerWorldSpeed;

    // Gear shift
    int   _lastShiftBlock;
    float _shiftFlashAt = -999f;

    Color _lineColor = Color.white;
    Color _metalColor = Color.gray;
    float _born;
    bool  _built, _retiring;
    float _witherStart;
    float _refCell = 1f;   // cellSize cached for hover amplitude

    static readonly int _ColorID        = Shader.PropertyToID("_Color");
    static readonly int _BaseColorID    = Shader.PropertyToID("_BaseColor");
    static readonly int _OutlineColorID = Shader.PropertyToID("_OutlineColor");
    static readonly int _OutlineWidthID = Shader.PropertyToID("_OutlineWidth");

    // cellCentersWorld = world center of each grid cell of the piece. lineColor
    // draws the structure; metalColor tints the gears.
    public void Build(Vector3[] cellCentersWorld, float cellSize, Color lineColor, Color metalColor)
    {
        Clear();
        _lineColor  = lineColor;
        _metalColor = metalColor;
        _refCell    = cellSize;
        _born       = Time.time;
        _retiring   = false;

        if (cellCentersWorld == null || cellCentersWorld.Length == 0) { _built = true; return; }

        Vector3 mn = cellCentersWorld[0], mx = cellCentersWorld[0];
        for (int i = 1; i < cellCentersWorld.Length; i++)
        {
            mn = Vector3.Min(mn, cellCentersWorld[i]);
            mx = Vector3.Max(mx, cellCentersWorld[i]);
        }
        float half = cellSize * 0.5f;
        Vector3 center = (mn + mx) * 0.5f;
        transform.SetPositionAndRotation(center, Quaternion.identity);
        transform.localScale = Vector3.one;

        BuildWireframe(cellCentersWorld, center, cellSize, half, mn, mx);
        if (showGears) BuildGears(cellCentersWorld, center, cellSize, half);

        Vector3 mnL = (mn - center) - new Vector3(half, half, half);
        Vector3 mxL = (mx - center) + new Vector3(half, half, half);
        if (showScan) BuildScan(mnL, mxL);
        if (showRunners) BuildRunners(mnL, mxL, cellSize);

        _lastShiftBlock = 0;
        _shiftFlashAt   = -999f;

        _built = true;
    }

    public void Retire()
    {
        if (_retiring) return;
        _retiring    = true;
        _witherStart = Time.time;
        if (!_built) Destroy(gameObject);
    }

    // ── Combat-ripple replay: re-assemble in sync with the block sprout ──────
    void OnEnable()  { SynergyVisualFX.OnReplayGrowIn += HandleReplay; }
    void OnDisable() { SynergyVisualFX.OnReplayGrowIn -= HandleReplay; }

    void HandleReplay(System.Func<Vector3, float> delayFor)
    {
        if (!_built || _retiring) return;
        float d = delayFor != null ? Mathf.Max(0f, delayFor(transform.position)) : 0f;
        _born = Time.time + d;   // fade/settle/gears collapse, then re-grow when the ripple arrives
    }

    void Update()
    {
        if (!_built) return;

        float fade;
        if (_retiring)
        {
            float wt = witherDuration > 1e-4f ? (Time.time - _witherStart) / witherDuration : 1f;
            fade = 1f - Mathf.Clamp01(wt);
            if (fade <= 0f) { Destroy(gameObject); return; }
        }
        else
        {
            fade = fadeInDuration > 1e-4f ? Mathf.Clamp01((Time.time - _born) / fadeInDuration) : 1f;
        }

        float now = Time.time;

        // Frame: snap-into-place settle + calibration breathing + alpha fade.
        float settle = _retiring ? 1f : Mathf.Lerp(0.92f, 1f, EaseOutBack(fade));
        transform.localScale = Vector3.one * settle;

        // Frame breathes ON THE BEAT (musical) rather than a free sine.
        float beatsGlobal = (now - _born) * (Mathf.Max(0f, bpm) / 60f);
        float beatPhase   = beatsGlobal - Mathf.Floor(beatsGlobal);   // 0..1 within the beat
        float beatEnv     = Mathf.Clamp01(1f - beatPhase / 0.4f);     // peak on the beat, decay
        float pulse       = 1f + pulseDepth * beatEnv;
        Color line = _lineColor * pulse; line.a = _lineColor.a * fade;
        if (_wireMr != null) SetCol(_wireMr, _wireMpb, line);

        // Gear shift: every shiftEveryBeats the whole clockwork reverses in one
        // synchronized clunk. Each gear's angle is REBASED at the flip (baseAngle
        // absorbs the turns already made) so the reversal never pops.
        if (shiftEveryBeats > 0 && !_retiring)
        {
            int block = Mathf.FloorToInt(beatsGlobal / shiftEveryBeats);
            if (block != _lastShiftBlock)
            {
                _lastShiftBlock = block;
                _shiftFlashAt   = now;
                for (int i = 0; i < _gears.Count; i++)
                {
                    var g = _gears[i];
                    float turnsNow = GearTurns(beatsGlobal, g.phase);
                    g.baseAngle  += (turnsNow - g.turnsAtFlip) * g.degPerBeat * g.dir;
                    g.turnsAtFlip = turnsNow;
                    g.dir         = -g.dir;
                }
            }
        }
        float shiftFlash = Mathf.Clamp01(1f - (now - _shiftFlashAt) / 0.35f);

        // Gears: BPM-locked STEPPED ratchet + a silkscreen ink-stamp flash on each
        // tick. Opaque, so appear/disappear is via scale.
        for (int i = 0; i < _gears.Count; i++)
        {
            var g = _gears[i];
            if (g.t == null) continue;
            g.t.localScale = Vector3.one * (g.radius * fade);

            // Optional hover along the face normal (0 amplitude = cogs stay put —
            // they ring the block without any up/down drift).
            if (holographic && hoverAmplitudeFrac > 1e-4f)
            {
                float hover = Mathf.Sin(now * hoverSpeed * Mathf.PI * 2f + g.hoverPhase)
                            * (hoverAmplitudeFrac * _refCell);
                g.t.localPosition = g.basePos + g.hoverAxis * hover;
            }

            float beats = Mathf.Max(0f, beatsGlobal - g.phase);
            float frac  = beats - Mathf.Floor(beats);
            float turns = GearTurns(beatsGlobal, g.phase);

            float ang = g.baseAngle + (turns - g.turnsAtFlip) * g.degPerBeat * g.dir;
            g.t.localRotation = g.tilt * Quaternion.Euler(0f, ang, 0f);

            // Ink-stamp: flash toward flashColor right after the tick — harder on
            // the synchronized gear-shift clunk — then settle. Holo cogs also get
            // a subtle opacity flicker (unstable projection) baked into the tint.
            if (g.mr != null)
            {
                float flash = Mathf.Max(beatFlash * Mathf.Clamp01(1f - frac / 0.3f),
                                        beatFlash * 1.6f * shiftFlash);
                Color col = Color.Lerp(g.baseCol, flashColor, Mathf.Clamp01(flash));
                if (holographic)
                {
                    float flick = 1f - flickerAmount * (0.5f + 0.5f * Mathf.Sin(now * 17f + g.hoverPhase * 3f));
                    col.a = g.baseCol.a * flick * fade;
                }
                g.mpb.SetColor(_BaseColorID, col);
                g.mpb.SetColor(_ColorID, col);
                g.mr.SetPropertyBlock(g.mpb);
            }
        }

        // Scan sweep: a bright cross-section outline rides bottom → top every
        // scanInterval, alpha enveloped so it fades in/out at the ends.
        if (_scan != null)
        {
            float cycle = Mathf.Repeat(now - _born, Mathf.Max(1f, scanInterval));
            bool sweeping = cycle < scanDuration && !_retiring && fade >= 0.999f;
            float k = sweeping ? cycle / Mathf.Max(0.01f, scanDuration) : 0f;
            _scan.localPosition = new Vector3(0f, Mathf.Lerp(_scanMinY, _scanMaxY, k), 0f);

            Color sc = _lineColor * 1.6f;
            sc.a = (sweeping ? Mathf.Sin(k * Mathf.PI) : 0f) * fade;
            if (_scanMr != null) SetCol(_scanMr, _scanMpb, sc);
        }

        // Circuit runners: constant-speed nodes tracing the base perimeter.
        if (_runners.Count > 0 && _loop != null && _loopLength > 1e-4f)
        {
            for (int i = 0; i < _runners.Count; i++)
            {
                var r = _runners[i];
                if (r == null) continue;
                float d = Mathf.Repeat((now - _born) * _runnerWorldSpeed + i * (_loopLength / _runners.Count), _loopLength);
                r.localPosition = PointOnLoop(d);
                r.localScale    = Vector3.one * (_runnerWorldSize * fade);
            }
        }
    }

    // Beats → accumulated turns under the current ratchet mode (shared by the
    // rotation and the shift-rebase so both always agree).
    float GearTurns(float beatsGlobal, float phase)
    {
        float beats = Mathf.Max(0f, beatsGlobal - phase);
        if (!steppedRotation) return beats;
        int   step = Mathf.FloorToInt(beats);
        float frac = beats - step;
        float snap = snapFraction > 1e-3f ? Mathf.Clamp01(frac / snapFraction) : 1f;
        return step + EaseOutBack(snap);   // quick snap, slight clack, then hold
    }

    // ── Wireframe (cube edges + brackets + ticks) as ONE Lines mesh ──────────
    void BuildWireframe(Vector3[] cells, Vector3 center, float cellSize, float half, Vector3 mnW, Vector3 mxW)
    {
        var v   = new List<Vector3>();
        var idx = new List<int>();

        for (int c = 0; c < cells.Length; c++)
            AddCubeEdges(v, idx, cells[c] - center, half);

        Vector3 mn = (mnW - center) - new Vector3(half, half, half);
        Vector3 mx = (mxW - center) + new Vector3(half, half, half);

        if (showBrackets)
            AddCornerBrackets(v, idx, mn, mx, cellSize * bracketFrac);

        if (showTicks && tickCount > 0)
        {
            float tick = cellSize * 0.1f;
            for (int k = 0; k <= tickCount; k++)
            {
                float xt = Mathf.Lerp(mn.x, mx.x, (float)k / tickCount);
                Vector3 p = new Vector3(xt, mn.y, mn.z);
                AddSeg(v, idx, p, p + Vector3.down * tick);
            }
        }

        _wireMesh = new Mesh { name = "OrderWire" };
        _wireMesh.SetVertices(v);
        _wireMesh.SetIndices(idx.ToArray(), MeshTopology.Lines, 0);
        _wireMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);

        var go = new GameObject("Wireframe");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = _wireMesh;
        _wireMr = go.AddComponent<MeshRenderer>();
        _wireMr.sharedMaterial = GetLineMaterial();
        ConfigRenderer(_wireMr);
        _wireMpb = new MaterialPropertyBlock();
    }

    // 6 axis-aligned faces: grid offset + world normal.
    static readonly (Vector3Int g, Vector3 n)[] _faces =
    {
        (Vector3Int.up,      Vector3.up),      (Vector3Int.down,  Vector3.down),
        (Vector3Int.left,    Vector3.left),    (Vector3Int.right, Vector3.right),
        (Vector3Int.forward, Vector3.forward), (Vector3Int.back,  Vector3.back),
    };

    // ── Metal cogs bolted onto the structure's OUTER faces ───────────────────
    // For every cell × face that isn't shared with a same-piece neighbour, a
    // random roll attaches a varying-size cog flush on that face, spinning around
    // the FACE NORMAL — so top cogs spin flat, side cogs spin in a vertical plane,
    // etc. (rotation no longer lives in a single plane).
    void BuildGears(Vector3[] cells, Vector3 center, float cellSize, float half)
    {
        Mesh gearMesh    = GetSolidGearMesh(Mathf.Clamp(gearTeeth, 6, 24));
        // Holographic cogs: translucent additive fill (see-through, glowing) with
        // the bright edge outline reading as the hologram's rim. The solid-metal
        // fill is kept as the non-holo fallback.
        Material fill    = holographic ? GetHoloMaterial() : GetGearFillMaterial();
        Material outline = GetGearOutlineMaterial();
        if (outline != null)
        {
            // Holo rim glows in the line color, not black ink.
            outline.SetColor(_OutlineColorID, holographic ? _lineColor : outlineColor);
            outline.SetFloat(_OutlineWidthID, Mathf.Max(0f, outlineWidth));
        }
        Material[] gearMats = outline != null ? new[] { fill, outline } : new[] { fill };

        // Quantize cell centers to integer keys so we can test face adjacency.
        var occupied = new HashSet<Vector3Int>();
        var keys     = new Vector3Int[cells.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            keys[i] = new Vector3Int(
                Mathf.RoundToInt(cells[i].x / cellSize),
                Mathf.RoundToInt(cells[i].y / cellSize),
                Mathf.RoundToInt(cells[i].z / cellSize));
            occupied.Add(keys[i]);
        }

        float refR = cellSize * Mathf.Max(0.01f, gearSizeMinFrac);
        int cap = Mathf.Max(1, maxGears);
        int made = 0, gi = 0;

        for (int i = 0; i < cells.Length && made < cap; i++)
        {
            Vector3 cl = cells[i] - center;   // local cell center

            for (int f = 0; f < 6 && made < cap; f++, gi++)
            {
                var (gdir, n) = _faces[f];
                if (sideFacesOnly && Mathf.Abs(n.y) > 0.5f) continue;   // skip top/bottom → don't cover the road
                if (occupied.Contains(keys[i] + gdir)) continue;        // interior face → skip
                if (Hash01(gi * 131 + 5) > faceCoverChance) continue;   // random coverage

                float radius = cellSize * Mathf.Lerp(gearSizeMinFrac, gearSizeMaxFrac, Hash01(gi * 977 + 3));
                // Float OFF the face (holo projections hover, they don't hug the
                // block) — extra floatDistanceFrac on top of the flush offset.
                float off    = half + cellSize * (faceOffsetFrac + floatDistanceFrac) + 0.12f * radius;
                Vector3 pos  = cl + n * off;

                // Orient so the cog's axis points along the face normal; it then
                // spins around that normal (Update applies tilt × spin-around-up).
                Quaternion faceOrient = Quaternion.FromToRotation(Vector3.up, n);

                var go = new GameObject("Gear");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = pos;
                go.transform.localScale    = Vector3.zero;   // grows in via fade

                go.AddComponent<MeshFilter>().sharedMesh = gearMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = gearMats;            // [holo/silkscreen fill, bright outline]
                ConfigRenderer(mr);

                Color baseCol = holographic
                    ? Color.Lerp(_lineColor, Color.white, 0.15f)   // holo cogs glow in the frame's line color
                    : _metalColor * Mathf.Lerp(0.82f, 1.12f, Hash01(gi * 967 + 8));
                baseCol.a = holographic ? holoAlpha : 1f;
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor(_BaseColorID, baseCol);
                mpb.SetColor(_ColorID, baseCol);
                mr.SetPropertyBlock(mpb);

                _gears.Add(new Gear
                {
                    t          = go.transform,
                    mr         = mr,
                    mpb        = mpb,
                    baseCol    = baseCol,
                    radius     = radius,
                    baseAngle  = Hash01(gi * 53 + 11) * 360f,
                    dir        = (gi & 1) == 0 ? 1f : -1f,
                    degPerBeat = degreesPerBeat * (refR / Mathf.Max(0.001f, radius)),   // bigger cog → smaller step
                    tilt       = faceOrient,
                    phase      = Hash01(gi * 97 + 13) * phaseJitter,
                    basePos    = pos,
                    hoverAxis  = n,
                    hoverPhase = Hash01(gi * 613 + 29) * Mathf.PI * 2f,
                });
                made++;
            }
        }
    }

    // ── Scan sweep: bounding-box cross-section outline, animated in Y ────────
    void BuildScan(Vector3 mnL, Vector3 mxL)
    {
        _scanMinY = mnL.y;
        _scanMaxY = mxL.y;

        var v   = new List<Vector3>();
        var idx = new List<int>();
        Vector3 a = new Vector3(mnL.x, 0f, mnL.z);
        Vector3 b = new Vector3(mxL.x, 0f, mnL.z);
        Vector3 c = new Vector3(mxL.x, 0f, mxL.z);
        Vector3 d = new Vector3(mnL.x, 0f, mxL.z);
        AddSeg(v, idx, a, b); AddSeg(v, idx, b, c);
        AddSeg(v, idx, c, d); AddSeg(v, idx, d, a);

        _scanMesh = new Mesh { name = "OrderScan" };
        _scanMesh.SetVertices(v);
        _scanMesh.SetIndices(idx.ToArray(), MeshTopology.Lines, 0);
        _scanMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);

        var go = new GameObject("Scan");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, _scanMinY, 0f);
        _scan = go.transform;
        go.AddComponent<MeshFilter>().sharedMesh = _scanMesh;
        _scanMr = go.AddComponent<MeshRenderer>();
        _scanMr.sharedMaterial = GetLineMaterial();
        ConfigRenderer(_scanMr);
        _scanMpb = new MaterialPropertyBlock();
    }

    // ── Circuit runners: nodes tracing the base perimeter loop ───────────────
    void BuildRunners(Vector3 mnL, Vector3 mxL, float cellSize)
    {
        _loop = new[]
        {
            new Vector3(mnL.x, mnL.y, mnL.z),
            new Vector3(mxL.x, mnL.y, mnL.z),
            new Vector3(mxL.x, mnL.y, mxL.z),
            new Vector3(mnL.x, mnL.y, mxL.z),
        };
        _loopLength = 0f;
        for (int i = 0; i < _loop.Length; i++)
            _loopLength += Vector3.Distance(_loop[i], _loop[(i + 1) % _loop.Length]);

        _runnerWorldSize  = cellSize * Mathf.Max(0.01f, runnerSizeFrac);
        _runnerWorldSpeed = cellSize * Mathf.Max(0.01f, runnerSpeed);

        Mesh node = GetNodeMesh();
        int count = Mathf.Clamp(runnerCount, 1, 4);
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("Runner");
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.zero;
            go.AddComponent<MeshFilter>().sharedMesh = node;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = GetGearFillMaterial();
            ConfigRenderer(mr);
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor(_BaseColorID, flashColor);
            mr.SetPropertyBlock(mpb);
            _runners.Add(go.transform);
        }
    }

    // Distance along the perimeter → local position on the loop.
    Vector3 PointOnLoop(float d)
    {
        for (int i = 0; i < _loop.Length; i++)
        {
            Vector3 p0 = _loop[i], p1 = _loop[(i + 1) % _loop.Length];
            float seg = Vector3.Distance(p0, p1);
            if (d <= seg || seg < 1e-5f) return Vector3.Lerp(p0, p1, seg < 1e-5f ? 0f : d / seg);
            d -= seg;
        }
        return _loop[0];
    }

    // ── line-geometry helpers ────────────────────────────────────────────────
    static void AddSeg(List<Vector3> v, List<int> idx, Vector3 a, Vector3 b)
    {
        int n = v.Count; v.Add(a); v.Add(b); idx.Add(n); idx.Add(n + 1);
    }

    static void AddCubeEdges(List<Vector3> v, List<int> idx, Vector3 c, float h)
    {
        Vector3 Corner(int i) => c + new Vector3((i & 1) != 0 ? h : -h,
                                                 (i & 2) != 0 ? h : -h,
                                                 (i & 4) != 0 ? h : -h);
        int[,] e =
        {
            {0,1},{2,3},{4,5},{6,7},   // x edges
            {0,2},{1,3},{4,6},{5,7},   // y edges
            {0,4},{1,5},{2,6},{3,7},   // z edges
        };
        for (int k = 0; k < 12; k++) AddSeg(v, idx, Corner(e[k, 0]), Corner(e[k, 1]));
    }

    static void AddCornerBrackets(List<Vector3> v, List<int> idx, Vector3 mn, Vector3 mx, float arm)
    {
        for (int sx = 0; sx < 2; sx++)
        for (int sy = 0; sy < 2; sy++)
        for (int sz = 0; sz < 2; sz++)
        {
            Vector3 p = new Vector3(sx == 0 ? mn.x : mx.x, sy == 0 ? mn.y : mx.y, sz == 0 ? mn.z : mx.z);
            AddSeg(v, idx, p, p + new Vector3(sx == 0 ? arm : -arm, 0f, 0f));
            AddSeg(v, idx, p, p + new Vector3(0f, sy == 0 ? arm : -arm, 0f));
            AddSeg(v, idx, p, p + new Vector3(0f, 0f, sz == 0 ? arm : -arm));
        }
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float xm = x - 1f;
        return 1f + c3 * xm * xm * xm + c1 * xm * xm;
    }

    static void SetCol(MeshRenderer mr, MaterialPropertyBlock mpb, Color c)
    {
        if (mr == null) return;
        mr.GetPropertyBlock(mpb);
        mpb.SetColor(_ColorID, c);
        mpb.SetColor(_BaseColorID, c);
        mr.SetPropertyBlock(mpb);
    }

    static void ConfigRenderer(MeshRenderer mr)
    {
        mr.shadowCastingMode    = ShadowCastingMode.Off;
        mr.receiveShadows       = false;
        mr.lightProbeUsage      = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
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

    // ── Teardown ─────────────────────────────────────────────────────────────
    void OnDestroy() => Clear();

    void Clear()
    {
        _built = false;
        for (int i = 0; i < _gears.Count; i++)
            if (_gears[i] != null && _gears[i].t != null) Destroy(_gears[i].t.gameObject);
        _gears.Clear();

        for (int i = 0; i < _runners.Count; i++)
            if (_runners[i] != null) Destroy(_runners[i].gameObject);
        _runners.Clear();

        if (_scan != null)     { Destroy(_scan.gameObject); _scan = null; _scanMr = null; }
        if (_scanMesh != null) { Destroy(_scanMesh); _scanMesh = null; }

        if (_wireMr != null)   { Destroy(_wireMr.gameObject); _wireMr = null; }
        if (_wireMesh != null) { Destroy(_wireMesh); _wireMesh = null; }   // per-rig mesh (gear/node meshes are shared, never destroyed)
    }

    // ── Shared (static) assets ───────────────────────────────────────────────
    static Material _lineMat;
    static Material _fillMat;
    static Material _holoMat;
    static Material _outlineMat;
    static readonly Dictionary<int, Mesh> _gearMeshes = new();

    static Material GetLineMaterial()
    {
        if (_lineMat != null) return _lineMat;
        var sh = Shader.Find("Sprites/Default");          // vertex-color, renders MeshTopology.Lines
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        _lineMat = new Material(sh) { hideFlags = HideFlags.DontSave };
        return _lineMat;
    }

    // Flat silkscreen fill (matches the cubes): flat colour + paper grain +
    // emulated-light posterised shade, double-sided. Per-cog colour via MPB.
    static Material GetGearFillMaterial()
    {
        if (_fillMat != null) return _fillMat;
        var sh = Shader.Find("GeoWorld/SilkscreenFlat");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        _fillMat = new Material(sh) { hideFlags = HideFlags.DontSave };
        return _fillMat;
    }

    // Translucent ADDITIVE fill for holographic cogs: see-through, glowing, so
    // overlapping cogs read as light rather than solid metal. Colour + alpha come
    // per-cog from MPB (_BaseColor / _Color). Configured on URP/Unlit when we can,
    // else Sprites/Default (already alpha-blended). Additive means alpha scales
    // brightness → the flicker reads as an unstable projection.
    static Material GetHoloMaterial()
    {
        if (_holoMat != null) return _holoMat;
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh != null)
        {
            _holoMat = new Material(sh) { hideFlags = HideFlags.DontSave };
            _holoMat.SetFloat("_Surface", 1f);          // transparent
            _holoMat.SetFloat("_Blend", 1f);            // additive
            _holoMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _holoMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            _holoMat.SetFloat("_ZWrite", 0f);
            _holoMat.SetFloat("_Cull", 0f);             // both sides
            _holoMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            _holoMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _holoMat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        }
        else
        {
            sh = Shader.Find("Sprites/Default");
            _holoMat = new Material(sh) { hideFlags = HideFlags.DontSave };
        }
        return _holoMat;
    }

    // The same bold inverse-hull cartoon outline the blocks wear (2nd slot).
    // Returns null if the shader is missing → cogs render fill-only.
    static Material GetGearOutlineMaterial()
    {
        if (_outlineMat != null) return _outlineMat;
        var sh = Shader.Find("GeoWorld/BlockOutline");
        if (sh == null) return null;
        _outlineMat = new Material(sh) { hideFlags = HideFlags.DontSave };
        return _outlineMat;
    }

    // Small octahedron (unit half-extent) — the circuit-runner node. Reads as a
    // point of energy without needing emissive materials.
    static Mesh _nodeMesh;
    static Mesh GetNodeMesh()
    {
        if (_nodeMesh != null) return _nodeMesh;

        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var tris  = new List<int>();

        Vector3 top = Vector3.up, bot = Vector3.down;
        Vector3[] eq =
        {
            Vector3.right, Vector3.forward, Vector3.left, Vector3.back,
        };

        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            int s = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            norms.Add(n); norms.Add(n); norms.Add(n);
            tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
        }

        for (int i = 0; i < 4; i++)
        {
            Vector3 a = eq[i], b = eq[(i + 1) % 4];
            Tri(top, a, b);
            Tri(bot, b, a);
        }

        _nodeMesh = new Mesh { name = "OrderRunnerNode" };
        _nodeMesh.SetVertices(verts);
        _nodeMesh.SetNormals(norms);
        _nodeMesh.SetTriangles(tris, 0);
        _nodeMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
        return _nodeMesh;
    }

    // Solid extruded gear (unit outer radius = 1) lying flat in the XZ plane,
    // thickness along Y, with a center hole. Authored normals (top +Y, bottom
    // −Y, walls radial) so the metal reads correctly. Cached per tooth count.
    static Mesh GetSolidGearMesh(int teeth)
    {
        if (_gearMeshes.TryGetValue(teeth, out var cached) && cached != null) return cached;

        const float rOuter = 1f, rRoot = 0.80f, rHole = 0.34f, halfT = 0.12f;
        float pitch = Mathf.PI * 2f / teeth;

        // Ring samples (angle, radius) tracing square-ish teeth.
        var ang = new List<float>();
        var rad = new List<float>();
        for (int t = 0; t < teeth; t++)
        {
            float b = t * pitch;
            ang.Add(b + pitch * 0.00f); rad.Add(rRoot);
            ang.Add(b + pitch * 0.12f); rad.Add(rOuter);
            ang.Add(b + pitch * 0.38f); rad.Add(rOuter);
            ang.Add(b + pitch * 0.50f); rad.Add(rRoot);
            ang.Add(b + pitch * 0.90f); rad.Add(rRoot);
        }
        int M = ang.Count;

        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var tris  = new List<int>();

        for (int i = 0; i < M; i++)
        {
            int j = (i + 1) % M;

            Vector3 oUp_i = Polar(ang[i], rad[i],  halfT), oUp_j = Polar(ang[j], rad[j],  halfT);
            Vector3 oDn_i = Polar(ang[i], rad[i], -halfT), oDn_j = Polar(ang[j], rad[j], -halfT);
            Vector3 hUp_i = Polar(ang[i], rHole,   halfT), hUp_j = Polar(ang[j], rHole,   halfT);
            Vector3 hDn_i = Polar(ang[i], rHole,  -halfT), hDn_j = Polar(ang[j], rHole,  -halfT);

            // Top face (normal +Y), bottom face (normal −Y).
            AddQuad(verts, norms, tris, oUp_i, oUp_j, hUp_j, hUp_i, Vector3.up);
            AddQuad(verts, norms, tris, hDn_i, hDn_j, oDn_j, oDn_i, Vector3.down);

            // Outer rim wall (normal radial out) and hole wall (radial in).
            Vector3 outN = AvgRadial(ang[i], ang[j],  1f);
            Vector3 inN  = AvgRadial(ang[i], ang[j], -1f);
            AddQuad(verts, norms, tris, oUp_i, oDn_i, oDn_j, oUp_j, outN);
            AddQuad(verts, norms, tris, hUp_j, hDn_j, hDn_i, hUp_i, inN);
        }

        var m = new Mesh { name = $"OrderMetalGear_{teeth}" };
        m.SetVertices(verts);
        m.SetNormals(norms);
        m.SetTriangles(tris, 0);
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
        _gearMeshes[teeth] = m;
        return m;
    }

    static void AddQuad(List<Vector3> v, List<Vector3> n, List<int> tri,
                        Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
    {
        int s = v.Count;
        v.Add(a); v.Add(b); v.Add(c); v.Add(d);
        n.Add(normal); n.Add(normal); n.Add(normal); n.Add(normal);
        tri.Add(s); tri.Add(s + 1); tri.Add(s + 2);
        tri.Add(s); tri.Add(s + 2); tri.Add(s + 3);
    }

    static Vector3 AvgRadial(float a0, float a1, float sign)
    {
        float a = (a0 + a1) * 0.5f;
        return new Vector3(Mathf.Cos(a) * sign, 0f, Mathf.Sin(a) * sign).normalized;
    }

    static Vector3 Polar(float a, float r, float y) => new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
}
