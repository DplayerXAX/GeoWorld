using System.Collections.Generic;
using UnityEngine;

// Detail for the map's middle, the Order workshop and the observatory, and the
// pieces every region shares. Those shared pieces are: placement on an organic
// outline, a few more primitive meshes, and the small moving parts that keep a
// plot alive (carts, belts, smoke, a swinging hook, bobbing boats, breathing
// light).
//
// The farm and the grove each carry dozens of separate things (beds, stalks,
// fences, a mill; trees, verges, an avenue). These two were one building and a
// scatter of gears. Everything added here stands AROUND the landmark, never on
// it, and always on a covered column: with organic outlines a spot picked off
// the footprint rectangle can be air. So every feature snaps to the nearest real
// ground, and every scatter draws only from coveredCols.

public partial class LevelMapController : MonoBehaviour
{
    // ═════════════════════════════════════════════════════════════════════════
    // Placement
    // ═════════════════════════════════════════════════════════════════════════

    // HashSet order isn't stable between runs. Every scatter sorts first so the
    // same save always grows the same plot.
    static List<Vector2Int> SortedCols(IEnumerable<Vector2Int> cols)
    {
        var l = new List<Vector2Int>(cols);
        l.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        return l;
    }

    static Vector2Int NearestCovered(HashSet<Vector2Int> covered, Vector2Int want, HashSet<Vector2Int> avoid = null)
    {
        if (covered.Contains(want) && (avoid == null || !avoid.Contains(want))) return want;
        var best  = want;
        int bestD = int.MaxValue;
        foreach (var c in covered)
        {
            if (avoid != null && avoid.Contains(c)) continue;
            int d = (c.x - want.x) * (c.x - want.x) + (c.y - want.y) * (c.y - want.y);
            if (d < bestD || (d == bestD && (c.x < best.x || (c.x == best.x && c.y < best.y))))
            {
                bestD = d;
                best  = c;
            }
        }
        return best;
    }

    // A footprint-local position (-1..1 on each axis, 0 = the middle) as a column
    // of real ground.
    static Vector2Int ColAtLocal(MapDecorConfig cfg, HashSet<Vector2Int> covered, float lx, float lz,
                                 HashSet<Vector2Int> avoid = null) =>
        NearestCovered(covered, cfg.ColumnAt(new Vector2(lx, lz)), avoid);

    // The plot's rim: covered columns with open air on at least one side.
    static HashSet<Vector2Int> RimCols(HashSet<Vector2Int> covered)
    {
        var rim = new HashSet<Vector2Int>();
        foreach (var c in covered)
            if (!covered.Contains(c + Vector2Int.right) || !covered.Contains(c + Vector2Int.left) ||
                !covered.Contains(c + Vector2Int.up)    || !covered.Contains(c + Vector2Int.down))
                rim.Add(c);
        return rim;
    }

    // The longest unbroken run of usable columns along world row z: where a rail
    // or a belt can lie without bridging air.
    static bool LongestRun(HashSet<Vector2Int> covered, HashSet<Vector2Int> blocked,
                           int z, int x0, int x1, out int a, out int b)
    {
        a = b = 0;
        int best = 0, start = int.MinValue;
        for (int x = x0; x <= x1 + 1; x++)
        {
            var c = new Vector2Int(x, z);
            bool ok = x <= x1 && covered.Contains(c) && (blocked == null || !blocked.Contains(c));
            if (ok) { if (start == int.MinValue) start = x; continue; }
            if (start != int.MinValue && x - start > best) { best = x - start; a = start; b = x - 1; }
            start = int.MinValue;
        }
        return best > 0;
    }

    // Up to `count` columns from `pool` in hash order, none closer than `spacing`
    // cells to another pick and none already in `taken`. Picks are added to
    // `taken`, so the next scatter keeps clear of them.
    static List<Vector2Int> Scatter(List<Vector2Int> pool, int count, int salt, float spacing, HashSet<Vector2Int> taken)
    {
        var picks = new List<Vector2Int>();
        if (pool.Count == 0 || count <= 0) return picks;
        float sp2 = spacing * spacing;
        for (int i = 0; picks.Count < count && i < count * 8; i++)
        {
            var c = pool[Mathf.FloorToInt(Hash01(DecorHash(i, salt)) * pool.Count) % pool.Count];
            if (taken != null && taken.Contains(c)) continue;
            bool clear = true;
            foreach (var p in picks)
                if ((p - c).sqrMagnitude < sp2) { clear = false; break; }
            if (!clear) continue;
            picks.Add(c);
            taken?.Add(c);
        }
        return picks;
    }

    // A column and its ring of neighbours, into every set given.
    static void Reserve(Vector2Int c, int radius, params HashSet<Vector2Int>[] sets)
    {
        for (int x = -radius; x <= radius; x++)
        for (int z = -radius; z <= radius; z++)
            foreach (var s in sets) s.Add(c + new Vector2Int(x, z));
    }

    // A thin bar between two points in `parent`'s local space: a tie line, a cable, a brace.
    void TieLine(Transform parent, Vector3 a, Vector3 b, float thick, Color color)
    {
        var d = b - a;
        if (d.sqrMagnitude < 1e-6f) return;
        var rot = Mathf.Abs(Vector3.Dot(d.normalized, Vector3.up)) > 0.99f
            ? Quaternion.FromToRotation(Vector3.forward, d.normalized)
            : Quaternion.LookRotation(d.normalized, Vector3.up);
        MakeMeshProp(parent, "Tie", RailMesh(), (a + b) * 0.5f, rot, new Vector3(thick, thick, d.magnitude), color, true);
    }

    // A puff of smoke (or bubbles, or embers) rising off `mouth`. Scale arguments
    // are in cells.
    Transform SpawnSmoke(Transform parent, Vector3 mouth, int puffs, Color color, float cs, int seed,
                         float size = 0.3f, float rise = 2.4f, float life = 4.5f, float spread = 0.12f,
                         Vector3? drift = null)
    {
        var s = new GameObject("Smoke").transform;
        s.SetParent(parent, false);
        s.position = mouth;
        // Ambient motion: none on the Low preset, fewer puffs on the middle ones.
        puffs = GraphicsQuality.AmbientMotion ? GraphicsQuality.Scaled(puffs, 2) : 0;
        if (puffs <= 0) return s;
        var list = new Transform[puffs];
        for (int i = 0; i < puffs; i++)
            list[i] = MakeMeshProp(s, $"Puff{i}", PuffMesh(), Vector3.zero, Quaternion.identity, Vector3.one * 0.02f,
                                   Color.Lerp(color, Color.white, Hash01(DecorHash(i, seed * 31 + 7)) * 0.18f), true);
        s.gameObject.AddComponent<DecorSmoke>().Init(list, life, rise * cs, size * cs,
                                                     (drift ?? new Vector3(0.9f, 0f, 0.35f)) * cs, spread * cs);
        return s;
    }

    // The graphics preset's say over a plot once it is built. On Low the ambient
    // motion (bobbing, swaying, flicker, drifting stars) stops, and the plot stands
    // still; the machinery (gears, carts, belts, the telescope, the lighthouse)
    // keeps turning on every preset.
    static void QualityPass(GameObject plotRoot)
    {
        if (plotRoot == null || GraphicsQuality.AmbientMotion) return;
        foreach (var b in plotRoot.GetComponentsInChildren<DecorBob>(true))       b.enabled = false;
        foreach (var b in plotRoot.GetComponentsInChildren<DecorSwing>(true))     b.enabled = false;
        foreach (var b in plotRoot.GetComponentsInChildren<DecorPulse>(true))     b.enabled = false;
        foreach (var b in plotRoot.GetComponentsInChildren<DecorStarField>(true)) b.enabled = false;
    }

    // One pulse driver per call site. Add renderers to it with their own colour pair.
    DecorPulse PulseOn(Transform host, float speed) => host.gameObject.AddComponent<DecorPulse>().Init(speed);

    // ═════════════════════════════════════════════════════════════════════════
    // 1-2 — Order workshop: the yard
    // ═════════════════════════════════════════════════════════════════════════

    // Everything the yard's big pieces claim, decided before any scatter runs: the
    // house and its apron, both lanes, the crane and the water tower with a cell of
    // clearance, and the residents.
    HashSet<Vector2Int> WorkshopReserved(OrderWorkshopConfig cfg, HashSet<Vector2Int> covered, Vector2Int ext,
                                         Vector2Int centreCol, out Vector2Int craneCol, out Vector2Int towerCol,
                                         List<Vector2Int> shedCols)
    {
        var r = new HashSet<Vector2Int>();
        Reserve(NpcColumn(cfg, ext), 0, r);
        Reserve(GameColumn(cfg, ext), 0, r);

        int hx = cfg.houseSize.x / 2 + 1, hz = cfg.houseSize.y / 2 + 1;
        for (int x = -hx; x <= hx; x++)
        for (int z = -hz; z <= hz; z++) r.Add(centreCol + new Vector2Int(x, z));

        for (int x = cfg.origin.x; x < cfg.origin.x + ext.x; x++)
        {
            if (cfg.railEnabled)     r.Add(new Vector2Int(x, cfg.RailRow));
            if (cfg.conveyorEnabled) r.Add(new Vector2Int(x, cfg.ConveyorRow));
        }

        craneCol = ColAtLocal(cfg, covered, -0.74f, 0.05f, r);
        Reserve(craneCol, 1, r);
        towerCol = ColAtLocal(cfg, covered, 0.74f, 0.10f, r);
        Reserve(towerCol, 1, r);

        // Sheds either side of the house, alternating out along the yard. A shed is
        // 3 × 2 cells plus a cell of apron; one that would overlap anything already
        // claimed, or hang off the plot, is skipped.
        for (int i = 0; i < cfg.sheds; i++)
        {
            float lx = (i % 2 == 0 ? -1f : 1f) * (0.44f + 0.14f * (i / 2));
            var c = cfg.ColumnAt(new Vector2(lx, (i % 2 == 0 ? -0.05f : 0.12f)));
            bool fits = true;
            for (int x = -2; x <= 2 && fits; x++)
                for (int z = -2; z <= 2 && fits; z++)
                {
                    var q = c + new Vector2Int(x, z);
                    bool body = Mathf.Abs(x) <= 1 && Mathf.Abs(z) <= 1;
                    if (r.Contains(q) || (body && !covered.Contains(q))) fits = false;
                }
            if (!fits) continue;
            Reserve(c, 2, r);
            shedCols.Add(c);
        }
        return r;
    }

    void BuildWorkshopYard(Transform root, OrderWorkshopConfig cfg, HashSet<Vector2Int> covered,
                           Dictionary<Vector2Int, Vector3Int> colTop, Vector2Int ext, float cs,
                           Vector3 housePos, HashSet<Vector2Int> taken, Vector2Int craneCol, Vector2Int towerCol,
                           List<Vector2Int> shedCols)
    {
        for (int i = 0; i < shedCols.Count; i++)
            BuildShed(root, cfg, ColumnSurface(colTop, shedCols[i], cfg, cs), cs, i);

        int x0 = cfg.origin.x, x1 = cfg.origin.x + ext.x - 1;
        var residents = new HashSet<Vector2Int> { NpcColumn(cfg, ext), GameColumn(cfg, ext) };

        if (cfg.railEnabled && LongestRun(covered, residents, cfg.RailRow, x0, x1, out int ra, out int rb) && rb - ra >= 3)
            BuildRailLine(root, cfg, colTop, cfg.RailRow, ra, rb, cs);

        if (cfg.conveyorEnabled && LongestRun(covered, residents, cfg.ConveyorRow, x0, x1, out int ca, out int cb) && cb - ca >= 3)
        {
            // Capped: a belt running the whole yard reads as a wall, not a machine.
            int maxLen = Mathf.Clamp(ext.x / 2, 6, 12);
            if (cb - ca + 1 > maxLen) { ca = (ca + cb) / 2 - maxLen / 2; cb = ca + maxLen - 1; }
            BuildConveyor(root, cfg, colTop, cfg.ConveyorRow, ca, cb, cs);
        }

        // The crane works over the conveyor, and it slews away from the house, so
        // its load never swings through the walls.
        if (cfg.craneEnabled && covered.Contains(craneCol))
        {
            var p = ColumnSurface(colTop, craneCol, cfg, cs);
            var toBelt = new Vector3(0f, 0f, cfg.ConveyorRow - craneCol.y);
            BuildJibCrane(root, cfg, p, toBelt.sqrMagnitude > 0.01f ? toBelt : Vector3.back, cs);
        }

        if (cfg.waterTowerEnabled && covered.Contains(towerCol))
        {
            var p = ColumnSurface(colTop, towerCol, cfg, cs);
            BuildWaterTower(root, cfg, p, cs);
            BuildPipeRun(root, cfg, p, housePos, cs);
        }

        // ── Crates & barrels ─────────────────────────────────────────────────
        var free = SortedCols(covered);
        free.RemoveAll(taken.Contains);
        int n = 0;
        foreach (var c in Scatter(free, cfg.crates, 4441, 1.5f, taken))
        {
            var p = ColumnSurface(colTop, c, cfg, cs);
            if (Hash01(DecorHash(c.x, c.y ^ 71)) < 0.6f) CrateStack(root, cfg, p, cs, n);
            else                                        BarrelCluster(root, cfg, p, cs, n);
            n++;
        }

        BuildLampPosts(root, cfg, covered, colTop, taken, cfg.lamps, cfg.roofColor, cfg.lampColor, cs * 1.25f, cs);
    }

    // A machine shed: a long low hall under a sawtooth roof (each tooth glazed
    // on its steep face), a big door and a turning gear on the front, a stack
    // smoking at one end.
    void BuildShed(Transform root, OrderWorkshopConfig cfg, Vector3 at, float cs, int i)
    {
        var shed = new GameObject($"Shed{i}").transform;
        shed.SetParent(root, false);
        shed.position = at;
        shed.rotation = Quaternion.Euler(0f, i % 2 == 0 ? 0f : 180f, 0f);   // alternate doors face the belt and the rail

        float w = cs * 2.9f, d = cs * 1.9f, h = cs * 1.1f;
        var walls = Color.Lerp(cfg.wallColor, cfg.roofColor, 0.35f);
        MakeMeshProp(shed, "Hall", RailMesh(), new Vector3(0f, h * 0.5f, 0f), Quaternion.identity,
                     new Vector3(w, h, d), walls, true);
        MakeMeshProp(shed, "Plinth", RailMesh(), new Vector3(0f, cs * 0.06f, 0f), Quaternion.identity,
                     new Vector3(w * 1.06f, cs * 0.12f, d * 1.06f), cfg.roofColor, true);

        const int teeth = 3;
        float tw = w / teeth;
        var glass = new Color(0.55f, 0.72f, 0.84f);
        for (int k = 0; k < teeth; k++)
        {
            float x = -w * 0.5f + tw * (k + 0.5f);
            // Ridge runs front to back (the gable's ridge is along X, hence the turn).
            MakeMeshProp(shed, $"Tooth{k}", GableMesh(), new Vector3(x, h, 0f), Quaternion.Euler(0f, 90f, 0f),
                         new Vector3(d * 1.04f, cs * 0.5f, tw), cfg.roofColor, true);
            MakeMeshProp(shed, $"Glazing{k}", RailMesh(), new Vector3(x + tw * 0.26f, h + cs * 0.24f, 0f), Quaternion.Euler(0f, 0f, 62f),
                         new Vector3(cs * 0.5f, cs * 0.03f, d * 0.9f), glass, true);
        }

        // Front: a door, and the Order's gear turning over it.
        float front = -d * 0.5f - 0.01f;
        MakeMeshProp(shed, "Door", RailMesh(), new Vector3(-w * 0.18f, h * 0.36f, front), Quaternion.identity,
                     new Vector3(w * 0.3f, h * 0.72f, cs * 0.04f), cfg.roofColor, true);
        MakeMeshProp(shed, "DoorStripe", RailMesh(), new Vector3(-w * 0.18f, h * 0.74f, front - 0.005f), Quaternion.identity,
                     new Vector3(w * 0.32f, cs * 0.06f, cs * 0.04f), cfg.hazardColor, true);
        var gear = MakeMeshProp(shed, "Gear", GearMeshFactory.Get(10), new Vector3(w * 0.26f, h * 0.58f, front - cs * 0.03f),
                                Quaternion.Euler(90f, 0f, 0f), Vector3.one * (cs * 0.3f), i % 2 == 0 ? cfg.gearAccent : cfg.gearColor, true);
        gear.gameObject.AddComponent<DecorGearSpin>().Init(cfg.gearBaseSpin * (i % 2 == 0 ? 1f : -1f) / 0.6f, Vector3.up);

        // A stack at the back corner.
        var stack = new Vector3(w * 0.38f, h, d * 0.25f);
        MakeMeshProp(shed, "Stack", DrumMesh(), stack, Quaternion.identity,
                     new Vector3(cs * 0.2f, cs * 0.9f, cs * 0.2f), cfg.roofColor, true);
        MakeMeshProp(shed, "StackBand", DrumMesh(), stack + Vector3.up * (cs * 0.72f), Quaternion.identity,
                     new Vector3(cs * 0.23f, cs * 0.06f, cs * 0.23f), cfg.hazardColor, true);
        if (cfg.smokePuffs > 0)
            SpawnSmoke(shed, shed.TransformPoint(stack + Vector3.up * (cs * 0.92f)), Mathf.Max(3, cfg.smokePuffs - 2),
                       cfg.smokeColor, cs, 40 + i, size: 0.26f, rise: 2f);
    }

    void BuildRailLine(Transform root, OrderWorkshopConfig cfg, Dictionary<Vector2Int, Vector3Int> colTop,
                       int z, int a, int b, float cs)
    {
        var line = new GameObject("RailLine").transform;
        line.SetParent(root, false);

        Vector3 pa = ColumnSurface(colTop, new Vector2Int(a, z), cfg, cs);
        Vector3 pb = ColumnSurface(colTop, new Vector2Int(b, z), cfg, cs);
        var steel = Tint(cfg.gearColor, 0.72f);
        var wood  = new Color(0.36f, 0.27f, 0.20f);

        // Sleepers, two a cell.
        for (int x = a; x <= b; x++)
        for (int k = 0; k < 2; k++)
        {
            var p = ColumnSurface(colTop, new Vector2Int(x, z), cfg, cs);
            MakeMeshProp(line, $"Sleeper_{x}_{k}", RailMesh(), p + new Vector3((k - 0.5f) * cs * 0.5f, cs * 0.025f, 0f),
                         Quaternion.identity, new Vector3(cs * 0.14f, cs * 0.05f, cs * 0.66f), wood);
        }

        float len = pb.x - pa.x + cs * 0.9f;
        var mid = new Vector3((pa.x + pb.x) * 0.5f, pa.y + cs * 0.075f, pa.z);
        for (int s = -1; s <= 1; s += 2)
            MakeMeshProp(line, $"Rail{s}", RailMesh(), mid + new Vector3(0f, 0f, s * cs * 0.2f),
                         Quaternion.identity, new Vector3(len, cs * 0.05f, cs * 0.05f), steel);

        // Buffer stops at both ends, hazard-striped beam across the top.
        for (int s = 0; s < 2; s++)
        {
            var p = new Vector3(s == 0 ? pa.x - cs * 0.44f : pb.x + cs * 0.44f, pa.y, pa.z);
            MakeMeshProp(line, $"Buffer{s}", RailMesh(), p + Vector3.up * (cs * 0.16f), Quaternion.identity,
                         new Vector3(cs * 0.1f, cs * 0.32f, cs * 0.56f), cfg.roofColor);
            MakeMeshProp(line, $"BufferBeam{s}", RailMesh(), p + Vector3.up * (cs * 0.3f), Quaternion.identity,
                         new Vector3(cs * 0.12f, cs * 0.08f, cs * 0.6f), cfg.hazardColor);
        }

        // The shuttle cart: a flatbed with a crate on it, wheels turning with the
        // ground it covers.
        var cart = new GameObject("Cart").transform;
        cart.SetParent(line, false);
        cart.position = pa + Vector3.up * (cs * 0.1f);
        MakeMeshProp(cart, "Bed", RailMesh(), new Vector3(0f, cs * 0.16f, 0f), Quaternion.identity,
                     new Vector3(cs * 0.72f, cs * 0.12f, cs * 0.5f), cfg.gearAccent, true);
        MakeMeshProp(cart, "Chassis", RailMesh(), new Vector3(0f, cs * 0.08f, 0f), Quaternion.identity,
                     new Vector3(cs * 0.6f, cs * 0.06f, cs * 0.36f), cfg.roofColor, true);
        MakeMeshProp(cart, "Load", RailMesh(), new Vector3(-cs * 0.08f, cs * 0.35f, 0f), Quaternion.Euler(0f, 8f, 0f),
                     new Vector3(cs * 0.34f, cs * 0.26f, cs * 0.34f), cfg.crateColor, true);
        MakeMeshProp(cart, "LoadGear", GearMeshFactory.Get(10), new Vector3(cs * 0.2f, cs * 0.24f, 0f), Quaternion.identity,
                     Vector3.one * (cs * 0.1f), cfg.gearColor, true);

        var wheels = new Transform[4];
        for (int i = 0; i < 4; i++)
        {
            float wx = (i & 1) == 0 ? -0.22f : 0.22f, wz = (i & 2) == 0 ? -0.21f : 0.21f;
            wheels[i] = MakeMeshProp(cart, $"Wheel{i}", GearMeshFactory.Get(10), new Vector3(wx * cs, cs * 0.06f, wz * cs),
                                     Quaternion.Euler(90f, 0f, 0f), Vector3.one * (cs * 0.07f), steel, true);
        }

        var from = line.InverseTransformPoint(pa + Vector3.up * (cs * 0.1f));
        var to   = line.InverseTransformPoint(pb + Vector3.up * (cs * 0.1f));
        cart.gameObject.AddComponent<DecorShuttle>().Init(from, to, cs * 1.1f, 1.6f, 0f, wheels, cs * 0.07f);
    }

    void BuildConveyor(Transform root, OrderWorkshopConfig cfg, Dictionary<Vector2Int, Vector3Int> colTop,
                       int z, int a, int b, float cs)
    {
        var belt = new GameObject("Conveyor").transform;
        belt.SetParent(root, false);

        Vector3 pa = ColumnSurface(colTop, new Vector2Int(a, z), cfg, cs);
        Vector3 pb = ColumnSurface(colTop, new Vector2Int(b, z), cfg, cs);
        float top = cs * 0.42f;
        float len = pb.x - pa.x + cs * 0.8f;
        var   mid = new Vector3((pa.x + pb.x) * 0.5f, pa.y, pa.z);
        var  dark = new Color(0.13f, 0.13f, 0.15f);

        MakeMeshProp(belt, "Belt", RailMesh(), mid + Vector3.up * (top - cs * 0.04f), Quaternion.identity,
                     new Vector3(len, cs * 0.08f, cs * 0.46f), dark);
        for (int s = -1; s <= 1; s += 2)
            MakeMeshProp(belt, $"Side{s}", RailMesh(), mid + new Vector3(0f, top, s * cs * 0.25f), Quaternion.identity,
                         new Vector3(len, cs * 0.1f, cs * 0.05f), cfg.gearColor);
        for (int x = a; x <= b; x += 2)
        for (int s = -1; s <= 1; s += 2)
        {
            var p = ColumnSurface(colTop, new Vector2Int(x, z), cfg, cs);
            float legH = top - cs * 0.08f;
            MakeMeshProp(belt, $"Leg_{x}_{s}", RailMesh(), p + new Vector3(0f, legH * 0.5f, s * cs * 0.2f),
                         Quaternion.identity, new Vector3(cs * 0.06f, legH, cs * 0.06f), cfg.roofColor);
        }

        // End rollers, turning.
        for (int s = 0; s < 2; s++)
        {
            float x = mid.x + (s == 0 ? -0.5f : 0.5f) * len;
            var roller = MakeMeshProp(belt, $"Roller{s}", DrumMesh(),
                                      new Vector3(x, mid.y + top - cs * 0.04f, mid.z - cs * 0.25f),
                                      Quaternion.Euler(90f, 0f, 0f), new Vector3(cs * 0.14f, cs * 0.5f, cs * 0.14f),
                                      cfg.gearAccent);
            roller.gameObject.AddComponent<DecorGearSpin>().Init(-160f, Vector3.up);
        }

        // Hopper over the near end, feeding it.
        var hopper = new Vector3(mid.x - len * 0.5f + cs * 0.3f, mid.y + top, mid.z);
        MakeMeshProp(belt, "Hopper", HopperMesh(), hopper + Vector3.up * (cs * 0.16f), Quaternion.identity,
                     new Vector3(cs * 0.34f, cs * 0.42f, cs * 0.34f), cfg.gearAccent);
        for (int s = -1; s <= 1; s += 2)
            MakeMeshProp(belt, $"HopperStay{s}", RailMesh(), hopper + new Vector3(s * cs * 0.2f, cs * 0.2f, cs * 0.26f),
                         Quaternion.identity, new Vector3(cs * 0.04f, cs * 0.4f, cs * 0.04f), cfg.roofColor);

        // A press astride the belt, its ram stamping down on whatever passes.
        var press = new Vector3(mid.x + len * 0.18f, mid.y, mid.z);
        float ph = top + cs * 0.85f;
        for (int s = -1; s <= 1; s += 2)
            MakeMeshProp(belt, $"PressPost{s}", RailMesh(), press + new Vector3(0f, ph * 0.5f, s * cs * 0.34f),
                         Quaternion.identity, new Vector3(cs * 0.1f, ph, cs * 0.1f), cfg.roofColor);
        MakeMeshProp(belt, "PressHead", RailMesh(), press + Vector3.up * ph, Quaternion.identity,
                     new Vector3(cs * 0.3f, cs * 0.16f, cs * 0.82f), cfg.hazardColor);
        var ram = MakeMeshProp(belt, "Ram", RailMesh(), press + Vector3.up * (ph - cs * 0.2f), Quaternion.identity,
                               new Vector3(cs * 0.26f, cs * 0.2f, cs * 0.34f), cfg.gearColor);
        ram.gameObject.AddComponent<DecorShuttle>().Init(
            belt.InverseTransformPoint(press + Vector3.up * (ph - cs * 0.2f)),
            belt.InverseTransformPoint(press + Vector3.up * (top + cs * 0.42f)), cs * 2.4f, 0.8f);

        // Goods on the belt: crates, and every third a finished gear.
        var items = new GameObject("Goods").transform;
        items.SetParent(belt, false);
        var run  = items.gameObject.AddComponent<DecorConveyor>();
        var from = items.InverseTransformPoint(new Vector3(mid.x - len * 0.5f + cs * 0.15f, mid.y + top, mid.z));
        var to   = items.InverseTransformPoint(new Vector3(mid.x + len * 0.5f - cs * 0.15f, mid.y + top, mid.z));
        run.Init(from, to, cs * 0.45f);

        int n = Mathf.Max(2, Mathf.RoundToInt(len / (cs * 0.85f)));
        for (int i = 0; i < n; i++)
        {
            var it = new GameObject($"Item{i}").transform;
            it.SetParent(items, false);
            if (i % 3 == 2)
                MakeMeshProp(it, "Gear", GearMeshFactory.Get(10), new Vector3(0f, cs * 0.04f, 0f), Quaternion.identity,
                             Vector3.one * (cs * 0.13f), cfg.gearAccent, true);
            else
                MakeMeshProp(it, "Crate", RailMesh(), new Vector3(0f, cs * 0.13f, 0f), Quaternion.Euler(0f, i * 17f, 0f),
                             Vector3.one * (cs * 0.26f), Tint(cfg.crateColor, 0.9f + 0.2f * Hash01(DecorHash(i, 5))), true);
            run.Add(it, i / (float)n);
        }
    }

    // A lattice mast with a jib that slews back and forth. A trolley runs out
    // along the jib, and the hook swings under it.
    void BuildJibCrane(Transform root, OrderWorkshopConfig cfg, Vector3 at, Vector3 toward, float cs)
    {
        var crane = new GameObject("JibCrane").transform;
        crane.SetParent(root, false);
        crane.position = at;

        float H = cs * 2.4f;
        var frame = cfg.hazardColor;
        for (int i = 0; i < 4; i++)
        {
            float sx = (i & 1) == 0 ? -1f : 1f, sz = (i & 2) == 0 ? -1f : 1f;
            MakeMeshProp(crane, $"Chord{i}", RailMesh(), new Vector3(sx * cs * 0.13f, H * 0.5f, sz * cs * 0.13f),
                         Quaternion.identity, new Vector3(cs * 0.05f, H, cs * 0.05f), frame, true);
        }
        // Lacing: one diagonal a face per bay, alternating. The zigzag is what says lattice.
        const int bays = 5;
        float bay  = H / bays, span = cs * 0.26f;
        float diag = Mathf.Sqrt(span * span + bay * bay);
        float tilt = Mathf.Atan2(span, bay) * Mathf.Rad2Deg;
        for (int k = 0; k < bays; k++)
        for (int f = 0; f < 4; f++)
        {
            var face = Quaternion.Euler(0f, f * 90f, 0f);
            MakeMeshProp(crane, $"Lace{k}_{f}", RailMesh(),
                         face * new Vector3(0f, 0f, cs * 0.13f) + Vector3.up * (bay * (k + 0.5f)),
                         face * Quaternion.Euler(0f, 0f, (k & 1) == 0 ? tilt : -tilt),
                         new Vector3(cs * 0.025f, diag, cs * 0.025f), frame, true);
        }
        MakeMeshProp(crane, "Foot", RailMesh(), new Vector3(0f, cs * 0.06f, 0f), Quaternion.identity,
                     new Vector3(cs * 0.5f, cs * 0.12f, cs * 0.5f), cfg.roofColor, true);

        var slew = new GameObject("Slew").transform;
        slew.SetParent(crane, false);
        slew.localPosition = Vector3.up * H;
        toward.y = 0f;
        slew.rotation = Quaternion.LookRotation(toward.sqrMagnitude > 1e-4f ? toward.normalized : Vector3.forward, Vector3.up);

        float jib = cs * 2.1f, counter = cs * 0.7f;
        MakeMeshProp(slew, "Cab", RailMesh(), new Vector3(0f, cs * 0.14f, 0f), Quaternion.identity,
                     new Vector3(cs * 0.36f, cs * 0.28f, cs * 0.36f), cfg.wallColor, true);
        MakeMeshProp(slew, "CabGlass", RailMesh(), new Vector3(0f, cs * 0.17f, cs * 0.181f), Quaternion.identity,
                     new Vector3(cs * 0.26f, cs * 0.12f, cs * 0.02f), new Color(0.55f, 0.75f, 0.85f), true);
        MakeMeshProp(slew, "Jib", RailMesh(), new Vector3(0f, cs * 0.32f, (jib - counter) * 0.5f), Quaternion.identity,
                     new Vector3(cs * 0.12f, cs * 0.1f, jib + counter), frame, true);
        MakeMeshProp(slew, "Weight", RailMesh(), new Vector3(0f, cs * 0.22f, -counter + cs * 0.12f), Quaternion.identity,
                     new Vector3(cs * 0.3f, cs * 0.24f, cs * 0.24f), cfg.roofColor, true);
        MakeMeshProp(slew, "Peak", RailMesh(), new Vector3(0f, cs * 0.62f, 0f), Quaternion.identity,
                     new Vector3(cs * 0.06f, cs * 0.6f, cs * 0.06f), frame, true);
        TieLine(slew, new Vector3(0f, cs * 0.9f, 0f), new Vector3(0f, cs * 0.36f, jib * 0.95f), cs * 0.018f, cfg.roofColor);
        TieLine(slew, new Vector3(0f, cs * 0.9f, 0f), new Vector3(0f, cs * 0.3f, -counter + cs * 0.1f), cs * 0.018f, cfg.roofColor);

        var trolley = new GameObject("Trolley").transform;
        trolley.SetParent(slew, false);
        trolley.localPosition = new Vector3(0f, cs * 0.24f, jib * 0.3f);
        MakeMeshProp(trolley, "Body", RailMesh(), Vector3.zero, Quaternion.identity,
                     new Vector3(cs * 0.18f, cs * 0.08f, cs * 0.2f), cfg.roofColor, true);

        var hook = new GameObject("HookPivot").transform;
        hook.SetParent(trolley, false);
        float drop = cs * 1.1f;
        MakeMeshProp(hook, "Cable", RailMesh(), new Vector3(0f, -drop * 0.5f, 0f), Quaternion.identity,
                     new Vector3(cs * 0.015f, drop, cs * 0.015f), new Color(0.15f, 0.15f, 0.16f), true);
        MakeMeshProp(hook, "Block", RailMesh(), new Vector3(0f, -drop, 0f), Quaternion.identity,
                     new Vector3(cs * 0.12f, cs * 0.1f, cs * 0.08f), cfg.hazardColor, true);
        MakeMeshProp(hook, "Load", RailMesh(), new Vector3(0f, -drop - cs * 0.2f, 0f), Quaternion.Euler(0f, 20f, 0f),
                     Vector3.one * (cs * 0.26f), cfg.crateColor, true);
        hook.gameObject.AddComponent<DecorSwing>().Init(Vector3.right, 5f, 1.3f);

        trolley.gameObject.AddComponent<DecorShuttle>().Init(new Vector3(0f, cs * 0.24f, jib * 0.3f),
                                                             new Vector3(0f, cs * 0.24f, jib * 0.9f), cs * 0.6f, 1.6f, 0.8f);
        slew.gameObject.AddComponent<DecorSwing>().Init(Vector3.up, 55f, 0.22f, 0.5f);
    }

    void BuildWaterTower(Transform root, OrderWorkshopConfig cfg, Vector3 at, float cs)
    {
        var tw = new GameObject("WaterTower").transform;
        tw.SetParent(root, false);
        tw.position = at;

        float legH = cs * 1.7f;
        var tank = new Color(0.44f, 0.53f, 0.58f);

        for (int i = 0; i < 4; i++)
        {
            float a = (i * 90f + 45f) * Mathf.Deg2Rad;
            var dir  = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var foot = dir * (cs * 0.44f);
            var head = dir * (cs * 0.3f) + Vector3.up * legH;
            var d = head - foot;
            MakeMeshProp(tw, $"Leg{i}", RailMesh(), (foot + head) * 0.5f, Quaternion.FromToRotation(Vector3.up, d.normalized),
                         new Vector3(cs * 0.07f, d.magnitude, cs * 0.07f), cfg.roofColor, true);
        }
        for (int k = 1; k <= 2; k++)
        {
            float y = legH * k / 3f;
            float r = Mathf.Lerp(0.44f, 0.3f, k / 3f) * cs;
            for (int i = 0; i < 4; i++)
            {
                float a0 = (i * 90f + 45f) * Mathf.Deg2Rad, a1 = ((i + 1) * 90f + 45f) * Mathf.Deg2Rad;
                TieLine(tw, new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * r + Vector3.up * y,
                            new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * r + Vector3.up * y, cs * 0.035f, cfg.roofColor);
            }
        }

        MakeMeshProp(tw, "Deck", DrumMesh(), Vector3.up * legH, Quaternion.identity,
                     new Vector3(cs * 0.96f, cs * 0.05f, cs * 0.96f), cfg.roofColor, true);
        MakeMeshProp(tw, "Tank", DrumMesh(), Vector3.up * (legH + cs * 0.05f), Quaternion.identity,
                     new Vector3(cs * 0.82f, cs * 0.72f, cs * 0.82f), tank, true);
        for (int k = 0; k < 2; k++)
            MakeMeshProp(tw, $"Hoop{k}", DrumMesh(), Vector3.up * (legH + cs * (0.22f + k * 0.28f)), Quaternion.identity,
                         new Vector3(cs * 0.85f, cs * 0.04f, cs * 0.85f), cfg.roofColor, true);
        MakeMeshProp(tw, "Roof", ConeMesh(), Vector3.up * (legH + cs * 0.77f), Quaternion.identity,
                     new Vector3(cs * 0.92f, cs * 0.34f, cs * 0.92f), cfg.roofColor, true);
        MakeMeshProp(tw, "Finial", PuffMesh(), Vector3.up * (legH + cs * 1.13f), Quaternion.identity,
                     Vector3.one * (cs * 0.08f), cfg.gearAccent, true);
        // The Order emblem painted on the tank: a gear, facing the yard.
        MakeMeshProp(tw, "Emblem", GearMeshFactory.Get(10), new Vector3(-cs * 0.42f, legH + cs * 0.42f, 0f),
                     Quaternion.Euler(0f, 0f, 90f), Vector3.one * (cs * 0.17f), cfg.gearAccent, true);

        // Ladder up the far side.
        for (int s = -1; s <= 1; s += 2)
            MakeMeshProp(tw, $"LadderRail{s}", RailMesh(), new Vector3(s * cs * 0.08f, (legH + cs * 0.77f) * 0.5f, cs * 0.46f),
                         Quaternion.identity, new Vector3(cs * 0.02f, legH + cs * 0.77f, cs * 0.02f), cfg.roofColor, true);
        for (int k = 1; k < 9; k++)
            MakeMeshProp(tw, $"Rung{k}", RailMesh(), new Vector3(0f, (legH + cs * 0.77f) * k / 9f, cs * 0.46f),
                         Quaternion.identity, new Vector3(cs * 0.16f, cs * 0.015f, cs * 0.015f), cfg.roofColor, true);
    }

    // A duct from the water tower straight to the nearest point on the house wall,
    // on flanges, with posts under it and a hand-wheel valve halfway.
    void BuildPipeRun(Transform root, OrderWorkshopConfig cfg, Vector3 tower, Vector3 house, float cs)
    {
        float hw = cfg.houseSize.x * cs * 0.5f, hd = cfg.houseSize.y * cs * 0.5f;
        float y  = Mathf.Min(cfg.wallHeight * 0.72f, cs * 1.2f);

        var to = house - tower;
        to.y = 0f;
        if (to.sqrMagnitude < 0.01f) return;
        // Where the line to the house centre enters its footprint.
        float tx = Mathf.Abs(to.x) > 1e-4f ? (Mathf.Abs(to.x) - hw) / Mathf.Abs(to.x) : 0f;
        float tz = Mathf.Abs(to.z) > 1e-4f ? (Mathf.Abs(to.z) - hd) / Mathf.Abs(to.z) : 0f;
        float t  = Mathf.Clamp01(Mathf.Max(tx, tz));

        var dirN = to.normalized;
        var a = tower + dirN * (cs * 0.42f) + Vector3.up * y;
        var b = tower + to * t + Vector3.up * y;
        var d = b - a;
        float len = d.magnitude;
        if (len < cs * 0.4f || Vector3.Dot(d, dirN) <= 0f) return;

        var pipe = new GameObject("PipeRun").transform;
        pipe.SetParent(root, false);
        var rot = Quaternion.LookRotation(d / len, Vector3.up);
        MakeMeshProp(pipe, "Duct", RailMesh(), (a + b) * 0.5f, rot, new Vector3(cs * 0.13f, cs * 0.13f, len), cfg.wallColor);

        int n = Mathf.Max(1, Mathf.FloorToInt(len / (cs * 0.9f)));
        for (int i = 0; i <= n; i++)
        {
            var p = Vector3.Lerp(a, b, i / (float)n);
            MakeMeshProp(pipe, $"Flange{i}", RailMesh(), p, rot, new Vector3(cs * 0.2f, cs * 0.2f, cs * 0.05f), cfg.gearAccent);
            if (i > 0 && i < n)
                MakeMeshProp(pipe, $"Post{i}", RailMesh(), new Vector3(p.x, (p.y + tower.y) * 0.5f, p.z), Quaternion.identity,
                             new Vector3(cs * 0.06f, p.y - tower.y, cs * 0.06f), cfg.roofColor);
        }

        var valve = Vector3.Lerp(a, b, 0.5f);
        MakeMeshProp(pipe, "ValveStem", RailMesh(), valve + Vector3.up * (cs * 0.12f), Quaternion.identity,
                     new Vector3(cs * 0.03f, cs * 0.14f, cs * 0.03f), cfg.roofColor);
        var wheel = MakeMeshProp(pipe, "ValveWheel", RingMesh(), valve + Vector3.up * (cs * 0.2f), Quaternion.identity,
                                 Vector3.one * (cs * 0.26f), cfg.barrelColor);
        wheel.gameObject.AddComponent<DecorGearSpin>().Init(12f, Vector3.up);
    }

    void CrateStack(Transform root, OrderWorkshopConfig cfg, Vector3 at, float cs, int seed)
    {
        int n = 1 + Mathf.FloorToInt(Hash01(DecorHash(seed, 19)) * 3f);
        float y = 0f;
        for (int k = 0; k < n; k++)
        {
            float s   = cs * (0.42f - k * 0.06f);
            float yaw = (Hash01(DecorHash(seed, 37 + k)) - 0.5f) * 30f;
            var off = new Vector3((Hash01(DecorHash(seed, 53 + k)) - 0.5f) * cs * 0.12f, 0f,
                                  (Hash01(DecorHash(seed, 59 + k)) - 0.5f) * cs * 0.12f);
            var c = Tint(cfg.crateColor, 0.88f + 0.24f * Hash01(DecorHash(seed, 61 + k)));
            var crate = MakeMeshProp(root, $"Crate{seed}_{k}", RailMesh(), at + off + Vector3.up * (y + s * 0.5f),
                                     Quaternion.Euler(0f, yaw, 0f), Vector3.one * s, c);
            // A darker band round the middle: a crate, not a block.
            MakeMeshProp(crate, "Band", RailMesh(), Vector3.zero, Quaternion.identity, new Vector3(1.04f, 0.2f, 1.04f), Tint(c, 0.7f), true);
            y += s;
        }
    }

    void BarrelCluster(Transform root, OrderWorkshopConfig cfg, Vector3 at, float cs, int seed)
    {
        int n = 2 + Mathf.FloorToInt(Hash01(DecorHash(seed, 23)) * 2f);
        for (int k = 0; k < n; k++)
        {
            float a = (k / (float)n + Hash01(DecorHash(seed, 29))) * Mathf.PI * 2f;
            var p = at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (cs * 0.18f);
            var c = k == 0 ? cfg.barrelColor : Color.Lerp(cfg.barrelColor, cfg.gearColor, 0.3f + 0.3f * k);
            MakeMeshProp(root, $"Barrel{seed}_{k}", DrumMesh(), p, Quaternion.Euler(0f, k * 20f, 0f),
                         new Vector3(cs * 0.26f, cs * 0.38f, cs * 0.26f), c);
            for (int h = 0; h < 2; h++)
                MakeMeshProp(root, $"Hoop{seed}_{k}_{h}", DrumMesh(), p + Vector3.up * (cs * (0.08f + h * 0.2f)), Quaternion.identity,
                             new Vector3(cs * 0.275f, cs * 0.03f, cs * 0.275f), Tint(c, 0.62f));
        }
    }

    // Lamp posts round the rim, spaced evenly by angle, each arm reaching in over the plot.
    void BuildLampPosts(Transform root, MapDecorConfig cfg, HashSet<Vector2Int> covered,
                        Dictionary<Vector2Int, Vector3Int> colTop, HashSet<Vector2Int> taken,
                        int count, Color post, Color light, float height, float cs)
    {
        if (count <= 0) return;
        var rim = SortedCols(RimCols(covered));
        rim.RemoveAll(taken.Contains);
        if (rim.Count == 0) return;

        var e = cfg.Extent;
        var mid = new Vector2(cfg.origin.x + (e.x - 1) * 0.5f, cfg.origin.z + (e.y - 1) * 0.5f);
        rim.Sort((a, b) => Mathf.Atan2(a.y - mid.y, a.x - mid.x).CompareTo(Mathf.Atan2(b.y - mid.y, b.x - mid.x)));

        var pulse = PulseOn(root, 1.3f);
        float step = rim.Count / (float)count;
        for (int i = 0; i < count; i++)
        {
            var col = rim[Mathf.FloorToInt(i * step + step * 0.5f) % rim.Count];
            if (!taken.Add(col)) continue;
            var inward = new Vector3(mid.x - col.x, 0f, mid.y - col.y);
            if (inward.sqrMagnitude < 1e-4f) inward = Vector3.forward;

            var lamp = new GameObject($"Lamp{i}").transform;
            lamp.SetParent(root, false);
            lamp.position = ColumnSurface(colTop, col, cfg, cs);
            lamp.rotation = Quaternion.LookRotation(inward.normalized, Vector3.up);
            MakeMeshProp(lamp, "Post", RailMesh(), new Vector3(0f, height * 0.5f, 0f), Quaternion.identity,
                         new Vector3(cs * 0.06f, height, cs * 0.06f), post, true);
            MakeMeshProp(lamp, "Arm", RailMesh(), new Vector3(0f, height, cs * 0.16f), Quaternion.identity,
                         new Vector3(cs * 0.04f, cs * 0.04f, cs * 0.36f), post, true);
            MakeMeshProp(lamp, "Shade", TowerCapMesh(), new Vector3(0f, height - cs * 0.06f, cs * 0.32f), Quaternion.identity,
                         new Vector3(cs * 0.2f, cs * 0.1f, cs * 0.2f), post, true);
            var bulb = MakeMeshProp(lamp, "Bulb", PuffMesh(), new Vector3(0f, height - cs * 0.08f, cs * 0.32f), Quaternion.identity,
                                    Vector3.one * (cs * 0.1f), light, true);
            pulse.Add(bulb.GetComponent<Renderer>(), Tint(light, 0.8f), light, i * 1.9f);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 1-3 — Observatory: the grounds
    // ═════════════════════════════════════════════════════════════════════════

    // The low wall around the precinct, traced along the plot's own rim, with a
    // gateway wherever a path reaches the edge. Each rim column links to its rim
    // neighbours east and north, and diagonally only where no square step already
    // joins them. That traces the outline without doubling back into triangles.
    // Posts stand at every turn and end, and taller ones with a brass ball flank
    // each gate.
    void BuildPrecinctWall(Transform root, ObservatoryConfig cfg, HashSet<Vector2Int> covered,
                           Dictionary<Vector2Int, Vector3Int> colTop, float cs)
    {
        var wall = new GameObject("PrecinctWall").transform;
        wall.SetParent(root, false);
        float h = cfg.wallHeight;

        var rim   = RimCols(covered);
        var gates = new HashSet<Vector2Int>();
        foreach (var c in rim) if (cfg.OnPath(c)) gates.Add(c);
        rim.ExceptWith(gates);

        var links = new Dictionary<Vector2Int, List<Vector2Int>>();
        void Link(Vector2Int a, Vector2Int b)
        {
            if (!links.TryGetValue(a, out var la)) links[a] = la = new List<Vector2Int>();
            if (!links.TryGetValue(b, out var lb)) links[b] = lb = new List<Vector2Int>();
            la.Add(b - a);
            lb.Add(a - b);

            Vector3 pa = ColumnSurface(colTop, a, cfg, cs), pb = ColumnSurface(colTop, b, cfg, cs);
            var flat = new Vector3(pb.x - pa.x, 0f, pb.z - pa.z);
            var rot  = Quaternion.LookRotation(flat.normalized, Vector3.up);
            var mid  = new Vector3((pa.x + pb.x) * 0.5f, Mathf.Min(pa.y, pb.y), (pa.z + pb.z) * 0.5f);
            MakeMeshProp(wall, $"Wall_{a.x}_{a.y}_{b.x}_{b.y}", RailMesh(), mid + Vector3.up * (h * 0.5f), rot,
                         new Vector3(cs * 0.28f, h, flat.magnitude), cfg.wallColor);
            MakeMeshProp(wall, $"Cap_{a.x}_{a.y}_{b.x}_{b.y}", RailMesh(), mid + Vector3.up * h, rot,
                         new Vector3(cs * 0.38f, cs * 0.07f, flat.magnitude), cfg.brassColor);
        }

        var sorted = SortedCols(rim);
        foreach (var c in sorted)
        {
            var e = c + Vector2Int.right;
            var n = c + Vector2Int.up;
            var s = c + Vector2Int.down;
            if (rim.Contains(e)) Link(c, e);
            if (rim.Contains(n)) Link(c, n);
            var ne = c + new Vector2Int(1, 1);
            if (rim.Contains(ne) && !rim.Contains(e) && !rim.Contains(n)) Link(c, ne);
            var se = c + new Vector2Int(1, -1);
            if (rim.Contains(se) && !rim.Contains(e) && !rim.Contains(s)) Link(c, se);
        }

        foreach (var c in sorted)
        {
            links.TryGetValue(c, out var dirs);
            bool straight = dirs != null && dirs.Count == 2 && dirs[0] + dirs[1] == Vector2Int.zero;
            bool gatePost = gates.Contains(c + Vector2Int.right) || gates.Contains(c + Vector2Int.left) ||
                            gates.Contains(c + Vector2Int.up)    || gates.Contains(c + Vector2Int.down);
            if (straight && !gatePost) continue;   // the middle of a straight run needs no post

            float ph = h * cfg.cornerPostScale * (gatePost ? 1.25f : 1f);
            var p = ColumnSurface(colTop, c, cfg, cs);
            MakeMeshProp(wall, $"Post_{c.x}_{c.y}", RailMesh(), p + Vector3.up * (ph * 0.5f), Quaternion.identity,
                         new Vector3(cs * 0.4f, ph, cs * 0.4f), cfg.wallColor);
            MakeMeshProp(wall, $"PostCap_{c.x}_{c.y}", RailMesh(), p + Vector3.up * ph, Quaternion.identity,
                         new Vector3(cs * 0.48f, cs * 0.08f, cs * 0.48f), cfg.brassColor);
            if (gatePost)
                MakeMeshProp(wall, $"Finial_{c.x}_{c.y}", PuffMesh(), p + Vector3.up * (ph + cs * 0.15f), Quaternion.identity,
                             Vector3.one * (cs * 0.2f), cfg.brassColor);
        }
    }

    // Instruments in three quarters of the garden, lanterns along the paths,
    // clipped hedges lining them, trees and night-blooming beds in what's left.
    void BuildObservatoryGrounds(Transform root, ObservatoryConfig cfg, HashSet<Vector2Int> covered,
                                 Dictionary<Vector2Int, Vector3Int> colTop, Vector2Int ext, float cs,
                                 Vector3 basePos, Vector2Int centreCol, HashSet<Vector2Int> taken)
    {
        var rim = RimCols(covered);
        taken.UnionWith(rim);
        Reserve(NpcColumn(cfg, ext), 1, taken);
        Reserve(GameColumn(cfg, ext), 1, taken);
        // The meridian arc's feet stand on the east-west path.
        int arcCells = Mathf.RoundToInt(cfg.domeRadius * 2.4f);
        Reserve(centreCol + new Vector2Int(arcCells, 0), 0, taken);
        Reserve(centreCol - new Vector2Int(arcCells, 0), 0, taken);

        var paved = new HashSet<Vector2Int>();
        foreach (var c in covered) if (cfg.OnPath(c) || cfg.OnPlaza(c)) paved.Add(c);
        var avoid = new HashSet<Vector2Int>(taken);
        avoid.UnionWith(paved);

        // ── Instruments ──────────────────────────────────────────────────────
        if (cfg.orrery)
        {
            var c = ColAtLocal(cfg, covered, 0.52f, -0.56f, avoid);
            Reserve(c, 1, avoid, taken);
            BuildOrrery(root, cfg, ColumnSurface(colTop, c, cfg, cs), cs);
        }
        if (cfg.sundial)
        {
            var c = ColAtLocal(cfg, covered, -0.56f, -0.52f, avoid);
            Reserve(c, 1, avoid, taken);
            BuildSundial(root, cfg, ColumnSurface(colTop, c, cfg, cs), cs);
        }
        if (cfg.annex)
        {
            var c = ColAtLocal(cfg, covered, 0.56f, 0.56f, avoid);
            Reserve(c, 1, avoid, taken);
            var p = ColumnSurface(colTop, c, cfg, cs);
            BuildAnnex(root, cfg, p, basePos - p, cs);
        }

        // ── Lanterns ─────────────────────────────────────────────────────────
        // On the path's edge cells, pushed to the garden side of the cell so they
        // line the walk instead of standing in it.
        if (cfg.lanterns > 0)
        {
            var edge = new List<Vector2Int>();
            foreach (var c in SortedCols(covered))
            {
                if (!cfg.OnPath(c) || cfg.OnPlaza(c) || taken.Contains(c)) continue;
                if (GardenSide(cfg, covered, c) != Vector2Int.zero) edge.Add(c);
            }
            var pulse = PulseOn(root, 2.1f);
            int i = 0;
            foreach (var c in Scatter(edge, cfg.lanterns, 5171, 2.2f, taken))
            {
                var side = GardenSide(cfg, covered, c);
                var p = ColumnSurface(colTop, c, cfg, cs) + new Vector3(side.x, 0f, side.y) * (cs * 0.36f);
                BuildLantern(root, cfg, p, cs, i++, pulse);
            }
        }

        // ── Hedges ───────────────────────────────────────────────────────────
        var garden = new List<Vector2Int>();
        foreach (var c in SortedCols(covered))
            if (!paved.Contains(c) && !taken.Contains(c)) garden.Add(c);

        var hedgeCols = garden.FindAll(c => GardenSide(cfg, covered, c, towardPath: true) != Vector2Int.zero);
        hedgeCols.Sort((a, b) => (a - centreCol).sqrMagnitude.CompareTo((b - centreCol).sqrMagnitude));
        for (int i = 0; i < hedgeCols.Count && i < cfg.hedges; i++)
        {
            var c = hedgeCols[i];
            taken.Add(c);
            var p = ColumnSurface(colTop, c, cfg, cs);
            var leaf = Tint(cfg.leafDeep, 0.9f + 0.2f * Hash01(DecorHash(c.x, c.y ^ 41)));
            MakeMeshProp(root, $"Hedge_{c.x}_{c.y}", RailMesh(), p + Vector3.up * (cs * 0.18f), Quaternion.identity,
                         new Vector3(cs * 0.9f, cs * 0.36f, cs * 0.9f), leaf);
            if (i % 3 == 0)
                MakeMeshProp(root, $"Topiary_{c.x}_{c.y}", PuffMesh(), p + Vector3.up * (cs * 0.52f), Quaternion.identity,
                             Vector3.one * (cs * 0.36f), cfg.leafLight);
        }

        // ── Trees ────────────────────────────────────────────────────────────
        // Round crowns and cypress spires alternating: a formal garden's rhythm.
        var mat = GroveMaterial();
        if (mat != null && cfg.gardenTrees > 0)
        {
            var round = TreeMesh.Recipe.Tree();
            round.bark = cfg.bark; round.leafA = cfg.leafDeep; round.leafB = cfg.leafLight;
            var cypress = round;
            cypress.height = 2.4f; cypress.spreadDeg = 14f; cypress.keep = 0.62f; cypress.levels = 3;
            cypress.leafSize = 0.34f; cypress.leavesPerTip = 7; cypress.trunkLean = 2f; cypress.sweepDeg = 6f;

            var rng = new System.Random(cfg.origin.x * 7919 + cfg.origin.z);
            var meshes = new[]
            {
                TreeMesh.Build(round, rng.Next()),   TreeMesh.Build(round, rng.Next()),
                TreeMesh.Build(cypress, rng.Next()), TreeMesh.Build(cypress, rng.Next()),
            };
            var pool = garden.FindAll(c => !taken.Contains(c));
            int i = 0;
            foreach (var c in Scatter(pool, GraphicsQuality.Scaled(cfg.gardenTrees, 2), 2203, 2.2f, taken))
            {
                bool cyp = (i & 1) == 1;
                PlantGrove(root, meshes[(cyp ? 2 : 0) + (i / 2) % 2], mat, ColumnSurface(colTop, c, cfg, cs),
                           cyp ? 0.6f : 0.5f, Hash01(DecorHash(c.x, c.y ^ 97)) * 360f, false, $"GardenTree{i}");
                i++;
            }
        }

        // ── Night blooms ─────────────────────────────────────────────────────
        var bedTops = new List<Vector3>();
        foreach (var c in garden) if (!taken.Contains(c)) bedTops.Add(ColumnSurface(colTop, c, cfg, cs));
        if (bedTops.Count > 0 && cfg.flowers > 0)
        {
            var go = new GameObject("NightBlooms");
            go.transform.SetParent(root, false);
            var patch = go.AddComponent<BloomPatch>();
            patch.bloomDuration = 0.6f;
            patch.bloomStagger  = 0.03f;
            patch.spinSpeed     = 6f;
            patch.swaySpeed     = 0.9f;
            patch.swayAngleDeg  = 5f;
            patch.bobAmplitude  = 0.025f * cs;
            patch.bobSpeed      = 0.8f;
            patch.stemHeight    = 0.18f * cs;
            patch.stemColor     = Color.Lerp(cfg.leafDeep, cfg.leafLight, 0.4f);
            var palette = cfg.flowerPalette != null && cfg.flowerPalette.Length > 0 ? cfg.flowerPalette : DecorPetalPalette();
            patch.Grow(bedTops.ToArray(), palette, cfg.starColor,
                       maxFlowersPerCell: 2, flowerSizeWorld: 0.24f * cs, scatterWorld: 0.3f * cs, maxFlowers: GraphicsQuality.Scaled(cfg.flowers, 4));
        }
    }

    // Which neighbour of `c` is garden (towardPath false: from a path cell), or
    // path (true: from a garden cell). Zero when none is.
    static Vector2Int GardenSide(ObservatoryConfig cfg, HashSet<Vector2Int> covered, Vector2Int c, bool towardPath = false)
    {
        var dirs = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        foreach (var d in dirs)
        {
            var n = c + d;
            if (!covered.Contains(n) || cfg.OnPlaza(n)) continue;
            if (cfg.OnPath(n) == towardPath) return d;
        }
        return Vector2Int.zero;
    }

    void BuildOrrery(Transform root, ObservatoryConfig cfg, Vector3 at, float cs)
    {
        var o = new GameObject("Orrery").transform;
        o.SetParent(root, false);
        o.position = at;

        MakeMeshProp(o, "Plinth", DrumMesh(), Vector3.zero, Quaternion.identity,
                     new Vector3(cs * 0.42f, cs * 0.42f, cs * 0.42f), cfg.drumColor, true);
        MakeMeshProp(o, "Table", DrumMesh(), new Vector3(0f, cs * 0.42f, 0f), Quaternion.identity,
                     new Vector3(cs * 0.6f, cs * 0.05f, cs * 0.6f), cfg.brassColor, true);
        MakeMeshProp(o, "Spindle", RailMesh(), new Vector3(0f, cs * 0.6f, 0f), Quaternion.identity,
                     new Vector3(cs * 0.04f, cs * 0.3f, cs * 0.04f), cfg.brassColor, true);

        float hub = cs * 0.76f;
        var sun = MakeMeshProp(o, "Sun", PuffMesh(), new Vector3(0f, hub, 0f), Quaternion.identity,
                               Vector3.one * (cs * 0.2f), new Color(1.3f, 0.95f, 0.45f), true);
        PulseOn(o, 1.6f).Add(sun.GetComponent<Renderer>(), new Color(1.1f, 0.78f, 0.36f), new Color(1.55f, 1.15f, 0.55f));

        Color[] planets = { cfg.linkColor, cfg.domeColor, new Color(0.85f, 0.45f, 0.35f), cfg.starColor };
        for (int i = 0; i < planets.Length; i++)
        {
            float r = cs * (0.2f + i * 0.12f);
            var arm = new GameObject($"Arm{i}").transform;
            arm.SetParent(o, false);
            arm.localPosition = new Vector3(0f, hub - cs * 0.025f * (i + 1), 0f);
            arm.localRotation = Quaternion.Euler(0f, i * 83f, 0f);
            MakeMeshProp(arm, "Rod", RailMesh(), new Vector3(r * 0.5f, 0f, 0f), Quaternion.identity,
                         new Vector3(r, cs * 0.015f, cs * 0.015f), cfg.brassColor, true);
            MakeMeshProp(arm, "Drop", RailMesh(), new Vector3(r, cs * 0.04f, 0f), Quaternion.identity,
                         new Vector3(cs * 0.012f, cs * 0.08f, cs * 0.012f), cfg.brassColor, true);
            float ps = cs * (0.06f + 0.025f * ((i * 7) % 3));
            MakeMeshProp(arm, "Planet", PuffMesh(), new Vector3(r, cs * 0.09f, 0f), Quaternion.identity,
                         Vector3.one * ps, planets[i], true);
            if (i == 2)
                MakeMeshProp(arm, "Rings", RingMesh(), new Vector3(r, cs * 0.09f, 0f), Quaternion.Euler(20f, 0f, 10f),
                             Vector3.one * (ps * 2.4f), cfg.brassColor, true);
            arm.gameObject.AddComponent<DecorGearSpin>().Init(40f / (1f + i * 0.9f), Vector3.up);
        }
    }

    void BuildSundial(Transform root, ObservatoryConfig cfg, Vector3 at, float cs)
    {
        var s = new GameObject("Sundial").transform;
        s.SetParent(root, false);
        s.position = at;

        MakeMeshProp(s, "Step", DrumMesh(), Vector3.zero, Quaternion.identity,
                     new Vector3(cs * 0.9f, cs * 0.08f, cs * 0.9f), cfg.wallColor, true);
        MakeMeshProp(s, "Pedestal", DrumMesh(), new Vector3(0f, cs * 0.08f, 0f), Quaternion.identity,
                     new Vector3(cs * 0.3f, cs * 0.36f, cs * 0.3f), cfg.drumColor, true);
        MakeMeshProp(s, "Dial", DrumMesh(), new Vector3(0f, cs * 0.44f, 0f), Quaternion.identity,
                     new Vector3(cs * 0.7f, cs * 0.05f, cs * 0.7f), cfg.brassColor, true);
        // The gnomon, raised toward the north.
        MakeMeshProp(s, "Gnomon", RailMesh(), new Vector3(0f, cs * 0.6f, cs * 0.04f), Quaternion.Euler(-40f, 0f, 0f),
                     new Vector3(cs * 0.03f, cs * 0.36f, cs * 0.16f), cfg.brassColor, true);
        for (int i = 0; i < 12; i++)
        {
            float a = i / 12f * Mathf.PI * 2f;
            MakeMeshProp(s, $"Hour{i}", RailMesh(), new Vector3(Mathf.Cos(a) * cs * 0.28f, cs * 0.5f, Mathf.Sin(a) * cs * 0.28f),
                         Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f),
                         new Vector3(cs * (i % 3 == 0 ? 0.09f : 0.05f), cs * 0.015f, cs * 0.02f), cfg.domeColor, true);
        }
        // The shadow creeping round the dial. The one moving part, and slow
        // enough that it only shows if you look.
        var shadow = new GameObject("Shadow").transform;
        shadow.SetParent(s, false);
        shadow.localPosition = new Vector3(0f, cs * 0.495f, 0f);
        MakeMeshProp(shadow, "Blade", RailMesh(), new Vector3(0f, 0f, cs * 0.14f), Quaternion.identity,
                     new Vector3(cs * 0.03f, cs * 0.006f, cs * 0.28f), Tint(cfg.domeColor, 0.5f), true);
        shadow.gameObject.AddComponent<DecorGearSpin>().Init(3f, Vector3.up);
    }

    // A small second observatory: a stone house under its own turning dome, its
    // door toward the main one.
    void BuildAnnex(Transform root, ObservatoryConfig cfg, Vector3 at, Vector3 faceDir, float cs)
    {
        var a = new GameObject("Annex").transform;
        a.SetParent(root, false);
        a.position = at;
        faceDir.y = 0f;
        a.rotation = Quaternion.LookRotation(faceDir.sqrMagnitude > 1e-4f ? faceDir.normalized : Vector3.forward, Vector3.up);

        float w = cs * 1.2f, h = cs * 0.8f;
        MakeMeshProp(a, "Walls", RailMesh(), new Vector3(0f, h * 0.5f, 0f), Quaternion.identity, new Vector3(w, h, w), cfg.drumColor, true);
        MakeMeshProp(a, "Cornice", RailMesh(), new Vector3(0f, h, 0f), Quaternion.identity,
                     new Vector3(w * 1.08f, cs * 0.07f, w * 1.08f), cfg.brassColor, true);
        MakeMeshProp(a, "Door", RailMesh(), new Vector3(0f, h * 0.32f, w * 0.5f + 0.01f), Quaternion.identity,
                     new Vector3(w * 0.26f, h * 0.6f, cs * 0.04f), cfg.domeColor, true);
        for (int i = -1; i <= 1; i += 2)
            MakeMeshProp(a, $"Window{i}", RailMesh(), new Vector3(i * w * 0.3f, h * 0.6f, w * 0.5f + 0.01f), Quaternion.identity,
                         new Vector3(w * 0.14f, h * 0.2f, cs * 0.04f), cfg.starColor, true);

        var pivot = new GameObject("MiniDome").transform;
        pivot.SetParent(a, false);
        pivot.localPosition = new Vector3(0f, h + cs * 0.03f, 0f);
        MakeMeshProp(pivot, "Dome", DomeMesh(), Vector3.zero, Quaternion.identity,
                     new Vector3(w * 0.8f, w * 0.46f, w * 0.8f), cfg.domeColor, true);
        var scope = new GameObject("Scope").transform;
        scope.SetParent(pivot, false);
        scope.localPosition = new Vector3(0f, w * 0.18f, 0f);
        MakeMeshProp(scope, "Tube", DrumMesh(), Vector3.zero, Quaternion.Euler(-50f, 0f, 0f),
                     new Vector3(cs * 0.11f, w * 0.7f, cs * 0.11f), cfg.brassColor, true);
        pivot.gameObject.AddComponent<DecorSkyScan>().Init(scope, -6f, 10f);
    }

    void BuildLantern(Transform root, ObservatoryConfig cfg, Vector3 at, float cs, int i, DecorPulse pulse)
    {
        var l = new GameObject($"Lantern{i}").transform;
        l.SetParent(root, false);
        l.position = at;
        MakeMeshProp(l, "Foot", DrumMesh(), Vector3.zero, Quaternion.identity,
                     new Vector3(cs * 0.14f, cs * 0.06f, cs * 0.14f), cfg.wallColor, true);
        MakeMeshProp(l, "Post", RailMesh(), new Vector3(0f, cs * 0.36f, 0f), Quaternion.identity,
                     new Vector3(cs * 0.05f, cs * 0.66f, cs * 0.05f), cfg.brassColor, true);
        var glow = MakeMeshProp(l, "Light", RailMesh(), new Vector3(0f, cs * 0.76f, 0f), Quaternion.Euler(0f, 45f, 0f),
                                Vector3.one * (cs * 0.13f), cfg.lanternColor, true);
        MakeMeshProp(l, "Roof", PyramidMesh(), new Vector3(0f, cs * 0.83f, 0f), Quaternion.Euler(0f, 45f, 0f),
                     new Vector3(cs * 0.2f, cs * 0.12f, cs * 0.2f), cfg.domeColor, true);
        pulse.Add(glow.GetComponent<Renderer>(), Tint(cfg.lanternColor, 0.8f), cfg.lanternColor, i * 1.7f);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Meshes
    // ═════════════════════════════════════════════════════════════════════════

    static Mesh _puffMesh;
    static readonly Dictionary<string, Mesh> _solids = new();

    static Mesh DrumMesh()    => Solid("DecorDrum",    8, 1f);      // barrels, tanks, pipes, trunks
    static Mesh ConeMesh()    => Solid("DecorCone",    8, 0f);      // roofs
    static Mesh PyramidMesh() => Solid("DecorPyramid", 4, 0f);      // capstones, shards
    static Mesh HexMesh()     => Solid("DecorHex",     6, 1f);      // basalt columns
    static Mesh HopperMesh()  => Solid("DecorHopper",  4, 1.7f);    // widening upward
    static Mesh ObeliskMesh() => Solid("DecorObelisk", 4, 0.62f);   // narrowing upward
    static Mesh BeamMesh()    => Solid("DecorBeam",    4, 3f);      // a lighthouse's light, spreading

    // A prism, frustum or cone. It has `sides` faces round and is 1 unit across
    // the flats at y 0, scaled by `top` at y 1 (0 = a point). Both ends are capped.
    // Faces are wound outward by construction (see OutTri), so any profile is safe.
    static Mesh Solid(string name, int sides, float top)
    {
        if (_solids.TryGetValue(name, out var m) && m != null) return m;
        if (!TryLoadBaked(name, ref m))
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            var inside = new Vector3(0f, 0.3f, 0f);
            float r0 = 0.5f / Mathf.Cos(Mathf.PI / sides);
            float r1 = r0 * top;
            float off = Mathf.PI / sides;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides + off, a1 = (i + 1) * Mathf.PI * 2f / sides + off;
                var b0 = new Vector3(Mathf.Cos(a0) * r0, 0f, Mathf.Sin(a0) * r0);
                var b1 = new Vector3(Mathf.Cos(a1) * r0, 0f, Mathf.Sin(a1) * r0);
                var u0 = new Vector3(Mathf.Cos(a0) * r1, 1f, Mathf.Sin(a0) * r1);
                var u1 = new Vector3(Mathf.Cos(a1) * r1, 1f, Mathf.Sin(a1) * r1);
                OutTri(v, t, b0, b1, u0, inside);
                if (r1 > 1e-4f)
                {
                    OutTri(v, t, b1, u1, u0, inside);
                    OutTri(v, t, Vector3.up, u0, u1, inside);
                }
                OutTri(v, t, Vector3.zero, b0, b1, inside);
            }
            m = Finish(name, v, t);
        }
        _solids[name] = m;
        return m;
    }

    // Low icosahedron, unit diameter, centred: smoke, boulders, planets, buoys.
    // Faceted on purpose, like everything else on the map.
    static Mesh PuffMesh()
    {
        if (_puffMesh != null) return _puffMesh;
        if (TryLoadBaked("DecorPuff", ref _puffMesh)) return _puffMesh;

        float g = (1f + Mathf.Sqrt(5f)) * 0.5f;
        var P = new[]
        {
            new Vector3(-1f,  g, 0f), new Vector3( 1f,  g, 0f), new Vector3(-1f, -g, 0f), new Vector3( 1f, -g, 0f),
            new Vector3( 0f, -1f,  g), new Vector3( 0f,  1f,  g), new Vector3( 0f, -1f, -g), new Vector3( 0f,  1f, -g),
            new Vector3( g, 0f, -1f), new Vector3( g, 0f,  1f), new Vector3(-g, 0f, -1f), new Vector3(-g, 0f,  1f),
        };
        for (int i = 0; i < P.Length; i++) P[i] = P[i].normalized * 0.5f;
        int[] F =
        {
            0, 11, 5,  0, 5, 1,   0, 1, 7,   0, 7, 10,  0, 10, 11,
            1, 5, 9,   5, 11, 4,  11, 10, 2, 10, 7, 6,  7, 1, 8,
            3, 9, 4,   3, 4, 2,   3, 2, 6,   3, 6, 8,   3, 8, 9,
            4, 9, 5,   2, 4, 11,  6, 2, 10,  8, 6, 7,   9, 8, 1,
        };
        var v = new List<Vector3>();
        var t = new List<int>();
        for (int i = 0; i < F.Length; i += 3) OutTri(v, t, P[F[i]], P[F[i + 1]], P[F[i + 2]], Vector3.zero);
        _puffMesh = Finish("DecorPuff", v, t);
        return _puffMesh;
    }

    // A triangle wound to face away from `inside`. Only valid for convex shapes,
    // which is all of these.
    static void OutTri(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 inside)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - inside) < 0f) { var s = b; b = c; c = s; }
        Tri(v, t, a, b, c);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Moving parts
    // ═════════════════════════════════════════════════════════════════════════
    // All in LOCAL space: the whole plot is sunk and raised by its root during the
    // reveal, and anything pinned to world positions would be left behind.

    // Back and forth between two local points, easing at each end and resting a
    // moment there. Optional wheels turn with the ground covered. faceTravel turns
    // it round during each rest (its rest rotation must face A→B).
    class DecorShuttle : MonoBehaviour
    {
        Vector3 _a, _b, _last;
        float _speed, _pause, _t, _len, _wheelR;
        Transform[] _wheels;
        bool _face;
        Quaternion _rest;

        public void Init(Vector3 localA, Vector3 localB, float speed, float pause, float phase = 0f,
                         Transform[] wheels = null, float wheelRadius = 0.1f, bool faceTravel = false)
        {
            _a = localA; _b = localB;
            _speed  = Mathf.Max(0.01f, speed);
            _pause  = Mathf.Max(0f, pause);
            _t      = phase;
            _wheels = wheels;
            _wheelR = Mathf.Max(0.01f, wheelRadius);
            _len    = Vector3.Distance(_a, _b);
            _face   = faceTravel;
            _rest   = transform.localRotation;
            _last   = _a;
            transform.localPosition = _a;
        }

        void Update()
        {
            if (_len < 0.001f) return;
            _t += Time.deltaTime;
            float go = _len / _speed, leg = go + _pause;
            float u = Mathf.Repeat(_t, 2f * leg);
            bool back = u >= leg;
            float s = back ? u - leg : u;
            float k = s < go ? Mathf.SmoothStep(0f, 1f, s / go) : 1f;
            var p = back ? Vector3.Lerp(_b, _a, k) : Vector3.Lerp(_a, _b, k);

            if (_wheels != null)
            {
                float deg = Vector3.Dot(p - _last, (_b - _a) / _len) / _wheelR * Mathf.Rad2Deg;
                foreach (var w in _wheels) if (w != null) w.Rotate(Vector3.up, -deg, Space.Self);
            }
            if (_face)
            {
                float turn = s < go ? 0f : Mathf.SmoothStep(0f, 1f, (s - go) / Mathf.Max(0.01f, _pause));
                transform.localRotation = _rest * Quaternion.Euler(0f, (back ? 180f : 0f) + turn * 180f, 0f);
            }
            transform.localPosition = _last = p;
        }
    }

    // Items riding a belt from one local point to the other and round again. Each
    // shrinks away over the last stretch and grows back at the start, so the
    // wrap never pops.
    class DecorConveyor : MonoBehaviour
    {
        struct Item { public Transform t; public Vector3 scale; public float u; }
        readonly List<Item> _items = new();
        Vector3 _a, _b;
        float _speed, _len;

        public void Init(Vector3 localA, Vector3 localB, float speed)
        {
            _a = localA; _b = localB; _speed = speed;
            _len = Mathf.Max(0.01f, Vector3.Distance(localA, localB));
        }

        public void Add(Transform t, float u)
        {
            var it = new Item { t = t, scale = t.localScale, u = u };
            _items.Add(it);
            Place(it);
        }

        void Place(Item it)
        {
            it.t.localPosition = Vector3.Lerp(_a, _b, it.u);
            float edge = Mathf.Clamp01(Mathf.Min(it.u, 1f - it.u) / 0.08f);
            it.t.localScale = it.scale * Mathf.Max(0.02f, edge);
        }

        void Update()
        {
            float du = _speed * Time.deltaTime / _len;
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                if (it.t == null) continue;
                it.u = Mathf.Repeat(it.u + du, 1f);
                _items[i] = it;
                Place(it);
            }
        }
    }

    // Rocks about a local axis on a sine: a hanging hook, a slewing jib, a frond.
    class DecorSwing : MonoBehaviour
    {
        Quaternion _rest;
        Vector3 _axis;
        float _amp, _speed, _phase;

        public void Init(Vector3 axis, float amplitudeDeg, float speed, float phase = 0f)
        {
            _rest = transform.localRotation;
            _axis = axis; _amp = amplitudeDeg; _speed = speed; _phase = phase;
        }

        void Update() =>
            transform.localRotation = _rest * Quaternion.AngleAxis(Mathf.Sin(Time.time * _speed + _phase) * _amp, _axis);
    }

    // Puffs rising off a mouth: each is born small, swells as it climbs and
    // drifts downwind, then shrinks away. A fixed pool, cycled. With small puffs,
    // a short rise and wide spread it serves for embers and bubbles too.
    class DecorSmoke : MonoBehaviour
    {
        Transform[] _puffs;
        Vector3[]   _off;
        float[]     _phase;
        float _life, _rise, _size;
        Vector3 _drift;

        public void Init(Transform[] puffs, float life, float rise, float size, Vector3 drift, float spread)
        {
            _puffs = puffs; _life = Mathf.Max(0.1f, life); _rise = rise; _size = size; _drift = drift;
            _phase = new float[puffs.Length];
            _off   = new Vector3[puffs.Length];
            for (int i = 0; i < puffs.Length; i++)
            {
                _phase[i] = i / (float)puffs.Length;
                float a = Hash01(DecorHash(i, 887)) * Mathf.PI * 2f;
                _off[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (spread * (0.4f + 0.6f * Hash01(DecorHash(i, 331))));
            }
        }

        void Update()
        {
            if (_puffs == null) return;
            float time = Time.time / _life;
            for (int i = 0; i < _puffs.Length; i++)
            {
                var p = _puffs[i];
                if (p == null) continue;
                float u = Mathf.Repeat(time + _phase[i], 1f);
                p.localPosition = Vector3.up * (_rise * u) + _drift * (u * u) + _off[i] * u;
                p.localScale    = Vector3.one * Mathf.Max(0.02f, _size * Mathf.Sin(u * Mathf.PI) * (0.55f + u));
                p.localRotation = Quaternion.Euler(u * 80f, i * 47f + u * 50f, 0f);
            }
        }
    }

    // Floating: a slow heave plus a little roll and pitch. Boats, buoys, foam.
    class DecorBob : MonoBehaviour
    {
        Vector3 _home;
        Quaternion _rest;
        float _amp, _roll, _speed, _phase;

        public void Init(float amplitude, float rollDeg, float speed, float phase)
        {
            _home = transform.localPosition; _rest = transform.localRotation;
            _amp = amplitude; _roll = rollDeg; _speed = speed; _phase = phase;
        }

        void Update()
        {
            float t = Time.time * _speed + _phase;
            transform.localPosition = _home + Vector3.up * (Mathf.Sin(t) * _amp);
            transform.localRotation = _rest * Quaternion.Euler(Mathf.Sin(t * 0.8f + 1.3f) * _roll, 0f, Mathf.Sin(t * 1.1f) * _roll);
        }
    }

    // Colour breathing between two values per renderer: lava, lamps, lanterns.
    // One driver for a whole set, through MpbColor, so the shared material is
    // never touched.
    class DecorPulse : MonoBehaviour
    {
        struct Entry { public Renderer r; public Color a, b; public float phase; }
        readonly List<Entry> _rs = new();
        float _speed;

        public DecorPulse Init(float speed) { _speed = speed; return this; }

        public void Add(Renderer r, Color a, Color b, float phase = 0f)
        {
            if (r != null) _rs.Add(new Entry { r = r, a = a, b = b, phase = phase });
        }

        void Update()
        {
            float t = Time.time * _speed;
            for (int i = 0; i < _rs.Count; i++)
            {
                var e = _rs[i];
                if (e.r != null) MpbColor.Set(e.r, Color.Lerp(e.a, e.b, 0.5f + 0.5f * Mathf.Sin(t + e.phase)));
            }
        }
    }
}
