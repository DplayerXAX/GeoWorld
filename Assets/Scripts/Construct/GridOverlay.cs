using UnityEngine;
using UnityEngine.Rendering;

// 三轴虚线网格，边界自动对齐 GridSystem.size。
// 外框线较亮，内部线极淡，形成一个透明的立体格子感。
public class GridOverlay : MonoBehaviour
{
    [Header("References")]
    public GridSystem grid;
    public PlacementController placement;

    [Header("颜色")]
    public Color outerColor = new(0.35f, 0.88f, 1.00f, 0.28f);  // 外框线
    public Color innerColor = new(0.35f, 0.88f, 1.00f, 0.06f);  // 内部线
    public Color highlightColor = new(0.00f, 0.95f, 1.00f, 0.85f);

    [Header("虚线")]
    [Range(0.02f, 0.5f)] public float dashLen = 0.15f;
    [Range(0.02f, 0.5f)] public float gapLen  = 0.12f;

    [Header("显示")]
    public bool showXZ = true;   // 水平层格线（X方向 + Z方向）
    public bool showY  = true;   // 竖向格线
    public bool showCursor = true;
    public bool visible    = true;   // 整体显隐（按 toggleKey 切换）

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

    void OnRenderObject()
    {
        if (!visible || !_mat || grid == null) return;

        _mat.SetPass(0);
        GL.PushMatrix();
        GL.MultMatrix(Matrix4x4.identity);

        int   sx = grid.size.x, sy = grid.size.y, sz = grid.size.z;
        float cs = grid.cellSize;

        GL.Begin(GL.LINES);

        if (showXZ)
        {
            // X 方向线：每个 (yi, zi) 节点处画一条沿 X 的线
            for (int yi = 0; yi <= sy; yi++)
            for (int zi = 0; zi <= sz; zi++)
            {
                Color c = IsOnFaceXZ(yi, zi, sy, sz) ? outerColor : innerColor;
                Dash(new Vector3(0, yi*cs, zi*cs), new Vector3(sx*cs, yi*cs, zi*cs), c);
            }

            // Z 方向线：每个 (xi, yi) 节点处画一条沿 Z 的线
            for (int xi = 0; xi <= sx; xi++)
            for (int yi = 0; yi <= sy; yi++)
            {
                Color c = IsOnFaceXZ(xi, yi, sx, sy) ? outerColor : innerColor;
                Dash(new Vector3(xi*cs, yi*cs, 0), new Vector3(xi*cs, yi*cs, sz*cs), c);
            }
        }

        if (showY)
        {
            // Y 方向线：每个 (xi, zi) 节点处画一条竖线
            for (int xi = 0; xi <= sx; xi++)
            for (int zi = 0; zi <= sz; zi++)
            {
                Color c = IsOnFaceY(xi, zi, sx, sz) ? outerColor : innerColor;
                Dash(new Vector3(xi*cs, 0, zi*cs), new Vector3(xi*cs, sy*cs, zi*cs), c);
            }
        }

        GL.End();

        if (showCursor && placement != null)
            DrawCursorBox(placement.SnappedGridPos, cs);

        GL.PopMatrix();
    }

    // 判断是否在外层面上（两个坐标都在边界时）
    static bool IsOnFaceXZ(int a, int b, int maxA, int maxB)
        => a == 0 || a == maxA || b == 0 || b == maxB;

    static bool IsOnFaceY(int x, int z, int maxX, int maxZ)
        => x == 0 || x == maxX || z == 0 || z == maxZ;

    // ── 虚线（在 GL.Begin 内调用）────────────────────────────────────────────
    void Dash(Vector3 from, Vector3 to, Color col)
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
                GL.Color(col);
                GL.Vertex(from + dir * t);
                GL.Vertex(from + dir * t2);
            }
            t    = t2;
            draw = !draw;
        }
    }

    // ── 鼠标悬停格高亮 ────────────────────────────────────────────────────────
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
        // 底
        L(v[0],v[1]); L(v[1],v[2]); L(v[2],v[3]); L(v[3],v[0]);
        // 顶
        L(v[4],v[5]); L(v[5],v[6]); L(v[6],v[7]); L(v[7],v[4]);
        // 柱
        L(v[0],v[4]); L(v[1],v[5]); L(v[2],v[6]); L(v[3],v[7]);
        GL.End();
    }

    static void L(Vector3 a, Vector3 b) { GL.Vertex(a); GL.Vertex(b); }
}
