using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "GeoWorld/Synergy/Visualizers/CellMaterialVisualizer",
                 fileName = "CellMaterialVisualizer")]
public class CellMaterialVisualizer : SynergyVisualizer
{
    [Header("Material Settings")]
    [Tooltip("The material to apply to the target cells. If null, it will just change the color via MPB.")]
    public Material replacementMaterial;

    [Tooltip("If you are just changing color without replacing the material, set it here.")]
    public Color overrideColor = Color.white;

    [Tooltip("Use the Synergy Rule's theme color instead of the overrideColor?")]
    public bool useThemeColor = true;

    public override GameObject OnPieceClaimed(PlacedBlockInstance instance, ActiveSynergy active)
    {
        if (instance?.visualObject == null) return null;
        var grid = GridSystem.instance;
        if (grid == null) return null;

        var parent = instance.visualObject.transform;

        var allRenderers = parent.GetComponentsInChildren<Renderer>();
        if (allRenderers.Length == 0) return null;

        var themeCol = BlockColorPalette.Get(active.rule.color);
        Color targetColor = useThemeColor ? themeCol : overrideColor;

        var cellFilter = active.rule as ICellHighlightFilter;

        GameObject stateTracker = new GameObject($"SynergyMaterialBackup_{active.rule.name}");
        stateTracker.transform.SetParent(parent, false);
        var backupComp = stateTracker.AddComponent<CellMaterialBackup>();

        foreach (var worldCell in instance.occupiedCells)
        {
            Vector3 targetWorldPos = grid.GridToWorld(worldCell);
            Renderer targetRenderer = FindRendererAtWorldPos(allRenderers, targetWorldPos, grid.cellSize);

            if (targetRenderer != null)
            {
                // 无条件备份原始材质
                backupComp.savedStates.Add(new CellMaterialBackup.RendererState
                {
                    renderer = targetRenderer,
                    originalMaterial = targetRenderer.sharedMaterial
                });

                // 核心过滤逻辑
                if (cellFilter == null || cellFilter.ShouldHighlight(worldCell))
                {
                    if (replacementMaterial != null)
                    {
                        targetRenderer.sharedMaterial = replacementMaterial;
                    }
                    MpbColor.Set(targetRenderer, targetColor);
                }
                else
                {
                    // 对超出的格子进行剔除，强制还原
                    targetRenderer.sharedMaterial = targetRenderer.sharedMaterial;
                    targetRenderer.SetPropertyBlock(null);
                }
            }
        }

        return stateTracker;
    }

    public override void OnPieceReleased(PlacedBlockInstance instance, ActiveSynergy active, GameObject spawned)
    {
        if (spawned != null)
        {
            var backupComp = spawned.GetComponent<CellMaterialBackup>();
            if (backupComp != null)
            {
                foreach (var state in backupComp.savedStates)
                {
                    if (state.renderer != null)
                    {
                        state.renderer.sharedMaterial = state.originalMaterial;
                        state.renderer.SetPropertyBlock(null);
                    }
                }
            }
            base.OnPieceReleased(instance, active, spawned);
        }
    }

    /// <summary>
    /// Find the renderer whose bounds center is closest to the target world position.
    /// </summary>
    private Renderer FindRendererAtWorldPos(Renderer[] renderers, Vector3 worldPos, float cellSize)
    {
        float threshold = cellSize * 0.45f;
        Renderer closest = null;
        float minDst = float.MaxValue;

        foreach (var r in renderers)
        {
            float dst = Vector3.Distance(r.bounds.center, worldPos);
            if (dst < minDst)
            {
                minDst = dst;
                closest = r;
            }
        }

        if (minDst <= threshold)
        {
            return closest;
        }

        return null;
    }
}