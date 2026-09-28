using System.Collections.Generic;
using UnityEngine;

// Far scenery round the play area, from the level's LevelEnvironment: cliffs,
// ruins or floating islands, standing in a ring well outside anywhere a block
// goes. No colliders — nothing here can be clicked, built on or block a ray.
// Drawn flat (GeoWorld/Backdrop) and hazed with distance, so it reads as shapes in
// the air behind the board rather than as detail to look at; the height fog and
// far haze, when on, swallow its feet.
public class EnvironmentBackdrop : MonoBehaviour
{
    static Mesh _cube, _spike;

    Material _mat;
    readonly List<(Transform t, float baseY, float amp, float phase)> _bob = new();

    public static EnvironmentBackdrop Build(Transform parent, LevelEnvironment env, Vector3 centre, float floorY, float cs)
    {
        var sh = Shader.Find("GeoWorld/Backdrop");
        if (sh == null) { Debug.LogWarning("[Backdrop] GeoWorld/Backdrop shader not found — no scenery."); return null; }

        var go = new GameObject("Backdrop");
        go.transform.SetParent(parent, false);
        var b = go.AddComponent<EnvironmentBackdrop>();

        b._mat = new Material(sh) { name = "Backdrop (runtime)" };
        b._mat.SetColor("_Color",     env.backdropColor);
        b._mat.SetColor("_HazeColor", env.backdropHaze);
        b._mat.SetFloat("_HazeStart", env.backdropDistance.x * cs * 0.5f);
        b._mat.SetFloat("_HazeRange", env.backdropDistance.y * cs * 1.2f);
        b._mat.SetFloat("_HazeMax",   0.8f);
        b._mat.SetFloat("_BaseY",     floorY - 30f * cs);
        b._mat.SetFloat("_BaseRange", 26f * cs);

        var rng = new System.Random(env.backdropSeed);
        int n = env.backdropCount;
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

            switch (env.backdrop)
            {
                case LevelEnvironment.Backdrop.Cliffs:          b.Cliff(piece, rng, cs);  break;
                case LevelEnvironment.Backdrop.Ruins:           b.Ruin(piece, rng, cs);   break;
                case LevelEnvironment.Backdrop.FloatingIslands: b.Island(piece, rng, cs); break;
            }
        }
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

    void Update()
    {
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
        if (_mat != null) Destroy(_mat);
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

    static Mesh Cube()
    {
        if (_cube != null) return _cube;
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _cube = tmp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(tmp);
        return _cube;
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
