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
    List<(Vector3Int start, Vector3Int end)> stages = new();
    int currentStageIndex = 0;
    private SurfaceUnit currentUnit;
    public GamePhase phase;

    [Header("Ambient Scan")]
    public int scanBeats = 8;
    [Range(0f, 1f)] public float scanFlashBrightness = 0.7f;

    float _scanTimer;
    bool _scanning;
    readonly HashSet<GameObject> _scanFlashing = new();

    void Start()
    {
        graph = new SurfaceGraphBuilder();
        endpoints.gridSystem = gridSystem;

        CreateFirstStage();

        phase = GamePhase.Build;
        StartTurn();
    }

    void ConfigureEndpointBounds()
    {
        endpoints.minDistance = Mathf.Max(2f, blocksPerTurn * 0.5f);
        endpoints.maxDistance = blocksPerTurn + 1f;
    }

    void CreateFirstStage()
    {
        ConfigureEndpointBounds();
        endpoints.Generate();
        stages.Add((endpoints.startCell, endpoints.endCell));
    }

    void AddNextStage()
    {
        ConfigureEndpointBounds();
        endpoints.Generate();
        stages.Add((endpoints.startCell, endpoints.endCell));
    }

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

    IEnumerator PathPulseScan()
    {
        _scanning = true;

        var scanGraph = new SurfaceGraphBuilder();
        scanGraph.SetData(gridSystem);
        scanGraph.Build();

        var stage = stages[currentStageIndex];
        var startF = scanGraph.GetFaceNodes(stage.start);
        var endF = scanGraph.GetFaceNodes(stage.end);

        if (startF != null && endF != null && startF.Count > 0 && endF.Count > 0)
        {
            var path = SurfacePathfinding.FindPath(startF, endF);

            if (path != null && path.Count > 0)
            {
                float secPerBeat = 60f / unitPrefab.bpm;
                float stepSec = Mathf.Clamp(secPerBeat * scanBeats / path.Count, 0.08f, 0.72f);

                foreach (var node in path)
                {
                    var inst = gridSystem.GetInstanceAt(node.cell);

                    if (inst?.data != null)
                    {
                        int root = RootDegree(inst.data.blockType);

                        int yIndex = Mathf.Clamp(node.cell.y, 0, YDegreeMap.Length - 1);
                        int offset = YDegreeMap[yIndex];

                        // 更安全的degree循环
                        int finalDegree = ((root + offset - 2) % 7 + 7) % 7 + 1;

                        int octave = Mathf.Clamp(node.cell.y - 1, -1, 3);

                        ArpeggiatorManager.Instance.PlayAmbientNote(
                            finalDegree,
                            octave,
                            0.28f
                        );

                        // 👉 让scan也有“生命感”
                        BackgroundReactor.Instance?.OnNote(0.2f);

                        if (inst.visualObject != null)
                            StartCoroutine(ScanFlashBlock(inst.visualObject, stepSec * 0.7f));
                    }

                    yield return new WaitForSeconds(stepSec);

                    if (phase != GamePhase.Build)
                        break;
                }
            }
        }

        _scanning = false;
    }

    // ✔ 完整合法定义（你之前这里断了）
    static readonly int[] YDegreeMap = { 1, 2, 4, 5, 7 };

    static int RootDegree(BlockType t) => t switch
    {
        BlockType.Home => 1,
        BlockType.Lift => 4,
        BlockType.Pull => 5,
        BlockType.Shadow => 7,
        _ => 1,
    };

    IEnumerator ScanFlashBlock(GameObject obj, float duration)
    {
        if (obj == null || !_scanFlashing.Add(obj)) yield break;

        var rends = obj.GetComponentsInChildren<Renderer>();
        int n = rends.Length;

        var orig = new Color[n];
        var bright = new Color[n];

        for (int i = 0; i < n; i++)
        {
            orig[i] = rends[i].material.color;
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

        if (startFaces == null || endFaces == null || startFaces.Count == 0 || endFaces.Count == 0)
            return;

        var path = SurfacePathfinding.FindPath(startFaces, endFaces);

        if (path == null)
        {
            Debug.Log("Path failed");
            phase = GamePhase.Build;
            return;
        }

        if (currentUnit != null)
            Destroy(currentUnit.gameObject);

        currentUnit = Instantiate(unitPrefab);
        currentUnit.gameFlow = this;
        currentUnit.transform.position = startFaces[0].worldPos;
        currentUnit.SetPath(path);

        phase = GamePhase.Running;
    }

    public void EndRunningPhase()
    {
        if (phase != GamePhase.Running) return;

        currentStageIndex++;

        if (currentStageIndex >= stages.Count)
            AddNextStage();

        phase = GamePhase.Build;
        StartTurn();
    }
}