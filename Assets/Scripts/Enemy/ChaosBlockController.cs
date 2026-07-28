using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Per-level obstacle mechanic (enabled by adding a ChaosBlockMechanicConfig to
// LevelDefinition.mechanics): spawns one black "Chaos Block" on the build grid
// at the start of EVERY round (Build phase). It's a stationary EnemySurfaceUnit
// — destructible by turrets during combat exactly like a normal enemy (see
// ChaosBlockUnit for the visual side) — registered with EnemyBaseManager as a
// PERSISTENT target (see EnemyBaseManager.RegisterPersistentTarget) so it never
// blocks wave completion and survives CancelWave()'s per-round reset. Any
// chaos block still alive when combat ends drains currencyDrain block
// currency; since a survivor isn't removed, ignoring them lets the threat
// (and the tax) pile up round over round until someone kills them.
//
// Auto-spawns itself once per gameplay scene load — same hook pattern as
// TutorialDirector — so no scene wiring is required.
[DisallowMultipleComponent]
public class ChaosBlockController : MonoBehaviour
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
        if (RunConfig.Level.GetMechanic<ChaosBlockMechanicConfig>() == null) return;   // mechanic not added to this level
        if (PlacementController.Instance == null) return;   // gameplay scene only
        if (FindFirstObjectByType<ChaosBlockController>() != null) return;
        new GameObject("ChaosBlockController").AddComponent<ChaosBlockController>();
    }

    [Header("Visual (leave empty for a runtime fallback black cube)")]
    [Tooltip("Must already carry (or will have added at runtime) an EnemySurfaceUnit + ChaosBlockUnit.")]
    public GameObject visualPrefab;

    public static ChaosBlockController Instance { get; private set; }

    // Currently-alive chaos blocks — TutorialDirector's TutorialFocus.ChaosBlock
    // reads this to pick a camera target; anything else that wants to react to
    // "is there a live chaos block right now" can too.
    public IReadOnlyList<EnemySurfaceUnit> Alive => _alive;

    LevelDefinition _lv;
    ChaosBlockMechanicConfig _cfg;
    readonly List<EnemySurfaceUnit> _alive = new();
    readonly Dictionary<EnemySurfaceUnit, Vector3Int> _cellOf = new();
    Xoshiro256StarStar _rng;
    bool _combatEndHooked;

    void Awake() => Instance = this;

    void Start()
    {
        _lv  = RunConfig.Level;
        _cfg = _lv != null ? _lv.GetMechanic<ChaosBlockMechanicConfig>() : null;
        if (_cfg == null) { Destroy(gameObject); return; }

        GameFlowManager.OnTurnStarted += HandleTurnStarted;

        // Independent stream (salted off the level seed) so chaos-block placement
        // never perturbs the run's main RNG draw order.
        ulong seed = _lv.runSeed != 0 ? _lv.runSeed ^ 0x4348414F53424CUL : (ulong)System.DateTime.UtcNow.Ticks;
        _rng = new Xoshiro256StarStar(seed);
    }

    void OnDestroy()
    {
        GameFlowManager.OnTurnStarted -= HandleTurnStarted;
        if (EnemyBaseManager.Instance != null) EnemyBaseManager.Instance.OnWaveCompleted -= HandleCombatEnded;
        if (Instance == this) Instance = null;
    }

    // EnemyBaseManager.Instance is Awake()-set but not guaranteed ready before
    // THIS component's own Start() runs (creation-order dependent) — poll a few
    // frames instead of assuming. Cheap: stops checking the instant it's hooked.
    void Update()
    {
        if (_combatEndHooked) return;
        if (EnemyBaseManager.Instance == null) return;
        EnemyBaseManager.Instance.OnWaveCompleted += HandleCombatEnded;
        _combatEndHooked = true;
    }

    void HandleTurnStarted()
    {
        // UpcomingWaveNumber is the 1-based "which wave is this" counter — the
        // SAME one TutorialStep.requiredWave gates on. < startWave → too early,
        // no chaos blocks yet this level.
        var gfm = GameFlowManager.Instance;
        if (gfm != null && gfm.UpcomingWaveNumber < Mathf.Max(1, _cfg.startWave)) return;

        _alive.RemoveAll(e => e == null);
        if (_alive.Count >= Mathf.Max(1, _cfg.maxSimultaneous)) return;
        SpawnOne();
    }

    void HandleCombatEnded()
    {
        _alive.RemoveAll(e => e == null);
        if (_alive.Count == 0 || ResourceManager.Instance == null) return;

        for (int i = 0; i < _alive.Count; i++)
        {
            var e = _alive[i];
            if (e == null || e.CurrentHealth <= 0) continue;
            ResourceManager.Instance.DrainBlockCurrency(_cfg.currencyDrain);
            // Reverse of the normal kill/sell fly-fx: coins leave the currency
            // panel and fly OUT to the surviving chaos block, instead of flying
            // in from a world position.
            CurrencyFlyFx.Drain(e.transform.position, isTurret: false, _cfg.currencyDrain);
        }
    }

    void SpawnOne()
    {
        var grid = GridSystem.instance;
        if (grid == null) return;

        Vector3Int cell = PickSpawnCell(grid);
        Vector3    pos  = grid.GridToWorld(cell);

        GameObject go = visualPrefab != null
            ? Instantiate(visualPrefab, pos, Quaternion.identity)
            : BuildFallbackVisual(grid.cellSize);
        go.name = "ChaosBlock";
        go.transform.position = pos;

        if (!go.TryGetComponent(out EnemySurfaceUnit unit)) unit = go.AddComponent<EnemySurfaceUnit>();
        unit.SetMaxHealth(Mathf.Max(1, _cfg.health));
        unit.rewardOnKill = 0;   // the reward for killing it is NOT losing currency — no bonus on top

        if (!go.TryGetComponent<ChaosBlockUnit>(out _)) go.AddComponent<ChaosBlockUnit>();

        grid.SetOccupied(cell);
        grid.SetNoSupport(cell);   // can't rest a new block/turret on the Chaos Block alone
        _cellOf[unit] = cell;
        _alive.Add(unit);
        unit.OnDied += HandleChaosBlockDied;

        EnemyBaseManager.Instance?.RegisterPersistentTarget(unit);
    }

    void HandleChaosBlockDied(EnemySurfaceUnit died)
    {
        if (died == null) return;
        died.OnDied -= HandleChaosBlockDied;
        EnemyBaseManager.Instance?.UnregisterPersistentTarget(died);

        if (_cellOf.TryGetValue(died, out var cell))
        {
            GridSystem.instance?.ClearOccupied(cell);
            _cellOf.Remove(died);
        }
        _alive.Remove(died);
        Destroy(died.gameObject);
    }

    // A free cell in the RING [minDistance, maxDistance] around the build.
    //
    // Sampled by stepping out from a random EXISTING block rather than drawing a
    // uniform point from the build's bounding box: that box grows with the build,
    // its volume grows cubically, and nearly all of a big box is empty space far
    // from anything — so late-game spawns drifted way past minDistance and ended
    // up outside turret range, where the drain can never be stopped. Anchoring on
    // a real block keeps the spawn tied to the build at any size.
    //
    // Falls back to whichever candidate came closest to the ring if nothing landed
    // in it (e.g. the build is boxed in), so a spawn always happens.
    Vector3Int PickSpawnCell(GridSystem grid)
    {
        var occupied = new List<Vector3Int>(grid.GetGrid().Keys);
        if (occupied.Count == 0) return Vector3Int.zero;

        int minDist = Mathf.Max(1, _cfg.minDistance);
        int maxDist = Mathf.Max(minDist, _cfg.maxDistance);
        float minSqr = (float)minDist * minDist;
        float maxSqr = (float)maxDist * maxDist;

        Vector3Int best      = occupied[0];
        float      bestMiss  = float.MaxValue;   // distance outside the ring

        const int attempts = 60;
        for (int i = 0; i < attempts; i++)
        {
            var anchor = occupied[_rng.NextIntInclusive(0, occupied.Count - 1)];
            var cand = anchor + new Vector3Int(
                _rng.NextIntInclusive(-maxDist, maxDist),
                _rng.NextIntInclusive(-maxDist, maxDist),
                _rng.NextIntInclusive(-maxDist, maxDist));

            if (grid.IsOccupied(cand)) continue;

            float nearestSqr = NearestSqrDistance(cand, occupied);
            if (nearestSqr >= minSqr && nearestSqr <= maxSqr) return cand;   // in the ring

            // Track the near-miss in case the ring is unreachable this round.
            float miss = nearestSqr < minSqr ? minSqr - nearestSqr : nearestSqr - maxSqr;
            if (miss < bestMiss) { bestMiss = miss; best = cand; }
        }

        return best;
    }

    static float NearestSqrDistance(Vector3Int cand, List<Vector3Int> occupied)
    {
        float best = float.MaxValue;
        for (int i = 0; i < occupied.Count; i++)
        {
            float d = ((Vector3)(cand - occupied[i])).sqrMagnitude;
            if (d < best) best = d;
        }
        return best;
    }

    // Runtime fallback so the mechanic works with zero art: a plain black cube,
    // trigger collider (matches how bullets auto-collider enemies — see
    // TurretBullet.EnsureEnemyCollider — and keeps physics from pushing it).
    static Material _fallbackMat;
    GameObject BuildFallbackVisual(float cellSize)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.transform.localScale = Vector3.one * cellSize * 0.92f;

        if (go.TryGetComponent<Collider>(out var col)) col.isTrigger = true;

        if (_fallbackMat == null)
        {
            var sh = Shader.Find("GeoWorld/SilkscreenFlat")
                  ?? Shader.Find("Universal Render Pipeline/Lit")
                  ?? Shader.Find("Standard");
            _fallbackMat = new Material(sh) { hideFlags = HideFlags.DontSave };
        }
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = _fallbackMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        MpbColor.Set(mr, new Color(0.03f, 0.03f, 0.05f, 1f));

        return go;
    }
}
