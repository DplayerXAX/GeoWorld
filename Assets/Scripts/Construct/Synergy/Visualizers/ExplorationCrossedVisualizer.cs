using UnityEngine;

// 探寻 (Exploration) — black panel + custom-colored X on each external face.
// Reads as "marked / surveyed". All face iteration / panel logic lives in
// FaceMaterialVisualizerBase — this subclass only draws the X strokes.
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Visualizers/Exploration Crossed",
                 fileName = "ExplorationCrossedVisualizer")]
public class ExplorationCrossedVisualizer : FaceMaterialVisualizerBase
{
    [Header("X strokes")]
    [Tooltip("Length of each stroke, in cell-units. 0.78 ≈ leaves a small margin.")]
    [Min(0.01f)] public float strokeLength = 0.78f;

    [Tooltip("Width (thickness across) of each stroke.")]
    [Min(0.005f)] public float strokeWidth = 0.10f;

    [Tooltip("Depth of each stroke off the panel.")]
    [Min(0.001f)] public float strokeDepth = 0.015f;

    [Tooltip("Color of the X strokes.")]
    public Color crossColor = new(0.98f, 0.85f, 0.20f);

    protected override void BuildPattern(Transform parent, Vector3 faceCenter,
                                          Vector3 normal, float cellSize, Color themeColor)
    {
        // Two strokes at +45° and -45° from in-face reference direction.
        var u = InPlaneU(normal);
        for (int i = 0; i < 2; i++)
        {
            float angle = (i == 0) ? 45f : -45f;
            var dir = Quaternion.AngleAxis(angle, normal) * u;
            CreateBarOnFace(parent, faceCenter, normal, dir, strokeLength, strokeWidth, strokeDepth, crossColor);
        }
    }
}
