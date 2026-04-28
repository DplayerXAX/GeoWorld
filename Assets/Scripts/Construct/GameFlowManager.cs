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

    SurfaceUnit currentUnit;

    public GamePhase phase;

    void Start()
    {
        graph = new SurfaceGraphBuilder();
        endpoints.gridSystem = gridSystem;
        //generate endpoints first
        endpoints.Generate();

        phase = GamePhase.Build;
    }

    void Update()
    {
        if (phase == GamePhase.Build)
        {
            if (Input.GetKeyDown(KeyCode.P))
            {
                BuildGraph();
            }
        }

        if (phase == GamePhase.ReadyToRun)
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                Run();
            }
        }
    }

    void BuildGraph()
    {
        graph.SetData(gridSystem);
        graph.Build();

        phase = GamePhase.ReadyToRun;
    }

    void Run()
    {
        var startNode = graph.GetFaceNode(endpoints.startCell);
        var endNode = graph.GetFaceNode(endpoints.endCell);

        if (startNode == null)
        {
            Debug.Log("Invalid start face");
            return;
        }

        if (endNode == null)
        {
            Debug.Log("Invalid end face");
            return;
        }

        var path = SurfacePathfinding.FindPath(startNode, endNode);

        if (path == null)
        {
            Debug.Log("No path");
            return;
        }

        if (currentUnit != null)
            Destroy(currentUnit.gameObject);


        Debug.Log("Start running!");
        currentUnit = Instantiate(unitPrefab);
        currentUnit.transform.position = startNode.worldPos;
        currentUnit.SetPath(path);

        //phase = GamePhase.Running;
    }
}