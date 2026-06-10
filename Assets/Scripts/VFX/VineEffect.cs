using System.Collections;
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

    [Header("Width")]
    [Tooltip("Width at the root.")]
    public float baseWidth = 0.06f;

    [Tooltip("Width at the tip (taper to near-zero for a branch look).")]
    public float tipWidth = 0.005f;

    [Header("Material")]
    [Tooltip("Optional line material. It MUST read vertex colors (default line / URP Particles-Unlit / Sprites-Default) for the brown→green gradient to show — a plain URP/Unlit material renders the line WHITE. Leave EMPTY to auto-use a built-in vertex-color material so the gradient always works.")]
    public Material lineMaterialOverride;

    [Header("Timing")]
    [Tooltip("Seconds to extend ONE segment. Total grow time ~ this x (segments - 1). Smaller = faster.")]
    public float segmentGrowDuration = 0.08f;

    [Tooltip("Seconds to wither (width -> 0) before the branch is destroyed on release.")]
    public float witherDuration = 0.25f;

    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorID     = Shader.PropertyToID("_Color");
    private static MaterialPropertyBlock _mpb;
    private static Material _lineMat;   // shared vertex-color material for all vines/forks

    private LineRenderer _line;
    private Coroutine    _routine;
    private bool         _retiring;
    private GameObject   _prefab;   // source prefab, propagated to forks (so forks clone the prefab, not a sub-tree)
    private int          _forks;

    private void Awake()
    {
        _line = GetComponent<LineRenderer>();
    }

    // Public entry — trunk (depth 0).
    //   rootColor = branch / body color (woody brown, or theme color).
    //   tipColor  = new-growth color shown near the end (green bud).
    //   outward   = world direction this vine should lean toward (piece - cluster center).
    //   prefab    = the vine prefab, so forks can spawn clean clones of it.
    public void Grow(Color rootColor, Color tipColor, Vector3 outward, GameObject prefab)
    {
        _prefab = prefab;
        if (_line == null) _line = GetComponent<LineRenderer>();
        Begin(rootColor, tipColor, transform.position, ComputeMainDir(outward), 0);
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

        Vector3 current = startPos;
        for (int i = 1; i < segs; i++)
        {
            Vector3 noise = new Vector3(
                Random.Range(-randomness, randomness),
                Random.Range(-randomness, randomness),
                Random.Range(-randomness, randomness));

            // Curve a little more outward each node for a spreading-out feel.
            Vector3 next = current
                         + mainDir  * (segmentLength * lengthScale)
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
            current = next;

            // Maybe sprout a fork at this node (not at the very base or tip).
            if (enableForking && _prefab != null
                && depth < maxForkDepth && _forks < maxForksPerBranch
                && i >= 1 && i <= segs - 2
                && Random.value < forkProbability)
            {
                SpawnFork(rootColor, tipColor, next, mainDir, depth);
                _forks++;
            }
        }

        _routine = null;
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
