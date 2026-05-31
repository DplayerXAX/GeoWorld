using UnityEngine;

// Creates a slightly-inflated "shell" of every claimed piece's mesh and
// applies a custom overlay material. Use for flowing textures, animated
// patterns, energy auras — anything that should LOOK like it's painted on
// the block's surface without modifying the block's actual material.
//
// Workflow:
//   1. Author an overlay material (e.g. a Shader Graph with UV scroll for
//      "flowing", or a stencil pattern for "engraved").
//   2. Project → Create → GeoWorld → Synergy → Visualizers → Material Overlay.
//   3. Drop the material in. Tune inflate amount to avoid z-fighting.
//   4. Drag this visualizer asset into a SynergyRule.visualizer slot.
//
// The shell sits at the piece's renderer transforms, slightly larger so
// the overlay reads as a coating. Each MeshFilter on the original is
// mirrored; SkinnedMeshRenderer is not supported (use prefab decoration
// for those).
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Visualizers/Material Overlay",
                 fileName = "MaterialOverlayVisualizer")]
public class MaterialOverlayVisualizer : SynergyVisualizer
{
    [Tooltip("Material applied to the overlay shell. Typically a transparent / additive shader so the underlying block still reads through.")]
    public Material overlayMaterial;

    [Tooltip("Uniform scale multiplier on the shell relative to the original mesh. Slightly > 1 avoids z-fighting; slightly < 1 inscribes the overlay inside.")]
    [Range(0.9f, 1.1f)] public float shellScale = 1.005f;

    [Tooltip("If true, the overlay material's _Color (and MPB color) gets tinted to the synergy's theme color.")]
    public bool tintToThemeColor = true;

    public override GameObject OnPieceClaimed(PlacedBlockInstance instance, ActiveSynergy active)
    {
        if (overlayMaterial == null || instance == null || instance.visualObject == null) return null;

        var root = new GameObject("SynergyOverlay");
        root.transform.SetParent(instance.visualObject.transform, false);

        var tint = BlockColorPalette.Get(active.rule.color);
        var sourceRends = instance.visualObject.GetComponentsInChildren<MeshRenderer>();

        for (int i = 0; i < sourceRends.Length; i++)
        {
            var srcR  = sourceRends[i];
            var srcMf = srcR.GetComponent<MeshFilter>();
            if (srcMf == null || srcMf.sharedMesh == null) continue;

            var shell = new GameObject("OverlayShell");
            // Parent under the source renderer so it inherits position / rotation.
            shell.transform.SetParent(srcR.transform, false);
            shell.transform.localPosition = Vector3.zero;
            shell.transform.localRotation = Quaternion.identity;
            shell.transform.localScale    = Vector3.one * shellScale;

            var mf = shell.AddComponent<MeshFilter>();
            mf.sharedMesh = srcMf.sharedMesh;

            var mr = shell.AddComponent<MeshRenderer>();
            // sharedMaterial keeps batching; per-instance tint via MPB.
            mr.sharedMaterial = overlayMaterial;
            if (tintToThemeColor) MpbColor.Set(mr, tint);
        }

        return root;
    }
}
