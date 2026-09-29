using System.Collections.Generic;
using UnityEngine;

// Far scenery round the play area, from the level's LevelEnvironment: cliffs,
// ruins, floating islands or barren fields, standing in a ring well outside
// anywhere a block goes. No colliders — nothing here can be clicked, built on or block a ray.
// Drawn flat (GeoWorld/Backdrop) and hazed with distance, so it reads as shapes in
// the air behind the board rather than as detail to look at; the height fog and
// far haze, when on, swallow its feet.
public partial class EnvironmentBackdrop : MonoBehaviour
{
    static Mesh _cube, _spike;

    Material _mat, _wheatMat;
    readonly List<Material> _ownedMats = new();
    readonly List<(Transform t, float baseY, float amp, float phase)> _bob = new();

    // Where barren fields can come back to life (Bloom): dry tufts and bare
    // ground flower, and the dead trees are replaced by living ones.
    readonly List<(Vector3 at, float size)>     _groundSpots = new();
    readonly List<(Transform t, float height)>  _deadTrees   = new();
    readonly List<Renderer>  _land  = new();   // barren ground and peaks — they green over
    readonly List<Transform> _peaks = new();   // grass comes up their slopes

    struct Greening { public Renderer r; public float delay; }
    Vector2 _peakRise = new(4f, 15f);
    bool    _farm;
    float   _fieldScale = 1f;
    Vector2 _shelf = new(-9f, -2f);
    // The field strips of the piece being built, in its own space — flowers are kept off them.
    readonly List<Rect> _pieceStrips = new();
    Color   _growth, _growth2;   // bloom colours; alpha 0 = derive from the leaf colour
    bool    _growSkin = true;
    readonly List<Greening> _greening = new();
    // Land that ripens when it blooms. The vertex colour is the dry colour; the
    // ripe colour and when each vertex turns (seconds after the bloom) ride in
    // TEXCOORD2, and GeoWorld/Backdrop eases between them from _RipenT. So the
    // ripening costs one float a frame per material, not a re-upload of every
    // vertex colour. Terraces ripen bottom up; the countryside field by field,
    // outward from the board, with its woods and hedges.
    readonly List<Material> _ripenMats = new();
    float _ripenEnd;
    bool  _ripen;

    // The plants the bloom grows, built while the level loads rather than on the
    // frame the synergy fires.
    Mesh[] _bloomTrees, _bloomShrubs, _bloomGrass;
    TreeMesh.Recipe _treeR, _shrubR, _grassR;
    Color _bloomLeaf;

    // Trees grown branch by branch (VineEffect) instead of popped in.
    GameObject _treePrefab;
    class VineSpawn { public Vector3 at; public float scale, delay; public bool done; public Color leaf; }
    readonly List<VineSpawn> _vines = new();
    Transform _growthRoot;
    MaterialPropertyBlock _mpb;
    readonly List<Mesh> _grownMeshes = new();
    float _cs = 1f;
    float _dist01;   // how far out the piece being built stands, 0 (inner edge of the ring) … 1 (outer)
    bool  _bloomed;

    // shrink: withers away. rise: grows in height only. emerge: comes up out of the
    // ground from `depth` below `home` (a field of stalks on a slope, which a
    // height-only scale would drag toward its pivot). Otherwise: pops, all three axes.
    struct Growing
    {
        public Transform t; public Vector3 scale; public float delay; public bool shrink, rise, emerge;
        public Vector3 home; public float depth;
    }

    // Field strips (Farmland) the wheat grows on: the piece, the strip's top-centre
    // in the piece's space, and its size (x across, y along).
    readonly List<(Transform piece, Vector3 centre, Vector2 size)> _fieldStrips = new();
    readonly List<Growing> _growing = new();
    float _growT = -1f;

    public static EnvironmentBackdrop Build(Transform parent, LevelEnvironment env, Vector3 centre, float floorY, float cs)
    {
        var sh = Shader.Find("GeoWorld/Backdrop");
        if (sh == null) { Debug.LogWarning("[Backdrop] GeoWorld/Backdrop shader not found — no scenery."); return null; }

        var go = new GameObject("Backdrop");
        go.transform.SetParent(parent, false);
        var b = go.AddComponent<EnvironmentBackdrop>();
        b._cs = cs;

        b._mat = new Material(sh) { name = "Backdrop (runtime)" };
        b._mat.SetColor("_Color",     env.backdropColor);
        b._mat.SetColor("_HazeColor", env.backdropHaze);
        b._mat.SetFloat("_HazeStart", env.backdropDistance.x * cs * 0.5f);
        b._mat.SetFloat("_HazeRange", env.backdropDistance.y * cs * 1.2f);
        b._mat.SetFloat("_HazeMax",   env.backdropHazeMax);
        b._mat.SetFloat("_BaseY",     floorY - 30f * cs);
        b._mat.SetFloat("_BaseRange", 26f * cs);
        b._mat.SetColor("_SunGlow",   env.backdropSunGlow);
        b._mat.SetColor("_Rim",       env.backdropRim);
        b._mat.SetColor("_Accent",    env.backdropAccent);
        b._mat.SetFloat("_AccentAmount", env.backdropAccentAmount);

        var rng = new System.Random(env.backdropSeed);
        b._peakRise   = env.backdropPeakRise;
        // Low preset: bloom trees pop in instead of growing branch by branch.
        b._treePrefab = GraphicsQuality.GrowingTrees ? env.backdropTreePrefab : null;
        b._growth     = env.backdropGrowth;
        b._growth2    = env.backdropGrowth2;
        bool blooms = env.backdropBloomOn != BlockColor.None;
        // One landform, not pieces.
        if (env.backdrop == LevelEnvironment.Backdrop.Terraces)
        {
            b.TerraceLand(go.transform, env, centre, floorY, cs, rng);
            if (blooms) b.PrepareBloom(env.backdropLeafColor);
            return b;
        }
        if (env.backdrop == LevelEnvironment.Backdrop.Countryside)
        {
            // Built over several frames, holding the loading page until it's done.
            b.StartCoroutine(b.Countryside(go.transform, env, centre, floorY, cs, rng));
            return b;
        }
        int n = GraphicsQuality.Scaled(env.backdropCount, 4);
        for (int i = 0; i < n; i++)
        {
            // Stratified round the ring: one piece per sector, jittered, so it never clumps.
            float ang  = (i + Rand(rng, 0.2f, 0.8f)) / n * Mathf.PI * 2f;
            float dist = Rand(rng, env.backdropDistance.x, env.backdropDistance.y) * cs;
            var at = new Vector3(centre.x + Mathf.Cos(ang) * dist, floorY, centre.z + Mathf.Sin(ang) * dist);
            float yaw = Rand(rng, 0f, 360f);

            var piece = new GameObject(env.backdrop.ToString()).transform;
            piece.SetParent(go.transform, false);
            piece.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
            b._dist01 = Mathf.InverseLerp(env.backdropDistance.x, env.backdropDistance.y, dist / cs);
            b._peakRise = env.backdropPeakRise;
            b._farm     = env.backdropFarm;
            b._fieldScale = env.backdropFieldScale;
            b._shelf    = env.backdropShelfHeight;
            b._growth   = env.backdropGrowth;
            b._growth2  = env.backdropGrowth2;
            b._growSkin = env.backdropGrowSkin;

            switch (env.backdrop)
            {
                case LevelEnvironment.Backdrop.Cliffs:          b.Cliff(piece, rng, cs);  break;
                case LevelEnvironment.Backdrop.Ruins:           b.Ruin(piece, rng, cs);   break;
                case LevelEnvironment.Backdrop.FloatingIslands: b.Island(piece, rng, cs); break;
                case LevelEnvironment.Backdrop.BarrenFields:    b.Barren(piece, rng, cs); break;
            }
        }
        if (blooms) b.PrepareBloom(env.backdropLeafColor);
        return b;
    }

    // ── Pieces (built in the piece's local space, y = the board's floor) ────────

    // A mesa rising from far below, stepped into a terrace or two at the top.
    void Cliff(Transform p, System.Random rng, float cs)
    {
        float w = Rand(rng, 8f, 18f) * cs, d = Rand(rng, 6f, 14f) * cs;
        float top = Rand(rng, -3f, 8f) * cs;
        Box(p, new Vector3(0f, (top - 60f * cs) * 0.5f, 0f), new Vector3(w, top + 60f * cs, d), Vector3.zero);

        int steps = rng.Next(1, 3);
        for (int i = 0; i < steps; i++)
        {
            float sw = w * Rand(rng, 0.35f, 0.7f), sd = d * Rand(rng, 0.35f, 0.7f), sh = Rand(rng, 2f, 6f) * cs;
            var off = new Vector3(Rand(rng, -0.25f, 0.25f) * w, top + sh * 0.5f, Rand(rng, -0.25f, 0.25f) * d);
            Box(p, off, new Vector3(sw, sh, sd), Vector3.zero);
            top += sh; w = sw; d = sd;
        }
    }

    // A lower mesa with broken columns on it — an arch now and then, a fallen drum.
    void Ruin(Transform p, System.Random rng, float cs)
    {
        float w = Rand(rng, 10f, 16f) * cs, d = Rand(rng, 8f, 13f) * cs;
        float top = Rand(rng, -6f, 0f) * cs;
        Box(p, new Vector3(0f, (top - 60f * cs) * 0.5f, 0f), new Vector3(w, top + 60f * cs, d), Vector3.zero);

        int columns = rng.Next(2, 6);
        for (int i = 0; i < columns; i++)
        {
            float cw = Rand(rng, 0.8f, 1.6f) * cs, ch = Rand(rng, 3f, 12f) * cs;
            var at = new Vector3(Rand(rng, -0.35f, 0.35f) * w, top + ch * 0.5f, Rand(rng, -0.35f, 0.35f) * d);
            var tilt = rng.NextDouble() < 0.4 ? new Vector3(Rand(rng, -12f, 12f), 0f, Rand(rng, -12f, 12f)) : Vector3.zero;
            Box(p, at, new Vector3(cw, ch, cw), tilt);
        }

        if (rng.NextDouble() < 0.45)
        {
            float span = Rand(rng, 3f, 6f) * cs, ah = Rand(rng, 5f, 9f) * cs, aw = 1.1f * cs;
            var mid = new Vector3(Rand(rng, -0.2f, 0.2f) * w, top, Rand(rng, -0.2f, 0.2f) * d);
            Box(p, mid + new Vector3(-span * 0.5f, ah * 0.5f, 0f), new Vector3(aw, ah, aw), Vector3.zero);
            Box(p, mid + new Vector3( span * 0.5f, ah * 0.5f, 0f), new Vector3(aw, ah, aw), Vector3.zero);
            if (rng.NextDouble() < 0.7)   // the lintel, unless it has fallen
                Box(p, mid + new Vector3(0f, ah + 0.4f * cs, 0f), new Vector3(span + aw * 1.4f, 0.8f * cs, aw * 1.2f), Vector3.zero);
        }

        int fallen = rng.Next(0, 3);
        for (int i = 0; i < fallen; i++)
        {
            float len = Rand(rng, 2f, 5f) * cs;
            var at = new Vector3(Rand(rng, -0.35f, 0.35f) * w, top + 0.5f * cs, Rand(rng, -0.35f, 0.35f) * d);
            Box(p, at, new Vector3(len, 1f * cs, 1f * cs), new Vector3(0f, Rand(rng, 0f, 180f), Rand(rng, -8f, 8f)));
        }
    }

    // A slab of ground hanging in the air, its underside tapering to a point, with
    // a stone or two on top. Bobs very slowly.
    void Island(Transform p, System.Random rng, float cs)
    {
        float w = Rand(rng, 5f, 12f) * cs, d = w * Rand(rng, 0.7f, 1.2f);
        float y = Rand(rng, -4f, 14f) * cs;
        float depth = Rand(rng, 5f, 12f) * cs;

        var body = new GameObject("Island").transform;
        body.SetParent(p, false);
        body.localPosition = new Vector3(0f, y, 0f);

        Box(body, new Vector3(0f, -0.6f * cs, 0f), new Vector3(w, 1.2f * cs, d), Vector3.zero);
        var spike = Part(body, Spike(), new Vector3(0f, -1.2f * cs, 0f), new Vector3(w * 0.95f, depth, d * 0.95f), Vector3.zero);
        spike.name = "Underside";

        int stones = rng.Next(0, 4);
        for (int i = 0; i < stones; i++)
        {
            float s = Rand(rng, 0.8f, 2.2f) * cs, h = s * Rand(rng, 0.8f, 3f);
            Box(body, new Vector3(Rand(rng, -0.3f, 0.3f) * w, h * 0.5f, Rand(rng, -0.3f, 0.3f) * d),
                new Vector3(s, h, s), new Vector3(0f, Rand(rng, 0f, 90f), 0f));
        }

        _bob.Add((body, y, Rand(rng, 0.3f, 0.9f) * cs, Rand(rng, 0f, Mathf.PI * 2f)));
    }

    // Worked-out land: a broad, low shelf of ground a few cells under the board,
    // its edge stepped down in terraces, with what's left on it — a dead tree
    // bent by the wind, a few leaning fence posts, dry tufts.
    void Barren(Transform p, System.Random rng, float cs)
    {
        float w = Rand(rng, 16f, 30f) * cs, d = Rand(rng, 10f, 20f) * cs;
        if (_farm) { w *= _fieldScale; d *= _fieldScale; }   // broad, overlapping farmland
        float top = Rand(rng, Mathf.Min(_shelf.x, _shelf.y), Mathf.Max(_shelf.x, _shelf.y)) * cs;
        _pieceStrips.Clear();
        Land(Box(p, new Vector3(0f, (top - 60f * cs) * 0.5f, 0f), new Vector3(w, top + 60f * cs, d), Vector3.zero));

        // A terrace or two dropping away along one side.
        int steps = rng.Next(1, 3);
        float stepTop = top;
        for (int i = 0; i < steps; i++)
        {
            stepTop -= Rand(rng, 1.2f, 2.6f) * cs;
            float sw = w * Rand(rng, 0.5f, 0.9f);
            Land(Box(p, new Vector3(Rand(rng, -0.2f, 0.2f) * w, (stepTop - 60f * cs) * 0.5f, d * (0.5f + 0.18f * (i + 1))),
                new Vector3(sw, stepTop + 60f * cs, d * 0.4f), Vector3.zero));
        }

        if (_farm) Farmland(p, rng, cs, w, d, top);

        // Ridges standing up out of the fog, layered: taller and more of them the
        // further out the piece is, so the far ring reads as range behind range.
        int peaks = _peakRise.y <= 0f ? 0 : rng.Next(1, 3) + (_dist01 > 0.55f ? 1 : 0);
        for (int i = 0; i < peaks; i++)
        {
            float rise = Mathf.Lerp(_peakRise.x, _peakRise.y, _dist01) * Rand(rng, 0.7f, 1.3f) * cs;   // summit above the board's floor
            float foot = top - 2f * cs;
            float pw   = (rise - foot) * Rand(rng, 1.3f, 2.3f);
            var peak = Part(p, Peak(), new Vector3(Rand(rng, -0.45f, 0.45f) * w, foot, Rand(rng, -0.45f, 0.45f) * d),
                 new Vector3(pw, rise - foot, pw * Rand(rng, 0.55f, 1f)), new Vector3(0f, Rand(rng, 0f, 360f), 0f));
            Land(peak);
            _peaks.Add(peak);
        }

        // Dead trees: a leaning trunk, a few bare limbs.
        int trees = rng.Next(0, 3);
        for (int i = 0; i < trees; i++)
            DeadTree(p, new Vector3(Rand(rng, -0.4f, 0.4f) * w, top, Rand(rng, -0.4f, 0.4f) * d), rng, cs, 1f);

        // A broken fence line: leaning posts in a rough row.
        if (rng.NextDouble() < 0.6)
        {
            int posts = rng.Next(3, 8);
            var start = new Vector3(Rand(rng, -0.4f, 0f) * w, top, Rand(rng, -0.35f, 0.35f) * d);
            float step = Rand(rng, 1.4f, 2.2f) * cs, yaw = Rand(rng, 0f, 180f);
            var along = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            for (int k = 0; k < posts; k++)
            {
                if (rng.NextDouble() < 0.25) continue;   // gaps where posts have gone
                float ph = Rand(rng, 0.8f, 1.5f) * cs;
                Box(p, start + along * (step * k) + Vector3.up * (ph * 0.45f), new Vector3(0.22f * cs, ph, 0.22f * cs),
                    new Vector3(Rand(rng, -18f, 18f), yaw, Rand(rng, -18f, 18f)));
            }
        }

        // Dry tufts.
        int tufts = rng.Next(3, 9);
        for (int k = 0; k < tufts; k++)
        {
            var at = new Vector3(Rand(rng, -0.45f, 0.45f) * w, top, Rand(rng, -0.45f, 0.45f) * d);
            float s = Rand(rng, 0.4f, 0.9f) * cs;
            Box(p, at + Vector3.up * (s * 0.3f), new Vector3(s * 1.4f, s * 0.6f, s * 1.1f),
                new Vector3(0f, Rand(rng, 0f, 90f), Rand(rng, -12f, 12f)));
            if (!OnStrip(at)) _groundSpots.Add((p.TransformPoint(at + Vector3.up * (s * 0.55f)), s * 1.3f));
        }
        // Bare patches of the shelf, so a bloom reads as the land greening, not a few tufts.
        int bare = rng.Next(4, 9);
        for (int k = 0; k < bare; k++)
        {
            var at = new Vector3(Rand(rng, -0.45f, 0.45f) * w, top, Rand(rng, -0.45f, 0.45f) * d);
            if (!OnStrip(at)) _groundSpots.Add((p.TransformPoint(at), Rand(rng, 1.2f, 2.4f) * cs));
        }
    }

    // Is this point (piece space) on one of the piece's field strips — wheat ground,
    // where flowers don't go? A cell's margin, since a flower spot scatters.
    bool OnStrip(Vector3 at)
    {
        foreach (var r in _pieceStrips)
            if (at.x > r.xMin - _cs && at.x < r.xMax + _cs && at.z > r.yMin - _cs && at.z < r.yMax + _cs) return true;
        return false;
    }

    // A bare tree — a leaning trunk, a few limbs. Leafs out (a living grove tree
    // comes up in its place) when the land blooms.
    void DeadTree(Transform p, Vector3 localAt, System.Random rng, float cs, float scale)
    {
        float th = Rand(rng, 3.5f, 7f) * cs * scale, tw = Rand(rng, 0.35f, 0.6f) * cs * scale;
        var tree = new GameObject("DeadTree").transform;
        tree.SetParent(p, false);
        tree.localPosition = localAt;
        tree.localRotation = Quaternion.Euler(Rand(rng, -9f, 9f), Rand(rng, 0f, 360f), Rand(rng, -9f, 9f));
        Box(tree, new Vector3(0f, th * 0.5f, 0f), new Vector3(tw, th, tw), Vector3.zero);
        int limbs = rng.Next(2, 5);
        for (int k = 0; k < limbs; k++)
        {
            float y = th * Rand(rng, 0.45f, 0.95f), len = th * Rand(rng, 0.25f, 0.45f);
            float yaw = Rand(rng, 0f, 360f), lift = Rand(rng, 25f, 60f);
            var dir = Quaternion.Euler(-lift, yaw, 0f) * Vector3.forward;
            Box(tree, new Vector3(0f, y, 0f) + dir * (len * 0.5f), new Vector3(tw * 0.45f, tw * 0.45f, len),
                Quaternion.LookRotation(dir).eulerAngles);
        }
        _deadTrees.Add((tree, th));
    }

    // ── Terraces ─────────────────────────────────────────────────────────────
    // One continuous terraced landscape round the board: a heightfield of rolling
    // hills that climb with distance, cut into treads by quantising the height — so
    // every terrace edge follows the land's own contour and runs on round one hill
    // into the next, the way paddies do. Coloured per vertex: treads by terrace
    // (dusty now; gold and green in turn once it blooms, bottom up), risers as bare
    // earth. Houses on flat treads; bare trees that grow leaves when it blooms.
    void TerraceLand(Transform parent, LevelEnvironment env, Vector3 centre, float floorY, float cs, System.Random rng)
    {
        float inner = env.backdropDistance.x * cs, outer = env.backdropDistance.y * cs;
        float grid  = GraphicsQuality.BackdropGrid * cs;
        int   n     = Mathf.CeilToInt(outer * 2f / grid) + 1;
        float stepCells = env.terraceStep;             // terrace height, in cells
        float tread     = env.terraceTread;            // flat share of each step
        Color ripeA = env.backdropGrowth.a  > 0f ? env.backdropGrowth  : new Color(0.88f, 0.66f, 0.20f, 1f);
        Color ripeB = env.backdropGrowth2.a > 0f ? env.backdropGrowth2 : new Color(0.50f, 0.66f, 0.24f, 1f);
        float ox = Rand(rng, 0f, 100f), oz = Rand(rng, 0f, 100f);

        var verts = new Vector3[n * n];
        var dry   = new Color[n * n];
        var ripe  = new Color[n * n];
        var h01   = new float[n * n];
        var level = new float[n * n];
        float lo = float.MaxValue, hi = float.MinValue;

        for (int iz = 0; iz < n; iz++)
            for (int ix = 0; ix < n; ix++)
            {
                int i = iz * n + ix;
                float x = -outer + ix * grid, z = -outer + iz * grid;
                float d = Mathf.Sqrt(x * x + z * z);
                float t = Mathf.InverseLerp(inner, outer, d);

                // Rolling hills that rise toward the outside, in cells above the floor.
                float hill = Fbm2(x / cs * 0.02f + ox, z / cs * 0.02f + oz);
                float raw  = -8f + (_peakRise.y + 8f) * Mathf.Pow(t, 0.8f) * (0.35f + 0.9f * hill) + (hill - 0.5f) * 5f;

                // Treads and risers: flat most of each step, a short steep rise at its end.
                float lv  = raw / stepCells;
                float fl  = Mathf.Floor(lv), fr = lv - fl;
                float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(tread, 1f, fr));
                float y   = (fl + rise) * stepCells;

                // Dropped away under the board and toward the middle.
                float ramp = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner - 4f * cs, inner + 14f * cs, d));
                y = Mathf.Lerp(-30f, y, ramp);

                verts[i] = centre + new Vector3(x, 0f, z) + Vector3.up * (floorY - centre.y + y * cs);
                level[i] = fl;
                lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);

                float j = 0.92f + 0.08f * Mathf.Repeat(fl * 0.37f, 1f);
                bool riser = fr > tread - 0.02f;
                var earth = Shade(env.landEarth, j);
                dry[i]  = riser ? earth : Shade(env.landDry, j);
                ripe[i] = riser ? earth : Shade((int)fl % 2 == 0 ? ripeA : ripeB, j);
                h01[i] = y;
            }
        for (int i = 0; i < h01.Length; i++) h01[i] = Mathf.InverseLerp(lo, hi, h01[i]);

        // Triangles, leaving the middle open where the board is.
        var tris = new List<int>((n - 1) * (n - 1) * 6);
        float hole = (inner - 6f * cs) * (inner - 6f * cs);
        for (int iz = 0; iz < n - 1; iz++)
            for (int ix = 0; ix < n - 1; ix++)
            {
                int a = iz * n + ix, b = a + 1, c = a + n, e = c + 1;
                Vector3 mid = (verts[a] + verts[e]) * 0.5f - centre;
                if (mid.x * mid.x + mid.z * mid.z < hole) continue;
                tris.Add(a); tris.Add(c); tris.Add(b);   // clockwise from above
                tris.Add(b); tris.Add(c); tris.Add(e);
            }

        var terrain = new Mesh { name = "TerraceLand" };
        if (verts.Length > 65000) terrain.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        terrain.vertices = verts;
        terrain.colors   = dry;
        terrain.SetTriangles(tris, 0);
        terrain.RecalculateNormals();
        terrain.RecalculateBounds();
        _grownMeshes.Add(terrain);
        var at = new float[h01.Length];
        for (int i = 0; i < at.Length; i++) at[i] = h01[i] * 2.6f;   // bottom up

        var land = new GameObject("TerraceLand");
        land.transform.SetParent(parent, false);
        land.AddComponent<MeshFilter>().sharedMesh = terrain;
        var r = land.AddComponent<MeshRenderer>();
        var landMat = new Material(_mat) { name = "TerraceLand (runtime)" };
        landMat.SetFloat("_VertexColor", 1f);
        Ripens(terrain, ripe, at, landMat);
        _ownedMats.Add(landMat);
        r.sharedMaterial = landMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        // Houses, trees and flower spots on flat treads (a vertex whose neighbours
        // share its terrace), in the band beyond the board.
        bool FlatAt(int ix, int iz)
        {
            if (ix < 1 || iz < 1 || ix >= n - 1 || iz >= n - 1) return false;
            int i = iz * n + ix;
            return level[i] == level[i - 1] && level[i] == level[i + 1] && level[i] == level[i - n] && level[i] == level[i + n];
        }
        int houses = 0, trees = 0, spots = 0;
        int wantHouses = GraphicsQuality.Scaled(env.landHouses), wantTrees = GraphicsQuality.Scaled(env.landTrees);
        int wantSpots  = GraphicsQuality.Scaled(env.landSpots);
        for (int tries = 0; tries < 4000 && (houses < wantHouses || trees < wantTrees || spots < wantSpots); tries++)
        {
            int ix = rng.Next(1, n - 1), iz = rng.Next(1, n - 1);
            int i = iz * n + ix;
            Vector3 rel = verts[i] - centre; rel.y = 0f;
            float d = rel.magnitude;
            if (d < inner + 6f * cs || d > outer * 0.9f || !FlatAt(ix, iz)) continue;
            Vector3 local = parent.InverseTransformPoint(verts[i]);
            if (houses < wantHouses && rng.NextDouble() < 0.25) { House(parent, local, rng, cs); houses++; }
            else if (trees < wantTrees && rng.NextDouble() < 0.5) { DeadTree(parent, local, rng, cs, 0.7f); trees++; }
            else if (spots < wantSpots) { _groundSpots.Add((verts[i], 1.3f * cs)); spots++; }
        }
    }

    static float Fbm2(float x, float z)
    {
        float s = 0f, a = 0.5f, f = 1f;
        for (int k = 0; k < 4; k++) { s += Mathf.PerlinNoise(x * f, z * f) * a; f *= 2.03f; a *= 0.5f; }
        return s / 0.9375f;
    }

    // A farmhouse: timber walls under a dark gable roof, turned to look downhill.
    void House(Transform p, Vector3 localAt, System.Random rng, float cs)
    {
        var h = new GameObject("House").transform;
        h.SetParent(p, false);
        h.localPosition = localAt;
        var outward = new Vector3(localAt.x, 0f, localAt.z);
        h.localRotation = outward.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(outward) : Quaternion.identity;
        float w = Rand(rng, 1.8f, 2.6f) * cs, d = Rand(rng, 1.4f, 1.9f) * cs, hh = Rand(rng, 1f, 1.4f) * cs;
        Paint(Box(h, new Vector3(0f, hh * 0.5f, 0f), new Vector3(w, hh, d), Vector3.zero), new Color(0.56f, 0.42f, 0.3f, 1f));
        Paint(Part(h, Gable(), new Vector3(0f, hh, 0f), new Vector3(w * 1.2f, hh * 0.7f, d * 1.3f), Vector3.zero),
              new Color(0.24f, 0.22f, 0.23f, 1f));
    }

    // Worked land on the shelf: strips of field running across it, split by a dirt
    // track; now and then a red barn with a grey roof, and hay bales. The strips are
    // land, so when the land blooms the crop grows over them.
    void Farmland(Transform p, System.Random rng, float cs, float w, float d, float top)
    {
        // Rows about two and a half cells deep, however big the piece — a bigger
        // piece is more field, not fatter strips.
        int rows = Mathf.Max(3, Mathf.RoundToInt(d / (2.5f * cs)));
        float rowD = d / rows;
        for (int r = 0; r < rows; r++)
        {
            if (rng.NextDouble() < 0.06) continue;   // the odd fallow gap
            float z = -d * 0.5f + rowD * (r + 0.5f);
            float sw = w * Rand(rng, 0.8f, 0.97f);
            var strip = Box(p, new Vector3(0f, top + 0.12f * cs, z),
                            new Vector3(sw, 0.24f * cs, rowD * 0.82f), Vector3.zero);
            Land(strip);
            _fieldStrips.Add((p, new Vector3(0f, top + 0.24f * cs, z), new Vector2(sw, rowD * 0.82f)));
            _pieceStrips.Add(new Rect(-sw * 0.5f, z - rowD * 0.41f, sw, rowD * 0.82f));
            float k = Rand(rng, 0.85f, 1.15f);
            Paint(strip, new Color(0.46f * k, 0.32f * k, 0.19f * k, 1f));   // stubble
        }
        // A dirt track across the rows.
        Paint(Box(p, new Vector3(Rand(rng, -0.3f, 0.3f) * w, top + 0.14f * cs, 0f),
                  new Vector3(Rand(rng, 0.8f, 1.4f) * cs, 0.26f * cs, d * 0.98f),
                  new Vector3(0f, Rand(rng, -8f, 8f), 0f)),
              new Color(0.52f, 0.36f, 0.24f, 1f));

        if (rng.NextDouble() < 0.3)
        {
            var barn = new GameObject("Barn").transform;
            barn.SetParent(p, false);
            barn.localPosition = new Vector3(Rand(rng, -0.3f, 0.3f) * w, top + 0.24f * cs, Rand(rng, -0.3f, 0.3f) * d);
            barn.localRotation = Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f);
            float bw = Rand(rng, 3f, 4.5f) * cs, bd = Rand(rng, 4.5f, 6.5f) * cs, bh = Rand(rng, 2.2f, 3f) * cs;
            Paint(Box(barn, new Vector3(0f, bh * 0.5f, 0f), new Vector3(bw, bh, bd), Vector3.zero),
                  new Color(0.62f, 0.15f, 0.10f, 1f));
            // Ridge along the barn's length (the gable mesh's ridge runs along X).
            Paint(Part(barn, Gable(), new Vector3(0f, bh, 0f), new Vector3(bd * 1.06f, bh * 0.6f, bw * 1.16f),
                       new Vector3(0f, 90f, 0f)),
                  new Color(0.44f, 0.50f, 0.58f, 1f));
            Paint(Box(barn, new Vector3(0f, bh * 0.32f, bd * 0.5f + 0.02f * cs), new Vector3(bw * 0.4f, bh * 0.64f, 0.06f * cs),
                      Vector3.zero),
                  new Color(0.86f, 0.64f, 0.28f, 1f));   // the door
        }

        int bales = rng.Next(0, 5);
        for (int k = 0; k < bales; k++)
        {
            float s = Rand(rng, 0.7f, 1f) * cs;
            Paint(Box(p, new Vector3(Rand(rng, -0.42f, 0.42f) * w, top + 0.24f * cs + s * 0.4f, Rand(rng, -0.42f, 0.42f) * d),
                      new Vector3(s, s * 0.8f, s * 1.2f), new Vector3(0f, Rand(rng, 0f, 180f), 0f)),
                  new Color(0.88f, 0.66f, 0.3f, 1f));
        }
    }

    void Paint(Transform t, Color c)
    {
        var r = t != null ? t.GetComponent<Renderer>() : null;
        if (r == null) return;
        _mpb ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(_mpb);
        _mpb.SetColor("_Color", c);
        r.SetPropertyBlock(_mpb);
    }

    // Gable roof: a triangular prism, ridge along X, base at y = 0, unit size.
    static Mesh _gable;
    static Mesh Gable()
    {
        if (_gable != null) return _gable;
        const float h = 0.5f;
        Vector3 a = new(-h, 0f, -h), b = new(h, 0f, -h), c = new(h, 0f, h), d = new(-h, 0f, h);
        Vector3 r0 = new(-h, 1f, 0f), r1 = new(h, 1f, 0f);
        var v = new List<Vector3>(); var t = new List<int>();
        void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            int s = v.Count; v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
            t.Add(s); t.Add(s + 1); t.Add(s + 2); t.Add(s); t.Add(s + 2); t.Add(s + 3);
        }
        void Tri(Vector3 p0, Vector3 p1, Vector3 p2)
        {
            int s = v.Count; v.Add(p0); v.Add(p1); v.Add(p2);
            t.Add(s); t.Add(s + 1); t.Add(s + 2);
        }
        Quad(d, c, r1, r0);   // +Z slope
        Quad(b, a, r0, r1);   // -Z slope
        Tri(a, d, r0);        // -X end
        Tri(c, b, r1);        // +X end
        _gable = new Mesh { name = "BackdropGable" };
        _gable.SetVertices(v);
        _gable.SetTriangles(t, 0);
        _gable.RecalculateNormals();
        _gable.RecalculateBounds();
        return _gable;
    }

    void Land(Transform t)
    {
        var r = t != null ? t.GetComponent<Renderer>() : null;
        if (r != null) _land.Add(r);
    }

    // ── Bloom ────────────────────────────────────────────────────────────────
    // The barren fields answer the board, with the growth the board itself uses:
    // Abundance's flowers (BloomPatch) open over the dry tufts and bare ground, and
    // the Harmony grove's trees (TreeMesh) come up where the dead ones stood — which
    // wither away under them — with shrubs among the tufts. Each plant on its own
    // delay, so the green spreads across the land rather than switching on. Once.
    public void Bloom(Color[] petals, Color leaf)
    {
        if (_bloomed || (_groundSpots.Count == 0 && _deadTrees.Count == 0 && _ripenMats.Count == 0)) return;
        _bloomed = true;
        var rng = new System.Random(_groundSpots.Count * 7919 + _deadTrees.Count);
        const float Spread = 2.6f;   // seconds for the green to cross the land
        _ripen = _ripenMats.Count > 0;

        // Flowers — sized for the distance they're seen from.
        var tops = new List<Vector3>();
        foreach (var (at, size) in _groundSpots)
        {
            int n = rng.Next(2, 5);
            for (int k = 0; k < n; k++)
                tops.Add(at + new Vector3(Rand(rng, -1f, 1f), 0f, Rand(rng, -1f, 1f)) * size);
        }
        if (tops.Count > 0)
        {
            var go = new GameObject("Bloom");
            go.transform.SetParent(transform, false);
            var patch = go.AddComponent<BloomPatch>();
            patch.bloomDuration = 0.7f;
            patch.bloomStagger  = Spread / Mathf.Max(1, tops.Count * 2);
            patch.spinSpeed     = 8f;
            patch.swaySpeed     = 1f;
            patch.swayAngleDeg  = 6f;
            patch.bobAmplitude  = 0.05f * _cs;
            patch.bobSpeed      = 0.8f;
            patch.stemHeight    = 0.5f * _cs;
            patch.Grow(tops.ToArray(), petals, new Color(1f, 0.85f, 0.35f), maxFlowersPerCell: 3,
                       flowerSizeWorld: 0.8f * _cs, scatterWorld: 0.9f * _cs, maxFlowers: GraphicsQuality.Scaled(480, 60));
        }

        // The land grows a skin of grass and flowers (GeoWorld/Backdrop's
        // _GreenAmount): flat ground and gentle slopes first, in patches, climbing
        // until only the steepest rock is bare. Each piece on its own delay.
        if (_mat != null)
        {
            _mat.SetColor("_Grass",   _growth.a  > 0f ? _growth  : new Color(leaf.r * 0.72f, leaf.g * 0.78f, leaf.b * 0.7f, 1f));
            _mat.SetColor("_Grass2",  _growth2.a > 0f ? _growth2 : Color.Lerp(leaf, new Color(0.82f, 0.9f, 0.38f), 0.4f));
            _mat.SetColor("_Flower",  petals[0]);
            _mat.SetColor("_Flower2", petals[petals.Length - 1]);
        }
        if (_growSkin)
            foreach (var r in _land)
                if (r != null) _greening.Add(new Greening { r = r, delay = Rand(rng, 0f, Spread) });

        // Real wheat on the field strips — the farm's own stalk, a field at a time,
        // rising out of the stubble.
        if (_fieldStrips.Count > 0) GrowWheat(rng, Spread);
        if (_wheatFields.Count > 0) GrowCountryWheat(rng);

        var mat = FoliageMaterial();
        if (mat == null) { _growT = 0f; return; }

        PrepareBloom(leaf);
        var treeR  = _treeR;
        var shrubR = _shrubR;
        var trees  = _bloomTrees;
        var shrubs = _bloomShrubs;
        var root = new GameObject("Growth").transform;
        root.SetParent(transform, false);

        _growthRoot = root;

        // A living tree where each dead one stood, about its height; the dead one
        // shrinks away as it comes up. With a growing-tree prefab it grows branch by
        // branch (VineEffect); without, a grove tree pops in.
        foreach (var (dead, height) in _deadTrees)
        {
            if (dead == null) continue;
            float delay = Rand(rng, 0f, Spread);
            if (_treePrefab != null)
                _vines.Add(new VineSpawn { at = dead.position, scale = height / (1.6f * _cs) * _cs, delay = delay, leaf = leaf });
            else
                Plant(root, trees[rng.Next(trees.Length)], mat, dead.position,
                      height / Mathf.Max(0.1f, treeR.height) * 0.8f, Rand(rng, 0f, 360f), delay);
            _growing.Add(new Growing { t = dead, scale = dead.localScale, delay = delay, shrink = true });
        }
        // Grass all the way up the peaks, the lushest green.
        var grassR = _grassR;
        var grass  = _bloomGrass;
        var apex = new Vector3(0.08f, 1f, -0.05f);   // Peak()'s summit
        foreach (var peak in _peaks)
        {
            if (peak == null) continue;
            for (int k = 0; k < 14; k++)
            {
                float a = Rand(rng, 0f, Mathf.PI * 2f), h = Rand(rng, 0.15f, 0.8f);
                var local = Vector3.Lerp(new Vector3(Mathf.Cos(a) * 0.45f, 0f, Mathf.Sin(a) * 0.45f), apex, h);
                Plant(root, grass[rng.Next(grass.Length)], mat, peak.TransformPoint(local),
                      _cs * Rand(rng, 1.2f, 2.2f) / Mathf.Max(0.05f, grassR.height), Rand(rng, 0f, 360f),
                      Rand(rng, 0f, Spread) + h * 0.8f);   // climbs the slope
            }
        }

        // Shrubs on some of the tufts.
        foreach (var (at, _) in _groundSpots)
        {
            if (rng.NextDouble() > 0.4) continue;
            if (_treePrefab != null)
                _vines.Add(new VineSpawn { at = at, scale = _cs * Rand(rng, 0.9f, 1.5f), delay = Rand(rng, 0f, Spread), leaf = leaf });
            else
                Plant(root, shrubs[rng.Next(shrubs.Length)], mat, at,
                      _cs * Rand(rng, 1.5f, 2.6f) / Mathf.Max(0.1f, shrubR.height), Rand(rng, 0f, 360f), Rand(rng, 0f, Spread));
        }
        _growT = 0f;
    }

    // One combined mesh per strip (a few dozen stalks each), so a whole ring of
    // farmland is a few hundred draws, not thousands of stalks. Each strip rises
    // on its own delay.
    void GrowWheat(System.Random rng, float spread)
    {
        var stalk = LevelMapController.SharedWheatMesh();
        if (stalk == null || WheatMat() == null) return;
        Color gold  = _growth.a  > 0f ? _growth  : new Color(0.84f, 0.6f, 0.18f, 1f);
        Color gold2 = _growth2.a > 0f ? _growth2 : new Color(1f, 0.8f, 0.32f, 1f);

        float spacing = 1.3f * _cs;
        var parts = new List<CombineInstance>();
        foreach (var (piece, centre, size) in _fieldStrips)
        {
            if (piece == null) continue;
            parts.Clear();
            int nx = Mathf.Max(1, Mathf.FloorToInt(size.x / spacing));
            int nz = Mathf.Max(1, Mathf.FloorToInt(size.y / spacing));
            for (int ix = 0; ix < nx; ix++)
                for (int iz = 0; iz < nz; iz++)
                {
                    if (rng.NextDouble() < 0.1) continue;
                    var at = new Vector3(((ix + 0.5f) / nx - 0.5f) * size.x + Rand(rng, -0.3f, 0.3f) * spacing, 0f,
                                         ((iz + 0.5f) / nz - 0.5f) * size.y + Rand(rng, -0.3f, 0.3f) * spacing);
                    float h = Rand(rng, 1.4f, 2.2f) * _cs;
                    parts.Add(new CombineInstance
                    {
                        mesh = stalk,
                        transform = Matrix4x4.TRS(at, Quaternion.Euler(Rand(rng, -8f, 8f), Rand(rng, 0f, 360f), Rand(rng, -8f, 8f)),
                                                  new Vector3(h * 1.3f, h, h * 1.3f)),
                    });
                }
            if (parts.Count == 0) continue;

            var field = new Mesh { name = "WheatField" };
            if (parts.Count * stalk.vertexCount > 65000) field.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            field.CombineMeshes(parts.ToArray(), true, true);
            _grownMeshes.Add(field);

            var go = new GameObject("Wheat");
            go.transform.SetParent(piece, false);
            go.transform.localPosition = centre;
            go.transform.localRotation = Quaternion.identity;
            go.AddComponent<MeshFilter>().sharedMesh = field;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial    = _wheatMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows    = false;
            Paint(go.transform, Color.Lerp(gold, gold2, (float)rng.NextDouble()));
            go.transform.localScale = new Vector3(1f, 0.02f, 1f);
            go.SetActive(false);   // until its turn — see GrowStep
            _growing.Add(new Growing { t = go.transform, scale = Vector3.one, delay = Rand(rng, 0f, spread), rise = true });
        }
    }

    void Plant(Transform root, Mesh mesh, Material mat, Vector3 at, float scale, float yaw, float delay)
    {
        var go = new GameObject("Plant");
        go.transform.SetParent(root, false);
        go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
        go.transform.localScale = Vector3.one * (scale * 0.02f);
        go.SetActive(false);   // until its turn — see GrowStep
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        _growing.Add(new Growing { t = go.transform, scale = Vector3.one * scale, delay = delay });
    }

    // The grove's recipe with the environment's leaf colour.
    static TreeMesh.Recipe Leafed(TreeMesh.Recipe r, Color leaf)
    {
        r.leafA = new Color(leaf.r * 0.8f, leaf.g * 0.8f, leaf.b * 0.8f, 1f);
        r.leafB = Color.Lerp(leaf, new Color(0.85f, 0.9f, 0.45f), 0.35f);
        return r;
    }

    // Same foliage material the Harmony grove draws its trees with.
    static Material _foliage;
    static Material FoliageMaterial()
    {
        if (_foliage != null) return _foliage;
        var sh = Shader.Find("GeoWorld/Foliage");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) return null;
        _foliage = new Material(sh) { name = "Backdrop Foliage (runtime, shared)" };
        if (_foliage.HasProperty("_Cull")) _foliage.SetFloat("_Cull", 0f);
        return _foliage;
    }

    void GrowStep()
    {
        _growT += Time.deltaTime;
        bool any = false;
        for (int i = 0; i < _growing.Count; i++)
        {
            var g = _growing[i];
            if (g.t == null) continue;
            float k = Mathf.Clamp01((_growT - g.delay) / (g.shrink ? 1.2f : 0.9f));
            if (k < 1f) any = true;
            if (g.shrink)
            {
                g.t.localScale = g.scale * (1f - k * k);
                if (k >= 1f) g.t.gameObject.SetActive(false);
                continue;
            }
            // Not started yet: stays hidden. (Drawn at zero scale it would have a
            // degenerate normal, which the lit foliage shader turns into NaN — the
            // white flashes a bloom used to set off.)
            if (k <= 0f) continue;
            if (!g.t.gameObject.activeSelf) g.t.gameObject.SetActive(true);
            // Back-out: overshoots a touch and settles, so each one pops.
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float e = Mathf.Max(0.02f, 1f + c3 * Mathf.Pow(k - 1f, 3f) + c1 * Mathf.Pow(k - 1f, 2f));
            if (g.emerge)
            {
                g.t.localPosition = g.home + Vector3.down * (g.depth * (1f - e));
                continue;
            }
            // A field rises (height only); a plant pops (all three).
            g.t.localScale = g.rise ? new Vector3(g.scale.x, g.scale.y * e, g.scale.z) : g.scale * e;
        }
        if (_greening.Count > 0)
        {
            _mpb ??= new MaterialPropertyBlock();
            foreach (var g in _greening)
            {
                if (g.r == null) continue;
                float k = Mathf.Clamp01((_growT - g.delay) / 2.4f);
                if (k < 1f) any = true;
                g.r.GetPropertyBlock(_mpb);
                _mpb.SetFloat("_GreenAmount", Mathf.SmoothStep(0f, 1f, k));
                g.r.SetPropertyBlock(_mpb);
            }
        }
        foreach (var v in _vines)
        {
            if (v.done) continue;
            any = true;
            if (_growT < v.delay) continue;
            v.done = true;
            GrowVine(v);
        }
        if (_ripen)
        {
            foreach (var rm in _ripenMats) if (rm != null) rm.SetFloat("_RipenT", _growT);
            if (_growT < _ripenEnd) any = true;
        }
        if (!any) { _growT = -1f; _growing.Clear(); _greening.Clear(); _vines.Clear(); _ripen = false; }
    }

    // One growing tree (the Harmony vine prefab), sized for the backdrop. Its trunk
    // and every fork grow node by node; leaves and blossoms open as they go.
    void GrowVine(VineSpawn v)
    {
        var go = Instantiate(_treePrefab, v.at, Quaternion.identity, _growthRoot != null ? _growthRoot : transform);
        if (!go.TryGetComponent<VineEffect>(out var fx)) { Destroy(go); return; }
        fx.worldScale   = Mathf.Max(0.1f, v.scale);
        fx.castShadows  = false;
        fx.leafColor    = v.leaf;
        fx.leafTipColor = Color.Lerp(v.leaf, new Color(0.85f, 0.95f, 0.5f), 0.4f);
        fx.GrowTree(new Color(0.40f, 0.29f, 0.2f), new Color(0.55f, 0.42f, 0.28f), Vector3.up, _treePrefab);
    }

    // ── Horizon glow ─────────────────────────────────────────────────────────
    // A big soft additive disc hung far out in the direction the light comes from,
    // turned to face the camera: the bloom of a low sun, which the scenery stands
    // in front of (it is drawn after it, so the silhouettes cut it).
    public static GameObject BuildSunGlow(Transform parent, LevelEnvironment env, Vector3 centre,
                                          Vector3 towardSun, float cs)
    {
        var sh = Shader.Find("GeoWorld/SoftDot");
        if (sh == null) return null;
        var mat = new Material(sh) { name = "SunGlow (runtime)" };
        mat.SetColor("_Color", env.sunGlowColor);
        mat.SetFloat("_Softness", 1f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);

        var go = new GameObject("SunGlow");
        go.transform.SetParent(parent, false);
        Vector3 dir = towardSun.sqrMagnitude > 1e-4f ? towardSun.normalized : Vector3.forward;
        go.transform.position   = centre + dir * (env.sunGlowDistance * cs);
        go.transform.localScale = Vector3.one * (env.sunGlowSize * cs);
        go.AddComponent<MeshFilter>().sharedMesh = Quad();
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        go.AddComponent<FaceCamera>().owned = mat;
        return go;
    }

    static Mesh _quad;
    static Mesh Quad()
    {
        if (_quad != null) return _quad;
        _quad = new Mesh { name = "SunGlowQuad" };
        _quad.SetVertices(new List<Vector3> { new(-0.5f, -0.5f, 0f), new(0.5f, -0.5f, 0f), new(0.5f, 0.5f, 0f), new(-0.5f, 0.5f, 0f) });
        _quad.SetUVs(0, new List<Vector2> { new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f) });
        _quad.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
        _quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
        _quad.RecalculateBounds();
        return _quad;
    }

    class FaceCamera : MonoBehaviour
    {
        public Material owned;
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }
        void OnDestroy() { if (owned != null) Destroy(owned); }
    }

    void Update()
    {
        if (_growT >= 0f) GrowStep();
        // Windmills stand still over the dead land and start turning once it blooms.
        _sailSpeed = Mathf.MoveTowards(_sailSpeed, _bloomed ? 40f : 0f, 12f * Time.deltaTime);
        if (_sailSpeed > 0f)
            foreach (var s in _sails)
                if (s != null) s.Rotate(Vector3.forward, _sailSpeed * Time.deltaTime, Space.Self);
        float t = Time.time * 0.35f;
        foreach (var b in _bob)
        {
            if (b.t == null) continue;
            var lp = b.t.localPosition;
            lp.y = b.baseY + Mathf.Sin(t + b.phase) * b.amp;
            b.t.localPosition = lp;
        }
    }

    void OnDestroy()
    {
        LoadingScreen.Release(this);
        if (_mat != null) Destroy(_mat);
        if (_wheatMat != null) Destroy(_wheatMat);
        foreach (var m in _ownedMats) if (m != null) Destroy(m);
        foreach (var m in _grownMeshes) if (m != null) Destroy(m);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    Transform Box(Transform parent, Vector3 pos, Vector3 size, Vector3 euler) =>
        Part(parent, Cube(), pos, size, euler);

    Transform Part(Transform parent, Mesh mesh, Vector3 pos, Vector3 size, Vector3 euler)
    {
        var go = new GameObject("Part");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale    = size;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial       = _mat;
        r.shadowCastingMode    = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows       = false;
        r.lightProbeUsage      = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return go.transform;
    }

    static float Rand(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

    static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

    // Gives a vertex-coloured mesh its ripe colour and timing (TEXCOORD2) and puts
    // its material on the bloom's clock.
    void Ripens(Mesh m, Color[] ripe, float[] at, Material mat)
    {
        var uv = new List<Vector4>(ripe.Length);
        for (int i = 0; i < ripe.Length; i++)
        {
            uv.Add(new Vector4(ripe[i].r, ripe[i].g, ripe[i].b, at[i]));
            _ripenEnd = Mathf.Max(_ripenEnd, at[i] + 1.4f);
        }
        m.SetUVs(2, uv);
        mat.SetFloat("_RipenT", -1f);
        if (!_ripenMats.Contains(mat)) _ripenMats.Add(mat);
    }

    // The bloom's trees, shrubs and grass. Building them is the slow part of a
    // bloom, so it happens while the level loads (see Build); Bloom only rebuilds
    // them if it is asked for a different leaf colour.
    void PrepareBloom(Color leaf)
    {
        if (_bloomTrees != null && _bloomLeaf == leaf) return;
        _bloomLeaf = leaf;
        var rng = new System.Random(7919);
        _treeR  = Leafed(TreeMesh.Recipe.Tree(),  leaf);
        _shrubR = Leafed(TreeMesh.Recipe.Shrub(), leaf);
        _grassR = Leafed(TreeMesh.Recipe.Tuft(), Color.Lerp(leaf, new Color(0.55f, 0.85f, 0.3f), 0.4f));
        _bloomTrees  = new Mesh[3];
        _bloomShrubs = new Mesh[3];
        _bloomGrass  = new Mesh[3];
        for (int v = 0; v < 3; v++)
        {
            _grownMeshes.Add(_bloomTrees[v]  = TreeMesh.Build(_treeR,  rng.Next()));
            _grownMeshes.Add(_bloomShrubs[v] = TreeMesh.Build(_shrubR, rng.Next()));
            _grownMeshes.Add(_bloomGrass[v]  = TreeMesh.Build(_grassR, rng.Next()));
        }
    }

    Material WheatMat()
    {
        if (_wheatMat == null && _mat != null)
        {
            _wheatMat = new Material(_mat) { name = "BackdropWheat (runtime)" };
            _wheatMat.SetFloat("_Cull", 0f);          // the stalk is single-sided cards
            _wheatMat.SetFloat("_GreenAmount", 0f);
        }
        return _wheatMat;
    }

    static Mesh Cube()
    {
        if (_cube != null) return _cube;
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _cube = tmp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(tmp);
        return _cube;
    }

    // A mountain: six-sided base (radius 0.5) at y = 0 rising to an off-centre
    // summit at y = 1, the base ring a little irregular. Flat-shaded.
    static Mesh _peak;
    static Mesh Peak()
    {
        if (_peak != null) return _peak;
        const int n = 6;
        float[] rad = { 0.50f, 0.42f, 0.48f, 0.38f, 0.50f, 0.44f };
        var ring = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            float a = i / (float)n * Mathf.PI * 2f;
            ring[i] = new Vector3(Mathf.Cos(a) * rad[i], 0f, Mathf.Sin(a) * rad[i]);
        }
        var apex = new Vector3(0.08f, 1f, -0.05f);
        var v = new List<Vector3>(); var tri = new List<int>();
        for (int i = 0; i < n; i++)
        {
            // Clockwise seen from outside (Unity's front face).
            int s0 = v.Count;
            v.Add(ring[(i + 1) % n]); v.Add(ring[i]); v.Add(apex);
            tri.Add(s0); tri.Add(s0 + 1); tri.Add(s0 + 2);
        }
        _peak = new Mesh { name = "BackdropPeak" };
        _peak.SetVertices(v);
        _peak.SetTriangles(tri, 0);
        _peak.RecalculateNormals();
        _peak.RecalculateBounds();
        return _peak;
    }

    // Unit square top at y = 0 (x,z in ±0.5) tapering to a point at y = -1; flat-shaded.
    static Mesh Spike()
    {
        if (_spike != null) return _spike;
        var a = new Vector3(-0.5f, 0f, -0.5f); var b = new Vector3(0.5f, 0f, -0.5f);
        var c = new Vector3(0.5f, 0f,  0.5f);  var d = new Vector3(-0.5f, 0f, 0.5f);
        var tip = new Vector3(0f, -1f, 0f);
        var v = new List<Vector3>(); var tri = new List<int>();
        void Tri(Vector3 p0, Vector3 p1, Vector3 p2)
        {
            int i = v.Count; v.Add(p0); v.Add(p1); v.Add(p2);
            tri.Add(i); tri.Add(i + 1); tri.Add(i + 2);
        }
        // Sides, wound clockwise seen from outside (Unity's front face).
        Tri(a, b, tip); Tri(b, c, tip); Tri(c, d, tip); Tri(d, a, tip);
        // Top, facing up (hidden under the slab, but closes the shape).
        Tri(a, d, c); Tri(a, c, b);
        _spike = new Mesh { name = "BackdropSpike" };
        _spike.SetVertices(v);
        _spike.SetTriangles(tri, 0);
        _spike.RecalculateNormals();
        _spike.RecalculateBounds();
        return _spike;
    }
}
