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

    [Header("Turn")]
    public int blocksPerTurn = 3;
    public GamePhase phase;

    // Roguelite accumulation — all endpoint cells and live looping units
    readonly List<Vector3Int> allStarts    = new();
    readonly List<Vector3Int> allEnds      = new();
    readonly List<SurfaceUnit> loopingUnits = new();
    int roundIndex;   // how many extra endpoints have been added so far

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
        graph = new SurfaceGraphBuilder();
        endpoints.gridSystem = gridSystem;

        CreateFirstStage();

        phase = GamePhase.Build;
        StartTurn();
    }

    // ── Endpoint helpers ─────────────────────────────────────────────────────

    void ConfigureEndpointBounds(float extraRange = 0f)
    {
        endpoints.minDistance = Mathf.Max(2f, blocksPerTurn * 0.5f);
        endpoints.maxDistance = Mathf.Min(blocksPerTurn + 1f + extraRange, 9f);
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
    void AddNextEndpoint()
    {
        float extraRange = roundIndex * 0.5f;
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
            if (Input.GetKeyDown(KeyCode.P))
                BuildGraph();

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

                        ArpeggiatorManager.Instance.PlayAmbientNote(deg, oct, 0.28f);
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
            orig[i]   = rends[i].material.color;
            bright[i] = Color.Lerp(orig[i], Color.white, scanFlashBrightness);
            rends[i].material.color = bright[i];
        }

        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float s = Mathf.Clamp01(t / duration);
            s = s * s * (3f - 2f * s);

            for (int i = 0; i < n; i++)
                if (rends[i])
                    rends[i].material.color = Color.Lerp(bright[i], orig[i], s);

            yield return null;
        }

        for (int i = 0; i < n; i++)
            if (rends[i])
                rends[i].material.color = orig[i];

        _scanFlashing.Remove(obj);
    }

    // ── Turn / run flow ───────────────────────────────────────────────────────

    public void StartTurn()
    {
        placement.currentBlock = null;
        placement.mode = PlacementMode.Select;
        placement.SpawnRoundBlocks(blocksPerTurn);
    }

    void BuildGraph()
    {
        graph.SetData(gridSystem);
        graph.Build();
        phase = GamePhase.ReadyToRun;
    }

    void Run()
    {
        graph.SetData(gridSystem);
        graph.Build();

        List<FaceNode> startFaces, endFaces;

        if (_challengeCell == Vector3Int.zero)
        {
            // First stage — any start to any end
            startFaces = CollectFaces(allStarts);
            endFaces   = CollectFaces(allEnds);
        }
        else if (_challengeIsStart)
        {
            // Player must route FROM the newly added start to any existing end
            startFaces = CollectFacesFromGraph(graph, new List<Vector3Int> { _challengeCell });
            endFaces   = CollectFaces(allEnds);
        }
        else
        {
            // Player must route from any existing start TO the newly added end
            startFaces = CollectFaces(allStarts);
            endFaces   = CollectFacesFromGraph(graph, new List<Vector3Int> { _challengeCell });
        }

        if (startFaces.Count == 0 || endFaces.Count == 0) return;

        var path = SurfacePathfinding.FindPath(startFaces, endFaces);

        if (path == null)
        {
            Debug.Log("Path failed");
            phase = GamePhase.Build;
            return;
        }

        // Spawn unit at the actual path start, not necessarily startFaces[0]
        currentUnit = Instantiate(unitPrefab);
        currentUnit.gameFlow = this;
        currentUnit.transform.position = path[0].worldPos;
        currentUnit.SetPath(path);

        phase = GamePhase.Running;
    }

    // Called by SurfaceUnit when it finishes its first traversal.
    public void EndRunningPhase()
    {
        if (phase != GamePhase.Running) return;

        // Promote the finished unit to a permanent ambient loop
        currentUnit.SetLooping(true);
        loopingUnits.Add(currentUnit);

        AddNextEndpoint();

        phase = GamePhase.Build;
        StartTurn();
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
