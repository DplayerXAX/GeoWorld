using UnityEngine;
using UnityEngine.Rendering;

// Dashed grid that follows the camera focus and fades with distance.
public class GridOverlay : MonoBehaviour
{
    [Header("References")]
    public GridSystem grid;
    public PlacementController placement;
    [Tooltip("Camera whose focus point we orbit around. If null, falls back to Camera.main.transform.position.")]
    public OrbitCamera cam;

    [Header("Range (in cells, around focus)")]
    [Range(1, 20)] public int radiusXZ = 8;
    [Tooltip("Fraction of the radius at which dashes start fading. 0 = fade from the centre, 1 = fade only at the edge.")]
    [Range(0f, 1f)] public float fadeStartPct = 0.4f;

    [Header("颜色")]
    public Color lineColor      = new(0.35f, 0.88f, 1.00f, 0.45f);
    public Color highlightColor = new(0.00f, 0.95f, 1.00f, 0.85f);

    [Header("虚线")]
    [Range(0.02f, 0.5f)] public float dashLen = 0.15f;
    [Range(0.02f, 0.5f)] public float gapLen  = 0.12f;

    [Header("显示")]
    public bool showCursor = true;
    public bool visible    = true;

    [Header("按键")]
    public KeyCode toggleKey = KeyCode.G;

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

    void OnEnable()
    {
        // URP RenderGraph no longer invokes legacy OnRenderObject reliably.
        // Subscribe to URP's end-camera event instead — fires once per active
        // camera in both Compatibility and RenderGraph modes.
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }

    void OnDisable()
    {
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
    }

    void OnDestroy() { if (_mat) DestroyImmediate(_mat); }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            visible = !visible;
    }

    Vector3 GetFocus()
    {
        if (cam == null) cam = FindObjectOfType<OrbitCamera>();
        if (cam != null) return cam.FocusPoint;
        if (placement != null && grid != null)
            return grid.GridToWorld(placement.SnappedGridPos);
        return Vector3.zero;
    }

    void OnEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        if (!visible || !_mat || grid == null) return;

        // Filter out helper cameras so we only draw on the gameplay screen view:
        //   - Skip non-Game cameras (Scene preview, RT cams).
        //   - Skip the perspective skybox child (cullingMask == 0).
        //   - Skip cameras that render to off-screen RTs (Shop).
        if (cam.cameraType   != CameraType.Game) return;
        if (cam.targetTexture != null)            return;
        if (cam.cullingMask  == 0)                return;

        _mat.SetPass(0);
        GL.PushMatrix();
        GL.MultMatrix(Matrix4x4.identity);

        float cs = grid.cellSize;
        Vector3 focus = GetFocus();
        Vector3Int focusCell = grid.WorldToGrid(focus);

        // Single horizontal plane at the cursor's Y (or focus Y if no cursor).
        // Reads cleanly under the ortho iso projection — no 3D lattice clutter.
        int planeY = placement != null ? placement.SnappedGridPos.y : focusCell.y;

        int xMin = focusCell.x - radiusXZ;
        int xMax = focusCell.x + radiusXZ + 1;
        int zMin = focusCell.z - radiusXZ;
        int zMax = focusCell.z + radiusXZ + 1;

        float fadeEnd   = radiusXZ * cs;
        float fadeStart = fadeEnd * Mathf.Clamp01(fadeStartPct);

        GL.Begin(GL.LINES);

        // X-direction lines (run along world X)
        for (int zi = zMin; zi <= zMax; zi++)
            Dash(new Vector3(xMin * cs, planeY * cs, zi * cs),
                 new Vector3(xMax * cs, planeY * cs, zi * cs),
                 focus, fadeStart, fadeEnd);

        // Z-direction lines (run along world Z)
        for (int xi = xMin; xi <= xMax; xi++)
            Dash(new Vector3(xi * cs, planeY * cs, zMin * cs),
                 new Vector3(xi * cs, planeY * cs, zMax * cs),
                 focus, fadeStart, fadeEnd);

        GL.End();

        // Cursor box only while actively placing — hiding it in Select keeps
        // the view clean when the player is just looking around.
        if (showCursor && placement != null && placement.mode == PlacementMode.Edit)
            DrawCursorBox(placement.SnappedGridPos, cs);

        GL.PopMatrix();
    }

    // Must be called inside GL.Begin(GL.LINES). Each dash fades by distance to focus.
    void Dash(Vector3 from, Vector3 to, Vector3 focus, float fadeStart, float fadeEnd)
    {
        float len = Vector3.Distance(from, to);
        if (len < 0.001f) return;

        Vector3 dir  = (to - from) / len;
        float   t    = 0f;
        bool    draw = true;

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
                    Color c = lineColor;
                    c.a *= fade;
                    GL.Color(c);
                    GL.Vertex(a);
                    GL.Vertex(b);
                }
            }
            t    = t2;
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
