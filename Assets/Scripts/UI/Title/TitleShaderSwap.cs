using UnityEngine;

// Swaps the cube material and the skybox material between two states:
//   TITLE   → white cube + flowing skybox  (the look on the landing screen)
//   SWAPPED → flowing cube + white skybox   (after you click into the menu)
// For a pure swap, just cross-assign: cubeTitleMat = white, skyboxTitleMat = flowing,
// cubeSwapMat = flowing, skyboxSwapMat = white. TitleFlow calls ApplyTitle/ApplySwapped.
public class TitleShaderSwap : MonoBehaviour
{
    public Renderer cube;

    [Header("TITLE state")]
    public Material cubeTitleMat;     // white cube  (left null → captured from the cube at Start)
    public Material skyboxTitleMat;   // flowing skybox (left null → captured from RenderSettings at Start)

    [Header("After click — MainMenu / Save (swapped)")]
    public Material cubeSwapMat;      // flowing, now on the cube
    public Material skyboxSwapMat;    // white, now on the skybox

    bool _swapped;
    public bool IsSwapped => _swapped;

    void Start()
    {
        if (cube != null && cubeTitleMat == null) cubeTitleMat = cube.sharedMaterial;
        if (skyboxTitleMat == null)               skyboxTitleMat = RenderSettings.skybox;
        ApplyTitle();
    }

    public void ApplyTitle()
    {
        if (cube != null && cubeTitleMat != null) cube.sharedMaterial = cubeTitleMat;
        if (skyboxTitleMat != null)               RenderSettings.skybox = skyboxTitleMat;
        DynamicGI.UpdateEnvironment();   // refresh ambient lighting from the new skybox
        _swapped = false;
    }

    public void ApplySwapped()
    {
        if (cube != null && cubeSwapMat != null) cube.sharedMaterial = cubeSwapMat;
        if (skyboxSwapMat != null)               RenderSettings.skybox = skyboxSwapMat;
        DynamicGI.UpdateEnvironment();
        _swapped = true;
    }

    public void Toggle() { if (_swapped) ApplyTitle(); else ApplySwapped(); }
}
