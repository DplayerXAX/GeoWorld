using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum GamePhase
{
    Init,
    Build,
    ReadyToRun,
    Running,
    GameOver
}

public class GameFlowManager : MonoBehaviour
{
    public SurfaceGraphBuilder graph;
    public LevelEndpointGenerator endpoints;
    public SurfaceUnit unitPrefab;
    public GridSystem gridSystem;
    public PlacementController placement;
    public EnemyBaseManager enemyBaseManager;
    public static GameFlowManager Instance;

    // Fired at the start of each Build phase (end of StartTurn). Per-turn synergy
    // effects (e.g. Abundance harvest income) subscribe to pay out once per turn.
    public static event System.Action OnTurnStarted;
    [Header("Turn")]
    public int blocksPerTurn  = 8;
    public int turretsPerTurn = 3;
    public GamePhase phase;

    [Header("Tower Defense Pacing")]
    [Tooltip("Completed runs before a new endpoint is added.")]
    public int runsPerEndpoint = 2;
    [Tooltip("Maximum concurrent ambient loop layers. Oldest retires when exceeded.")]
    public int maxLoopLayers   = 5;
    [Tooltip("ON: start/end spacing is auto-derived from blocksPerTurn each stage (overrides the generator). " +
             "OFF: use the LevelEndpointGenerator's own Inspector minDistance / maxDistance and leave them alone.")]
    public bool autoEndpointBounds = false;

    [Header("Waves — Procedural (roguelite)")]
    [Tooltip("If assigned, waves are generated procedurally from the run seed. Authored `waves` list (below) is used only for rounds with an explicit override.")]
    public WaveGenerator waveGenerator;

    [Tooltip("Seed for this run. 0 → randomize at first Run(). Persists for the whole run; resets to 0 (re-randomize) on RestartGame.")]
    public ulong runSeed;

    [Header("Waves — Authored overrides")]
    [Tooltip("Optional: a hand-authored WaveDefinition at index N overrides the generator for round N. Use null entries to fall back to the generator.")]
    public List<WaveDefinition> waves = new();
    [Tooltip("If true and using authored waves only (no generator), runs past the end of the list loop back to the start.")]
    public bool loopWaves = false;

    [Header("Synergy")]
    [Tooltip("Color assignment policy for tokens spawned each Build phase. If null, all tokens get BlockColor.None (no synergy participation).")]
    public ColorDistribution colorDistribution;

    // Run-scoped RNG. Lives for the whole run, fed by every random decision
    // (wave gen, loot drops, shop offers, …) so the run is reproducible from
    // `runSeed` alone.
    Xoshiro256StarStar _rng;

    int _runsSinceLastEndpoint;

    // Roguelite accumulation — all endpoint cells and live looping units
    readonly List<Vector3Int> allStarts    = new();
    readonly List<Vector3Int> allEnds      = new();
    readonly List<SurfaceUnit> loopingUnits = new();
    int roundIndex;   // how many extra endpoints have been added so far
    int _wavesCompleted;   // waves cleared this run — drives level victory + endless score

    // ── Read-only state exposed for UI ────────────────────────────────────────
    public int  ActiveLoopCount         => loopingUnits.Count;
    public int  RunsSinceLastEndpoint   => _runsSinceLastEndpoint;
    public int  RoundIndex              => roundIndex;
    public int  WavesCleared            => _wavesCompleted;
    // Length (in faces, each block face = 1) of the current start→end enemy path.
    // 0 when no path connects. Refreshed by EvaluateGrid on every grid change.
    public int  CurrentPathLength       => _currentPathLength;
    int _currentPathLength;
    public IReadOnlyList<Vector3Int> AllStarts => allStarts;
    public IReadOnlyList<Vector3Int> AllEnds   => allEnds;

    // The specific endpoint the player must connect this round.
    // Zero → first stage (any start to any end). Set by AddNextEndpoint.
    Vector3Int _challengeCell;
    bool       _challengeIsStart;

    SurfaceUnit currentUnit;

    [Header("Ambient Scan")]
    public int scanBeats = 8;
    [Range(0f, 1f)] public float scanFlashBrightness = 0.7f;

    float _scanTimer;
    bool  _scanning;
    readonly HashSet<GameObject> _scanFlashing = new();

    void Start()
    {
        Instance = this;
        graph = new SurfaceGraphBuilder();
        endpoints.gridSystem = gridSystem;
        enemyBaseManager = enemyBaseManager != null
            ? enemyBaseManager
            : FindFirstObjectByType<EnemyBaseManager>();
        if (enemyBaseManager == null)
            enemyBaseManager = new GameObject("EnemyBaseManager").AddComponent<EnemyBaseManager>();

        if (PlayerHealth.Instance == null)
            new GameObject("PlayerHealth").AddComponent<PlayerHealth>();
        PlayerHealth.Instance.OnGameOver += HandleGameOver;

        // Wave-driven battle end: when the last enemy is gone, return to Build.
        if (enemyBaseManager != null)
            enemyBaseManager.OnWaveCompleted += HandleWaveCompleted;

        // Enlightenment's turret-upgrade gate is static/asset-backed state that
        // otherwise survives across scene loads — clear it so a previous run
        // that ended mid-cube doesn't leak a stale allowance (or a stuck-locked
        // gate) into this one.
        TowerUpgradeGate.ResetAll();
        UnlockTowerUpgradeEffect.ResetAllHeld();

        RunStats.BeginRun();   // reset kill/blocks/time counters for score-keeping
        ApplyRunConfig();   // Level vs Endless setup (seed, pacing, authored waves)
        CreateFirstStage();
        SpawnStartingLayout();       // pre-built blocks authored via LevelMapAuthor, if any
        FocusCameraOnFirstStage();   // centre the camera between the first start & end

        phase = GamePhase.Build;
        StartTurn();
    }

    // Populated by SpawnStartingLayout(); IntroDirector reads this to pop the pre-built
    // blocks in alongside the start/end endpoints once the intro finishes, instead of
    // having them sit visible (at full scale) through the whole intro sequence.
    public readonly List<GameObject> startingLayoutVisuals = new();

    // Places the level's optional pre-built starting layout (LevelDefinition.startingLayout,
    // authored with LevelMapAuthor) as REAL functional blocks — reuses the same
    // PlacementController.PlaceBlockDirect path SnapshotManager uses for save/load, so turrets
    // get AttachTurretController and pieces get registered with SynergyEvaluator, not just
    // decorative geometry.
    void SpawnStartingLayout()
    {
        var layout = RunConfig.Mode == GameMode.Level ? RunConfig.Level?.startingLayout : null;
        if (layout?.data?.nodes == null) return;
        var pc = PlacementController.Instance;
        if (pc == null) return;

        foreach (var node in layout.data.nodes)
        {
            if (node.cells == null || node.cells.Length == 0) continue;

            bool clash = false;
            foreach (var c in node.cells) if (gridSystem.IsOccupied(c)) { clash = true; break; }
            if (clash) continue;

            BlockData bd = !string.IsNullOrEmpty(node.blockAssetName) ? pc.FindBlockDataByName(node.blockAssetName) : null;
            if (bd == null && System.Enum.TryParse(node.blockTypeName, out BlockType bt)) bd = pc.FindBlockData(bt);
            if (bd == null)
            {
                Debug.LogWarning($"[GameFlowManager] starting layout: can't resolve block '{node.blockAssetName}'/'{node.blockTypeName}', skipped.");
                continue;
            }

            var ins = pc.PlaceBlockDirect(bd, node.cells, node.rotation, node.color, node.synergyColor);
            if (ins != null)
            {
                ins.locked = true;   // level furniture — not pickup/sell/delete-able
                ResourceManager.Instance?.OnBlockPlaced(bd.blockType);
                if (ins.visualObject != null) startingLayoutVisuals.Add(ins.visualObject);
            }
        }

        EvaluateGrid();   // reconcile pathing/synergy once, same as SnapshotManager's restore flow
    }

    // Pull this run's settings from RunConfig (set by Title / LevelSelect before
    // the scene loaded). In Endless this is mostly a no-op.
    void ApplyRunConfig()
    {
        if (RunConfig.Seed != 0UL) runSeed = RunConfig.Seed;

        // No level asset (Endless) → always on; a Level run reads its own flag.
        SynergyEvaluator.Enabled = RunConfig.Mode != GameMode.Level
            || RunConfig.Level == null || RunConfig.Level.synergyEnabled;

        if (RunConfig.Mode == GameMode.Level && RunConfig.Level != null)
        {
            var lv = RunConfig.Level;
            if (lv.blocksPerTurn  > 0) blocksPerTurn  = lv.blocksPerTurn;
            if (lv.turretsPerTurn >= 0) turretsPerTurn = lv.turretsPerTurn;
            if (lv.waves != null && lv.waves.Count > 0)
            {
                waves     = new List<WaveDefinition>(lv.waves);
                loopWaves = false;
            }
        }
    }

    // Win/score bookkeeping per completed wave. Returns true if the run is over
    // (level cleared → returned to the map); callers should stop after that.
    bool HandleWaveProgress()
    {
        _wavesCompleted++;

        if (RunConfig.Mode == GameMode.Level && RunConfig.Level != null
            && RunConfig.Level.wavesToClear > 0
            && _wavesCompleted >= RunConfig.Level.wavesToClear
            && (!RunConfig.Level.requireAllObjectives || LevelObjectivesTracker.AllRequiredSatisfied))
        {
            DoLevelClear(_wavesCompleted);
            return true;
        }

        if (RunConfig.Mode == GameMode.Endless)
        {
            int lives = PlayerHealth.Instance != null ? PlayerHealth.Instance.CurrentLives : 0;
            int score = RunStats.ComputeScore(_wavesCompleted, lives, stars: 1);   // no star tiers in endless
            SaveSystem.RecordEndless(_wavesCompleted, score);
        }

        return false;
    }

    // Continuous objective-driven clear: fires the moment every NON-optional
    // objective is satisfied (any phase). Gated by Level.clearOnObjectivesComplete.
    bool _levelDone;

    // True while the level-clear settlement is on screen — systems read this to
    // lock input (shop, pickup, sell, …) and hide gameplay HUD.
    public bool LevelCleared => _levelDone;
    public static bool SettlementUp => Instance != null && Instance._levelDone;

    bool TryObjectiveClear()
    {
        if (_levelDone) return false;
        var lv = RunConfig.Mode == GameMode.Level ? RunConfig.Level : null;
        if (lv == null || !lv.clearOnObjectivesComplete) return false;
        if (!LevelObjectivesTracker.Tracking) return false;   // tracker not live yet
        if (!HasRequiredObjective(lv) || !LevelObjectivesTracker.AllRequiredSatisfied) return false;

        DoLevelClear(_wavesCompleted);
        return true;
    }

    static bool HasRequiredObjective(LevelDefinition lv)
    {
        if (lv == null || lv.objectives == null) return false;
        for (int i = 0; i < lv.objectives.Count; i++)
            if (lv.objectives[i] != null && !lv.objectives[i].optional) return true;
        return false;
    }

    // Records the clear, stops any in-progress wave, and shows the settlement.
    void DoLevelClear(int wavesReached)
    {
        if (_levelDone) return;
        _levelDone = true;

        if (phase == GamePhase.Running)
        {
            ArpeggiatorManager.Instance?.StopRecording();
            if (currentUnit != null) { Destroy(currentUnit.gameObject); currentUnit = null; }
            ResourceManager.Instance?.SetCombatActive(false);
            enemyBaseManager?.CancelWave();
            BackgroundReactor.Instance?.SetCombatMode(false);
            AudioManager.Instance?.ExitBattleBGM();
        }
        Time.timeScale = 1f;
        phase = GamePhase.Build;

        int lives    = PlayerHealth.Instance != null ? PlayerHealth.Instance.CurrentLives : 0;
        int maxLives = PlayerHealth.Instance != null ? PlayerHealth.Instance.maxLives     : 0;
        bool objMet  = LevelObjectivesTracker.AllRequiredSatisfied;
        var (objSatisfied, objTotal) = LevelObjectivesTracker.Tracking ? LevelObjectivesTracker.Progress : (0, 0);
        int stars    = RunStats.ComputeStars(objSatisfied, objTotal, lives, maxLives);
        int score    = RunStats.ComputeScore(wavesReached, lives, stars);

        int prevBest = RunConfig.Level != null ? SaveSystem.Profile.GetRecord(RunConfig.Level.levelId)?.bestScore ?? 0 : 0;
        bool isNewBest = score > prevBest;

        // Keepsake: the exact board this clear was won with, plus which synergy
        // theme was flying highest at the buzzer. Written into the record BEFORE
        // RecordClear so its own Save() call at the end persists both in the same
        // write. Overwritten on every clear (not just the first) so it always
        // reflects your latest winning build, not a stale first attempt.
        if (RunConfig.Level != null)
        {
            var rec = SaveSystem.Profile.GetOrCreateRecord(RunConfig.Level.levelId);
            rec.buildSnapshot       = SnapshotManager.Capture();
            rec.clearSynergyColor   = DominantActiveSynergyColor();
        }

        bool firstClear = SaveSystem.RecordClear(RunConfig.Level, wavesReached, score);
        if (firstClear && RunConfig.Level != null && RunConfig.Level.rewardConversation != null)
            RunConfig.PendingRewardConversation = RunConfig.Level.rewardConversation;

        LevelClearScreen.Show(RunConfig.Level, wavesReached, lives, maxLives, stars, score, prevBest, isNewBest, objMet, ReturnToMap);
    }

    // Highest-tier synergy still active the moment the level was cleared. None if
    // nothing was active — LevelMapController treats that the same as Universal
    // (no specific theme to celebrate), picking an arbitrary accent instead.
    static BlockColor DominantActiveSynergyColor()
    {
        var actives = SynergyEvaluator.Instance?.Actives;
        if (actives == null) return BlockColor.None;

        var best = BlockColor.None;
        int bestTier = -1;
        foreach (var a in actives)
        {
            if (a?.rule == null || a.rule.color == BlockColor.Universal) continue;
            if (a.tier > bestTier) { bestTier = a.tier; best = a.rule.color; }
        }
        return best;
    }

    public void ReturnToMap()
    {
        Time.timeScale = 1f;
        LoadingScreen.Go("LevelSelect");   // spinning-cube loading page, then async-load
    }

    void OnDestroy()
    {
        if (PlayerHealth.Instance != null)
            PlayerHealth.Instance.OnGameOver -= HandleGameOver;
        if (enemyBaseManager != null)
            enemyBaseManager.OnWaveCompleted -= HandleWaveCompleted;
    }

    void HandleWaveCompleted()
    {
        if (phase == GamePhase.Running) EndRunningPhase();
    }

    void HandleGameOver()
    {
        if (phase == GamePhase.GameOver) return;
        Debug.Log("[GameFlow] Game Over — last life lost.");
        AbortRun();
        enemyBaseManager?.CancelWave();
        BackgroundReactor.Instance?.SetCombatMode(false);
        AudioManager.Instance?.ExitBattleBGM();   // safety: dropping into Game Over also leaves battle music
        phase = GamePhase.GameOver;
    }

    // Hard reset → reload current scene from scratch. Bound to PlayerHealth's
    // restart UI; safest way to wipe all combat / music / scene state.
    public void RestartGame()
    {
        Time.timeScale = 1f;
        LoadingScreen.Go(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    }

    // ── Endpoint helpers ─────────────────────────────────────────────────────

    void ConfigureEndpointBounds(float extraRange = 0f)
    {
        // Per-level override wins outright — fixed bounds for EVERY endpoint this
        // level generates (opening pair and every later AddNextEndpoint() call
        // alike), ignoring both the auto blocksPerTurn scaling below AND whatever
        // the generator's own Inspector defaults are. This is what lets a level
        // author pin down "the second start point sometimes rolls really far"
        // instead of inheriting gameplay's global widening-per-round behavior.
        var lv = RunConfig.Mode == GameMode.Level ? RunConfig.Level : null;
        if (lv != null && lv.overrideEndpointDistance)
        {
            endpoints.minDistance = Mathf.Max(0f, lv.endpointMinDistance);
            endpoints.maxDistance = Mathf.Max(endpoints.minDistance, lv.endpointMaxDistance);
            return;
        }

        // Respect the generator's Inspector values when auto-bounds is off.
        if (!autoEndpointBounds) return;

        // Min: at least 4 cells, or 60 % of blocksPerTurn — whichever is larger.
        endpoints.minDistance = Mathf.Max(4f, blocksPerTurn * 0.6f);
        // Max: no hard cap — grows naturally with blocks and round index.
        endpoints.maxDistance = blocksPerTurn * 2f + extraRange;
    }

    void CreateFirstStage()
    {
        ConfigureEndpointBounds();

        // Tutorials / authored levels can pin the start & end instead of randomising.
        var lv = RunConfig.Mode == GameMode.Level ? RunConfig.Level : null;
        if (lv != null && lv.fixedEndpoints)
            endpoints.GenerateFixed(lv.startCell, lv.endCell);
        else
            endpoints.Generate();

        allStarts.Add(endpoints.startCell);
        allEnds.Add(endpoints.endCell);
    }

    // Centre the orbit camera on the midpoint of the first start & end endpoints.
    void FocusCameraOnFirstStage()
    {
        if (gridSystem == null || allStarts.Count == 0 || allEnds.Count == 0) return;
        Vector3 mid = (gridSystem.GridToWorld(allStarts[0]) + gridSystem.GridToWorld(allEnds[0])) * 0.5f;
        var orbit = FindFirstObjectByType<OrbitCamera>();
        if (orbit != null) orbit.FocusOnPoint(mid);
    }

    // Alternates: even roundIndex → +start, odd → +end.
    // Distance window widens so later endpoints can span larger gaps.
    public void AddNextEndpoint()
    {
        float extraRange = roundIndex * 1.5f;
        ConfigureEndpointBounds(extraRange);

        bool addStart = (roundIndex % 2 == 0);

        if (addStart)
        {
            // allEnds constrains the new start to the union of the existing
            // starts' reach circles — see LevelEndpointGenerator.SampleStartInsideReach.
            var cell = endpoints.GenerateSinglePoint(allStarts, true, allEnds);
            if (cell != Vector3Int.zero)
            {
                allStarts.Add(cell);
                _challengeCell    = cell;
                _challengeIsStart = true;
            }
        }
        else
        {
            var cell = endpoints.GenerateSinglePoint(allEnds, false);
            if (cell != Vector3Int.zero)
            {
                allEnds.Add(cell);
                _challengeCell    = cell;
                _challengeIsStart = false;
            }
        }

        roundIndex++;
    }

    // ── Per-frame logic ───────────────────────────────────────────────────────

    void Update()
    {
        if (phase == GamePhase.GameOver) return;

        // Objective-driven levels clear the instant every required goal is met (any
        // phase), independent of wavesToClear.
        if (TryObjectiveClear()) return;

        if (phase == GamePhase.Build)
        {
            // Space: commit and run
            if (Input.GetKeyDown(KeyCode.Space) && TutorialDirector.CanRun())
                Run();

            // P: manual re-evaluate (force-refresh live preview line)
            if (Input.GetKeyDown(KeyCode.P))
                EvaluateGrid();

            float secPerBeat = 60f / unitPrefab.bpm;
            _scanTimer += Time.deltaTime;

            if (_scanTimer >= secPerBeat * scanBeats && !_scanning)
            {
                _scanTimer = 0f;
                StartCoroutine(PathPulseScan());
            }
        }
        else
        {
            _scanTimer = 0f;
        }

        if (phase == GamePhase.ReadyToRun)
        {
            // Space confirms from preview state; B cancels back to build
            if (Input.GetKeyDown(KeyCode.Space) && TutorialDirector.CanRun())
                Run();

            if (Input.GetKeyDown(KeyCode.B))
                phase = GamePhase.Build;
        }
    }

    // ── Ambient path scan ─────────────────────────────────────────────────────

    IEnumerator PathPulseScan()
    {
        _scanning = true;

        var scanGraph = new SurfaceGraphBuilder();
        scanGraph.SetData(gridSystem);
        scanGraph.Build();

        var startFaces = CollectFacesFromGraph(scanGraph, allStarts);
        var endFaces   = CollectFacesFromGraph(scanGraph, allEnds);

        if (startFaces.Count > 0 && endFaces.Count > 0)
        {
            var path = SurfacePathfinding.FindPath(startFaces, endFaces);

            if (path != null && path.Count > 0)
            {
                float secPerBeat = 60f / unitPrefab.bpm;
                float stepSec    = Mathf.Clamp(secPerBeat * scanBeats / path.Count, 0.08f, 0.72f);

                foreach (var node in path)
                {
                    var inst = gridSystem.GetInstanceAt(node.cell);

                    if (inst?.data != null)
                    {
                        int root  = RootDegree(inst.data.blockType);
                        int yi    = Mathf.Clamp(node.cell.y, 0, YDegreeMap.Length - 1);
                        int deg   = ((root + YDegreeMap[yi] - 2) % 7 + 7) % 7 + 1;
                        int oct   = Mathf.Clamp(node.cell.y - 1, -1, 3);

                        ArpeggiatorManager.Instance.PlayAmbientNote(deg, oct, 0.28f,
                            inst.visualObject);
                        BackgroundReactor.Instance?.OnNote(0.2f);

                        if (inst.visualObject != null)
                            StartCoroutine(ScanFlashBlock(inst.visualObject, stepSec * 0.7f));
                    }

                    yield return new WaitForSeconds(stepSec);

                    if (phase != GamePhase.Build) break;
                }
            }
        }

        _scanning = false;
    }

    static readonly int[] YDegreeMap = { 1, 2, 4, 5, 7 };

    static int RootDegree(BlockType t) => t switch
    {
        BlockType.Home   => 1,
        BlockType.Lift   => 4,
        BlockType.Pull   => 5,
        BlockType.Shadow => 7,
        _                => 1,
    };

    IEnumerator ScanFlashBlock(GameObject obj, float duration)
    {
        if (obj == null || !_scanFlashing.Add(obj)) yield break;

        var rends = obj.GetComponentsInChildren<Renderer>();
        int n = rends.Length;

        var orig   = new Color[n];
        var bright = new Color[n];

        for (int i = 0; i < n; i++)
        {
            orig[i]   = MpbColor.Get(rends[i]);
            bright[i] = Color.Lerp(orig[i], Color.white, scanFlashBrightness);
            MpbColor.Set(rends[i], bright[i]);
        }

        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float s = Mathf.Clamp01(t / duration);
            s = s * s * (3f - 2f * s);

            for (int i = 0; i < n; i++)
                if (rends[i])
                    MpbColor.Set(rends[i], Color.Lerp(bright[i], orig[i], s));

            yield return null;
        }

        for (int i = 0; i < n; i++)
            if (rends[i])
                MpbColor.Set(rends[i], orig[i]);

        _scanFlashing.Remove(obj);
    }

    // ── Turn / run flow ───────────────────────────────────────────────────────

    public void StartTurn()
    {
        ResourceManager.Instance?.GrantRoundIncome();   // fixed income each build phase
        placement.currentBlock = null;
        placement.mode = PlacementMode.Select;
        placement.ClearTray();                          // remove leftover tokens from last round
        PlacementController.Instance.ResetRefreshCost();
        // Refresh the synergy color pool. In mode C this samples a new subset
        // of colors for the round; in mode B it's a no-op snapshot. Also re-applies
        // the CURRENT level's fixed color pool (LevelDefinition.allowedColors) every
        // round, so it's never stale across a level change.
        colorDistribution?.BeginRound(EnsureRng(), RunConfig.Level != null ? RunConfig.Level.allowedColors : null);

        placement.SpawnRoundBlocks(blocksPerTurn, turretsPerTurn);

        // Per-turn synergy payouts (Abundance harvest, etc.) run after the
        // standing income + token spawn so they see the live synergy state.
        OnTurnStarted?.Invoke();
    }

    // Called after every block place/remove — rebuilds graph, refreshes live
    // preview line, and stops the unit immediately if the path is now broken.
    public void EvaluateGrid()
    {
        graph.SetData(gridSystem);
        graph.Build();

        var path = FindCurrentPath();
        _currentPathLength = path != null ? path.Count : 0;

        if (phase == GamePhase.Running)
        {
            if (path == null)
            {
                // Path broken while unit was traversing — stop it immediately.
                ArpeggiatorManager.Instance?.StopRecording();
                if (currentUnit != null) { Destroy(currentUnit.gameObject); currentUnit = null; }
                enemyBaseManager?.CancelWave();
                ResourceManager.Instance?.SetCombatActive(false);
                phase = GamePhase.Build;
                // No live line yet — will appear on next block placement.
            }
            // Path still valid while running — re-route live enemies so anything the
            // newly-placed block blocked finds a fresh way to the end.
            enemyBaseManager?.RerouteActive(graph, CurrentEndFaces());
            return;
        }

        // Build / ReadyToRun: show a live preview line for EVERY connected spawn
        // point, not just the challenge path (multi-spawn rounds redraw all routes).
        PathFlowManager.Instance?.UpdateLiveLines(FindAllSpawnPaths());
        phase = path != null ? GamePhase.ReadyToRun : GamePhase.Build;
    }

    void Run()
    {
        graph.SetData(gridSystem);
        graph.Build();

        var path = FindCurrentPath();

        if (path == null)
        {
            Debug.Log("Path failed");
            phase = GamePhase.Build;
            PathFlowManager.Instance?.UpdateLiveLine(null);
            PlacementController.Instance?.ShowPlacementPopup(
                "No path — connect start to end before running.", 2f);
            return;
        }

        // Enemies spawn from EVERY connected start, not just the one the
        // challenge path traced back to. `path` above still gates the run (the
        // round's challenge endpoint must be connected); `spawnPaths` drives
        // the actual emission.
        var spawnPaths = FindAllSpawnPaths();
        if (spawnPaths.Count == 0) spawnPaths.Add(path);   // safety net

        // Live preview line → tracked loop lines, one per active route so the
        // player can see where every wave of enemies will come from.
        PathFlowManager.Instance?.ClearLiveLine();
        foreach (var p in spawnPaths)
            PathFlowManager.Instance?.AddFlow(p);

        // No more music SurfaceUnit — combat timing is driven by the enemy
        // wave. EndRunningPhase fires when EnemyBaseManager.OnWaveCompleted.
        currentUnit = null;

        phase = GamePhase.Running;
        enemyBaseManager?.BeginWave(spawnPaths, PickWaveForThisRound());
        ResourceManager.Instance?.SetCombatActive(true);   // start turret currency regen
        ShopController.Instance?.OnCombatStart();           // collapse and hide shop
        BackgroundReactor.Instance?.SetCombatMode(true);    // switch skybox to combat / distorted state
        AudioManager.Instance?.EnterBattleBGM();            // BGM → battle track
        // Combat ripple is a one-shot grow animation that touches every cube;
        // run it once on the challenge path (running it per-route would have
        // multiple coroutines fighting over the same cube scales).
        placement.TriggerCombatRipple(path);                // wave grows along path, then off-path blocks bloom
    }

    // Lazy run-scoped RNG. First call rolls a fresh seed if runSeed==0 so the
    // inspector value is sticky if the player set one, otherwise we get a new
    // run. Subsequent calls return the same instance — its state accumulates
    // across the run (wave gen, loot, shop, …).
    public Xoshiro256StarStar Rng => EnsureRng();

    Xoshiro256StarStar EnsureRng()
    {
        if (_rng != null) return _rng;

        if (runSeed == 0UL)
            runSeed = unchecked((ulong)System.DateTime.UtcNow.Ticks ^ 0xA5A5A5A5A5A5A5A5UL);

        _rng = new Xoshiro256StarStar(runSeed);
        Debug.Log($"[GameFlowManager] Run seed = {runSeed}");
        return _rng;
    }

    // Per-round wave selection. Priority:
    //   1. Authored waves[roundIndex] if present (non-null) → use its groups.
    //   2. Else, WaveGenerator if assigned → procedural list driven by _rng.
    //   3. Else null → EnemyBaseManager falls back to inspector spawnCount.
    //
    // Note: roundIndex only ticks when an endpoint is added (every
    // `runsPerEndpoint` completed runs). For a per-run wave counter, replace
    // `roundIndex` here with your own field that increments each Run().
    IList<SpawnGroup> PickWaveForThisRound()
    {
        // 1. Authored override
        if (waves != null && waves.Count > 0)
        {
            int i = roundIndex;
            if (i >= waves.Count && loopWaves) i %= waves.Count;
            if (i >= 0 && i < waves.Count && waves[i] != null)
                return waves[i].groups;
        }

        // 2. Procedural
        if (waveGenerator != null)
            return waveGenerator.Generate(roundIndex, EnsureRng());

        // 3. Fallback handled by EnemyBaseManager
        return null;
    }

    // ── Wave forecast (UI) ────────────────────────────────────────────────────
    // Display-ready preview of the NEXT wave, computed WITHOUT advancing the run
    // RNG (save/restore xoshiro state around the generator). Surfaced by the
    // spawn-point info panel. Deterministic from current state, so repeated
    // calls within the same Build phase return identical results.
    public struct WaveForecast
    {
        public bool valid;          // false → no generator/authored wave available
        public int  waveNumber;     // perceived, 1-based count of runs incl. this one
        public int  round;          // difficulty tier driving wave budget (roundIndex)
        public int  totalCount;     // enemies emitted per spawn point this wave
        public List<WaveGenerator.WaveForecastGroup> groups;
    }

    // 1-based ordinal of the upcoming run. Each Run() is one wave from the
    // player's POV, but roundIndex only ticks every runsPerEndpoint runs — so
    // this derived counter is the monotonic "which wave is this" number for UI.
    public int UpcomingWaveNumber =>
        roundIndex * Mathf.Max(1, runsPerEndpoint) + _runsSinceLastEndpoint + 1;

    public WaveForecast GetNextWaveForecast()
    {
        var fc = new WaveForecast
        {
            valid      = false,
            waveNumber = UpcomingWaveNumber,
            round      = roundIndex,
            totalCount = 0,
            groups     = new List<WaveGenerator.WaveForecastGroup>(),
        };

        // 1. Authored override for this round.
        if (waves != null && waves.Count > 0)
        {
            int i = roundIndex;
            if (i >= waves.Count && loopWaves) i %= waves.Count;
            if (i >= 0 && i < waves.Count && waves[i] != null)
            {
                AggregateGroups(waves[i].groups, fc.groups);
                fc.totalCount = SumCounts(fc.groups);
                fc.valid = true;
                return fc;
            }
        }

        // 2. Procedural — non-destructive: save → forecast → restore.
        if (waveGenerator != null)
        {
            var rng   = EnsureRng();
            var saved = rng.SaveState();
            fc.groups = waveGenerator.Forecast(roundIndex, rng);
            rng.LoadState(saved);
            fc.totalCount = SumCounts(fc.groups);
            fc.valid = true;
            return fc;
        }

        // 3. No generator / authored waves → leave invalid; UI shows a fallback.
        return fc;
    }

    static int SumCounts(List<WaveGenerator.WaveForecastGroup> groups)
    {
        int n = 0;
        if (groups != null) foreach (var g in groups) n += g.count;
        return n;
    }

    // Merge authored SpawnGroups into name-aggregated forecast lines, resolving
    // the display name from the prefab (authored waves carry a real prefab).
    static void AggregateGroups(List<SpawnGroup> src, List<WaveGenerator.WaveForecastGroup> dst)
    {
        if (src == null) return;
        foreach (var g in src)
        {
            if (g == null || g.count <= 0) continue;
            string name = g.prefab != null ? g.prefab.name : "Enemy";
            int at = dst.FindIndex(e => e.name == name);
            if (at >= 0) { var e = dst[at]; e.count += g.count; dst[at] = e; }
            else dst.Add(new WaveGenerator.WaveForecastGroup { name = name, count = g.count, prefab = g.prefab });
        }
    }

    public bool TryGetCurrentPath(out List<FaceNode> path)
    {
        graph.SetData(gridSystem);
        graph.Build();
        path = FindCurrentPath();
        return path != null && path.Count > 0;
    }

    // Builds one path per START cell that can still reach ANY end. Used to
    // spawn enemies from EVERY connected start point, not just whichever start
    // SurfacePathfinding's multi-source BFS happened to trace back to (which,
    // combined with the _challengeCell restriction below, was always the
    // last-added start — so enemies only ever emerged from one spawn point).
    // Requires `graph` to be built first (Run() / TryGetCurrentPath() do this).
    List<List<FaceNode>> FindAllSpawnPaths()
    {
        var paths    = new List<List<FaceNode>>();
        var endFaces = CollectFaces(allEnds);
        if (endFaces.Count == 0) return paths;

        foreach (var startCell in allStarts)
        {
            var startFaces = CollectFaces(new List<Vector3Int> { startCell });
            if (startFaces.Count == 0) continue;

            var p = SurfacePathfinding.FindPath(startFaces, endFaces);
            if (p != null && p.Count > 0) paths.Add(p);
        }
        return paths;
    }

    // Builds and returns the path for the current challenge state,
    // or null if no valid path exists.
    // End faces for the active run (mirrors FindCurrentPath's end selection).
    List<FaceNode> CurrentEndFaces()
    {
        if (_challengeCell != Vector3Int.zero && !_challengeIsStart)
            return CollectFacesFromGraph(graph, new List<Vector3Int> { _challengeCell });
        return CollectFaces(allEnds);
    }

    List<FaceNode> FindCurrentPath()
    {
        List<FaceNode> startFaces, endFaces;

        if (_challengeCell == Vector3Int.zero)
        {
            startFaces = CollectFaces(allStarts);
            endFaces   = CollectFaces(allEnds);
        }
        else if (_challengeIsStart)
        {
            startFaces = CollectFacesFromGraph(graph, new List<Vector3Int> { _challengeCell });
            endFaces   = CollectFaces(allEnds);
        }
        else
        {
            startFaces = CollectFaces(allStarts);
            endFaces   = CollectFacesFromGraph(graph, new List<Vector3Int> { _challengeCell });
        }

        if (startFaces.Count == 0 || endFaces.Count == 0) return null;
        return SurfacePathfinding.FindPath(startFaces, endFaces);
    }

    // Full reset — wipes blocks, endpoints, loops, and round counters.
    // Used by snapshot load to start from a clean slate before replay.
    public void WipeAll()
    {
        AbortRun();

        foreach (var ins in gridSystem.GetAllInstances())
            gridSystem.RemoveInstance(ins);

        PathFlowManager.Instance?.ClearAll();
        LoopManager.Instance?.ClearAllLoops();
        PlacementController.Instance?.ClearUndoHistory();

        foreach (var u in loopingUnits)
            if (u != null) Destroy(u.gameObject);
        loopingUnits.Clear();

        endpoints.ClearAll();
        allStarts.Clear();
        allEnds.Clear();

        _runsSinceLastEndpoint = 0;
        roundIndex             = 0;
        _wavesCompleted        = 0;
        _challengeCell         = Vector3Int.zero;
        _challengeIsStart      = false;

        // New run → new seed. EnsureRng() in the next Run() will roll a fresh
        // one if runSeed is still 0, or you can pre-set runSeed in the
        // inspector / pause menu to share a specific run.
        runSeed = 0UL;
        _rng    = null;

        UpgradeManager.Instance?.ResetForNewRun();
        SynergyEvaluator.Instance?.ResetForNewRun();

        phase = GamePhase.Build;
    }

    // Direct restore of round-scoped counters and endpoint tracking,
    // used after a snapshot load has re-created the visuals.
    public void RestoreRoundState(int round, int runsSinceLastEndpoint,
                                  IList<Vector3Int> starts, IList<Vector3Int> ends)
    {
        roundIndex             = round;
        _runsSinceLastEndpoint = runsSinceLastEndpoint;
        allStarts.Clear();
        allEnds.Clear();
        if (starts != null) allStarts.AddRange(starts);
        if (ends   != null) allEnds.AddRange(ends);
    }

    // Force-aborts an in-progress run without promoting it to a loop.
    // Used by dev tools / cancel-run shortcut.
    public void AbortRun()
    {
        if (phase != GamePhase.Running) return;

        ArpeggiatorManager.Instance?.StopRecording();
        if (currentUnit != null) { Destroy(currentUnit.gameObject); currentUnit = null; }
        ResourceManager.Instance?.SetCombatActive(false);
        BackgroundReactor.Instance?.SetCombatMode(false);
        AudioManager.Instance?.ExitBattleBGM();
        Time.timeScale = 1f;                               // restore normal speed when bailing out of combat
        phase = GamePhase.Build;
    }

    // Called when the wave completes (EnemyBaseManager.OnWaveCompleted) — or
    // legacy SurfaceUnit traversal end if music unit is ever re-enabled.
    public void EndRunningPhase()
    {
        if (phase != GamePhase.Running) return;

        // Legacy SurfaceUnit-driven path: promote to loop layer. With the
        // wave-driven flow currentUnit is null and we skip this entirely.
        if (currentUnit != null)
        {
            currentUnit.SetLooping(true);
            loopingUnits.Add(currentUnit);
            while (loopingUnits.Count > maxLoopLayers)
                RetireOldestLoop();
        }

        ResourceManager.Instance?.SetCombatActive(false);  // stop turret regen, income in StartTurn
        enemyBaseManager?.CancelWave();
        BackgroundReactor.Instance?.SetCombatMode(false);  // restore calm skybox
        AudioManager.Instance?.ExitBattleBGM();            // BGM → calm track
        Time.timeScale = 1f;                               // combat over → drop any fast-forward back to 1×
        phase = GamePhase.Build;

        // Score / victory. If a Level is cleared this returns to the map — stop here.
        if (HandleWaveProgress()) return;

        // Add a new endpoint only every N completed runs.
        _runsSinceLastEndpoint++;
        if (_runsSinceLastEndpoint >= runsPerEndpoint)
        {
            _runsSinceLastEndpoint = 0;
            AddNextEndpoint();
        }

        // Roguelite choice. Non-blocking — Build phase starts immediately,
        // the picker UI overlays on top. If you later want it to block input,
        // gate StartTurn() behind the picker's onPicked callback.
        UpgradeManager.Instance?.OfferEndOfWave(EnsureRng());
        Time.timeScale = 1f;
        StartTurn();
    }

    // Removes the oldest looping unit: stops its audio loop, removes its laser
    // line, and destroys the GameObject.
    void RetireOldestLoop()
    {
        if (loopingUnits.Count == 0) return;

        var old = loopingUnits[0];
        loopingUnits.RemoveAt(0);

        if (old == null) return;

        // Gather the path cells so the right loop/line entries can be removed.
        var cells = new List<Vector3Int>();
        if (old.Path != null)
            foreach (var n in old.Path) cells.Add(n.cell);

        if (cells.Count > 0)
        {
            PathFlowManager.Instance?.RemoveFlowsOverlapping(cells);
            LoopManager.Instance?.RemoveLoopsOverlapping(cells);
        }

        Destroy(old.gameObject);
    }

    // ── Path-break validation (used by PlacementController in combat) ────────

    /// <summary>
    /// Returns true if placing blocks at <paramref name="cells"/> would break the
    /// current enemy path.  Use this before placing a turret during combat to
    /// enforce the "turrets cannot block the active route" rule.
    ///
    /// Internally: temporarily registers a dummy instance, re-runs pathfinding,
    /// then removes it — leaves the grid unchanged.
    /// </summary>
    public bool WouldBlockPath(Vector3Int[] cells)
    {
        // Filter to only the cells that are currently empty (occupied cells are
        // already denied by CanPlace; no risk of stomping a real block's data).
        var testCells = new List<Vector3Int>();
        foreach (var c in cells)
            if (!gridSystem.IsOccupied(c))
                testCells.Add(c);

        if (testCells.Count == 0) return false;

        // Temporarily occupy the target cells.
        var fake = new PlacedBlockInstance();
        foreach (var c in testCells)
            fake.occupiedCells.Add(c);

        gridSystem.RegisterInstance(fake);

        var testGraph = new SurfaceGraphBuilder();
        testGraph.SetData(gridSystem);
        testGraph.Build();

        var startFaces = CollectFacesFromGraph(testGraph, allStarts);
        var endFaces   = CollectFacesFromGraph(testGraph, allEnds);
        bool blocked   = startFaces.Count == 0 || endFaces.Count == 0
                         || SurfacePathfinding.FindPath(startFaces, endFaces) == null;

        // Undo — RemoveInstance safely handles null visualObject.
        gridSystem.RemoveInstance(fake);

        return blocked;
    }

    // ── Graph helpers ─────────────────────────────────────────────────────────

    List<FaceNode> CollectFaces(List<Vector3Int> cells)
    {
        var result = new List<FaceNode>();
        foreach (var cell in cells)
        {
            var faces = graph.GetFaceNodes(cell);
            if (faces != null) result.AddRange(faces);
        }
        return result;
    }

    List<FaceNode> CollectFacesFromGraph(SurfaceGraphBuilder g, List<Vector3Int> cells)
    {
        var result = new List<FaceNode>();
        foreach (var cell in cells)
        {
            var faces = g.GetFaceNodes(cell);
            if (faces != null) result.AddRange(faces);
        }
        return result;
    }
}
