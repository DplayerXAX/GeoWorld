using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 每条完成的路径用 LineRenderer 画一条激光线。
// 场景里挂到任意 GameObject，把 PathLaser 材质赋给 laserMaterial。
//
// 两类线：
//   Live line  — 放置方块时自动预览当前最优路径，Space 发车后消失
//   Loop lines — 每次跑完路径后生成，按覆盖 cell 追踪，方块被捡起时移除
public class PathFlowManager : MonoBehaviour
{
    public static PathFlowManager Instance;

    [Header("激光线")]
    public Material laserMaterial;
    [Range(0.02f, 0.3f)] public float lineWidth    = 0.055f;
    [Range(0f,    1f)]   public float heightOffset = 0.04f;

    [Header("Live 预览线颜色")]
    public Color livePathColor = new Color(1f, 1f, 0.70f, 0.90f);   // 暖白

    // 已完成路径的颜色循环
    static readonly Color[] PathColors =
    {
        new Color(0.25f, 0.90f, 1.00f),   // 青
        new Color(1.00f, 0.72f, 0.18f),   // 琥珀
        new Color(0.38f, 1.00f, 0.52f),   // 薄荷
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

    // ── Live preview line ─────────────────────────────────────────────────────
    GameObject _liveGO;
    Coroutine  _liveCoroutine;

    void Awake() => Instance = this;

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
        ClearLiveLine();
        if (laserMaterial == null || path == null || path.Count < 2) return;

        var lr = MakeLine("PathLaser_Live", livePathColor, out _liveGO);
        _liveCoroutine = StartCoroutine(RevealLine(lr, BuildPositions(path)));
    }

    // Removes the live preview line (called when Run() commits the path).
    public void ClearLiveLine()
    {
        if (_liveCoroutine != null) { StopCoroutine(_liveCoroutine); _liveCoroutine = null; }
        if (_liveGO != null) { Destroy(_liveGO); _liveGO = null; }
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
