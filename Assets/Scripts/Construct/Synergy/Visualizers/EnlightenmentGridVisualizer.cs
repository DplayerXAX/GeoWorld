using UnityEngine;

// 启发 (Enlightenment) — thin sci-fi grid drawn on each panel:
//   ┌─┬─┬─┐
//   ├─┼─┼─┤    (gridDivisions=3 → 2 lines per axis = 9 cells)
//   ├─┼─┼─┤
//   └─┴─┴─┘
//
// With optional small "node" disks at the line intersections, reading as a
// digital display / scanner overlay.
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Visualizers/Enlightenment Grid",
                 fileName = "EnlightenmentGridVisualizer")]
public class EnlightenmentGridVisualizer : FaceMaterialVisualizerBase
{
    [Header("Grid lines")]
    [Tooltip("How many cells the face is split into per axis. 3 → 2 lines per axis (3 sub-cells).")]
    [Range(2, 6)] public int gridDivisions = 3;

    [Tooltip("Grid line length as a fraction of cellSize.")]
    [Range(0.4f, 1f)] public float lineLength = 0.86f;

    [Tooltip("Grid line thickness.")]
    [Min(0.005f)] public float lineWidth = 0.025f;

    [Tooltip("Grid line depth off the panel.")]
    [Min(0.001f)] public float lineDepth = 0.012f;

    [Header("Intersection nodes")]
    public bool  drawNodes      = true;
    [Tooltip("Node disk radius.")]
    [Min(0.005f)] public float nodeRadius = 0.025f;

    [Tooltip("Node disk depth off the panel.")]
    [Min(0.001f)] public float nodeDepth  = 0.018f;

    [Tooltip("Node brightness vs theme — 1=white (bright), 0=same as theme.")]
    [Range(0f, 1f)] public float nodeLighten = 0.55f;

    protected override void BuildPattern(Transform parent, Vector3 faceCenter,
                                          Vector3 normal, float cellSize, Color themeColor)
    {
        var u  = InPlaneU(normal);
        var v  = Vector3.Cross(normal, u).normalized;
        float L = cellSize * lineLength;

        // Lines per axis: gridDivisions-1 (e.g. 3 divisions = 2 interior lines).
        int interior = Mathf.Max(0, gridDivisions - 1);
        if (interior == 0) return;

        // Positions along ±L/2 evenly spaced for interior lines.
        // For 2 lines: positions are at -L/6 and +L/6 (third-points).
        // General: position[i] = (i+1)/(divisions) * L - L/2
        for (int i = 1; i < gridDivisions; i++)
        {
            float t      = (float)i / gridDivisions;
            float offset = (t - 0.5f) * L;

            // Horizontal line (along u) at vertical offset v*offset
            CreateBarOnFace(parent, faceCenter + v * offset, normal, u,
                            L, lineWidth, lineDepth, themeColor);
            // Vertical line (along v) at horizontal offset u*offset
            CreateBarOnFace(parent, faceCenter + u * offset, normal, v,
                            L, lineWidth, lineDepth, themeColor);
        }

        // Intersection nodes.
        if (drawNodes)
        {
            var nodeCol = Color.Lerp(themeColor, Color.white, nodeLighten);
            for (int i = 1; i < gridDivisions; i++)
            {
                float ti = (float)i / gridDivisions - 0.5f;
                for (int j = 1; j < gridDivisions; j++)
                {
                    float tj = (float)j / gridDivisions - 0.5f;
                    var pos = faceCenter + u * (ti * L) + v * (tj * L);
                    CreateDiskOnFace(parent, pos, normal, nodeRadius, nodeDepth, nodeCol);
                }
            }
        }
    }
}
