using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Per-level "Enlightenment Shrine" mechanic (enabled by adding a
// ShrineMechanicConfig to LevelDefinition.mechanics).
//
// At the start of each round a shrine SPROUTS on a free cell touching the
// existing build. It's immovable furniture: it occupies a grid cell but is NOT
// a PlacedBlockInstance, so pickup / sell / box-select never touch it. Every
// turret whose footprint touches a shrine (26-neighbour) gets a REVERSIBLE stat
// buff — a "borrowed" free upgrade (attack speed + damage) applied through
// TurretController's own shrine channel. The aura is re-asserted every frame
// from live adjacency, so a turret that leaves the aura (or whose shrine
// vanishes) is cleared automatically — no permanent stat is ever mutated.
//
// A shrine CLINGS to the build: if every block touching it is moved away so
// nothing is adjacent any more, the shrine simply vanishes (it does NOT veto the
// move the way a stranded turret does).
//
// Auto-spawns once per gameplay scene load — same hook pattern as
// ChaosBlockController / TutorialDirector — so no scene wiring is required.
[DisallowMultipleComponent]
public class ShrineController : MonoBehaviour
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
        if (RunConfig.Mode != GameMode.Level || RunConfig.Level == null) return;
        if (RunConfig.Level.GetMechanic<ShrineMechanicConfig>() == null) return;   // mechanic not added to this level
        if (PlacementController.Instance == null) return;   // gameplay scene only
        if (FindFirstObjectByType<ShrineController>() != null) return;
        new GameObject("ShrineController").AddComponent<ShrineController>();
    }

    [Header("Visual (leave empty for a runtime fallback gold obelisk)")]
    public GameObject visualPrefab;

    public static ShrineController Instance { get; private set; }

    class Shrine { public GameObject go; public Vector3Int cell; }

    LevelDefinition _lv;
    ShrineMechanicConfig _cfg;
    readonly List<Shrine>        _shrines     = new();
    readonly HashSet<Vector3Int> _shrineCells = new();
    Xoshiro256StarStar _rng;

    void Awake() => Instance = this;

    void Start()
    {
        _lv  = RunConfig.Level;
        _cfg = _lv != null ? _lv.GetMechanic<ShrineMechanicConfig>() : null;
        if (_cfg == null) { Destroy(gameObject); return; }

        GameFlowManager.OnTurnStarted += HandleTurnStarted;

        // Independent stream (salted off the level seed) so shrine placement never
        // perturbs the run's main RNG draw order — same convention ChaosBlock uses.
        ulong seed = _lv.runSeed != 0 ? _lv.runSeed ^ 0x53485249_4E450001UL : (ulong)System.DateTime.UtcNow.Ticks;
        _rng = new Xoshiro256StarStar(seed);
    }

    void OnDestroy()
    {
        GameFlowManager.OnTurnStarted -= HandleTurnStarted;
        ClearAllBuffs();   // never leave a turret boosted after the controller is gone
        if (Instance == this) Instance = null;
    }

    void HandleTurnStarted()
    {
        // Same 1-based "which wave is this" counter as ChaosBlockMechanicConfig.startWave / requiredWave.
        var gfm = GameFlowManager.Instance;
        if (gfm != null && gfm.UpcomingWaveNumber < Mathf.Max(1, _cfg.startWave)) return;

        PruneDead();
        PruneUnsupported();
        if (_shrines.Count >= Mathf.Max(1, _cfg.maxSimultaneous)) return;
        SpawnOne();
    }

    void Update()
    {
        PruneDead();          // GameObject destroyed for any external reason
        PruneUnsupported();   // lost every touching block → vanish
        ApplyAura();          // (re)assert buffs from current adjacency
    }

    // ── Spawn ───────────────────────────────────────────────────────────────
    void SpawnOne()
    {
        var grid = GridSystem.instance;
        if (grid == null) return;
        if (!TryPickSpawnCell(grid, out var cell)) return;

        Vector3 pos = grid.GridToWorld(cell);
        GameObject go = visualPrefab != null
            ? Instantiate(visualPrefab, pos, Quaternion.identity)
            : BuildFallbackVisual(grid.cellSize);
        go.name = "EnlightenmentShrine";
        go.transform.position = pos;

        grid.SetOccupied(cell);
        _shrineCells.Add(cell);
        _shrines.Add(new Shrine { go = go, cell = cell });
        // No EvaluateGrid() here — same as ChaosBlock: the graph is rebuilt fresh at
        // combat start (Run) and the live build-phase preview refreshes on the
        // player's next action, so forcing it mid-turn-start event is unnecessary.
    }

    // A free cell 26-adjacent to a random REAL block (never another shrine), so the
    // shrine sprouts touching the build and is immediately supported.
    bool TryPickSpawnCell(GridSystem grid, out Vector3Int result)
    {
        result = Vector3Int.zero;

        var blocks = new List<Vector3Int>();
        foreach (var kv in grid.GetGrid())
            if (!_shrineCells.Contains(kv.Key)) blocks.Add(kv.Key);
        if (blocks.Count == 0) return false;

        const int attempts = 40;
        for (int i = 0; i < attempts; i++)
        {
            var anchor = blocks[_rng.NextIntInclusive(0, blocks.Count - 1)];
            var off = new Vector3Int(
                _rng.NextIntInclusive(-1, 1),
                _rng.NextIntInclusive(-1, 1),
                _rng.NextIntInclusive(-1, 1));
            if (off == Vector3Int.zero) continue;

            var cand = anchor + off;
            if (grid.IsOccupied(cand)) continue;   // free cell only
            result = cand;
            return true;
        }
        return false;
    }

    // ── Support / pruning ───────────────────────────────────────────────────
    // Supported = at least one 26-neighbour cell is occupied by a REAL block
    // (another shrine doesn't count — two shrines can't prop each other up).
    bool HasSupport(Vector3Int cell)
    {
        var grid = GridSystem.instance;
        if (grid == null) return false;

        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dz = -1; dz <= 1; dz++)
        {
            if (dx == 0 && dy == 0 && dz == 0) continue;
            var n = new Vector3Int(cell.x + dx, cell.y + dy, cell.z + dz);
            if (grid.IsOccupied(n) && !_shrineCells.Contains(n)) return true;
        }
        return false;
    }

    void PruneUnsupported()
    {
        for (int i = _shrines.Count - 1; i >= 0; i--)
        {
            if (_shrines[i].go == null) continue;   // handled by PruneDead
            if (!HasSupport(_shrines[i].cell)) DestroyShrine(i);
        }
    }

    // A shrine's GameObject vanished out from under us (scene teardown, external
    // Destroy). Release its cell so the grid doesn't stay falsely occupied.
    void PruneDead()
    {
        for (int i = _shrines.Count - 1; i >= 0; i--)
            if (_shrines[i].go == null) ReleaseCell(i);
    }

    void DestroyShrine(int index)
    {
        var s = _shrines[index];
        if (s.go != null) Destroy(s.go);
        ReleaseCell(index);
    }

    void ReleaseCell(int index)
    {
        var s = _shrines[index];
        GridSystem.instance?.ClearOccupied(s.cell);
        _shrineCells.Remove(s.cell);
        _shrines.RemoveAt(index);
    }

    // ── Aura ────────────────────────────────────────────────────────────────
    void ApplyAura()
    {
        var grid = GridSystem.instance;
        if (grid == null) return;

        float frMult  = 1f + Mathf.Max(0f, _cfg.fireRateBonus);
        float dmgMult = 1f + Mathf.Max(0f, _cfg.damageBonus);

        foreach (var ins in grid.GetAllInstances())
        {
            if (ins?.data == null || ins.visualObject == null) continue;
            if (!TurretTypes.Is(ins.data.blockType)) continue;

            var tc = ins.visualObject.GetComponentInChildren<TurretController>();
            if (tc == null) continue;

            if (TouchesAnyShrine(ins.occupiedCells)) tc.SetShrineBuff(frMult, dmgMult);
            else                                     tc.SetShrineBuff(1f, 1f);   // out of every aura → clear
        }
    }

    bool TouchesAnyShrine(List<Vector3Int> cells)
    {
        if (cells == null || _shrineCells.Count == 0) return false;
        foreach (var c in cells)
            foreach (var sc in _shrineCells)
            {
                int cheb = Mathf.Max(Mathf.Abs(c.x - sc.x), Mathf.Abs(c.y - sc.y), Mathf.Abs(c.z - sc.z));
                if (cheb == 1) return true;   // exactly touching (any of the 26 neighbours)
            }
        return false;
    }

    void ClearAllBuffs()
    {
        var grid = GridSystem.instance;
        if (grid == null) return;
        foreach (var ins in grid.GetAllInstances())
        {
            if (ins?.data == null || ins.visualObject == null) continue;
            if (!TurretTypes.Is(ins.data.blockType)) continue;
            ins.visualObject.GetComponentInChildren<TurretController>()?.SetShrineBuff(1f, 1f);
        }
    }

    // ── Runtime fallback visual — a small stone ALTAR, not a spinning trophy.
    // The old version was a tall obelisk spinning at 40°/s on its own vertical
    // axis, which read as a collectible more than a place. A shrine is somewhere
    // you approach: a low, grounded dais the player can walk up to and click on,
    // with exactly one moving/glowing part (same "one accent, not a machine"
    // restraint as the LevelSelect start monument) — a slow-turning gold rim
    // cradling a Custom/SacredMarker glow orb. The dais and altar top themselves
    // never move. ─────────────────────────────────────────────────────────────
    static Material _fallbackMat;
    GameObject BuildFallbackVisual(float cellSize)
    {
        var root = new GameObject("ShrineVisual");

        // Dais — wide and short, the footing the altar reads as standing ON.
        MakePlate(root.transform, "Dais",
                  new Vector3(0.62f, 0.10f, 0.62f) * cellSize,
                  Vector3.up * (cellSize * 0.05f),
                  Quaternion.identity, GeoPalette.Ink);

        // Altar top — smaller, stepped up — the "surface" an offering/upgrade
        // would sit on. Gold, since this is the boon-granting mechanic (Shrine),
        // deliberately distinct from ChaosBlock's near-black hazard palette.
        MakePlate(root.transform, "AltarTop",
                  new Vector3(0.36f, 0.10f, 0.36f) * cellSize,
                  Vector3.up * (cellSize * 0.18f),
                  Quaternion.identity, GeoPalette.Gold);

        // Rim — thin gold square OUTLINE resting just above the altar top. The
        // one moving part; slow enough to read as ceremonial, not mechanical.
        var rim = MakeSquareFrame(root.transform, "Rim", cellSize * 0.22f, cellSize * 0.02f, GeoPalette.Gold);
        rim.localPosition = Vector3.up * (cellSize * 0.30f);

        // Glow orb — Custom/SacredMarker, same shader the LevelSelect start
        // monument uses, so "this is a sacred/special point" reads consistently
        // across the menu and gameplay layers. Soft aerogel haze + a tight bright
        // core, tuned not to blow out the way the old raw point-light did.
        var orb = MakeGlowOrb(root.transform, cellSize * 0.30f, cellSize * 0.46f);

        // Collider so the shrine is clickable (see ShrineUnit) — sized to the
        // dais footprint, one cell tall, so it doesn't poke into neighbours.
        var hitBox = root.AddComponent<BoxCollider>();
        hitBox.center = Vector3.up * (cellSize * 0.35f);
        hitBox.size   = new Vector3(cellSize * 0.9f, cellSize * 0.9f, cellSize * 0.9f);

        root.AddComponent<ShrineUnit>();
        root.AddComponent<ShrineBob>().Init(rim, orb);
        return root;
    }

    // One flat box plate — same "single primitive, keeps the silhouette
    // designed" convention the LevelSelect start monument uses.
    Transform MakePlate(Transform parent, string name, Vector3 scale, Vector3 localPos,
                        Quaternion localRot, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        if (go.TryGetComponent<Collider>(out var col)) Destroy(col);   // the root's own BoxCollider handles clicks
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        go.transform.localScale    = scale;

        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial    = FallbackMaterial();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        MpbColor.Set(mr, color);
        return go.transform;
    }

    // Four plates as a square OUTLINE, not a filled quad — the negative space in
    // the middle is the point, matching the start monument's frames.
    Transform MakeSquareFrame(Transform parent, string name, float half, float thick, Color color)
    {
        var frame = new GameObject(name);
        frame.transform.SetParent(parent, false);

        float len = half * 2f + thick;
        MakePlate(frame.transform, "N", new Vector3(len,   thick, thick), new Vector3(0f, 0f,  half), Quaternion.identity, color);
        MakePlate(frame.transform, "S", new Vector3(len,   thick, thick), new Vector3(0f, 0f, -half), Quaternion.identity, color);
        MakePlate(frame.transform, "E", new Vector3(thick, thick, len),   new Vector3( half, 0f, 0f), Quaternion.identity, color);
        MakePlate(frame.transform, "W", new Vector3(thick, thick, len),   new Vector3(-half, 0f, 0f), Quaternion.identity, color);
        return frame.transform;
    }

    static Material _glowMat;
    Transform MakeGlowOrb(Transform parent, float diameter, float localHeight)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Glow";
        if (go.TryGetComponent<Collider>(out var col)) Destroy(col);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.up * localHeight;
        go.transform.localScale    = Vector3.one * diameter;

        if (_glowMat == null)
        {
            var sh = Shader.Find("Custom/SacredMarker");
            _glowMat = sh != null ? new Material(sh) { hideFlags = HideFlags.DontSave } : null;
        }

        var mr = go.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows    = false;
        if (_glowMat != null)
        {
            var inst = new Material(_glowMat);
            inst.SetColor("_HazeColor", new Color(1.00f, 0.86f, 0.55f));
            inst.SetColor("_CoreColor", new Color(1.00f, 0.96f, 0.82f));
            mr.sharedMaterial = inst;
        }
        else
        {
            mr.sharedMaterial = FallbackMaterial();   // shader missing from the build — still shows SOMETHING
            MpbColor.Set(mr, GeoPalette.Gold);
        }
        return go.transform;
    }

    Material FallbackMaterial()
    {
        if (_fallbackMat != null) return _fallbackMat;
        var sh = Shader.Find("GeoWorld/SilkscreenFlat")
              ?? Shader.Find("Universal Render Pipeline/Lit")
              ?? Shader.Find("Standard");
        _fallbackMat = new Material(sh) { hideFlags = HideFlags.DontSave };
        return _fallbackMat;
    }

    // Rim turns slowly, orb bobs + breathes (SacredMarker's own shader pulse
    // carries most of the glow animation — this only handles motion). The dais
    // and altar top are NEVER touched here — they stay put, which is what makes
    // this read as a fixed shrine rather than a spinning pickup.
    class ShrineBob : MonoBehaviour
    {
        Transform _rim, _orb;
        Vector3   _orbBase;

        public void Init(Transform rim, Transform orb)
        {
            _rim = rim; _orb = orb;
            if (_orb != null) _orbBase = _orb.localPosition;
        }

        void Update()
        {
            if (_rim != null) _rim.Rotate(0f, 16f * Time.deltaTime, 0f, Space.Self);
            if (_orb != null)
                _orb.localPosition = _orbBase + Vector3.up * (Mathf.Sin(Time.time * 1.3f) * 0.05f);
        }
    }
}

// Click-to-inspect identity marker — mirrors ChaosBlockUnit's role for the
// read-only selection panel (see PlacementController.TrySelectObject /
// BuildShrineBody), but carries no state of its own: every shrine grants the
// same aura off the same ShrineMechanicConfig, so there's nothing per-instance
// to show.
[DisallowMultipleComponent]
public class ShrineUnit : MonoBehaviour
{
}
