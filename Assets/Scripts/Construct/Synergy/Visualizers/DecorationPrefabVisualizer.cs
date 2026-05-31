using UnityEngine;

// Spawns a prefab on each claimed piece — flowers, spikes, crowns, banners.
//
// Workflow:
//   1. Author the decoration as a prefab (e.g. a flower mesh with leaves).
//   2. Project → Create → GeoWorld → Synergy → Visualizers → Decoration Prefab.
//   3. Drag prefab in. Tune offset / scale / tint.
//   4. Drag this visualizer asset into a SynergyRule.visualizer slot.
//
// The prefab is parented to the piece's visualObject so it follows the
// block (and is auto-destroyed when the block is removed). On synergy
// revoke, the prefab is destroyed normally.
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Visualizers/Decoration Prefab",
                 fileName = "DecorationPrefabVisualizer")]
public class DecorationPrefabVisualizer : SynergyVisualizer
{
    [Tooltip("Prefab spawned on each claimed piece.")]
    public GameObject prefab;

    [Tooltip("Local offset from the piece's parent transform. Use +Y to place a flower on top.")]
    public Vector3 localOffset = Vector3.zero;

    [Tooltip("Local Euler rotation applied to the spawned decoration.")]
    public Vector3 localEuler = Vector3.zero;

    [Tooltip("Uniform scale multiplier for the spawned decoration.")]
    [Min(0.001f)] public float localScale = 1f;

    [Tooltip("If true, every Renderer in the spawned prefab is tinted to the synergy's theme color via MaterialPropertyBlock.")]
    public bool tintToThemeColor = true;

    [Tooltip("If true, the decoration is spawned per-cell of the piece (one flower per cell). If false, one decoration at the piece's centroid.")]
    public bool spawnPerCell = false;

    public override GameObject OnPieceClaimed(PlacedBlockInstance instance, ActiveSynergy active)
    {
        if (prefab == null || instance == null || instance.visualObject == null) return null;

        var parent = instance.visualObject.transform;

        var root = new GameObject("SynergyDeco");
        root.transform.SetParent(parent, false);

        var tint = BlockColorPalette.Get(active.rule.color);

        if (spawnPerCell && instance.occupiedCells != null)
        {
            // One decoration per cell, positioned at each cell's local offset.
            // (parent space — cells are world cells, so convert to local.)
            for (int i = 0; i < instance.occupiedCells.Count; i++)
            {
                var worldCell = GridSystem.instance.GridToWorld(instance.occupiedCells[i]);
                var localPos  = parent.InverseTransformPoint(worldCell) + localOffset;
                SpawnOne(root.transform, localPos, tint);
            }
        }
        else
        {
            SpawnOne(root.transform, localOffset, tint);
        }

        return root;
    }

    void SpawnOne(Transform parent, Vector3 localPos, Color tint)
    {
        var deco = Instantiate(prefab, parent);
        deco.transform.localPosition = localPos;
        deco.transform.localRotation = Quaternion.Euler(localEuler);
        deco.transform.localScale    = Vector3.one * localScale;

        if (tintToThemeColor)
        {
            var rends = deco.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < rends.Length; i++)
                MpbColor.Set(rends[i], tint);
        }
    }
}
