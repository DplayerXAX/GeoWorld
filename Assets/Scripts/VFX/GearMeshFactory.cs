using System.Collections.Generic;
using UnityEngine;

// The one gear in the game. Shared by the 1-2 Order workshop decor and by
// OrderArchitectVisualizer's rigs, because they're the same object seen at two
// scales — Order's whole identity is precision drivetrain, and having the
// synergy effect and the monument to it use different cogs undercut that.
//
// Unit OUTER radius 1, lying flat in the XZ plane, thickness along Y. Callers
// scale it; nothing here knows about cell sizes.
//
// Every quad carries its own four vertices, so RecalculateNormals produces flat
// per-face normals — a gear wants hard facets, and shared vertices would smooth
// the teeth into a blob.
public static class GearMeshFactory
{
    // Cached per tooth count for the process. Never destroyed: they're shared by
    // every rig on screen, and a rig retiring must not pull the mesh out from
    // under the others.
    static readonly Dictionary<int, Mesh> _cache = new();

    // Radii as fractions of the outer radius. The rim/hub/spoke breakdown is what
    // reads as "machined" rather than "a cog-shaped disc" — a solid plate with
    // teeth is legible but it isn't precise.
    const float RTip   = 1.00f;   // tooth tip
    const float RRim   = 0.80f;   // tooth root / rim outer
    const float RBore  = 0.60f;   // rim inner
    const float RHub   = 0.24f;   // hub outer
    const float HalfT  = 0.12f;   // half thickness
    const int   Spokes = 4;

    // Baked copies live under Resources/<BakedFolder>/Gear_<teeth>. Present = we
    // load; absent = we generate exactly as before. The bake is an optimisation,
    // never a dependency — see DecorMeshBaker.
    public const string BakedFolder = LevelMapController.BakedMeshFolder;

    // Set by the baker so it gets a fresh build rather than loading back the asset
    // it's about to overwrite.
    public static bool SkipBakedLoad;

    public static Mesh Get(int teeth)
    {
        teeth = Mathf.Clamp(teeth, 6, 24);
        if (_cache.TryGetValue(teeth, out var cached) && cached != null) return cached;

        if (!SkipBakedLoad)
        {
            var baked = Resources.Load<Mesh>($"{BakedFolder}/Gear_{teeth}");
            if (baked != null) { _cache[teeth] = baked; return baked; }
        }

        var v = new List<Vector3>();
        var t = new List<int>();

        BuildRim(v, t, teeth);
        BuildTeeth(v, t, teeth);
        BuildHub(v, t);
        BuildSpokes(v, t);

        var m = new Mesh { name = $"Gear_{teeth}" };
        m.SetVertices(v);
        m.SetTriangles(t, 0);
        m.RecalculateNormals();
        // Generous fixed bounds: these rigs get scaled from zero as they grow in,
        // and a tight bound on a near-zero scale flickers out of the frustum.
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);

        _cache[teeth] = m;
        return m;
    }

    // The annulus between hub and teeth: top, bottom, and both walls.
    static void BuildRim(List<Vector3> v, List<int> t, int teeth)
    {
        int seg = teeth * 2;
        for (int i = 0; i < seg; i++)
        {
            float a0 = i / (float)seg * Mathf.PI * 2f;
            float a1 = (i + 1) / (float)seg * Mathf.PI * 2f;

            Vector3 o0 = P(a0, RRim,  HalfT), o1 = P(a1, RRim,  HalfT);
            Vector3 i0 = P(a0, RBore, HalfT), i1 = P(a1, RBore, HalfT);
            Vector3 O0 = P(a0, RRim, -HalfT), O1 = P(a1, RRim, -HalfT);
            Vector3 I0 = P(a0, RBore,-HalfT), I1 = P(a1, RBore,-HalfT);

            Quad(v, t, i0, i1, o1, o0);   // top
            Quad(v, t, O0, O1, I1, I0);   // bottom
            Quad(v, t, O0, o0, o1, O1);   // outer wall
            Quad(v, t, I1, i1, i0, I0);   // inner wall
        }
    }

    // Square teeth straddling the rim. Angular width is a fixed fraction of the
    // pitch, so the tooth-to-gap ratio holds at every tooth count and two gears
    // of different sizes still look like they'd mesh.
    static void BuildTeeth(List<Vector3> v, List<int> t, int teeth)
    {
        float pitch = Mathf.PI * 2f / teeth;
        float halfW = pitch * 0.30f;

        for (int k = 0; k < teeth; k++)
        {
            float c = k * pitch;
            float a0 = c - halfW, a1 = c + halfW;

            Vector3 r0 = P(a0, RRim,  HalfT), r1 = P(a1, RRim,  HalfT);
            Vector3 p0 = P(a0, RTip,  HalfT), p1 = P(a1, RTip,  HalfT);
            Vector3 R0 = P(a0, RRim, -HalfT), R1 = P(a1, RRim, -HalfT);
            Vector3 P0 = P(a0, RTip, -HalfT), P1 = P(a1, RTip, -HalfT);

            Quad(v, t, r0, r1, p1, p0);   // top
            Quad(v, t, P0, P1, R1, R0);   // bottom
            Quad(v, t, P0, p0, p1, P1);   // tip face
            Quad(v, t, R0, r0, p0, P0);   // trailing flank
            Quad(v, t, P1, p1, r1, R1);   // leading flank
        }
    }

    // Solid hub — without it you see straight through the middle of the gear to
    // whatever's behind it, which instantly reads as a hole in the model.
    static void BuildHub(List<Vector3> v, List<int> t)
    {
        const int seg = 10;
        for (int i = 0; i < seg; i++)
        {
            float a0 = i / (float)seg * Mathf.PI * 2f;
            float a1 = (i + 1) / (float)seg * Mathf.PI * 2f;

            Vector3 u0 = P(a0, RHub,  HalfT), u1 = P(a1, RHub,  HalfT);
            Vector3 d0 = P(a0, RHub, -HalfT), d1 = P(a1, RHub, -HalfT);

            Tri(v, t, new Vector3(0f,  HalfT, 0f), u1, u0);
            Tri(v, t, new Vector3(0f, -HalfT, 0f), d0, d1);
            Quad(v, t, d0, u0, u1, d1);   // hub wall
        }
    }

    static void BuildSpokes(List<Vector3> v, List<int> t)
    {
        const float halfW = 0.09f;   // radians
        for (int s = 0; s < Spokes; s++)
        {
            float c  = s / (float)Spokes * Mathf.PI * 2f;
            float a0 = c - halfW, a1 = c + halfW;

            Vector3 h0 = P(a0, RHub,   HalfT), h1 = P(a1, RHub,   HalfT);
            Vector3 r0 = P(a0, RBore,  HalfT), r1 = P(a1, RBore,  HalfT);
            Vector3 H0 = P(a0, RHub,  -HalfT), H1 = P(a1, RHub,  -HalfT);
            Vector3 R0 = P(a0, RBore, -HalfT), R1 = P(a1, RBore, -HalfT);

            Quad(v, t, h0, h1, r1, r0);   // top
            Quad(v, t, R0, R1, H1, H0);   // bottom
            Quad(v, t, H0, h0, r0, R0);   // side
            Quad(v, t, R1, r1, h1, H1);   // side
        }
    }

#if UNITY_EDITOR
    // Baker hook: force a fresh procedural build of every tooth count the game
    // actually asks for, bypassing both caches.
    public static (string name, Mesh mesh)[] BuildAllForBake()
    {
        SkipBakedLoad = true;
        _cache.Clear();
        var made = new List<(string, Mesh)>();
        // 10 is the decor workshop's cog; 12 is OrderRig's default gearTeeth. Both
        // are baked so neither system pays for generation at runtime.
        foreach (int teeth in new[] { 10, 12 })
            made.Add(($"Gear_{teeth}", Get(teeth)));
        SkipBakedLoad = false;
        _cache.Clear();   // so play mode picks up the freshly saved assets
        return made.ToArray();
    }
#endif

    static Vector3 P(float angle, float radius, float y) =>
        new(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);

    static void Quad(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int s = v.Count;
        v.Add(a); v.Add(b); v.Add(c); v.Add(d);
        t.Add(s); t.Add(s + 1); t.Add(s + 2);
        t.Add(s); t.Add(s + 2); t.Add(s + 3);
    }

    static void Tri(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c)
    {
        int s = v.Count;
        v.Add(a); v.Add(b); v.Add(c);
        t.Add(s); t.Add(s + 1); t.Add(s + 2);
    }
}
