using UnityEngine;

// 和谐 (Harmony) — totem-mask face on each external panel:
//   ▔▔▔▔▔     ← brow bar
//      ●       ← center eye (disk)
//   ▔▔▔▔▔     ← mouth bar
//
// Reads as a tribal carving without going too literal. Bars + eye in theme
// color on the panel background.
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Visualizers/Harmony Totem Face",
                 fileName = "HarmonyTotemFaceVisualizer")]
public class HarmonyTotemFaceVisualizer : FaceMaterialVisualizerBase
{
    [Header("Brow / mouth bars")]
    [Tooltip("Bar length as a fraction of cellSize.")]
    [Range(0.2f, 0.95f)] public float barLength = 0.62f;

    [Tooltip("Bar thickness.")]
    [Min(0.005f)] public float barWidth = 0.08f;

    [Tooltip("Vertical offset of the brow bar from face center, fraction of cellSize. Mouth bar mirrors.")]
    [Range(0.1f, 0.45f)] public float barOffset = 0.28f;

    [Tooltip("Bar depth off the panel.")]
    [Min(0.001f)] public float barDepth = 0.014f;

    [Header("Center eye (disk)")]
    public bool  drawEye        = true;
    [Tooltip("Eye radius as a fraction of cellSize.")]
    [Range(0.02f, 0.3f)] public float eyeRadius = 0.10f;

    [Tooltip("Eye thickness off the panel.")]
    [Min(0.001f)] public float eyeDepth = 0.018f;

    [Tooltip("Eye color brightness vs theme: 1=white, 0=same as theme.")]
    [Range(0f, 1f)] public float eyeLighten = 0.55f;

    protected override void BuildPattern(Transform parent, Vector3 faceCenter,
                                          Vector3 normal, float cellSize, Color themeColor)
    {
        var u    = InPlaneU(normal);                       // "horizontal" in face
        var v    = Vector3.Cross(normal, u).normalized;     // "vertical" in face
        float bl = cellSize * barLength;
        float bo = cellSize * barOffset;

        // Brow (above center) and mouth (below center), both along u.
        CreateBarOnFace(parent, faceCenter + v * bo, normal, u, bl, barWidth, barDepth, themeColor);
        CreateBarOnFace(parent, faceCenter - v * bo, normal, u, bl, barWidth, barDepth, themeColor);

        // Center eye.
        if (drawEye)
        {
            var eyeCol = Color.Lerp(themeColor, Color.white, eyeLighten);
            CreateDiskOnFace(parent, faceCenter, normal, cellSize * eyeRadius, eyeDepth, eyeCol);
        }
    }
}
