using System.Collections.Generic;
using UnityEngine;

public class CellMaterialBackup : MonoBehaviour
{
    public struct RendererState
    {
        public Renderer renderer;
        public Material originalMaterial;
    }

    public List<RendererState> savedStates = new List<RendererState>();
}