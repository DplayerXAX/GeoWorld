using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PathFlowManager : MonoBehaviour
{
    public static PathFlowManager Instance;

    [Header("Laser line")]
    public Material laserMaterial;
    [Range(0.02f, 0.3f)] public float lineWidth    = 0.15f;   // thin lines read as decoration, not as "the enemy comes THIS way"
    [Range(0f,    1f)]   public float heightOffset = 0.05f;

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
        new Color(1.00f, 0.35f, 0.72f),   // pink
        new Color(0.78f, 0.46f, 1.00f),   // purple
        new Color(0.28f, 1.00f, 0.72f),   // teal
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
    public void UpdateLiveLines(List<List<FaceNode>> paths) => UpdateLiveLines(paths, null);

    // `coverage`: per route, per node, how many turrets can hit an enemy there
    // (RouteCoverage). When given, each route is drawn in runs coloured by it — red
    // where nothing can hit, amber for one turret, green for two or more — so the
    // route doubles as the answer to "is my defence where the enemies are".
    public void UpdateLiveLines(List<List<FaceNode>> paths, List<int[]> coverage)
    {
        ClearLiveLine();
        if (laserMaterial == null || paths == null) return;

        for (int pi = 0; pi < paths.Count; pi++)
        {
            var path = paths[pi];
            if (path == null || path.Count < 2) continue;

            int[] cov = coverage != null && pi < coverage.Count ? coverage[pi] : null;
            if (cov == null || cov.Length != path.Count)
            {
                var lr0 = MakeLine("PathLaser_Live", livePathColor, out GameObject go0);
                _liveGOs.Add(go0);
                _liveCoroutines.Add(StartCoroutine(RevealLine(lr0, BuildPositions(path))));
                continue;
            }

            var pts = BuildPositionsMapped(path, out int[] posIdx);
            int a = 0;
            while (a < path.Count)
            {
                int cls = RouteCoverage.Class(cov[a]);
                int b = a;
                while (b + 1 < path.Count && RouteCoverage.Class(cov[b + 1]) == cls) b++;

                // A run starts where the previous one ended, so the line stays whole.
                int from = a == 0 ? 0 : posIdx[a - 1];
                int to   = posIdx[b];
                if (to > from)
                {
                    var seg = new Vector3[to - from + 1];
                    System.Array.Copy(pts, from, seg, 0, seg.Length);
                    var lr = MakeCoverageLine(cls, out GameObject go);
                    _liveGOs.Add(go);
                    _liveCoroutines.Add(StartCoroutine(RevealLine(lr, seg)));
                }
                a = b + 1;
            }
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

    // Keeps exactly one loop line per route in `paths`: a line whose route no
    // longer exists is removed (the board was rebuilt, a route moved, a spawn
    // was cut off), and with addMissing a route with no line yet gets one. Lines
    // used to go only when a block under them was LIFTED, so a route changed any
    // other way — a block placed across it, a reroute mid-wave — left its old
    // line behind, and every wave stacked another on the same route.
    public void SyncFlows(List<List<FaceNode>> paths, bool addMissing)
    {
        var want = new List<(List<FaceNode> path, HashSet<Vector3Int> cells)>();
        if (paths != null)
            foreach (var p in paths)
            {
                if (p == null || p.Count < 2) continue;
                var set = new HashSet<Vector3Int>();
                foreach (var n in p) set.Add(n.cell);
                want.Add((p, set));
            }

        var covered = new bool[want.Count];
        for (int i = _flows.Count - 1; i >= 0; i--)
        {
            int match = -1;
            for (int k = 0; k < want.Count && match < 0; k++)
                if (!covered[k] && _flows[i].cells.SetEquals(want[k].cells)) match = k;
            if (match >= 0) { covered[match] = true; continue; }

            if (_flows[i].revealCoroutine != null) StopCoroutine(_flows[i].revealCoroutine);
            if (_flows[i].go != null) Destroy(_flows[i].go);
            _flows.RemoveAt(i);
        }

        if (!addMissing) return;
        for (int k = 0; k < want.Count; k++)
            if (!covered[k]) AddFlow(want[k].path);
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

    // The coverage runs share ONE material per class (a route is cut into many runs
    // and is redrawn on every board edit — an instance each would pile up).
    readonly Material[] _covMats = new Material[3];

    LineRenderer MakeCoverageLine(int cls, out GameObject go)
    {
        go = new GameObject("PathLaser_Live");
        go.transform.SetParent(transform, false);

        if (_covMats[cls] == null)
        {
            var m = new Material(laserMaterial);
            m.SetColor("_Color", RouteCoverage.ColorOf(cls));
            m.SetFloat(ZTestId, _xrayActive ? ZTestXray : ZTestNormal);
            _lineMats.Add(m);   // so the x-ray toggle reaches it too
            _covMats[cls] = m;
        }

        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial    = _covMats[cls];
        lr.useWorldSpace     = true;
        lr.positionCount     = 0;
        lr.startWidth        = lineWidth;
        lr.endWidth          = lineWidth;
        lr.numCapVertices    = 6;
        lr.numCornerVertices = 6;
        lr.textureMode       = LineTextureMode.Tile;
        return lr;
    }

    // FaceNode path -> world-position array, inserting corner points where
    // the normal changes.
    Vector3[] BuildPositions(List<FaceNode> path) => BuildPositionsMapped(path, out _);

    // As BuildPositions, and posIdx[i] = where in the result path node i landed (the
    // corner points inserted between nodes mean it is not simply i).
    Vector3[] BuildPositionsMapped(List<FaceNode> path, out int[] posIdx)
    {
        posIdx = new int[path.Count];
        var   gs   = GridSystem.instance;
        float fOff = gs.cellSize * 0.5f + heightOffset;

        var raw = new Vector3[path.Count];
        for (int i = 0; i < path.Count; i++)
            raw[i] = gs.GridToWorld(path[i].cell) + path[i].normal * fOff;

        var pts = new List<Vector3> { raw[0] };
        posIdx[0] = 0;
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
            posIdx[i] = pts.Count - 1;
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
