using System.Collections;
using UnityEngine;

// Abstract geometric art visual for the SurfaceUnit.
//
// Structure (all parented to a slow-tumbling Body pivot):
//   • Core sphere  — bright glowing centre, pulses gently
//   • Ring A       — horizontal gyroscope ring, fast CCW spin  (cyan)
//   • Ring B       — 60° X-tilted ring, medium CW spin         (violet)
//   • Ring C       — 60° Z-tilted ring, slow CCW spin          (amber)
//   • 4 cube nodes — orbit on tilted planes with diamond orientation
//
// Requires the Custom/UnitGlow shader to be in the project.
// If the shader is missing every primitive falls back to the default material.
public class SurfaceUnitVisual : MonoBehaviour
{
    [Header("Core Sphere")]
    public Color  coreColor      = new Color(1.00f, 0.95f, 0.65f, 1f);
    public float  coreRadius     = 0.27f;
    public float  corePulseHz    = 2.0f;
    public float  corePulseDepth = 0.20f;

    [Header("Gyroscope Rings")]
    public Color  ringColorA     = new Color(0.25f, 0.85f, 1.00f, 1f);  // cyan
    public Color  ringColorB     = new Color(0.80f, 0.35f, 1.00f, 1f);  // violet
    public Color  ringColorC     = new Color(1.00f, 0.65f, 0.20f, 1f);  // amber
    public float  ringRadius     = 0.66f;
    public float  ringThickness  = 0.048f;
    public float  spinSpeedA     =  88f;   // deg/s, CCW around local Y
    public float  spinSpeedB     = -60f;   // deg/s, CW
    public float  spinSpeedC     =  42f;   // deg/s, CCW

    [Header("Orbiting Nodes")]
    public Color  nodeColor       = new Color(1f, 1f, 1f, 0.90f);
    public float  nodeSize        = 0.120f;
    public float  nodeOrbitRadius = 0.465f;
    public float  nodeSpeedA      =  110f;
    public float  nodeSpeedB      =  -82f;
    public float  nodeSpeedC      =   67f;
    public float  nodeSpeedD      = -120f;

    [Header("Body Tumble")]
    public float  bodyRotSpeed    = 12f;   // slow ambient rotation of the whole structure

    [Header("Glow")]
    public float  glowIntensity   = 1.5f;

    // ── internals ─────────────────────────────────────────────────────────
    Shader    _glow;
    Transform _body;

    void Start()
    {
        _glow = Shader.Find("Custom/UnitGlow");

        _body = new GameObject("Body").transform;
        _body.SetParent(transform, false);

        BuildCore();
        BuildRings();
        BuildNodes();
    }

    void Update() => _body?.Rotate(Vector3.up, bodyRotSpeed * Time.deltaTime, Space.Self);

    // ── Core ──────────────────────────────────────────────────────────────

    void BuildCore()
    {
        var obj = MakePrimitive(PrimitiveType.Sphere, Vector3.zero,
            Vector3.one * coreRadius * 2f,
            coreColor, glowIntensity * 1.4f, corePulseHz, corePulseDepth);
        obj.transform.SetParent(_body, false);
    }

    // ── Gyroscope rings ───────────────────────────────────────────────────

    void BuildRings()
    {
        float h = ringThickness * 0.5f;

        var rA = MakeRing(ringRadius, h, ringColorA);
        rA.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
        StartCoroutine(SpinSelf(rA.transform, Vector3.up, spinSpeedA));

        var rB = MakeRing(ringRadius, h, ringColorB);
        rB.transform.localRotation = Quaternion.Euler(60f, 0f, 0f);
        StartCoroutine(SpinSelf(rB.transform, Vector3.up, spinSpeedB));

        var rC = MakeRing(ringRadius, h, ringColorC);
        rC.transform.localRotation = Quaternion.Euler(0f, 0f, 60f);
        StartCoroutine(SpinSelf(rC.transform, Vector3.up, spinSpeedC));
    }

    GameObject MakeRing(float radius, float halfH, Color col)
    {
        var obj = MakePrimitive(PrimitiveType.Cylinder, Vector3.zero,
            new Vector3(radius, halfH, radius),
            col, glowIntensity * 0.85f);
        obj.transform.SetParent(_body, false);
        return obj;
    }

    // ── Orbiting cube nodes ───────────────────────────────────────────────

    void BuildNodes()
    {
        // Each node starts at a different phase and orbits in a different tilted plane.
        AddNode(0f,              nodeSpeedA, new Vector3( 1.0f,  0.4f,  0.0f));
        AddNode(Mathf.PI * 0.5f, nodeSpeedB, new Vector3( 0.0f,  0.5f,  1.0f));
        AddNode(Mathf.PI,        nodeSpeedC, new Vector3(-0.5f,  1.0f,  0.3f));
        AddNode(Mathf.PI * 1.5f, nodeSpeedD, new Vector3( 0.3f,  0.0f, -1.0f));
    }

    void AddNode(float startPhase, float speedDeg, Vector3 orbitAxis)
    {
        var obj = MakePrimitive(PrimitiveType.Cube, Vector3.zero,
            Vector3.one * nodeSize,
            nodeColor, glowIntensity, corePulseHz * 1.3f, 0.15f);
        obj.transform.SetParent(_body, false);
        obj.transform.localRotation = Quaternion.Euler(45f, 45f, 0f);   // diamond facing
        StartCoroutine(OrbitNode(obj.transform, nodeOrbitRadius, speedDeg, startPhase,
                                 orbitAxis.normalized));
    }

    // ── Primitive factory ─────────────────────────────────────────────────

    GameObject MakePrimitive(PrimitiveType type, Vector3 localPos, Vector3 localScale,
        Color col, float intensity = 2.5f, float pulseHz = 1.6f, float pulseDepth = 0.18f)
    {
        var obj = GameObject.CreatePrimitive(type);
        obj.transform.localPosition = localPos;
        obj.transform.localScale    = localScale;

        var col3D = obj.GetComponent<Collider>();
        if (col3D) Destroy(col3D);

        if (_glow != null)
        {
            var mat = new Material(_glow);
            mat.SetColor("_Color",      col);
            mat.SetFloat("_Intensity",  intensity);
            mat.SetFloat("_PulseSpeed", pulseHz);
            mat.SetFloat("_PulseDepth", pulseDepth);
            obj.GetComponent<MeshRenderer>().material = mat;
        }

        return obj;
    }

    // ── Coroutines ────────────────────────────────────────────────────────

    IEnumerator SpinSelf(Transform t, Vector3 localAxis, float degPerSec)
    {
        while (true)
        {
            t.Rotate(localAxis, degPerSec * Time.deltaTime, Space.Self);
            yield return null;
        }
    }

    IEnumerator OrbitNode(Transform t, float radius, float degPerSec,
        float startPhase, Vector3 axis)
    {
        // Build two orthogonal vectors in the orbital plane
        Vector3 ref1  = Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right;
        Vector3 perpA = Vector3.Cross(axis, ref1).normalized;
        Vector3 perpB = Vector3.Cross(axis, perpA).normalized;

        while (true)
        {
            float a = Time.time * degPerSec * Mathf.Deg2Rad + startPhase;
            t.localPosition = (perpA * Mathf.Cos(a) + perpB * Mathf.Sin(a)) * radius;
            // Slow self-rotation so the cube tumbles as it orbits
            t.Rotate(Vector3.one, 90f * Time.deltaTime, Space.Self);
            yield return null;
        }
    }
}
