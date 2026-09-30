using UnityEngine;

// A lake that mirrors the sky over it (LevelEnvironment.backdropLake). The water
// paints the level's own painted sky along each reflected ray (GeoWorld/SkyLake,
// sharing PaintedSkyCore.hlsl with the skybox). This copies the sky material's
// values onto the lake every frame, so whatever BackgroundReactor does to the
// sky — combat, a clear, a flash, the music — happens in the water too. The
// camera mostly looks down at the board; this puts the sky where it is looking.
public class SkyLake : MonoBehaviour
{
    static readonly string[] SkyColors =
        { "_Deep", "_Blue", "_Teal", "_Cream", "_Warm", "_Hot", "_Ground", "_Sun", "_FlashColor", "_IntroColor" };
    static readonly string[] SkyFloats =
    {
        "_SunSize", "_StrokeScale", "_StrokeLength", "_StrokeWidth", "_Swirl", "_Warmth",
        "_BeatPulse", "_MusicIntensity", "_PitchGlow", "_CombatMode", "_ClearReact", "_KillReact",
        "_Collapse", "_DamageTint", "_FlashAmount", "_IntroBlend",
    };
    static int[] _colorIds, _floatIds;
    static readonly int LayersId = Shader.PropertyToID("_Layers");
    static readonly int DeepId   = Shader.PropertyToID("_Deep");

    Material _mat;
    Mesh     _mesh;
    OrbitCamera _orbit;
    float _prevMinCameraY, _prevMinFocusY;

    public static SkyLake Create(Transform parent, Vector3 centre, float y, float radius, LevelEnvironment env, float cs)
    {
        var sh = Shader.Find("GeoWorld/SkyLake");
        if (sh == null) { Debug.LogWarning("[SkyLake] GeoWorld/SkyLake shader not found — no lake."); return null; }

        if (_colorIds == null)
        {
            _colorIds = new int[SkyColors.Length];
            for (int i = 0; i < SkyColors.Length; i++) _colorIds[i] = Shader.PropertyToID(SkyColors[i]);
            _floatIds = new int[SkyFloats.Length];
            for (int i = 0; i < SkyFloats.Length; i++) _floatIds[i] = Shader.PropertyToID(SkyFloats[i]);
        }

        var go = new GameObject("SkyLake");
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(new Vector3(centre.x, y, centre.z), Quaternion.identity);

        var lake = go.AddComponent<SkyLake>();
        lake._mesh = Disc(radius, 72);
        lake._mat  = new Material(sh) { name = "SkyLake (runtime)" };
        lake._mat.SetColor("_LakeTint",        env.backdropLakeTint);
        lake._mat.SetFloat("_LakeMirror",      env.backdropLakeMirror);
        lake._mat.SetFloat("_LakeRipple",      env.backdropLakeRipple);
        lake._mat.SetFloat("_LakeRippleScale", 0.35f / Mathf.Max(0.01f, cs));
        lake._mat.SetFloat("_LakeShore",       0.4f * cs);
        lake._mat.SetFloat("_LakeClear",       6f * cs);

        go.AddComponent<MeshFilter>().sharedMesh = lake._mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial       = lake._mat;
        r.shadowCastingMode    = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows       = true;
        r.lightProbeUsage      = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        // The gameplay camera may not sink to (or through) the water: its body
        // and its focus both stop a little above the surface.
        var cam = Camera.main;
        var orbit = cam != null ? cam.GetComponent<OrbitCamera>() : null;
        if (orbit != null)
        {
            lake._orbit = orbit;
            lake._prevMinCameraY = orbit.minCameraY;
            lake._prevMinFocusY  = orbit.minFocusY;
            orbit.minCameraY = Mathf.Max(orbit.minCameraY, y + 1.5f * cs);
            orbit.minFocusY  = Mathf.Max(orbit.minFocusY,  y + 1f * cs);
        }

        MistBank.EnsureDepthTexture();   // the banks read scene depth; with the level's fog off nothing else asks for it
        lake.Sync();
        return lake;
    }

    void LateUpdate() => Sync();

    // From the sky when it is the painted one; under any other sky, from the
    // painted sky's own defaults (its keepalive material), so the lake still
    // shows a sky rather than the black of unset colours.
    static Material _fallbackSky;
    void Sync()
    {
        if (_mat == null) return;
        var sky = RenderSettings.skybox;
        if (sky == null || !sky.HasProperty(DeepId))
        {
            if (_fallbackSky == null) _fallbackSky = Resources.Load<Material>("GeoWorldShaderKeepalive/PaintedSky_keep");
            sky = _fallbackSky;
            if (sky == null) return;
        }
        for (int i = 0; i < _colorIds.Length; i++)
            if (sky.HasProperty(_colorIds[i])) _mat.SetColor(_colorIds[i], sky.GetColor(_colorIds[i]));
        for (int i = 0; i < _floatIds.Length; i++)
            if (sky.HasProperty(_floatIds[i])) _mat.SetFloat(_floatIds[i], sky.GetFloat(_floatIds[i]));
        // One stroke layer at most: the ripples blur the finer one away anyway,
        // and the lake can fill as much of the screen as the sky does.
        _mat.SetFloat(LayersId, Mathf.Min(sky.HasProperty(LayersId) ? sky.GetFloat(LayersId) : 1f, 1f));
    }

    void OnDestroy()
    {
        if (_orbit != null) { _orbit.minCameraY = _prevMinCameraY; _orbit.minFocusY = _prevMinFocusY; }
        if (_mat  != null) Destroy(_mat);
        if (_mesh != null) Destroy(_mesh);
    }

    // A flat disc facing up, centred on the object.
    static Mesh Disc(float radius, int segments)
    {
        var v = new Vector3[segments + 1];
        var n = new Vector3[segments + 1];
        var t = new int[segments * 3];
        v[0] = Vector3.zero; n[0] = Vector3.up;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            v[i + 1] = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            n[i + 1] = Vector3.up;
            int j = (i + 1) % segments;
            t[i * 3]     = 0;
            t[i * 3 + 1] = j + 1;   // clockwise seen from above
            t[i * 3 + 2] = i + 1;
        }
        var m = new Mesh { name = "SkyLake" };
        m.vertices = v; m.normals = n; m.triangles = t;
        m.RecalculateBounds();
        return m;
    }
}
