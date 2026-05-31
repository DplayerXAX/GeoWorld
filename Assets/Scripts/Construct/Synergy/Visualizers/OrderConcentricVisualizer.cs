using UnityEngine;

// 秩序 (Order) — concentric square frames on each face. Rigid, symmetric,
// repeating — the Constructivism "discipline" pattern.
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Visualizers/Order Concentric",
                 fileName = "OrderConcentricVisualizer")]
public class OrderConcentricVisualizer : FaceMaterialVisualizerBase
{
    [Header("Concentric frames")]
    [Tooltip("Number of nested square frames drawn on each face.")]
    [Range(1, 5)] public int frameCount = 3;

    [Tooltip("Side size of the outermost frame, as a fraction of cellSize.")]
    [Range(0.3f, 0.95f)] public float outerSize = 0.78f;

    [Tooltip("Shrink ratio between successive frames (inner = outer × this).")]
    [Range(0.3f, 0.95f)] public float shrinkRatio = 0.62f;

    [Tooltip("Width of each frame's bar.")]
    [Min(0.005f)] public float barWidth = 0.06f;

    [Tooltip("Depth of each frame's bar off the panel.")]
    [Min(0.001f)] public float barDepth = 0.012f;

    [Tooltip("Use the synergy theme color for frames. If false, uses the override.")]
    public bool  useThemeColor = true;
    public Color overrideColor = Color.white;

    protected override void BuildPattern(Transform parent, Vector3 faceCenter,
                                          Vector3 normal, float cellSize, Color themeColor)
    {
        var col  = useThemeColor ? themeColor : overrideColor;
        float side = cellSize * outerSize;

        for (int i = 0; i < frameCount; i++)
        {
            if (side < barWidth * 2f) break;   // frame collapsed to a square — stop
            CreateSquareFrameOnFace(parent, faceCenter, normal, side, barWidth, barDepth, col);
            side *= shrinkRatio;
        }
    }
}
