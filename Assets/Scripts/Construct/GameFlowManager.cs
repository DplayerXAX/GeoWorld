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
    List<(Vector3Int start, Vector3Int end)> stages = new();
    int currentStageIndex = 0;
    private SurfaceUnit currentUnit;
    public GamePhase phase;

    void Start()
    {
        graph = new SurfaceGraphBuilder();
        endpoints.gridSystem = gridSystem;

        CreateFirstStage();

        phase = GamePhase.Build;
        StartTurn();
    }
    void CreateFirstStage()
    {
        endpoints.Generate();
        stages.Add((endpoints.startCell, endpoints.endCell));
    }

    void AddNextStage()
    {
        endpoints.Generate();

        stages.Add((endpoints.startCell, endpoints.endCell));

    }
    void Update()
    {
        if (phase == GamePhase.Build)
        {
            // P confirms the current layout and attempts to run the path.
            // Blocks do NOT refresh on tray-empty — the puzzle is solved
            // with the fixed set issued at stage start.
            if (Input.GetKeyDown(KeyCode.P))
                BuildGraph();
        }

        if (phase == GamePhase.ReadyToRun)
        {
            if (Input.GetKeyDown(KeyCode.Space))
                Run();

            // B lets the player return to build mode to rearrange before confirming.
            if (Input.GetKeyDown(KeyCode.B))
                phase = GamePhase.Build;
        }
    }

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

        var stage = stages[currentStageIndex];

        var startFaces = graph.GetFaceNodes(stage.start);
        var endFaces = graph.GetFaceNodes(stage.end);

        if (startFaces == null || startFaces.Count == 0) return;
        if (endFaces == null || endFaces.Count == 0) return;

        var path = SurfacePathfinding.FindPath(startFaces, endFaces);

        if (path == null)
        {
            Debug.Log("Path failed");
            phase = GamePhase.Build;
            return;
        }

        if (currentUnit != null) Destroy(currentUnit.gameObject);

        currentUnit = Instantiate(unitPrefab);
        currentUnit.gameFlow = this;    // inject so OnPathFinished can call back
        currentUnit.transform.position = startFaces[0].worldPos;
        currentUnit.SetPath(path);

        phase = GamePhase.Running;
    }

    public void EndRunningPhase()
    {
        if (phase != GamePhase.Running) return;

        currentStageIndex++;

        // Generate next stage's start/end pair (existing stages remain in the list
        // so future replays could re-run earlier paths if needed).
        if (currentStageIndex >= stages.Count)
            AddNextStage();

        phase = GamePhase.Build;
        StartTurn();   // reset mode + issue new block set for the next segment
    }
}