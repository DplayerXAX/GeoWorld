using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PathFlowManager : MonoBehaviour
{
    public static PathFlowManager Instance;

    [Header("Laser line")]
    public Material laserMaterial;
    [Range(0.02f, 0.3f)] public float lineWidth    = 0.085f;   // slightly thicker than before
    [Range(0f,    1f)]   public float heightOffset = 0.04f;

    [Header("X-ray (hold middle mouse)")]
    [Tooltip("Mouse button that reveals every path line through walls/blocks while held (2 = middle).")]
    public int xrayMouseButton = 2;
    static readonly int ZTestId = Shader.PropertyToID("_ZTest");
    const float ZTestNormal = 4f;   // LEqual — occluded by geometry (default)
    const float ZTestXray   = 8f;   // Always — draws through everything
    bool _xrayActive;

    [Header("Live path")]
    public Color livePathColor = new Color(1f, 1f, 0.70f, 0.90f);   

    static readonly Color[] PathColors =
    {
        new Color(0.25f, 0.90f, 1.00f),   
        new Color(1.00f, 0.72f, 0.18f),  
        new Color(0.38f, 1.00f, 0.52f),  
        new Color(1.00f, 0.35f, 0.72f),   // 粉红
        new Color(0.78f, 0.46f, 1.00f),   // 紫
        new Color(0.28f, 1.00f, 0.72f),   // 青绿
    };

    int _count;

    // ── Loop line tracking ────────────────────────────────────────────────────
    class FlowEntry
    {
        public GameObject          go;
        public HashSet<Vector3Int> cells;
        public Coroutine           revealCoroutine;
    }
    readonly List<FlowEntry> _flows = new();

    // ── Live preview lines (one per spawn point) ───────────────────────────────
    readonly List<GameObject> _liveGOs        = new();
    readonly List<Coroutine>  _liveCoroutines = new();

    // Every per-line material instance ever created (MakeLine), so the x-ray toggle
    // can flip ZTest on all of them at once regardless of which list they live in.
    readonly List<Material> _lineMats = new();

    void Awake() => Instance = this;

    void Update()
    {
        bool held = Input.GetMouseButton(xrayMouseButton);
        if (held == _xrayActive) return;
        _xrayActive = held;

        float zt = held ? ZTestXray : ZTestNormal;
        for (int i = _lineMats.Count - 1; i >= 0; i--)
        {
            var m = _lineMats[i];
            if (m == null) { _lineMats.RemoveAt(i); continue; }
            m.SetFloat(ZTestId, zt);
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    // Adds a completed-run laser line tracked by the cells it passes through.
    // Call this when a traversal finishes (the line persists as an ambient loop line).
    public void AddFlow(List<FaceNode> path)
    {
        if (laserMaterial == null || path == null || path.Count < 2) return;

        Color col = PathColors[_count % PathColors.Length];
        _count++;

        var lr = MakeLine($"PathLaser_{_count}", col, out GameObject go);

        var cellSet = new HashSet<Vector3Int>();
        foreach (var n in path) cellSet.Add(n.cell);

        var entry = new FlowEntry { go = go, cells = cellSet };
        entry.revealCoroutine = StartCoroutine(RevealLine(lr, BuildPositions(path)));
        _flows.Add(entry);
    }

    // Updates (or clears) the live preview line shown while the player is building.
    // Pass null to hide the preview.
    public void UpdateLiveLine(List<FaceNode> path)
    {
        if (path == null) { ClearLiveLine(); return; }
        UpdateLiveLines(new List<List<FaceNode>> { path });
    }

    // Shows a live preview line for EVERY spawn-point path (so all routes redraw on
    // each block placement, not just the last one). Pass null/empty to hide.
    public void UpdateLiveLines(List<List<FaceNode>> paths)
    {
        ClearLiveLine();
        if (laserMaterial == null || paths == null) return;

        foreach (var path in paths)
        {
            if (path == null || path.Count < 2) continue;
            var lr = MakeLine("PathLaser_Live", livePathColor, out GameObject go);
            _liveGOs.Add(go);
            _liveCoroutines.Add(StartCoroutine(RevealLine(lr, BuildPositions(path))));
        }
    }

    // Removes the live preview lines (called when Run() commits the path).
    public void ClearLiveLine()
    {
        foreach (var c in _liveCoroutines) if (c != null) StopCoroutine(c);
        _liveCoroutines.Clear();
        foreach (var g in _liveGOs) if (g != null) Destroy(g);
        _liveGOs.Clear();
    }

    // Removes all loop lines whose path overlaps any of the given cells.
    // Called when a block is lifted from the grid.
    public void RemoveFlowsOverlapping(IEnumerable<Vector3Int> cells)
    {
        var check = new HashSet<Vector3Int>(cells);
        for (int i = _flows.Count - 1; i >= 0; i--)
        {
            if (!_flows[i].cells.Overlaps(check)) continue;
            if (_flows[i].revealCoroutine != null) StopCoroutine(_flows[i].revealCoroutine);
            if (_flows[i].go != null) Destroy(_flows[i].go);
            _flows.RemoveAt(i);
        }
    }

    public void ClearAll()
    {
        ClearLiveLine();
        foreach (var e in _flows)
        {
            if (e.revealCoroutine != null) StopCoroutine(e.revealCoroutine);
            if (e.go != null) Destroy(e.go);
        }
        _flows.Clear();
        _count = 0;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    LineRenderer MakeLine(string name, Color col, out GameObject go)
    {
        go = new GameObject(name);
        go.transform.SetParent(transform, false);

        var lr  = go.AddComponent<LineRenderer>();
        var mat = new Material(laserMaterial);
        mat.SetColor("_Color", col);
        mat.SetFloat(ZTestId, _xrayActive ? ZTestXray : ZTestNormal);
        _lineMats.Add(mat);
        lr.material          = mat;
        lr.useWorldSpace     = true;
        lr.positionCount     = 0;
        lr.startWidth        = lineWidth;
        lr.endWidth          = lineWidth;
        lr.numCapVertices    = 6;
        lr.numCornerVertices = 6;
        lr.textureMode       = LineTextureMode.Tile;
        return lr;
    }

    // FaceNode 路径 → 世界坐标数组，在法线变化处插入拐角路径点。
    Vector3[] BuildPositions(List<FaceNode> path)
    {
        var   gs   = GridSystem.instance;
        float fOff = gs.cellSize * 0.5f + heightOffset;

        var raw = new Vector3[path.Count];
        for (int i = 0; i < path.Count; i++)
            raw[i] = gs.GridToWorld(path[i].cell) + path[i].normal * fOff;

        var pts = new List<Vector3> { raw[0] };
        for (int i = 1; i < raw.Length; i++)
        {
            Vector3 nA = path[i - 1].normal;
            Vector3 nB = path[i].normal;

            if (Vector3.Angle(nA, nB) > 5f)
            {
                Vector3 p0       = raw[i - 1];
                Vector3 p1       = raw[i];
                Vector3 nAcomp   = Vector3.Project(p0, nA);
                Vector3 nBcomp   = Vector3.Project(p1, nB);
                Vector3 edgeComp = p0 - nAcomp - Vector3.Project(p0, nB);
                pts.Add(nAcomp + nBcomp + edgeComp);
            }
            pts.Add(raw[i]);
        }
        return pts.ToArray();
    }

    IEnumerator RevealLine(LineRenderer lr, Vector3[] positions)
    {
        int total = positions.Length;
        int shown = 0;
        lr.positionCount = 0;

        while (shown < total)
        {
            if (lr == null) yield break;   // GO destroyed mid-reveal
            int next = Mathf.Min(shown + Mathf.Max(1, total / 20), total);
            lr.positionCount = next;
            for (int i = shown; i < next; i++)
                lr.SetPosition(i, positions[i]);
            shown = next;
            yield return null;
        }
    }
}
