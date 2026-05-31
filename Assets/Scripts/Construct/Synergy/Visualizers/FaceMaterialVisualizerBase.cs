using System.Collections.Generic;
using UnityEngine;

// Shared base for visualizers that modify the FACES of a claimed piece
// (rather than spawn 3-D decorations like flowers or monuments).
//
// Common behavior:
//   • Iterate every cell of the piece, every external face
//   • Apply a flat-colored "panel" covering the face (optional)
//   • Hand off to BuildPattern() for subclass-specific decals on top of the panel
//   • Skip interior faces (shared with same-piece neighbors)
//   • Per-face-tier toggles (top / side / bottom)
//
// Subclass provides the pattern-drawing code. Use the supplied helpers
// (CreateBarOnFace, CreateDiskOnFace, CreateSquareFrameOnFace) to build
// pieces that sit flush on the panel with correct z-bias.
public abstract class FaceMaterialVisualizerBase : SynergyVisualizer
{
    [Header("Face panel (background)")]
    [Tooltip("If true, a flat-colored panel covers each face before the pattern is drawn on top.")]
    public bool  panelEnabled = true;

    [Tooltip("Panel color.")]
    public Color panelColor   = Color.black;

    [Tooltip("Panel size as a fraction of cellSize. <1.0 leaves a frame of the original face visible.")]
    [Range(0.5f, 1.0f)] public float panelSizeFraction = 1f;

    [Tooltip("Panel thickness off the face.")]
    [Min(0.001f)] public float panelDepth = 0.012f;

    [Tooltip("Push the whole decoration outward from the face by this much (extra z-bias).")]
    [Min(0f)] public float faceBias = 0.005f;

    [Header("Which faces")]
    public bool topFace    = true;
    public bool sideFaces  = true;
    public bool bottomFace = false;

    static readonly (Vector3 normal, Vector3Int offset, string tier)[] _faces =
    {
        (Vector3.up,      Vector3Int.up,      "top"),
        (Vector3.down,    Vector3Int.down,    "bottom"),
        (Vector3.forward, Vector3Int.forward, "side"),
        (Vector3.back,    Vector3Int.back,    "side"),
        (Vector3.right,   Vector3Int.right,   "side"),
        (Vector3.left,    Vector3Int.left,    "side"),
    };

    public override GameObject OnPieceClaimed(PlacedBlockInstance instance, ActiveSynergy active)
    {
        if (instance?.visualObject == null) return null;
        var grid = GridSystem.instance;
        if (grid == null) return null;

        var parent = instance.visualObject.transform;
        var root   = new GameObject($"Synergy_{active.rule.name}_Faces");
        root.transform.SetParent(parent, false);

        var sameCells = new HashSet<Vector3Int>(instance.occupiedCells);
        var themeCol  = BlockColorPalette.Get(active.rule.color);
        float cellSize = grid.cellSize;
        float half     = cellSize * 0.5f;

        foreach (var worldCell in instance.occupiedCells)
        {
            var cellLocal = parent.InverseTransformPoint(grid.GridToWorld(worldCell));
            for (int i = 0; i < _faces.Length; i++)
            {
                var (normal, offset, tier) = _faces[i];

                if (tier == "top"    && !topFace)    continue;
                if (tier == "bottom" && !bottomFace) continue;
                if (tier == "side"   && !sideFaces)  continue;
                if (sameCells.Contains(worldCell + offset)) continue;

                var faceCenter = cellLocal + normal * half;

                if (panelEnabled)
                    CreatePanel(root.transform, faceCenter, normal, cellSize, panelColor);

                BuildPattern(root.transform, faceCenter, normal, cellSize, themeCol);
            }
        }

        return root;
    }

    // ── Subclass override point ────────────────────────────────────────

    protected abstract void BuildPattern(Transform parent, Vector3 faceCenter,
                                          Vector3 normal, float cellSize, Color themeColor);

    // ── Helpers — placement math handles panel z-bias for you ─────────

    // Computes the outward offset that lifts an `itemDepth`-thick decoration
    // so it sits on top of the panel (or on the bare face if panel is off).
    protected float OutwardOffsetFor(float itemDepth)
        => faceBias + (panelEnabled ? panelDepth : 0f) + itemDepth * 0.5f;

    // Pick any direction tangent to the face, normalised.
    protected static Vector3 InPlaneU(Vector3 normal)
    {
        var u = (Mathf.Abs(normal.y) < 0.9f) ? Vector3.Cross(normal, Vector3.up)
                                              : Vector3.Cross(normal, Vector3.right);
        return u.normalized;
    }

    // Thin bar lying flat on the face, oriented along `dirInPlane`.
    // length = along dirInPlane, width = perpendicular in-face, depth = off-face.
    protected GameObject CreateBarOnFace(Transform parent, Vector3 faceCenter, Vector3 normal,
                                         Vector3 dirInPlane, float length, float width, float depth, Color color)
    {
        var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = "Bar";
        var c = bar.GetComponent<Collider>();
        if (c != null) Object.Destroy(c);
        bar.transform.SetParent(parent, false);

        var forward = Vector3.Cross(normal, dirInPlane);
        bar.transform.localRotation = Quaternion.LookRotation(forward, normal);
        bar.transform.localPosition = faceCenter + normal * OutwardOffsetFor(depth);
        bar.transform.localScale    = new Vector3(length, depth, width);

        var r = bar.GetComponent<Renderer>();
        if (r != null) MpbColor.Set(r, color);
        return bar;
    }

    // Centered disk on the face (flattened cylinder).
    protected GameObject CreateDiskOnFace(Transform parent, Vector3 faceCenter, Vector3 normal,
                                          float radius, float depth, Color color)
    {
        var disk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disk.name = "Disk";
        var c = disk.GetComponent<Collider>();
        if (c != null) Object.Destroy(c);
        disk.transform.SetParent(parent, false);

        disk.transform.localRotation = Quaternion.FromToRotation(Vector3.up, normal);
        disk.transform.localPosition = faceCenter + normal * OutwardOffsetFor(depth);
        // Cylinder primitive is 2 units tall along Y. Scale Y by depth/2 to get `depth` thickness.
        disk.transform.localScale    = new Vector3(radius * 2f, depth * 0.5f, radius * 2f);

        var r = disk.GetComponent<Renderer>();
        if (r != null) MpbColor.Set(r, color);
        return disk;
    }

    // 4 bars forming a square outline centered on the face.
    protected void CreateSquareFrameOnFace(Transform parent, Vector3 faceCenter, Vector3 normal,
                                           float sideSize, float barWidth, float depth, Color color)
    {
        var u    = InPlaneU(normal);
        var v    = Vector3.Cross(normal, u).normalized;
        float h  = sideSize * 0.5f;

        // Top & bottom bars (run along u, offset by ±v)
        CreateBarOnFace(parent, faceCenter + v * h, normal, u, sideSize, barWidth, depth, color);
        CreateBarOnFace(parent, faceCenter - v * h, normal, u, sideSize, barWidth, depth, color);
        // Left & right bars (run along v, offset by ±u). Slightly shorter so
        // they don't clip into the top/bottom bars' ends.
        float verticalLen = sideSize - barWidth * 2f;
        CreateBarOnFace(parent, faceCenter + u * h, normal, v, verticalLen, barWidth, depth, color);
        CreateBarOnFace(parent, faceCenter - u * h, normal, v, verticalLen, barWidth, depth, color);
    }

    void CreatePanel(Transform parent, Vector3 faceCenter, Vector3 normal, float cellSize, Color col)
    {
        var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = "Panel";
        var c = panel.GetComponent<Collider>();
        if (c != null) Object.Destroy(c);
        panel.transform.SetParent(parent, false);

        var u = InPlaneU(normal);
        var forward = Vector3.Cross(normal, u);
        panel.transform.localRotation = Quaternion.LookRotation(forward, normal);
        panel.transform.localPosition = faceCenter + normal * (panelDepth * 0.5f + faceBias);

        float side = cellSize * panelSizeFraction;
        panel.transform.localScale = new Vector3(side, panelDepth, side);

        var r = panel.GetComponent<Renderer>();
        if (r != null) MpbColor.Set(r, col);
    }
}
