using UnityEngine;
using UnityEngine.Rendering;

// Dashed grid overlay. Two display modes (switchable in inspector):
//
//   1. Follow Cursor (default):
//      • Centered on placement.SnappedGridPos
//      • Bounded radius in cells
//      • Smooth fade with distance from cursor → soft edge, not blocky
//      • Hidden outside Edit mode (so it never overlays the shop in Select)
//
//   2. Fixed Region:
//      • Spans GridSystem.size (or customSize)
//      • Same world position every frame
//      • Useful for "you can build inside this box" indication
public class GridOverlay : MonoBehaviour
{
    [Header("References")]
    public GridSystem grid;
    public PlacementController placement;
    [Tooltip("OrbitCamera providing FocusPoint — used as the grid anchor when not actively placing. Auto-found if null.")]
    public OrbitCamera cam;

    [Header("Mode")]
    [Tooltip("True: grid follows the cursor cell, radius-bounded with distance fade. False: static region driven by GridSystem.size / customSize.")]
    public bool followCursor = true;

    [Tooltip("If true (and followCursor is true), the grid is only drawn while placement.mode == Edit. Disable if you want the grid visible while browsing the shop too.")]
    public bool onlyInEditMode = false;

    [Tooltip("Hide the grid during the Running phase (waves) so it doesn't clutter combat. Recommended on.")]
    public bool hideInRunningPhase = true;

    [Header("Follow-Cursor mode")]
    [Range(1, 20)] public int radiusXZ = 6;
    [Tooltip("Smooth fade — 0 = fade starts from center, 1 = fade only at the very edge.")]
    [Range(0f, 1f)] public float fadeStartPct = 0.55f;

    [Header("Fixed-Region mode")]
    public bool useGridSize = true;
    public Vector3Int customSize    = new(10, 5, 10);
    public Vector3Int originOffset  = Vector3Int.zero;

    [Header("Layers")]
    [Tooltip("Draw a horizontal plane at every Y level (0..size.y). Off = floor / cursor plane only.")]
    public bool drawAllHorizontalPlanes = false;
    [Tooltip("Frame the region with vertical edge lines (fixed-region mode only).")]
    public bool drawVerticalWalls = false;

    [Header("Style")]
    public Color lineColor      = new(0.35f, 0.88f, 1.00f, 0.45f);
    public Color highlightColor = new(0.00f, 0.95f, 1.00f, 0.85f);

    [Header("Dashes")]
    [Range(0.02f, 0.5f)] public float dashLen = 0.15f;
    [Range(0.02f, 0.5f)] public float gapLen  = 0.12f;

    [Header("Cursor")]
    public bool showCursor = true;

    [Header("Toggle")]
    public bool    visible    = true;
    public KeyCode toggleKey  = KeyCode.G;

    Material _mat;

    void Awake()
    {
        _mat = new Material(Shader.Find("Hidden/Internal-Colored"))
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        _mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        _mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        _mat.SetInt("_Cull",     (int)CullMode.Off);
        _mat.SetInt("_ZWrite",   0);
        _mat.SetInt("_ZTest",    (int)CompareFunction.LessEqual);
    }

    void OnEnable()  => RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    void OnDisable() => RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
    void OnDestroy() { if (_mat) DestroyImmediate(_mat); }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey)) visible = !visible;
    }

    void OnEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        if (!visible || !_mat || grid == null) return;

        if (cam.cameraType   != CameraType.Game) return;
        if (cam.targetTexture != null)            return;
        if (cam.cullingMask  == 0)                return;

        // Edit-mode gate (when enabled).
        if (followCursor && onlyInEditMode)
        {
            if (placement == null || placement.mode != PlacementMode.Edit) return;
        }

        // Hide during waves so the grid doesn't clutter combat readability.
        if (hideInRunningPhase
            && GameFlowManager.Instance != null
            && GameFlowManager.Instance.phase == GamePhase.Running) return;

        _mat.SetPass(0);
        GL.PushMatrix();
        GL.MultMatrix(Matrix4x4.identity);

        if (followCursor) DrawFollowCursor();
        else              DrawFixedRegion();

        if (showCursor && placement != null && placement.mode == PlacementMode.Edit)
            DrawCursorBox(placement.SnappedGridPos, grid.cellSize);

        GL.PopMatrix();
    }

    // ── Follow-cursor mode: radius bounded, distance fade. ───────────────

    void DrawFollowCursor()
    {
        float cs = grid.cellSize;

        // Anchor strategy:
        //   • Edit mode → cursor cell (where the block will land — precise)
        //   • Else      → camera focus point (always valid + tracks WASD pan)
        //   • Fallback  → world origin
        Vector3Int cursorCell;
        if (placement != null && placement.mode == PlacementMode.Edit)
        {
            cursorCell = placement.SnappedGridPos;
        }
        else
        {
            if (cam == null) cam = FindFirstObjectByType<OrbitCamera>();
            cursorCell = cam != null ? grid.WorldToGrid(cam.FocusPoint) : Vector3Int.zero;
        }

        int planeY = cursorCell.y;
        Vector3 focus = grid.GridToWorld(cursorCell);

        int xMin = cursorCell.x - radiusXZ;
        int xMax = cursorCell.x + radiusXZ;
        int zMin = cursorCell.z - radiusXZ;
        int zMax = cursorCell.z + radiusXZ;

        float fadeEnd   = radiusXZ * cs;
        float fadeStart = fadeEnd * Mathf.Clamp01(fadeStartPct);

        GL.Begin(GL.LINES);

        for (int zi = zMin; zi <= zMax; zi++)
            DashFaded(new Vector3(xMin * cs, planeY * cs, zi * cs),
                      new Vector3(xMax * cs, planeY * cs, zi * cs),
                      focus, fadeStart, fadeEnd);

        for (int xi = xMin; xi <= xMax; xi++)
            DashFaded(new Vector3(xi * cs, planeY * cs, zMin * cs),
                      new Vector3(xi * cs, planeY * cs, zMax * cs),
                      focus, fadeStart, fadeEnd);

        GL.End();
    }

    // ── Fixed-region mode: covers GridSystem.size area. ──────────────────

    Vector3Int RegionSize => useGridSize && grid != null ? grid.size : customSize;

    void DrawFixedRegion()
    {
        float cs = grid.cellSize;
        var s = RegionSize;
        var o = originOffset;

        int xMin = o.x, xMax = o.x + s.x;
        int yMin = o.y, yMax = o.y + s.y;
        int zMin = o.z, zMax = o.z + s.z;

        GL.Begin(GL.LINES);

        int yLow  = yMin;
        int yHigh = drawAllHorizontalPlanes ? yMax : yMin;
        for (int y = yLow; y <= yHigh; y++)
        {
            float yw = y * cs;
            for (int zi = zMin; zi <= zMax; zi++)
                DashSolid(new Vector3(xMin * cs, yw, zi * cs),
                          new Vector3(xMax * cs, yw, zi * cs));
            for (int xi = xMin; xi <= xMax; xi++)
                DashSolid(new Vector3(xi * cs, yw, zMin * cs),
                          new Vector3(xi * cs, yw, zMax * cs));
        }

        if (drawVerticalWalls)
        {
            for (int xi = xMin; xi <= xMax; xi++)
            {
                DashSolid(new Vector3(xi * cs, yMin * cs, zMin * cs), new Vector3(xi * cs, yMax * cs, zMin * cs));
                DashSolid(new Vector3(xi * cs, yMin * cs, zMax * cs), new Vector3(xi * cs, yMax * cs, zMax * cs));
            }
            for (int zi = zMin; zi <= zMax; zi++)
            {
                DashSolid(new Vector3(xMin * cs, yMin * cs, zi * cs), new Vector3(xMin * cs, yMax * cs, zi * cs));
                DashSolid(new Vector3(xMax * cs, yMin * cs, zi * cs), new Vector3(xMax * cs, yMax * cs, zi * cs));
            }
        }

        GL.End();
    }

    // ── Dash helpers (must be called inside GL.Begin(GL.LINES)) ──────────

    void DashFaded(Vector3 from, Vector3 to, Vector3 focus, float fadeStart, float fadeEnd)
    {
        float len = Vector3.Distance(from, to);
        if (len < 0.001f) return;
        Vector3 dir = (to - from) / len;

        float t = 0f;
        bool  draw = true;
        while (t < len)
        {
            float step = draw ? dashLen : gapLen;
            float t2   = Mathf.Min(t + step, len);
            if (draw)
            {
                Vector3 a   = from + dir * t;
                Vector3 b   = from + dir * t2;
                Vector3 mid = (a + b) * 0.5f;
                float   d   = Vector3.Distance(mid, focus);
                float   fade = 1f - Mathf.SmoothStep(fadeStart, fadeEnd, d);
                if (fade > 0.01f)
                {
                    Color c = lineColor; c.a *= fade;
                    GL.Color(c);
                    GL.Vertex(a);
                    GL.Vertex(b);
                }
            }
            t = t2;
            draw = !draw;
        }
    }

    void DashSolid(Vector3 from, Vector3 to)
    {
        float len = Vector3.Distance(from, to);
        if (len < 0.001f) return;
        Vector3 dir = (to - from) / len;

        float t = 0f;
        bool  draw = true;
        while (t < len)
        {
            float step = draw ? dashLen : gapLen;
            float t2   = Mathf.Min(t + step, len);
            if (draw)
            {
                GL.Color(lineColor);
                GL.Vertex(from + dir * t);
                GL.Vertex(from + dir * t2);
            }
            t = t2;
            draw = !draw;
        }
    }

    void DrawCursorBox(Vector3Int cell, float cs)
    {
        float x0 = cell.x * cs, y0 = cell.y * cs, z0 = cell.z * cs;
        float x1 = x0 + cs,    y1 = y0 + cs,     z1 = z0 + cs;

        var v = new Vector3[8]
        {
            new(x0,y0,z0), new(x1,y0,z0), new(x1,y0,z1), new(x0,y0,z1),
            new(x0,y1,z0), new(x1,y1,z0), new(x1,y1,z1), new(x0,y1,z1),
        };

        GL.Begin(GL.LINES);
        GL.Color(highlightColor);
        L(v[0],v[1]); L(v[1],v[2]); L(v[2],v[3]); L(v[3],v[0]);
        L(v[4],v[5]); L(v[5],v[6]); L(v[6],v[7]); L(v[7],v[4]);
        L(v[0],v[4]); L(v[1],v[5]); L(v[2],v[6]); L(v[3],v[7]);
        GL.End();
    }

    static void L(Vector3 a, Vector3 b) { GL.Vertex(a); GL.Vertex(b); }
}
