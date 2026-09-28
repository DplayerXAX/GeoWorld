using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The map reveals itself as the player progresses.
//
// A level's REGION — its block and the ground around it — is not on the map until
// the level it waits for (LevelDefinition.revealAfter) has been cleared. On the
// visit right after that clear, it rises out of the ground together with whatever
// decor plots the same clear unlocked, in one cutscene. On later visits it is
// simply there.
//
// ── WHY NOT THE UNLOCK FLAGS ─────────────────────────────────────────────────
//
// Every level is unlockedByDefault, and progression runs through the map itself:
// the regions are separated by carved gaps the player bridges with the blocks each
// clear rewards. So "unlocked" says nothing about what should be visible, and the
// reveal has its own gate — the same shape decor plots already use (gateLevelId).
//
// ── WHICH BLOCKS BELONG TO WHICH REGION ──────────────────────────────────────
//
// Derived from the map, not authored. Every authored block is assigned to the
// level it is closest to by walking distance — a multi-source flood from every
// level block across the map's own adjacency. Because the regions are separated by
// those carved gaps, the flood cannot cross between them, so each island of the
// map goes to the levels standing on it without anyone having to label a single
// block. Within one island, a path between two levels splits at its midpoint.
//
// Only the AUTHORED map is partitioned. Blocks the player built are theirs and are
// always shown; they can only ever have been placed on ground that was revealed.
public partial class LevelMapController : MonoBehaviour
{
    [Header("Map reveal")]
    [Tooltip("Show every region regardless of progress. Also implied by autoFill, whose whole point is being able to walk to any level.")]
    public bool revealWholeMap = false;

    [Tooltip("How far below its resting place a newly revealed block starts.")]
    public float revealRiseHeight = 3f;

    // Revealed ground rises CUBE BY CUBE, not block by block: every cube of every
    // rising block has its own start, so a block assembles itself out of the mist
    // rather than arriving whole.
    [Tooltip("Seconds for ONE cube to come up. Short, so each cube reads as its own arrival.")]
    public float revealCubeDuration = 0.6f;

    [Tooltip("Seconds between one cube starting to rise and the next. Cubes go roughly in order of distance from the land that was already there, shuffled by `revealShuffle`, so the new ground builds itself outward, interleaved.")]
    public float revealCubeInterval = 0.05f;

    [Tooltip("How much the order is shuffled, in cells of distance. 0 = a clean wave outward; higher = neighbouring cubes, even within one block, come up in a scattered, interleaved order.")]
    [Range(0f, 6f)] public float revealShuffle = 2.5f;

    [Tooltip("Cap on the whole sequence (first start to last start), in seconds. A big region tightens its interval to fit rather than dragging on.")]
    public float revealMaxSpread = 3.5f;

    [Tooltip("How far a block overshoots its resting place before settling, as a fraction of the rise. 0 = plain ease-out.")]
    [Range(0f, 0.3f)] public float revealOvershoot = 0.08f;

    [Header("Mist")]
    // Ground that has not been revealed is not simply absent: it lies under mist,
    // so the player can see that there IS more map out there and roughly where.
    // One bank per reveal gate — everything waiting on the same clear sits under
    // one cloud — and on the visit after that clear the bank burns off and the land
    // rises out of it.
    public bool  mistEnabled = true;
    [Tooltip("Look of the veil over unrevealed ground. Ray-marched and lit through the shadow map (GeoWorld/MistVolume), so anything standing in it casts a lane of shade through it.")]
    public MistBank.Settings mistStyle = new()
    {
        color = new Color(0.94f, 0.91f, 0.86f), strength = 0.92f, density = 1.7f,
        height = 3f, depth = 6f, offset = -3f, falloff = 0.9f, skyBlend = 0.3f,
        scatter = 1.8f, anisotropy = 0.55f, steps = 24,
        edge = 5f, clearance = 1.4f, edgeWarp = 1.4f,
    };
    [Tooltip("Seconds for a bank to burn off.")]
    public float mistDisperse = 1.4f;
    [Tooltip("Seconds of burn-off before the land starts to rise, so it comes up out of thinning mist rather than through a solid cloud.")]
    public float mistLead = 0.6f;

    [Header("Height fog")]
    // A sea of fog UNDER the map: the blocks stand in it up to their ankles, you look
    // down into it between them, and revealed ground rises up out of it. Analytic
    // (GeoWorld/HeightFog), so it is smooth at any distance and nearly free.
    public bool heightFogEnabled = true;
    public HeightFog.Settings heightFogStyle = new()
    {
        color = new Color(0.90f, 0.89f, 0.87f), density = 0.35f,
        topOffset = -0.5f, falloff = 0.8f, wave = 0.6f, maxDistance = 400f,
        skyBlend = 0.7f, scatter = 0.8f, anisotropy = 0.5f,
        mapClear = 1f, clearFrom = 0f, clearTo = 0.6f,
    };
    HeightFog _heightFog;

    [Header("Horizon haze")]
    // Beyond the banks: air that thickens with distance from the ground on show,
    // until past the edge of the map it swallows the backdrop — the depth you get
    // looking across a Journey dune. Clear over the revealed map, so it draws back
    // as the map grows. Independent of the banks: it stays when the whole map is
    // revealed, framing it.
    public bool hazeEnabled = true;
    public MistBank.Settings hazeStyle = new()
    {
        color = new Color(0.93f, 0.90f, 0.86f), strength = 0.95f, density = 0.45f,
        height = 7f, depth = 10f, offset = -3f, falloff = 0.7f, skyBlend = 0.85f,
        scatter = 1.6f, anisotropy = 0.5f, steps = 28,
        edge = 24f, clearance = 2.5f, edgeWarp = 3f, noiseScale = 0.1f,
    };
    [Tooltip("How far past the furthest map block the haze volume goes on, in cells. It should reach the backdrop, or the backdrop shows above and beyond it.")]
    public float hazeMargin = 30f;
    [Tooltip("Haze density at the inner mist banks, as a fraction of its full strength. Outward of each bank the density is interpolated from this up to full at the horizon, so bank and haze grade into each other with no gap between. 0 = independent (the haze ignores the banks).")]
    [Range(0f, 1f)] public float hazeAtBanks = 0.4f;
    [Tooltip("The haze sinks away from the map's centre, so it falls off toward the horizon instead of lying flat. start = flat out to this many cells from the centre; rate = cells of drop per cell of distance beyond it; max = the most it drops, in cells; soft = cells over which the drop eases in (no crease where it starts).")]
    public MistBank.Sink hazeSink = new() { start = 14f, rate = 0.35f, max = 12f, soft = 8f };
    MistBank _haze;

    readonly Dictionary<string, MistBank> _mist   = new();   // gate levelId -> bank
    readonly Dictionary<LevelNode, string> _gateOf = new();  // veiled / rising block -> its gate

    readonly List<LevelNode>        _veiled     = new();   // not revealed yet: inactive, off the walkable map
    readonly List<LevelNode>        _rising     = new();   // revealed by the clear we just came back from
    readonly Dictionary<LevelNode, Vector3> _risingRest = new();
    // What actually moves in the rise: each cube of each rising block, plus one pivot
    // per block carrying its marker and anything else standing on it.
    readonly List<(Transform t, Vector3 rest, float delay)> _riseItems = new();
    readonly HashSet<Vector2Int>    _veiledCols = new();
    readonly HashSet<Vector2Int>    _risingCols = new();

    // The level whose first clear we have just come back from — captured ONCE, then
    // cleared from RunConfig. Both the regions and the decor plots read this copy.
    // Previously the first decor plot to match cleared the flag itself, so a second
    // plot gated on the same level (the farm and the grove both wait on 1-1) never
    // got to rise.
    string _growthLevelId;

    // Right after the authored map is built, BEFORE player blocks are replayed and
    // anything is linked or surfaced — a veiled block must be off the map for all of
    // that, or the pawn could walk onto ground that is not there.
    void VeilUnrevealedRegions()
    {
        _growthLevelId = RunConfig.PendingMapGrowthLevelId;
        RunConfig.PendingMapGrowthLevelId = null;

        _veiled.Clear(); _rising.Clear(); _risingRest.Clear(); _riseItems.Clear();
        _veiledCols.Clear(); _risingCols.Clear(); _gateOf.Clear();

        if (revealWholeMap || autoFill) return;

        var authored = new List<LevelNode>(_nodes);
        var owner = AssignRegions(authored);

        foreach (var n in authored)
        {
            if (n == null || !owner.TryGetValue(n, out var lead) || lead?.level == null) continue;
            var gate = lead.level.revealAfter;
            if (gate == null || string.IsNullOrEmpty(gate.levelId)) continue;

            bool cleared = SaveSystem.Profile.GetRecord(gate.levelId)?.cleared ?? false;
            if (!cleared || gate.levelId == _growthLevelId) _gateOf[n] = gate.levelId;
            if (!cleared)
            {
                _veiled.Add(n);
                if (n.cells != null) foreach (var c in n.cells) _veiledCols.Add(new Vector2Int(c.x, c.z));
            }
            else if (gate.levelId == _growthLevelId)
            {
                _rising.Add(n);
                if (n.cells != null) foreach (var c in n.cells) _risingCols.Add(new Vector2Int(c.x, c.z));
            }
        }

        foreach (var n in _veiled)
        {
            _nodes.Remove(n);
            n.gameObject.SetActive(false);
        }
    }

    // Multi-source flood from every level block across the map's own adjacency.
    // Seeds whose region is visible from the start go into the queue FIRST, so where
    // two levels are exactly equidistant from a path block, the path stays with the
    // one already on show — a shared path never goes missing between two revealed
    // levels.
    Dictionary<LevelNode, LevelNode> AssignRegions(List<LevelNode> nodes)
    {
        var owner = new Dictionary<LevelNode, LevelNode>();
        var adj   = new Dictionary<LevelNode, List<LevelNode>>();
        foreach (var n in nodes) adj[n] = new List<LevelNode>();
        for (int i = 0; i < nodes.Count; i++)
            for (int j = i + 1; j < nodes.Count; j++)
                if (nodes[i] != null && nodes[j] != null && nodes[i].IsAdjacentTo(nodes[j]))
                {
                    adj[nodes[i]].Add(nodes[j]);
                    adj[nodes[j]].Add(nodes[i]);
                }

        var seeds = new List<LevelNode>();
        foreach (var n in nodes) if (n != null && n.level != null) seeds.Add(n);
        seeds.Sort((a, b) => (a.level.revealAfter == null ? 0 : 1).CompareTo(b.level.revealAfter == null ? 0 : 1));

        var queue = new Queue<LevelNode>();
        foreach (var s in seeds) { owner[s] = s; queue.Enqueue(s); }

        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            foreach (var m in adj[n])
                if (!owner.ContainsKey(m)) { owner[m] = owner[n]; queue.Enqueue(m); }
        }

        // An island with no level on it goes to the nearest level that is NOT on
        // show from the start. Such an island is, by definition, not joined to the
        // starting ground, so it cannot be part of it: by plain nearest distance a
        // stepping-stone out toward a later region could land on the start's side
        // of the line and be left floating on its own in an otherwise empty map.
        var pool = seeds.FindAll(s => s.level.revealAfter != null);
        if (pool.Count == 0) pool = seeds;
        foreach (var n in nodes)
        {
            if (n == null || owner.ContainsKey(n) || pool.Count == 0) continue;
            LevelNode best = null; float bestD = float.MaxValue;
            foreach (var s in pool)
            {
                float d = (s.transform.position - n.transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = s; }
            }
            owner[n] = best;
        }
        return owner;
    }

    // Lay a bank of mist over everything still waiting on a clear — and over what
    // the clear we just came back from is about to reveal, so the cutscene can burn
    // it off. Covers both kinds of hidden ground: level regions, and decor plots.
    void BuildMist()
    {
        foreach (var b in _mist.Values) if (b != null) Destroy(b.gameObject);
        _mist.Clear();
        if (_haze != null) { Destroy(_haze.gameObject); _haze = null; }
        if (_heightFog != null) { Destroy(_heightFog.gameObject); _heightFog = null; }
        if (gridSystem == null) return;
        bool banks = mistEnabled && !revealWholeMap && !autoFill;

        // The sea under the map. Its top is measured from the underside of the
        // lowest block of the WHOLE map, hidden regions included, so it stays at the
        // same height as the map reveals itself.
        if (heightFogEnabled)
        {
            float lowest = MapLowestY();
            if (lowest < float.MaxValue)
                _heightFog = HeightFog.Create(transform, heightFogStyle, lowest, gridSystem.cellSize);
        }

        var cover = new Dictionary<string, List<Vector3>>();
        void Add(string gate, Vector3 p)
        {
            if (!cover.TryGetValue(gate, out var list)) cover[gate] = list = new List<Vector3>();
            list.Add(p);
        }

        // Region blocks, at the height they will STAND at — rising ones are sunk
        // already, so their transform would put the mist underground.
        foreach (var kv in _gateOf)
            if (kv.Key?.cells != null)
                foreach (var c in kv.Key.cells) Add(kv.Value, BlockTop(c));

        // Decor plots not built yet, and the ones rising this visit.
        foreach (var cfg in AllDecorConfigs())
        {
            if (cfg == null || !cfg.enabled || string.IsNullOrEmpty(cfg.gateLevelId)) continue;
            bool cleared = SaveSystem.Profile.GetRecord(cfg.gateLevelId)?.cleared ?? false;
            if (cleared && cfg.gateLevelId != _growthLevelId) continue;   // already standing
            foreach (var p in PlotFootprint(cfg)) Add(cfg.gateLevelId, p);
        }

        // Everything already on show — the level blocks standing from the start, the
        // player's own builds, and the plots already grown. The mist is carved away
        // around these whatever it was asked to cover: a padded plot footprint or a
        // soft edge must never spill over the ground the player can already use.
        var avoid = new List<Vector3>();
        foreach (var n in _nodes)
            if (n != null && n.cells != null && n.gameObject.activeSelf && !_gateOf.ContainsKey(n))
                foreach (var c in n.cells) avoid.Add(BlockTop(c));
        foreach (var cfg in AllDecorConfigs())
        {
            if (cfg == null || !cfg.enabled) continue;
            bool standing = string.IsNullOrEmpty(cfg.gateLevelId)
                || ((SaveSystem.Profile.GetRecord(cfg.gateLevelId)?.cleared ?? false) && cfg.gateLevelId != _growthLevelId);
            if (standing) foreach (var p in PlotFootprint(cfg)) avoid.Add(p);
        }

        MistBank.SetProtected(avoid, gridSystem.cellSize);

        if (hazeEnabled)
        {
            // Clear over what is on show AND what rises this visit — that ground is
            // under its own bank until the cutscene burns it off, and must come up
            // into clear air, not into the haze. Spanning everything, hidden or not,
            // so the volume covers the whole map before its margin.
            var clear  = new List<Vector3>(avoid);
            var extent = new List<Vector3>(avoid);
            var feed   = new List<Vector3>();      // under the banks that STAY — see hazeAtBanks
            foreach (var kv in cover)
            {
                extent.AddRange(kv.Value);
                if (kv.Key == _growthLevelId) clear.AddRange(kv.Value);
                else if (banks)               feed.AddRange(kv.Value);
            }
            _haze = MistBank.CreateHorizon(transform, "HorizonHaze", clear, extent,
                                           gridSystem.cellSize, hazeStyle, hazeMargin, 104729,
                                           feed, hazeAtBanks, hazeSink);
        }

        if (!banks) return;

        int seed = 1;
        foreach (var kv in cover)
        {
            var bank = MistBank.Create(transform, "Mist_" + kv.Key, kv.Value,
                                       gridSystem.cellSize, mistStyle, 7919 * seed++, avoid);
            if (bank != null) _mist[kv.Key] = bank;
        }
    }

    // World Y of the underside of the lowest block on the WHOLE authored map, hidden
    // regions included (they sit in _gateOf, not _nodes, while veiled) — so it does
    // not move as the map reveals itself. float.MaxValue if there is no map.
    float MapLowestY()
    {
        if (gridSystem == null) return float.MaxValue;
        float lowest = float.MaxValue;
        void Low(LevelNode n)
        {
            if (n?.cells == null) return;
            foreach (var c in n.cells) lowest = Mathf.Min(lowest, BlockTop(c).y - gridSystem.cellSize);
        }
        foreach (var n in _nodes) Low(n);
        foreach (var n in _gateOf.Keys) Low(n);
        return lowest;
    }

    // The ground a plot will occupy. The grove has no footprint of its own until
    // it is built — it is sized around the farm at build time — so its mist covers
    // the farm grown by most of the wood's depth, which is where it will stand —
    // grown ROUND (distance from the farm's rectangle), not as a bigger rectangle.
    IEnumerable<Vector3> PlotFootprint(MapDecorConfig cfg)
    {
        var o = cfg.origin;
        var e = (cfg.rotationSteps & 1) == 1 ? new Vector2Int(cfg.size.y, cfg.size.x) : cfg.size;
        int pad = 0;

        if (cfg is HarmonyGroveConfig g && decor != null)
        {
            o   = decor.origin;
            e   = (decor.rotationSteps & 1) == 1 ? new Vector2Int(decor.size.y, decor.size.x) : decor.size;
            pad = Mathf.CeilToInt(g.depth * 0.6f);
        }

        for (int x = -pad; x < e.x + pad; x++)
            for (int z = -pad; z < e.y + pad; z++)
            {
                int dx = Mathf.Max(0, Mathf.Max(-x, x - (e.x - 1)));
                int dz = Mathf.Max(0, Mathf.Max(-z, z - (e.y - 1)));
                if (dx * dx + dz * dz > pad * pad) continue;
                yield return BlockTop(new Vector3Int(o.x + x, o.y, o.z + z));
            }
    }

    bool HasMistFor(string gate) => gate != null && _mist.TryGetValue(gate, out var b) && b != null;

    // Sink the blocks that are about to rise. Deliberately LATE — after the level
    // markers and interactable spots have been placed — because both are positioned
    // from the logical grid height: a marker parented under a node that was ALREADY
    // sunk would be placed at full height and then carried up by the rise, ending up
    // hovering a whole rise-height above its block.
    //
    // The BLOCK itself never moves — its cubes do, each on its own clock (see
    // revealCubeInterval). Everything else on the block (level marker, clear
    // effect) is moved under one pivot that comes up with the cube it stands on.
    void SinkRisingRegions()
    {
        _riseItems.Clear();
        if (_rising.Count == 0 || gridSystem == null) return;
        float cs = gridSystem.cellSize;

        // Ground already standing, cell by cell — the wave starts from its edge.
        var standing = new List<Vector3>();
        foreach (var n in _nodes)
            if (n != null && n.cells != null && !_rising.Contains(n))
                foreach (var c in n.cells) standing.Add(gridSystem.GridToWorld(c));

        float Key(Vector3 p)
        {
            float d = 0f;
            if (standing.Count > 0)
            {
                d = float.MaxValue;
                foreach (var s in standing) d = Mathf.Min(d, (s - p).sqrMagnitude);
                d = Mathf.Sqrt(d);
            }
            // Stable per-position shuffle: the same map interleaves the same way
            // every time, but neighbours — even inside one block — don't go in step.
            float h = Mathf.Abs(Mathf.Sin(p.x * 12.9898f + p.y * 4.1414f + p.z * 78.233f) * 43758.5453f) % 1f;
            return d + h * revealShuffle * cs;
        }

        var cubes   = new List<(Transform t, float key)>();
        var pivotOf = new List<(Transform pivot, Transform cubeUnder)>();

        foreach (var n in _rising)
        {
            if (n == null || n.cells == null) continue;
            _risingRest[n] = n.transform.position;   // framing only — the node stays put

            var cellPos = new Dictionary<Vector3Int, Vector3>();
            foreach (var c in n.cells) cellPos[c] = gridSystem.GridToWorld(c);
            var top = TopCellOf(n);

            // Split the block's children: a child sitting exactly on one of its cells
            // is a cube (BlockRenderer places them there); anything else is standing
            // on the block.
            var kids = new List<Transform>();
            foreach (Transform k in n.transform) kids.Add(k);
            Transform topCube = null;
            var attached = new List<Transform>();
            foreach (var k in kids)
            {
                bool isCube = false;
                foreach (var kv in cellPos)
                    if ((k.position - kv.Value).sqrMagnitude < 1e-4f)
                    {
                        isCube = true;
                        cubes.Add((k, Key(kv.Value)));
                        if (kv.Key == top) topCube = k;
                        break;
                    }
                if (!isCube) attached.Add(k);
            }

            if (attached.Count > 0)
            {
                // The marker holds its rest RELATIVE TO ITS PARENT (MapLevelMarker),
                // so carrying it on a moving pivot moves it — sinking its own
                // transform would just be overwritten next frame.
                var pivot = new GameObject("RiseAttachments").transform;
                pivot.SetParent(n.transform, false);
                foreach (var k in attached) k.SetParent(pivot, worldPositionStays: true);
                pivotOf.Add((pivot, topCube));
            }
        }

        cubes.Sort((a, b) => a.key.CompareTo(b.key));
        float step = cubes.Count > 1 ? Mathf.Min(revealCubeInterval, revealMaxSpread / (cubes.Count - 1)) : 0f;
        var delayOf = new Dictionary<Transform, float>();
        for (int i = 0; i < cubes.Count; i++)
        {
            var t = cubes[i].t;
            delayOf[t] = i * step;
            _riseItems.Add((t, t.position, i * step));
        }
        // A block's pivot arrives with the cube it sits on — the badge rides up on
        // its own cube, not ahead of the block or after it.
        foreach (var (pivot, under) in pivotOf)
        {
            float dl = under != null && delayOf.TryGetValue(under, out var d0) ? d0 : 0f;
            _riseItems.Add((pivot, pivot.position, dl));
        }

        foreach (var it in _riseItems) it.t.position = it.rest + Vector3.down * revealRiseHeight;

        // An interactable standing on rising ground would hover over the hole until
        // the ground arrives under it.
        foreach (var kv in _spots)
            if (kv.Value != null && _risingCols.Contains(kv.Key)) kv.Value.gameObject.SetActive(false);
    }

    bool HasReveal(List<DecorPlot> plots) =>
        (plots != null && plots.Count > 0) || _rising.Count > 0 || HasMistFor(_growthLevelId);

    // One cutscene for everything a single clear revealed: every decor plot gated on
    // it AND every region waiting on it, rising together. Split into one cutscene
    // per thing, clearing 1-1 would mean watching the camera lurch between the farm,
    // the wood and the new level ground in turn.
    IEnumerator PlayRevealCutscene(List<DecorPlot> plots)
    {
        _decorCutscenePlaying = true;

        // Timing and framing come from the first plot when there is one; a reveal
        // that is only level ground borrows the farm's, the plot every map has.
        MapDecorConfig lead = plots.Count > 0 ? plots[0].cfg : decor;

        // Frame the lot.
        Vector3 focus = Vector3.zero; int count = 0;
        foreach (var p in plots)  { focus += p.center; count++; }
        foreach (var n in _rising) if (_risingRest.TryGetValue(n, out var rp)) { focus += rp; count++; }
        if (count > 0) focus /= count;

        if (_orbit != null && count > 0)
        {
            _orbit.focusViewport = new Vector2(0.5f, 0.5f);
            _orbit.FocusOnPoint(focus, snap: false);
            if (lead.growZoom > 0f) _orbit.SetZoom(lead.growZoom);
            _orbit.AddYaw(lead.growYawOffset);
        }

        string aside = null; float asideSeconds = 0f;
        foreach (var p in plots)
            if (!string.IsNullOrEmpty(p.cfg.growAsideText)) { aside = p.cfg.growAsideText; asideSeconds = p.cfg.growAsideSeconds; break; }
        if (aside != null) AsideBubble.Show(defaultCharacter, "default", aside, asideSeconds);

        // The mist goes first. The land waits `mistLead` and then comes up through
        // what is left of it — rising out of a thinning cloud, not through a solid
        // one, and not into clear air as if the mist had never been there.
        float lead0 = 0f;
        if (HasMistFor(_growthLevelId))
        {
            _mist[_growthLevelId].Disperse(mistDisperse);
            _mist.Remove(_growthLevelId);
            lead0 = mistLead;
        }

        // Everything rises on one clock. Plots rise whole; region blocks rise one by
        // one on their stagger, outward from the land that was already there.
        float plotDur  = plots.Count > 0 ? lead.growRiseDuration : 0f;
        float lastCube = 0f;
        foreach (var it in _riseItems) lastCube = Mathf.Max(lastCube, it.delay);
        float total = lead0 + Mathf.Max(plotDur, _riseItems.Count > 0 ? lastCube + revealCubeDuration : 0f);
        total = Mathf.Max(total, lead0 > 0f ? mistDisperse : 0f);

        var plotFrom = new List<Vector3>();
        foreach (var p in plots) plotFrom.Add(p.root != null ? p.root.transform.position : p.restPos);

        float t = 0f;
        while (t < total)
        {
            t += Time.deltaTime;

            for (int i = 0; i < plots.Count; i++)
            {
                var p = plots[i];
                if (p.root == null) continue;
                p.root.transform.position = Vector3.Lerp(plotFrom[i], p.restPos, EaseOut((t - lead0) / Mathf.Max(0.01f, plotDur)));
            }

            foreach (var it in _riseItems)
            {
                if (it.t == null) continue;
                float local = (t - lead0 - it.delay) / revealCubeDuration;
                it.t.position = Vector3.LerpUnclamped(it.rest + Vector3.down * revealRiseHeight, it.rest, EaseOutBack(local));
            }
            yield return null;
        }

        foreach (var p in plots)
        {
            if (p.root != null) p.root.transform.position = p.restPos;
            foreach (var r in p.residents) if (r != null) r.SetActive(true);   // the plot has arrived — its people with it
        }
        foreach (var it in _riseItems) if (it.t != null) it.t.position = it.rest;
        foreach (var kv in _spots)
            if (kv.Value != null && _risingCols.Contains(kv.Key)) kv.Value.gameObject.SetActive(true);

        // Camera waits for the line to finish (its clock started at the rise).
        float hold = lead.growHoldSeconds;
        if (aside != null) hold = Mathf.Max(hold, asideSeconds + AsideBubble.SlideSeconds - total);
        yield return new WaitForSeconds(hold);

        // Fade for the hand-off — focusViewport resets with no lerp of its own.
        yield return FadeScreen(0f, 1f, lead.transitionFadeDuration);

        if (_orbit != null)
        {
            _orbit.focusViewport = new Vector2(focusViewportX, focusViewportY);
            _orbit.FocusOnPoint(_camFocus, snap: true);
        }

        PlayEntryDialogueIfAny();   // may re-focus again (reward conversation) — still hidden

        yield return FadeScreen(1f, 0f, lead.transitionFadeDuration);
        _decorCutscenePlaying = false;
    }

    // Ease-out cubic, no overshoot.
    static float EaseOut(float x)
    {
        x = Mathf.Clamp01(x);
        return 1f - (1f - x) * (1f - x) * (1f - x);
    }

    // A block's rise: comes up a touch past its place and settles back — each one
    // lands with a small bump, which is what makes a sequence of them read as
    // pieces arriving rather than a surface sliding up. The standard back curve
    // (c1 = 1.70158) overshoots by 10%; blending toward it scales that to
    // `revealOvershoot`.
    float EaseOutBack(float x)
    {
        x = Mathf.Clamp01(x);
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float back = 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        float ease = EaseOut(x);
        return ease + (back - ease) * (revealOvershoot / 0.1f);
    }
}
