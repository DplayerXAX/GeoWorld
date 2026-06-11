using System.Collections.Generic;
using UnityEngine;

public class CellMaterialBackup : MonoBehaviour
{
    public struct RendererState
    {
        public Renderer renderer;
        // Full material array (every slot), so the inverse-hull block outline
        // in slot 1 survives a round-trip even when the synergy strips it.
        public Material[] originalMaterials;
        // The renderer's MaterialPropertyBlock as it was BEFORE the synergy
        // recoloured it — carries the block's own _BaseColor. Restored on
        // release so the cube returns to its real colour instead of the
        // material's grey default (which SetPropertyBlock(null) would expose).
        public MaterialPropertyBlock originalBlock;
    }

    public List<RendererState> savedStates = new List<RendererState>();
}