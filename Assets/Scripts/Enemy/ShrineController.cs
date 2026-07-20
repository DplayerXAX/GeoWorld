using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Per-level "Enlightenment Shrine" mechanic (LevelDefinition.shrineEnabled).
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
        if (RunConfig.Mode != GameMode.Level || RunConfig.Level == null || !RunConfig.Level.shrineEnabled) return;
        if (PlacementController.Instance == null) return;   // gameplay scene only
        if (FindFirstObjectByType<ShrineController>() != null) return;
        new GameObject("ShrineController").AddComponent<ShrineController>();
    }

    [Header("Visual (leave empty for a runtime fallback gold obelisk)")]
    public GameObject visualPrefab;

    public static ShrineController Instance { get; private set; }

    class Shrine { public GameObject go; public Vector3Int cell; }

    LevelDefinition _lv;
    readonly List<Shrine>        _shrines     = new();
    readonly HashSet<Vector3Int> _shrineCells = new();
    Xoshiro256StarStar _rng;

    void Awake() => Instance = this;

    void Start()
    {
        _lv = RunConfig.Level;
        if (_lv == null || !_lv.shrineEnabled) { Destroy(gameObject); return; }

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
        // Same 1-based "which wave is this" counter as chaosBlockStartWave / requiredWave.
        var gfm = GameFlowManager.Instance;
        if (gfm != null && gfm.UpcomingWaveNumber < Mathf.Max(1, _lv.shrineStartWave)) return;

        PruneDead();
        PruneUnsupported();
        if (_shrines.Count >= Mathf.Max(1, _lv.shrineMaxSimultaneous)) return;
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

        float frMult  = 1f + Mathf.Max(0f, _lv.shrineFireRateBonus);
        float dmgMult = 1f + Mathf.Max(0f, _lv.shrineDamageBonus);

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

    // ── Runtime fallback visual — a bright gold obelisk with a soft glow, so the
    // mechanic reads with zero art. Deliberately unlike ChaosBlock's black cube:
    // this is the boon, not the hazard. ──────────────────────────────────────
    static Material _fallbackMat;
    GameObject BuildFallbackVisual(float cellSize)
    {
        var root = new GameObject("ShrineVisual");

        var obelisk = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obelisk.transform.SetParent(root.transform, false);
        obelisk.transform.localScale    = new Vector3(0.34f, 1.1f, 0.34f) * cellSize;
        obelisk.transform.localPosition = Vector3.up * cellSize * 0.2f;
        if (obelisk.TryGetComponent<Collider>(out var col)) Destroy(col);

        if (_fallbackMat == null)
        {
            var sh = Shader.Find("GeoWorld/SilkscreenFlat")
                  ?? Shader.Find("Universal Render Pipeline/Lit")
                  ?? Shader.Find("Standard");
            _fallbackMat = new Material(sh) { hideFlags = HideFlags.DontSave };
        }
        var mr = obelisk.GetComponent<MeshRenderer>();
        mr.sharedMaterial     = _fallbackMat;
        mr.shadowCastingMode  = UnityEngine.Rendering.ShadowCastingMode.Off;
        MpbColor.Set(mr, GeoPalette.Gold);

        var lightGo = new GameObject("ShrineLight");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = Vector3.up * cellSize;
        var light = lightGo.AddComponent<Light>();
        light.type      = LightType.Point;
        light.color     = GeoPalette.Gold;
        light.range     = cellSize * 4.5f;
        light.intensity = 2.2f;

        root.AddComponent<ShrineBob>();
        return root;
    }

    // Gentle idle spin + bob so the shrine reads as "alive". Purely cosmetic; the
    // grid cell it occupies is fixed regardless of the visual's bob.
    class ShrineBob : MonoBehaviour
    {
        Vector3 _base;
        bool    _captured;
        void Update()
        {
            if (!_captured) { _base = transform.position; _captured = true; }   // capture AFTER SpawnOne positions us
            transform.Rotate(Vector3.up, 40f * Time.deltaTime, Space.World);
            transform.position = _base + Vector3.up * (Mathf.Sin(Time.time * 1.5f) * 0.08f);
        }
    }
}
