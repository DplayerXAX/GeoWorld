using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Countryside: 1-1's far land. Rolling hills cut into a patchwork of fields,
// each its own crop, with hedgerows along some of the field edges, woods on
// the high ground, and farmsteads and windmills. All of it withered at the start:
// bare earth, stubble, dry grass, rust-coloured woods, still sails. When the
// bloom synergy fires it ripens field by field outward from the board. Wheat
// turns gold, pasture green, rapeseed yellow, and the woods and hedges leaf
// green. Real wheat rises out of the near fields, the dead trees grow into
// living ones, and the sails start to turn.
//
// The land is one mesh with a colour per TRIANGLE (unshared vertices), so field
// edges stay crisp. Its normals come from the heightfield itself, so the hills
// still shade smoothly. Woods and hedges are low faceted solids combined into a
// few big meshes. Everything ripens in the shader (see Ripens), never on the CPU.
//
// Built as a coroutine in slices of a few milliseconds, holding the loading page
// until it is done, so the level comes up behind a turning spinner rather than
// behind one long frozen frame.
public partial class EnvironmentBackdrop
{
    enum Crop { Plough, Wheat, Pasture, Rapeseed, Fallow }

    // Geometry built in bulk: faceted solids with a dry and a ripe colour per
    // vertex, and when (seconds after the bloom) each one ripens.
    class Bulk
    {
        public readonly List<Vector3> v = new(), n = new();
        public readonly List<Color>   dry = new(), ripe = new();
        public readonly List<float>   at = new();
        public readonly List<int>     t = new();
    }

    class WheatField { public Vector3 centre; public readonly List<Vector3> spots = new(); public float delay; }
    readonly List<WheatField> _wheatFields = new();
    readonly List<Transform>  _sails = new();
    float _sailSpeed;

    static Vector3[] _ico;
    static int[]     _icoF;

    const long SliceMs = 10;   // work per frame while building

    IEnumerator Countryside(Transform parent, LevelEnvironment env, Vector3 centre, float floorY, float cs, System.Random rng)
    {
        LoadingScreen.Hold(this);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool Over() => sw.ElapsedMilliseconds > SliceMs;

        float inner = env.backdropDistance.x * cs, outer = env.backdropDistance.y * cs;
        float grid  = GraphicsQuality.BackdropGrid * cs;   // coarser land on the lower presets
        int   n     = Mathf.CeilToInt(outer * 2f / grid) + 1;
        float ox = Rand(rng, 0f, 100f), oz = Rand(rng, 0f, 100f);
        float ang = Rand(rng, 0f, Mathf.PI * 0.5f);
        float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
        float fw = Mathf.Max(2f, env.fieldSize.x) * cs, fd = Mathf.Max(2f, env.fieldSize.y) * cs;
        float woodCut = 0.5f + (0.5f - env.woodland) * 0.32f;
        float woodIn2 = (inner + 10f * cs) * (inner + 10f * cs);
        Color leaf  = env.backdropLeafColor;
        Color gold  = env.backdropGrowth.a  > 0f ? env.backdropGrowth  : new Color(0.86f, 0.62f, 0.18f, 1f);
        Color gold2 = env.backdropGrowth2.a > 0f ? env.backdropGrowth2 : new Color(0.98f, 0.80f, 0.32f, 1f);

        // ── The land's shape ─────────────────────────────────────────────────
        // Cells above the floor: broad hills climbing toward the outside with a
        // finer roll over them, dropped away under the board. Evaluated once per
        // grid point; everything placed later reads the grid back (Sample), which
        // is a few multiplies instead of eight octaves of noise.
        float Height(float x, float z)
        {
            float d = Mathf.Sqrt(x * x + z * z);
            float t = Mathf.InverseLerp(inner, outer, d);
            float hill = Fbm2(x / cs * 0.018f + ox, z / cs * 0.018f + oz);
            float roll = Fbm2(x / cs * 0.05f + oz, z / cs * 0.05f + ox);
            float raw  = -7f + (_peakRise.y + 7f) * Mathf.Pow(t, 0.9f) * (0.3f + 0.9f * hill) + (roll - 0.5f) * 3f;
            float ramp = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner - 4f * cs, inner + 14f * cs, d));
            return Mathf.Lerp(-30f, raw, ramp);
        }

        var h    = new float[n * n];   // cells above the floor
        var wood = new float[n * n];   // woodland noise
        for (int iz = 0; iz < n; iz++)
        {
            for (int ix = 0; ix < n; ix++)
            {
                float x = -outer + ix * grid, z = -outer + iz * grid;
                h[iz * n + ix]    = Height(x, z);
                wood[iz * n + ix] = Fbm2(x / cs * 0.014f + ox + 50f, z / cs * 0.014f + oz + 80f);
            }
            if (Over()) { yield return null; sw.Restart(); }
        }

        float Sample(float[] a, float x, float z)
        {
            float fx = Mathf.Clamp((x + outer) / grid, 0f, n - 1.001f);
            float fz = Mathf.Clamp((z + outer) / grid, 0f, n - 1.001f);
            int ix = (int)fx, iz = (int)fz;
            float tx = fx - ix, tz = fz - iz;
            int i = iz * n + ix;
            return Mathf.Lerp(Mathf.Lerp(a[i], a[i + 1], tx), Mathf.Lerp(a[i + n], a[i + n + 1], tx), tz);
        }
        Vector3 World(float x, float z) => centre + new Vector3(x, floorY - centre.y + Sample(h, x, z) * cs, z);
        bool Wood(float x, float z) => x * x + z * z > woodIn2 && Sample(wood, x, z) > woodCut;

        // Two octaves: plenty to make a field edge wander.
        float Warp(float x, float z) =>
            Mathf.PerlinNoise(x, z) * 0.67f + Mathf.PerlinNoise(x * 2.03f + 5.2f, z * 2.03f + 1.3f) * 0.33f;

        // The field a point lies in. Rows of fields run along a turned axis, and
        // each row's fields have their own length and offset, so edges never line
        // up into a grid. All of it is warped so the edges wander. Returns the
        // field's key, and how far the point is from the field's edge.
        (int key, float edge) FieldAt(float x, float z)
        {
            float wx = x + (Warp(x / cs * 0.04f + 17f, z / cs * 0.04f + 3f) - 0.5f) * 7f * cs;
            float wz = z + (Warp(x / cs * 0.04f + 5f, z / cs * 0.04f + 29f) - 0.5f) * 7f * cs;
            float u = wx * ca + wz * sa, v = -wx * sa + wz * ca;
            float fv = v / fd;
            int row = Mathf.FloorToInt(fv);
            float len = fw * (0.75f + 0.5f * Hash01i(row, 7));
            float fu = u / len + Hash01i(row, 3);
            int col = Mathf.FloorToInt(fu);
            float edge = Mathf.Min(Mathf.Min(fu - col, col + 1 - fu) * len, Mathf.Min(fv - row, row + 1 - fv) * fd);
            return (row * 7919 + col, edge);
        }

        Crop CropOf(int key)
        {
            float r = Hash01i(key, 11);
            return r < 0.30f ? Crop.Wheat : r < 0.52f ? Crop.Pasture : r < 0.70f ? Crop.Plough
                 : r < 0.82f ? Crop.Rapeseed : Crop.Fallow;
        }

        // Each crop dry, then ripe.
        (Color dry, Color ripe) CropColours(Crop crop, int key)
        {
            bool alt = Hash01i(key, 29) < 0.5f;
            switch (crop)
            {
                case Crop.Plough:     // young shoots
                    return (env.landEarth, Color.Lerp(leaf, new Color(0.62f, 0.70f, 0.30f, 1f), 0.5f));
                case Crop.Wheat:      // stubble
                    return (Color.Lerp(env.landDry, new Color(0.72f, 0.62f, 0.44f, 1f), 0.45f), alt ? gold : gold2);
                case Crop.Pasture:    // dry grass
                    return (Color.Lerp(env.landDry, new Color(0.56f, 0.52f, 0.32f, 1f), 0.55f),
                            Color.Lerp(leaf, new Color(0.55f, 0.78f, 0.30f, 1f), alt ? 0.1f : 0.35f));
                case Crop.Rapeseed:
                    return (Color.Lerp(env.landEarth, env.landDry, 0.55f), new Color(0.96f, 0.84f, 0.22f, 1f));
                default:              // fallow, then meadow
                    return (env.landDry, Color.Lerp(leaf, new Color(0.78f, 0.80f, 0.42f, 1f), 0.5f));
            }
        }

        Vector3 P(int ix, int iz) =>
            centre + new Vector3(-outer + ix * grid, floorY - centre.y + h[iz * n + ix] * cs, -outer + iz * grid);
        Vector3 N(int ix, int iz)
        {
            int x0 = Mathf.Max(0, ix - 1), x1 = Mathf.Min(n - 1, ix + 1);
            int z0 = Mathf.Max(0, iz - 1), z1 = Mathf.Min(n - 1, iz + 1);
            float dx = (h[iz * n + x1] - h[iz * n + x0]) * cs / ((x1 - x0) * grid);
            float dz = (h[z1 * n + ix] - h[z0 * n + ix]) * cs / ((z1 - z0) * grid);
            return new Vector3(-dx, 1f, -dz).normalized;
        }

        // ── The land, a colour per triangle ──────────────────────────────────
        var land   = new Bulk();
        var wheat  = new Dictionary<int, WheatField>();
        var hedges = new List<(Vector3 at, float t)>();
        float hole = inner - 6f * cs;

        void LandTri(int ax, int az, int bx, int bz, int cx, int cz)
        {
            Vector3 p0 = P(ax, az), p1 = P(bx, bz), p2 = P(cx, cz);
            var mid = (p0 + p1 + p2) / 3f - centre;
            float d01 = Mathf.InverseLerp(inner, outer, new Vector2(mid.x, mid.z).magnitude);
            var (key, edge) = FieldAt(mid.x, mid.z);
            float tone = 0.9f + 0.16f * Hash01i(key, 19);
            float at = 0.3f + 2.4f * d01 + 0.7f * Hash01i(key, 23);
            bool woodHere = mid.x * mid.x + mid.z * mid.z > woodIn2 &&
                            (wood[az * n + ax] + wood[bz * n + bx] + wood[cz * n + cx]) / 3f > woodCut;
            Color dry, ripe;
            if (woodHere)
            {
                dry  = Shade(new Color(0.36f, 0.26f, 0.18f, 1f), tone);
                ripe = Shade(Color.Lerp(leaf, Color.black, 0.45f), tone);
                at   = 0.3f + 2.4f * d01;
            }
            else if (edge < 0.8f * cs)
            {
                // Field margins: a dark line of verge between the crops.
                dry  = Shade(Color.Lerp(env.landEarth, env.landDry, 0.3f), 0.85f);
                ripe = Shade(Color.Lerp(leaf, Color.black, 0.3f), 0.95f);
                if (Hash01i(key, 41) < env.hedgerows && d01 < 0.9f && Hash01i(ax * 131 + az, bx * 17 + cz) < 0.45f)
                    hedges.Add((p0 * 0.5f + (p1 + p2) * 0.25f, at));
            }
            else
            {
                var crop = CropOf(key);
                (dry, ripe) = CropColours(crop, key);
                dry  = Shade(dry, tone);
                ripe = Shade(ripe, tone);
                if (crop == Crop.Wheat && d01 < 0.35f)
                {
                    if (!wheat.TryGetValue(key, out var f)) wheat[key] = f = new WheatField { delay = at };
                    f.spots.Add(mid + centre);
                }
            }

            int s = land.v.Count;
            land.v.Add(p0); land.v.Add(p1); land.v.Add(p2);
            land.n.Add(N(ax, az)); land.n.Add(N(bx, bz)); land.n.Add(N(cx, cz));
            for (int k = 0; k < 3; k++) { land.dry.Add(dry); land.ripe.Add(ripe); land.at.Add(at); }
            land.t.Add(s); land.t.Add(s + 1); land.t.Add(s + 2);
        }

        for (int iz = 0; iz < n - 1; iz++)
        {
            for (int ix = 0; ix < n - 1; ix++)
            {
                var mid = (P(ix, iz) + P(ix + 1, iz + 1)) * 0.5f - centre;
                if (mid.x * mid.x + mid.z * mid.z < hole * hole) continue;   // open under the board
                LandTri(ix, iz, ix, iz + 1, ix + 1, iz);            // clockwise from above
                LandTri(ix + 1, iz, ix, iz + 1, ix + 1, iz + 1);
            }
            if (Over()) { yield return null; sw.Restart(); }
        }

        var landMat = new Material(_mat) { name = "CountryLand (runtime)" };
        landMat.SetFloat("_VertexColor", 1f);
        _ownedMats.Add(landMat);
        BulkMesh(parent, "CountryLand", land, landMat);
        yield return null; sw.Restart();

        // ── Woods and hedges ─────────────────────────────────────────────────
        var vegMat = new Material(_mat) { name = "CountryVegetation (runtime)" };
        vegMat.SetFloat("_VertexColor", 1f);
        vegMat.SetFloat("_GreenAmount", 0f);
        _ownedMats.Add(vegMat);

        var veg = new List<Bulk> { new Bulk() };
        Bulk Veg()
        {
            if (veg[veg.Count - 1].v.Count > 60000) veg.Add(new Bulk());
            return veg[veg.Count - 1];
        }

        float sp = 3.2f * cs / Mathf.Sqrt(Mathf.Max(0.2f, GraphicsQuality.Detail));   // fewer woodland trees on the lower presets
        int m = Mathf.CeilToInt(outer * 2f / sp);
        int treeN = 0;
        for (int gz = 0; gz < m; gz++)
        {
            for (int gx = 0; gx < m; gx++)
            {
                float x = -outer + (gx + Rand(rng, 0.15f, 0.85f)) * sp;
                float z = -outer + (gz + Rand(rng, 0.15f, 0.85f)) * sp;
                float d = Mathf.Sqrt(x * x + z * z);
                if (d > outer * 0.97f || !Wood(x, z) || rng.NextDouble() < 0.12) continue;
                float d01 = Mathf.InverseLerp(inner, outer, d);
                bool conifer = Sample(h, x, z) > _peakRise.y * 0.55f || rng.NextDouble() < 0.3;
                CountryTree(Veg(), World(x, z), Rand(rng, 2.2f, 3.4f) * cs, conifer, Rand(rng, 0f, Mathf.PI * 2f), leaf,
                            0.3f + 2.4f * d01 + Rand(rng, 0f, 0.4f), treeN++);
            }
            if (Over()) { yield return null; sw.Restart(); }
        }

        int hedgeN = 0;
        foreach (var (at, t) in hedges)
        {
            if (hedgeN++ > GraphicsQuality.Scaled(1400)) break;
            var dryH  = Shade(new Color(0.48f, 0.37f, 0.23f, 1f), Rand(rng, 0.85f, 1.1f));
            var ripeH = Shade(Color.Lerp(leaf, new Color(0.15f, 0.35f, 0.15f, 1f), 0.3f), Rand(rng, 0.85f, 1.1f));
            float r = Rand(rng, 0.8f, 1.1f) * cs;
            BulkBlob(Veg(), at + Vector3.up * (0.3f * cs), new Vector3(r, r * 0.75f, r), Rand(rng, 0f, 360f), dryH, ripeH, t + 0.2f);
        }
        for (int i = 0; i < veg.Count; i++) BulkMesh(parent, $"CountryVegetation{i}", veg[i], vegMat);
        yield return null; sw.Restart();

        // ── Real wheat, on the near fields ───────────────────────────────────
        var near = new List<WheatField>(wheat.Values);
        near.RemoveAll(f => f.spots.Count < 6);
        near.Sort((a, b) => a.delay.CompareTo(b.delay));
        for (int i = 0; i < near.Count && i < GraphicsQuality.Scaled(14, 3); i++)
        {
            var f = near[i];
            var c = Vector3.zero;
            foreach (var p in f.spots) c += p;
            f.centre = c / f.spots.Count;
            _wheatFields.Add(f);
        }

        // ── Farmsteads, mills, bare trees, flower spots ──────────────────────
        // Denser toward the board, where they're seen best. Windmills want a hilltop.
        int houses = 0, trees = 0, spots = 0, mills = 0;
        int wantHouses = GraphicsQuality.Scaled(env.landHouses), wantTrees = GraphicsQuality.Scaled(env.landTrees);
        int wantSpots  = GraphicsQuality.Scaled(env.landSpots),  wantMills = GraphicsQuality.Scaled(env.windmills, 1);
        for (int tries = 0; tries < 6000 && (houses < wantHouses || trees < wantTrees
                                             || spots < wantSpots || mills < wantMills); tries++)
        {
            float a = Rand(rng, 0f, Mathf.PI * 2f);
            float d = Mathf.Lerp(inner + 7f * cs, outer * 0.85f, Mathf.Pow(Rand(rng, 0f, 1f), 1.4f));
            float x = Mathf.Cos(a) * d, z = Mathf.Sin(a) * d;
            if (Wood(x, z)) continue;
            float hc = Sample(h, x, z);
            float slope = Mathf.Abs(Sample(h, x + cs, z) - Sample(h, x - cs, z)) + Mathf.Abs(Sample(h, x, z + cs) - Sample(h, x, z - cs));
            var w = World(x, z);
            var local = parent.InverseTransformPoint(w);
            float edge = FieldAt(x, z).edge;

            if (mills < wantMills && hc > _peakRise.y * 0.4f && slope < 1.4f && rng.NextDouble() < 0.4)
            {
                CountryMill(parent, local, rng, cs);
                mills++;
            }
            else if (houses < wantHouses && slope < 0.8f && edge > 1.5f * cs && rng.NextDouble() < 0.3)
            {
                if (rng.NextDouble() < 0.4) CountryBarn(parent, local, rng, cs);
                else                        House(parent, local, rng, cs);
                houses++;
            }
            else if (trees < wantTrees && edge < 1.2f * cs && d < inner + 50f * cs && rng.NextDouble() < 0.5)
            {
                DeadTree(parent, local, rng, cs, 0.7f);
                trees++;
            }
            else if (spots < wantSpots && edge < 1.4f * cs && d < inner + 35f * cs)
            {
                _groundSpots.Add((w, 1.3f * cs));
                spots++;
            }
            if (Over()) { yield return null; sw.Restart(); }
        }

        // The bloom's plants, made now rather than on the frame the synergy fires.
        if (env.backdropBloomOn != BlockColor.None)
        {
            yield return null;
            PrepareBloom(env.backdropLeafColor);
        }

        LoadingScreen.Release(this);
    }

    // One bulk list as a mesh in the scene, put on the bloom's clock. The
    // vertices are already in world space, so the object sits at the world origin.
    Mesh BulkMesh(Transform parent, string name, Bulk b, Material mat)
    {
        if (b.v.Count == 0) return null;
        var m = new Mesh { name = name };
        if (b.v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(b.v);
        m.SetNormals(b.n);
        m.SetColors(b.dry);
        m.SetTriangles(b.t, 0);
        m.RecalculateBounds();
        Ripens(m, b.ripe.ToArray(), b.at.ToArray(), mat);
        m.UploadMeshData(true);   // never touched on the CPU again: free the copy
        _grownMeshes.Add(m);

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.AddComponent<MeshFilter>().sharedMesh = m;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial       = mat;
        r.shadowCastingMode    = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows       = false;
        r.lightProbeUsage      = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return m;
    }

    // A woodland tree: a trunk, then either two stacked cones (a conifer) or a
    // pair of faceted blobs (broadleaf). Withered: rust and ochre, a drab olive
    // for the conifers. Ripe: the level's leaf green.
    static void CountryTree(Bulk b, Vector3 at, float size, bool conifer, float yaw, Color leaf, float t, int seed)
    {
        var bark = new Color(0.30f, 0.22f, 0.16f, 1f);
        BulkFrustum(b, at - Vector3.up * (0.3f * size), size * 0.85f, size * 0.1f, size * 0.07f, 4, yaw, bark, Shade(bark, 1.1f), t);
        if (conifer)
        {
            var dryC  = Color.Lerp(new Color(0.42f, 0.34f, 0.20f, 1f), new Color(0.50f, 0.38f, 0.22f, 1f), Hash01i(seed, 3));
            var ripeC = Shade(Color.Lerp(leaf, new Color(0.10f, 0.30f, 0.20f, 1f), 0.45f), 0.9f + 0.2f * Hash01i(seed, 5));
            BulkFrustum(b, at + Vector3.up * (size * 0.35f), size * 1.2f, size * 0.62f, 0f, 6, yaw, dryC, ripeC, t);
            BulkFrustum(b, at + Vector3.up * (size * 0.95f), size * 0.95f, size * 0.46f, 0f, 6, yaw + 0.5f,
                        Shade(dryC, 1.08f), Shade(ripeC, 1.1f), t + 0.1f);
        }
        else
        {
            var dryC  = Color.Lerp(new Color(0.62f, 0.36f, 0.16f, 1f), new Color(0.76f, 0.52f, 0.24f, 1f), Hash01i(seed, 3));
            var ripeC = Color.Lerp(Shade(leaf, 0.78f), Color.Lerp(leaf, new Color(0.80f, 0.90f, 0.40f, 1f), 0.35f), Hash01i(seed, 5));
            float r = size * 0.62f;
            float yawDeg = yaw * Mathf.Rad2Deg;
            BulkBlob(b, at + Vector3.up * (size * 0.55f + r * 0.7f), new Vector3(r, r * 0.85f, r), yawDeg, dryC, ripeC, t);
            var off = Quaternion.Euler(0f, yawDeg + 60f, 0f) * Vector3.right * (r * 0.55f);
            BulkBlob(b, at + Vector3.up * (size * 0.5f + r * 0.5f) + off, Vector3.one * (r * 0.7f), yawDeg + 40f,
                     Shade(dryC, 0.9f), Shade(ripeC, 0.9f), t + 0.08f);
        }
    }

    // A `sides`-gon frustum standing on `baseC`, radius r0 at the foot to r1 at
    // the top (0 = a cone). Open underneath, where it meets the ground.
    static void BulkFrustum(Bulk b, Vector3 baseC, float height, float r0, float r1, int sides, float yaw,
                            Color dry, Color ripe, float at)
    {
        var inside = baseC + Vector3.up * (height * 0.3f);
        var top    = baseC + Vector3.up * height;
        for (int i = 0; i < sides; i++)
        {
            float a0 = yaw + i * Mathf.PI * 2f / sides, a1 = yaw + (i + 1) * Mathf.PI * 2f / sides;
            var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
            var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            Vector3 b0 = baseC + d0 * r0, b1 = baseC + d1 * r0;
            Vector3 u0 = top + d0 * r1,   u1 = top + d1 * r1;
            BulkTri(b, b0, b1, u0, inside, dry, ripe, at);
            if (r1 > 1e-4f)
            {
                BulkTri(b, b1, u1, u0, inside, dry, ripe, at);
                BulkTri(b, top, u0, u1, inside, dry, ripe, at);
            }
        }
    }

    // A faceted blob: an icosahedron stretched to `radius` on each axis.
    static void BulkBlob(Bulk b, Vector3 c, Vector3 radius, float yawDeg, Color dry, Color ripe, float at)
    {
        Ico();
        var q = Quaternion.Euler(0f, yawDeg, 0f);
        for (int f = 0; f < _icoF.Length; f += 3)
            BulkTri(b, c + q * Vector3.Scale(_ico[_icoF[f]], radius),
                       c + q * Vector3.Scale(_ico[_icoF[f + 1]], radius),
                       c + q * Vector3.Scale(_ico[_icoF[f + 2]], radius), c, dry, ripe, at);
    }

    // One flat-shaded triangle, wound to face away from `inside` (convex solids only).
    static void BulkTri(Bulk b, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 inside, Color dry, Color ripe, float at)
    {
        var nn = Vector3.Cross(p1 - p0, p2 - p0);
        if (Vector3.Dot(nn, (p0 + p1 + p2) / 3f - inside) < 0f) { var s = p1; p1 = p2; p2 = s; nn = -nn; }
        nn.Normalize();
        int i = b.v.Count;
        b.v.Add(p0); b.v.Add(p1); b.v.Add(p2);
        for (int k = 0; k < 3; k++) { b.n.Add(nn); b.dry.Add(dry); b.ripe.Add(ripe); b.at.Add(at); }
        b.t.Add(i); b.t.Add(i + 1); b.t.Add(i + 2);
    }

    static void Ico()
    {
        if (_ico != null) return;
        float g = (1f + Mathf.Sqrt(5f)) * 0.5f;
        _ico = new[]
        {
            new Vector3(-1f,  g, 0f), new Vector3( 1f,  g, 0f), new Vector3(-1f, -g, 0f), new Vector3( 1f, -g, 0f),
            new Vector3( 0f, -1f,  g), new Vector3( 0f,  1f,  g), new Vector3( 0f, -1f, -g), new Vector3( 0f,  1f, -g),
            new Vector3( g, 0f, -1f), new Vector3( g, 0f,  1f), new Vector3(-g, 0f, -1f), new Vector3(-g, 0f,  1f),
        };
        for (int i = 0; i < _ico.Length; i++) _ico[i] = _ico[i].normalized;
        _icoF = new[]
        {
            0, 11, 5,  0, 5, 1,   0, 1, 7,   0, 7, 10,  0, 10, 11,
            1, 5, 9,   5, 11, 4,  11, 10, 2, 10, 7, 6,  7, 1, 8,
            3, 9, 4,   3, 4, 2,   3, 2, 6,   3, 6, 8,   3, 8, 9,
            4, 9, 5,   2, 4, 11,  6, 2, 10,  8, 6, 7,   9, 8, 1,
        };
    }

    static float Hash01i(int a, int b)
    {
        unchecked
        {
            int h = a * 73856093 ^ b * 19349663;
            h = (h ^ 61) ^ (h >> 16);
            h += h << 3;
            h ^= h >> 4;
            h *= 0x27d4eb2d;
            h ^= h >> 15;
            return (h & 0xffffff) / (float)0xffffff;
        }
    }

    // A whitewashed tower mill, facing the board, with four sails on a hub.
    // The sails stand still until the land blooms (see Update).
    void CountryMill(Transform p, Vector3 localAt, System.Random rng, float cs)
    {
        var mill = new GameObject("Windmill").transform;
        mill.SetParent(p, false);
        mill.localPosition = localAt;
        var inward = new Vector3(-localAt.x, 0f, -localAt.z);
        mill.localRotation = inward.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(inward) : Quaternion.identity;

        float th = Rand(rng, 5f, 6.5f) * cs, tw = 1.7f * cs;
        var white = new Color(0.88f, 0.84f, 0.76f, 1f);
        Paint(Box(mill, new Vector3(0f, th * 0.3f, 0f), new Vector3(tw, th * 0.62f, tw), Vector3.zero), white);
        Paint(Box(mill, new Vector3(0f, th * 0.75f, 0f), new Vector3(tw * 0.82f, th * 0.3f, tw * 0.82f), Vector3.zero), Shade(white, 0.95f));
        Paint(Part(mill, Gable(), new Vector3(0f, th * 0.9f, 0f), new Vector3(tw * 1.1f, tw * 0.7f, tw * 1.1f), new Vector3(0f, 90f, 0f)),
              new Color(0.30f, 0.26f, 0.24f, 1f));
        Paint(Box(mill, new Vector3(0f, th * 0.14f, tw * 0.5f + 0.02f * cs), new Vector3(tw * 0.34f, th * 0.26f, 0.06f * cs), Vector3.zero),
              new Color(0.40f, 0.28f, 0.20f, 1f));

        var hub = new GameObject("Hub").transform;
        hub.SetParent(mill, false);
        hub.localPosition = new Vector3(0f, th * 0.92f, tw * 0.5f + 0.25f * cs);
        hub.localRotation = Quaternion.Euler(0f, 0f, Rand(rng, 0f, 90f));
        float len = th * 0.55f;
        for (int k = 0; k < 4; k++)
        {
            var arm = new GameObject($"Sail{k}").transform;
            arm.SetParent(hub, false);
            arm.localRotation = Quaternion.Euler(0f, 0f, k * 90f);
            Paint(Box(arm, new Vector3(0f, len * 0.5f, 0f), new Vector3(0.12f * cs, len, 0.12f * cs), Vector3.zero),
                  new Color(0.42f, 0.30f, 0.22f, 1f));
            Paint(Box(arm, new Vector3(0.3f * cs, len * 0.58f, 0.04f * cs), new Vector3(0.55f * cs, len * 0.78f, 0.05f * cs), Vector3.zero),
                  new Color(0.92f, 0.88f, 0.80f, 1f));
        }
        _sails.Add(hub);
    }

    // A red barn under a grey roof, with a few bales by it.
    void CountryBarn(Transform p, Vector3 localAt, System.Random rng, float cs)
    {
        var barn = new GameObject("Barn").transform;
        barn.SetParent(p, false);
        barn.localPosition = localAt;
        barn.localRotation = Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f);
        float bw = Rand(rng, 3f, 4.5f) * cs, bd = Rand(rng, 4.5f, 6.5f) * cs, bh = Rand(rng, 2.2f, 3f) * cs;
        Paint(Box(barn, new Vector3(0f, bh * 0.5f, 0f), new Vector3(bw, bh, bd), Vector3.zero),
              new Color(0.62f, 0.15f, 0.10f, 1f));
        // Ridge along the barn's length (the gable mesh's ridge runs along X).
        Paint(Part(barn, Gable(), new Vector3(0f, bh, 0f), new Vector3(bd * 1.06f, bh * 0.6f, bw * 1.16f), new Vector3(0f, 90f, 0f)),
              new Color(0.44f, 0.50f, 0.58f, 1f));
        Paint(Box(barn, new Vector3(0f, bh * 0.32f, bd * 0.5f + 0.02f * cs), new Vector3(bw * 0.4f, bh * 0.64f, 0.06f * cs), Vector3.zero),
              new Color(0.86f, 0.64f, 0.28f, 1f));
        int bales = rng.Next(1, 4);
        for (int k = 0; k < bales; k++)
        {
            float s = Rand(rng, 0.7f, 1f) * cs;
            Paint(Box(barn, new Vector3(Rand(rng, -1f, 1f) * bw, s * 0.4f, bd * 0.5f + Rand(rng, 1f, 2.5f) * cs),
                      new Vector3(s, s * 0.8f, s * 1.2f), new Vector3(0f, Rand(rng, 0f, 180f), 0f)),
                  new Color(0.88f, 0.66f, 0.30f, 1f));
        }
    }

    // The near wheat fields' stalks, one combined mesh per field, coming up out of
    // the ground as that field ripens.
    void GrowCountryWheat(System.Random rng)
    {
        var stalk = LevelMapController.SharedWheatMesh();
        if (stalk == null || WheatMat() == null) return;
        Color gold  = _growth.a  > 0f ? _growth  : new Color(0.84f, 0.6f, 0.18f, 1f);
        Color gold2 = _growth2.a > 0f ? _growth2 : new Color(1f, 0.8f, 0.32f, 1f);

        var parts = new List<CombineInstance>();
        foreach (var f in _wheatFields)
        {
            parts.Clear();
            foreach (var p in f.spots)
            {
                if (rng.NextDouble() < 0.15) continue;
                var at = p - f.centre + new Vector3(Rand(rng, -0.5f, 0.5f), -0.15f, Rand(rng, -0.5f, 0.5f)) * _cs;
                float hh = Rand(rng, 1.4f, 2.2f) * _cs;
                parts.Add(new CombineInstance
                {
                    mesh = stalk,
                    transform = Matrix4x4.TRS(at, Quaternion.Euler(Rand(rng, -8f, 8f), Rand(rng, 0f, 360f), Rand(rng, -8f, 8f)),
                                              new Vector3(hh * 1.3f, hh, hh * 1.3f)),
                });
            }
            if (parts.Count == 0) continue;

            var field = new Mesh { name = "CountryWheat" };
            if (parts.Count * stalk.vertexCount > 65000) field.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            field.CombineMeshes(parts.ToArray(), true, true);
            _grownMeshes.Add(field);

            var go = new GameObject("Wheat");
            go.transform.SetParent(transform, false);
            go.transform.position = f.centre;
            go.AddComponent<MeshFilter>().sharedMesh = field;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial    = _wheatMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows    = false;
            Paint(go.transform, Color.Lerp(gold, gold2, (float)rng.NextDouble()));
            var home = go.transform.localPosition;
            go.SetActive(false);   // until its turn — see GrowStep
            _growing.Add(new Growing { t = go.transform, scale = Vector3.one, delay = f.delay, emerge = true, home = home, depth = 2.6f * _cs });
        }
    }
}
