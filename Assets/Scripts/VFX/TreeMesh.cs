using System.Collections.Generic;
using UnityEngine;

// Builds one tree, shrub or tuft as a SINGLE combined mesh — branches as tubes,
// leaves as cards, all in one vertex buffer, vertex-coloured for GeoWorld/Foliage.
//
// Deliberately different from VineEffect, which gives every branch its own renderer
// because every branch is animating independently. Scenery does not animate, so
// there is no reason to pay for that: one mesh per plant means a wood of sixty
// plants is sixty draws instead of several thousand.
//
// And because the colour is in the vertices, every plant in the scene can share ONE
// material — so the whole wood batches, and no MaterialPropertyBlock has to exist to
// vary colour (an MPB would disable the SRP Batcher for every renderer it touched).
// Shape varies per mesh; colour varies per mesh; the material never changes.
public static class TreeMesh
{
    [System.Serializable]
    public struct Recipe
    {
        [Tooltip("Trunk length before any splitting, in world units.")]
        public float height;
        [Tooltip("Trunk radius at the ground.")]
        public float trunkRadius;

        [Tooltip("How many times the plant splits. Each level multiplies branch count by `splits`, so 4 levels x 3 splits is already 81 tips — this is the expensive number.")]
        [Range(1, 6)] public int levels;
        [Range(2, 4)] public int splits;

        [Tooltip("Fraction of its parent's length each branch keeps. High = a reaching, open plant; low = a compact bush.")]
        [Range(0.4f, 0.95f)] public float keep;

        [Tooltip("How far branches swing off their parent, in degrees.")]
        [Range(10f, 80f)] public float spreadDeg;

        [Tooltip("Downward bend accumulated along each branch. 0 = reaching up, 0.4 = weeping.")]
        [Range(0f, 0.8f)] public float droop;

        [Tooltip("Sideways wander per node. Keep low — this world is drawn clean.")]
        [Range(0f, 0.4f)] public float wobble;

        [Range(3, 8)] public int radial;
        [Range(2, 8)] public int nodesPerBranch;

        public Color bark;
        public Color leafA, leafB;

        [Tooltip("Leaf card size. 0 = a bare winter plant, which is often the better silhouette.")]
        public float leafSize;
        [Range(0, 10)] public int leavesPerTip;

        [Tooltip("Also hang leaves off the second-to-last level, so the canopy has depth instead of being a shell of dots at the tips.")]
        public bool innerLeaves;

        [Tooltip("Buttress roots flaring out of the base. Only an old tree has them, and they are most of why one reads as old.")]
        [Range(0, 10)] public int rootFlare;

        [Tooltip("How far the trunk leans off vertical at its base, in degrees. It straightens as it climbs, so the trunk curves up toward the light rather than tilting like a pole.")]
        [Range(0f, 35f)] public float trunkLean;

        [Tooltip("Sideways S-bend along the trunk. What makes a trunk look grown rather than turned on a lathe.")]
        [Range(0f, 0.5f)] public float trunkBend;

        [Tooltip("Nodes along the trunk. A curve needs points to curve through — three nodes can only ever make a dog-leg.")]
        [Range(2, 12)] public int trunkNodes;

        [Tooltip("How far each limb arcs over its length, in degrees, in a plane of its own. A limb bends one way along its whole length; random wobble per node only ever makes it kink.")]
        [Range(0f, 60f)] public float sweepDeg;

        public static Recipe Tree() => new()
        {
            height = 3.4f, trunkRadius = 0.13f, levels = 4, splits = 3, keep = 0.74f,
            spreadDeg = 38f, droop = 0.10f, wobble = 0.10f, radial = 5, nodesPerBranch = 4,
            bark  = new Color(0.72f, 0.65f, 0.55f),
            leafA = new Color(0.36f, 0.60f, 0.29f),
            leafB = new Color(0.60f, 0.80f, 0.38f),
            leafSize = 0.42f, leavesPerTip = 5, innerLeaves = true,
            trunkLean = 7f, trunkBend = 0.10f, trunkNodes = 6, sweepDeg = 16f,
        };

        // The landmark.
        //
        // A landmark tree is NOT a big version of the others — scale alone reads as
        // the same tree standing closer to the camera, because nothing about its
        // shape has changed. What says ANCIENT is PROPORTION: a trunk far too thick
        // for its height (here about 1:5, against roughly 1:26 on an ordinary tree),
        // a crown that goes WIDE instead of tall, outer limbs that weep, and roots
        // that flare out of the ground. Read as a silhouette it is a different
        // species from everything around it, which is the whole job.
        public static Recipe Elder() => new()
        {
            height = 2.0f, trunkRadius = 0.40f, levels = 4, splits = 3, keep = 0.80f,
            spreadDeg = 70f, droop = 0.34f, wobble = 0.07f, radial = 6, nodesPerBranch = 3,
            bark  = new Color(0.70f, 0.63f, 0.53f),
            leafA = new Color(0.22f, 0.48f, 0.28f),
            leafB = new Color(0.58f, 0.82f, 0.42f),
            leafSize = 0.58f, leavesPerTip = 7, innerLeaves = true, rootFlare = 6,
            // An old tree LEANS, and bends back on itself: decades of reaching for
            // the light from one side. Its limbs arc hard.
            trunkLean = 17f, trunkBend = 0.24f, trunkNodes = 8, sweepDeg = 26f,
        };

        public static Recipe Shrub() => new()
        {
            height = 0.85f, trunkRadius = 0.06f, levels = 3, splits = 3, keep = 0.72f,
            spreadDeg = 52f, droop = 0.18f, wobble = 0.14f, radial = 4, nodesPerBranch = 3,
            bark  = new Color(0.68f, 0.62f, 0.52f),
            leafA = new Color(0.33f, 0.56f, 0.27f),
            leafB = new Color(0.56f, 0.78f, 0.36f),
            leafSize = 0.26f, leavesPerTip = 6, innerLeaves = true,
            trunkLean = 12f, trunkBend = 0.12f, trunkNodes = 3, sweepDeg = 18f,
        };

        public static Recipe Tuft() => new()
        {
            height = 0.34f, trunkRadius = 0.02f, levels = 2, splits = 4, keep = 0.8f,
            spreadDeg = 62f, droop = 0.05f, wobble = 0.08f, radial = 3, nodesPerBranch = 2,
            bark  = new Color(0.62f, 0.66f, 0.45f),
            leafA = new Color(0.40f, 0.62f, 0.30f),
            leafB = new Color(0.66f, 0.82f, 0.42f),
            leafSize = 0.16f, leavesPerTip = 3, innerLeaves = false,
            trunkLean = 8f, trunkBend = 0f, trunkNodes = 2, sweepDeg = 10f,
        };
    }

    // Working buffers. Build() runs at scene setup, never per frame, so one shared
    // set is fine and saves re-growing four lists for every plant in a wood.
    static readonly List<Vector3> _v = new();
    static readonly List<Vector3> _n = new();
    static readonly List<Color>   _c = new();
    static readonly List<int>     _t = new();
    static System.Random _rng;

    public static Mesh Build(Recipe r, int seed)
    {
        _v.Clear(); _n.Clear(); _c.Clear(); _t.Clear();
        _rng = new System.Random(seed);

        r.levels         = Mathf.Clamp(r.levels, 1, 6);
        r.splits         = Mathf.Clamp(r.splits, 2, 4);
        r.radial         = Mathf.Clamp(r.radial, 3, 8);
        r.nodesPerBranch = Mathf.Clamp(r.nodesPerBranch, 2, 8);

        Branch(r, Vector3.zero, Vector3.up, r.height, r.trunkRadius, 0);
        RootFlare(r);

        var m = new Mesh { name = "TreeMesh" };
        if (_v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(_v);
        m.SetNormals(_n);
        m.SetColors(_c);
        m.SetTriangles(_t, 0);
        m.RecalculateBounds();
        return m;
    }

    // Short, thick buttresses leaving the trunk just above the ground and driving
    // down and out. They also hide the seam where a straight trunk meets a flat
    // floor, which is the single thing that most makes a tree look placed rather
    // than grown.
    static void RootFlare(Recipe r)
    {
        if (r.rootFlare <= 0) return;

        float roll = Rand(0f, 360f);
        for (int k = 0; k < r.rootFlare; k++)
        {
            float a = roll + k * 360f / r.rootFlare + Rand(-14f, 14f);
            Vector3 outward = Quaternion.AngleAxis(a, Vector3.up) * Vector3.forward;

            float len = r.trunkRadius * Rand(1.6f, 2.6f);
            var path = new[]
            {
                Vector3.up * (r.trunkRadius * 0.9f),
                Vector3.up * (r.trunkRadius * 0.45f) + outward * (len * 0.5f),
                outward * len,
            };
            AppendTube(path, r.trunkRadius * 0.42f, r.trunkRadius * 0.16f, Mathf.Max(3, r.radial - 1),
                       Shade(r.bark, 0.92f), Shade(r.bark, 1.02f));
        }
    }

    static void Branch(Recipe r, Vector3 start, Vector3 dir, float len, float rad, int level)
    {
        int nodes = level == 0 ? Mathf.Max(r.nodesPerBranch, r.trunkNodes) : r.nodesPerBranch;
        float step = len / nodes;

        // Bark pales toward the tips. Young wood IS paler than old, and it keeps the
        // fine outer twigs from reading as a dark haze against the sky.
        Color c0 = Shade(r.bark, 1f - level * 0.04f);
        Color c1 = Shade(r.bark, 1f - (level + 1) * 0.04f + 0.06f);

        var path = new Vector3[nodes + 1];
        path[0] = start;

        Vector3 d = dir.normalized;

        if (level == 0)
        {
            // THE TRUNK.
            //
            // This used to be bent the same way as a limb — a little `down` added
            // per node — and on a trunk that does nothing at all: the trunk points
            // straight UP, adding a vertical vector to a vertical vector only
            // changes its length, and normalising throws that away. So every trunk
            // came out as a dead straight pole, with only a hair of wobble on it.
            //
            // A trunk is shaped by its own rules instead: it LEANS at the base and
            // straightens as it climbs (reaching for light from one side), with a
            // sideways S-bend through its height. The lean is strongest low down and
            // eases off toward the crown, so the trunk curves rather than tilts.
            Vector3 lean = Quaternion.AngleAxis(Rand(0f, 360f), Vector3.up) * Vector3.forward;
            Vector3 bend = Vector3.Cross(Vector3.up, lean).normalized;
            float tanLean = Mathf.Tan(r.trunkLean * Mathf.Deg2Rad);
            float phase   = Rand(0f, Mathf.PI);

            for (int i = 1; i <= nodes; i++)
            {
                float t = i / (float)nodes;
                d = (Vector3.up
                     + lean * (tanLean * (1f - 0.7f * t))
                     + bend * (Mathf.Sin(t * Mathf.PI * 1.6f + phase) * r.trunkBend)
                     + new Vector3(Rand(-1f, 1f), 0f, Rand(-1f, 1f)) * (r.wobble * 0.5f / nodes)).normalized;
                path[i] = path[i - 1] + d * step;
            }
        }
        else
        {
            // A LIMB: droop, plus a sweep in a plane of its own. Each limb picks one
            // bend axis and bends steadily around it, so it arcs; the per-node
            // wobble is kept small, because random direction changes at every node
            // only ever produce kinks.
            Vector3 axis  = Quaternion.AngleAxis(Rand(0f, 360f), d) * Perp(d);
            float   sweep = Rand(-1f, 1f) * r.sweepDeg / nodes;

            for (int i = 1; i <= nodes; i++)
            {
                d = Quaternion.AngleAxis(sweep, axis) * d;
                d = (d + Vector3.down * (r.droop / nodes)
                       + new Vector3(Rand(-1f, 1f), Rand(-0.3f, 0.3f), Rand(-1f, 1f)) * (r.wobble / nodes)).normalized;
                path[i] = path[i - 1] + d * step;
            }
        }

        AppendTube(path, rad, rad * Mathf.Clamp(r.keep, 0.4f, 0.95f), r.radial, c0, c1);

        Vector3 tip = path[nodes];

        if (level >= r.levels - 1)
        {
            AddLeaves(r, tip, d, 1f);
            return;
        }

        if (r.innerLeaves && level == r.levels - 2) AddLeaves(r, tip, d, 0.75f);

        // Children fan evenly around the parent, with the roll offset per branch, so
        // the plant fills space instead of splitting in one plane every time.
        float roll = Rand(0f, 360f);
        for (int k = 0; k < r.splits; k++)
        {
            Vector3 axis = Perp(d);
            Vector3 kid  = Quaternion.AngleAxis(roll + k * 360f / r.splits, d)
                         * Quaternion.AngleAxis(r.spreadDeg + Rand(-8f, 8f), axis) * d;

            Branch(r, tip, kid.normalized,
                   len * r.keep * Rand(0.88f, 1.12f),
                   rad * r.keep, level + 1);
        }
    }

    // Sweep a ring along the path with a rotation-minimising frame — each ring's
    // reference vector is the previous one projected back perpendicular to the new
    // tangent. Building the frame from world up instead would spin the tube wherever
    // a branch passes near vertical, which on a tree is the trunk.
    static void AppendTube(Vector3[] path, float r0, float r1, int radial, Color c0, Color c1)
    {
        int n = path.Length;
        if (n < 2) return;

        int baseIndex = _v.Count;

        Vector3 seed = (path[1] - path[0]).normalized;
        Vector3 refN = Vector3.Cross(seed, Vector3.up);
        if (refN.sqrMagnitude < 1e-6f) refN = Vector3.Cross(seed, Vector3.right);
        refN.Normalize();

        for (int i = 0; i < n; i++)
        {
            Vector3 tan = i == 0     ? path[1] - path[0]
                        : i == n - 1 ? path[n - 1] - path[n - 2]
                                     : path[i + 1] - path[i - 1];
            if (tan.sqrMagnitude < 1e-10f) tan = seed;
            tan.Normalize();

            refN -= tan * Vector3.Dot(refN, tan);
            if (refN.sqrMagnitude < 1e-8f) refN = Perp(tan);
            refN.Normalize();
            Vector3 bin = Vector3.Cross(tan, refN);

            float f = i / (float)(n - 1);
            float rad = Mathf.Lerp(r0, r1, f);
            Color col = Color.Lerp(c0, c1, f);

            for (int k = 0; k < radial; k++)
            {
                float a = k / (float)radial * Mathf.PI * 2f;
                Vector3 outward = refN * Mathf.Cos(a) + bin * Mathf.Sin(a);
                _v.Add(path[i] + outward * rad);
                _n.Add(outward);
                _c.Add(col);
            }
        }

        for (int i = 0; i < n - 1; i++)
            for (int k = 0; k < radial; k++)
            {
                int a = baseIndex + i * radial + k;
                int b = baseIndex + i * radial + (k + 1) % radial;
                int c = baseIndex + (i + 1) * radial + k;
                int d = baseIndex + (i + 1) * radial + (k + 1) % radial;
                _t.Add(a); _t.Add(c); _t.Add(b);
                _t.Add(b); _t.Add(c); _t.Add(d);
            }
    }

    static void AddLeaves(Recipe r, Vector3 at, Vector3 dir, float scale)
    {
        if (r.leafSize <= 0.001f || r.leavesPerTip <= 0) return;

        for (int k = 0; k < r.leavesPerTip; k++)
        {
            Vector3 outDir = (Quaternion.AngleAxis(k * 360f / r.leavesPerTip + Rand(-25f, 25f), dir)
                              * Perp(dir) + dir * 0.45f).normalized;

            Vector3 up   = outDir;
            Vector3 fwd  = Perp(up);
            Vector3 side = Vector3.Cross(up, fwd).normalized;

            float size = r.leafSize * scale * Rand(0.75f, 1.25f);
            Vector3 o = at + outDir * (size * 0.35f) + Random(r.leafSize * 0.25f);

            Color col = Color.Lerp(r.leafA, r.leafB, Rand(0f, 1f));
            AppendLeaf(o, up * size, side * (size * 0.42f), fwd, col);
        }
    }

    // One leaf card: a diamond, base to tip. Given a flat normal rather than a
    // recalculated one — the Foliage shader flips it on back faces, so a single
    // quad reads as a leaf from either side instead of going black when the light
    // is behind it.
    static void AppendLeaf(Vector3 o, Vector3 up, Vector3 side, Vector3 normal, Color col)
    {
        int b = _v.Count;
        _v.Add(o);
        _v.Add(o + up * 0.5f - side);
        _v.Add(o + up);
        _v.Add(o + up * 0.5f + side);

        Color mid = Color.Lerp(col, Color.Lerp(col, Color.white, 0.18f), 0.5f);
        _c.Add(Shade(col, 0.85f)); _c.Add(mid); _c.Add(Color.Lerp(col, Color.white, 0.12f)); _c.Add(mid);

        for (int i = 0; i < 4; i++) _n.Add(normal);

        _t.Add(b); _t.Add(b + 1); _t.Add(b + 2);
        _t.Add(b); _t.Add(b + 2); _t.Add(b + 3);
    }

    static Vector3 Perp(Vector3 v)
    {
        Vector3 p = Vector3.Cross(v, Vector3.up);
        if (p.sqrMagnitude < 1e-4f) p = Vector3.Cross(v, Vector3.right);
        return p.normalized;
    }

    static Color Shade(Color c, float k) =>
        new(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), 1f);

    static float Rand(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

    static Vector3 Random(float s) => new(Rand(-s, s), Rand(-s, s), Rand(-s, s));
}
