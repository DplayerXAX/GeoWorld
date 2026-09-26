using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// One procedurally-grown branch/vine, drawn as a REAL TUBE MESH, with optional
// recursive sub-branches (forks). Spawned and owned by HarmonyVineVisualizer; the
// trunk is parented to the host block and forks are parented under the trunk, so
// destroying the block (or the trunk) takes the whole bush with it. Grow() animates
// the trunk outward node-by-node and may sprout forks along the way; Retire()
// withers the whole bush before destroy.
//
// WHY A MESH AND NOT A LineRenderer (this used to be one — do not go back):
//
// A LineRenderer builds a ribbon that always faces the camera, which means every
// vertex on it carries the same camera-facing normal. Put any lit shader on that and
// the whole strip returns one flat value — it cannot be lit, because there is no
// surface to light. Its shadow has the same problem: it is the shadow of a flat
// sheet that rotates to follow the camera, so it swims as you orbit. No material
// fixes either one; the geometry is what is wrong.
//
// So each branch now generates a tube: a ring of vertices swept along the node path
// with a rotation-minimising frame, normals pointing out of the surface. It lights
// like everything else in the scene, casts a real shadow, and shows up in the depth
// normals buffer so SSAO sees it.
//
// Colour is baked into VERTEX COLOURS rather than set per-renderer. A lush canopy is
// hundreds of separate branches, and a MaterialPropertyBlock on each one would
// disable the SRP Batcher for every one of them. Each branch already owns a unique
// generated mesh, so its gradient costs nothing to bake in — and then every branch,
// leaf and blossom in the game shares a single material.
public class VineEffect : MonoBehaviour
{
    [Header("Shape")]
    [Tooltip("Number of nodes along the trunk. More = longer and curvier. Forks use fewer.")]
    public int segments = 6;

    [Tooltip("Length added per segment, in world units.")]
    public float segmentLength = 0.25f;

    [Tooltip("Per-node wobble off the growth axis. Higher = wilder, lower = straighter.")]
    public float randomness = 0.12f;

    [Tooltip("Sides on the branch tube. 5 is plenty at this scale; 3 reads as a twig.")]
    [Range(3, 10)] public int radialSegments = 5;

    [Header("Direction / spread")]
    [Tooltip("Upward climb bias.")]
    public float upBias = 1f;

    [Tooltip("Outward (away from the cluster center) lean. Higher = spreads more sideways instead of straight up.")]
    public float outwardBias = 0.85f;

    [Tooltip("Random azimuth fan (degrees) so neighbouring vines don't grow parallel.")]
    public float spreadAngleDeg = 35f;

    [Tooltip("Extra outward drift added per node, for a curving-outward feel.")]
    public float outwardDrift = 0.04f;

    [Tooltip("Downward bend accumulated per node — branches arc UP then DROOP at the tips, the single biggest 'real branch / climbing vine' cue. 0 = straight, ~0.25 = a gentle weeping arc.")]
    public float gravityDroop = 0.22f;

    [Tooltip("How hard a branch SWEEPS: degrees of bend per node, all in one direction, easing off toward the tip. Each branch picks its own plane and sign, so no two curve alike.")]
    public float curlPerNodeDeg = 14f;

    [Header("Forking")]
    [Tooltip("Sprout recursive sub-branches while growing.")]
    public bool enableForking = true;

    [Range(0f, 1f)]
    [Tooltip("Chance to sprout a fork at each eligible node.")]
    public float forkProbability = 0.65f;

    [Tooltip("How many levels of sub-branches (0 = trunk only). This is the number that makes a plant look DENSE — each level roughly multiplies the branch count.")]
    public int maxForkDepth = 3;

    [Tooltip("Max forks a single branch may sprout.")]
    public int maxForksPerBranch = 3;

    [Range(0.2f, 1f)]
    [Tooltip("Each fork's length relative to its parent.")]
    public float forkLengthScale = 0.7f;

    [Range(0.2f, 1f)]
    [Tooltip("Each fork's width relative to its parent.")]
    public float forkWidthScale = 0.62f;

    [Tooltip("How far a fork veers off its parent's direction (degrees).")]
    public float forkAngleDeg = 40f;

    [Tooltip("How much narrower the fork angle gets with each level. Big limbs leave the " +
             "trunk WIDE; fine twigs run almost along their parent. That difference " +
             "between orders is what reads as layering — one angle at every level is " +
             "what reads as a diagram. 1 = no falloff (the old behaviour).")]
    [Range(0.2f, 1f)] public float forkAngleFalloff = 0.5f;

    [Tooltip("Upward pull added to every fork after it veers. This was hard-coded at 0.3. " +
             "It COMPOUNDS — each level is pulled back toward vertical again — so it is " +
             "the knob to turn if the plant still closes up: 0 lets forks keep the angle " +
             "they were given.")]
    [Range(0f, 1f)] public float forkLift = 0.3f;

    [Header("Coil / wrap around block")]
    [Tooltip("Climbing-vine mode: instead of branching outward, spiral UP AROUND the host block as a helix. The visualizer sets radius/height from the block footprint.")]
    public bool coilAround = false;

    [Tooltip("Block footprint half-extent on X (world), set by HarmonyVineVisualizer. The coil traces the block's RECTANGULAR cross-section so it hugs the flat faces and turns at the corners.")]
    public float coilHalfX = 0.5f;

    [Tooltip("Block footprint half-extent on Z (world), set by HarmonyVineVisualizer.")]
    public float coilHalfZ = 0.5f;

    [Tooltip("How far OUTSIDE the block faces the coil sits (world). Small = hugs tightly.")]
    public float coilSurfaceGap = 0.04f;

    [Tooltip("Full turns the coil makes from base to top.")]
    public float coilTurns = 1.6f;

    [Tooltip("Total climb height in world units (set by HarmonyVineVisualizer).")]
    public float coilHeight = 1.1f;

    [Tooltip("Coil node count — more = smoother helix.")]
    public int coilSegments = 26;

    [Tooltip("Sprout side-shoots off a coil too, so a climbing vine bushes out instead of being one bare wire around the block.")]
    public bool coilForks = true;

    [Header("Bridge / runner")]
    // The thing that makes an overgrown structure read as OVERGROWN rather than as a
    // structure with spikes on it: runners that leave one block, arch through the
    // air, and LAND on another. A branch whose tip stops in mid-air reads as a
    // spike — the eye has nothing to resolve it against. A runner with both ends
    // attached reads as a plant that has grown ACROSS the thing, and it is what ties
    // separate vines into one canopy instead of a row of unrelated bushes.
    [Tooltip("Grow toward bridgeTarget and terminate on it, instead of branching freely.")]
    public bool bridgeTo = false;

    [Tooltip("World point the runner lands on. Set by HarmonyVineVisualizer.")]
    public Vector3 bridgeTarget;

    [Tooltip("How high the runner arches over the gap, as a fraction of the span.")]
    [Range(0f, 0.6f)] public float bridgeArc = 0.22f;

    [Tooltip("Sideways meander across the span, as a fraction of it. Keeps a runner from being a taut wire.")]
    [Range(0f, 0.3f)] public float bridgeSway = 0.08f;

    [Tooltip("Sideways waves over the span. Whole numbers only — anything else leaves the runner's ends off their anchors.")]
    [Range(1, 4)] public int bridgeWaves = 2;

    [Header("Creeper (path mode)")]
    // The primary look. A creeper does not grow through the air at all: it is handed
    // a route that lies ON the cluster's exposed faces — across a top, over the rim,
    // down the side, onto the next block's top — and it follows it.
    //
    // This is what "clinging" actually is, and no amount of tuning a free-space walk
    // produces it: a branch that picks its own direction has no idea where the
    // surfaces are, so it either misses them or passes through them. The geometry
    // has to come from whoever knows the board, which is the visualizer.
    [Tooltip("Leaves per node while crawling a surface. Lower than a free branch — a runner pressed to a face should show the RUN, not bury it.")]
    [Range(0, 8)] public int creeperLeavesPerNode = 2;

    [Tooltip("Chance per node that a short twig lifts off the surface.")]
    [Range(0f, 1f)] public float creeperTwigChance = 0.16f;

    [Header("Width")]
    [Tooltip("Tube radius at the root.")]
    public float baseWidth = 0.055f;

    [Tooltip("Tube radius at the tip (taper to near-zero for a branch look).")]
    public float tipWidth = 0.008f;

    [Header("Material")]
    [Tooltip("Optional override. Leave EMPTY to use the shared GeoWorld/Foliage material — lit, shadow-casting, vertex-coloured, and batched across every vine in the scene.")]
    public Material foliageMaterialOverride;

    [Tooltip("Branches and leaves cast real shadows. This is most of what makes them sit in the scene rather than on top of it.")]
    public bool castShadows = true;

    [Header("Leaves")]
    [Tooltip("Sprout green leaves along the vine as it grows.")]
    public bool enableLeaves = true;
    [Tooltip("How many leaves to try sprouting at each node.")]
    [Range(0, 10)] public int leavesPerNode = 5;
    [Tooltip("Extra leaves massed at each branch TIP (a canopy tuft), so foliage clusters at the ends like a real bush instead of spreading evenly. 0 = off.")]
    [Range(0, 16)] public int tipCanopyLeaves = 7;
    [Tooltip("Leaf size range (world units, base→tip of one leaf).")]
    public Vector2 leafSize = new Vector2(0.09f, 0.17f);
    // Lifted off the near-black end for the same reason the bark was: a deep
    // saturated green loses its hue entirely on its shaded side.
    public Color leafColor    = new Color(0.40f, 0.68f, 0.30f);   // leaf green
    public Color leafTipColor = new Color(0.64f, 0.87f, 0.42f);   // bright new-growth green
    [Tooltip("Seconds for one leaf to scale in.")]
    public float leafGrowDuration = 0.18f;

    [Header("Blossoms")]
    [Tooltip("Sprinkle small pale blossoms among the leaves for a lush flowering-vine look.")]
    public bool enableBlossoms = true;
    [Range(0f, 1f)]
    [Tooltip("Chance per node to add a blossom. Kept low — this world is drawn with a " +
             "limited set of inks, and blossoms are the first thing that makes a " +
             "canopy read as busy rather than dense.")]
    public float blossomChance = 0.10f;
    public Color blossomColor = new Color(0.95f, 0.92f, 0.72f);   // pale cream blossom
    [Tooltip("Blossom size (world units).")]
    public float blossomSize = 0.06f;

    [Header("Timing")]
    [Tooltip("Seconds to extend ONE segment. Total grow time ~ this x (segments - 1). Smaller = faster.")]
    public float segmentGrowDuration = 0.08f;

    [Tooltip("Seconds to wither (width -> 0) before the branch is destroyed on release.")]
    public float witherDuration = 0.25f;

    static Material _foliageMat;              // shared by every branch, leaf and blossom
    static Mesh[]   _leafMeshes;              // shared leaf quads in 3 green tones
    static Mesh     _blossomMesh;

    MeshFilter   _mf;
    MeshRenderer _mr;
    Mesh         _mesh;

    // The branch path, in WORLD space. Kept in world and converted at build time so
    // the shape is authored at its true size no matter what the host block's
    // transform is doing.
    readonly List<Vector3> _nodes = new();

    // Reused buffers — a lush cluster rebuilds hundreds of these meshes per frame
    // while growing, and allocating four arrays per branch per frame would be the
    // whole cost of the effect.
    readonly List<Vector3> _vb = new();
    readonly List<Vector3> _nb = new();
    readonly List<Color>   _cb = new();
    readonly List<int>     _ib = new();

    Coroutine  _routine;
    bool       _retiring;
    GameObject _prefab;
    int        _forks;
    Vector3    _coilCenter;
    float      _coilStartAngle;
    Color      _rootCol, _tipCol;
    Vector3    _mainDir;
    Coroutine  _replayRoutine;

    // A second run that leaves the main one partway along, still clinging. This is
    // what turns a creeper from a line into a NETWORK: one stem entering the
    // structure and dividing over it, which is what the eye reads as a plant that
    // has spread across something rather than a cable laid on it.
    public class CreeperBranch
    {
        public int           at;        // index into the parent's point list
        public List<Vector3> points;
        public List<Vector3> normals;
    }

    // The creeper route and the surface normal at each point of it, kept so a
    // GrowIn replay retraces exactly the same path instead of inventing a new one.
    readonly List<Vector3> _path  = new();
    readonly List<Vector3> _pathN = new();
    List<CreeperBranch>    _pathBranches;

    // Chosen once per branch, at Begin. A branch that re-rolls its bend every node
    // wobbles; one that commits to a plane and a direction ARCS, which is what a
    // real limb does and what the eye reads as growth rather than as noise.
    Vector3 _bendAxis = Vector3.right;
    float   _bend = 1f;
    float   _lengthJitter = 1f;

    float _rootRadius = 0.05f, _tipRadius = 0.008f;
    float _widthScale = 1f;                   // wither multiplier

    void Awake()
    {
        // The authored prefab still carries a LineRenderer. Leaving it alive would
        // draw the old camera-facing ribbon straight through the new tube.
        if (TryGetComponent<LineRenderer>(out var stale)) stale.enabled = false;

        _mesh = new Mesh { name = "Vine" };
        _mesh.MarkDynamic();

        if (!TryGetComponent(out _mf)) _mf = gameObject.AddComponent<MeshFilter>();
        if (!TryGetComponent(out _mr)) _mr = gameObject.AddComponent<MeshRenderer>();
        _mf.sharedMesh = _mesh;
        ApplyRendererSettings(_mr);
    }

    void OnDestroy() { if (_mesh != null) Destroy(_mesh); }

    void OnEnable()  { SynergyVisualFX.OnReplayGrowIn += HandleReplay; }
    void OnDisable() { SynergyVisualFX.OnReplayGrowIn -= HandleReplay; }

    void ApplyRendererSettings(MeshRenderer mr)
    {
        mr.sharedMaterial = foliageMaterialOverride != null ? foliageMaterialOverride : GetFoliageMaterial();
        // TwoSided, because the shared material does not cull: a leaf is one quad and
        // a one-sided shadow caster would drop half the canopy out of the shadow map.
        mr.shadowCastingMode = castShadows
            ? UnityEngine.Rendering.ShadowCastingMode.TwoSided
            : UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = true;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    // Only the TRUNK replays (it rebuilds its own forks, so forks must not self-replay).
    bool IsTrunk => transform.parent == null || transform.parent.GetComponent<VineEffect>() == null;

    void HandleReplay(System.Func<Vector3, float> delayFor)
    {
        if (_retiring || !IsTrunk) return;
        float d = delayFor != null ? Mathf.Max(0f, delayFor(transform.position)) : 0f;

        var subs = GetComponentsInChildren<VineEffect>(true);
        for (int i = 0; i < subs.Length; i++)
            if (subs[i] != null && subs[i] != this) Destroy(subs[i].gameObject);
        foreach (Transform t in transform)
            if (t != null && t.GetComponent<VineEffect>() == null) Destroy(t.gameObject);

        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
        _nodes.Clear();
        if (_mesh != null) _mesh.Clear();

        if (_replayRoutine != null) StopCoroutine(_replayRoutine);
        _replayRoutine = StartCoroutine(ReplayAfter(d));
    }

    IEnumerator ReplayAfter(float d)
    {
        if (d > 0.001f) yield return new WaitForSeconds(d);
        if (_retiring) yield break;
        if (_path.Count >= 2) BeginCreeper();
        else if (bridgeTo)    BeginBridge(_rootCol, _tipCol);
        else if (coilAround)  BeginCoil(_rootCol, _tipCol);
        else                  Begin(_rootCol, _tipCol, transform.position, _mainDir, 0);
    }

    // A tree standing on a block's rim: the ordinary free-branch growth, but along a
    // direction the caller chose rather than one derived from "away from the
    // cluster". Grow() leans a trunk ~40° outward, which is right for a vine
    // reaching off a block and wrong for a tree, which stands.
    public void GrowTree(Color rootColor, Color tipColor, Vector3 up, GameObject prefab)
    {
        _prefab  = prefab;
        _rootCol = rootColor;
        _tipCol  = tipColor;
        _path.Clear();                      // not a creeper — so a replay regrows it as a tree
        coilAround = false;
        bridgeTo   = false;
        _mainDir   = up.sqrMagnitude > 1e-6f ? up.normalized : Vector3.up;
        Begin(rootColor, tipColor, transform.position, _mainDir, 0);
    }

    // Public entry — trunk (depth 0).
    public void Grow(Color rootColor, Color tipColor, Vector3 outward, GameObject prefab)
        => Grow(rootColor, tipColor, outward, prefab, transform.position);

    public void Grow(Color rootColor, Color tipColor, Vector3 outward, GameObject prefab, Vector3 coilCenter)
    {
        _prefab     = prefab;
        _coilCenter = coilCenter;
        _rootCol    = rootColor;
        _tipCol     = tipColor;

        if (bridgeTo)
        {
            _mainDir = ComputeMainDir(outward);
            BeginBridge(rootColor, tipColor);
        }
        else if (coilAround)
        {
            _mainDir = ComputeMainDir(outward);   // side-shoots off the coil use this
            BeginCoil(rootColor, tipColor);
        }
        else
        {
            _mainDir = ComputeMainDir(outward);   // cached so a replay keeps the shape
            Begin(rootColor, tipColor, transform.position, _mainDir, 0);
        }
    }

    void Begin(Color rootColor, Color tipColor, Vector3 startPos, Vector3 mainDir, int depth)
    {
        _forks = 0;
        _rootCol = rootColor;
        _tipCol  = tipColor;

        float w = Mathf.Pow(Mathf.Clamp(forkWidthScale, 0.2f, 1f), depth);
        _rootRadius = baseWidth * w;
        _tipRadius  = tipWidth  * w;
        _widthScale = 1f;

        // The sweep, decided here so it is constant for the whole branch.
        Vector3 perp = Vector3.Cross(mainDir, Vector3.up);
        if (perp.sqrMagnitude < 1e-6f) perp = Vector3.Cross(mainDir, Vector3.right);
        _bendAxis = (Quaternion.AngleAxis(Random.Range(0f, 360f), mainDir) * perp).normalized;
        _bend     = Random.Range(0.55f, 1.45f) * (Random.value < 0.5f ? -1f : 1f);

        // Length varies branch to branch. A fixed ratio per level makes every limb at
        // a given order exactly as long as its siblings, and that regularity is a
        // large part of what reads as manufactured.
        _lengthJitter = Random.Range(0.78f, 1.26f);

        _nodes.Clear();
        _nodes.Add(startPos);

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(GrowRoutine(rootColor, tipColor, startPos, mainDir, depth));
    }

    // ── TUBE MESH ────────────────────────────────────────────────────────────

    // Sweep a ring along the node path with a ROTATION-MINIMISING frame: each ring's
    // reference vector is the previous ring's, projected back perpendicular to the
    // new tangent. Deriving the frame from world up instead would spin the tube
    // wherever the branch passes through vertical, and the taper would visibly
    // corkscrew on exactly the arcing, drooping branches this thing is made of.
    void RebuildMesh()
    {
        if (_mesh == null) return;

        int n = _nodes.Count;
        if (n < 2 || _widthScale <= 0.001f) { _mesh.Clear(); return; }

        int r = Mathf.Clamp(radialSegments, 3, 10);

        _vb.Clear(); _nb.Clear(); _cb.Clear(); _ib.Clear();

        Vector3 seed = (_nodes[1] - _nodes[0]).normalized;
        if (seed.sqrMagnitude < 1e-8f) seed = Vector3.up;
        Vector3 refN = Vector3.Cross(seed, Vector3.up);
        if (refN.sqrMagnitude < 1e-6f) refN = Vector3.Cross(seed, Vector3.right);
        refN.Normalize();

        for (int i = 0; i < n; i++)
        {
            Vector3 tan = i == 0       ? _nodes[1] - _nodes[0]
                        : i == n - 1   ? _nodes[n - 1] - _nodes[n - 2]
                                       : _nodes[i + 1] - _nodes[i - 1];
            if (tan.sqrMagnitude < 1e-10f) tan = seed;
            tan.Normalize();

            refN -= tan * Vector3.Dot(refN, tan);
            if (refN.sqrMagnitude < 1e-8f)
            {
                refN = Vector3.Cross(tan, Vector3.up);
                if (refN.sqrMagnitude < 1e-8f) refN = Vector3.Cross(tan, Vector3.right);
            }
            refN.Normalize();
            Vector3 bin = Vector3.Cross(tan, refN);

            float f   = i / (float)(n - 1);
            float rad = Mathf.Lerp(_rootRadius, _tipRadius, f) * _widthScale;

            // Woody at the base, new growth at the tip — the same read the old
            // gradient had, only now it survives into a lit surface.
            Color col = Color.Lerp(_rootCol, _tipCol, Mathf.SmoothStep(0f, 1f, Mathf.Max(0f, (f - 0.55f) / 0.45f)));

            Vector3 centreL = transform.InverseTransformPoint(_nodes[i]);
            for (int k = 0; k < r; k++)
            {
                float a = k / (float)r * Mathf.PI * 2f;
                Vector3 dirL = transform.InverseTransformDirection(refN * Mathf.Cos(a) + bin * Mathf.Sin(a));
                _vb.Add(centreL + dirL * rad);
                _nb.Add(dirL.normalized);
                _cb.Add(col);
            }
        }

        // A pointed cap, so a branch ends in a tip instead of an open pipe.
        Vector3 lastTan = (_nodes[n - 1] - _nodes[n - 2]).normalized;
        _vb.Add(transform.InverseTransformPoint(_nodes[n - 1] + lastTan * (_tipRadius * _widthScale * 2f)));
        _nb.Add(transform.InverseTransformDirection(lastTan));
        _cb.Add(_tipCol);

        for (int i = 0; i < n - 1; i++)
            for (int k = 0; k < r; k++)
            {
                int a = i * r + k, b = i * r + (k + 1) % r;
                int c = (i + 1) * r + k, d = (i + 1) * r + (k + 1) % r;
                _ib.Add(a); _ib.Add(c); _ib.Add(b);
                _ib.Add(b); _ib.Add(c); _ib.Add(d);
            }

        int apex = n * r;
        for (int k = 0; k < r; k++)
        {
            int a = (n - 1) * r + k, b = (n - 1) * r + (k + 1) % r;
            _ib.Add(a); _ib.Add(apex); _ib.Add(b);
        }

        _mesh.Clear();
        _mesh.SetVertices(_vb);
        _mesh.SetNormals(_nb);
        _mesh.SetColors(_cb);
        _mesh.SetTriangles(_ib, 0);
        _mesh.RecalculateBounds();
    }

    // One shared material for every branch, leaf and blossom, so the whole canopy
    // batches. It does NOT cull: leaves are single quads that must light from both
    // sides, and the shader flips the normal on back faces to match. That also means
    // the tube's winding can never show up as an inside-out branch.
    static Material GetFoliageMaterial()
    {
        if (_foliageMat != null) return _foliageMat;

        Shader sh = Shader.Find("GeoWorld/Foliage");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
        _foliageMat = new Material(sh) { name = "Foliage (runtime, shared)" };
        if (_foliageMat.HasProperty("_Cull")) _foliageMat.SetFloat("_Cull", 0f);
        return _foliageMat;
    }

    // Trunk direction: horizontal outward + random azimuth fan + upward bias.
    Vector3 ComputeMainDir(Vector3 outward)
    {
        outward.y = 0f;
        if (outward.sqrMagnitude < 1e-4f)
            outward = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
        outward = outward.normalized;

        float az = Random.Range(-spreadAngleDeg, spreadAngleDeg) * Mathf.Deg2Rad;
        float ca = Mathf.Cos(az), sa = Mathf.Sin(az);
        outward = new Vector3(outward.x * ca - outward.z * sa, 0f, outward.x * sa + outward.z * ca);

        return (Vector3.up * upBias + outward * outwardBias).normalized;
    }

    IEnumerator GrowRoutine(Color rootColor, Color tipColor, Vector3 startPos, Vector3 mainDir, int depth)
    {
        float lengthScale = Mathf.Pow(Mathf.Clamp(forkLengthScale, 0.2f, 1f), depth) * _lengthJitter;
        int   segs        = Mathf.Max(3, segments - depth);

        Vector3 driftDir = new Vector3(mainDir.x, 0f, mainDir.z);
        if (driftDir.sqrMagnitude > 1e-4f) driftDir.Normalize();

        // growDir evolves node-to-node: winding curl (alternating twist around the
        // vertical) + accumulating gravity droop, so the branch arcs up then weeps
        // down at the tip — the core "living branch / climbing vine" read.
        Vector3 growDir  = mainDir;
        Vector3 lastNode = startPos;
        Vector3 current  = startPos;

        for (int i = 1; i < segs; i++)
        {
            // SWEEP, not zigzag.
            //
            // This used to alternate +/- every node, which draws a regular saw wave —
            // and regularity is exactly what reads as machine-made. It was most of
            // the stiffness. A real limb bends the SAME way along its whole length,
            // hard near the base where it is heavy and easing off toward the tip, so
            // the branch arcs. Each branch picked its own plane and sign at Begin,
            // so siblings curve differently instead of mirroring each other.
            if (curlPerNodeDeg > 0.01f)
            {
                float ease = 1f - i / (float)segs;
                growDir = Quaternion.AngleAxis(curlPerNodeDeg * _bend * ease, _bendAxis) * growDir;
            }
            growDir = (growDir + Vector3.down * (gravityDroop * (i / (float)segs))).normalized;

            Vector3 noise = new Vector3(
                Random.Range(-randomness, randomness),
                Random.Range(-randomness, randomness),
                Random.Range(-randomness, randomness));

            Vector3 next = current
                         + growDir  * (segmentLength * lengthScale)
                         + driftDir * (outwardDrift * i * lengthScale)
                         + noise    * lengthScale;

            _nodes.Add(current);                      // extend, then animate the new tip

            float dur = Mathf.Max(0.0001f, segmentGrowDuration);
            float t   = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                _nodes[_nodes.Count - 1] = Vector3.Lerp(current, next, t / dur);
                RebuildMesh();
                yield return null;
            }
            _nodes[_nodes.Count - 1] = next;
            RebuildMesh();

            lastNode = current;
            current  = next;

            if (enableLeaves) SpawnLeavesAt(next, growDir, lengthScale);
            if (enableBlossoms && Random.value < blossomChance) SpawnBlossomAt(next, lengthScale);

            if (enableForking && _prefab != null
                && depth < maxForkDepth && _forks < maxForksPerBranch
                && i >= 1 && i <= segs - 2
                && Random.value < forkProbability)
            {
                SpawnFork(rootColor, tipColor, next, growDir, depth);   // angle narrows with depth
                _forks++;
            }
        }

        if (enableLeaves && tipCanopyLeaves > 0)
        {
            Vector3 tipDir = (current - lastNode).sqrMagnitude > 1e-5f ? (current - lastNode).normalized : growDir;
            for (int k = 0; k < tipCanopyLeaves; k++)
                SpawnLeaf(current, Quaternion.AngleAxis(k * 360f / tipCanopyLeaves, tipDir) * Perpendicular(tipDir),
                          lengthScale * 1.05f);
            if (enableBlossoms) SpawnBlossomAt(current, lengthScale * 1.1f);
        }

        _routine = null;
    }

    static Vector3 Perpendicular(Vector3 v)
    {
        Vector3 p = Vector3.Cross(v, Vector3.up);
        if (p.sqrMagnitude < 1e-4f) p = Vector3.right;
        return p.normalized;
    }

    void SpawnFork(Color rootColor, Color tipColor, Vector3 atPos, Vector3 parentDir, int depth)
    {
        // Clone the PREFAB (not this sub-tree) so forks never duplicate existing
        // branches. Parent under this branch so the whole bush is one hierarchy.
        GameObject clone = Instantiate(_prefab, transform);
        if (!clone.TryGetComponent<VineEffect>(out var child)) { Destroy(clone); return; }
        child._prefab = _prefab;
        child.Begin(rootColor, tipColor, atPos, DeviateDirection(parentDir, depth), depth + 1);
    }

    Vector3 DeviateDirection(Vector3 dir, int depth)
    {
        // Wide at the trunk, shallow at the twigs. A cherry's big limbs fan almost
        // square off the trunk while its fine twigs run nearly parallel to whatever
        // carries them — and it is the DIFFERENCE between those orders that reads as
        // layering. One angle everywhere reads as a diagram of a tree.
        float t   = maxForkDepth > 0 ? Mathf.Clamp01(depth / (float)maxForkDepth) : 0f;
        float ang = forkAngleDeg * Mathf.Lerp(1f, forkAngleFalloff, t) * Random.Range(0.7f, 1.3f);

        Vector3 d = Deviate(dir, ang, Random.Range(0f, 360f));
        return (d + Vector3.up * forkLift).normalized;
    }

    // Turn `dir` by `deg` away from itself, rolled `roll` degrees around it.
    //
    // THIS IS THE BUNCHING FIX. DeviateDirection used to rotate about WORLD UP:
    //
    //     new Vector3(dir.x * ca - dir.z * sa, dir.y, dir.x * sa + dir.z * ca)
    //
    // which is a yaw, so it turns a branch in proportion to how HORIZONTAL that
    // branch already is. A branch pointing straight up has no horizontal component
    // to yaw, so its children came out very nearly PARALLEL TO IT — the 40° fork
    // angle was being applied to almost nothing. And since every fork also gets an
    // upward lift, each level was more vertical than the one before it, so the
    // effect compounded: by depth 2 a plant had closed into a broom.
    //
    // Free branches are the worst case, because they leave a surface along its
    // normal and a top face's normal IS up.
    //
    // Rotating about an axis PERPENDICULAR to the branch applies the authored angle
    // whatever the branch's orientation, and the roll spreads siblings around the
    // parent instead of stacking them all into one plane.
    static Vector3 Deviate(Vector3 dir, float deg, float roll)
    {
        dir = dir.normalized;
        Vector3 axis = Vector3.Cross(dir, Vector3.up);
        if (axis.sqrMagnitude < 1e-6f) axis = Vector3.Cross(dir, Vector3.right);
        axis.Normalize();
        return Quaternion.AngleAxis(roll, dir) * (Quaternion.AngleAxis(deg, axis) * dir);
    }

    // ── Creeper / surface mode ───────────────────────────────────────────────

    // Follow a route the visualizer computed over the cluster's faces. `normals` is
    // the outward normal of the face each point sits on, and it is what lets the
    // leaves lie DOWN on the surface: flat on a top face, pressed against a wall.
    // Without a per-point normal a creeper crossing a rim would go on sprouting
    // leaves straight up out of a vertical face.
    public void GrowAlong(List<Vector3> route, List<Vector3> normals,
                          Color rootColor, Color tipColor, GameObject prefab,
                          List<CreeperBranch> branches = null)
    {
        _prefab  = prefab;
        _rootCol = rootColor;
        _tipCol  = tipColor;

        _path.Clear();  _path.AddRange(route);
        _pathN.Clear(); _pathN.AddRange(normals);
        while (_pathN.Count < _path.Count) _pathN.Add(Vector3.up);

        _pathBranches = branches;

        BeginCreeper();
    }

    void BeginCreeper()
    {
        _forks = 0;

        // A creeper keeps its thickness: it is one stem lying across the structure,
        // not a branch tapering away into nothing. The taper belongs on the free
        // branches that leave it.
        _rootRadius = baseWidth * 0.85f;
        _tipRadius  = baseWidth * 0.62f;
        _widthScale = 1f;

        _nodes.Clear();
        _nodes.Add(_path[0]);

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(CreeperRoutine());
    }

    IEnumerator CreeperRoutine()
    {
        // The run leaves the surface at its START too, not only at its end — in a
        // real overgrowth the stem came from somewhere.
        SpawnSpray(_path[0], LaunchDir(_pathN[0], 0.8f));

        Vector3 prev = _path[0];

        for (int i = 1; i < _path.Count; i++)
        {
            Vector3 next = _path[i];
            Vector3 n    = _pathN[i];

            _nodes.Add(prev);

            // Paced by DISTANCE, not per point. The route's points are spaced by the
            // geometry it traces — a rim turn packs several of them into a few
            // centimetres — so a fixed time per point would make the vine crawl to a
            // halt at every corner.
            float dur = Mathf.Max(0.0001f, segmentGrowDuration)
                      * Mathf.Clamp(Vector3.Distance(prev, next) / Mathf.Max(0.02f, segmentLength), 0.15f, 3f);
            float e = 0f;
            while (e < dur)
            {
                e += Time.deltaTime;
                _nodes[_nodes.Count - 1] = Vector3.Lerp(prev, next, e / dur);
                RebuildMesh();
                yield return null;
            }
            _nodes[_nodes.Count - 1] = next;
            RebuildMesh();
            prev = next;

            if (enableLeaves && creeperLeavesPerNode > 0)
            {
                int keep = leavesPerNode;
                leavesPerNode = creeperLeavesPerNode;
                SpawnSurfaceLeavesAt(next, n, 0.85f);
                leavesPerNode = keep;
            }

            if (enableForking && _prefab != null && _forks < maxForksPerBranch
                && i > 1 && i < _path.Count - 1
                && Random.value < creeperTwigChance)
            {
                // 0, not 1: SpawnFork adds one internally, so this starts the twig at
                // depth 1 — the same order as a trunk's first fork. Depth scales both
                // length and node count down, and a twig started deeper than that is a
                // stub before it has even left the face.
                SpawnFork(_rootCol, _tipCol, next, LaunchDir(n, 0.5f), 0);
                _forks++;
            }

            if (enableBlossoms && Random.value < blossomChance * 0.5f) SpawnBlossomAt(next, 0.8f);

            // Divide where the route divides. Spawned as the run REACHES the point
            // rather than all at once at the start, so the plant visibly spreads
            // outward from where it entered instead of appearing whole.
            if (_pathBranches != null && _prefab != null)
                for (int k = 0; k < _pathBranches.Count; k++)
                {
                    var br = _pathBranches[k];
                    if (br == null || br.at != i || br.points == null || br.points.Count < 3) continue;

                    GameObject sub = Instantiate(_prefab, transform);
                    if (!sub.TryGetComponent<VineEffect>(out var child)) { Destroy(sub); continue; }
                    child.GrowAlong(br.points, br.normals, _rootCol, _tipCol, _prefab);
                }
        }

        // The free branch at the far end — the part of the sketch that is actually
        // in the air.
        int last = _path.Count - 1;
        SpawnSpray(_path[last], LaunchDir(_pathN[last], 0.9f));

        _routine = null;
    }

    // Which way a free branch leaves the surface.
    //
    // The surface normal plus a lift was giving every branch on a top face the SAME
    // direction — straight up — because a top face's normal is already up. Every run
    // in the cluster then threw an identical vertical branch. Leaning each one off
    // by a random amount, rolled anywhere around that axis, is what makes two
    // branches next to each other read as two branches.
    Vector3 LaunchDir(Vector3 normal, float lift)
    {
        Vector3 d = (normal + Vector3.up * lift).normalized;
        return Deviate(d, Random.Range(18f, 42f), Random.Range(0f, 360f));
    }

    // The free branch that lifts off the surface at the end of a run.
    //
    // It is an ORDINARY branch: a clean prefab clone growing under its own authored
    // parameters, the same Begin/GrowRoutine walk the vines used before any of the
    // surface work existed. It deliberately overrides NOTHING.
    //
    // It used to carry its own set of fork counts, angles, lengths and leaf budgets,
    // and that block was the bug — it meant the branches on screen were never the
    // branches the component is configured to grow, so every attempt to tune them
    // through the inspector did nothing and every attempt to tune them here was a
    // second, competing set of numbers. One place decides how a branch extends:
    // the fields at the top of this file.
    void SpawnSpray(Vector3 pos, Vector3 dir)
    {
        if (_prefab == null) return;

        GameObject clone = Instantiate(_prefab, transform);
        if (!clone.TryGetComponent<VineEffect>(out var child)) { Destroy(clone); return; }

        child._prefab = _prefab;

        // THICKNESS and LENGTH are separated here, because `depth` conflates them.
        //
        // The fusing problem was width: at depth 0 a branch starts at the full trunk
        // radius, which is WIDER than the runner it grows out of, and a child thicker
        // than its parent has no junction — the two tubes just merge into a lump.
        // Passing depth 1 fixed that, but it also cut the branch's length and node
        // count, which is why they came out stunted.
        //
        // So the junction is stated directly — a branch starts at forkWidthScale of
        // whatever the runner is at that point, which is the same ratio a fork uses —
        // and it then grows at depth 0, at full length. Setting the base radius is a
        // structural constraint (a child cannot be thicker than its parent), not a
        // second opinion about how a branch should grow.
        child.baseWidth = _rootRadius * Mathf.Clamp(forkWidthScale, 0.2f, 1f);
        child.tipWidth  = tipWidth;

        // Starts a little way along its own direction, so it emerges FROM the
        // runner's surface rather than from inside it.
        child.Begin(_rootCol, _tipCol, pos + dir * (_rootRadius * 1.8f), dir, 0);
    }

    // ── Bridge / runner mode ─────────────────────────────────────────────────

    // An arch from where this vine sprouted to another block in the cluster, with a
    // lateral meander laid over it.
    //
    // The arch height and the sway both use sin() over the span, which is zero at
    // BOTH ends — so the runner is guaranteed to leave its own block and arrive
    // exactly on its anchor no matter how the parameters are tuned. That is the
    // whole reason this is a parametric path rather than a steered walk: a walk
    // aimed at a target misses it, and a runner that misses is just a spike again.
    void BeginBridge(Color rootColor, Color tipColor)
    {
        _forks   = 0;
        _rootCol = rootColor;
        _tipCol  = tipColor;

        // A runner keeps its thickness end to end — it is one continuous stem that
        // happens to be lying across something, not a branch tapering into nothing.
        _rootRadius = baseWidth * 0.8f;
        _tipRadius  = baseWidth * 0.55f;
        _widthScale = 1f;

        _nodes.Clear();
        _nodes.Add(transform.position);

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(BridgeRoutine());
    }

    IEnumerator BridgeRoutine()
    {
        Vector3 a = _nodes[0];
        Vector3 b = bridgeTarget;

        Vector3 along = b - a;
        float span = along.magnitude;
        if (span < 0.05f) { _routine = null; yield break; }
        along /= span;

        Vector3 side = Vector3.Cross(along, Vector3.up);
        if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
        side.Normalize();

        int segs = Mathf.Clamp(Mathf.CeilToInt(span / Mathf.Max(0.05f, segmentLength)), 4, 40);
        Vector3 prev = a;

        for (int i = 1; i <= segs; i++)
        {
            float t  = i / (float)segs;
            float up = Mathf.Sin(t * Mathf.PI) * span * bridgeArc;
            float sw = Mathf.Sin(t * Mathf.PI * bridgeWaves) * span * bridgeSway;

            // Noise is faded out at both ends for the same reason the arch is: the
            // anchors have to be exact.
            float ends = Mathf.Sin(t * Mathf.PI);
            Vector3 next = Vector3.Lerp(a, b, t)
                         + Vector3.up * up
                         + side * sw
                         + Random.insideUnitSphere * (randomness * 0.4f * ends);

            _nodes.Add(prev);

            float dur = Mathf.Max(0.0001f, segmentGrowDuration);
            float e   = 0f;
            while (e < dur)
            {
                e += Time.deltaTime;
                _nodes[_nodes.Count - 1] = Vector3.Lerp(prev, next, e / dur);
                RebuildMesh();
                yield return null;
            }
            _nodes[_nodes.Count - 1] = next;
            RebuildMesh();
            prev = next;

            // Foliage runs the WHOLE length of a runner rather than massing at the
            // tip. A runner has no tip to speak of — both its ends are anchored —
            // and ivy along a span is what the eye reads as "grown across".
            Vector3 dir = (next - a).sqrMagnitude > 1e-5f ? along : Vector3.up;
            if (enableLeaves) SpawnLeavesAt(next, dir, 0.9f);
            if (enableBlossoms && Random.value < blossomChance * 0.6f) SpawnBlossomAt(next, 0.9f);

            // Side-shoots hang DOWN off the runner, the way a real one sends growth
            // toward the ground rather than back up along itself.
            if (enableForking && _prefab != null
                && _forks < maxForksPerBranch
                && i > 1 && i < segs - 1
                && Random.value < forkProbability * 0.5f)
            {
                SpawnFork(_rootCol, _tipCol, next, (side * (Random.value < 0.5f ? 1f : -1f) + Vector3.down * 0.8f).normalized, 1);
                _forks++;
            }
        }

        _routine = null;
    }

    // ── Coil / wrap mode ─────────────────────────────────────────────────────
    void BeginCoil(Color rootColor, Color tipColor)
    {
        _forks   = 0;
        _rootCol = rootColor;
        _tipCol  = tipColor;
        _rootRadius = baseWidth;
        _tipRadius  = Mathf.Max(tipWidth, baseWidth * 0.5f);   // a coil keeps its thickness
        _widthScale = 1f;

        _coilStartAngle = Random.value * Mathf.PI * 2f;

        _nodes.Clear();
        _nodes.Add(CoilPoint(0f));

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(CoilRoutine());
    }

    // f ∈ [0,1] from the base to the top of the helix. Traces the block's
    // RECTANGULAR cross-section (not a circle) plus a small surface gap, so the coil
    // stays glued to the flat faces and turns at the corners.
    Vector3 CoilPoint(float f)
    {
        float ang = _coilStartAngle + f * coilTurns * Mathf.PI * 2f;
        float ca  = Mathf.Cos(ang), sa = Mathf.Sin(ang);

        float tx = coilHalfX / Mathf.Max(1e-4f, Mathf.Abs(ca));
        float tz = coilHalfZ / Mathf.Max(1e-4f, Mathf.Abs(sa));
        float t  = Mathf.Min(tx, tz) + coilSurfaceGap;

        float y = (-0.45f + f) * coilHeight;
        return _coilCenter + new Vector3(ca * t, 0f, sa * t) + Vector3.up * y;
    }

    IEnumerator CoilRoutine()
    {
        int segs = Mathf.Max(8, coilSegments);
        Vector3 prev = CoilPoint(0f);

        for (int i = 1; i < segs; i++)
        {
            float f = (float)i / (segs - 1);
            Vector3 next = CoilPoint(f) + Vector3.up * (Random.Range(-randomness, randomness) * 0.4f);

            _nodes.Add(prev);

            float dur = Mathf.Max(0.0001f, segmentGrowDuration);
            float t   = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                _nodes[_nodes.Count - 1] = Vector3.Lerp(prev, next, t / dur);
                RebuildMesh();
                yield return null;
            }
            _nodes[_nodes.Count - 1] = next;
            RebuildMesh();
            prev = next;

            Vector3 outward = new Vector3(next.x - _coilCenter.x, 0f, next.z - _coilCenter.z);
            if (outward.sqrMagnitude < 1e-5f) outward = Vector3.up;
            outward.Normalize();

            // Coil leaves lie FLAT against the block face so the climbing vine stays
            // glued to the surface instead of poking leaves into open space.
            if (enableLeaves) SpawnSurfaceLeavesAt(next, outward, 0.85f);

            // Side-shoots off the coil. Without these a climbing vine is one bare
            // wire wrapped round a box; with them it reads as something growing ON
            // the block, which is the whole point of the coil.
            if (coilForks && enableForking && _prefab != null
                && _forks < maxForksPerBranch
                && i > 2 && i < segs - 2
                && Random.value < forkProbability * 0.6f)
            {
                SpawnFork(_rootCol, _tipCol, next, (outward + Vector3.up * 0.7f).normalized, 1);
                _forks++;
            }
        }

        _routine = null;
    }

    // ── Leaves ───────────────────────────────────────────────────────────────

    void SpawnSurfaceLeavesAt(Vector3 pos, Vector3 normal, float sizeScale)
    {
        if (leavesPerNode <= 0) return;
        if (normal.sqrMagnitude < 1e-4f) normal = Vector3.up;
        normal = normal.normalized;

        Vector3 tan = Vector3.up - normal * Vector3.Dot(Vector3.up, normal);
        if (tan.sqrMagnitude < 1e-4f) tan = Vector3.Cross(normal, Vector3.right);
        tan.Normalize();

        for (int k = 0; k < leavesPerNode; k++)
        {
            float roll     = Random.Range(0f, 360f);
            Vector3 leafUp = Quaternion.AngleAxis(roll, normal) * tan;
            Vector3 fwd    = (normal + leafUp * 0.15f).normalized;   // tiny lift off the face
            MakeLeaf(pos, Quaternion.LookRotation(fwd, leafUp), sizeScale);
        }
    }

    void SpawnLeavesAt(Vector3 pos, Vector3 growthDir, float sizeScale)
    {
        if (leavesPerNode <= 0) return;
        if (growthDir.sqrMagnitude < 1e-4f) growthDir = Vector3.up;
        growthDir = growthDir.normalized;

        Vector3 side = Vector3.Cross(growthDir, Vector3.up);
        if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
        side.Normalize();

        for (int k = 0; k < leavesPerNode; k++)
        {
            float fan = (k / Mathf.Max(1f, leavesPerNode - 1f) - 0.5f) * 2f;   // -1..1
            Vector3 outDir = Quaternion.AngleAxis(fan * 55f + Random.Range(-20f, 20f), growthDir) * side;
            SpawnLeaf(pos, outDir, sizeScale);
        }
    }

    void SpawnLeaf(Vector3 pos, Vector3 outDir, float sizeScale)
    {
        if (outDir.sqrMagnitude < 1e-4f) outDir = Vector3.up;
        outDir = outDir.normalized;

        Vector3 up = (outDir + Vector3.up * 0.6f).normalized;
        var rot = Quaternion.LookRotation(Random.onUnitSphere * 0.2f + outDir, up)
                * Quaternion.Euler(Random.Range(-25f, 25f), 0f, Random.Range(-20f, 20f));
        MakeLeaf(pos, rot, sizeScale);
    }

    void MakeLeaf(Vector3 pos, Quaternion rot, float sizeScale)
    {
        var go = new GameObject("VineLeaf");
        go.transform.SetParent(transform, false);
        go.transform.SetPositionAndRotation(pos, rot);

        go.AddComponent<MeshFilter>().sharedMesh =
            GetLeafMeshVariant(leafColor, leafTipColor, Random.Range(0, 3));
        ApplyRendererSettings(go.AddComponent<MeshRenderer>());

        float target = Random.Range(leafSize.x, leafSize.y) * Mathf.Max(0.4f, sizeScale);
        StartCoroutine(LeafGrow(go.transform, target));
    }

    void SpawnBlossomAt(Vector3 pos, float sizeScale)
    {
        var go = new GameObject("VineBlossom");
        go.transform.SetParent(transform, false);
        go.transform.SetPositionAndRotation(pos + Random.onUnitSphere * (blossomSize * 0.5f), Random.rotation);

        go.AddComponent<MeshFilter>().sharedMesh = GetBlossomMesh(blossomColor);
        ApplyRendererSettings(go.AddComponent<MeshRenderer>());

        float target = blossomSize * Mathf.Max(0.4f, sizeScale) * Random.Range(0.8f, 1.2f);
        StartCoroutine(LeafGrow(go.transform, target));
    }

    IEnumerator LeafGrow(Transform t, float target)
    {
        float dur = Mathf.Max(0.0001f, leafGrowDuration);
        float e = 0f;
        while (e < dur)
        {
            if (t == null) yield break;
            e += Time.deltaTime;
            float p = Mathf.Clamp01(e / dur);
            float s = 1f - (1f - p) * (1f - p);     // ease-out
            t.localScale = Vector3.one * (target * s);
            yield return null;
        }
        if (t != null) t.localScale = Vector3.one * target;
    }

    // Pointed leaf quad (base at origin → tip at +Y), vertex-coloured base→tip so the
    // leaf shades from deep to bright green. THREE tone variants (deep shade / mid /
    // bright new-growth) picked randomly per leaf, so a dense canopy reads as layered
    // foliage instead of one flat green.
    //
    // Normals point straight out of the quad rather than being recalculated from the
    // triangles: with the shader flipping them on back faces, a flat leaf then reads
    // as a leaf from either side instead of going black when the light is behind it.
    static Mesh GetLeafMeshVariant(Color baseCol, Color tipCol, int variant)
    {
        if (_leafMeshes == null)
        {
            _leafMeshes = new Mesh[3];
            for (int t = 0; t < 3; t++)
            {
                float k = t == 0 ? 0.72f : (t == 1 ? 1f : 1.22f);
                Color b = new Color(baseCol.r * k, baseCol.g * k, baseCol.b * k * 0.92f, 1f);
                Color p = new Color(Mathf.Min(1f, tipCol.r * k), Mathf.Min(1f, tipCol.g * k),
                                    Mathf.Min(1f, tipCol.b * k * 0.92f), 1f);

                var m = new Mesh { name = $"VineLeaf_{t}" };
                m.vertices = new[]
                {
                    new Vector3( 0f,    0f,  0f),
                    new Vector3(-0.45f, 0.5f, 0f),
                    new Vector3( 0f,    1f,  0f),
                    new Vector3( 0.45f, 0.5f, 0f),
                };
                m.colors  = new[] { b, Color.Lerp(b, p, 0.5f), p, Color.Lerp(b, p, 0.5f) };
                m.normals = new[] { -Vector3.forward, -Vector3.forward, -Vector3.forward, -Vector3.forward };
                m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                m.RecalculateBounds();
                _leafMeshes[t] = m;
            }
        }
        return _leafMeshes[Mathf.Clamp(variant, 0, 2)];
    }

    static Mesh GetBlossomMesh(Color petal)
    {
        if (_blossomMesh != null) return _blossomMesh;
        const int n = 5;
        var verts = new List<Vector3> { Vector3.zero };
        var cols  = new List<Color>   { Color.Lerp(petal, new Color(1f, 0.85f, 0.5f), 0.6f) };
        var nrms  = new List<Vector3> { -Vector3.forward };
        for (int k = 0; k < n; k++)
        {
            float a = k * 2f * Mathf.PI / n;
            verts.Add(new Vector3(Mathf.Cos(a) * 0.5f, Mathf.Sin(a) * 0.5f, 0f));
            cols.Add(petal);
            nrms.Add(-Vector3.forward);
        }
        var tris = new List<int>();
        for (int k = 0; k < n; k++) { tris.Add(0); tris.Add(1 + k); tris.Add(1 + (k + 1) % n); }

        _blossomMesh = new Mesh { name = "VineBlossom" };
        _blossomMesh.SetVertices(verts);
        _blossomMesh.SetColors(cols);
        _blossomMesh.SetNormals(nrms);
        _blossomMesh.SetTriangles(tris, 0);
        _blossomMesh.RecalculateBounds();
        return _blossomMesh;
    }

    // Graceful teardown: freeze growth, wither the whole bush, then destroy.
    public void Retire()
    {
        if (_retiring) return;
        _retiring = true;

        var effects = GetComponentsInChildren<VineEffect>(true);
        for (int i = 0; i < effects.Length; i++) effects[i].StopAllCoroutines();

        if (!isActiveAndEnabled) { Destroy(gameObject); return; }
        StartCoroutine(WitherRoutine());
    }

    IEnumerator WitherRoutine()
    {
        var bush = GetComponentsInChildren<VineEffect>(true);

        // Leaves and blossoms are separate objects with their own scale, so the
        // wither has to shrink them too — a bush that thins to bare wire and then
        // pops a full canopy out of existence is worse than no wither at all.
        var deco = new List<Transform>();
        var size = new List<Vector3>();
        foreach (var e in bush)
            foreach (Transform t in e.transform)
                if (t != null && t.GetComponent<VineEffect>() == null)
                { deco.Add(t); size.Add(t.localScale); }

        float dur = Mathf.Max(0.0001f, witherDuration);
        float t0  = 0f;
        while (t0 < dur)
        {
            t0 += Time.deltaTime;
            float k = 1f - t0 / dur;

            for (int i = 0; i < bush.Length; i++)
                if (bush[i] != null) { bush[i]._widthScale = k; bush[i].RebuildMesh(); }

            for (int i = 0; i < deco.Count; i++)
                if (deco[i] != null) deco[i].localScale = size[i] * k;

            yield return null;
        }

        Destroy(gameObject);
    }
}
