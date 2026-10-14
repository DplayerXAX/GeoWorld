using System.Collections.Generic;
using UnityEngine;

// "The Watcher" — the 1-5 boss, modelled. Built entirely in code from flat-shaded
// procedural meshes (no art asset), in the house constructivist look: a few hard
// shapes in violet, ink, cream, gold and signal red, shaded by GeoWorld/SilkscreenFlat
// so every facet reads as a printed plane.
//
// It is meant to be the opposite of an ordinary enemy — those are a handful of tumbling
// primitives; this is an APPARATUS, many parts moving at different rates round one
// watching eye:
//
//   core      a lantern of six radial fins round a counter-turning red gem, capped top
//             and bottom by gold spires
//   rings     three faceted gimbal rings at crossed tilts, each turning at its own
//             rate, carrying beads
//   crown     eight leaning spires and four lances, turning against the rings
//   wings     four angular plates that breathe, and spread when it charges
//   eye       a hexagonal socket, a red lens with a slit pupil, two lids; the whole
//             assembly aims (BossController turns it) and the beam leaves from the lens
//   legs      four jointed struts that hang to the board it perches over, ending in
//             gold foot plates a hair above the block
//   plumb     a hanging chain of diminishing gems under the core
//   orbit     ten detached pieces — cubes, spikes, plates — on tilted orbits
//
// Tick(dt, charge): charge 0..1 is how close it is to firing. Everything quickens, the
// lids open wide, the wings spread, the lens and gem go white-hot. Pulse() is the
// shot itself: a flash and a recoil.
//
// Meshes are built once and shared (they are tiny); the dissolve effect on death copies
// them, so they must outlive the boss.
public class WatcherModel : MonoBehaviour
{
    public Transform Eye    { get; private set; }   // aimed by the boss; local +z is where it looks
    public Transform Muzzle { get; private set; }   // the lens — where the beam leaves from

    // On: the model advances itself every frame (a walker has no controller calling
    // Tick). Off (default): the owner drives it, as BossController does with the
    // charge of its laser.
    public bool autoTick;

    void Update()
    {
        if (autoTick) Tick(Time.deltaTime, 0f);
    }

    // ── Build ────────────────────────────────────────────────────────────────

    // `size`: world size of the body (the model is designed in units of one body size).
    // `legReach`: how far below the core the feet hang, in world units.
    public static WatcherModel Build(Transform parent, float size, float legReach, BossMechanicConfig cfg)
    {
        var sh = Shader.Find("GeoWorld/SilkscreenFlat") ?? Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null || parent == null) return null;

        var go = new GameObject("WatcherModel");
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one * size;

        var m = go.AddComponent<WatcherModel>();
        m._mat = new Material(sh) { name = "Watcher (runtime)" };

        // The config's three colours, and the house accents round them.
        Color body = cfg != null ? cfg.bodyColor : new Color(0.14f, 0.06f, 0.2f);
        Color ring = cfg != null ? cfg.ringColor : new Color(0.62f, 0.24f, 0.92f);
        Color eye  = cfg != null ? cfg.eyeColor  : new Color(1f, 0.25f, 0.3f);
        m._ink    = Color.Lerp(body, Color.black, 0.35f);
        m._body   = body;
        m._violet = ring;
        m._paper  = Color.Lerp(GeoPalette.Paper, ring, 0.10f);
        m._gold   = GeoPalette.Gold;
        m._signal = eye;

        m.Construct(Mathf.Max(0.3f, legReach / Mathf.Max(0.01f, size)));
        return m;
    }

    void OnDestroy()
    {
        if (_mat != null) Destroy(_mat);
    }

    Material _mat;
    Color _ink, _body, _violet, _paper, _gold, _signal;

    Transform _bob, _core, _crown, _lidUp, _lidDown;
    MeshRenderer _lens, _gem;

    class Spin { public Transform t; public Vector3 axis; public float rate, boost; }
    class Flap { public Transform pivot; public Quaternion rest; public Vector3 axis; public float amp, phase, spread; }
    class Sway { public Transform pivot; public Quaternion rest; public float amp, phase; }
    readonly List<Spin> _spins = new();
    readonly List<Flap> _flaps = new();
    readonly List<Sway> _sways = new();

    float _t, _flash, _blinkIn = 3f, _blinkT;

    void Construct(float legReach)
    {
        _bob = Node("Bob", transform, Vector3.zero);
        BuildCore();
        BuildRings();
        BuildCrown();
        BuildWings();
        BuildEye();
        BuildLegs(legReach);
        BuildPlumb();
        BuildOrbit();
    }

    // ── Parts ────────────────────────────────────────────────────────────────

    void BuildCore()
    {
        _core = Node("Core", _bob, Vector3.zero);
        _spins.Add(new Spin { t = _core, axis = Vector3.up, rate = 12f, boost = 2.2f });

        // Six radial fins, each a tapered blade starting clear of the axis, so the gem
        // inside shows between them. Each leans into a spiral.
        var fin = Cached("fin", () => Plate(new[]
        {
            new Vector2(0.09f, -0.30f), new Vector2(0.20f, -0.10f),
            new Vector2(0.22f,  0.16f), new Vector2(0.09f,  0.34f),
        }, 0.014f));
        for (int i = 0; i < 6; i++)
        {
            float a = i * 60f;
            Part("Fin" + i, fin, _core, Vector3.zero, Quaternion.Euler(0f, -a + 18f, 0f), Vector3.one,
                 i % 2 == 0 ? _violet : _body);
        }

        // Caps: a gold spire up, a violet one down.
        var cap = Cached("cap", () => Pyramid(6, 0.10f, 0.22f));
        Part("CapTop", cap, _core, new Vector3(0f, 0.34f, 0f), Quaternion.identity, Vector3.one, _gold);
        Part("CapBottom", cap, _core, new Vector3(0f, -0.34f, 0f), Quaternion.Euler(180f, 0f, 0f), Vector3.one, _violet);

        // The gem at the heart, turning against the lantern.
        var gem = Cached("gem", () => Gem(4, 0.09f, 0.17f, 0.02f, 45f));
        _gem = Part("Gem", gem, _bob, Vector3.zero, Quaternion.identity, Vector3.one, _signal);
        _spins.Add(new Spin { t = _gem.transform, axis = Vector3.up, rate = -46f, boost = 3.5f });
    }

    void BuildRings()
    {
        float[] radius = { 0.42f, 0.54f, 0.64f };
        float[] width  = { 0.045f, 0.05f, 0.04f };
        float[] thick  = { 0.028f, 0.03f, 0.025f };
        float[] rate   = { 22f, -31f, 17f };
        Quaternion[] tilt = { Quaternion.Euler(68f, 0f, 0f), Quaternion.Euler(0f, 0f, -52f), Quaternion.Euler(35f, 0f, 28f) };
        Color[] col  = { _violet, _gold, _paper };
        Color[] bead = { _gold, _signal, _violet };

        var beadMesh = Cached("box", () => Plate(Rect(1f, 1f), 1f));
        for (int i = 0; i < 3; i++)
        {
            float r = radius[i], w = width[i], th = thick[i];
            var tilted = Node("RingTilt" + i, _bob, Vector3.zero);
            tilted.localRotation = tilt[i];

            var ringMesh = Cached($"ring{i}", () => Ring(r, w, th, 12));
            var ring = Part("Ring" + i, ringMesh, tilted, Vector3.zero, Quaternion.identity, Vector3.one, col[i]);
            _spins.Add(new Spin { t = ring.transform, axis = Vector3.up, rate = rate[i], boost = 3f });

            // Three beads, spaced round the ring, riding it.
            for (int b = 0; b < 3; b++)
            {
                float a = (b * 120f + i * 40f) * Mathf.Deg2Rad;
                Part("Bead" + b, beadMesh, ring.transform,
                     new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r),
                     Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f),
                     new Vector3(0.05f, 0.075f, 0.07f), bead[i]);
            }
        }
    }

    void BuildCrown()
    {
        _crown = Node("Crown", _bob, new Vector3(0f, 0.28f, 0f));
        _spins.Add(new Spin { t = _crown, axis = Vector3.up, rate = 9f, boost = 2.6f });

        // Eight spires leaning outward, tall and short in turn.
        var spire = Cached("spire", () => Pyramid(4, 0.045f, 0.34f));
        for (int i = 0; i < 8; i++)
        {
            float a = i * 45f * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var lean = Quaternion.AngleAxis(22f, Vector3.Cross(Vector3.up, dir));
            bool tall = i % 2 == 0;
            Part("Spire" + i, spire, _crown, dir * 0.26f, lean,
                 new Vector3(1f, tall ? 1f : 0.72f, 1f), tall ? _gold : _violet);
        }

        // Four thin lances up the middle.
        var lance = Cached("lance", () => Pyramid(4, 0.022f, 0.46f));
        for (int i = 0; i < 4; i++)
        {
            float a = (i * 90f + 20f) * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Part("Lance" + i, lance, _crown, dir * 0.05f + new Vector3(0f, 0.06f, 0f),
                 Quaternion.AngleAxis(8f, Vector3.Cross(Vector3.up, dir)), Vector3.one, _paper);
        }
    }

    void BuildWings()
    {
        var right = Cached("wingR", () => Plate(new[]
        {
            new Vector2(0.00f, -0.05f), new Vector2(0.30f, -0.10f), new Vector2(0.50f, 0.14f),
            new Vector2(0.26f, 0.30f),  new Vector2(0.08f, 0.12f),
        }, 0.02f));
        var left = Cached("wingL", () => Plate(new[]
        {
            new Vector2(0.00f, -0.05f), new Vector2(-0.30f, -0.10f), new Vector2(-0.50f, 0.14f),
            new Vector2(-0.26f, 0.30f), new Vector2(-0.08f, 0.12f),
        }, 0.02f));

        for (int side = 0; side < 2; side++)
        {
            bool r = side == 0;
            float sx = r ? 1f : -1f;
            // Two plates a side: a big one up and back, a smaller one tucked beneath.
            for (int k = 0; k < 2; k++)
            {
                float yaw  = (r ? 20f : -20f) + (k == 1 ? sx * 18f : 0f);
                float roll = k == 0 ? -28f : -8f;
                var pivot = Node($"Wing{side}{k}", _bob, new Vector3(sx * 0.14f, k == 0 ? 0.10f : -0.04f, -0.10f));
                var rest  = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(roll, 0f, 0f);
                pivot.localRotation = rest;
                float scale = k == 0 ? 1f : 0.66f;

                Part("Plate", r ? right : left, pivot, Vector3.zero, Quaternion.identity, Vector3.one * scale,
                     k == 0 ? _gold : _violet);
                // A cream stripe laid on it.
                Part("Stripe", r ? right : left, pivot, new Vector3(sx * 0.07f * scale, 0.04f * scale, 0.016f),
                     Quaternion.identity, Vector3.one * (scale * 0.5f), k == 0 ? _paper : _signal);

                _flaps.Add(new Flap { pivot = pivot, rest = rest, axis = new Vector3(0f, 0f, sx),
                                      amp = 5f + k * 3f, phase = side * 1.3f + k * 0.8f, spread = 14f + k * 8f });
            }
        }
    }

    void BuildEye()
    {
        Eye = Node("Eye", _bob, Vector3.zero);
        const float z = 0.30f;

        // Hexagonal socket facing +z, in cream so the red reads.
        var socket = Cached("socket", () => Ring(0.17f, 0.06f, 0.06f, 6));
        Part("Socket", socket, Eye, new Vector3(0f, 0f, z), Quaternion.Euler(90f, 0f, 0f), Vector3.one, _paper);

        // The lens, and the slit pupil across it.
        var lens = Cached("lens", () => Plate(Poly(6, 0.125f, 0f), 0.03f));
        _lens = Part("Lens", lens, Eye, new Vector3(0f, 0f, z), Quaternion.identity, Vector3.one, _signal);
        var slit = Cached("slit", () => Plate(Rect(0.022f, 0.15f), 0.012f));
        Part("Pupil", slit, Eye, new Vector3(0f, 0f, z + 0.02f), Quaternion.identity, Vector3.one, _ink);

        Muzzle = Node("Muzzle", Eye, new Vector3(0f, 0f, z + 0.06f));

        // Two lids, hinged top and bottom of the socket. Hanging closed they cover
        // part of the lens; swung forward they are open.
        var lidU = Cached("lidU", () => Plate(new[]
            { new Vector2(-0.14f, 0f), new Vector2(0.14f, 0f), new Vector2(0.10f, -0.12f), new Vector2(-0.10f, -0.12f) }, 0.02f));
        var lidD = Cached("lidD", () => Plate(new[]
            { new Vector2(-0.14f, 0f), new Vector2(0.14f, 0f), new Vector2(0.10f, 0.12f), new Vector2(-0.10f, 0.12f) }, 0.02f));
        _lidUp   = Node("LidUp",   Eye, new Vector3(0f,  0.165f, z + 0.035f));
        _lidDown = Node("LidDown", Eye, new Vector3(0f, -0.165f, z + 0.035f));
        Part("Lid", lidU, _lidUp,   Vector3.zero, Quaternion.identity, Vector3.one, _violet);
        Part("Lid", lidD, _lidDown, Vector3.zero, Quaternion.identity, Vector3.one, _violet);

        // Four small gold spikes at the socket's corners, like lashes.
        var lash = Cached("lash", () => Pyramid(3, 0.03f, 0.11f));
        for (int i = 0; i < 4; i++)
        {
            float a = (45f + i * 90f) * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            // Pyramid points +y; turn it to point along dir (in the socket plane).
            var rot = Quaternion.FromToRotation(Vector3.up, dir);
            Part("Lash" + i, lash, Eye, dir * 0.215f + new Vector3(0f, 0f, z - 0.01f), rot, Vector3.one, _gold);
        }
    }

    void BuildLegs(float reach)
    {
        var box  = Cached("box", () => Plate(Rect(1f, 1f), 1f));
        var knee = Cached("knee", () => Gem(4, 0.045f, 0.07f, 0f, 45f));
        var foot = Cached("foot", () => Plate(new[]
            { new Vector2(-0.07f, -0.045f), new Vector2(0.09f, 0f), new Vector2(-0.07f, 0.045f) }, 0.016f));

        for (int k = 0; k < 4; k++)
        {
            float a = (45f + 90f * k) * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 hip = dir * 0.13f + new Vector3(0f, -0.30f, 0f);
            Vector3 ft  = dir * 0.48f + new Vector3(0f, -reach, 0f);
            Vector3 kn  = Vector3.Lerp(hip, ft, 0.45f) + dir * 0.15f + Vector3.up * 0.06f;

            var leg = Node("Leg" + k, _bob, hip);
            Strut(box, leg, Vector3.zero, kn - hip, 0.045f, _violet);
            Strut(box, leg, kn - hip, ft - hip, 0.032f, _ink);
            Part("Knee", knee, leg, kn - hip, Quaternion.identity, Vector3.one, _gold);
            // The foot lies flat, pointing outward.
            Part("Foot", foot, leg, ft - hip + new Vector3(0f, 0.012f, 0f),
                 Quaternion.Euler(90f, -a * Mathf.Rad2Deg, 0f), Vector3.one, _gold);

            _sways.Add(new Sway { pivot = leg, rest = leg.localRotation, amp = 3f, phase = k * 1.7f });
        }
    }

    // A hanging chain of gems, big to small, under the core.
    void BuildPlumb()
    {
        var gem = Cached("plumb", () => Gem(4, 0.05f, 0.075f, 0f, 45f));
        float[] y = { -0.46f, -0.57f, -0.65f, -0.71f };
        float[] s = { 1f, 0.8f, 0.6f, 0.42f };
        for (int i = 0; i < 4; i++)
            Part("Plumb" + i, gem, _bob, new Vector3(0f, y[i], 0f), Quaternion.identity, Vector3.one * s[i],
                 i % 2 == 0 ? _gold : _violet);
    }

    // Ten detached pieces — cubes, spikes, plates — on tilted orbits.
    void BuildOrbit()
    {
        var box  = Cached("box", () => Plate(Rect(1f, 1f), 1f));
        var spike = Cached("orbSpike", () => Pyramid(3, 0.05f, 0.1f));
        var kite = Cached("orbKite", () => Plate(new[]
            { new Vector2(0f, -0.07f), new Vector2(0.05f, 0f), new Vector2(0f, 0.09f), new Vector2(-0.05f, 0f) }, 0.014f));
        Color[] col = { _gold, _paper, _signal, _violet, _gold, _paper, _violet, _signal, _gold, _paper };

        for (int i = 0; i < 10; i++)
        {
            var pivot = Node("Orbit" + i, _bob, Vector3.zero);
            pivot.localRotation = Quaternion.Euler(Mathf.Lerp(-40f, 40f, (i * 0.618f) % 1f), i * 36f, 0f);
            float dir = i % 2 == 0 ? 1f : -1f;
            _spins.Add(new Spin { t = pivot, axis = Vector3.up, rate = dir * (11f + (i % 4) * 7f), boost = 2.4f });

            float r = 0.56f + (i % 5) * 0.055f;
            Mesh mesh = i % 3 == 0 ? box : (i % 3 == 1 ? spike : kite);
            Vector3 scale = i % 3 == 0 ? new Vector3(0.07f, 0.05f, 0.07f) : Vector3.one;
            var piece = Part("Piece", mesh, pivot, new Vector3(r, ((i % 3) - 1) * 0.08f, 0f),
                             Quaternion.identity, scale, col[i]);
            _spins.Add(new Spin { t = piece.transform, axis = new Vector3(0.3f, 1f, 0.2f).normalized,
                                  rate = 50f + (i % 5) * 18f, boost = 1.5f });
        }
    }

    // ── Motion ───────────────────────────────────────────────────────────────

    // charge: 0 at rest .. 1 the instant before it fires.
    public void Tick(float dt, float charge)
    {
        float k = Mathf.Clamp01(charge), k2 = k * k;
        _t += dt * (1f + 1.2f * k);
        _flash = Mathf.MoveTowards(_flash, 0f, dt * 3.5f);

        for (int i = 0; i < _spins.Count; i++)
        {
            var s = _spins[i];
            if (s.t != null) s.t.Rotate(s.axis, s.rate * (1f + s.boost * k2 + 2.5f * _flash) * dt, Space.Self);
        }

        if (_bob != null) _bob.localPosition = new Vector3(0f, Mathf.Sin(_t * 0.9f) * 0.02f, 0f);

        // Wings breathe, and open out as it charges.
        for (int i = 0; i < _flaps.Count; i++)
        {
            var f = _flaps[i];
            if (f.pivot == null) continue;
            float ang = Mathf.Sin(_t * 0.9f + f.phase) * f.amp + k * f.spread + _flash * 10f;
            f.pivot.localRotation = f.rest * Quaternion.AngleAxis(ang, f.axis);
        }
        for (int i = 0; i < _sways.Count; i++)
        {
            var s = _sways[i];
            if (s.pivot == null) continue;
            s.pivot.localRotation = s.rest * Quaternion.Euler(0f, Mathf.Sin(_t * 0.7f + s.phase) * s.amp, 0f);
        }

        // Lids: half-lidded and watchful at rest, a slow blink now and then, wide open
        // when it is about to fire.
        _blinkIn -= dt;
        if (_blinkIn <= 0f) { _blinkIn = Random.Range(4f, 8f); _blinkT = 0.18f; }
        if (_blinkT > 0f) _blinkT = Mathf.Max(0f, _blinkT - dt);
        float open = Mathf.Lerp(0.32f, 1f, k);
        if (_blinkT > 0f) open *= 1f - Mathf.Sin((1f - _blinkT / 0.18f) * Mathf.PI);
        if (_lidUp != null)   _lidUp.localRotation   = Quaternion.Euler(Mathf.Lerp(-6f, -80f, open), 0f, 0f);
        if (_lidDown != null) _lidDown.localRotation = Quaternion.Euler(Mathf.Lerp(6f, 80f, open), 0f, 0f);

        // At rest the eye scans; while charging the boss aims it (and it is left alone).
        if (Eye != null)
        {
            if (k < 0.001f)
            {
                var want = Quaternion.Euler(Mathf.Sin(_t * 0.45f) * 10f, Mathf.Sin(_t * 0.31f) * 38f, 0f);
                Eye.localRotation = Quaternion.Slerp(Eye.localRotation, want, 1f - Mathf.Exp(-1.6f * dt));
            }
            Eye.localPosition = new Vector3(0f, 0f, -0.05f * _flash);   // the recoil
        }

        // Lens and gem heat up with the charge.
        float heat = Mathf.Clamp01(0.55f * k + 0.5f * _flash);
        if (_lens != null) MpbColor.Set(_lens, Color.Lerp(_signal, Color.white, heat));
        if (_gem  != null) MpbColor.Set(_gem,  Color.Lerp(_signal, Color.Lerp(_gold, Color.white, 0.5f), Mathf.Clamp01(k + _flash)));
    }

    // The shot: a flash through every moving part, and the eye kicks back.
    public void Pulse() => _flash = 1f;

    // ── Construction helpers ─────────────────────────────────────────────────

    Transform Node(string name, Transform parent, Vector3 pos)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = pos;
        return t;
    }

    MeshRenderer Part(string name, Mesh mesh, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, Color color)
    {
        var go = new GameObject(name);
        var t = go.transform;
        t.SetParent(parent, false);
        t.localPosition = pos;
        t.localRotation = rot;
        t.localScale    = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial    = _mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows    = false;
        MpbColor.Set(r, color);
        return r;
    }

    // A thin box from p0 to p1 (parent space).
    void Strut(Mesh box, Transform parent, Vector3 p0, Vector3 p1, float thick, Color color)
    {
        Vector3 d = p1 - p0;
        float len = d.magnitude;
        if (len < 1e-4f) return;
        Vector3 up = Mathf.Abs(Vector3.Dot(d / len, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up;
        Part("Strut", box, parent, (p0 + p1) * 0.5f, Quaternion.LookRotation(d / len, up),
             new Vector3(thick, thick, len), color);
    }

    // ── Flat-shaded meshes ───────────────────────────────────────────────────
    // Every face gets its own vertices and one normal, so each facet is a plane of
    // one tone. Winding is made right per face against a hint direction.

    static readonly Dictionary<string, Mesh> _cache = new();

    static Mesh Cached(string key, System.Func<Mesh> make)
    {
        if (_cache.TryGetValue(key, out var m) && m != null) return m;
        m = make();
        _cache[key] = m;
        return m;
    }

    sealed class MB
    {
        readonly List<Vector3> _v = new(), _n = new();
        readonly List<int> _t = new();

        // A triangle whose normal faces along `hint` (the triangle is re-wound if need be).
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 hint)
        {
            Vector3 nrm = Vector3.Cross(b - a, c - a);
            if (nrm.sqrMagnitude < 1e-12f) return;
            nrm.Normalize();
            if (Vector3.Dot(nrm, hint) < 0f) { var tmp = b; b = c; c = tmp; nrm = -nrm; }
            int i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c);
            _n.Add(nrm); _n.Add(nrm); _n.Add(nrm);
            _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 hint)
        {
            Tri(a, b, c, hint);
            Tri(a, c, d, hint);
        }

        // A face of a solid round `center`: its normal points away from it.
        public void TriOut(Vector3 a, Vector3 b, Vector3 c, Vector3 center) => Tri(a, b, c, (a + b + c) / 3f - center);

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            m.SetVertices(_v);
            m.SetNormals(_n);
            m.SetTriangles(_t, 0);
            m.RecalculateBounds();
            return m;
        }
    }

    static Vector2[] Rect(float w, float h) => new[]
    {
        new Vector2(-w * 0.5f, -h * 0.5f), new Vector2(w * 0.5f, -h * 0.5f),
        new Vector2(w * 0.5f, h * 0.5f),   new Vector2(-w * 0.5f, h * 0.5f),
    };

    static Vector2[] Poly(int n, float r, float rotDeg)
    {
        var p = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float a = (rotDeg + i * 360f / n) * Mathf.Deg2Rad;
            p[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }
        return p;
    }

    // An outline extruded `thick` along z (centred). The outline must be star-shaped
    // about its centroid (every plate here is).
    static Mesh Plate(Vector2[] p, float thick)
    {
        var mb = new MB();
        float h = thick * 0.5f;
        Vector2 c = Vector2.zero;
        foreach (var q in p) c += q;
        c /= p.Length;

        var cT = new Vector3(c.x, c.y, h);
        var cB = new Vector3(c.x, c.y, -h);
        for (int i = 0; i < p.Length; i++)
        {
            int j = (i + 1) % p.Length;
            var tI = new Vector3(p[i].x, p[i].y, h);  var tJ = new Vector3(p[j].x, p[j].y, h);
            var bI = new Vector3(p[i].x, p[i].y, -h); var bJ = new Vector3(p[j].x, p[j].y, -h);
            mb.Tri(cT, tI, tJ, Vector3.forward);
            mb.Tri(cB, bI, bJ, Vector3.back);
            Vector2 mid = (p[i] + p[j]) * 0.5f - c;
            mb.Quad(tI, tJ, bJ, bI, new Vector3(mid.x, mid.y, 0f));
        }
        return mb.ToMesh("WatcherPlate");
    }

    // A faceted band round the y axis: cross-section width (radial) × thick (vertical).
    static Mesh Ring(float radius, float width, float thick, int segs)
    {
        var mb = new MB();
        float ri = radius - width * 0.5f, ro = radius + width * 0.5f, h = thick * 0.5f;
        var up = new Vector3(0f, h, 0f);
        for (int i = 0; i < segs; i++)
        {
            float a0 = i * 2f * Mathf.PI / segs, a1 = (i + 1) * 2f * Mathf.PI / segs;
            var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
            var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            var dm = (d0 + d1).normalized;

            Vector3 oT0 = d0 * ro + up, oB0 = d0 * ro - up, iT0 = d0 * ri + up, iB0 = d0 * ri - up;
            Vector3 oT1 = d1 * ro + up, oB1 = d1 * ro - up, iT1 = d1 * ri + up, iB1 = d1 * ri - up;

            mb.Quad(oT0, oT1, oB1, oB0, dm);             // outer face
            mb.Quad(iT0, iT1, iB1, iB0, -dm);            // inner face
            mb.Quad(oT0, oT1, iT1, iT0, Vector3.up);     // top
            mb.Quad(oB0, oB1, iB1, iB0, Vector3.down);   // bottom
        }
        return mb.ToMesh("WatcherRing");
    }

    // A cone of `sides` faces: base on y = 0, apex at y = height.
    static Mesh Pyramid(int sides, float baseR, float height)
    {
        var mb = new MB();
        var apex = new Vector3(0f, height, 0f);
        var inside = new Vector3(0f, height * 0.3f, 0f);
        for (int i = 0; i < sides; i++)
        {
            float a0 = i * 2f * Mathf.PI / sides, a1 = (i + 1) * 2f * Mathf.PI / sides;
            var b0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * baseR;
            var b1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * baseR;
            mb.TriOut(apex, b0, b1, inside);
            mb.Tri(Vector3.zero, b0, b1, Vector3.down);
        }
        return mb.ToMesh("WatcherPyramid");
    }

    // A double pyramid with a twisted waist: apexes at ±apex, waist rings at ±waist,
    // the lower turned by `twistDeg` against the upper.
    static Mesh Gem(int sides, float r, float apex, float waist, float twistDeg)
    {
        var mb = new MB();
        var up = new Vector3[sides];
        var dn = new Vector3[sides];
        float step = 2f * Mathf.PI / sides, tw = twistDeg * Mathf.Deg2Rad;
        for (int i = 0; i < sides; i++)
        {
            float a = i * step;
            up[i] = new Vector3(Mathf.Cos(a) * r, waist, Mathf.Sin(a) * r);
            dn[i] = new Vector3(Mathf.Cos(a + tw) * r, -waist, Mathf.Sin(a + tw) * r);
        }
        var top = new Vector3(0f, apex, 0f);
        var bot = new Vector3(0f, -apex, 0f);
        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            mb.TriOut(top, up[i], up[j], Vector3.zero);
            mb.TriOut(bot, dn[j], dn[i], Vector3.zero);
            mb.TriOut(up[i], dn[i], up[j], Vector3.zero);
            mb.TriOut(dn[i], dn[j], up[j], Vector3.zero);
        }
        return mb.ToMesh("WatcherGem");
    }
}
