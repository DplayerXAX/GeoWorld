using System.Collections.Generic;
using UnityEngine;

// "Sealed" marker for a block that can't be moved — an EnemyBlockSealer's seal,
// or the level's own fixed furniture (different tint). Built at runtime from a
// procedural ring mesh (no art assets — same approach as ChaosBubble and the
// synergy visualizers), parented to the block so it follows and dies with it.
//
// Links are proper hollow ovals (an elliptical guide curve swept with a round
// tube cross-section), not stretched cubes — cubes read as a dashed line of
// short bars, never as an interlocked chain. Consecutive links alternate a 90°
// roll and sit close enough to physically pass through each other's loop, the
// way real chain links interlock, so the strand reads continuous with no gaps.
//
// SPECTRAL, not solid iron: translucent with a bright energy pulse that runs
// along each strand. Ghost chains are deliberate — a physical chain demands
// perfect surface contact and gravity sag, which primitives can't fake (and a
// bounds-box wrap visibly floats across the empty cells of an L-shaped block).
// A seal made of light is ALLOWED to hover a little, so the same geometry reads
// as intentional instead of broken.
//
// Face-hugging path math borrowed from VineEffect.CoilPoint (Harmony's coiling
// vine): sweep the angle continuously, project the radius onto the wrap's
// rectangular cross-section — flush on the flat faces, turning at the corners.
//
// Deliberately NOT a SynergyAura: that system keeps only ONE aura per target, so
// a chained TURRET would fight Harmony's buff aura over the slot (and destroy it
// on expiry). Chains own their own GameObjects and can't be clobbered.
public class SealedBlockChains : MonoBehaviour
{
    [Tooltip("Number of chain strands wrapped over the block. Kept low — a couple of strands reads as chained, more just gets busy.")]
    public int strands = 2;
    [Tooltip("How far outside the block faces the chain sits, in cell-size units.")]
    public float surfaceGap = 0.03f;
    [Tooltip("Link wire thickness (tube radius) in cell-size units.")]
    public float linkThickness = 0.028f;
    [Tooltip("Link length (long axis) in cell-size units. Height is ~0.6× this.")]
    public float linkLength = 0.16f;
    [Tooltip("Fraction of a link's length the next one overlaps by, so rings interlock instead of leaving gaps. 0.5 = classic chain look.")]
    [Range(0.2f, 0.7f)] public float linkOverlap = 0.5f;

    [Header("Shimmer")]
    [Tooltip("Resting opacity of a link.")]
    [Range(0f, 1f)] public float baseAlpha = 0.55f;
    [Tooltip("Extra opacity at the crest of the travelling energy pulse.")]
    [Range(0f, 1f)] public float pulseAlpha = 0.35f;
    [Tooltip("How dark the chain sits at rest — 0 = pure seal tint, 1 = near-black iron with just a hint of tint.")]
    [Range(0f, 1f)] public float restDarkness = 0.7f;
    [Tooltip("Pulses per second running along each strand.")]
    public float pulseSpeed = 0.45f;
    [Tooltip("Pulse width as a fraction of the strand (0.2 = a fifth of the chain lit at once).")]
    [Range(0.05f, 0.8f)] public float pulseWidth = 0.22f;
    [Tooltip("Per-link random flicker amount added on top.")]
    [Range(0f, 0.3f)] public float flicker = 0.06f;

    struct Link
    {
        public Renderer r;
        public float    phase;      // 0..1 along its strand
        public int      strand;
        public float    seed;
    }

    static readonly Color _darkIron = new Color(0.06f, 0.06f, 0.07f);

    readonly List<Link> _links = new();
    MaterialPropertyBlock _mpb;
    Mesh  _linkMesh;
    Color _tint;
    float _phase;
    float _bornAt;   // chains stay collapsed until this time (combat-ripple replay)

    static readonly int _ColorID     = Shader.PropertyToID("_Color");
    static readonly int _BaseColorID = Shader.PropertyToID("_BaseColor");

    public static SealedBlockChains Attach(Transform block, float cellSize, Color tint)
    {
        if (block == null) return null;

        var existing = block.GetComponentInChildren<SealedBlockChains>();
        if (existing != null) return existing;

        var go = new GameObject("SealedChains") { layer = block.gameObject.layer };
        go.transform.SetParent(block, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        var chains = go.AddComponent<SealedBlockChains>();
        chains.Build(block, cellSize, tint);
        return chains;
    }

    void Build(Transform block, float cellSize, Color tint)
    {
        _tint  = tint;
        _mpb   = new MaterialPropertyBlock();
        _phase = Random.value * 6.2832f;

        if (!TryGetLocalBounds(block, out Vector3 centre, out Vector3 half))
        {
            centre = Vector3.zero;
            half   = Vector3.one * (cellSize * 0.5f);
        }
        transform.localPosition = centre;

        float gap    = cellSize * surfaceGap;
        float length = cellSize * linkLength;
        float tube   = cellSize * linkThickness;
        _linkMesh    = BuildRingMesh(length * 0.5f, length * 0.3f, tube);

        // First two strands wrap over the top (around X, then Z) — the actual
        // "bound shut" read; extras slant around the sides. Each gets its own
        // start angle, partial span and climb so no two are parallel.
        int n = Mathf.Max(1, strands);
        bool flip = Random.value < 0.5f;
        for (int i = 0; i < n; i++)
        {
            int kind = i == 0 ? (flip ? 1 : 2)
                     : i == 1 ? (flip ? 2 : 1)
                     : Random.Range(0, 3);

            Quaternion rot; Vector2 cross; float climb;
            switch (kind)
            {
                case 1:  rot = Quaternion.Euler(0f, 0f, 90f); cross = new Vector2(half.y, half.z); climb = half.x; break;
                case 2:  rot = Quaternion.Euler(90f, 0f, 0f); cross = new Vector2(half.x, half.y); climb = half.z; break;
                default: rot = Quaternion.identity;           cross = new Vector2(half.x, half.z); climb = half.y; break;
            }

            // TAUT band: the climb offset is small and nearly constant, so each
            // strand stays almost planar — straight runs across the faces with
            // crisp corner turns — instead of spiralling loosely like a draped
            // rope. The slight tilt keeps the strands from looking stamped.
            float a0        = Random.Range(0f, Mathf.PI * 2f);
            float span      = Random.Range(1.4f, 2f) * Mathf.PI * (Random.value < 0.5f ? 1f : -1f);
            float centreOff = climb * Random.Range(-0.35f, 0.35f);
            float tilt      = climb * Random.Range(0.05f, 0.18f) * (Random.value < 0.5f ? 1f : -1f);
            float c0 = centreOff - tilt;
            float c1 = centreOff + tilt;

            BuildStrand(i, cross, gap, length, a0, span, c0, c1, rot);
        }
    }

    void BuildStrand(int strandIndex, Vector2 cross, float gap, float linkLen,
                     float startAngle, float span, float c0, float c1, Quaternion rot)
    {
        // Sample the path finely, then walk it by ARC LENGTH so link spacing
        // stays uniform regardless of the block's footprint.
        const int Samples = 96;
        var pts = new List<Vector3>(Samples);
        for (int i = 0; i < Samples; i++)
        {
            float f = i / (float)(Samples - 1);
            pts.Add(rot * CoilPoint(f, cross, gap, startAngle, span, c0, c1));
        }

        // Total length first, so each link can know its 0..1 phase for the pulse.
        float total = 0f;
        for (int i = 1; i < pts.Count; i++) total += Vector3.Distance(pts[i - 1], pts[i]);
        if (total <= 1e-4f) return;

        // Spacing is a FRACTION of a link's own length (not its thickness) —
        // that's what makes consecutive rings overlap and physically interlock
        // instead of leaving daylight between them.
        float step = linkLen * (1f - Mathf.Clamp01(linkOverlap));
        float acc = 0f, walked = 0f;
        int idx = 0;
        Quaternion inv = Quaternion.Inverse(rot);

        for (int i = 1; i < pts.Count; i++)
        {
            Vector3 a = pts[i - 1], b = pts[i];
            float seg = Vector3.Distance(a, b);
            if (seg <= 1e-5f) continue;

            acc += seg;
            while (acc >= step)
            {
                acc -= step;
                float f = 1f - (acc / seg);
                Vector3 p       = Vector3.Lerp(a, b, Mathf.Clamp01(f));
                Vector3 tangent = (b - a).normalized;

                // Face normal: radial in the strand's own frame, rotated out —
                // correct even for vertical wraps lying on the top face.
                Vector3 pf = inv * p;
                Vector3 of = new Vector3(pf.x, 0f, pf.z);
                Vector3 outward = of.sqrMagnitude > 1e-6f ? rot * of.normalized : Vector3.up;

                float phase = (walked + seg * Mathf.Clamp01(f)) / total;
                MakeLink(p, tangent, outward, strandIndex, phase, idx++);
            }
            walked += seg;
        }
    }

    void MakeLink(Vector3 pos, Vector3 tangent, Vector3 outward, int strandIndex, float phase, int index)
    {
        var link = new GameObject("Link", typeof(MeshFilter), typeof(MeshRenderer));
        link.layer = gameObject.layer;
        link.transform.SetParent(transform, false);
        link.transform.localPosition = pos;

        // Alternating ~90° rolls is what makes a chain read as INTERLOCKED (each
        // ring passes through its neighbour edge-on) rather than a flat ribbon.
        // Jitter stays minimal: a chain under tension pulls its links into line.
        float roll = (index % 2) * 90f + Random.Range(-4f, 4f);
        link.transform.localRotation = Quaternion.LookRotation(tangent, outward)
                                     * Quaternion.Euler(Random.Range(-2f, 2f), Random.Range(-2f, 2f), roll);
        float sizeMul = Random.Range(0.96f, 1.04f);
        link.transform.localScale = Vector3.one * sizeMul;

        link.GetComponent<MeshFilter>().sharedMesh = _linkMesh;
        var mr = link.GetComponent<MeshRenderer>();
        mr.sharedMaterial       = GetChainMaterial();
        mr.shadowCastingMode    = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows       = false;
        mr.lightProbeUsage      = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        _links.Add(new Link { r = mr, phase = phase, strand = strandIndex, seed = Random.value * 17f });
    }

    // f ∈ [0,1] along one strand, in the strand's own frame (the caller rotates
    // the result into place). Angle sweeps across `span`, climb runs c0→c1, and
    // the radius is projected onto the wrap's RECTANGULAR cross-section so the
    // strand hugs the flat faces and turns at the corners.
    static Vector3 CoilPoint(float f, Vector2 cross, float gap, float startAngle, float span, float c0, float c1)
    {
        float ang = startAngle + f * span;
        float ca  = Mathf.Cos(ang), sa = Mathf.Sin(ang);

        float tx = (cross.x + gap) / Mathf.Max(1e-4f, Mathf.Abs(ca));
        float tz = (cross.y + gap) / Mathf.Max(1e-4f, Mathf.Abs(sa));
        float t  = Mathf.Min(tx, tz);

        return new Vector3(ca * t, Mathf.Lerp(c0, c1, f), sa * t);
    }

    // ── Ring mesh (one real chain link: an elliptical guide curve swept with a
    // round tube cross-section — a hollow oval, not a solid bar) ─────────────
    //
    // The guide ellipse lies in the local YZ plane (long axis on Z = the link's
    // "forward"/travel direction, short axis on Y), so at rotation 0 a link
    // viewed from local +X shows its full oval face — exactly the "some links
    // face-on, some edge-on" alternation real interlocked chain shows once
    // every other link is rolled 90°.
    static Mesh BuildRingMesh(float longHalf, float shortHalf, float tubeRadius,
                              int ringSegments = 14, int tubeSegments = 6)
    {
        int vcount = ringSegments * tubeSegments;
        var verts  = new Vector3[vcount];
        var norms  = new Vector3[vcount];
        var tris   = new int[ringSegments * tubeSegments * 6];

        for (int i = 0; i < ringSegments; i++)
        {
            float u = i / (float)ringSegments * Mathf.PI * 2f;
            // Guide point + tangent on the ellipse (0, shortHalf·sin u, longHalf·cos u).
            Vector3 guide   = new Vector3(0f, shortHalf * Mathf.Sin(u), longHalf * Mathf.Cos(u));
            Vector3 tangent = new Vector3(0f, shortHalf * Mathf.Cos(u), -longHalf * Mathf.Sin(u)).normalized;

            // Path lives entirely in the YZ plane, so local +X is always a valid
            // "side" basis vector for the tube cross-section; the other basis
            // vector completes an orthonormal frame around the tangent.
            Vector3 side = Vector3.right;
            Vector3 up   = Vector3.Cross(tangent, side).normalized;

            for (int j = 0; j < tubeSegments; j++)
            {
                float v  = j / (float)tubeSegments * Mathf.PI * 2f;
                Vector3 n = side * Mathf.Cos(v) + up * Mathf.Sin(v);
                int idx = i * tubeSegments + j;
                verts[idx] = guide + n * tubeRadius;
                norms[idx] = n;
            }
        }

        int t = 0;
        for (int i = 0; i < ringSegments; i++)
        {
            int iNext = (i + 1) % ringSegments;
            for (int j = 0; j < tubeSegments; j++)
            {
                int jNext = (j + 1) % tubeSegments;
                int a = i * tubeSegments + j;
                int b = iNext * tubeSegments + j;
                int c = iNext * tubeSegments + jNext;
                int d = i * tubeSegments + jNext;

                tris[t++] = a; tris[t++] = b; tris[t++] = c;
                tris[t++] = a; tris[t++] = c; tris[t++] = d;
            }
        }

        var mesh = new Mesh { name = "SealedChainLink", hideFlags = HideFlags.DontSave };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // ── Shimmer ──────────────────────────────────────────────────────────────

    // Re-grow with the combat ripple, on the same "after every block is up"
    // schedule the synergy decorations use — chains sprouting onto a block that
    // hasn't popped in yet looks like they're floating in the void.
    void OnEnable()  { SynergyVisualFX.OnReplayGrowIn += HandleReplay; }
    void OnDisable() { SynergyVisualFX.OnReplayGrowIn -= HandleReplay; }

    void HandleReplay(System.Func<Vector3, float> delayFor)
    {
        float d = delayFor != null ? Mathf.Max(0f, delayFor(transform.position)) : 0f;
        _bornAt = Time.time + d;
    }

    void Update()
    {
        if (_links.Count == 0 || _mpb == null) return;

        // Collapsed until the ripple reaches us, then a quick snap to full size.
        const float GrowDur = 0.28f;
        float grow = _bornAt <= 0f ? 1f : Mathf.Clamp01((Time.time - _bornAt) / GrowDur);
        transform.localScale = Vector3.one * grow;
        if (grow <= 0f) return;

        float t = Time.time;
        for (int i = 0; i < _links.Count; i++)
        {
            var l = _links[i];
            if (l.r == null) continue;

            // Energy pulse travelling along the strand (each strand offset so
            // they don't fire in sync), plus per-link flicker noise.
            float pulsePos = Mathf.Repeat(t * pulseSpeed + l.strand * 0.37f + _phase * 0.1f, 1f + pulseWidth * 2f) - pulseWidth;
            float d        = Mathf.Abs(l.phase - pulsePos);
            float glow     = Mathf.Clamp01(1f - d / pulseWidth);
            glow          *= glow;   // sharper crest

            float noise = (Mathf.PerlinNoise(l.seed, t * 2.3f) - 0.5f) * 2f * flicker;
            float alpha = Mathf.Clamp01(baseAlpha + glow * pulseAlpha + noise);

            // Resting colour sits dark (near-black iron with a hint of the seal
            // tint) so the chain reads as solid metal, not a coloured ghost;
            // the pulse crest still whitens toward hot light as it passes.
            Color rest = Color.Lerp(_tint, _darkIron, restDarkness);
            Color c    = Color.Lerp(rest, Color.white, glow * 0.65f);
            c.a = alpha;

            l.r.GetPropertyBlock(_mpb);
            _mpb.SetColor(_ColorID, c);
            _mpb.SetColor(_BaseColorID, c);
            l.r.SetPropertyBlock(_mpb);
        }
    }

    // Shared translucent unlit material (alpha-blended, NOT additive — additive
    // would wash out over bright block colours). Sprites/Default is alpha-
    // blended by default, same fallback used by ChaosBubble.
    static Material _chainMat;
    static Material GetChainMaterial()
    {
        if (_chainMat != null) return _chainMat;
        var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
        _chainMat = new Material(sh) { hideFlags = HideFlags.DontSave };
        return _chainMat;
    }

    // Combined renderer bounds expressed in `block`'s local space. Uses each
    // renderer's localBounds (mesh-local, rotation-invariant) rather than the
    // world-space AABB, which inflates once the block is rotated.
    static bool TryGetLocalBounds(Transform block, out Vector3 centre, out Vector3 half)
    {
        centre = Vector3.zero;
        half   = Vector3.zero;

        var rends = block.GetComponentsInChildren<Renderer>();
        bool any = false;
        Bounds acc = default;

        for (int i = 0; i < rends.Length; i++)
        {
            var r = rends[i];
            if (r == null || r.GetComponentInParent<SealedBlockChains>() != null) continue;

            var lb = r.localBounds;
            var c  = block.InverseTransformPoint(r.transform.TransformPoint(lb.center));
            var e  = Vector3.Scale(lb.extents, LossyRatio(r.transform, block));
            var bb = new Bounds(c, e * 2f);

            if (!any) { acc = bb; any = true; }
            else acc.Encapsulate(bb);
        }

        if (!any) return false;
        centre = acc.center;
        half   = acc.extents;
        return true;
    }

    // A child's scale relative to the block root, so its mesh extents convert
    // into root-local units.
    static Vector3 LossyRatio(Transform child, Transform root)
    {
        Vector3 c = child.lossyScale, r = root.lossyScale;
        return new Vector3(
            Mathf.Approximately(r.x, 0f) ? 1f : c.x / r.x,
            Mathf.Approximately(r.y, 0f) ? 1f : c.y / r.y,
            Mathf.Approximately(r.z, 0f) ? 1f : c.z / r.z);
    }
}
