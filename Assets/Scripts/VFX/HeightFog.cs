using UnityEngine;

// A sea of fog lying under the map (GeoWorld/HeightFog): full density below `top`,
// thinning exponentially above it, infinite in x/z. Analytic, not ray-marched — see
// the shader.
//
// Drawn on a big cube that follows the camera every frame; only its back faces'
// screen coverage matters, the ray itself comes from the camera. Sized to stay
// inside the far plane, or the back faces would be clipped and the fog with them.
public class HeightFog : MonoBehaviour
{
    [System.Serializable]
    public struct Settings
    {
        public Color color;
        [Tooltip("Density below the fog's top, per cell of distance. Higher = the sea goes opaque sooner.")]
        public float density;
        [Tooltip("Where the fog's top sits, in cells, relative to the UNDERSIDE of the lowest map block. Negative = below the blocks.")]
        public float topOffset;
        [Tooltip("How fast it thins above its top, per cell. Lower = a taller, more gradual fade up the sides of the blocks.")]
        public float falloff;
        [Tooltip("How far the top swells up and down, in cells.")]
        public float wave;
        [Tooltip("How far out the fog is integrated, in cells. The sea ends here — keep it past the horizon haze.")]
        public float maxDistance;
        [Range(0f, 1f)] public float skyBlend;
        [Range(0f, 4f)] public float scatter;
        [Range(-0.9f, 0.9f)] public float anisotropy;

        [Header("Keep the map clear")]
        [Tooltip("How completely the fog in front of the map's own blocks and props is removed (1 = fully). Only surfaces standing on ground that is on show, and above the fog line — the fog sea itself is unchanged.")]
        [Range(0f, 1f)] public float mapClear;
        [Tooltip("Height above the fog's top, in cells, where the map starts to clear.")]
        public float clearFrom;
        [Tooltip("Height above the fog's top, in cells, by which the map is fully clear. The feet of the blocks below this still sink into the fog.")]
        public float clearTo;
    }

    Material _mat;
    Camera   _cam;
    static Mesh _cube;

    public static HeightFog Create(Transform parent, Settings s, float top, float cs)
    {
        var sh = Shader.Find("GeoWorld/HeightFog");
        if (sh == null) { Debug.LogWarning("[HeightFog] GeoWorld/HeightFog shader not found."); return null; }

        MistBank.EnsureDepthTexture();

        var go = new GameObject("HeightFog");
        go.transform.SetParent(parent, false);
        var fog = go.AddComponent<HeightFog>();
        fog._mat = new Material(sh) { name = "HeightFog (runtime)" };

        go.AddComponent<MeshFilter>().sharedMesh = Cube();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial       = fog._mat;
        mr.shadowCastingMode    = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows       = false;
        mr.lightProbeUsage      = UnityEngine.Rendering.LightProbeUsage.Off;
        // Off = the renderer's environment cubemap is the SKYBOX, which is what the
        // shader's sky blend reads.
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        fog.Apply(s, top, cs);
        fog.LateUpdate();
        return fog;
    }

    public void Apply(Settings s, float top, float cs)
    {
        if (_mat == null) return;
        // Per-cell values in, per-world-unit values out.
        _mat.SetColor("_FogColor",    s.color);
        _mat.SetFloat("_Density",     Mathf.Max(0f, s.density) / cs);
        _mat.SetFloat("_FogTop",      top + s.topOffset * cs);
        _mat.SetFloat("_Falloff",     Mathf.Max(0.01f, s.falloff) / cs);
        _mat.SetFloat("_TopWave",     s.wave * cs);
        _mat.SetFloat("_WaveScale",   0.08f / cs);
        _mat.SetFloat("_MaxDistance", Mathf.Max(10f, s.maxDistance) * cs);
        _mat.SetFloat("_SkyBlend",    s.skyBlend);
        _mat.SetFloat("_Scatter",     s.scatter);
        _mat.SetFloat("_Anisotropy",  s.anisotropy);
        _mat.SetFloat("_MapClear",    s.mapClear);
        _mat.SetFloat("_ClearFrom",   s.clearFrom * cs);
        _mat.SetFloat("_ClearTo",     s.clearTo * cs);
    }

    void LateUpdate()
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;
        transform.position   = _cam.transform.position;
        transform.rotation   = Quaternion.identity;
        // Corners at 0.87 × size from the centre: keep them inside the far plane.
        float size = _cam.farClipPlane * 1.1f;
        var lossy = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        transform.localScale = new Vector3(size / lossy.x, size / lossy.y, size / lossy.z);
    }

    void OnDestroy()
    {
        if (_mat != null) Destroy(_mat);
    }

    static Mesh Cube()
    {
        if (_cube != null) return _cube;
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _cube = tmp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(tmp);
        return _cube;
    }
}
