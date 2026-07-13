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
// FLOWER CONSTRUCTION (geometric-print look, no textures, no lighting):
// the material is UNLIT, so all "shading" is authored explicitly — every flower
// is a stack of flat mesh layers in separately-tinted children, like layers of
// a silkscreen print or paper-cut (剪纸):
//   • Under   — a larger, darker, flat rosette rotated a half-step behind the
//               main petals (silhouette depth).
//   • PetalsA/PetalsB — the main petals are KITES FOLDED down their length
//               axis; the two halves are separate meshes tinted light/dark, so
//               each petal reads as a crisp origami crease (fake lighting, on
//               purpose — the flat-art idiom).
//   • Ring    — a thin pale annulus framing the rosette (the "registration
//               circle" of a print), grand archetype only.
//   • Core + CoreDot — golden hex heart with a small dark hex printed on top.
// Three archetypes (grand 6-petal / 8-petal mandala / small 5-petal daisy) are
// hash-picked per flower so a field reads as a garden, not a stamp sheet.
// Flowers pop open in a staggered wave (EaseOutBack) while UNFURLING — a small
// extra yaw that eases out as the bloom completes — then slowly turn and sway.
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
    public float unfurlDegrees  = 45f;     // extra yaw the flower unwinds while opening
    public float stemHeight     = 0.3f;    // WORLD height of the stalk the flower head rides on (caller scales)
    public Color stemColor      = new Color(0.20f, 0.42f, 0.16f, 1f);  // stalk green

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

        Material mat = GetFlowerMaterial();

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

                // Archetype mix: mostly small daisies, sprinkled mandalas, the
                // occasional grand bloom — a garden, not a stamp sheet.
                float roll = Hash01(gi * 4931 + 57);
                int arch = roll < 0.42f ? 2 : (roll < 0.75f ? 1 : 0);
                var A = GetArchetype(arch);

                // Flower HEAD sits atop a stalk, so the bloom reads taller and
                // clears the block face. A thin green stem (a static cross-quad
                // parented to the PATCH, not the head) connects the surface to the
                // head so it doesn't look like it's floating; the head still
                // nods/sways on top.
                float stemH = stemHeight * Mathf.Lerp(0.85f, 1.15f, Hash01(gi * 613 + 29));
                Vector3 basePos = world - center;
                Vector3 headPos = basePos + Vector3.up * stemH;
                if (stemH > 1e-3f) NewStem(basePos, stemH, Mathf.Max(0.01f, flowerSizeWorld * 0.06f), stemColor);

                var go = new GameObject("Flower");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = headPos;
                go.transform.localScale    = Vector3.zero;   // opens in Update

                // Layered print: each layer is one child renderer, one shared
                // material, its own MPB tint (dark → light stacking order).
                if (A.under != null)
                    NewMeshChild("Under", go.transform, A.under, mat, Scale(petal, 0.52f));
                NewMeshChild("PetalsB", go.transform, A.petalsB, mat, Scale(petal, 0.72f));
                NewMeshChild("PetalsA", go.transform, A.petalsA, mat, petal);
                if (A.ring != null)
                    NewMeshChild("Ring", go.transform, A.ring, mat, Color.Lerp(petal, Color.white, 0.55f));
                NewMeshChild("Core", go.transform, A.core, mat, centerColor);
                if (A.coreDot != null)
                    NewMeshChild("CoreDot", go.transform, A.coreDot, mat, Scale(centerColor, 0.55f));

                _flowers.Add(new Flower
                {
                    t          = go.transform,
                    basePos    = headPos,
                    size       = flowerSizeWorld * A.scale * Mathf.Lerp(0.85f, 1.15f, Hash01(gi * 9277 + 3)),
                    bloomDelay = made * bloomStagger,
                    phase      = gi * golden,
                });
                made++;
            }
        }

        _built = true;
    }

    static Color Scale(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

    // Graceful teardown: wilt the whole patch to 0, then destroy. Called by
    // AbundanceVisualizer when the piece leaves the synergy.
    public void Retire()
    {
        if (_retiring) return;
        _retiring    = true;
        _witherStart = Time.time;
        if (!_built || _flowers.Count == 0) Destroy(gameObject);
    }

    // ── Combat-ripple replay: re-bloom in sync with the block sprout ─────────
    void OnEnable()  { SynergyVisualFX.OnReplayGrowIn += HandleReplay; }
    void OnDisable() { SynergyVisualFX.OnReplayGrowIn -= HandleReplay; }

    void HandleReplay(System.Func<Vector3, float> delayFor)
    {
        if (!_built || _retiring) return;
        float d = delayFor != null ? Mathf.Max(0f, delayFor(transform.position)) : 0f;
        _born = Time.time + d;   // flowers collapse to 0, then re-bloom when the ripple arrives
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
            float btC   = Mathf.Clamp01(bt);
            float bloom = bt <= 0f ? 0f : (bt >= 1f ? 1f : EaseOutBack(bt));
            fl.t.localScale = Vector3.one * Mathf.Max(0f, fl.size * bloom * wither);

            // Stand upright in WORLD space (root is identity, so local == world),
            // slowly turning around the stem with a gentle wind sway. While the
            // bloom opens, an extra yaw eases out — the flower UNFURLS rather
            // than just inflating.
            float unfurl = (1f - btC) * unfurlDegrees;
            float spinY  = (now - _born) * spinSpeed + fl.phase * Mathf.Rad2Deg + unfurl;
            float swayX  = Mathf.Sin(now * swaySpeed + fl.phase) * swayAngleDeg;
            float swayZ  = Mathf.Cos(now * swaySpeed * 0.8f + fl.phase) * swayAngleDeg * 0.5f;
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

    // A static green stalk (vertical cross-quad) from a base point up by `height`.
    // Parented to the patch (not the flower head), so it stays grounded while the
    // head nods on top. Slightly tapered — wider at the base.
    void NewStem(Vector3 baseLocal, float height, float width, Color color)
    {
        var go = new GameObject("Stem");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = baseLocal;
        go.transform.localScale    = new Vector3(width, height, width);

        go.AddComponent<MeshFilter>().sharedMesh = GetStemMesh();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial       = GetFlowerMaterial();
        mr.shadowCastingMode    = ShadowCastingMode.Off;
        mr.receiveShadows       = false;
        mr.lightProbeUsage      = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

        var mpb = new MaterialPropertyBlock();
        mpb.SetColor(_BaseColorID, color);
        mpb.SetColor(_ColorID, color);
        mr.SetPropertyBlock(mpb);
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

    // One flower archetype's layer meshes (unit space, outer radius ≈ 1).
    sealed class Archetype
    {
        public Mesh petalsA;   // bright fold halves
        public Mesh petalsB;   // shaded fold halves
        public Mesh under;     // dark back rosette (null = none)
        public Mesh ring;      // pale annulus frame (null = none)
        public Mesh core;      // golden hex heart
        public Mesh coreDot;   // small dark hex printed on the core (null = none)
        public float scale;    // overall size multiplier
    }

    static readonly Dictionary<int, Archetype> _archetypes = new();
    static Material _flowerMat;
    static Mesh     _stemMesh;

    // Vertical cross of two quads (y 0..1, tapered: wider at the base), so a thin
    // stalk reads from any angle. Unit space; NewStem scales it to size.
    static Mesh GetStemMesh()
    {
        if (_stemMesh != null) return _stemMesh;
        var verts = new List<Vector3>();
        var tris  = new List<int>();

        void Strip(Vector3 axis)
        {
            // base wider (±0.5), tip narrower (±0.2).
            Vector3 b0 = -axis * 0.5f, b1 = axis * 0.5f;
            Vector3 t0 = -axis * 0.2f + Vector3.up, t1 = axis * 0.2f + Vector3.up;
            int s = verts.Count;
            verts.Add(b0); verts.Add(b1); verts.Add(t1); verts.Add(t0);
            tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
            tris.Add(s); tris.Add(s + 2); tris.Add(s + 3);
        }
        Strip(Vector3.right);
        Strip(Vector3.forward);

        _stemMesh = new Mesh { name = "BloomStem" };
        _stemMesh.SetVertices(verts);
        _stemMesh.SetTriangles(tris, 0);
        _stemMesh.RecalculateNormals();
        _stemMesh.bounds = new Bounds(new Vector3(0f, 0.5f, 0f), Vector3.one * 1000f);
        return _stemMesh;
    }

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

    // 0 = grand 6-petal (ring + under + core dot), 1 = 8-petal mandala
    // (under + core dot), 2 = small 5-petal daisy (petals + core only).
    static Archetype GetArchetype(int id)
    {
        if (_archetypes.TryGetValue(id, out var cached)) return cached;

        var a = new Archetype();
        switch (id)
        {
            case 0:   // grand bloom
                BuildFoldedRing(6, 0f, 24f, 1.00f, 0.30f, 0.10f, 0.10f, out a.petalsA, out a.petalsB);
                a.under   = BuildFlatRing(6, 30f, 8f, 1.18f, 0.42f, 0.12f);
                a.ring    = BuildAnnulus(0.52f, 0.60f, 0.03f, 28);
                a.core    = BuildHexDisc(0.22f, 0.16f, 0f);
                a.coreDot = BuildHexDisc(0.11f, 0.20f, 30f);
                a.scale   = 1.15f;
                break;
            case 1:   // mandala
                BuildFoldedRing(8, 0f, 30f, 0.92f, 0.24f, 0.09f, 0.08f, out a.petalsA, out a.petalsB);
                a.under   = BuildFlatRing(8, 22.5f, 6f, 1.10f, 0.30f, 0.10f);
                a.core    = BuildHexDisc(0.20f, 0.15f, 0f);
                a.coreDot = BuildHexDisc(0.10f, 0.19f, 30f);
                a.scale   = 1.0f;
                break;
            default:  // daisy filler
                BuildFoldedRing(5, 0f, 20f, 0.85f, 0.30f, 0.08f, 0.07f, out a.petalsA, out a.petalsB);
                a.core  = BuildHexDisc(0.20f, 0.14f, 0f);
                a.scale = 0.78f;
                break;
        }
        _archetypes[id] = a;
        return a;
    }

    // Kite petals FOLDED down their length axis: the crease runs base→tip, the
    // two side points sag below it. Halves land in separate meshes (A = one
    // side, B = the other) so the caller can tint them light/dark — an explicit
    // origami crease, since the unlit material won't shade the fold for us.
    static void BuildFoldedRing(int count, float az0Deg, float tiltDeg, float len, float wid,
                                float inner, float sag, out Mesh meshA, out Mesh meshB)
    {
        var vA = new List<Vector3>(); var tA = new List<int>();
        var vB = new List<Vector3>(); var tB = new List<int>();

        for (int i = 0; i < count; i++)
        {
            float az  = (az0Deg + i * 360f / count) * Mathf.Deg2Rad;
            float tlt = tiltDeg * Mathf.Deg2Rad;

            Vector3 radial = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));
            Vector3 side   = new Vector3(-Mathf.Sin(az), 0f, Mathf.Cos(az));
            Vector3 lengthAxis = radial * Mathf.Cos(tlt) + Vector3.up * Mathf.Sin(tlt);

            Vector3 baseP = radial * inner;
            Vector3 tip   = baseP + lengthAxis * len;
            Vector3 mid   = baseP + lengthAxis * (len * 0.48f);
            Vector3 left  = mid - side * wid - Vector3.up * sag;
            Vector3 right = mid + side * wid - Vector3.up * sag;

            int a = vA.Count;
            vA.Add(baseP); vA.Add(left); vA.Add(tip);
            tA.Add(a); tA.Add(a + 1); tA.Add(a + 2);

            int b = vB.Count;
            vB.Add(baseP); vB.Add(tip); vB.Add(right);
            tB.Add(b); tB.Add(b + 1); tB.Add(b + 2);
        }

        meshA = FinishMesh("BloomPetalsA", vA, tA);
        meshB = FinishMesh("BloomPetalsB", vB, tB);
    }

    // Flat diamond rosette (single mesh) — the dark under-layer silhouette.
    static Mesh BuildFlatRing(int count, float az0Deg, float tiltDeg, float len, float wid, float inner)
    {
        var verts = new List<Vector3>();
        var tris  = new List<int>();
        for (int i = 0; i < count; i++)
        {
            float az  = (az0Deg + i * 360f / count) * Mathf.Deg2Rad;
            float tlt = tiltDeg * Mathf.Deg2Rad;

            Vector3 radial = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));
            Vector3 side   = new Vector3(-Mathf.Sin(az), 0f, Mathf.Cos(az));
            Vector3 lengthAxis = radial * Mathf.Cos(tlt) + Vector3.up * Mathf.Sin(tlt);

            Vector3 baseP = radial * inner - Vector3.up * 0.02f;   // sit just below the fold layer
            Vector3 tip   = baseP + lengthAxis * len;
            Vector3 mid   = baseP + lengthAxis * (len * 0.45f);
            Vector3 left  = mid - side * wid;
            Vector3 right = mid + side * wid;

            int v = verts.Count;
            verts.Add(baseP); verts.Add(left); verts.Add(right); verts.Add(tip);
            tris.Add(v); tris.Add(v + 1); tris.Add(v + 3);
            tris.Add(v); tris.Add(v + 3); tris.Add(v + 2);
        }
        return FinishMesh("BloomUnder", verts, tris);
    }

    // Thin flat ring (annulus) framing the rosette — the print's registration circle.
    static Mesh BuildAnnulus(float r0, float r1, float y, int segs)
    {
        var verts = new List<Vector3>();
        var tris  = new List<int>();
        for (int i = 0; i < segs; i++)
        {
            float a0 = (i / (float)segs) * Mathf.PI * 2f;
            float a1 = ((i + 1) / (float)segs) * Mathf.PI * 2f;
            Vector3 in0  = new Vector3(Mathf.Cos(a0) * r0, y, Mathf.Sin(a0) * r0);
            Vector3 in1  = new Vector3(Mathf.Cos(a1) * r0, y, Mathf.Sin(a1) * r0);
            Vector3 out0 = new Vector3(Mathf.Cos(a0) * r1, y, Mathf.Sin(a0) * r1);
            Vector3 out1 = new Vector3(Mathf.Cos(a1) * r1, y, Mathf.Sin(a1) * r1);

            int v = verts.Count;
            verts.Add(in0); verts.Add(out0); verts.Add(out1); verts.Add(in1);
            tris.Add(v); tris.Add(v + 1); tris.Add(v + 2);
            tris.Add(v); tris.Add(v + 2); tris.Add(v + 3);
        }
        return FinishMesh("BloomRing", verts, tris);
    }

    // Small raised hex polygon — golden heart (and, smaller + rotated + darker,
    // the dot printed on top of it).
    static Mesh BuildHexDisc(float r, float y, float rotDeg)
    {
        const int n = 6;
        var verts = new List<Vector3> { new(0f, y, 0f) };
        for (int k = 0; k < n; k++)
        {
            float a = rotDeg * Mathf.Deg2Rad + k * 2f * Mathf.PI / n;
            verts.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
        }

        var tris = new List<int>();
        for (int k = 0; k < n; k++)
        {
            tris.Add(0);
            tris.Add(1 + k);
            tris.Add(1 + (k + 1) % n);
        }
        return FinishMesh("BloomCore", verts, tris);
    }

    static Mesh FinishMesh(string name, List<Vector3> verts, List<int> tris)
    {
        var m = new Mesh { name = name };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);   // never frustum-cull
        return m;
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
