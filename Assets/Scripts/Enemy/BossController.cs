using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Boss mechanic (enabled by adding a BossMechanicConfig to LevelDefinition.mechanics).
//
// One large enemy that perches ON the board's blocks and stays across rounds until
// it is destroyed:
//   • Perches — chosen on arrival, spread across the build (block tops with open
//     air above). Each round it teleports to the next; the next perch is marked
//     during the build phase, so the player can set turrets where it will land.
//   • Laser — in combat, every laserInterval seconds it picks a live synergy set,
//     shows an aim line for laserCharge seconds, then fires. A hit switches that
//     synergy off (SynergyEvaluator.SetSuppressed) until the wave ends; a block
//     standing between its eye and the set takes the beam instead.
//   • It's an EnemySurfaceUnit registered with EnemyBaseManager as a persistent
//     target — turrets shoot it like any enemy, it never holds up a wave, and it
//     survives the per-round reset. Killing it sets Defeated, which the DefeatBoss
//     objective reads.
//
// Auto-spawns itself on a gameplay scene load whose level carries the mechanic —
// same hook as ChaosBlockController — so no scene wiring is required. Everything it
// draws is built at runtime (a template look; swap in art via visualPrefab).
[DisallowMultipleComponent]
public class BossController : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TrySpawn();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TrySpawn();

    static void TrySpawn()
    {
        Defeated = false;
        if (RunConfig.Mode != GameMode.Level || RunConfig.Level == null) return;
        if (RunConfig.Level.GetMechanic<BossMechanicConfig>() == null) return;
        if (PlacementController.Instance == null) return;   // gameplay scene only
        if (FindFirstObjectByType<BossController>() != null) return;
        new GameObject("BossController").AddComponent<BossController>();
    }

    [Tooltip("Optional art. Leave empty for the runtime template look.")]
    public GameObject visualPrefab;

    public static BossController Instance { get; private set; }
    /// <summary>The level's boss has been destroyed (the DefeatBoss objective).</summary>
    public static bool Defeated { get; private set; }
    public EnemySurfaceUnit Unit => _unit;

    BossMechanicConfig _cfg;
    EnemySurfaceUnit   _unit;
    Transform _root, _body, _ring, _eye;
    GameObject _marker;
    Material _flatMat, _beamMat;
    LineRenderer _line;

    // A place the boss can stand: where it hovers, the air cells it fills while
    // there (nothing can be built into them), and the pad under it, if any.
    struct Perch { public Vector3 at; public Vector3Int[] air; public int pad; }
    readonly List<Perch> _perches = new();
    int  _perchIndex = -1;
    Vector3Int[] _held = new Vector3Int[0];   // air cells occupied right now

    // The level's pads (BossMechanicConfig.usePads): the blocks under each, and
    // their renderers + resting colour for the next-pad glow.
    class Pad { public PlacedBlockInstance ins; public Renderer[] rends; public Color color; }
    readonly List<Pad> _pads = new();
    bool _padsLaid;
    bool _spawned, _combatHooked, _spawnFailed;

    float _laserTimer, _chargeT = -1f, _teleportTimer;
    ActiveSynergy _target;
    readonly List<(ActiveSynergy active, float until)> _severed = new();

    void Awake() => Instance = this;

    void Start()
    {
        _cfg = RunConfig.Level != null ? RunConfig.Level.GetMechanic<BossMechanicConfig>() : null;
        if (_cfg == null) { Destroy(gameObject); return; }
        GameFlowManager.OnTurnStarted += HandleTurnStarted;
    }

    void OnDestroy()
    {
        GameFlowManager.OnTurnStarted -= HandleTurnStarted;
        if (EnemyBaseManager.Instance != null) EnemyBaseManager.Instance.OnWaveCompleted -= HandleWaveEnded;
        ReleaseAll();
        if (_flatMat != null) Destroy(_flatMat);
        if (_beamMat != null) Destroy(_beamMat);
        if (Instance == this) Instance = null;
    }

    // ── Rounds ───────────────────────────────────────────────────────────────

    void HandleTurnStarted()
    {
        var gfm = GameFlowManager.Instance;
        if (!_spawned)
        {
            _spawnFailed = false;
            if (gfm == null || gfm.UpcomingWaveNumber >= Mathf.Max(1, _cfg.startWave)) Spawn();
            return;
        }
        if (_unit == null) return;
        if (_cfg.teleportEachRound) TeleportToNext();
        ShowNextMarker();
    }

    void HandleWaveEnded()
    {
        CancelCharge();
        // "Until the wave ends" severings come back for the build phase.
        for (int i = _severed.Count - 1; i >= 0; i--)
            if (_severed[i].until >= float.MaxValue) Release(i);
    }

    void Update()
    {
        if (!_combatHooked && EnemyBaseManager.Instance != null)
        {
            EnemyBaseManager.Instance.OnWaveCompleted += HandleWaveEnded;
            _combatHooked = true;
        }
        // The first turn can start before this component has subscribed (script
        // start order) — so it also arrives from here, on the first build frame.
        if (!_spawned)
        {
            var gfm = GameFlowManager.Instance;
            if (gfm != null && gfm.phase == GamePhase.Build && gfm.UpcomingWaveNumber >= Mathf.Max(1, _cfg.startWave)
                && !_spawnFailed)
            {
                Spawn();
                if (!_spawned) _spawnFailed = true;   // nowhere to stand yet — the next turn start retries
            }
            return;
        }
        if (_unit == null) return;

        Animate();

        for (int i = _severed.Count - 1; i >= 0; i--)
            if (Time.time >= _severed[i].until) Release(i);

        bool combat = EnemyBaseManager.Instance != null && EnemyBaseManager.Instance.WaveActive;
        if (_marker != null) _marker.SetActive(!combat && _perches.Count > 1 && _pads.Count == 0);
        if (!combat) { CancelCharge(); return; }

        if (_cfg.teleportInCombatSeconds > 0f)
        {
            _teleportTimer += Time.deltaTime;
            if (_teleportTimer >= _cfg.teleportInCombatSeconds && _chargeT < 0f)
            {
                _teleportTimer = 0f;
                TeleportToNext();
            }
        }
        UpdateLaser(Time.deltaTime);
    }

    // ── Arrival ──────────────────────────────────────────────────────────────

    void Spawn()
    {
        var grid = GridSystem.instance;
        if (grid == null) return;
        if (_cfg.usePads && !_padsLaid) { _padsLaid = true; LayPads(grid); }
        ChoosePerches(grid);
        if (_perches.Count == 0) return;

        BuildVisual(grid.cellSize);
        _unit = _root.gameObject.AddComponent<EnemySurfaceUnit>();
        _unit.SetMaxHealth(Mathf.Max(1, _cfg.health));
        _unit.rewardOnKill = 0;
        _unit.OnDied += HandleDied;

        _spawned = true;
        _perchIndex = 0;
        PlaceAt(_perches[0], grid, instant: true);
        EnemyBaseManager.Instance?.RegisterPersistentTarget(_unit);
        PlacementController.Instance?.ShowPlacementPopup($"{_cfg.displayName} has come.");
        ShowNextMarker();
    }

    // The pads: 2×2 fixed blocks round the midpoint of the first start and end,
    // evenly spaced on a circle and each clear of everything else. Heresy's colour
    // but no synergy tag — they're the boss's ground, not the player's pieces.
    void LayPads(GridSystem grid)
    {
        var gfm = GameFlowManager.Instance;
        var pc  = PlacementController.Instance;
        if (gfm == null || pc == null || gfm.AllStarts.Count == 0 || gfm.AllEnds.Count == 0) return;

        BlockData padData = null;
        if (pc.blocks != null)
            foreach (var b in pc.blocks)
                if (b != null && b.blockShape == BlockShape.O2x2 && !TurretTypes.Is(b.blockType)) { padData = b; break; }
        if (padData == null) { Debug.LogWarning("[Boss] no 2×2 block in PlacementController.blocks — no pads, perching on the build instead."); return; }

        Vector3 mid = ((Vector3)gfm.AllStarts[0] + (Vector3)gfm.AllEnds[0]) * 0.5f;
        int floor = Mathf.Min(gfm.AllStarts[0].y, gfm.AllEnds[0].y);
        Color col = _cfg.padColor.a > 0f ? _cfg.padColor : BlockColorPalette.Get(BlockColor.Heresy);
        float phase = Random.value * Mathf.PI * 2f;

        for (int i = 0; i < _cfg.padCount; i++)
        {
            float a = phase + i / (float)_cfg.padCount * Mathf.PI * 2f;
            // Out along this spoke until a clear 2×2 turns up.
            for (float r = _cfg.padRadius; r < _cfg.padRadius + 8f; r += 1f)
            {
                var min = new Vector3Int(Mathf.RoundToInt(mid.x + Mathf.Cos(a) * r) - 1, floor,
                                         Mathf.RoundToInt(mid.z + Mathf.Sin(a) * r) - 1);
                var cells = new[] { min, min + Vector3Int.right, min + new Vector3Int(0, 0, 1), min + new Vector3Int(1, 0, 1) };
                if (!Clear(grid, cells)) continue;

                var ins = pc.PlaceBlockDirect(padData, cells, Quaternion.identity, col, BlockColor.None);
                if (ins == null) break;
                ins.locked = true;
                ResourceManager.Instance?.OnBlockPlaced(padData.blockType);
                if (ins.visualObject != null) gfm.startingLayoutVisuals.Add(ins.visualObject);
                _pads.Add(new Pad
                {
                    ins = ins,
                    rends = ins.visualObject != null ? ins.visualObject.GetComponentsInChildren<Renderer>() : new Renderer[0],
                    color = col,
                });
                break;
            }
        }
        if (_pads.Count > 0) gfm.EvaluateGrid();
    }

    // Free, with the cells round it free too (and the air above), so a pad never
    // joins onto anything.
    static bool Clear(GridSystem grid, Vector3Int[] cells)
    {
        foreach (var c in cells)
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = 0; dy <= 2; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                        if (grid.IsOccupied(c + new Vector3Int(dx, dy, dz))) return false;
        return true;
    }

    // With pads: stand over each pad's middle, filling the four air cells above it.
    // Without: block tops with open air above (the boss needs room), spread across
    // the build by farthest-point sampling — so its perches cover the whole base,
    // and each one is somewhere a turret could be set up to meet it.
    void ChoosePerches(GridSystem grid)
    {
        _perches.Clear();
        if (_pads.Count > 0)
        {
            for (int i = 0; i < _pads.Count; i++)
            {
                var cells = _pads[i].ins.occupiedCells;
                var air = new Vector3Int[cells.Count];
                Vector3 at = Vector3.zero;
                for (int k = 0; k < cells.Count; k++) { air[k] = cells[k] + Vector3Int.up; at += grid.GridToWorld(air[k]); }
                _perches.Add(new Perch { at = at / cells.Count, air = air, pad = i });
            }
            return;
        }

        var cand = new List<Vector3Int>();
        var seen = new HashSet<Vector3Int>();
        foreach (var ins in grid.GetAllInstances())
        {
            if (ins?.data == null || TurretTypes.Is(ins.data.blockType)) continue;
            foreach (var c in ins.occupiedCells)
            {
                var air = c + Vector3Int.up;
                if (grid.IsOccupied(air) || grid.IsOccupied(air + Vector3Int.up)) continue;
                if (seen.Add(air)) cand.Add(air);
            }
        }
        if (cand.Count == 0) return;

        cand.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y != b.y ? a.y.CompareTo(b.y) : a.z.CompareTo(b.z));
        Vector3 centre = Vector3.zero;
        foreach (var c in cand) centre += (Vector3)c;
        centre /= cand.Count;

        // Start at the candidate furthest from the middle, then keep taking the one
        // furthest from everything already taken.
        Vector3Int first = cand[0];
        float best = -1f;
        foreach (var c in cand) { float d = ((Vector3)c - centre).sqrMagnitude; if (d > best) { best = d; first = c; } }
        var taken = new List<Vector3Int> { first };
        while (taken.Count < Mathf.Min(_cfg.perchCount, cand.Count))
        {
            Vector3Int pick = cand[0]; float far = -1f;
            foreach (var c in cand)
            {
                float near = float.MaxValue;
                foreach (var p in taken) near = Mathf.Min(near, ((Vector3)(c - p)).sqrMagnitude);
                if (near > far) { far = near; pick = c; }
            }
            if (far <= 0f) break;
            taken.Add(pick);
        }
        foreach (var c in taken)
            _perches.Add(new Perch { at = grid.GridToWorld(c), air = new[] { c }, pad = -1 });
    }

    // ── Teleport ─────────────────────────────────────────────────────────────

    void TeleportToNext()
    {
        var grid = GridSystem.instance;
        if (grid == null || _perches.Count == 0) return;
        // A perch the player has built over (or taken away the ground under) is
        // replaced before use.
        // The next perch in turn that's still usable (the player may have built on
        // a pad, or taken the ground from under a build perch). Build perches that
        // have all gone are re-chosen; blocked pads are skipped.
        for (int tries = 0; tries < _perches.Count; tries++)
        {
            int next = (_perchIndex + 1 + tries) % _perches.Count;
            if (!PerchUsable(_perches[next], grid)) continue;
            _perchIndex = next;
            PlaceAt(_perches[_perchIndex], grid, instant: false);
            return;
        }
        if (_pads.Count == 0)
        {
            ChoosePerches(grid);
            if (_perches.Count > 0) { _perchIndex = 0; PlaceAt(_perches[0], grid, instant: false); }
        }
    }

    bool PerchUsable(Perch p, GridSystem grid)
    {
        foreach (var c in p.air)
        {
            if (System.Array.IndexOf(_held, c) >= 0) continue;   // it's where the boss already is
            if (grid.IsOccupied(c)) return false;
            if (p.pad < 0 && !grid.IsOccupied(c + Vector3Int.down)) return false;
        }
        return true;
    }

    void PlaceAt(Perch p, GridSystem grid, bool instant)
    {
        foreach (var c in _held) grid.ClearOccupied(c);
        _held = p.air;
        foreach (var c in _held)
        {
            grid.SetOccupied(c);
            grid.SetNoSupport(c);   // nothing can be built resting on the boss
        }

        Vector3 to = p.at + Vector3.up * (_cfg.hover * grid.cellSize);
        if (instant || !isActiveAndEnabled) _root.position = to;
        else StartCoroutine(Blink(to));
    }

    // Shrinks out, reappears at the perch, swells back.
    IEnumerator Blink(Vector3 to)
    {
        const float half = 0.3f;
        for (float t = 0f; t < half && _root != null; t += Time.deltaTime)
        { _root.localScale = Vector3.one * (1f - t / half); yield return null; }
        if (_root == null) yield break;
        _root.position = to;
        for (float t = 0f; t < half && _root != null; t += Time.deltaTime)
        { _root.localScale = Vector3.one * (t / half); yield return null; }
        if (_root != null) _root.localScale = Vector3.one;
    }

    // Where it will go next: on pads, that pad glows (see Animate); on the build,
    // a ring is drawn on that block's top during the build phase.
    int NextPerch => _perches.Count > 0 ? (_perchIndex + 1) % _perches.Count : -1;

    void ShowNextMarker()
    {
        var grid = GridSystem.instance;
        if (grid == null || _perches.Count < 2 || _marker == null || _pads.Count > 0) return;
        _marker.transform.position = _perches[NextPerch].at + Vector3.down * (grid.cellSize * 0.47f);
    }

    // ── Laser ────────────────────────────────────────────────────────────────

    void UpdateLaser(float dt)
    {
        if (_chargeT < 0f)
        {
            _laserTimer += dt;
            if (_laserTimer < _cfg.laserInterval) return;
            _laserTimer = 0f;
            _target = PickTarget();
            if (_target == null) return;
            _chargeT = 0f;
        }

        if (_target == null || _target.Suppressed) { CancelCharge(); return; }

        _chargeT += dt;
        Vector3 from = EyePosition(), to = TargetPoint(_target);
        if (_eye != null) _eye.rotation = Quaternion.LookRotation(to - _root.position);

        // Aim line: thin and flickering, brighter as it comes due.
        float k = Mathf.Clamp01(_chargeT / _cfg.laserCharge);
        DrawBeam(from, to, Mathf.Lerp(0.03f, 0.08f, k), (0.35f + 0.65f * k) * (0.6f + 0.4f * Mathf.Sin(Time.time * 30f)));

        if (_chargeT >= _cfg.laserCharge) Fire(from, to);
    }

    void Fire(Vector3 from, Vector3 to)
    {
        var grid  = GridSystem.instance;
        var target = _target;
        CancelCharge();
        if (grid == null || target == null) return;

        var blocker = FirstBlocker(grid, from, to, target);
        if (blocker.HasValue)
        {
            Vector3 hit = grid.GridToWorld(blocker.Value);
            StartCoroutine(Flash(from, hit, 0.35f));
            if (_cfg.blockerDestroyed) DestroyBlock(grid, blocker.Value);
            PlacementController.Instance?.ShowPlacementPopup("A block took the beam.");
            return;
        }

        StartCoroutine(Flash(from, to, 0.35f));
        SynergyEvaluator.Instance?.SetSuppressed(target, true);
        _severed.Add((target, _cfg.suppressSeconds > 0f ? Time.time + _cfg.suppressSeconds : float.MaxValue));
        PlacementController.Instance?.ShowPlacementPopup(
            $"{_cfg.displayName} severed {(target.rule != null ? target.rule.displayName : "a synergy")}!");
    }

    // A live synergy within range that isn't already severed — the nearest one,
    // so the threat is readable: it goes for what's closest to where it stands.
    ActiveSynergy PickTarget()
    {
        var ev = SynergyEvaluator.Instance;
        if (ev == null) return null;
        ActiveSynergy best = null;
        float bestD = float.MaxValue;
        float range = _cfg.laserRange * (GridSystem.instance != null ? GridSystem.instance.cellSize : 1f);
        foreach (var a in ev.Actives)
        {
            if (a == null || a.Suppressed || a.claimedPieces == null || a.claimedPieces.Count == 0) continue;
            float d = Vector3.Distance(_root.position, TargetPoint(a));
            if (d > range || d >= bestD) continue;
            best = a; bestD = d;
        }
        return best;
    }

    static Vector3 TargetPoint(ActiveSynergy a)
    {
        var grid = GridSystem.instance;
        Vector3 sum = Vector3.zero; int n = 0;
        if (grid != null && a?.claimedPieces != null)
            foreach (var p in a.claimedPieces)
                if (p?.cells != null)
                    foreach (var c in p.cells) { sum += grid.GridToWorld(c); n++; }
        return n > 0 ? sum / n : Vector3.zero;
    }

    Vector3 EyePosition() => _eye != null ? _eye.position : _root.position;

    // Walks the beam through the grid. The first occupied cell that isn't the
    // boss's own, its perch, or part of the targeted set is what takes the hit.
    Vector3Int? FirstBlocker(GridSystem grid, Vector3 from, Vector3 to, ActiveSynergy target)
    {
        var ignore = new HashSet<Vector3Int>();
        foreach (var c in _held) { ignore.Add(c); ignore.Add(c + Vector3Int.down); }
        if (target.claimedPieces != null)
            foreach (var p in target.claimedPieces)
                if (p?.cells != null) foreach (var c in p.cells) ignore.Add(c);

        float step = grid.cellSize * 0.2f;
        float len  = Vector3.Distance(from, to);
        Vector3 dir = (to - from) / Mathf.Max(len, 1e-4f);
        var last = new Vector3Int(int.MinValue, 0, 0);
        for (float s = 0f; s < len; s += step)
        {
            var cell = grid.WorldToGrid(from + dir * s);
            if (cell == last) continue;
            last = cell;
            if (ignore.Contains(cell)) continue;
            if (grid.IsOccupied(cell)) return cell;
        }
        return null;
    }

    void DestroyBlock(GridSystem grid, Vector3Int cell)
    {
        var ins = grid.GetInstanceAt(cell);
        if (ins == null || ins.locked) return;
        BlockDissolveFx.Play(ins.visualObject, 1.2f);
        var cells = ins.occupiedCells.ToArray();
        ResourceManager.Instance?.OnBlockRemoved(ins.data.blockType);
        SynergyEvaluator.Instance?.OnPieceRemoved(ins.placedPiece);
        PathFlowManager.Instance?.RemoveFlowsOverlapping(cells);
        LoopManager.Instance?.RemoveLoopsOverlapping(cells);
        grid.RemoveInstance(ins);
        GameFlowManager.Instance?.EvaluateGrid();
    }

    void CancelCharge()
    {
        _chargeT = -1f;
        _target = null;
        if (_line != null) _line.enabled = false;
    }

    IEnumerator Flash(Vector3 from, Vector3 to, float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float k = 1f - t / seconds;
            DrawBeam(from, to, Mathf.Lerp(0.05f, 0.32f, k), k);
            yield return null;
        }
        if (_line != null && _chargeT < 0f) _line.enabled = false;
    }

    void DrawBeam(Vector3 from, Vector3 to, float widthCells, float alpha)
    {
        if (_line == null) return;
        float cs = GridSystem.instance != null ? GridSystem.instance.cellSize : 1f;
        _line.enabled = true;
        _line.SetPosition(0, from);
        _line.SetPosition(1, to);
        _line.startWidth = _line.endWidth = widthCells * cs;
        // URP Unlit ignores vertex colour — the beam's colour and fade go on the material.
        var c = _cfg.laserColor; c.a = Mathf.Clamp01(alpha);
        if (_beamMat != null) _beamMat.SetColor("_BaseColor", c);
    }

    void Release(int i)
    {
        var (a, _) = _severed[i];
        _severed.RemoveAt(i);
        if (a != null) SynergyEvaluator.Instance?.SetSuppressed(a, false);
    }

    void ReleaseAll()
    {
        for (int i = _severed.Count - 1; i >= 0; i--) Release(i);
    }

    // ── Death ────────────────────────────────────────────────────────────────

    void HandleDied(EnemySurfaceUnit died)
    {
        if (died != null) died.OnDied -= HandleDied;
        Defeated = true;
        CancelCharge();
        ReleaseAll();
        EnemyBaseManager.Instance?.UnregisterPersistentTarget(died);
        if (GridSystem.instance != null) foreach (var c in _held) GridSystem.instance.ClearOccupied(c);
        _held = new Vector3Int[0];
        foreach (var pad in _pads) foreach (var r in pad.rends) if (r != null) MpbColor.Set(r, pad.color);
        if (_marker != null) Destroy(_marker);
        PlacementController.Instance?.ShowPlacementPopup($"{_cfg.displayName} is destroyed!");
        if (_root != null) BlockDissolveFx.Play(_root.gameObject, 1.6f);
        if (died != null) Destroy(died.gameObject);
        _unit = null;
    }

    // ── Look (template) ──────────────────────────────────────────────────────

    // Idle: the body turns slowly, the ring of shards orbits, the whole thing bobs.
    void Animate()
    {
        if (_body != null) _body.Rotate(0f, 20f * Time.deltaTime, 0f, Space.World);
        if (_ring != null) _ring.Rotate(0f, -45f * Time.deltaTime, 0f, Space.Self);
        if (_marker != null) _marker.transform.Rotate(0f, 60f * Time.deltaTime, 0f, Space.World);

        // The next pad breathes with light.
        if (_pads.Count > 0 && _perches.Count > 1)
        {
            int nextPad = _perches[NextPerch].pad;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f);
            for (int i = 0; i < _pads.Count; i++)
            {
                var pad = _pads[i];
                var c = i == nextPad ? Color.Lerp(pad.color, _cfg.padGlow, 0.35f + 0.5f * pulse) : pad.color;
                foreach (var r in pad.rends) if (r != null) MpbColor.Set(r, c);
            }
        }
    }

    void BuildVisual(float cs)
    {
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh != null) _flatMat = new Material(sh) { name = "Boss (runtime)" };

        if (visualPrefab != null)
        {
            _root = Instantiate(visualPrefab).transform;
        }
        else
        {
            _root = new GameObject("Boss").transform;
            float s = _cfg.size * cs;

            _body = Part(PrimitiveType.Cube, _root, Vector3.zero, Vector3.one * (s * 0.62f), _cfg.bodyColor);
            _body.localRotation = Quaternion.Euler(45f, 0f, 45f);   // a diamond

            _ring = new GameObject("Ring").transform;
            _ring.SetParent(_root, false);
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f;
                var shard = Part(PrimitiveType.Cube, _ring, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (s * 0.62f),
                                 new Vector3(0.14f, 0.34f, 0.14f) * s, _cfg.ringColor);
                shard.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 18f);
            }

            _eye = new GameObject("Eye").transform;
            _eye.SetParent(_root, false);
            Part(PrimitiveType.Sphere, _eye, new Vector3(0f, 0f, s * 0.36f), Vector3.one * (s * 0.24f), _cfg.eyeColor);
        }

        // One trigger box for turret fire (bullets auto-collide enemies by trigger).
        var box = _root.gameObject.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = Vector3.one * (_cfg.size * cs);

        // Next-perch marker: a flat ring on the block top.
        _marker = new GameObject("BossNextPerch");
        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * Mathf.PI * 2f;
            var t = Part(PrimitiveType.Cube, _marker.transform, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (cs * 0.55f),
                         new Vector3(0.28f, 0.04f, 0.08f) * cs, _cfg.ringColor);
            t.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f);
        }
        _marker.SetActive(false);

        // Beam.
        var lineGo = new GameObject("BossBeam");
        lineGo.transform.SetParent(transform, false);
        _line = lineGo.AddComponent<LineRenderer>();
        _line.positionCount = 2;
        _line.useWorldSpace = true;
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _line.receiveShadows = false;
        if (sh != null)
        {
            _beamMat = new Material(sh) { name = "BossBeam (runtime)" };
            MakeAdditive(_beamMat);
            _line.sharedMaterial = _beamMat;
        }
        _line.enabled = false;
    }

    Transform Part(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        if (go.TryGetComponent<Collider>(out var col)) Destroy(col);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        var r = go.GetComponent<MeshRenderer>();
        if (_flatMat != null) r.sharedMaterial = _flatMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        MpbColor.Set(r, color);
        return go.transform;
    }

    // URP Unlit → additive, vertex colour carrying the line's colour and fade.
    static void MakeAdditive(Material m)
    {
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 2f);   // additive
        m.SetFloat("_ZWrite", 0f);
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
    }
}
