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

    [Tooltip("The cube carries an inverse-hull cartoon outline in material slot 1. " +
             "Against a TRANSPARENT replacement (e.g. the raymarched synergy volumes) " +
             "it has no opaque face to clip it, so it fills a solid dark cube that " +
             "bleeds through the volume. When ON, the outline slot is stripped while " +
             "the synergy is active (restored on release) ONLY if the replacement " +
             "material is transparent — opaque replacements keep their rim untouched. " +
             "Turn OFF to always keep the hard cartoon rim.")]
    public bool stripBlockOutlineWhileActive = true;

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

        // Only strip the inverse-hull outline (slot 1) for TRANSPARENT replacements
        // (renderQueue in the transparent range), where it would otherwise fill dark
        // behind the volume. Opaque replacements clip the hull to a proper rim on
        // their own, so leave their material array untouched.
        bool stripOutline = stripBlockOutlineWhileActive
                            && replacementMaterial != null
                            && replacementMaterial.renderQueue > 2500;

        // World center of the whole synergy's highlighted cells. Each swapped
        // cube offsets its object-space raymarch by its position relative to
        // this, so the group renders as ONE continuous volume instead of a
        // self-centered copy per cube.
        Vector3 groupCenterWorld = ComputeGroupCenter(active, cellFilter, grid, parent.position);

        GameObject stateTracker = new GameObject($"SynergyMaterialBackup_{active.rule.name}");
        stateTracker.transform.SetParent(parent, false);
        var backupComp = stateTracker.AddComponent<CellMaterialBackup>();

        foreach (var worldCell in instance.occupiedCells)
        {
            Vector3 targetWorldPos = grid.GridToWorld(worldCell);
            Renderer targetRenderer = FindRendererAtWorldPos(allRenderers, targetWorldPos, grid.cellSize);

            if (targetRenderer != null)
            {
                // Back up the FULL material array (surface + inverse-hull outline
                // slot) AND the renderer's current MaterialPropertyBlock — the
                // latter carries the block's own _BaseColor. Capturing it BEFORE
                // we overwrite the colour below is what lets the release path put
                // the real colour back instead of dropping to a grey default.
                var originalBlock = new MaterialPropertyBlock();
                targetRenderer.GetPropertyBlock(originalBlock);
                backupComp.savedStates.Add(new CellMaterialBackup.RendererState
                {
                    renderer = targetRenderer,
                    originalMaterials = targetRenderer.sharedMaterials,
                    originalBlock = originalBlock
                });

                // Core highlight logic.
                if (cellFilter == null || cellFilter.ShouldHighlight(worldCell))
                {
                    if (replacementMaterial != null)
                    {
                        // The cube's slot 1 holds an inverse-hull cartoon outline.
                        // With a transparent synergy surface there is no opaque
                        // face to clip it, so it fills a solid dark cube that
                        // shows through the volume. Strip to a single slot to
                        // kill that bleed (restored on release); otherwise just
                        // swap slot 0 and leave the outline in place.
                        if (stripOutline)
                            targetRenderer.sharedMaterials = new[] { replacementMaterial };
                        else
                            targetRenderer.sharedMaterial = replacementMaterial;
                    }
                    // Group-shared raymarch space: offset = this cube's center
                    // relative to the group center, in the cube's object space.
                    Vector3 cubeCenterWorld = targetRenderer.transform.position;
                    Vector3 groupOffsetOS = targetRenderer.transform.InverseTransformVector(cubeCenterWorld - groupCenterWorld);
                    ApplyCellMpb(targetRenderer, targetColor, groupOffsetOS);
                }
                else
                {
                    // Filtered-out (overhang) cell: restore its ORIGINAL block
                    // rather than nulling it — SetPropertyBlock(null) would wipe
                    // the block's _BaseColor and leave a grey cube.
                    targetRenderer.SetPropertyBlock(originalBlock);
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
                        if (state.originalMaterials != null)
                            state.renderer.sharedMaterials = state.originalMaterials;
                        // Restore the ORIGINAL property block (the block's own
                        // _BaseColor) instead of clearing to null, which would
                        // drop the cube to its grey material default.
                        if (state.originalBlock != null)
                            state.renderer.SetPropertyBlock(state.originalBlock);
                        else
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

    // ── Group-shared raymarch helpers ──────────────────────────────────────

    static readonly int _BaseColorID   = Shader.PropertyToID("_BaseColor");
    static readonly int _GroupOffsetID = Shader.PropertyToID("_GroupOffset");
    static MaterialPropertyBlock _mpb;

    // World-space center of every HIGHLIGHTED cell across the WHOLE synergy
    // claim (all pieces). Filtered cells (ICellHighlightFilter) are excluded so
    // the center tracks the lit region (e.g. Enlightenment's cube), not the
    // claimed overhang. Falls back to the piece root when nothing highlights.
    static Vector3 ComputeGroupCenter(ActiveSynergy active, ICellHighlightFilter filter,
                                      GridSystem grid, Vector3 fallback)
    {
        if (active?.claimedPieces == null) return fallback;
        Vector3 sum = Vector3.zero;
        int n = 0;
        foreach (var piece in active.claimedPieces)
        {
            if (piece?.cells == null) continue;
            for (int i = 0; i < piece.cells.Length; i++)
            {
                var c = piece.cells[i];
                if (filter == null || filter.ShouldHighlight(c))
                {
                    sum += grid.GridToWorld(c);
                    n++;
                }
            }
        }
        return n > 0 ? sum / n : fallback;
    }

    // Writes _BaseColor + _GroupOffset in one MPB pass (keeps GPU instancing
    // friendly; never instantiates a per-renderer material).
    static void ApplyCellMpb(Renderer r, Color color, Vector3 groupOffsetOS)
    {
        if (r == null) return;
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        r.GetPropertyBlock(_mpb);
        _mpb.SetColor(_BaseColorID, color);
        _mpb.SetVector(_GroupOffsetID, new Vector4(groupOffsetOS.x, groupOffsetOS.y, groupOffsetOS.z, 0f));
        r.SetPropertyBlock(_mpb);
    }
}