using UnityEngine;
using UnityEngine.Rendering;

public class GridOverlay : MonoBehaviour
{
    [Header("References")]
    public GridSystem grid;
    public PlacementController placement;

    [Header("Appearance")]
    public Color lineColor = new(1f, 1f, 1f, 0.08f);
    public Color highlightColor = new(0f, 0.9f, 1f, 0.80f);

    [Header("Range")]
    [Range(2, 12)] public int extentXZ = 6;
    [Range(1, 8)] public int extentY = 3;

    private Material _mat;

    void Awake()
    {
        _mat = new Material(Shader.Find("Hidden/Internal-Colored"))
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        _mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        _mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        _mat.SetInt("_Cull", (int)CullMode.Off);
        _mat.SetInt("_ZWrite", 0);
        _mat.SetInt("_ZTest", (int)CompareFunction.LessEqual);
    }

    void OnDestroy()
    {
        if (_mat) DestroyImmediate(_mat);
    }

    void OnRenderObject() => Draw();

    void Draw()
    {
        if (!_mat || !grid || !placement) return;

        _mat.SetPass(0);
        GL.PushMatrix();
        GL.MultMatrix(Matrix4x4.identity);

        Vector3Int cursor = placement.SnappedGridPos;
        float cs = grid.cellSize;

        int x0 = -extentXZ, x1 = extentXZ;
        int y0 = 0, y1 = extentY;
        int z0 = -extentXZ, z1 = extentXZ;

        GL.Begin(GL.LINES);

        for (int yi = y0; yi <= y1; yi++)
        {
            for (int zi = z0; zi <= z1; zi++)
            {
                GL.Color(FadeColor(lineColor, x0, yi, zi));
                GL.Vertex3(x0 * cs, yi * cs, zi * cs);
                GL.Color(FadeColor(lineColor, x1, yi, zi));
                GL.Vertex3(x1 * cs, yi * cs, zi * cs);
            }

            for (int xi = x0; xi <= x1; xi++)
            {
                GL.Color(FadeColor(lineColor, xi, yi, z0));
                GL.Vertex3(xi * cs, yi * cs, z0 * cs);
                GL.Color(FadeColor(lineColor, xi, yi, z1));
                GL.Vertex3(xi * cs, yi * cs, z1 * cs);
            }
        }

        for (int xi = x0; xi <= x1; xi++)
        {
            for (int zi = z0; zi <= z1; zi++)
            {
                GL.Color(FadeColor(lineColor, xi, y0, zi));
                GL.Vertex3(xi * cs, y0 * cs, zi * cs);
                GL.Color(FadeColor(lineColor, xi, y1, zi));
                GL.Vertex3(xi * cs, y1 * cs, zi * cs);
            }
        }

        GL.End();

        DrawCellWireframe(cursor, cs);

        GL.PopMatrix();
    }

    Color FadeColor(Color baseColor, int gx, int gy, int gz)
    {
        float dx = (float)Mathf.Abs(gx) / extentXZ;
        float dy = (float)gy / extentY;
        float dz = (float)Mathf.Abs(gz) / extentXZ;

        float t = Mathf.Max(dx, dy, dz);
        float fade = 1f - Mathf.SmoothStep(0f, 1f, t);

        return new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * fade);
    }

    void DrawCellWireframe(Vector3Int cell, float cs)
    {
        Vector3 p000 = new(cell.x * cs, cell.y * cs, cell.z * cs);
        Vector3 p100 = new((cell.x + 1) * cs, cell.y * cs, cell.z * cs);
        Vector3 p010 = new(cell.x * cs, (cell.y + 1) * cs, cell.z * cs);
        Vector3 p110 = new((cell.x + 1) * cs, (cell.y + 1) * cs, cell.z * cs);
        Vector3 p001 = new(cell.x * cs, cell.y * cs, (cell.z + 1) * cs);
        Vector3 p101 = new((cell.x + 1) * cs, cell.y * cs, (cell.z + 1) * cs);
        Vector3 p011 = new(cell.x * cs, (cell.y + 1) * cs, (cell.z + 1) * cs);
        Vector3 p111 = new((cell.x + 1) * cs, (cell.y + 1) * cs, (cell.z + 1) * cs);

        GL.Begin(GL.LINES);
        GL.Color(highlightColor);

        Line(p000, p100); Line(p100, p101);
        Line(p101, p001); Line(p001, p000);
        Line(p010, p110); Line(p110, p111);
        Line(p111, p011); Line(p011, p010);
        Line(p000, p010); Line(p100, p110);
        Line(p101, p111); Line(p001, p011);

        GL.End();
    }

    static void Line(Vector3 a, Vector3 b)
    {
        GL.Vertex(a);
        GL.Vertex(b);
    }
}