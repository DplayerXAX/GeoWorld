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
    }

    public List<RendererState> savedStates = new List<RendererState>();
}