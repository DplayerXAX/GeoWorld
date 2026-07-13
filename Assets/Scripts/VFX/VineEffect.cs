using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// One procedurally-grown branch/vine, drawn with a LineRenderer, with optional
// recursive sub-branches (forks). Spawned and owned by HarmonyVineVisualizer;
// the trunk is parented to the host block and forks are parented under the
// trunk, so destroying the block (or the trunk) takes the whole bush with it.
// Grow() animates the trunk outward node-by-node and may sprout forks along the
// way; Retire() withers the whole bush before destroy.
[RequireComponent(typeof(LineRenderer))]
public class VineEffect : MonoBehaviour
{
    [Header("Shape")]
    [Tooltip("Number of nodes along the trunk. More = longer and curvier. Forks use fewer.")]
    public int segments = 6;

    [Tooltip("Length added per segment, in world units.")]
    public float segmentLength = 0.25f;

    [Tooltip("Per-node wobble off the growth axis. Higher = wilder, lower = straighter.")]
    public float randomness = 0.12f;

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

    [Tooltip("Winding curl: degrees each node twists around the growth axis, alternating — gives climbing-vine sinuousness instead of a straight shoot.")]
    public float curlPerNodeDeg = 14f;

    [Header("Forking")]
    [Tooltip("Sprout recursive sub-branches while growing.")]
    public bool enableForking = true;

    [Range(0f, 1f)]
    [Tooltip("Chance to sprout a fork at each eligible node.")]
    public float forkProbability = 0.5f;

    [Tooltip("How many levels of sub-branches (0 = trunk only).")]
    public int maxForkDepth = 2;

    [Tooltip("Max forks a single branch may sprout.")]
    public int maxForksPerBranch = 3;

    [Range(0.2f, 1f)]
    [Tooltip("Each fork's length relative to its parent.")]
    public float forkLengthScale = 0.65f;

    [Range(0.2f, 1f)]
    [Tooltip("Each fork's width relative to its parent.")]
    public float forkWidthScale = 0.6f;

    [Tooltip("How far a fork veers off its parent's direction (degrees).")]
    public float forkAngleDeg = 40f;

    [Header("Coil / wrap around block")]
    [Tooltip("Climbing-vine mode: instead of branching outward, spiral the line UP AROUND the host block as a helix. The visualizer sets radius/height from the block footprint.")]
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

    [Header("Width")]
    [Tooltip("Width at the root.")]
    public float baseWidth = 0.06f;

    [Tooltip("Width at the tip (taper to near-zero for a branch look).")]
    public float tipWidth = 0.005f;

    [Header("Material")]
    [Tooltip("Optional line material. It MUST read vertex colors (default line / URP Particles-Unlit / Sprites-Default) for the brown→green gradient to show — a plain URP/Unlit material renders the line WHITE. Leave EMPTY to auto-use a built-in vertex-color material so the gradient always works.")]
    public Material lineMaterialOverride;

    [Header("Leaves")]
    [Tooltip("Sprout green leaves along the vine as it grows.")]
    public bool enableLeaves = true;
    [Tooltip("How many leaves to try sprouting at each node.")]
    [Range(0, 8)] public int leavesPerNode = 4;
    [Tooltip("Extra leaves massed at each branch TIP (a canopy tuft), so foliage clusters at the ends like a real bush instead of spreading evenly. 0 = off.")]
    [Range(0, 12)] public int tipCanopyLeaves = 6;
    [Tooltip("Leaf size range (world units, base→tip of one leaf).")]
    public Vector2 leafSize = new Vector2(0.09f, 0.17f);
    public Color leafColor    = new Color(0.27f, 0.62f, 0.20f);   // deep leaf green
    public Color leafTipColor = new Color(0.55f, 0.86f, 0.35f);   // bright new-growth green
    [Tooltip("Seconds for one leaf to scale in.")]
    public float leafGrowDuration = 0.18f;

    [Header("Blossoms")]
    [Tooltip("Sprinkle small pale blossoms among the leaves for a lush flowering-vine look.")]
    public bool enableBlossoms = true;
    [Range(0f, 1f)]
    [Tooltip("Chance per node to add a blossom.")]
    public float blossomChance = 0.22f;
    public Color blossomColor = new Color(0.95f, 0.92f, 0.72f);   // pale cream blossom
    [Tooltip("Blossom size (world units).")]
    public float blossomSize = 0.06f;

    [Header("Timing")]
    [Tooltip("Seconds to extend ONE segment. Total grow time ~ this x (segments - 1). Smaller = faster.")]
    public float segmentGrowDuration = 0.08f;

    [Tooltip("Seconds to wither (width -> 0) before the branch is destroyed on release.")]
    public float witherDuration = 0.25f;

    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorID     = Shader.PropertyToID("_Color");
    private static MaterialPropertyBlock _mpb;
    private static Material _lineMat;     // shared vertex-color material for all vines/forks
    private static Material _leafMat;     // shared vertex-color material for leaves
    private static Mesh[]   _leafMeshes;  // shared leaf quads in 3 green tones (deep / mid / bright)

    private LineRenderer _line;
    private Coroutine    _routine;
    private bool         _retiring;
    private GameObject   _prefab;   // source prefab, propagated to forks (so forks clone the prefab, not a sub-tree)
    private int          _forks;
    private Vector3      _coilCenter;       // host block world center (coil mode)
    private float        _coilStartAngle;   // random helix start azimuth
    private Color        _lastRoot, _lastTip;   // last Grow colors, for combat-ripple replay
    private Vector3      _mainDir;              // last outward growth dir (kept stable across replays)
    private Coroutine    _replayRoutine;

    private void Awake()
    {
        _line = GetComponent<LineRenderer>();
    }

    private void OnEnable()  { SynergyVisualFX.OnReplayGrowIn += HandleReplay; }
    private void OnDisable() { SynergyVisualFX.OnReplayGrowIn -= HandleReplay; }

    // Only the TRUNK replays (it rebuilds its own forks, so forks must not self-replay).
    private bool IsTrunk => transform.parent == null || transform.parent.GetComponent<VineEffect>() == null;

    private void HandleReplay(System.Func<Vector3, float> delayFor)
    {
        if (_retiring || !IsTrunk) return;
        float d = delayFor != null ? Mathf.Max(0f, delayFor(transform.position)) : 0f;

        // Drop existing forks (so they don't accumulate) and hide the trunk line.
        var subs = GetComponentsInChildren<VineEffect>(true);
        for (int i = 0; i < subs.Length; i++)
            if (subs[i] != null && subs[i] != this) Destroy(subs[i].gameObject);
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
        if (_line != null) _line.positionCount = 0;

        if (_replayRoutine != null) StopCoroutine(_replayRoutine);
        _replayRoutine = StartCoroutine(ReplayAfter(d));
    }

    private IEnumerator ReplayAfter(float d)
    {
        if (d > 0.001f) yield return new WaitForSeconds(d);
        if (_retiring) yield break;
        if (coilAround) BeginCoil(_lastRoot, _lastTip);
        else            Begin(_lastRoot, _lastTip, transform.position, _mainDir, 0);
    }

    // Public entry — trunk (depth 0).
    //   rootColor = branch / body color (woody brown, or theme color).
    //   tipColor  = new-growth color shown near the end (green bud).
    //   outward   = world direction this vine should lean toward (piece - cluster center).
    //   prefab    = the vine prefab, so forks can spawn clean clones of it.
    public void Grow(Color rootColor, Color tipColor, Vector3 outward, GameObject prefab)
    {
        Grow(rootColor, tipColor, outward, prefab, transform.position);
    }

    // Overload with an explicit coil center (the host block's world center). Used
    // when coilAround is on so the helix wraps the correct block.
    public void Grow(Color rootColor, Color tipColor, Vector3 outward, GameObject prefab, Vector3 coilCenter)
    {
        _prefab     = prefab;
        _coilCenter = coilCenter;
        _lastRoot   = rootColor;
        _lastTip    = tipColor;
        if (_line == null) _line = GetComponent<LineRenderer>();

        if (coilAround)
        {
            BeginCoil(rootColor, tipColor);
        }
        else
        {
            _mainDir = ComputeMainDir(outward);   // cache so a replay keeps the same shape
            Begin(rootColor, tipColor, transform.position, _mainDir, 0);
        }
    }

    private void Begin(Color rootColor, Color tipColor, Vector3 startPos, Vector3 mainDir, int depth)
    {
        if (_line == null) _line = GetComponent<LineRenderer>();

        // World space: positions are absolute, so the branch keeps its authored
        // size regardless of the host block's scale.
        _line.useWorldSpace = true;
        _forks = 0;

        // Guarantee a VERTEX-COLOR material so the brown→green gradient shows.
        // A plain URP/Unlit material ignores colorGradient and the line renders
        // white — assigning here makes the colors robust no matter what material
        // sits on the prefab.
        _line.sharedMaterial = lineMaterialOverride != null ? lineMaterialOverride : GetLineMaterial();

        ApplyColor(rootColor, tipColor);
        ApplyWidth(Mathf.Pow(Mathf.Clamp(forkWidthScale, 0.2f, 1f), depth));

        _line.positionCount = 1;
        _line.SetPosition(0, startPos);

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(GrowRoutine(rootColor, tipColor, startPos, mainDir, depth));
    }

    private void ApplyColor(Color rootColor, Color tipColor)
    {
        // Neutralize the material's base color so the per-length gradient shows its
        // TRUE colors. IMPORTANT: a two-color (brown -> green) line needs a
        // VERTEX-COLOR line material (the default line material, or URP
        // Particles/Unlit). On a plain URP/Unlit material the gradient is ignored
        // and the line shows white.
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        _line.GetPropertyBlock(_mpb);
        _mpb.SetColor(BaseColorID, Color.white);
        _mpb.SetColor(ColorID, Color.white);
        _line.SetPropertyBlock(_mpb);

        // Brown body that turns green near the tip (new growth / bud). The mid key
        // keeps most of the branch brown, so the green reads as a tip, not half
        // the vine.
        var grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(rootColor, 0f),
                new GradientColorKey(rootColor, 0.6f),
                new GradientColorKey(tipColor,  1f)
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        _line.colorGradient = grad;
    }

    // A material that READS VERTEX COLORS, so the LineRenderer's colorGradient
    // (the brown→green keys) actually shows. Shared across every vine/fork for
    // batching; the gradient is per-renderer, so sharing the material is safe.
    private static Material GetLineMaterial()
    {
        if (_lineMat != null) return _lineMat;
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
        _lineMat = new Material(sh) { name = "VineLine (runtime, vertex color)" };
        return _lineMat;
    }

    private void ApplyWidth(float widthScale)
    {
        // Taper root -> tip. Final width = widthCurve x widthMultiplier, so keep
        // the multiplier at 1 and bake the absolute widths into the curve.
        _line.widthCurve = new AnimationCurve(
            new Keyframe(0f, baseWidth * widthScale),
            new Keyframe(1f, tipWidth  * widthScale));
        _line.widthMultiplier = 1f;
    }

    // Trunk direction: horizontal outward + random azimuth fan + upward bias.
    private Vector3 ComputeMainDir(Vector3 outward)
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

    private IEnumerator GrowRoutine(Color rootColor, Color tipColor, Vector3 startPos, Vector3 mainDir, int depth)
    {
        float lengthScale = Mathf.Pow(Mathf.Clamp(forkLengthScale, 0.2f, 1f), depth);
        int   segs        = Mathf.Max(3, segments - depth);   // forks a touch shorter

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
            // Curl: twist the horizontal heading a little, alternating each node.
            if (curlPerNodeDeg > 0.01f)
            {
                float sign = (i & 1) == 0 ? 1f : -1f;
                growDir = Quaternion.AngleAxis(curlPerNodeDeg * sign, Vector3.up) * growDir;
            }
            // Gravity: bend the heading downward, more toward the tip (weeping arc).
            growDir = (growDir + Vector3.down * (gravityDroop * (i / (float)segs))).normalized;

            Vector3 noise = new Vector3(
                Random.Range(-randomness, randomness),
                Random.Range(-randomness, randomness),
                Random.Range(-randomness, randomness));

            Vector3 next = current
                         + growDir  * (segmentLength * lengthScale)
                         + driftDir * (outwardDrift * i * lengthScale)
                         + noise    * lengthScale;

            _line.positionCount = i + 1;

            float dur = Mathf.Max(0.0001f, segmentGrowDuration);
            float t   = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                _line.SetPosition(i, Vector3.Lerp(current, next, t / dur));
                yield return null;
            }
            _line.SetPosition(i, next);
            lastNode = current;
            current  = next;

            if (enableLeaves) SpawnLeavesAt(next, growDir, lengthScale);
            if (enableBlossoms && Random.value < blossomChance) SpawnBlossomAt(next, lengthScale);

            // Maybe sprout a fork at this node (not at the very base or tip).
            if (enableForking && _prefab != null
                && depth < maxForkDepth && _forks < maxForksPerBranch
                && i >= 1 && i <= segs - 2
                && Random.value < forkProbability)
            {
                SpawnFork(rootColor, tipColor, next, growDir, depth);
                _forks++;
            }
        }

        // Tip canopy: mass a tuft of leaves (+ a blossom) at the branch end, so
        // foliage clusters at the tips the way a real bush's does.
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

    private static Vector3 Perpendicular(Vector3 v)
    {
        Vector3 p = Vector3.Cross(v, Vector3.up);
        if (p.sqrMagnitude < 1e-4f) p = Vector3.right;
        return p.normalized;
    }

    private void SpawnFork(Color rootColor, Color tipColor, Vector3 atPos, Vector3 parentDir, int depth)
    {
        // Clone the PREFAB (not this sub-tree) so forks never duplicate existing
        // branches. Parent under this branch so the whole bush is one hierarchy.
        GameObject clone = Instantiate(_prefab, transform);
        if (!clone.TryGetComponent<VineEffect>(out var child))
        {
            Destroy(clone);
            return;
        }
        child._prefab = _prefab;
        child.Begin(rootColor, tipColor, atPos, DeviateDirection(parentDir), depth + 1);
    }

    private Vector3 DeviateDirection(Vector3 dir)
    {
        float sign = Random.value < 0.5f ? -1f : 1f;
        float az   = forkAngleDeg * sign * Mathf.Deg2Rad;
        float ca   = Mathf.Cos(az), sa = Mathf.Sin(az);
        Vector3 d  = new Vector3(dir.x * ca - dir.z * sa, dir.y, dir.x * sa + dir.z * ca);
        return (d + Vector3.up * 0.3f).normalized;   // forks lift a little
    }

    // ── Coil / wrap mode ─────────────────────────────────────────────────────
    // A climbing-vine variant: instead of the outward branch walk, spiral the
    // line UP AROUND the host block as a helix. World-space, so the block's
    // GrowIn scale and rotation don't distort it; parenting only ties its
    // lifetime to the block. No forks — a clean single coil.
    private void BeginCoil(Color rootColor, Color tipColor)
    {
        if (_line == null) _line = GetComponent<LineRenderer>();
        _line.useWorldSpace = true;
        _forks = 0;

        _line.sharedMaterial = lineMaterialOverride != null ? lineMaterialOverride : GetLineMaterial();
        ApplyColor(rootColor, tipColor);
        ApplyWidth(1f);

        _coilStartAngle = Random.value * Mathf.PI * 2f;

        _line.positionCount = 1;
        _line.SetPosition(0, CoilPoint(0f));

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(CoilRoutine());
    }

    // f ∈ [0,1] from the base to the top of the helix. Traces the block's
    // RECTANGULAR cross-section (not a circle) plus a small surface gap, so the
    // coil stays glued to the flat faces and turns at the corners.
    private Vector3 CoilPoint(float f)
    {
        float ang = _coilStartAngle + f * coilTurns * Mathf.PI * 2f;
        float ca  = Mathf.Cos(ang), sa = Mathf.Sin(ang);

        // Distance from center to the rectangle border along (ca, sa).
        float tx = coilHalfX / Mathf.Max(1e-4f, Mathf.Abs(ca));
        float tz = coilHalfZ / Mathf.Max(1e-4f, Mathf.Abs(sa));
        float t  = Mathf.Min(tx, tz) + coilSurfaceGap;

        float y = (-0.45f + f) * coilHeight;   // start just below center, climb over the top
        return _coilCenter + new Vector3(ca * t, 0f, sa * t) + Vector3.up * y;
    }

    private IEnumerator CoilRoutine()
    {
        int segs = Mathf.Max(8, coilSegments);
        Vector3 prev = CoilPoint(0f);

        for (int i = 1; i < segs; i++)
        {
            float f = (float)i / (segs - 1);
            // Only a little VERTICAL jitter — horizontal stays exactly on the face
            // so the coil keeps hugging the block instead of drifting off it.
            Vector3 next = CoilPoint(f) + Vector3.up * (Random.Range(-randomness, randomness) * 0.4f);

            _line.positionCount = i + 1;

            float dur = Mathf.Max(0.0001f, segmentGrowDuration);
            float t   = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                _line.SetPosition(i, Vector3.Lerp(prev, next, t / dur));
                yield return null;
            }
            _line.SetPosition(i, next);
            prev = next;

            // Coil leaves lie FLAT against the block face (leaf plane ⟂ the outward
            // normal), so the climbing vine stays glued to the surface and doesn't
            // poke leaves out into open space.
            if (enableLeaves)
            {
                Vector3 outward = new Vector3(next.x - _coilCenter.x, 0f, next.z - _coilCenter.z);
                if (outward.sqrMagnitude < 1e-5f) outward = Vector3.up;
                SpawnSurfaceLeavesAt(next, outward.normalized, 0.85f);
            }
        }

        _routine = null;
    }

    // Leaves pressed FLAT onto a surface whose outward normal is `normal` — used
    // by the coil (climbing-vine) mode. Each leaf lies in the face's tangent
    // plane, pointing in a random tangential direction with a tiny outward tilt,
    // so foliage spreads ACROSS the block face instead of standing off it.
    private void SpawnSurfaceLeavesAt(Vector3 pos, Vector3 normal, float sizeScale)
    {
        if (leavesPerNode <= 0) return;
        if (normal.sqrMagnitude < 1e-4f) normal = Vector3.up;
        normal = normal.normalized;

        // A stable up-ish tangent (project world up into the face plane).
        Vector3 tan = Vector3.up - normal * Vector3.Dot(Vector3.up, normal);
        if (tan.sqrMagnitude < 1e-4f) tan = Vector3.Cross(normal, Vector3.right);
        tan.Normalize();

        for (int k = 0; k < leavesPerNode; k++)
        {
            var go = new GameObject("VineLeaf");
            go.transform.SetParent(transform, false);
            go.transform.position = pos;

            go.AddComponent<MeshFilter>().sharedMesh = GetLeafMeshVariant(leafColor, leafTipColor, Random.Range(0, 3));
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial    = GetLeafMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows    = false;

            // Leaf lies in the tangent plane: local +Z = outward normal (small tilt
            // so it isn't perfectly flush), local +Y spins around the normal so
            // leaves fan across the surface.
            float roll  = Random.Range(0f, 360f);
            Vector3 leafUp = Quaternion.AngleAxis(roll, normal) * tan;
            Vector3 fwd    = (normal + leafUp * 0.15f).normalized;   // tiny lift off the face
            go.transform.rotation = Quaternion.LookRotation(fwd, leafUp);

            float target = Random.Range(leafSize.x, leafSize.y) * Mathf.Max(0.4f, sizeScale);
            StartCoroutine(LeafGrow(go.transform, target));
        }
    }

    // ── Leaves ───────────────────────────────────────────────────────────────
    // Sprout a few green leaf quads at a vine node, fanned around the growth axis
    // and tilted up/out, each scaling in. Parented to the vine so they die with it.
    private void SpawnLeavesAt(Vector3 pos, Vector3 growthDir, float sizeScale)
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

    // One leaf pointing along outDir (relative to the vine), tilted up + out,
    // random roll, scaling in. Shared by the per-node fan and the tip canopy.
    private void SpawnLeaf(Vector3 pos, Vector3 outDir, float sizeScale)
    {
        if (outDir.sqrMagnitude < 1e-4f) outDir = Vector3.up;
        outDir = outDir.normalized;

        var go = new GameObject("VineLeaf");
        go.transform.SetParent(transform, false);
        go.transform.position = pos;

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = GetLeafMeshVariant(leafColor, leafTipColor, Random.Range(0, 3));
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial    = GetLeafMaterial();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows    = false;

        Vector3 up = (outDir + Vector3.up * 0.6f).normalized;
        go.transform.rotation = Quaternion.LookRotation(Random.onUnitSphere * 0.2f + outDir, up)
                              * Quaternion.Euler(Random.Range(-25f, 25f), 0f, Random.Range(-20f, 20f));

        float target = Random.Range(leafSize.x, leafSize.y) * Mathf.Max(0.4f, sizeScale);
        StartCoroutine(LeafGrow(go.transform, target));
    }

    // A small pale blossom (a flat hex flower) nestled at a vine node.
    private void SpawnBlossomAt(Vector3 pos, float sizeScale)
    {
        var go = new GameObject("VineBlossom");
        go.transform.SetParent(transform, false);
        go.transform.position = pos + Random.onUnitSphere * (blossomSize * 0.5f);
        go.transform.rotation = Random.rotation;

        go.AddComponent<MeshFilter>().sharedMesh = GetBlossomMesh(blossomColor);
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial    = GetLeafMaterial();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows    = false;

        float target = blossomSize * Mathf.Max(0.4f, sizeScale) * Random.Range(0.8f, 1.2f);
        StartCoroutine(LeafGrow(go.transform, target));
    }

    private IEnumerator LeafGrow(Transform t, float target)
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

    // Pointed leaf quad (base at origin → tip at +Y), vertex-coloured base→tip so
    // the leaf shades from deep to bright green. THREE tone variants (deep shade /
    // mid / bright new-growth) picked randomly per leaf, so a dense canopy reads
    // as layered foliage instead of one flat green. Baked once from the first
    // vine's colors and shared by every leaf after that.
    private static Mesh GetLeafMeshVariant(Color baseCol, Color tipCol, int variant)
    {
        if (_leafMeshes == null)
        {
            _leafMeshes = new Mesh[3];
            for (int t = 0; t < 3; t++)
            {
                // 0 = deep understory, 1 = as authored, 2 = bright sunlit tip.
                float k = t == 0 ? 0.72f : (t == 1 ? 1f : 1.22f);
                Color b = new Color(baseCol.r * k, baseCol.g * k, baseCol.b * k * 0.92f, 1f);
                Color p = new Color(Mathf.Min(1f, tipCol.r * k), Mathf.Min(1f, tipCol.g * k), Mathf.Min(1f, tipCol.b * k * 0.92f), 1f);

                var m = new Mesh { name = $"VineLeaf_{t}" };
                m.vertices = new[]
                {
                    new Vector3( 0f,   0f,  0f),    // base
                    new Vector3(-0.45f, 0.5f, 0f),  // left
                    new Vector3( 0f,   1f,  0f),    // tip
                    new Vector3( 0.45f, 0.5f, 0f),  // right
                };
                m.colors = new[] { b, Color.Lerp(b, p, 0.5f), p, Color.Lerp(b, p, 0.5f) };
                m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                m.RecalculateNormals();
                m.RecalculateBounds();
                _leafMeshes[t] = m;
            }
        }
        return _leafMeshes[Mathf.Clamp(variant, 0, 2)];
    }

    private static Material GetLeafMaterial()
    {
        if (_leafMat != null) return _leafMat;
        Shader sh = Shader.Find("Sprites/Default")
                 ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                 ?? Shader.Find("Universal Render Pipeline/Unlit");
        _leafMat = new Material(sh) { name = "VineLeaf (runtime, vertex color)" };
        return _leafMat;
    }

    // Flat 5-petal hex blossom (unit radius), vertex-coloured pale with a warmer
    // center. Shared across all blossoms.
    private static Mesh _blossomMesh;
    private static Mesh GetBlossomMesh(Color petal)
    {
        if (_blossomMesh != null) return _blossomMesh;
        const int n = 5;
        var verts = new List<Vector3> { Vector3.zero };
        var cols  = new List<Color>  { Color.Lerp(petal, new Color(1f, 0.85f, 0.5f), 0.6f) };
        for (int k = 0; k < n; k++)
        {
            float a = k * 2f * Mathf.PI / n;
            verts.Add(new Vector3(Mathf.Cos(a) * 0.5f, Mathf.Sin(a) * 0.5f, 0f));
            cols.Add(petal);
        }
        var tris = new List<int>();
        for (int k = 0; k < n; k++) { tris.Add(0); tris.Add(1 + k); tris.Add(1 + (k + 1) % n); }

        _blossomMesh = new Mesh { name = "VineBlossom" };
        _blossomMesh.SetVertices(verts);
        _blossomMesh.SetColors(cols);
        _blossomMesh.SetTriangles(tris, 0);
        _blossomMesh.RecalculateNormals();
        _blossomMesh.RecalculateBounds();
        return _blossomMesh;
    }

    // Graceful teardown: freeze growth, wither the whole bush's width to 0, then
    // destroy. Called by HarmonyVineVisualizer when the piece leaves the synergy.
    public void Retire()
    {
        if (_retiring) return;
        _retiring = true;

        // Stop growth on this branch and every fork beneath it.
        var effects = GetComponentsInChildren<VineEffect>(true);
        for (int i = 0; i < effects.Length; i++) effects[i].StopAllCoroutines();

        if (!isActiveAndEnabled)
        {
            Destroy(gameObject);
            return;
        }
        StartCoroutine(WitherRoutine());
    }

    private IEnumerator WitherRoutine()
    {
        var lines = GetComponentsInChildren<LineRenderer>(true);
        var baseMul = new float[lines.Length];
        for (int i = 0; i < lines.Length; i++)
            baseMul[i] = lines[i] != null ? lines[i].widthMultiplier : 0f;

        float dur = Mathf.Max(0.0001f, witherDuration);
        float t   = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = 1f - t / dur;
            for (int i = 0; i < lines.Length; i++)
                if (lines[i] != null) lines[i].widthMultiplier = baseMul[i] * k;
            yield return null;
        }

        Destroy(gameObject);
    }
}
