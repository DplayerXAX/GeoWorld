using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// A bank of volumetric mist lying over a patch of the map.
//
// Rendered by GeoWorld/MistVolume — the same ray-marched technique as the gameplay
// DepthFog (noise clumps, main-light scattering, shadowing), confined to one box.
// Because every sample is lit through the shadow map, anything standing in or near
// the mist — trees, blocks, a windmill — throws a dark lane through it, and sunlight
// between them stands out as a shaft: the Tyndall effect.
//
// Its shape is baked from the WORLD POINTS it has to cover into a small soft
// top-down mask, so a bank takes the outline of what is under it.
//
// Two lives: idling (the shader scrolls the noise on the wind — nothing to do on the
// CPU) and dispersing (it burns off from the thin parts in, lifts, and removes
// itself).
public class MistBank : MonoBehaviour
{
    [System.Serializable]
    public struct Settings
    {
        public Color color;
        [Range(0f, 1f)] public float strength;
        public float density;
        [Tooltip("Height of the bank above the ground it covers, in cells.")]
        public float height;
        [Tooltip("How far the bank reaches BELOW that ground, in cells. Dense all the way down, thinning only above the ground — so it reads as mist welling up from below (like the atmosphere's height fog) rather than a cloud resting on the floor.")]
        [Range(0f, 20f)] public float depth;
        [Tooltip("Shifts the whole bank up or down, in cells — ground line, top and bottom together. Negative sinks the mist below the ground it covers.")]
        [Range(-10f, 10f)] public float offset;
        [Tooltip("How fast it thins going up from the ground. Lower = more gradual. 0 keeps the default (1.1).")]
        [Range(0f, 4f)] public float falloff;
        [Tooltip("How far the mist's colour is pulled toward the sky behind it (sampled from the skybox's environment reflection), most on grazing rays near the horizon. What lets distant haze melt into the sky instead of standing in front of it as a pale band.")]
        [Range(0f, 1f)] public float skyBlend;
        [Range(0f, 6f)] public float scatter;
        [Range(-0.9f, 0.9f)] public float anisotropy;
        [Range(6, 48)] public int steps;

        [Tooltip("How far past the covered ground the mist takes to fade out, in cells. The bank also lowers toward its edge over this distance, so it is a dome rather than a slab.")]
        [Range(0.5f, 40f)] public float edge;
        [Tooltip("Ground kept clear around anything that must stay visible, in cells (from the block's centre).")]
        [Range(0f, 8f)] public float clearance;
        [Tooltip("How far the edge is pushed in and out by noise, in cells — what stops the outline following the grid.")]
        [Range(0f, 6f)] public float edgeWarp;
        [Tooltip("Frequency of the clumps, per world unit. Lower = bigger, softer masses. 0 keeps the shader's default.")]
        [Range(0f, 1f)] public float noiseScale;

        public static Settings Default => new()
        {
            color = new Color(0.93f, 0.90f, 0.85f), strength = 0.85f, density = 0.9f,
            height = 3f, scatter = 1.6f, anisotropy = 0.55f, steps = 20,
            edge = 3f, clearance = 1.3f, edgeWarp = 1.2f,
        };
    }

    // The horizon haze sinking away from the map's centre: flat out to `start`
    // cells, then dropping `rate` cells per cell of distance (easing in over
    // `soft` cells), down to at most `max` cells below where it started.
    [System.Serializable]
    public struct Sink
    {
        public float start;
        public float rate;
        public float max;
        public float soft;
    }

    Material  _mat;
    Texture2D _mask;
    float     _disperseT = -1f, _disperseDur = 1f;

    static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
    static readonly int LiftId     = Shader.PropertyToID("_Lift");
    static Mesh _cube;

    public bool Dispersing => _disperseT >= 0f;

    // `avoid`: tops of blocks that are on show and must not be hidden. The mist is
    // carved away around them (with `clearance`), whatever it was asked to cover.
    public static MistBank Create(Transform parent, string name, List<Vector3> points,
                                  float cellSize, Settings s, int seed, List<Vector3> avoid = null)
    {
        if (points == null || points.Count == 0) return null;

        var sh = Shader.Find("GeoWorld/MistVolume");
        if (sh == null) { Debug.LogWarning("[Mist] GeoWorld/MistVolume shader not found."); return null; }

        EnsureDepthTexture();

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var bank = go.AddComponent<MistBank>();
        bank.Build(sh, points, avoid, cellSize, s, seed);
        return bank;
    }

    // The horizon haze: one volume over the whole map and far beyond it. Clear over
    // the ground on show (`clear`), then thickening — and heaping up taller — with
    // distance from it, until past the edge of the map it is a wall that swallows the
    // backdrop. The same ray-march as a bank, so distance reads as air, lit and
    // shadowed, rather than as a flat tint; and because the clear zone IS the
    // revealed ground, the haze draws back as the map grows.
    //
    // `extent`: every point the map could reach (visible and hidden), so the volume
    // spans all of it; `margin`: how far past that it goes on, in cells.
    //
    // `feed`: ground under the inner banks. Outward of them the haze density is
    // INTERPOLATED from `feedStrength` at the bank up to full at the horizon, so a
    // bank's soft edge runs into haze already at about its own level and the two
    // grade into each other, instead of the bank thinning out into a clear ring
    // before the far haze begins.
    public static MistBank CreateHorizon(Transform parent, string name, List<Vector3> clear,
                                         List<Vector3> extent, float cellSize, Settings s,
                                         float margin, int seed,
                                         List<Vector3> feed = null, float feedStrength = 0f,
                                         Sink sink = default)
    {
        if (extent == null || extent.Count == 0) return null;

        var sh = Shader.Find("GeoWorld/MistVolume");
        if (sh == null) { Debug.LogWarning("[Mist] GeoWorld/MistVolume shader not found."); return null; }

        EnsureDepthTexture();

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var bank = go.AddComponent<MistBank>();
        bank.BuildHorizon(sh, clear, extent, cellSize, s, margin, seed, feed, feedStrength, sink);
        return bank;
    }

    void BuildHorizon(Shader sh, List<Vector3> clear, List<Vector3> extent, float cs,
                      Settings s, float margin, int seed,
                      List<Vector3> feed, float feedStrength, Sink sink)
    {
        Vector3 lo = extent[0], hi = extent[0];
        foreach (var p in extent) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
        if (clear != null) foreach (var p in clear) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }

        float pad = Mathf.Max(1f, margin) * cs;
        float x0 = lo.x - pad, z0 = lo.z - pad;
        float sx = hi.x - lo.x + 2f * pad, sz = hi.z - lo.z + 2f * pad;

        // Square texels (the distance transform needs them), two per cell, and no
        // more than 256 on a side — the haze is soft enough not to need more.
        float texel = Mathf.Max(cs * 0.5f, Mathf.Max(sx, sz) / 256f);
        int w = Mathf.CeilToInt(sx / texel), h = Mathf.CeilToInt(sz / texel);
        sx = w * texel; sz = h * texel;

        float shift  = s.offset * cs;
        float ground = MeanY(extent) + shift;
        // Room below for the layer to sink into toward the horizon (see Sink).
        float sinkMax = sink.rate > 0f ? Mathf.Max(0f, sink.max) * cs : 0f;
        float y0 = lo.y + shift - Mathf.Max(0.6f, s.depth) * cs - sinkMax;
        float y1 = hi.y + shift + Mathf.Max(1f, s.height) * cs;
        Place(new Vector3(x0 + sx * 0.5f, (y0 + y1) * 0.5f, z0 + sz * 0.5f), new Vector3(sx, y1 - y0, sz));

        // Density = how far from the clear ground, ramped over `edge` cells after
        // the first `clearance`. The shader turns the same value into height, so the
        // haze is a low veil near the map and a tall bank far off.
        var d  = DistanceTransform(clear, w, h, x0, z0, texel);
        var px = new float[w * h];
        float a = s.clearance * cs, b = a + Mathf.Max(1f, s.edge) * cs;
        for (int i = 0; i < px.Length; i++)
            px[i] = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, d[i]));

        // Interpolated out from the inner banks. Beyond a bank (farther from the
        // clear ground than the bank itself is), the density is a blend between two
        // ends — `feedStrength` at the bank, full haze where the distance ramp is
        // full — weighted by how far the point is from each. So walking outward the
        // density only ever climbs, from the bank's level to the horizon's, with no
        // dip in between and no extra ring around the bank. On the map's side of a
        // bank nothing changes: the haze there stays the plain distance ramp.
        if (feed != null && feed.Count > 0 && feedStrength > 0f)
        {
            var bankAt = new float[w * h];                     // clear-distance of the nearest banked point
            var df = DistanceTransform(feed, w, h, x0, z0, texel, d, bankAt);
            for (int i = 0; i < px.Length; i++)
            {
                if (df[i] >= float.MaxValue) continue;
                float outward = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-cs, 2f * cs, d[i] - bankAt[i]));
                if (outward <= 0f) continue;

                float toFull = Mathf.Max(0f, b - d[i]);        // still to go before the haze is full
                float t      = df[i] / Mathf.Max(1e-4f, df[i] + toFull);
                float interp = Mathf.Lerp(feedStrength, 1f, Mathf.SmoothStep(0f, 1f, t));
                px[i] = Mathf.Lerp(px[i], Mathf.Max(px[i], interp), outward);
            }
        }
        int r = Mathf.Max(1, Mathf.RoundToInt(cs / texel));
        BoxBlur(px, w, h, r);
        BoxBlur(px, w, h, r);
        _mask = ToTexture(px, w, h, "HorizonMask");

        Setup(sh, new Vector3(sx, y1 - y0, sz), s, cs, seed, clear != null && clear.Count > 0,
              (ground - y0) / (y1 - y0));

        if (sink.rate > 0f)
        {
            // Centre: the middle of the whole map (visible and hidden), so the fall-off
            // is symmetric about it and does not shift as regions are revealed.
            Vector3 c = Vector3.zero;
            foreach (var p in extent) c += p;
            c /= extent.Count;
            _mat.SetVector("_SinkCentre", new Vector4(c.x, c.z, 0f, 0f));
            _mat.SetVector("_Sink", new Vector4(sink.start * cs, sink.rate, sinkMax, Mathf.Max(0.1f, sink.soft) * cs));
        }
        // Drawn before the banks: it lies behind and around them, and overlapping
        // premultiplied volumes composite correctly back to front.
        _mat.renderQueue = 2940;
    }

    void Build(Shader sh, List<Vector3> points, List<Vector3> avoid, float cs, Settings s, int seed)
    {
        float edge = s.edge > 0f ? s.edge : 3f;

        // Bounds of what to cover, padded so the soft edge — and the noise pushing
        // it about — has room to fall off before the box does. A mask cut off by its
        // own box shows as a straight wall of mist.
        float pad = (edge + s.edgeWarp + 0.5f) * cs;
        Vector3 lo = points[0], hi = points[0];
        foreach (var p in points) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }

        float x0 = lo.x - pad, x1 = hi.x + pad;
        float z0 = lo.z - pad, z1 = hi.z + pad;
        float shift  = s.offset * cs;
        float ground = MeanY(points) + shift;
        float y0 = lo.y + shift - Mathf.Max(0.6f, s.depth) * cs;
        float y1 = hi.y + shift + Mathf.Max(0.5f, s.height) * cs;

        var size   = new Vector3(x1 - x0, y1 - y0, z1 - z0);
        var centre = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
        Place(centre, size);

        _mask = BakeMask(points, avoid, x0, z0, size.x, size.z, cs, edge, s.clearance);

        // Banks told what to keep clear also honour the screen-space protection
        // (see SetProtected) — a mask carved on the ground can't stop a tall bank
        // beside a block from covering it along the camera's line of sight.
        Setup(sh, size, s, cs, seed, avoid != null && avoid.Count > 0 && s.clearance > 0f,
              (ground - y0) / size.y);
    }

    // The ground level the mist is measured from — the MEAN of what it covers, so a
    // region on several levels gets its mist line through the middle of them rather
    // than at the lowest block.
    static float MeanY(List<Vector3> pts)
    {
        float y = 0f;
        foreach (var p in pts) y += p.y;
        return pts.Count > 0 ? y / pts.Count : 0f;
    }

    // WORLD placement, set while any moving parent is still at rest: a bank inside a
    // decor plot is built before the plot is sunk for its grow-in, so it sinks and
    // rises with the plot like everything else in it.
    void Place(Vector3 centre, Vector3 size)
    {
        transform.position   = centre;
        transform.rotation   = Quaternion.identity;
        transform.localScale = Vector3.one;
        var lossy = transform.lossyScale;
        transform.localScale = new Vector3(size.x / lossy.x, size.y / lossy.y, size.z / lossy.z);
    }

    void Setup(Shader sh, Vector3 size, Settings s, float cs, int seed, bool protect, float floor)
    {
        _mat = new Material(sh) { name = "Mist (runtime)" };
        _mat.SetFloat("_Floor", Mathf.Clamp(floor, 0.02f, 0.9f));
        _mat.SetFloat("_Falloff", s.falloff > 0f ? s.falloff : 1.1f);
        _mat.SetFloat("_SkyBlend", s.skyBlend);
        _mat.SetTexture("_Mask", _mask);
        _mat.SetVector("_BoxSize", size);
        _mat.SetFloat("_Protect", protect ? 1f : 0f);
        _mat.SetFloat("_EdgeWarp", s.edgeWarp * cs);
        if (s.noiseScale > 0f) _mat.SetFloat("_NoiseScale", s.noiseScale);
        _mat.SetColor("_FogColor", s.color);
        _mat.SetFloat("_Strength", s.strength);
        _mat.SetFloat("_Density", s.density);
        _mat.SetFloat("_Scatter", s.scatter);
        _mat.SetFloat("_Anisotropy", s.anisotropy);
        _mat.SetFloat("_Steps", Mathf.Clamp(s.steps, 6, 48));
        // Noise is sampled in WORLD space, so without this every bank on the map
        // would show the identical cloud pattern wherever it overlapped.
        var rng = new System.Random(seed);
        _mat.SetVector("_Offset", new Vector4((float)rng.NextDouble() * 97f, (float)rng.NextDouble() * 31f,
                                              (float)rng.NextDouble() * 71f, 0f));

        gameObject.AddComponent<MeshFilter>().sharedMesh = Cube();
        var mr = gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial       = _mat;
        mr.shadowCastingMode    = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows       = false;
        mr.lightProbeUsage      = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    // Top-down footprint, from two distance fields:
    //   * distance to the nearest covered point — full over the covered ground,
    //     falling away over `edge` cells beyond it. Euclidean, so the outside corners
    //     of a rectangular region come out round;
    //   * distance to the nearest point to AVOID — nothing within `clearance`, rising
    //     back over a cell or two beyond.
    // Then blurred about a cell, which rounds off the inside corners and steps the
    // two fields leave. The shader also warps where it reads this with noise, so the
    // final edge wanders rather than tracing the blur.
    static Texture2D BakeMask(List<Vector3> points, List<Vector3> avoid,
                              float x0, float z0, float sx, float sz, float cs,
                              float edge, float clearance)
    {
        const int perCell = 3;
        int w = Mathf.Clamp(Mathf.CeilToInt(sx / cs * perCell), 8, 256);
        int h = Mathf.Clamp(Mathf.CeilToInt(sz / cs * perCell), 8, 256);
        float tx = w / sx, tz = h / sz;          // texels per world unit

        float inner = 0.4f * cs, outer = Mathf.Max(inner + 0.1f, edge * cs);
        var dC = NearestDistance(points, w, h, x0, z0, tx, tz, outer);

        float aIn = clearance * cs, aOut = aIn + Mathf.Max(1f, edge * 0.5f) * cs;
        var dA = avoid != null && avoid.Count > 0 && clearance > 0f
               ? NearestDistance(avoid, w, h, x0, z0, tx, tz, aOut) : null;

        var px = new float[w * h];
        for (int i = 0; i < px.Length; i++)
        {
            float v = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner, outer, dC[i]));
            if (dA != null) v *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(aIn, aOut, dA[i]));
            px[i] = v;
        }
        BoxBlur(px, w, h, perCell);
        BoxBlur(px, w, h, perCell);

        var tex = new Texture2D(w, h, TextureFormat.R8, false, true)
        {
            name = "MistMask", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
        };
        var cols = new Color[w * h];
        for (int i = 0; i < cols.Length; i++) cols[i] = new Color(px[i], 0f, 0f, 1f);
        tex.SetPixels(cols);
        tex.Apply(false, true);
        return tex;
    }

    // Per texel, the world distance to the nearest point, looked for only within
    // `reach` (beyond that it stays MaxValue — as good as infinitely far).
    static float[] NearestDistance(List<Vector3> points, int w, int h, float x0, float z0,
                                   float tx, float tz, float reach)
    {
        var d = new float[w * h];
        for (int i = 0; i < d.Length; i++) d[i] = float.MaxValue;
        int rx = Mathf.CeilToInt(reach * tx) + 1, rz = Mathf.CeilToInt(reach * tz) + 1;

        foreach (var p in points)
        {
            int cx = Mathf.FloorToInt((p.x - x0) * tx);
            int cz = Mathf.FloorToInt((p.z - z0) * tz);
            int xa = Mathf.Max(0, cx - rx), xb = Mathf.Min(w - 1, cx + rx);
            int za = Mathf.Max(0, cz - rz), zb = Mathf.Min(h - 1, cz + rz);
            if (xa > xb || za > zb) continue;                 // nowhere near this bank
            for (int z = za; z <= zb; z++)
                for (int x = xa; x <= xb; x++)
                {
                    float wx = x0 + (x + 0.5f) / tx - p.x;
                    float wz = z0 + (z + 0.5f) / tz - p.z;
                    float dd = Mathf.Sqrt(wx * wx + wz * wz);
                    int i = z * w + x;
                    if (dd < d[i]) d[i] = dd;
                }
        }
        return d;
    }

    // The ground on show, as a global top-down map every protecting bank reads: 1 on
    // and just around each visible block, 0 elsewhere. Where the camera's ray ends on
    // that ground, the mist in front of it is dropped.
    static Texture2D _protect;

    public static void SetProtected(List<Vector3> visible, float cs)
    {
        if (_protect != null) { Destroy(_protect); _protect = null; }
        if (visible == null || visible.Count == 0)
        {
            Shader.SetGlobalVector("_MistProtectRect", new Vector4(1e6f, 1e6f, 0f, 0f));
            return;
        }

        float pad = 2f * cs;
        Vector3 lo = visible[0], hi = visible[0];
        foreach (var p in visible) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
        float x0 = lo.x - pad, z0 = lo.z - pad;
        float sx = hi.x - lo.x + 2f * pad, sz = hi.z - lo.z + 2f * pad;

        const int perCell = 3;
        int w = Mathf.Clamp(Mathf.CeilToInt(sx / cs * perCell), 8, 512);
        int h = Mathf.Clamp(Mathf.CeilToInt(sz / cs * perCell), 8, 512);
        float tx = w / sx, tz = h / sz;

        // Full over the block (±0.5 cell from its centre, plus a margin for the
        // outline), gone a cell further out — so the hole in the mist is a soft
        // halo round what is on show, not a cut-out of it.
        float full = 0.75f * cs, none = 1.6f * cs;
        var d  = NearestDistance(visible, w, h, x0, z0, tx, tz, none);
        var px = new float[w * h];
        for (int i = 0; i < px.Length; i++)
            px[i] = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(full, none, d[i]));
        BoxBlur(px, w, h, 1);

        _protect = new Texture2D(w, h, TextureFormat.R8, false, true)
        {
            name = "MistProtect", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
        };
        var cols = new Color[w * h];
        for (int i = 0; i < cols.Length; i++) cols[i] = new Color(px[i], 0f, 0f, 1f);
        _protect.SetPixels(cols);
        _protect.Apply(false, true);

        Shader.SetGlobalTexture("_MistProtect", _protect);
        Shader.SetGlobalVector("_MistProtectRect", new Vector4(x0, z0, 1f / sx, 1f / sz));
    }

    // World distance from each texel to the nearest seed, over the whole texture —
    // the haze needs it far beyond any sensible splat reach. Two-pass chamfer
    // (1, √2): within a few percent of true Euclidean, which is invisible in fog.
    // No seeds at all: everything is infinitely far, i.e. full haze.
    //
    // With `seedValue` / `nearestValue`: also carries, to every texel, the value
    // `seedValue` holds at its NEAREST seed's texel (a feature transform, the same
    // two passes) — used to ask "how far from the clear ground is the bank nearest
    // to this point".
    static float[] DistanceTransform(List<Vector3> seeds, int w, int h, float x0, float z0, float texel,
                                     float[] seedValue = null, float[] nearestValue = null)
    {
        const float Inf = 1e9f, A = 1f, B = 1.41421356f;
        bool carry = seedValue != null && nearestValue != null;
        var d = new float[w * h];
        for (int i = 0; i < d.Length; i++) d[i] = Inf;
        if (seeds != null)
            foreach (var p in seeds)
            {
                int x = Mathf.FloorToInt((p.x - x0) / texel), z = Mathf.FloorToInt((p.z - z0) / texel);
                if (x >= 0 && x < w && z >= 0 && z < h)
                {
                    int i = z * w + x;
                    d[i] = 0f;
                    if (carry) nearestValue[i] = seedValue[i];
                }
            }

        void Relax(int i, int j, float step)
        {
            float c = d[j] + step;
            if (c < d[i]) { d[i] = c; if (carry) nearestValue[i] = nearestValue[j]; }
        }

        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                int i = z * w + x;
                if (x > 0) Relax(i, i - 1, A);
                if (z > 0)
                {
                    Relax(i, i - w, A);
                    if (x > 0)     Relax(i, i - w - 1, B);
                    if (x < w - 1) Relax(i, i - w + 1, B);
                }
            }
        for (int z = h - 1; z >= 0; z--)
            for (int x = w - 1; x >= 0; x--)
            {
                int i = z * w + x;
                if (x < w - 1) Relax(i, i + 1, A);
                if (z < h - 1)
                {
                    Relax(i, i + w, A);
                    if (x < w - 1) Relax(i, i + w + 1, B);
                    if (x > 0)     Relax(i, i + w - 1, B);
                }
            }

        for (int i = 0; i < d.Length; i++) d[i] = d[i] >= Inf * 0.5f ? float.MaxValue : d[i] * texel;
        return d;
    }

    static Texture2D ToTexture(float[] px, int w, int h, string name)
    {
        var tex = new Texture2D(w, h, TextureFormat.R8, false, true)
        {
            name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
        };
        var cols = new Color[w * h];
        for (int i = 0; i < cols.Length; i++) cols[i] = new Color(px[i], 0f, 0f, 1f);
        tex.SetPixels(cols);
        tex.Apply(false, true);
        return tex;
    }

    // Separable box blur, clamped at the borders.
    static void BoxBlur(float[] px, int w, int h, int r)
    {
        var tmp = new float[px.Length];
        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                float s = 0f; int n = 0;
                for (int k = -r; k <= r; k++) { int xx = x + k; if (xx < 0 || xx >= w) continue; s += px[z * w + xx]; n++; }
                tmp[z * w + x] = s / n;
            }
        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                float s = 0f; int n = 0;
                for (int k = -r; k <= r; k++) { int zz = z + k; if (zz < 0 || zz >= h) continue; s += tmp[zz * w + x]; n++; }
                px[z * w + x] = s / n;
            }
    }

    public void Disperse(float duration)
    {
        if (Dispersing) return;
        _disperseDur = Mathf.Max(0.05f, duration);
        _disperseT   = 0f;
    }

    void Update()
    {
        if (!Dispersing || _mat == null) return;

        _disperseT += Time.deltaTime;
        float p = Mathf.Clamp01(_disperseT / _disperseDur);

        // Burn off first, lift after: the threshold climbs fast early (the fringes
        // go at once), and the remaining cores are carried up as they thin.
        _mat.SetFloat(DissolveId, 1f - (1f - p) * (1f - p));
        _mat.SetFloat(LiftId, p * p * 0.45f);

        if (p >= 1f) Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (_mat  != null) Destroy(_mat);
        if (_mask != null) Destroy(_mask);
    }

    // The mist marches the scene depth to know where it is cut off. The PC pipeline
    // asset has the depth texture on; the Mobile one does not — and without it every
    // bank would draw straight over the blocks in front of it. So the camera that
    // sees the mist asks for depth itself, whatever the quality tier.
    public static void EnsureDepthTexture()
    {
        var cam = Camera.main;
        if (cam == null) return;
        var data = cam.GetUniversalAdditionalCameraData();
        if (data != null) data.requiresDepthOption = CameraOverrideOption.On;
    }

    static Mesh Cube()
    {
        if (_cube != null) return _cube;
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _cube = tmp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(tmp);
        return _cube;
    }
}
