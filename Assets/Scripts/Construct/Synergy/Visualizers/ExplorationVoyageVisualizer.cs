using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Exploration — "expedition" visualizer. ExplorationRule claims a long
// straight line of same-color blocks; this draws it as a mountaineering
// route: roadside pennant flags, a gliding comet-dart trailing a ribbon,
// and a subtle current of motes along the top faces.
//
// Same reconciler contract as OrderArchitectVisualizer / HarmonyVineVisualizer:
// we own each rig (keyed by pieceId) via Reconcile(), OnPieceClaimed returns
// null so the dispatcher never destroys it. Rigs are free-standing world
// objects, immune to block rotation/GrowIn.
[CreateAssetMenu(
    menuName = "GeoWorld/Synergy/Visualizers/Exploration Voyage",
    fileName = "ExplorationVoyageVisualizer")]
public class ExplorationVoyageVisualizer : SynergyVisualizer
{
    [Header("Color")]
    [Tooltip("Tint the pennants / current with the synergy's theme color. If off, uses Pennant Color below.")]
    public bool useThemeColor = true;

    [Tooltip("Pennant cloth color when 'Use Theme Color' is off.")]
    public Color pennantColor = new Color(0.88f, 0.18f, 0.20f, 1f);

    [Tooltip("Hoist band + comet + current mote color — the shared accent gold.")]
    public Color accentColor = new Color(0.98f, 0.85f, 0.20f, 1f);

    [Tooltip("Flag pole color — dark ink, matches the block outlines.")]
    public Color poleColor = new Color(0.10f, 0.09f, 0.12f, 1f);

    [Header("Flags")]
    [Tooltip("One flag roughly every this many cells along the route (both ends always get one).")]
    [Range(1f, 5f)] public float flagSpacingCells = 2f;

    [Tooltip("Pole height, in cell-size units.")]
    [Range(0.2f, 1.2f)] public float flagHeightFrac = 0.62f;

    [Tooltip("Pennant length, in cell-size units.")]
    [Range(0.1f, 0.8f)] public float pennantLenFrac = 0.34f;

    [Tooltip("Wind heading offset from the route direction, degrees — all flags share it.")]
    [Range(-90f, 90f)] public float windSkewDeg = 30f;

    [Tooltip("Flutter speed.")]
    public float flutterSpeed = 2.6f;

    [Tooltip("Flutter yaw swing, degrees.")]
    [Range(0f, 30f)] public float flutterYawDeg = 10f;

    [Header("Comet traveller")]
    public bool showComet = true;

    [Tooltip("Glide speed in cells per second.")]
    public float cometSpeedCells = 1.8f;

    [Tooltip("Pause at the route's end before the next departure, seconds.")]
    public float cometPauseSec = 1.6f;

    [Tooltip("Dart size, in cell-size units.")]
    [Range(0.05f, 0.4f)] public float cometSizeFrac = 0.15f;

    [Tooltip("Flight height above the top faces, in cell-size units.")]
    [Range(0f, 0.6f)] public float cometLiftFrac = 0.22f;

    [Tooltip("Ribbon trail lifetime, seconds.")]
    public float trailTime = 0.55f;

    [Tooltip("Ribbon width at the dart, in cell-size units.")]
    [Range(0.01f, 0.2f)] public float trailWidthFrac = 0.05f;

    [Header("Material flow (flowing shader on the block surface)")]
    [Tooltip("Coat each claimed block in a flowing energy SHADER (scrolling stripes + edge glow) — the block's own surface reads as a moving material, not a particle effect.")]
    public bool showMaterialFlow = true;

    [Tooltip("Shell scale over the block mesh (slightly >1 avoids z-fighting).")]
    [Range(1f, 1.1f)] public float flowShellScale = 1.012f;

    [Tooltip("Stripe scroll speed.")]
    public float flowSpeed = 0.6f;

    [Tooltip("Stripe density across a face.")]
    public float flowDensity = 5f;

    [Tooltip("Stripe sharpness (higher = thinner, crisper bands).")]
    [Range(1f, 40f)] public float flowSharpness = 8f;

    [Tooltip("Overall glow intensity of the flow coating.")]
    public float flowGlow = 1.6f;

    [Header("Current motes (optional legacy particle flow — off by default)")]
    [Tooltip("Discrete particle motes streaming along the route. Off by default; the material flow above is the intended 'flow'.")]
    public bool showCurrent = false;

    [Range(1, 10)] public int currentMotes = 5;
    public float currentSpeedCells = 2.4f;
    [Range(0.02f, 0.2f)] public float currentMoteSizeFrac = 0.07f;
    [Range(0f, 0.2f)] public float currentLiftFrac = 0.05f;
    public float currentTrailTime = 0.3f;

    [Header("Animation")]
    [Tooltip("Seconds for a rig to raise its flags.")]
    public float fadeInDuration = 0.5f;

    [Tooltip("Extra delay per flag so they raise in a wave along the route.")]
    public float flagStagger = 0.07f;

    [Tooltip("Seconds to fade out a released rig.")]
    public float witherDuration = 0.3f;

    private struct Target
    {
        public Color pennant;
        public Color accent;
        public Color pole;
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

            var target = new Target
            {
                pennant = useThemeColor ? BlockColorPalette.Get(a.rule.color) : pennantColor,
                accent  = accentColor,
                pole    = poleColor,
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

        var go = new GameObject($"ExplorationRig_{pieceId}");

        var rig = go.AddComponent<ExplorationRig>();
        rig.flagSpacingCells    = flagSpacingCells;
        rig.flagHeightFrac      = flagHeightFrac;
        rig.pennantLenFrac      = pennantLenFrac;
        rig.windSkewDeg         = windSkewDeg;
        rig.flutterSpeed        = flutterSpeed;
        rig.flutterYawDeg       = flutterYawDeg;
        rig.showComet           = showComet;
        rig.cometSpeedCells     = cometSpeedCells;
        rig.cometPauseSec       = cometPauseSec;
        rig.cometSizeFrac       = cometSizeFrac;
        rig.cometLiftFrac       = cometLiftFrac;
        rig.trailTime           = trailTime;
        rig.trailWidthFrac      = trailWidthFrac;
        rig.showCurrent         = showCurrent;
        rig.currentMotes        = currentMotes;
        rig.currentSpeedCells   = currentSpeedCells;
        rig.currentMoteSizeFrac = currentMoteSizeFrac;
        rig.currentLiftFrac     = currentLiftFrac;
        rig.currentTrailTime    = currentTrailTime;
        rig.fadeInDuration      = fadeInDuration;
        rig.flagStagger         = flagStagger;
        rig.witherDuration      = witherDuration;

        rig.Build(centers, cs, target.pennant, target.accent, target.pole);

        // Coat the block's own surface with the flowing shader (slightly
        // inflated shell), theme-tinted — not particles.
        if (showMaterialFlow)
        {
            var rends = ins.visualObject.GetComponentsInChildren<MeshRenderer>();
            rig.AttachFlowShells(rends, target.pennant, flowShellScale,
                                 flowSpeed, flowDensity, flowSharpness, flowGlow);
        }

        _rigs[pieceId] = go;
    }

    private static void RetireRig(GameObject go)
    {
        if (go == null) return;
        if (go.TryGetComponent<ExplorationRig>(out var rig)) rig.Retire();
        else Object.Destroy(go);
    }
}

// One expedition rig: roadside pennant flags + a gliding comet + a flowing
// current of motes along the route. Free-standing world object; Build() raises
// the flags in a wave, Retire() fades everything then self-destroys.
[DisallowMultipleComponent]
public class ExplorationRig : MonoBehaviour
{
    public float flagSpacingCells = 2f;
    public float flagHeightFrac   = 0.62f;
    public float pennantLenFrac   = 0.34f;
    public float windSkewDeg      = 30f;
    public float flutterSpeed     = 2.6f;
    public float flutterYawDeg    = 10f;

    public bool  showComet       = true;
    public float cometSpeedCells = 1.8f;
    public float cometPauseSec   = 1.6f;
    public float cometSizeFrac   = 0.15f;
    public float cometLiftFrac   = 0.22f;
    public float trailTime       = 0.55f;
    public float trailWidthFrac  = 0.05f;

    public bool  showCurrent         = true;
    public int   currentMotes        = 5;
    public float currentSpeedCells   = 2.4f;
    public float currentMoteSizeFrac = 0.07f;
    public float currentLiftFrac     = 0.05f;
    public float currentTrailTime    = 0.3f;

    public float fadeInDuration = 0.5f;
    public float flagStagger    = 0.07f;
    public float witherDuration = 0.3f;

    sealed class Flag
    {
        public Transform root;      // pole base (scaled for raise/lower)
        public Transform pennant;   // pivot at the pole top (fluttered)
        public float     delay;     // raise-wave stagger
        public float     phase;     // flutter phase offset
    }

    sealed class Mote
    {
        public Transform     t;
        public TrailRenderer trail;
        public float         offset;  // phase along the route (0..1)
    }

    readonly List<Flag>       _flags = new();
    readonly List<Mote>       _motes = new();
    readonly List<GameObject> _flowShells = new();   // material-flow coating shells on the block

    Transform     _comet;
    TrailRenderer _cometTrail;
    List<Vector3> _cometRoute;   // flight path (comet lift)
    List<Vector3> _currentRoute; // current path (small lift over faces)
    float         _cometRouteLen, _currentRouteLen;
    float         _cometBornAt;

    Color _accentColor = Color.yellow;
    float _cellSize = 1f;
    float _born;
    bool  _built, _retiring;
    float _witherStart;
    int   _seed;
    float _windHeading;

    static readonly int _ColorID     = Shader.PropertyToID("_Color");
    static readonly int _BaseColorID = Shader.PropertyToID("_BaseColor");

    public void Build(Vector3[] cellCentersWorld, float cellSize, Color pennant, Color accent, Color pole)
    {
        Clear();
        _accentColor = accent;
        _cellSize    = cellSize;
        _born        = Time.time;
        _retiring    = false;
        _seed        = GetInstanceID();

        if (cellCentersWorld == null || cellCentersWorld.Length == 0) { _built = true; return; }

        Vector3 mn = cellCentersWorld[0], mx = cellCentersWorld[0];
        for (int i = 1; i < cellCentersWorld.Length; i++)
        {
            mn = Vector3.Min(mn, cellCentersWorld[i]);
            mx = Vector3.Max(mx, cellCentersWorld[i]);
        }
        Vector3 center = (mn + mx) * 0.5f;
        transform.SetPositionAndRotation(center, Quaternion.identity);
        transform.localScale = Vector3.one;

        float half = cellSize * 0.5f;

        var surface = BuildSurfacePoints(cellCentersWorld, center, cellSize, half);
        if (surface.Count == 0) { _built = true; return; }

        Vector3 routeDir = surface.Count > 1 ? (surface[surface.Count - 1] - surface[0]).normalized : Vector3.right;
        _windHeading = Mathf.Atan2(routeDir.x, routeDir.z) * Mathf.Rad2Deg + windSkewDeg;

        BuildFlags(surface, cellSize, pennant, accent, pole);

        // Comet flight route.
        if (showComet && surface.Count > 1)
        {
            _cometRoute = LiftRoute(surface, cellSize * cometLiftFrac);
            _cometRouteLen = RouteLength(_cometRoute);
            BuildComet(cellSize, accent);
            _cometBornAt = _born + fadeInDuration;
        }

        // Current flow route + motes (the "material flow" along the line).
        if (showCurrent && surface.Count > 1)
        {
            _currentRoute = LiftRoute(surface, cellSize * currentLiftFrac);
            _currentRouteLen = RouteLength(_currentRoute);
            int n = Mathf.Clamp(currentMotes, 1, 10);
            for (int i = 0; i < n; i++)
                BuildMote((i + 0.5f) / n, cellSize, accent);
        }

        _built = true;
    }

    public void Retire()
    {
        if (_retiring) return;
        _retiring    = true;
        _witherStart = Time.time;
        if (_cometTrail != null) _cometTrail.emitting = false;
        for (int i = 0; i < _motes.Count; i++)
            if (_motes[i]?.trail != null) _motes[i].trail.emitting = false;
        if (!_built) Destroy(gameObject);
    }

    // ── Combat-ripple replay ─────────────────────────────────────────────────
    void OnEnable()  { SynergyVisualFX.OnReplayGrowIn += HandleReplay; }
    void OnDisable() { SynergyVisualFX.OnReplayGrowIn -= HandleReplay; }

    void HandleReplay(System.Func<Vector3, float> delayFor)
    {
        if (!_built || _retiring) return;
        float d = delayFor != null ? Mathf.Max(0f, delayFor(transform.position)) : 0f;
        _born = Time.time + d;
        _cometBornAt = _born + fadeInDuration;
        if (_cometTrail != null) _cometTrail.Clear();
        for (int i = 0; i < _motes.Count; i++)
            if (_motes[i]?.trail != null) _motes[i].trail.Clear();
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

        // Flags: raise in a wave, then flutter in the shared wind.
        for (int i = 0; i < _flags.Count; i++)
        {
            var f = _flags[i];
            if (f.root == null) continue;

            float raise = _retiring
                ? fade
                : Mathf.Clamp01((now - _born - f.delay) / Mathf.Max(0.01f, fadeInDuration));
            f.root.localScale = Vector3.one * EaseOutBack(raise);

            if (f.pennant != null)
            {
                float yaw   = Mathf.Sin(now * flutterSpeed * Mathf.PI + f.phase) * flutterYawDeg
                            + Mathf.Sin(now * flutterSpeed * 2.7f + f.phase * 1.7f) * flutterYawDeg * 0.35f;
                float pitch = Mathf.Sin(now * flutterSpeed * 1.4f + f.phase * 0.6f) * flutterYawDeg * 0.3f;
                f.pennant.localRotation = Quaternion.Euler(pitch, _windHeading + yaw, 0f);
            }
        }

        // Comet: glide start → end, pause, ribbon-clear, depart again.
        if (_comet != null && _cometRoute != null && _cometRouteLen > 1e-4f)
        {
            float cycle   = _cometRouteLen / Mathf.Max(0.01f, cometSpeedCells * _cellSize) + Mathf.Max(0f, cometPauseSec);
            float tCycle  = Mathf.Repeat(now - _cometBornAt, cycle);
            float d       = tCycle * cometSpeedCells * _cellSize;
            bool  gliding = now >= _cometBornAt && d <= _cometRouteLen && !_retiring && fade >= 0.999f;

            _comet.gameObject.SetActive(gliding);
            if (gliding)
            {
                Vector3 pos = PointOnRoute(_cometRoute, d, out Vector3 dir);
                pos += Vector3.up * (Mathf.Sin(d * 2.1f / _cellSize) * _cellSize * 0.03f);
                _comet.localPosition = pos;
                if (dir.sqrMagnitude > 1e-6f) _comet.localRotation = Quaternion.LookRotation(dir);
            }
            else if (_cometTrail != null) _cometTrail.Clear();
        }

        // Current: motes stream continuously along the route (wrap end→start).
        bool flowing = !_retiring && fade >= 0.999f;
        if (_currentRoute != null && _currentRouteLen > 1e-4f)
        {
            float speed = currentSpeedCells * _cellSize;
            float sz = _cellSize * currentMoteSizeFrac;
            for (int i = 0; i < _motes.Count; i++)
            {
                var m = _motes[i];
                if (m.t == null) continue;
                float d = Mathf.Repeat(m.offset * _currentRouteLen + (now - _born) * speed, _currentRouteLen);
                Vector3 pos = PointOnRoute(_currentRoute, d, out _);
                m.t.localPosition = pos;
                m.t.localScale = Vector3.one * (sz * fade);
                m.t.gameObject.SetActive(flowing);
                if (m.trail != null && d < speed * Time.deltaTime * 1.5f) m.trail.Clear();
            }
        }
    }

    // Mirror each block renderer's mesh as a slightly-inflated shell wearing
    // the flowing shader, parented to the source renderer so it follows the
    // block's transform / GrowIn scale.
    public void AttachFlowShells(Renderer[] srcRends, Color tint, float shellScale,
                                 float speed, float density, float sharp, float glow)
    {
        var mat = GetFlowMaterial(speed, density, sharp, glow);
        if (mat == null || srcRends == null) return;

        for (int i = 0; i < srcRends.Length; i++)
        {
            var srcR  = srcRends[i];
            if (srcR == null) continue;
            var srcMf = srcR.GetComponent<MeshFilter>();
            if (srcMf == null || srcMf.sharedMesh == null) continue;

            var shell = new GameObject("ExplorationFlowShell");
            shell.transform.SetParent(srcR.transform, false);
            shell.transform.localPosition = Vector3.zero;
            shell.transform.localRotation = Quaternion.identity;
            shell.transform.localScale    = Vector3.one * shellScale;

            shell.AddComponent<MeshFilter>().sharedMesh = srcMf.sharedMesh;
            var mr = shell.AddComponent<MeshRenderer>();
            mr.sharedMaterial       = mat;
            mr.shadowCastingMode    = ShadowCastingMode.Off;
            mr.receiveShadows       = false;
            mr.lightProbeUsage      = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            MpbColor.Set(mr, tint);   // writes _BaseColor (theme tint)

            _flowShells.Add(shell);
        }
    }

    // ── Route helpers ─────────────────────────────────────────────────────────
    List<Vector3> BuildSurfacePoints(Vector3[] cells, Vector3 center, float cellSize, float half)
    {
        var byColumn = new Dictionary<Vector2Int, Vector3>();
        var occupied = new HashSet<Vector3Int>();
        foreach (var c in cells)
            occupied.Add(new Vector3Int(
                Mathf.RoundToInt(c.x / cellSize),
                Mathf.RoundToInt(c.y / cellSize),
                Mathf.RoundToInt(c.z / cellSize)));

        foreach (var c in cells)
        {
            var k = new Vector3Int(
                Mathf.RoundToInt(c.x / cellSize),
                Mathf.RoundToInt(c.y / cellSize),
                Mathf.RoundToInt(c.z / cellSize));
            if (occupied.Contains(k + Vector3Int.up)) continue;
            var col = new Vector2Int(k.x, k.z);
            if (!byColumn.TryGetValue(col, out var ex) || c.y > ex.y) byColumn[col] = c;
        }

        var pts = new List<Vector3>(byColumn.Values);
        if (pts.Count == 0) return pts;

        Vector3 mn = pts[0], mx = pts[0];
        for (int i = 1; i < pts.Count; i++) { mn = Vector3.Min(mn, pts[i]); mx = Vector3.Max(mx, pts[i]); }
        Vector3 span = mx - mn;
        Vector3 axis = Mathf.Abs(span.x) >= Mathf.Abs(span.z) ? Vector3.right : Vector3.forward;
        pts.Sort((a, b) => Vector3.Dot(a, axis).CompareTo(Vector3.Dot(b, axis)));

        for (int i = 0; i < pts.Count; i++)
            pts[i] = (pts[i] - center) + Vector3.up * half;   // top-face height, rig-local
        return pts;
    }

    static List<Vector3> LiftRoute(List<Vector3> src, float lift)
    {
        var r = new List<Vector3>(src.Count);
        for (int i = 0; i < src.Count; i++) r.Add(src[i] + Vector3.up * lift);
        return r;
    }

    static float RouteLength(List<Vector3> r)
    {
        float len = 0f;
        for (int i = 0; i + 1 < r.Count; i++) len += Vector3.Distance(r[i], r[i + 1]);
        return len;
    }

    static Vector3 PointOnRoute(List<Vector3> r, float d, out Vector3 dir)
    {
        for (int i = 0; i + 1 < r.Count; i++)
        {
            float seg = Vector3.Distance(r[i], r[i + 1]);
            if (d <= seg || i + 2 == r.Count)
            {
                dir = seg < 1e-5f ? Vector3.forward : (r[i + 1] - r[i]) / seg;
                return Vector3.Lerp(r[i], r[i + 1], seg < 1e-5f ? 0f : Mathf.Clamp01(d / seg));
            }
            d -= seg;
        }
        dir = Vector3.forward;
        return r[r.Count - 1];
    }

    // ── Flags ─────────────────────────────────────────────────────────────────
    void BuildFlags(List<Vector3> surface, float cellSize, Color pennant, Color hoist, Color pole)
    {
        var spots = new List<Vector3> { surface[0] };
        if (surface.Count > 1)
        {
            float spacing = Mathf.Max(0.5f, flagSpacingCells) * cellSize;
            float acc = 0f;
            for (int i = 1; i < surface.Count - 1; i++)
            {
                acc += Vector3.Distance(surface[i - 1], surface[i]);
                if (acc >= spacing) { spots.Add(surface[i]); acc = 0f; }
            }
            spots.Add(surface[surface.Count - 1]);
        }

        Material fill = GetFillMaterial();
        float poleH   = cellSize * flagHeightFrac;
        float poleW   = cellSize * 0.022f;
        float penLen  = cellSize * pennantLenFrac;
        float penDrop = penLen * 0.42f;

        // Flags line the roadside, alternating sides, so the walkable lane
        // stays clear.
        Vector3 routeDir = surface.Count > 1
            ? (surface[surface.Count - 1] - surface[0]).normalized : Vector3.right;
        Vector3 perp = Vector3.Cross(Vector3.up, routeDir);
        if (perp.sqrMagnitude < 1e-4f) perp = Vector3.right;
        perp.Normalize();

        for (int i = 0; i < spots.Count; i++)
        {
            float side = (i & 1) == 0 ? 1f : -1f;
            Vector3 edge = perp * (side * cellSize * 0.34f);
            Vector3 jit  = routeDir * ((Hash01(_seed + i * 131 + 1) - 0.5f) * cellSize * 0.22f);

            var root = new GameObject("Flag").transform;
            root.SetParent(transform, false);
            root.localPosition = spots[i] + edge + jit;
            root.localScale    = Vector3.zero;

            NewMeshChild("Pole", root, GetBoxMesh(), fill, pole,
                         new Vector3(0f, poleH * 0.5f, 0f), new Vector3(poleW, poleH, poleW));
            NewMeshChild("Finial", root, GetNodeMesh(), fill, hoist,
                         new Vector3(0f, poleH + poleW * 1.6f, 0f), Vector3.one * (poleW * 2.2f));

            var pivot = new GameObject("Pennant").transform;
            pivot.SetParent(root, false);
            pivot.localPosition = new Vector3(0f, poleH, 0f);

            NewMeshChild("Cloth", pivot, GetPennantMesh(), fill, pennant,
                         Vector3.zero, new Vector3(penLen, penDrop, 1f));
            NewMeshChild("Hoist", pivot, GetHoistMesh(), fill, hoist,
                         Vector3.zero, new Vector3(penLen, penDrop, 1f));

            _flags.Add(new Flag
            {
                root    = root,
                pennant = pivot,
                delay   = i * flagStagger,
                phase   = Hash01(_seed + i * 53) * Mathf.PI * 2f,
            });
        }
    }

    // ── Comet ─────────────────────────────────────────────────────────────────
    void BuildComet(float cellSize, Color accent)
    {
        var go = new GameObject("Comet");
        go.transform.SetParent(transform, false);
        _comet = go.transform;

        NewMeshChild("Dart", _comet, GetDartMesh(), GetFillMaterial(), accent,
                     Vector3.zero, Vector3.one * (cellSize * cometSizeFrac));

        _cometTrail = MakeTrail(go, cellSize * trailWidthFrac, trailTime, accent);
        go.SetActive(false);
    }

    void BuildMote(float offset, float cellSize, Color accent)
    {
        var go = new GameObject("CurrentMote");
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.zero;
        go.AddComponent<MeshFilter>().sharedMesh = GetNodeMesh();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = GetFillMaterial();
        ConfigRenderer(mr);
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor(_BaseColorID, accent);
        mpb.SetColor(_ColorID, accent);
        mr.SetPropertyBlock(mpb);

        TrailRenderer tr = null;
        if (currentTrailTime > 1e-3f)
            tr = MakeTrail(go, cellSize * currentMoteSizeFrac * 0.6f, currentTrailTime, accent);

        _motes.Add(new Mote { t = go.transform, trail = tr, offset = offset });
    }

    TrailRenderer MakeTrail(GameObject go, float width, float time, Color color)
    {
        var tr = go.AddComponent<TrailRenderer>();
        tr.time          = Mathf.Max(0.05f, time);
        tr.startWidth    = width;
        tr.endWidth      = 0f;
        tr.numCapVertices    = 3;
        tr.numCornerVertices = 3;
        tr.minVertexDistance = 0.02f;
        tr.shadowCastingMode = ShadowCastingMode.Off;
        tr.receiveShadows    = false;
        tr.sharedMaterial    = GetTrailMaterial();
        var grad = new Gradient();
        Color c0 = color; c0.a = 0.8f;
        Color c1 = color; c1.a = 0f;
        grad.SetKeys(
            new[] { new GradientColorKey(c0, 0f), new GradientColorKey(c1, 1f) },
            new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
        tr.colorGradient = grad;
        return tr;
    }

    // ── Shared helpers ────────────────────────────────────────────────────────
    void NewMeshChild(string name, Transform parent, Mesh mesh, Material mat, Color color,
                      Vector3 localPos, Vector3 localScale)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale    = localScale;

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        ConfigRenderer(mr);

        var mpb = new MaterialPropertyBlock();
        mpb.SetColor(_BaseColorID, color);
        mpb.SetColor(_ColorID, color);
        mr.SetPropertyBlock(mpb);
    }

    static void ConfigRenderer(MeshRenderer mr)
    {
        mr.shadowCastingMode    = ShadowCastingMode.Off;
        mr.receiveShadows       = false;
        mr.lightProbeUsage      = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float xm = x - 1f;
        return 1f + c3 * xm * xm * xm + c1 * xm * xm;
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
        for (int i = 0; i < _flags.Count; i++)
            if (_flags[i] != null && _flags[i].root != null) Destroy(_flags[i].root.gameObject);
        _flags.Clear();
        for (int i = 0; i < _motes.Count; i++)
            if (_motes[i] != null && _motes[i].t != null) Destroy(_motes[i].t.gameObject);
        _motes.Clear();
        for (int i = 0; i < _flowShells.Count; i++)
            if (_flowShells[i] != null) Destroy(_flowShells[i]);
        _flowShells.Clear();
        if (_comet != null) { Destroy(_comet.gameObject); _comet = null; _cometTrail = null; }
        _cometRoute = null;
        _currentRoute = null;
    }

    // ── Shared (static) assets ───────────────────────────────────────────────
    static Material _fillMat;
    static Material _trailMat;
    static Material _flowMat;
    static Mesh _boxMesh, _nodeMesh, _pennantMesh, _hoistMesh, _dartMesh;

    // Shared, GPU-instanced coating material; params are global so the last
    // caller's values win (fine, same across the whole synergy).
    static Material GetFlowMaterial(float speed, float density, float sharp, float glow)
    {
        var sh = Shader.Find("GeoWorld/Synergy/ExplorationFlow");
        if (sh == null) return null;   // shader missing → skip the coating gracefully
        if (_flowMat == null)
        {
            _flowMat = new Material(sh) { hideFlags = HideFlags.DontSave };
            _flowMat.enableInstancing = true;
        }
        _flowMat.SetFloat("_Speed", speed);
        _flowMat.SetFloat("_Density", density);
        _flowMat.SetFloat("_Sharp", sharp);
        _flowMat.SetFloat("_Glow", glow);
        return _flowMat;
    }

    static Material GetFillMaterial()
    {
        if (_fillMat != null) return _fillMat;
        var sh = Shader.Find("GeoWorld/SilkscreenFlat");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        _fillMat = new Material(sh) { hideFlags = HideFlags.DontSave };
        if (_fillMat.HasProperty("_Cull")) _fillMat.SetFloat("_Cull", 0f);
        return _fillMat;
    }

    static Material GetTrailMaterial()
    {
        if (_trailMat != null) return _trailMat;
        var sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
        _trailMat = new Material(sh) { hideFlags = HideFlags.DontSave };
        return _trailMat;
    }

    static Mesh GetBoxMesh()
    {
        if (_boxMesh != null) return _boxMesh;
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var tris  = new List<int>();

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
        {
            int s = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            norms.Add(n); norms.Add(n); norms.Add(n); norms.Add(n);
            tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
            tris.Add(s); tris.Add(s + 2); tris.Add(s + 3);
        }

        Vector3 p(float x, float y, float z) => new Vector3(x - 0.5f, y - 0.5f, z - 0.5f);
        Quad(p(0,0,0), p(0,1,0), p(1,1,0), p(1,0,0), Vector3.back);
        Quad(p(1,0,1), p(1,1,1), p(0,1,1), p(0,0,1), Vector3.forward);
        Quad(p(0,0,1), p(0,1,1), p(0,1,0), p(0,0,0), Vector3.left);
        Quad(p(1,0,0), p(1,1,0), p(1,1,1), p(1,0,1), Vector3.right);
        Quad(p(0,1,0), p(0,1,1), p(1,1,1), p(1,1,0), Vector3.up);
        Quad(p(0,0,1), p(0,0,0), p(1,0,0), p(1,0,1), Vector3.down);

        _boxMesh = new Mesh { name = "ExplorationPole" };
        _boxMesh.SetVertices(verts);
        _boxMesh.SetNormals(norms);
        _boxMesh.SetTriangles(tris, 0);
        _boxMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
        return _boxMesh;
    }

    static Mesh GetNodeMesh()
    {
        if (_nodeMesh != null) return _nodeMesh;
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var tris  = new List<int>();

        Vector3 top = Vector3.up * 0.5f, bot = Vector3.down * 0.5f;
        Vector3[] eq = { Vector3.right * 0.5f, Vector3.forward * 0.5f, Vector3.left * 0.5f, Vector3.back * 0.5f };

        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            int s = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            norms.Add(n); norms.Add(n); norms.Add(n);
            tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
        }
        for (int i = 0; i < 4; i++) { Vector3 a = eq[i], b = eq[(i + 1) % 4]; Tri(top, a, b); Tri(bot, b, a); }

        _nodeMesh = new Mesh { name = "ExplorationNode" };
        _nodeMesh.SetVertices(verts);
        _nodeMesh.SetNormals(norms);
        _nodeMesh.SetTriangles(tris, 0);
        _nodeMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
        return _nodeMesh;
    }

    static Mesh GetPennantMesh()
    {
        if (_pennantMesh != null) return _pennantMesh;
        _pennantMesh = new Mesh { name = "ExplorationPennant" };
        _pennantMesh.vertices  = new[]
        {
            new Vector3(0f,  0f,   0f),
            new Vector3(1f, -0.5f, 0f),
            new Vector3(0f, -1f,   0f),
        };
        _pennantMesh.triangles = new[] { 0, 1, 2 };
        _pennantMesh.RecalculateNormals();
        _pennantMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
        return _pennantMesh;
    }

    static Mesh GetHoistMesh()
    {
        if (_hoistMesh != null) return _hoistMesh;
        _hoistMesh = new Mesh { name = "ExplorationHoist" };
        _hoistMesh.vertices = new[]
        {
            new Vector3(0f,     0.02f,  0.001f),
            new Vector3(0.16f, -0.06f,  0.001f),
            new Vector3(0.16f, -0.94f,  0.001f),
            new Vector3(0f,    -1.02f,  0.001f),
        };
        _hoistMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        _hoistMesh.RecalculateNormals();
        _hoistMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
        return _hoistMesh;
    }

    static Mesh GetDartMesh()
    {
        if (_dartMesh != null) return _dartMesh;
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var tris  = new List<int>();

        Vector3 nose = new Vector3(0f, 0f, 1f);
        Vector3 bl   = new Vector3(-0.32f,  0.16f, -0.55f);
        Vector3 br   = new Vector3( 0.32f,  0.16f, -0.55f);
        Vector3 bb   = new Vector3( 0f,    -0.28f, -0.55f);

        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            int s = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            norms.Add(n); norms.Add(n); norms.Add(n);
            tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
        }

        Tri(nose, bl, br);
        Tri(nose, br, bb);
        Tri(nose, bb, bl);
        Tri(bl, bb, br);

        _dartMesh = new Mesh { name = "ExplorationDart" };
        _dartMesh.SetVertices(verts);
        _dartMesh.SetNormals(norms);
        _dartMesh.SetTriangles(tris, 0);
        _dartMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
        return _dartMesh;
    }
}
