using UnityEngine;

// Procedural visual for start/end endpoints. Two layers:
//
//   1. CUBE FRAME — 12 thin edges marking the cell's grid position.
//      Color matches the portal theme.
//   2. PORTAL DISC — a single Quad rotated face-up, rendered with the
//      assigned shader material (EndpointChaosPortal or EndpointHaloPortal).
//      All the animation lives in the shader, not in C#.
//
// Author one prefab per mode:
//   • Portal_Start: mode = Start, portalMaterial → Mat_ChaosPortal
//   • Portal_End:   mode = End,   portalMaterial → Mat_HaloPortal
// Drag prefabs into LevelEndpointGenerator.startPrefab / endPrefab.
public class EndpointPortalVisual : MonoBehaviour
{
    public enum Mode { Start, End }

    [Header("Theme")]
    public Mode mode = Mode.Start;

    [Header("Portal core (3D)")]
    [Tooltip("Material using GeoWorld/ChaosCore (start) or GeoWorld/HaloCore (end) shader. Renders on a sphere mesh inside the cube frame.")]
    public Material portalMaterial;

    [Tooltip("Core sphere diameter as a fraction of cellSize. 0.7 = roughly fills the cube frame inner volume.")]
    [Range(0.2f, 1.2f)] public float coreScale = 0.70f;

    [Header("Cube frame (grid position indicator)")]
    [Range(0.5f, 1.1f)] public float frameScale     = 0.98f;
    [Min(0.005f)] public float       frameThickness = 0.04f;

    [Header("Start frame color")]
    public Color startFrameColor = new(0.45f, 0.15f, 0.55f, 1f);

    [Header("End frame color")]
    public Color endFrameColor = new(1.00f, 0.78f, 0.30f, 1f);

    [Header("Optional outline")]
    [Tooltip("If set, applies the cartoon outline material (same as blocks) to the cube frame so it pops the same way.")]
    public Material frameOutlineMaterial;

    // The portal-core sphere's transform, once built — WavePreview zooms/orbits around this.
    public Transform Core { get; private set; }

    void Start()
    {
        var frameCol = (mode == Mode.Start) ? startFrameColor : endFrameColor;

        BuildCubeFrame(frameCol);
        BuildPortalCore();
    }

    // ── Cube frame ──────────────────────────────────────────────────────

    void BuildCubeFrame(Color color)
    {
        var frame = new GameObject("Frame");
        frame.transform.SetParent(transform, false);

        float h = 0.5f * frameScale;
        Vector3[] v =
        {
            new(-h,-h,-h), new(+h,-h,-h),
            new(-h,+h,-h), new(+h,+h,-h),
            new(-h,-h,+h), new(+h,-h,+h),
            new(-h,+h,+h), new(+h,+h,+h),
        };
        var edges = new (int, int)[]
        {
            (0,1),(2,3),(4,5),(6,7),
            (0,2),(1,3),(4,6),(5,7),
            (0,4),(1,5),(2,6),(3,7),
        };
        foreach (var (a, b) in edges)
            BuildEdge(frame.transform, v[a], v[b], color);

        if (frameOutlineMaterial != null)
        {
            var applier = frame.AddComponent<BlockOutlineApplier>();
            applier.outlineMaterial = frameOutlineMaterial;
            applier.applyOnAwake    = false;
            applier.includeChildren = true;
            applier.Apply();
        }
    }

    void BuildEdge(Transform parent, Vector3 a, Vector3 b, Color color)
    {
        var mid    = (a + b) * 0.5f;
        var diff   = b - a;
        float len  = diff.magnitude;
        if (len < 0.001f) return;

        var rod = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rod.name = "Edge";
        var c = rod.GetComponent<Collider>(); if (c != null) Destroy(c);
        rod.transform.SetParent(parent, false);
        rod.transform.localPosition = mid;
        rod.transform.localRotation = Quaternion.FromToRotation(Vector3.up, diff.normalized);
        rod.transform.localScale    = new Vector3(frameThickness, len * 0.5f, frameThickness);

        var r = rod.GetComponent<Renderer>();
        if (r != null) MpbColor.Set(r, color);
    }

    // ── 3D portal core (sphere + shader) ────────────────────────────────

    void BuildPortalCore()
    {
        if (portalMaterial == null)
        {
            Debug.LogWarning($"[EndpointPortalVisual] No portalMaterial on {name} ({mode}). Drag a Material using GeoWorld/ChaosCore (start) or GeoWorld/HaloCore (end).");
            return;
        }

        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "PortalCore";
        var c = sphere.GetComponent<Collider>(); if (c != null) Destroy(c);
        sphere.transform.SetParent(transform, false);
        sphere.transform.localPosition = Vector3.zero;
        sphere.transform.localScale    = Vector3.one * coreScale;
        Core = sphere.transform;

        var rend = sphere.GetComponent<Renderer>();
        rend.sharedMaterial       = portalMaterial;
        rend.shadowCastingMode    = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows       = false;
        rend.lightProbeUsage      = UnityEngine.Rendering.LightProbeUsage.Off;
        rend.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        // Optional outline on the core itself (in addition to the frame).
        if (frameOutlineMaterial != null)
        {
            var applier = sphere.AddComponent<BlockOutlineApplier>();
            applier.outlineMaterial = frameOutlineMaterial;
            applier.applyOnAwake    = false;
            applier.includeChildren = false;
            applier.Apply();
        }
    }
}
