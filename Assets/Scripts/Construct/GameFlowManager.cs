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

    private SurfaceUnit currentUnit;
    public GamePhase phase;

    void Start()
    {
        graph = new SurfaceGraphBuilder();
        endpoints.gridSystem = gridSystem;
        endpoints.Generate();

        phase = GamePhase.Build;
        StartTurn();
    }

    void Update()
    {
        if (phase == GamePhase.Build)
        {
            if (Input.GetKeyDown(KeyCode.P))
            {
                BuildGraph();
            }

            if (placement.currentBlock == null && FindObjectsOfType<SelectableBlock>().Length == 0)
            {
                StartTurn();
            }
        }

        if (phase == GamePhase.ReadyToRun)
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                Run();
            }

            if (Input.GetKeyDown(KeyCode.B))
            {
                phase = GamePhase.Build;
                StartTurn();
            }
        }
    }

    public void StartTurn()
    {
        placement.currentBlock = null;
        placement.mode = PlacementMode.Select;
        placement.SpawnRoundBlocks(Random.Range(2, 5));
    }

    void BuildGraph()
    {
        graph.SetData(gridSystem);
        graph.Build();
        phase = GamePhase.ReadyToRun;
    }

    void Run()
    {
        var startFaces = graph.GetFaceNodes(endpoints.startCell);
        var endFaces = graph.GetFaceNodes(endpoints.endCell);

        if (startFaces == null || startFaces.Count == 0) { Debug.Log("Invalid start face"); return; }
        if (endFaces == null || endFaces.Count == 0) { Debug.Log("Invalid end face"); return; }

        var path = SurfacePathfinding.FindPath(startFaces, endFaces);

        if (path == null)
        {
            Debug.Log("No path found - Back to Build Mode");
            phase = GamePhase.Build;
            return;
        }

        if (currentUnit != null) Destroy(currentUnit.gameObject);

        currentUnit = Instantiate(unitPrefab);
        currentUnit.transform.position = startFaces[0].worldPos;
        currentUnit.SetPath(path);

        phase = GamePhase.Running;
    }

    public void EndRunningPhase()
    {
        if (phase == GamePhase.Running)
        {
            phase = GamePhase.Build;
            StartTurn();
        }
    }
}