using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Per-level obstacle mechanic (LevelDefinition.chaosBlockEnabled): spawns one
// black "Chaos Block" on the build grid at the start of EVERY round (Build
// phase). It's a stationary EnemySurfaceUnit — destructible by turrets during
// combat exactly like a normal enemy (see ChaosBlockUnit for the visual side) —
// registered with EnemyBaseManager as a PERSISTENT target (see
// EnemyBaseManager.RegisterPersistentTarget) so it never blocks wave
// completion and survives CancelWave()'s per-round reset. Any chaos block
// still alive when combat ends drains chaosBlockCurrencyDrain block currency;
// since a survivor isn't removed, ignoring them lets the threat (and the tax)
// pile up round over round until someone kills them.
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
        if (RunConfig.Mode != GameMode.Level || RunConfig.Level == null || !RunConfig.Level.chaosBlockEnabled) return;
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
    readonly List<EnemySurfaceUnit> _alive = new();
    readonly Dictionary<EnemySurfaceUnit, Vector3Int> _cellOf = new();
    Xoshiro256StarStar _rng;
    bool _combatEndHooked;

    void Awake() => Instance = this;

    void Start()
    {
        _lv = RunConfig.Level;
        if (_lv == null || !_lv.chaosBlockEnabled) { Destroy(gameObject); return; }

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
        if (gfm != null && gfm.UpcomingWaveNumber < Mathf.Max(1, _lv.chaosBlockStartWave)) return;

        _alive.RemoveAll(e => e == null);
        if (_alive.Count >= Mathf.Max(1, _lv.chaosBlockMaxSimultaneous)) return;
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
            ResourceManager.Instance.DrainBlockCurrency(_lv.chaosBlockCurrencyDrain);
            // Reverse of the normal kill/sell fly-fx: coins leave the currency
            // panel and fly OUT to the surviving chaos block, instead of flying
            // in from a world position.
            CurrencyFlyFx.Drain(e.transform.position, isTurret: false, _lv.chaosBlockCurrencyDrain);
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
        unit.SetMaxHealth(Mathf.Max(1, _lv.chaosBlockHealth));
        unit.rewardOnKill = 0;   // the reward for killing it is NOT losing currency — no bonus on top

        if (!go.TryGetComponent<ChaosBlockUnit>(out _)) go.AddComponent<ChaosBlockUnit>();

        grid.SetOccupied(cell);
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

    // Random cell in the current build's (expanded) bounding box, preferring one
    // at least chaosBlockMinDistance from the nearest occupied cell. Falls back
    // to the farthest candidate tried if no attempt clears the minimum.
    Vector3Int PickSpawnCell(GridSystem grid)
    {
        var occupied = new List<Vector3Int>(grid.GetGrid().Keys);
        if (occupied.Count == 0) return Vector3Int.zero;

        Vector3Int min = occupied[0], max = occupied[0];
        for (int i = 1; i < occupied.Count; i++)
        {
            min = Vector3Int.Min(min, occupied[i]);
            max = Vector3Int.Max(max, occupied[i]);
        }

        int margin = Mathf.Max(1, _lv.chaosBlockSearchMargin);
        min -= Vector3Int.one * margin;
        max += Vector3Int.one * margin;

        int minDist   = Mathf.Max(1, _lv.chaosBlockMinDistance);
        float minSqr  = (float)minDist * minDist;

        Vector3Int best = occupied[0];
        float      bestNearestSqr = -1f;

        const int attempts = 40;
        for (int i = 0; i < attempts; i++)
        {
            var cand = new Vector3Int(
                _rng.NextIntInclusive(min.x, max.x),
                _rng.NextIntInclusive(min.y, max.y),
                _rng.NextIntInclusive(min.z, max.z));

            if (grid.IsOccupied(cand)) continue;

            float nearestSqr = NearestSqrDistance(cand, occupied);
            if (nearestSqr > bestNearestSqr) { bestNearestSqr = nearestSqr; best = cand; }
            if (nearestSqr >= minSqr) return cand;   // good enough — stop early
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
