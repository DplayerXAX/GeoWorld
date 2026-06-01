using UnityEngine;

// Pure textured face — covers each external face with a material (typically
// holding a Texture2D + chosen shader). No primitive overlay on top.
//
// Use for Harmony (wood-grain swirls), Enlightenment (flowing wave field),
// or any theme that's best expressed as imagery rather than geometric bars.
//
// Workflow:
//   1. Import the texture (.png / .jpg) into Assets.
//   2. Create a Material with the texture assigned (recommended shader:
//      Unlit/Texture for flat painterly look, or URP Lit for normal-mapped).
//   3. Project → Create → GeoWorld → Synergy → Visualizers → Face Texture.
//   4. Drag the Material into `Panel Material`.
//   5. Drag this asset into SynergyRule.visualizer.
//
// The panel inherits all the iteration / face-filter / interior-cull logic
// from FaceMaterialVisualizerBase — you still get bottom-face toggle,
// per-face skip when neighbours hide them, etc.
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Visualizers/Face Texture",
                 fileName = "FaceTextureVisualizer")]
public class FaceTextureVisualizer : FaceMaterialVisualizerBase
{
    // No additional fields — texture comes from base's `panelMaterial`.
    // BuildPattern is intentionally empty: this visualizer is panel-only.

    protected override void BuildPattern(Transform parent, Vector3 faceCenter,
                                          Vector3 normal, float cellSize, Color themeColor)
    {
        // No overlay primitives — the panel material IS the visual.
    }
}
