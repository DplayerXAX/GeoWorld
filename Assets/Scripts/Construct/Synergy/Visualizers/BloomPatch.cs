using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// One "bloom patch": a small field of GEOMETRIC flowers that open on the top
// faces of ONE claimed Abundance (丰饶) block. Spawned + owned by
// AbundanceVisualizer — the same relationship ConstellationView has with the
// Enlightenment visualizer.
//
// WHY FREE-STANDING (not parented to the block): blocks can be ROTATED (the
// player flips pieces with 90° Euler turns) and are SCALED 0→1 by the GrowIn
// pop. A flower built from real upright mesh geometry would be tipped sideways
// by a rotated parent and collapsed by a scale-0 parent. So — exactly like
// ConstellationView — this lives on a FREE world-space GameObject whose lifetime
// the visualizer's Reconcile owns (prune → Retire). The flowers therefore always
// stand straight up in WORLD space, whatever the block underneath is doing.
//
// The flowers are flat, bold, radially-symmetric mesh rosettes (Constructivism /
// silkscreen, not fuzzy sprites): two interleaved rings of diamond petals tilted
// into a shallow cup, with a small raised golden core (the "fruit / grain"). They
// pop open in a staggered wave (EaseOutBack), then slowly turn and sway like a
// field in wind. Petals + core are two child renderers sharing ONE opaque unlit
// material, each tinted by its own MaterialPropertyBlock — so the two-tone needs
// no per-flower material and no texture.
[DisallowMultipleComponent]
public class BloomPatch : MonoBehaviour
{
    // ── Animation config (set by the visualizer before Grow) ─────────────────
    public float bloomDuration  = 0.5f;    // seconds for ONE flower to open
    public float bloomStagger   = 0.06f;   // delay added per flower (blooming wave)
    public float spinSpeed      = 16f;     // slow turn around the stem, deg/sec
    public float swaySpeed      = 1.5f;    // wind sway speed
    public float swayAngleDeg   = 7f;      // max sway tilt, degrees
    public float bobAmplitude   = 0.03f;   // vertical bob, WORLD units (caller scales by cell size)
    public float bobSpeed       = 1.2f;
    public float witherDuration = 0.35f;   // seconds to wilt before destroy

    sealed class Flower
    {
        public Transform t;          // flower root (scaled / turned / swayed as a whole)
        public Vector3   basePos;    // local rest position (root sits at patch center)
        public float     size;       // per-flower world size (radius)
        public float     bloomDelay; // when this flower starts opening
        public float     phase;      // sway + spin phase offset
    }

    readonly List<Flower> _flowers = new();
    float _born;
    bool  _built;
    bool  _retiring;
    float _witherStart;

    static readonly int _ColorID     = Shader.PropertyToID("_Color");
    static readonly int _BaseColorID = Shader.PropertyToID("_BaseColor");

    // Build the patch. cellTopsWorld = world center of each top face to plant on
    // (the caller already filtered out bare cells). Each cell grows a RANDOM 1..
    // maxFlowersPerCell flowers, each tinted with a random color from petalPalette
    // (an analogous set, so the field is multi-color but cohesive) and a shared
    // golden core. flowerSizeWorld is the flower's outer radius in world units,
    // scatterWorld the spread on the top face (caller scaled both by cell size).
    public void Grow(Vector3[] cellTopsWorld, Color[] petalPalette, Color centerColor,
                     int maxFlowersPerCell, float flowerSizeWorld, float scatterWorld, int maxFlowers)
    {
        Clear();
        _born     = Time.time;
        _retiring = false;

        if (cellTopsWorld == null || cellTopsWorld.Length == 0 ||
            petalPalette == null || petalPalette.Length == 0) { _built = true; return; }

        // Root sits at the patch center (average of the tops); flowers are placed
        // in its local space. Root stays identity, so local == world for them.
        Vector3 center = Vector3.zero;
        for (int i = 0; i < cellTopsWorld.Length; i++) center += cellTopsWorld[i];
        center /= cellTopsWorld.Length;
        transform.SetPositionAndRotation(center, Quaternion.identity);
        transform.localScale = Vector3.one;

        Mesh     petalMesh  = GetPetalMesh();
        Mesh     centerMesh = GetCenterMesh();
        Material mat        = GetFlowerMaterial();

        const float golden = 2.39996323f;   // golden angle — even scatter + decorrelated phases
        int maxPer = Mathf.Max(1, maxFlowersPerCell);
        int made = 0, gi = 0;

        for (int c = 0; c < cellTopsWorld.Length && made < maxFlowers; c++)
        {
            Vector3 top = cellTopsWorld[c];

            // Random density per cell (1..max) so the field isn't a uniform carpet.
            int count = Mathf.Clamp(1 + Mathf.FloorToInt(Hash01(c * 2749 + 13) * maxPer), 1, maxPer);

            for (int f = 0; f < count && made < maxFlowers; f++, gi++)
            {
                float ang = gi * golden;
                float rad = scatterWorld * Mathf.Sqrt((f + 0.5f) / count);
                Vector3 world = top + new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);

                Color petal = petalPalette[Mathf.Min(petalPalette.Length - 1,
                                Mathf.FloorToInt(Hash01(gi * 7919 + 101) * petalPalette.Length))];

                var go = new GameObject("Flower");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = world - center;
                go.transform.localScale    = Vector3.zero;   // opens in Update

                // Petals + golden core: two child renderers, one shared material,
                // independent tints via per-renderer MPB.
                NewMeshChild("Petals", go.transform, petalMesh,  mat, petal);
                NewMeshChild("Core",   go.transform, centerMesh, mat, centerColor);

                _flowers.Add(new Flower
                {
                    t          = go.transform,
                    basePos    = go.transform.localPosition,
                    size       = flowerSizeWorld * Mathf.Lerp(0.82f, 1.18f, Hash01(gi * 9277 + 3)),
                    bloomDelay = made * bloomStagger,
                    phase      = gi * golden,
                });
                made++;
            }
        }

        _built = true;
    }

    // Graceful teardown: wilt the whole patch to 0, then destroy. Called by
    // AbundanceVisualizer when the piece leaves the synergy.
    public void Retire()
    {
        if (_retiring) return;
        _retiring    = true;
        _witherStart = Time.time;
        if (!_built || _flowers.Count == 0) Destroy(gameObject);
    }

    void Update()
    {
        if (!_built) return;

        float wither = 1f;
        if (_retiring)
        {
            float wt = witherDuration > 1e-4f ? (Time.time - _witherStart) / witherDuration : 1f;
            wither = 1f - Mathf.Clamp01(wt);
            if (wither <= 0f) { Destroy(gameObject); return; }
        }

        float now = Time.time;
        for (int i = 0; i < _flowers.Count; i++)
        {
            var fl = _flowers[i];
            if (fl.t == null) continue;

            // Staggered overshoot bloom.
            float bt    = bloomDuration > 1e-4f ? (now - _born - fl.bloomDelay) / bloomDuration : 1f;
            float bloom = bt <= 0f ? 0f : (bt >= 1f ? 1f : EaseOutBack(bt));
            fl.t.localScale = Vector3.one * Mathf.Max(0f, fl.size * bloom * wither);

            // Stand upright in WORLD space (root is identity, so local == world),
            // slowly turning around the stem with a gentle wind sway.
            float spinY = (now - _born) * spinSpeed + fl.phase * Mathf.Rad2Deg;
            float swayX = Mathf.Sin(now * swaySpeed + fl.phase) * swayAngleDeg;
            float swayZ = Mathf.Cos(now * swaySpeed * 0.8f + fl.phase) * swayAngleDeg * 0.5f;
            fl.t.localRotation = Quaternion.Euler(swayX, spinY, swayZ);

            float bob = Mathf.Sin(now * bobSpeed + fl.phase * 1.3f) * bobAmplitude;
            fl.t.localPosition = fl.basePos + Vector3.up * bob;
        }
    }

    // 0→1 with a ~10% spring past 1 near the end.
    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float xm = x - 1f;
        return 1f + c3 * xm * xm * xm + c1 * xm * xm;
    }

    void NewMeshChild(string name, Transform parent, Mesh mesh, Material mat, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial       = mat;
        mr.shadowCastingMode    = ShadowCastingMode.Off;
        mr.receiveShadows       = false;
        mr.lightProbeUsage      = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

        var mpb = new MaterialPropertyBlock();
        mpb.SetColor(_BaseColorID, color);
        mpb.SetColor(_ColorID, color);
        mr.SetPropertyBlock(mpb);
    }

    // ── Teardown ─────────────────────────────────────────────────────────────

    void OnDestroy() => Clear();

    void Clear()
    {
        _built = false;
        for (int i = 0; i < _flowers.Count; i++)
            if (_flowers[i] != null && _flowers[i].t != null) Destroy(_flowers[i].t.gameObject);
        _flowers.Clear();
    }

    // ── Shared (static) GPU assets — built once, reused by every flower ───────
    // hideFlags.DontSave keeps them out of the scene / asset DB; they live for
    // the process and are intentionally never destroyed per-patch.

    static Mesh     _petalMesh;
    static Mesh     _centerMesh;
    static Material _flowerMat;

    // Opaque, unlit, flat-shaded, double-sided — the Constructivism look (bold
    // solid color, crisp edges, correct ZWrite sorting for overlapping petals).
    // Color comes per-renderer from MPB _BaseColor, so ONE material serves every
    // flower and tint. Falls back to Sprites/Default if URP/Unlit is missing.
    static Material GetFlowerMaterial()
    {
        if (_flowerMat != null) return _flowerMat;
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        _flowerMat = new Material(sh) { hideFlags = HideFlags.DontSave };
        if (_flowerMat.HasProperty("_Cull")) _flowerMat.SetFloat("_Cull", 0f);   // render both sides
        return _flowerMat;
    }

    // Two interleaved rings of flat diamond petals, tilted up into a shallow cup.
    // Built in a unit space (outer radius ≈ 1) and pointing up +Y; the flower's
    // transform scales it to world size.
    static Mesh GetPetalMesh()
    {
        if (_petalMesh != null) return _petalMesh;

        var verts = new List<Vector3>();
        var tris  = new List<int>();
        //          count az0   reach tilt  len   wid   inner
        AddPetalRing(verts, tris, 6, 0f,  22f, 1.00f, 0.34f, 0.10f);   // outer, flatter
        AddPetalRing(verts, tris, 6, 30f, 44f, 0.60f, 0.24f, 0.07f);   // inner, steeper, interleaved

        _petalMesh = new Mesh { name = "BloomPetals" };
        _petalMesh.SetVertices(verts);
        _petalMesh.SetTriangles(tris, 0);
        _petalMesh.RecalculateNormals();
        _petalMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);   // never frustum-cull
        return _petalMesh;
    }

    static void AddPetalRing(List<Vector3> verts, List<int> tris,
                             int count, float az0Deg, float tiltDeg, float len, float wid, float inner)
    {
        for (int i = 0; i < count; i++)
        {
            float az  = (az0Deg + i * 360f / count) * Mathf.Deg2Rad;
            float tlt = tiltDeg * Mathf.Deg2Rad;

            Vector3 radial = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));
            Vector3 side   = new Vector3(-Mathf.Sin(az), 0f, Mathf.Cos(az));
            Vector3 lengthAxis = radial * Mathf.Cos(tlt) + Vector3.up * Mathf.Sin(tlt);

            Vector3 baseP = radial * inner;
            Vector3 tip   = baseP + lengthAxis * len;
            Vector3 mid   = baseP + lengthAxis * (len * 0.45f);
            Vector3 left  = mid - side * wid;
            Vector3 right = mid + side * wid;

            int v = verts.Count;
            verts.Add(baseP); verts.Add(left); verts.Add(right); verts.Add(tip);
            // diamond = two tris (double-sided via material Cull Off).
            tris.Add(v); tris.Add(v + 1); tris.Add(v + 3);
            tris.Add(v); tris.Add(v + 3); tris.Add(v + 2);
        }
    }

    // A small raised polygon disc — the golden core / grain / fruit.
    static Mesh GetCenterMesh()
    {
        if (_centerMesh != null) return _centerMesh;

        const int n = 6;
        const float cy = 0.16f, cr = 0.22f;
        var verts = new List<Vector3> { new(0f, cy, 0f) };
        for (int k = 0; k < n; k++)
        {
            float a = k * 2f * Mathf.PI / n;
            verts.Add(new Vector3(Mathf.Cos(a) * cr, cy, Mathf.Sin(a) * cr));
        }

        var tris = new List<int>();
        for (int k = 0; k < n; k++)
        {
            tris.Add(0);
            tris.Add(1 + k);
            tris.Add(1 + (k + 1) % n);
        }

        _centerMesh = new Mesh { name = "BloomCore" };
        _centerMesh.SetVertices(verts);
        _centerMesh.SetTriangles(tris, 0);
        _centerMesh.RecalculateNormals();
        _centerMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
        return _centerMesh;
    }

    // Deterministic hash → [0,1] for stable per-flower jitter.
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
}
