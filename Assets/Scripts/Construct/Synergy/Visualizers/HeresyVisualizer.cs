using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 异端 (Heresy) — "defiance" visualizer.
//
// Where every other synergy celebrates the grid's order, Heresy is the one that
// bends it: a BROKEN halo — arc segments with deliberate gaps — orbits the piece
// on a canted axis, turning the WRONG way (counter to Order's clockwork), while
// small inverted shards (point-down pyramids) hover over the claimed cells,
// drifting smoothly and then glitch-snapping a few degrees at irregular
// intervals. Dark ink-purple base with a brief venom-bright flash on each
// glitch. Deliberately sparse and slow so it unsettles without shouting.
//
// RECONCILER (read before editing) — same contract as OrderArchitectVisualizer /
// ExplorationVoyageVisualizer:
//   • WE own each rig in a pieceId → HeresyRig-GameObject dictionary.
//   • OnPieceClaimed / OnPieceReleased just call Reconcile(), which reads the
//     LIVE claim state and makes the world match.
//   • OnPieceClaimed returns null so the dispatcher never Destroys our rigs.
// Rigs are FREE-STANDING world-space objects, immune to block rotation/GrowIn.
[CreateAssetMenu(
    menuName = "GeoWorld/Synergy/Visualizers/Heresy Defiance",
    fileName = "HeresyVisualizer")]
public class HeresyVisualizer : SynergyVisualizer
{
    [Header("Color")]
    [Tooltip("Tint the halo lines with the synergy's theme color (blended toward white). If off, uses Line Color below.")]
    public bool useThemeColor = true;

    [Tooltip("How far the theme color is blended toward white for the halo (0 = pure theme, 1 = white).")]
    [Range(0f, 1f)] public float themeWhiten = 0.25f;

    [Tooltip("Halo color when 'Use Theme Color' is off.")]
    public Color lineColor = new Color(0.75f, 0.45f, 1f, 0.9f);

    [Tooltip("Shard body color — kept dark so the silhouette (not brightness) carries the identity.")]
    public Color shardColor = new Color(0.24f, 0.10f, 0.32f, 1f);

    [Tooltip("Color a shard flashes toward on each glitch-snap.")]
    public Color glitchFlashColor = new Color(0.85f, 0.40f, 1f, 1f);

    [Header("Broken halo")]
    public bool showHalo = true;

    [Tooltip("Halo radius as a multiple of the piece's bounding radius.")]
    [Range(0.8f, 2f)] public float haloRadiusMul = 1.15f;

    [Tooltip("Tilt of the halo's spin axis away from vertical, in degrees — the 'wrongness' cue.")]
    [Range(0f, 45f)] public float haloTiltDeg = 18f;

    [Tooltip("Number of arc segments the halo is broken into.")]
    [Range(2, 8)] public int haloSegments = 5;

    [Tooltip("Fraction of each segment slot that is drawn (rest is gap).")]
    [Range(0.2f, 0.95f)] public float haloFill = 0.62f;

    [Tooltip("Degrees per second the halo turns. NEGATIVE = counter to Order's gears (the point).")]
    public float haloSpinSpeed = -9f;

    [Header("Inverted shards")]
    public bool showShards = true;

    [Tooltip("Chance each top-exposed cell hosts a shard.")]
    [Range(0f, 1f)] public float shardChance = 0.65f;

    [Tooltip("Max shards per claimed piece.")]
    public int maxShards = 6;

    [Tooltip("Shard size (base half-width) in cell-size units.")]
    [Range(0.05f, 0.4f)] public float shardSizeFrac = 0.16f;

    [Tooltip("Hover height above the top face, in cell-size units.")]
    [Range(0.1f, 1f)] public float shardHoverFrac = 0.42f;

    [Tooltip("Smooth drift rotation, degrees per second.")]
    public float shardDriftSpeed = 16f;

    [Tooltip("Average seconds between glitch-snaps (per shard, jittered).")]
    public float glitchInterval = 3.2f;

    [Tooltip("Degrees a shard jumps on each glitch-snap.")]
    public float glitchAngle = 32f;

    [Tooltip("How hard a shard flashes toward the glitch color on each snap (0 = no flash).")]
    [Range(0f, 1f)] public float glitchFlash = 0.5f;

    [Tooltip("Gentle vertical bob of the shards (amplitude in cell-size units / speed).")]
    [Range(0f, 0.2f)] public float bobAmplitude = 0.05f;
    public float bobSpeed = 0.9f;

    [Header("Animation")]
    [Tooltip("Seconds for a rig to materialize.")]
    public float fadeInDuration = 0.5f;

    [Tooltip("Seconds to fade out a released rig.")]
    public float witherDuration = 0.3f;

    private struct Target
    {
        public Color line;
        public Color shard;
        public Color flash;
    }

    [System.NonSerialized] private Dictionary<int, GameObject> _rigs;
    [System.NonSerialized] private Dictionary<int, Target>     _desired;
    [System.NonSerialized] private List<int>                   _prune;

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

    private void Reconcile()
    {
        if (_rigs == null) OnEnable();

        var evaluator = SynergyEvaluator.Instance;
        var grid      = GridSystem.instance;
        if (evaluator == null || grid == null) return;

        _desired.Clear();
        var actives = evaluator.Actives;
        for (int i = 0; i < actives.Count; i++)
        {
            var a = actives[i];
            if (a?.rule == null || a.rule.visualizer != this || a.claimedPieces == null) continue;

            Color theme = BlockColorPalette.Get(a.rule.color);
            var target = new Target
            {
                line  = useThemeColor ? Color.Lerp(theme, Color.white, themeWhiten) : lineColor,
                shard = shardColor,
                flash = glitchFlashColor,
            };
            foreach (var p in a.claimedPieces)
                if (p != null) _desired[p.id] = target;
        }

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

        var go = new GameObject($"HeresyRig_{pieceId}");

        var rig = go.AddComponent<HeresyRig>();
        rig.showHalo        = showHalo;
        rig.haloRadiusMul   = haloRadiusMul;
        rig.haloTiltDeg     = haloTiltDeg;
        rig.haloSegments    = haloSegments;
        rig.haloFill        = haloFill;
        rig.haloSpinSpeed   = haloSpinSpeed;
        rig.showShards      = showShards;
        rig.shardChance     = shardChance;
        rig.maxShards       = maxShards;
        rig.shardSizeFrac   = shardSizeFrac;
        rig.shardHoverFrac  = shardHoverFrac;
        rig.shardDriftSpeed = shardDriftSpeed;
        rig.glitchInterval  = glitchInterval;
        rig.glitchAngle     = glitchAngle;
        rig.glitchFlash     = glitchFlash;
        rig.bobAmplitude    = bobAmplitude;
        rig.bobSpeed        = bobSpeed;
        rig.fadeInDuration  = fadeInDuration;
        rig.witherDuration  = witherDuration;

        rig.Build(centers, cs, target.line, target.shard, target.flash);

        _rigs[pieceId] = go;
    }

    private static void RetireRig(GameObject go)
    {
        if (go == null) return;
        if (go.TryGetComponent<HeresyRig>(out var rig)) rig.Retire();
        else Object.Destroy(go);
    }
}

// One defiance rig: broken halo (Lines mesh on a tilted, reverse-spinning
// pivot) + hovering inverted-pyramid shards that drift and glitch-snap.
// Free-standing world object; Build() draws, Retire() fades then self-destroys.
[DisallowMultipleComponent]
public class HeresyRig : MonoBehaviour
{
    public bool  showHalo      = true;
    public float haloRadiusMul = 1.15f;
    public float haloTiltDeg   = 18f;
    public int   haloSegments  = 5;
    public float haloFill      = 0.62f;
    public float haloSpinSpeed = -9f;

    public bool  showShards      = true;
    public float shardChance     = 0.65f;
    public int   maxShards       = 6;
    public float shardSizeFrac   = 0.16f;
    public float shardHoverFrac  = 0.42f;
    public float shardDriftSpeed = 16f;
    public float glitchInterval  = 3.2f;
    public float glitchAngle     = 32f;
    public float glitchFlash     = 0.5f;
    public float bobAmplitude    = 0.05f;
    public float bobSpeed        = 0.9f;

    public float fadeInDuration = 0.5f;
    public float witherDuration = 0.3f;

    sealed class Shard
    {
        public Transform             t;
        public MeshRenderer          mr;
        public MaterialPropertyBlock mpb;
        public Color                 baseCol;
        public Vector3 basePos;      // local hover position (bob oscillates around it)
        public float   size;         // world scale
        public float   drift;        // accumulated smooth rotation (deg)
        public float   snapped;      // accumulated glitch-snap rotation (deg)
        public float   nextGlitch;   // absolute time of the next snap
        public float   lastGlitch;   // absolute time of the previous snap (drives the flash)
        public float   phase;        // bob phase offset
    }

    readonly List<Shard> _shards = new();
    Transform             _halo;      // tilted pivot; spins around its local up
    MeshRenderer          _haloMr;
    MaterialPropertyBlock _haloMpb;
    Mesh                  _haloMesh;

    Color _lineColor  = Color.white;
    Color _shardColor = Color.gray;
    Color _flashColor = Color.magenta;
    float _born;
    bool  _built, _retiring;
    float _witherStart;
    int   _seed;

    static readonly int _ColorID     = Shader.PropertyToID("_Color");
    static readonly int _BaseColorID = Shader.PropertyToID("_BaseColor");

    public void Build(Vector3[] cellCentersWorld, float cellSize, Color line, Color shard, Color flash)
    {
        Clear();
        _lineColor  = line;
        _shardColor = shard;
        _flashColor = flash;
        _born       = Time.time;
        _retiring   = false;
        _seed       = GetInstanceID();

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

        // Bounding radius of the footprint (in the horizontal plane, padded out
        // to the cube corners) — the halo circles just outside it.
        Vector3 ext = (mx - mn) * 0.5f + new Vector3(half, half, half);
        float boundR = Mathf.Sqrt(ext.x * ext.x + ext.z * ext.z);

        if (showHalo) BuildHalo(boundR * haloRadiusMul, cellSize);
        if (showShards) BuildShards(cellCentersWorld, center, cellSize, half);

        _built = true;
    }

    public void Retire()
    {
        if (_retiring) return;
        _retiring    = true;
        _witherStart = Time.time;
        if (!_built) Destroy(gameObject);
    }

    // ── Combat-ripple replay: re-materialize in sync with the block sprout ───
    void OnEnable()  { SynergyVisualFX.OnReplayGrowIn += HandleReplay; }
    void OnDisable() { SynergyVisualFX.OnReplayGrowIn -= HandleReplay; }

    void HandleReplay(System.Func<Vector3, float> delayFor)
    {
        if (!_built || _retiring) return;
        float d = delayFor != null ? Mathf.Max(0f, delayFor(transform.position)) : 0f;
        _born = Time.time + d;
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

        // Halo: reverse spin around its tilted axis + alpha fade.
        if (_halo != null)
        {
            _halo.localScale = Vector3.one * EaseOutBack(Mathf.Clamp01(fade));
            _halo.localRotation = Quaternion.AngleAxis(haloTiltDeg, Vector3.right)
                                * Quaternion.AngleAxis(now * haloSpinSpeed, Vector3.up);
            Color line = _lineColor;
            line.a = _lineColor.a * fade;
            if (_haloMr != null) SetCol(_haloMr, _haloMpb, line);
        }

        // Shards: smooth drift + glitch-snap + bob; flash decays after each snap.
        for (int i = 0; i < _shards.Count; i++)
        {
            var s = _shards[i];
            if (s.t == null) continue;

            s.t.localScale = Vector3.one * (s.size * fade);

            s.drift += shardDriftSpeed * Time.deltaTime;
            if (now >= s.nextGlitch && !_retiring)
            {
                float sign = Hash01(_seed + i * 71 + Mathf.FloorToInt(now * 10f)) < 0.5f ? -1f : 1f;
                s.snapped   += glitchAngle * sign;
                s.lastGlitch = now;
                s.nextGlitch = now + glitchInterval * Mathf.Lerp(0.6f, 1.5f, Hash01(_seed + i * 913 + Mathf.FloorToInt(now)));
            }
            s.t.localRotation = Quaternion.Euler(0f, s.drift + s.snapped, 0f);
            s.t.localPosition = s.basePos
                + Vector3.up * (Mathf.Sin(now * bobSpeed * Mathf.PI * 2f + s.phase) * bobAmplitude);

            if (s.mr != null)
            {
                float flash = glitchFlash * Mathf.Clamp01(1f - (now - s.lastGlitch) / 0.25f);
                s.mpb.SetColor(_BaseColorID, Color.Lerp(s.baseCol, _flashColor, flash));
                s.mr.SetPropertyBlock(s.mpb);
            }
        }
    }

    // ── Halo (broken arc segments) as ONE Lines mesh on a tilted pivot ───────
    void BuildHalo(float radius, float cellSize)
    {
        _halo = new GameObject("Halo").transform;
        _halo.SetParent(transform, false);
        _halo.localScale = Vector3.zero;   // grows in via fade

        var v   = new List<Vector3>();
        var idx = new List<int>();

        int   segs   = Mathf.Max(2, haloSegments);
        float slot   = Mathf.PI * 2f / segs;
        int   subdiv = 10;   // line subdivisions per drawn arc
        for (int sIdx = 0; sIdx < segs; sIdx++)
        {
            float a0 = sIdx * slot;
            float a1 = a0 + slot * Mathf.Clamp01(haloFill);
            for (int k = 0; k < subdiv; k++)
            {
                float t0 = Mathf.Lerp(a0, a1, k / (float)subdiv);
                float t1 = Mathf.Lerp(a0, a1, (k + 1) / (float)subdiv);
                AddSeg(v, idx,
                    new Vector3(Mathf.Cos(t0) * radius, 0f, Mathf.Sin(t0) * radius),
                    new Vector3(Mathf.Cos(t1) * radius, 0f, Mathf.Sin(t1) * radius));
            }
        }

        _haloMesh = new Mesh { name = "HeresyHalo" };
        _haloMesh.SetVertices(v);
        _haloMesh.SetIndices(idx.ToArray(), MeshTopology.Lines, 0);
        _haloMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);

        var go = new GameObject("Arcs");
        go.transform.SetParent(_halo, false);
        go.AddComponent<MeshFilter>().sharedMesh = _haloMesh;
        _haloMr = go.AddComponent<MeshRenderer>();
        _haloMr.sharedMaterial = GetLineMaterial();
        ConfigRenderer(_haloMr);
        _haloMpb = new MaterialPropertyBlock();
    }

    // ── Shards (inverted pyramids over top-exposed cells) ────────────────────
    void BuildShards(Vector3[] cells, Vector3 center, float cellSize, float half)
    {
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

        Mesh shardMesh   = GetShardMesh();
        Material fill    = GetFillMaterial();
        Material outline = GetOutlineMaterial();
        Material[] mats  = outline != null ? new[] { fill, outline } : new[] { fill };

        int cap = Mathf.Max(1, maxShards);
        int made = 0;

        for (int i = 0; i < cells.Length && made < cap; i++)
        {
            if (occupied.Contains(keys[i] + Vector3Int.up)) continue;          // buried → skip
            if (Hash01(_seed + i * 131 + 7) > shardChance) continue;           // random coverage

            Vector3 basePos = (cells[i] - center)
                + Vector3.up * (half + cellSize * shardHoverFrac);

            var go = new GameObject("Shard");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = basePos;
            go.transform.localScale    = Vector3.zero;   // grows in via fade

            go.AddComponent<MeshFilter>().sharedMesh = shardMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            ConfigRenderer(mr);

            Color baseCol = _shardColor * Mathf.Lerp(0.85f, 1.15f, Hash01(_seed + i * 977 + 3));
            baseCol.a = 1f;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor(_BaseColorID, baseCol);
            mr.SetPropertyBlock(mpb);

            _shards.Add(new Shard
            {
                t          = go.transform,
                mr         = mr,
                mpb        = mpb,
                baseCol    = baseCol,
                basePos    = basePos,
                size       = cellSize * shardSizeFrac,
                drift      = Hash01(_seed + i * 53) * 360f,
                snapped    = 0f,
                lastGlitch = -999f,
                nextGlitch = Time.time + glitchInterval * Mathf.Lerp(0.3f, 1.3f, Hash01(_seed + i * 97)),
                phase      = Hash01(_seed + i * 17) * Mathf.PI * 2f,
            });
            made++;
        }
    }

    // ── geometry helpers ─────────────────────────────────────────────────────
    static void AddSeg(List<Vector3> v, List<int> idx, Vector3 a, Vector3 b)
    {
        int n = v.Count; v.Add(a); v.Add(b); idx.Add(n); idx.Add(n + 1);
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
        for (int i = 0; i < _shards.Count; i++)
            if (_shards[i] != null && _shards[i].t != null) Destroy(_shards[i].t.gameObject);
        _shards.Clear();

        if (_haloMr != null)   { Destroy(_haloMr.gameObject); _haloMr = null; }
        if (_haloMesh != null) { Destroy(_haloMesh); _haloMesh = null; }   // per-rig mesh (shard mesh is shared)
        _halo = null;
    }

    // ── Shared (static) assets ───────────────────────────────────────────────
    static Material _lineMat;
    static Material _fillMat;
    static Material _outlineMat;
    static Mesh     _shardMesh;

    static Material GetLineMaterial()
    {
        if (_lineMat != null) return _lineMat;
        var sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        _lineMat = new Material(sh) { hideFlags = HideFlags.DontSave };
        return _lineMat;
    }

    static Material GetFillMaterial()
    {
        if (_fillMat != null) return _fillMat;
        var sh = Shader.Find("GeoWorld/SilkscreenFlat");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        _fillMat = new Material(sh) { hideFlags = HideFlags.DontSave };
        return _fillMat;
    }

    // The same bold inverse-hull cartoon outline the blocks wear (2nd slot).
    static Material GetOutlineMaterial()
    {
        if (_outlineMat != null) return _outlineMat;
        var sh = Shader.Find("GeoWorld/BlockOutline");
        if (sh == null) return null;
        _outlineMat = new Material(sh) { hideFlags = HideFlags.DontSave };
        _outlineMat.SetColor(Shader.PropertyToID("_OutlineColor"), new Color(0.04f, 0.04f, 0.06f, 1f));
        _outlineMat.SetFloat(Shader.PropertyToID("_OutlineWidth"), 0.05f);
        return _outlineMat;
    }

    // Inverted 4-sided pyramid (unit-ish): square base UP at y=+0.5, apex DOWN
    // at y=-0.9 — a point-down "wrong way" crystal. Flat normals per face.
    static Mesh GetShardMesh()
    {
        if (_shardMesh != null) return _shardMesh;

        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var tris  = new List<int>();

        Vector3 apex = new Vector3(0f, -0.9f, 0f);
        Vector3 b0 = new Vector3(-0.5f, 0.5f, -0.5f);
        Vector3 b1 = new Vector3( 0.5f, 0.5f, -0.5f);
        Vector3 b2 = new Vector3( 0.5f, 0.5f,  0.5f);
        Vector3 b3 = new Vector3(-0.5f, 0.5f,  0.5f);

        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            int s = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            norms.Add(n); norms.Add(n); norms.Add(n);
            tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
        }

        // Side faces (outward), base cap (up).
        Tri(b0, b1, apex);
        Tri(b1, b2, apex);
        Tri(b2, b3, apex);
        Tri(b3, b0, apex);
        Tri(b0, b3, b2);
        Tri(b0, b2, b1);

        _shardMesh = new Mesh { name = "HeresyShard" };
        _shardMesh.SetVertices(verts);
        _shardMesh.SetNormals(norms);
        _shardMesh.SetTriangles(tris, 0);
        _shardMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
        return _shardMesh;
    }
}
