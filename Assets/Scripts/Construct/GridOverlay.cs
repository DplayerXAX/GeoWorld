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
    [Range(1, 20)] public int radiusXZ = 3;
    [Range(1, 10)] public int radiusY  = 2;
    [Tooltip("Fraction of the radius at which dashes start fading. 0 = fade from the centre, 1 = fade only at the edge.")]
    [Range(0f, 1f)] public float fadeStartPct = 0.35f;

    [Header("颜色")]
    public Color lineColor      = new(0.35f, 0.88f, 1.00f, 0.45f);
    public Color highlightColor = new(0.00f, 0.95f, 1.00f, 0.85f);

    [Header("虚线")]
    [Range(0.02f, 0.5f)] public float dashLen = 0.15f;
    [Range(0.02f, 0.5f)] public float gapLen  = 0.12f;

    [Header("显示")]
    public bool showXZ = true;
    public bool showY  = true;
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

    void OnRenderObject()
    {
        if (!visible || !_mat || grid == null) return;
        // OnRenderObject fires per-camera. Limit drawing to the main gameplay
        // camera; otherwise the shop / skybox cameras project the same world
        // coords through different matrices and we get ghost copies on screen.
        if (Camera.current != Camera.main) return;

        _mat.SetPass(0);
        GL.PushMatrix();
        GL.MultMatrix(Matrix4x4.identity);

        float cs = grid.cellSize;
        Vector3 focus = GetFocus();
        Vector3Int focusCell = grid.WorldToGrid(focus);

        int xMin = focusCell.x - radiusXZ;
        int xMax = focusCell.x + radiusXZ + 1;
        int yMin = focusCell.y - radiusY;
        int yMax = focusCell.y + radiusY + 1;
        int zMin = focusCell.z - radiusXZ;
        int zMax = focusCell.z + radiusXZ + 1;

        float fadeEnd   = Mathf.Max(radiusXZ, radiusY) * cs;
        float fadeStart = fadeEnd * Mathf.Clamp01(fadeStartPct);

        GL.Begin(GL.LINES);

        if (showXZ)
        {
            for (int yi = yMin; yi <= yMax; yi++)
            for (int zi = zMin; zi <= zMax; zi++)
                Dash(new Vector3(xMin*cs, yi*cs, zi*cs),
                     new Vector3(xMax*cs, yi*cs, zi*cs),
                     focus, fadeStart, fadeEnd);

            for (int xi = xMin; xi <= xMax; xi++)
            for (int yi = yMin; yi <= yMax; yi++)
                Dash(new Vector3(xi*cs, yi*cs, zMin*cs),
                     new Vector3(xi*cs, yi*cs, zMax*cs),
                     focus, fadeStart, fadeEnd);
        }

        if (showY)
        {
            for (int xi = xMin; xi <= xMax; xi++)
            for (int zi = zMin; zi <= zMax; zi++)
                Dash(new Vector3(xi*cs, yMin*cs, zi*cs),
                     new Vector3(xi*cs, yMax*cs, zi*cs),
                     focus, fadeStart, fadeEnd);
        }

        GL.End();

        if (showCursor && placement != null)
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
