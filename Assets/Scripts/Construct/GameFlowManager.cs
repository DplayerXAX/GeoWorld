using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum GamePhase
{
    Init,
    Build,
    ReadyToRun,
    Running
}

public class GameFlowManager : MonoBehaviour
{
    public SurfaceGraphBuilder graph;
    public LevelEndpointGenerator endpoints;
    public SurfaceUnit unitPrefab;
    public GridSystem gridSystem;
    public PlacementController placement;
    public static GameFlowManager Instance;
    [Header("Turn")]
    public int blocksPerTurn = 8;
    public GamePhase phase;

    [Header("Tower Defense Pacing")]
    [Tooltip("Completed runs before a new endpoint is added.")]
    public int runsPerEndpoint = 3;
    [Tooltip("Maximum concurrent ambient loop layers. Oldest retires when exceeded.")]
    public int maxLoopLayers   = 5;

    int _runsSinceLastEndpoint;

    // Roguelite accumulation — all endpoint cells and live looping units
    readonly List<Vector3Int> allStarts    = new();
    readonly List<Vector3Int> allEnds      = new();
    readonly List<SurfaceUnit> loopingUnits = new();
    int roundIndex;   // how many extra endpoints have been added so far

    // ── Read-only state exposed for UI ────────────────────────────────────────
    public int  ActiveLoopCount         => loopingUnits.Count;
    public int  RunsSinceLastEndpoint   => _runsSinceLastEndpoint;
    public int  RoundIndex              => roundIndex;
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

        CreateFirstStage();

        phase = GamePhase.Build;
        StartTurn();
    }

    // ── Endpoint helpers ─────────────────────────────────────────────────────

    void ConfigureEndpointBounds(float extraRange = 0f)
    {
        // Min: at least 4 cells, or 60 % of blocksPerTurn — whichever is larger.
        endpoints.minDistance = Mathf.Max(4f, blocksPerTurn * 0.6f);
        // Max: no hard cap — grows naturally with blocks and round index.
        endpoints.maxDistance = blocksPerTurn * 2f + extraRange;
    }

    void CreateFirstStage()
    {
        ConfigureEndpointBounds();
        endpoints.Generate();
        allStarts.Add(endpoints.startCell);
        allEnds.Add(endpoints.endCell);
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
            var cell = endpoints.GenerateSinglePoint(allStarts, true);
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
        if (phase == GamePhase.Build)
        {
            // Space: commit and run
            if (Input.GetKeyDown(KeyCode.Space))
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
            if (Input.GetKeyDown(KeyCode.Space))
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
        placement.SpawnRoundBlocks(blocksPerTurn);
    }

    // Called after every block place/remove — rebuilds graph, refreshes live
    // preview line, and stops the unit immediately if the path is now broken.
    public void EvaluateGrid()
    {
        graph.SetData(gridSystem);
        graph.Build();

        var path = FindCurrentPath();

        if (phase == GamePhase.Running)
        {
            if (path == null)
            {
                // Path broken while unit was traversing — stop it immediately.
                ArpeggiatorManager.Instance?.StopRecording();
                if (currentUnit != null) { Destroy(currentUnit.gameObject); currentUnit = null; }
                ResourceManager.Instance?.SetCombatActive(false);
                phase = GamePhase.Build;
                // No live line yet — will appear on next block placement.
            }
            // Path still valid while running: loop line already visible, leave it.
            return;
        }

        // Build / ReadyToRun: show or hide the live preview line.
        PathFlowManager.Instance?.UpdateLiveLine(path);
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
            return;
        }

        // Live preview line → tracked loop line.
        PathFlowManager.Instance?.ClearLiveLine();
        PathFlowManager.Instance?.AddFlow(path);

        // Spawn unit at the visual face-centre of the first path node.
        currentUnit = Instantiate(unitPrefab);
        currentUnit.gameFlow = this;
        currentUnit.transform.position =
            gridSystem.GridToWorld(path[0].cell) + path[0].normal * (gridSystem.cellSize * 0.5f);
        currentUnit.SetPath(path);

        phase = GamePhase.Running;
        ResourceManager.Instance?.SetCombatActive(true);   // start turret currency regen
        ShopController.Instance?.OnCombatStart();           // collapse and hide shop
        placement.TriggerCombatRipple(path);                // wave grows along path, then off-path blocks bloom
    }

    // Builds and returns the path for the current challenge state,
    // or null if no valid path exists.
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
        _challengeCell         = Vector3Int.zero;
        _challengeIsStart      = false;

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
        phase = GamePhase.Build;
    }

    // Called by SurfaceUnit when it finishes its first traversal.
    public void EndRunningPhase()
    {
        if (phase != GamePhase.Running) return;

        // Promote the finished unit to a permanent ambient loop.
        currentUnit.SetLooping(true);
        loopingUnits.Add(currentUnit);

        // Retire the oldest loop layer if we've hit the limit.
        while (loopingUnits.Count > maxLoopLayers)
            RetireOldestLoop();

        // Add a new endpoint only every N completed runs.
        _runsSinceLastEndpoint++;
        if (_runsSinceLastEndpoint >= runsPerEndpoint)
        {
            _runsSinceLastEndpoint = 0;
            AddNextEndpoint();
        }

        ResourceManager.Instance?.SetCombatActive(false);  // stop turret regen, income in StartTurn
        phase = GamePhase.Build;
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
