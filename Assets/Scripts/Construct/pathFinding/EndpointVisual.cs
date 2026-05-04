using System.Collections;
using UnityEngine;

// Endpoint visual: the original cube stays visible but its SacredMarker material is
// tweaked to become a transparent aerogel haze (no bright core).  A black-hole
// sphere is added at the block centre, visible through the haze.
//
// START block: warm-coloured core (orange/amber) + thin antenna spire above.
// END   block: cool-coloured core (cyan/blue)   + flat spinning ring around block.
//
// If the black-hole sphere appears inside/outside the block, adjust coreScale.
// If the antenna/ring floats at the wrong height, adjust blockTopLocal.
public class EndpointVisual : MonoBehaviour
{
    [Header("Block haze (SacredMarker tweaks)")]
    [Range(0f, 1f)] public float hazeStrength = 0.55f;   // translucency of the block body

    [Header("Core sphere")]
    public float coreScale    = 0.58f;   // diameter relative to block local scale
    public float pulseSpeed   = 1.8f;
    public float pulseAmount  = 0.07f;   // fractional scale oscillation

    [Header("Start — warm color scheme")]
    public Color startDisk   = new Color(1.00f, 0.52f, 0.08f, 1f);  // orange ring
    public Color startStream = new Color(1.00f, 0.85f, 0.28f, 1f);  // amber streams
    public Color startHaze   = new Color(1.00f, 0.22f, 0.70f, 1f);  // magenta haze

    [Header("End — cool color scheme")]
    public Color endDisk     = new Color(0.18f, 0.85f, 1.00f, 1f);  // cyan ring
    public Color endStream   = new Color(0.45f, 1.00f, 0.82f, 1f);  // mint streams
    public Color endHaze     = new Color(0.38f, 0.25f, 1.00f, 1f);  // violet haze

    [Header("Block geometry reference")]
    // Local-space Y of the top face of the block (default 0.5 for a unit cube centred at origin).
    // Increase this if the antenna/ring float inside the block.
    public float blockTopLocal = 0.50f;

    [Header("Start — antenna spire above block")]
    public float antennaHeight = 0.52f;
    public float antennaRadius = 0.024f;

    [Header("End — spinning ring around block")]
    public float ringRadius    = 0.60f;   // slightly larger than block half-width
    public float ringThickness = 0.022f;
    public float ringTilt      = 13f;
    public float ringRotSpeed  = 28f;

    // ── internals ────────────────────────────────────────────────────────────
    Material _hazeMat;   // modified instance of the block's SacredMarker material

    void Start()
    {
        bool isStart = gameObject.name.StartsWith("start", System.StringComparison.OrdinalIgnoreCase);

        // ── Turn the block into transparent aerogel haze ──────────────────────
        // Zero out the core brightness so the inside is hollow and the black-hole
        // sphere inside is the dominant visual.
        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            if (r.sharedMaterial == null) continue;
            var mat = r.material;                   // creates a per-object instance
            mat.SetFloat("_CoreOpacity",   0f);     // no bright SacredMarker core
            mat.SetFloat("_GlowStrength",  0.04f);  // barely-there inner glow
            mat.SetFloat("_HazeStrength",  hazeStrength);
            if (_hazeMat == null) _hazeMat = mat;
        }

        Build(isStart);
    }

    void Build(bool isStart)
    {
        BuildCore(isStart);

        if (isStart)
            BuildAntenna();
        else
            BuildRing();
    }

    // ── Black hole core ───────────────────────────────────────────────────────

    void BuildCore(bool isStart)
    {
        var shader = Shader.Find("Custom/BlackHoleCore");
        Material mat;
        if (shader != null)
        {
            mat = new Material(shader);
            mat.SetColor("_DiskColor",   isStart ? startDisk   : endDisk);
            mat.SetColor("_StreamColor", isStart ? startStream : endStream);
            mat.SetColor("_HazeColor",   isStart ? startHaze   : endHaze);
        }
        else
        {
            // Fallback: reuse block material so at least something renders
            mat = _hazeMat;
        }

        var obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        obj.transform.SetParent(transform, false);
        obj.transform.localPosition = Vector3.zero;   // block centre
        obj.transform.localScale    = Vector3.one * coreScale;
        if (mat != null) obj.GetComponent<MeshRenderer>().material = mat;
        var col = obj.GetComponent<Collider>();
        if (col) Destroy(col);

        StartCoroutine(PulseCore(obj.transform));
    }

    // ── Start indicator: thin antenna spire above the block ───────────────────

    void BuildAntenna()
    {
        float baseY   = blockTopLocal + 0.01f;           // just above block top face
        float centerY = baseY + antennaHeight * 0.5f;
        var   obj     = AddCylinder(new Vector3(0f, centerY, 0f),
                                    new Vector3(antennaRadius, antennaHeight * 0.5f, antennaRadius));
        _ = obj;   // no extra coroutine needed
    }

    // ── End indicator: flat spinning ring around the block perimeter ──────────

    void BuildRing()
    {
        float ringY = blockTopLocal + ringThickness;     // sit just above the top face
        var   ring  = AddCylinder(new Vector3(0f, ringY, 0f),
                                  new Vector3(ringRadius, ringThickness * 0.5f, ringRadius));
        ring.transform.localRotation = Quaternion.Euler(ringTilt, 0f, 0f);
        StartCoroutine(Spin(ring.transform));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    GameObject AddCylinder(Vector3 localPos, Vector3 localScale)
    {
        var obj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        obj.transform.SetParent(transform, false);
        obj.transform.localPosition = localPos;
        obj.transform.localScale    = localScale;
        if (_hazeMat != null) obj.GetComponent<MeshRenderer>().material = _hazeMat;
        var col = obj.GetComponent<Collider>();
        if (col) Destroy(col);
        return obj;
    }

    IEnumerator PulseCore(Transform t)
    {
        float phase = Random.value * Mathf.PI * 2f;
        while (true)
        {
            float s = coreScale * (1f + pulseAmount * Mathf.Sin(Time.time * pulseSpeed + phase));
            t.localScale = Vector3.one * s;
            yield return null;
        }
    }

    IEnumerator Spin(Transform t)
    {
        while (true)
        {
            t.Rotate(Vector3.up, ringRotSpeed * Time.deltaTime, Space.Self);
            yield return null;
        }
    }
}
