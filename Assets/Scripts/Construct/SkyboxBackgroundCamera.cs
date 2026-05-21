using UnityEngine;
using UnityEngine.Rendering.Universal;

// Attach to the main (orthographic) camera. Spawns a child camera that
// renders ONLY the skybox in perspective, so the sky keeps its rounded dome
// feel while the foreground stays orthographic. Child follows parent, so
// rotation/pan stay in sync automatically.
[RequireComponent(typeof(Camera))]
public class SkyboxBackgroundCamera : MonoBehaviour
{
    [Tooltip("FOV of the perspective skybox camera. Lower = more zoomed-in skybox.")]
    [Range(20f, 120f)] public float backgroundFOV = 60f;

    Camera _main;
    Camera _bg;

    void Awake()
    {
        _main = GetComponent<Camera>();

        // BG camera will be the Base of the stack (it owns the framebuffer).
        var bgGO = new GameObject("PerspectiveSkyboxCamera");
        bgGO.transform.SetParent(transform, worldPositionStays: false);

        _bg = bgGO.AddComponent<Camera>();
        _bg.orthographic        = false;
        _bg.fieldOfView         = backgroundFOV;
        _bg.cullingMask         = 0;            // skybox only, no geometry
        _bg.nearClipPlane       = 0.3f;
        _bg.farClipPlane        = 100f;
        _bg.useOcclusionCulling = false;
        _bg.clearFlags          = CameraClearFlags.Skybox;
        _bg.allowMSAA           = true;
        _bg.allowHDR            = _main.allowHDR;
        _bg.targetDisplay       = _main.targetDisplay;
        _bg.depth               = _main.depth;  // stack handles ordering now

        BuildCameraStack();
    }

    // Set up renderType + stack each enable. Unity sometimes resets these on
    // asset reimport / domain reload — re-asserting is cheap.
    void OnEnable() => BuildCameraStack();

    void BuildCameraStack()
    {
        if (_main == null) _main = GetComponent<Camera>();
        if (_main == null || _bg == null) return;

        var bgData   = _bg.GetUniversalAdditionalCameraData();
        var mainData = _main.GetUniversalAdditionalCameraData();
        if (bgData == null || mainData == null) return;

        // BG = Base (owns skybox + framebuffer + post-FX).
        bgData.renderType           = CameraRenderType.Base;
        bgData.renderPostProcessing = false;

        // Main = Overlay layered on top of BG. Shares BG's render target so
        // there's no "fresh framebuffer" between them — main draws its
        // geometry directly on top of the skybox BG painted.
        mainData.renderType         = CameraRenderType.Overlay;
        mainData.renderPostProcessing = true;
        _main.clearFlags            = CameraClearFlags.Depth;

        if (!bgData.cameraStack.Contains(_main))
            bgData.cameraStack.Add(_main);
    }

    void OnValidate()
    {
        if (_bg != null) _bg.fieldOfView = backgroundFOV;
    }
}
