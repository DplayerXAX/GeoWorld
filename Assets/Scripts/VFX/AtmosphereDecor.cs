using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Distant atmosphere for the gameplay scene: exponential-ish distance fog,
// stylized Tyndall (god-ray) light shafts, and a field of slowly drifting
// Manifold-Garden-style floating geometry. Everything is built at runtime and
// rendered ONLY by the perspective skybox dome camera (PerspectiveSkyboxCamera),
// so it sits far behind the orthographic gameplay with real parallax and never
// clutters the play field. Calm by default; combat nudges the motion/glow up.
//
// Auto-spawns in the gameplay scene (needs the SkyboxBackgroundCamera rig +
// GameFlowManager). Drop the component into a scene by hand to tune the fields —
// the auto-spawner skips when one already exists.
[DisallowMultipleComponent]
public class AtmosphereDecor : MonoBehaviour
{
    [Header("Render routing")]
    [Tooltip("Layer used for distant decor — rendered by the perspective skybox camera, hidden from the orthographic main camera.")]
    public int backdropLayer = 11;

    [Header("Pillars (slender minimalist columns receding into fog)")]
    public int     archCount       = 22;
    [Tooltip("How far out the pillars stand (world units). Wide band = layered depth.")]
    public Vector2 archRadiusRange = new Vector2(150f, 360f);
    [Tooltip("Pillar height range — tall and airy.")]
    public Vector2 archHeightRange = new Vector2(45f, 135f);
    [Tooltip("Pillar thickness — keep small for slender columns.")]
    public Vector2 archWidthRange  = new Vector2(2.5f, 6f);
    [Tooltip("Ground level the pillars rise from (world Y).")]
    public float   archBaseY       = -22f;
    [Tooltip("Chance a pillar gets a thin geometric cap.")]
    [Range(0f, 1f)] public float archCapChance = 0.25f;
    [Tooltip("Pillar colour (warm pale stone). Haze blends it toward the glowing fog.")]
    public Color   pillarColor     = new Color(0.95f, 0.86f, 0.74f);
    [Range(0f, 0.4f)] public float pillarColorJitter = 0.12f;

    [Header("Floating art shapes (0 = off, for the minimalist pillar look)")]
    public int     shapeCount   = 0;
    [Tooltip("Shell radius range around the focus (world units).")]
    public Vector2 radiusRange  = new Vector2(80f, 160f);
    public float   heightRange  = 75f;
    public Vector2 sizeRange    = new Vector2(2.5f, 9f);
    [Tooltip("Self-spin (deg/s) base; randomised per shape.")]
    public float   spinSpeed    = 6f;
    public float   bobAmplitude = 2.2f;
    public float   bobSpeed     = 0.3f;
    [Tooltip("Slow orbit of the whole field around the focus (deg/s).")]
    public float   driftSpeed   = 1f;

    [Tooltip("Flat colours for the floating shapes (muted/monochrome to match the pillars).")]
    public Color[] palette =
    {
        new Color(0.90f, 0.86f, 0.78f),
        new Color(0.82f, 0.76f, 0.66f),
        new Color(0.74f, 0.66f, 0.55f),
    };

    [Header("Light columns (giant vertical god-ray pillars rising from underground, far off)")]
    public int     shaftCount    = 6;
    [Tooltip("Column height range — giant. Base sits below the floor so they grow up from underground.")]
    public Vector2 shaftLength    = new Vector2(170f, 320f);
    public Vector2 shaftWidth     = new Vector2(12f, 28f);
    [Tooltip("Distance band from the focus where the columns stand (far away).")]
    public Vector2 shaftDistance  = new Vector2(90f, 240f);
    [Tooltip("Base Y — well below the floor so the columns appear to emerge from underground.")]
    public float   shaftBaseY     = -55f;
    public Color   shaftColor     = new Color(1f, 0.88f, 0.6f, 0.2f);   // warm light (additive)
    public float   shaftPulse     = 0.3f;
    public float   shaftPulseSpeed = 0.5f;

    [Header("Column-base fog — legacy billboard fog (off; depth fog covers this)")]
    public bool    baseFog          = false;
    public int     baseFogPerColumn = 6;
    public float   baseFogY         = -10f;
    public Vector2 baseFogSize      = new Vector2(45f, 95f);
    [Range(0f, 1f)] public float baseFogAlpha = 0.22f;
    public float   baseFogScatter   = 18f;   // XZ jitter around the column base

    [Header("Light-source glow (the bright bloom up high)")]
    public bool    sunGlow         = true;
    public Vector3 sunGlowDir      = new Vector3(0.15f, 1f, 0.1f);   // direction from focus to the glow (up high)
    public Color   sunGlowColor    = new Color(1f, 0.85f, 0.55f, 0.38f);
    public float   sunGlowSize     = 140f;
    public float   sunGlowDistance = 240f;
    public float   sunGlowPulse    = 0.12f;

    [Header("Distant fog banks — legacy billboard fog (off; replaced by depth fog)")]
    public bool    fogPatches        = false;
    public int     fogPatchCount     = 28;
    [Tooltip("Distance band from the focus where the fog banks sit (far away).")]
    public Vector2 fogPatchDistance  = new Vector2(75f, 240f);
    [Tooltip("Height band (world Y) for the banks — around the pillar bases / horizon.")]
    public Vector2 fogPatchHeight    = new Vector2(-12f, 45f);
    [Tooltip("Bank size (width); height is ~0.55× this for a wide bank shape.")]
    public Vector2 fogPatchSize      = new Vector2(60f, 135f);
    [Tooltip("Per-card alpha — keep LOW; density comes from many overlapping cards (alpha caps at opaque, won't brighten).")]
    [Range(0f, 1f)] public float fogPatchAlpha = 0.18f;
    [Tooltip("Slow horizontal drift of the banks around the focus (deg/s).")]
    public float   fogPatchDrift     = 1.2f;

    [Header("Volumetric depth fog (raymarched: noise clumps + main-light scattering)")]
    [Tooltip("Screen-space volumetric fog on the backdrop camera. Needs the GeoWorld/DepthFog shader.")]
    public bool    depthFog              = true;
    public Color   depthFogColor         = new Color(0.70f, 0.63f, 0.56f);
    [Tooltip("Tints the in-scattered MAIN LIGHT (the Tyndall glow). White = pure light colour.")]
    public Color   depthFogScatterTint   = Color.white;
    [Tooltip("Overall fog density (after the distance ramp).")]
    public float   depthFogBaseDensity   = 0.04f;
    public float   depthFogExtinction    = 1f;
    [Tooltip("Clear radius — NO fog within this distance from the camera.")]
    public float   depthFogStart         = 40f;
    [Tooltip("Over this distance past the start, fog ramps from 0 to full (atmospheric perspective).")]
    public float   depthFogRamp          = 140f;
    [Tooltip("How far the ray marches (also the sky fog reach). Keep ≤ camera far clip.")]
    public float   depthFogMaxDistance   = 420f;
    [Tooltip("Below this world Y the fog is full; thins above by Height Falloff. Lower = fog tops out lower.")]
    public float   depthFogHeight        = 0f;
    [Tooltip("Higher = fog thins faster with height (lower fog ceiling). Raise this if fog climbs too far up the sky.")]
    public float   depthFogHeightFalloff = 0.2f;
    [Range(0f, 2f)] public float depthFogStrength = 1f;

    [Header("  ↳ Light scattering (Tyndall)")]
    [Tooltip("Henyey-Greenstein anisotropy. + = forward scatter → bright beams when looking toward the light.")]
    [Range(-0.95f, 0.95f)] public float depthFogAnisotropy = 0.6f;
    [Range(0f, 4f)] public float depthFogScatter = 1.2f;
    [Tooltip("Raymarch steps — more = smoother shafts, costlier.")]
    [Range(4, 40)]  public int   depthFogSteps   = 14;

    [Header("  ↳ Noise (clumps / layers)")]
    public float   depthFogNoiseScale    = 0.02f;
    [Range(0f, 1f)] public float depthFogNoiseAmount = 0.7f;
    [Tooltip("Wind (xyz) scrolling the noise so the fog drifts/churns.")]
    public Vector3 depthFogWind          = new Vector3(0.5f, 0.05f, 0.3f);

    [Tooltip("Radius of the surrounding fog sphere (just needs to enclose the camera).")]
    public float   depthFogRadius        = 25f;

    [Header("Distance haze (pillars fade toward the fog colour with distance)")]
    [Tooltip("Legacy per-pillar tint haze (off; the depth fog now fades the pillars).")]
    public bool  enableFog = false;
    [Tooltip("Fog colour — a DESATURATED warm grey (not bright). Shared by the fog banks + pillar haze.")]
    public Color fogColor  = new Color(0.70f, 0.63f, 0.56f);
    [Tooltip("Below this distance decor keeps its full colour; far pillars haze out toward `fogColor` by `fogEnd`.")]
    public float fogStart  = 40f;
    public float fogEnd     = 280f;
    [Range(0f, 1f)] public float fogMaxStrength = 0.88f;   // keep a faint silhouette — real fog isn't 100%

    [Header("Combat reaction")]
    [Range(0f, 2f)] public float combatMotionBoost = 0.6f;
    [Range(0f, 2f)] public float combatGlowBoost   = 0.5f;
    public float combatLerpSpeed = 2f;

    // ── Runtime state ───────────────────────────────────────────────────────────
    struct Floater
    {
        public Transform t;
        public Vector3   axis;
        public float     spin, bobPhase, bobAmp, baseY, driftAngle, radius, drift;
        public Vector3   centerXZ;
    }

    struct Shaft
    {
        public Transform t;
        public Renderer  r;
        public float     phase, baseAlpha, swayPhase;
        public Quaternion baseRot;
    }

    // Opaque decor we recolour for the distance haze each frame.
    struct Decor { public Renderer r; public Color baseColor; }

    // Soft camera-facing fog bank.
    struct Patch { public Transform t; public Renderer r; public float angle, dist, height, phase, baseAlpha, drift; }

    readonly List<Floater> _floaters = new();
    readonly List<Shaft>   _shafts   = new();
    readonly List<Decor>   _decor    = new();
    readonly List<Patch>   _patches  = new();

    Vector3   _center;
    Transform _camT;          // perspective dome camera (haze is by distance from it)
    float     _combat;
    Material  _shaftMat;
    Material  _floaterMat;
    Material  _patchMat;
    Material  _glowMat;
    Texture2D _shaftTex;
    Texture2D _patchTex;
    Mesh      _octa, _tetra, _quad;
    Transform _sunGlow;
    Renderer  _sunGlowR;
    Transform _depthFog;
    Material  _depthFogMat;
    MaterialPropertyBlock _mpb;

    bool _started;     // gate OnValidate so it only rebuilds at runtime
    bool _dirty;       // an Inspector field changed → rebuild next Update

    // ── Auto-spawn (gameplay only) ──────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TrySpawn();
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m) => TrySpawn();

    static void TrySpawn()
    {
        if (FindFirstObjectByType<AtmosphereDecor>() != null) return;            // hand-placed wins
        if (SceneManager.GetActiveScene().name != "LevelSelect") return;         // LevelSelect only
        new GameObject("AtmosphereDecor").AddComponent<AtmosphereDecor>();
    }

    void Start()
    {
        _mpb = new MaterialPropertyBlock();

        var orbit = FindFirstObjectByType<OrbitCamera>();
        _center = orbit != null ? orbit.FocusPoint : Vector3.zero;
        transform.position = _center;

        RouteToBackdropCamera();
        BuildAll();
        _started = true;
    }

    void BuildAll()
    {
        EnsureAssets();
        BuildArchitecture();
        BuildFloaters();
        BuildShafts();
        BuildFogPatches();
        BuildDepthFog();
        BuildSunGlow();
    }

    // ── Live tuning ─────────────────────────────────────────────────────────────
    // Select the runtime "AtmosphereDecor" object in the Play-mode Hierarchy and
    // tweak any field — OnValidate flags a rebuild so you see it immediately. You
    // can also right-click the component ▸ "Rebuild Atmosphere".
    void OnValidate() { if (_started) _dirty = true; }

    [ContextMenu("Rebuild Atmosphere")]
    public void Rebuild()
    {
        ClearDecor();
        BuildAll();
    }

    void ClearDecor()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);
        _floaters.Clear();
        _shafts.Clear();
        _decor.Clear();
        _patches.Clear();
        _sunGlow = null; _sunGlowR = null;
        _depthFog = null;
        DestroyAssets();
    }

    void DestroyAssets()
    {
        if (_shaftMat != null)    { Destroy(_shaftMat);    _shaftMat = null; }
        if (_shaftTex != null)    { Destroy(_shaftTex);    _shaftTex = null; }
        if (_floaterMat != null)  { Destroy(_floaterMat);  _floaterMat = null; }
        if (_patchMat != null)    { Destroy(_patchMat);    _patchMat = null; }
        if (_patchTex != null)    { Destroy(_patchTex);    _patchTex = null; }
        if (_glowMat != null)     { Destroy(_glowMat);     _glowMat = null; }
        if (_depthFogMat != null) { Destroy(_depthFogMat); _depthFogMat = null; }
        if (_octa != null)        { Destroy(_octa);  _octa  = null; }
        if (_tetra != null)       { Destroy(_tetra); _tetra = null; }
        if (_quad != null)        { Destroy(_quad);  _quad  = null; }
    }

    void EnsureAssets()
    {
        if (_floaterMat == null)
            _floaterMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "DecorFlat" };
        if (_octa  == null) _octa  = BuildOctahedron();
        if (_tetra == null) _tetra = BuildTetrahedron();
    }

    // Show decor on the perspective dome camera, hide it from the ortho main camera.
    void RouteToBackdropCamera()
    {
        int   mask = 1 << backdropLayer;
        float maxR = Mathf.Max(archRadiusRange.y, radiusRange.y);

        // Gameplay-style stacked rig: decor on the perspective backdrop camera,
        // hidden from the ortho main camera.
        var bgGo = GameObject.Find("PerspectiveSkyboxCamera");
        var bg   = bgGo != null ? bgGo.GetComponent<Camera>() : null;
        if (bg != null)
        {
            bg.cullingMask  |= mask;
            bg.farClipPlane  = Mathf.Max(bg.farClipPlane, maxR + 280f);
            var bgData = bg.GetUniversalAdditionalCameraData();
            if (bgData != null) bgData.requiresDepthTexture = true;
            _camT = bg.transform;

            var rig  = FindFirstObjectByType<SkyboxBackgroundCamera>();
            var main = rig != null ? rig.GetComponent<Camera>() : Camera.main;
            if (main != null) main.cullingMask &= ~mask;
            return;
        }

        // Single-camera scene (LevelSelect): render the decor ON the main camera.
        var cam = Camera.main;
        if (cam != null)
        {
            cam.cullingMask  |= mask;
            cam.farClipPlane  = Mathf.Max(cam.farClipPlane, maxR + 280f);
            var cData = cam.GetUniversalAdditionalCameraData();
            if (cData != null) cData.requiresDepthTexture = true;
            _camT = cam.transform;
        }
    }

    // Register an opaque decor renderer for the distance-haze pass, and set its
    // starting colour.
    void RegisterDecor(Renderer r, Color baseColor)
    {
        MpbColor.Set(r, baseColor);
        _decor.Add(new Decor { r = r, baseColor = baseColor });
    }

    void OnDestroy() => DestroyAssets();

    // ── Build: distant fog banks ────────────────────────────────────────────────
    // Soft, camera-facing white-grey quads placed FAR OUT near the horizon (not a
    // global veil) — discrete fog banks drifting among/behind the pillars. The
    // skybox shows everywhere else.
    void EnsurePatchMat()
    {
        if (_patchMat != null) return;
        if (_quad == null)     _quad     = BuildQuad();
        if (_patchTex == null) _patchTex = BuildSoftRadialTexture();
        _patchMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "FogBank" };
        if (_patchMat.HasProperty("_BaseMap"))   _patchMat.SetTexture("_BaseMap", _patchTex);
        if (_patchMat.HasProperty("_BaseColor")) _patchMat.SetColor("_BaseColor", new Color(fogColor.r, fogColor.g, fogColor.b, 1f));
        m_SetAlpha(_patchMat);   // fog = alpha-over (occludes), NOT additive glow
    }

    // One soft camera-facing fog bank added to the animated _patches list.
    void AddPatch(float angleDeg, float dist, float height, float w, float hRatio, float alpha, float drift)
    {
        var go = new GameObject("FogBank");
        go.layer = backdropLayer;
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = _quad;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial    = _patchMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows    = false;

        float ang = angleDeg * Mathf.Deg2Rad;
        go.transform.position   = _center + new Vector3(Mathf.Cos(ang) * dist, height, Mathf.Sin(ang) * dist);
        go.transform.localScale = new Vector3(w, w * hRatio, 1f);

        _patches.Add(new Patch
        {
            t         = go.transform,
            r         = r,
            angle     = angleDeg,
            dist      = dist,
            height    = height,
            phase     = Random.value * Mathf.PI * 2f,
            baseAlpha = alpha * Random.Range(0.7f, 1.1f),
            drift     = drift,
        });
    }

    void BuildFogPatches()
    {
        if (!fogPatches || fogPatchCount <= 0) return;
        EnsurePatchMat();
        // Two overlapping puffs per slot → denser, more volumetric banks.
        for (int i = 0; i < fogPatchCount; i++)
        {
            float ang  = Random.value * 360f;
            float dist = Random.Range(fogPatchDistance.x, fogPatchDistance.y);
            float hgt  = Random.Range(fogPatchHeight.x, fogPatchHeight.y);
            float w    = Random.Range(fogPatchSize.x, fogPatchSize.y);
            AddPatch(ang, dist, hgt, w, Random.Range(0.5f, 0.75f), fogPatchAlpha, fogPatchDrift);
            AddPatch(ang + Random.Range(-6f, 6f), dist + Random.Range(-15f, 15f),
                     hgt + Random.Range(-6f, 10f), w * Random.Range(0.6f, 0.9f),
                     Random.Range(0.5f, 0.8f), fogPatchAlpha * 0.8f, fogPatchDrift);
        }
    }

    // Dense fog pool clustered at a column base so the column rises out of it.
    void AddColumnBaseFog(float angRad, float dist)
    {
        if (!baseFog || baseFogPerColumn <= 0) return;
        float angDeg = angRad * Mathf.Rad2Deg;
        for (int j = 0; j < baseFogPerColumn; j++)
        {
            float dJit = Random.Range(-baseFogScatter, baseFogScatter);
            float aJit = Random.Range(-baseFogScatter, baseFogScatter) / Mathf.Max(1f, dist) * Mathf.Rad2Deg;
            AddPatch(angDeg + aJit, dist + dJit,
                     baseFogY + Random.Range(-4f, 10f),
                     Random.Range(baseFogSize.x, baseFogSize.y),
                     Random.Range(0.45f, 0.7f),
                     baseFogAlpha, 0f);   // static — stays with its column
        }
    }

    // ── Build: volumetric depth fog ─────────────────────────────────────────────
    // A big inward-facing sphere around the backdrop camera whose shader samples
    // scene depth → true distance/height fog + light scattering. Params are pushed
    // every frame in Update so editing fields in Play updates instantly.
    void BuildDepthFog()
    {
        if (!depthFog) return;

        var sh = Shader.Find("GeoWorld/DepthFog");
        if (sh == null)
        {
            Debug.LogWarning("[AtmosphereDecor] Shader 'GeoWorld/DepthFog' not found — depth fog skipped. Make sure Assets/Shader/Fog/DepthFog.shader compiled.");
            return;
        }
        _depthFogMat = new Material(sh) { name = "DepthFog" };

        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
        go.name  = "DepthFog";
        go.layer = backdropLayer;   // backdrop camera only → gameplay stays crisp
        go.transform.SetParent(transform, false);
        go.transform.position   = _camT != null ? _camT.position : _center;
        go.transform.localScale = Vector3.one * (depthFogRadius * 2f);

        var r = go.GetComponent<Renderer>();
        r.sharedMaterial    = _depthFogMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows    = false;
        _depthFog = go.transform;

        PushDepthFogParams();
    }

    void PushDepthFogParams()
    {
        if (_depthFogMat == null) return;
        _depthFogMat.SetColor("_FogColor", depthFogColor);
        _depthFogMat.SetColor("_ScatterTint", depthFogScatterTint);
        _depthFogMat.SetVector("_Wind", depthFogWind);
        _depthFogMat.SetFloat("_BaseDensity", depthFogBaseDensity);
        _depthFogMat.SetFloat("_Extinction", depthFogExtinction);
        _depthFogMat.SetFloat("_FogStart", depthFogStart);
        _depthFogMat.SetFloat("_FogRamp", depthFogRamp);
        _depthFogMat.SetFloat("_MaxDistance", depthFogMaxDistance);
        _depthFogMat.SetFloat("_FogHeight", depthFogHeight);
        _depthFogMat.SetFloat("_HeightFalloff", depthFogHeightFalloff);
        _depthFogMat.SetFloat("_FogStrength", depthFogStrength);
        _depthFogMat.SetFloat("_Anisotropy", depthFogAnisotropy);
        _depthFogMat.SetFloat("_ScatterIntensity", depthFogScatter);
        _depthFogMat.SetFloat("_Steps", depthFogSteps);
        _depthFogMat.SetFloat("_NoiseScale", depthFogNoiseScale);
        _depthFogMat.SetFloat("_NoiseAmount", depthFogNoiseAmount);
    }

    // ── Build: light-source glow ────────────────────────────────────────────────
    // A big soft additive warm disk where the light pours from, billboarded to the
    // camera. Sits far out along sunGlowDir (up high) as the bright source.
    void BuildSunGlow()
    {
        if (!sunGlow) return;

        if (_quad == null)     _quad     = BuildQuad();
        if (_patchTex == null) _patchTex = BuildSoftRadialTexture();

        _glowMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "SunGlow" };
        if (_glowMat.HasProperty("_BaseMap"))   _glowMat.SetTexture("_BaseMap", _patchTex);
        if (_glowMat.HasProperty("_BaseColor")) _glowMat.SetColor("_BaseColor", sunGlowColor);
        m_SetAdditive(_glowMat);

        var go = new GameObject("SunGlow");
        go.layer = backdropLayer;
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = _quad;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial    = _glowMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows    = false;

        Vector3 dir = sunGlowDir.sqrMagnitude < 0.0001f ? Vector3.up : sunGlowDir.normalized;
        go.transform.position   = _center + dir * sunGlowDistance;
        go.transform.localScale = Vector3.one * sunGlowSize;
        _sunGlow  = go.transform;
        _sunGlowR = r;
    }

    // URP Unlit → additive transparent (for LIGHT: shafts, sun glow).
    static void m_SetAdditive(Material m)
    {
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_ZWrite", 0f);
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)BlendMode.One);
        if (m.HasProperty("_Cull")) m.SetInt("_Cull", (int)CullMode.Off);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = (int)RenderQueue.Transparent;
    }

    // URP Unlit → standard alpha-over transparent (for FOG: a participating
    // medium that blends its colour OVER the background, capping at opaque — never
    // brightening like additive does).
    static void m_SetAlpha(Material m)
    {
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_ZWrite", 0f);
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_Cull")) m.SetInt("_Cull", (int)CullMode.Off);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = (int)RenderQueue.Transparent;
    }

    // ── Build: distant tall architecture ────────────────────────────────────────
    void BuildArchitecture()
    {
        for (int i = 0; i < archCount; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var c = go.GetComponent<Collider>(); if (c != null) Destroy(c);
            go.name  = "Tower";
            go.layer = backdropLayer;
            go.transform.SetParent(transform, false);

            // Stratified angle: one pillar per sector + small jitter → even spread, no clumping.
            float ang    = (i + Random.Range(0.25f, 0.75f)) / Mathf.Max(1, archCount) * Mathf.PI * 2f;
            float radius = Random.Range(archRadiusRange.x, archRadiusRange.y);
            float h      = Random.Range(archHeightRange.x, archHeightRange.y);
            float w      = Random.Range(archWidthRange.x,  archWidthRange.y);

            Vector3 baseXZ = _center + new Vector3(Mathf.Cos(ang) * radius, 0f, Mathf.Sin(ang) * radius);
            float   cy     = archBaseY + h * 0.5f;
            go.transform.position      = new Vector3(baseXZ.x, cy, baseXZ.z);
            go.transform.localScale    = new Vector3(w, h, w * Random.Range(0.8f, 1.25f));
            go.transform.localRotation = Quaternion.Euler(0f, Random.value * 360f, 0f);   // yaw only — buildings stand upright

            var rend = go.GetComponent<Renderer>();
            rend.sharedMaterial    = _floaterMat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows    = false;
            float j = 1f - Random.value * pillarColorJitter;        // slight per-pillar value variation
            Color col = new Color(pillarColor.r * j, pillarColor.g * j, pillarColor.b * j);
            RegisterDecor(rend, col);

            // Optional geometric cap (octahedron or a smaller offset box) on top.
            if (Random.value < archCapChance)
            {
                bool octaCap = Random.value < 0.6f;
                GameObject cap;
                if (octaCap)
                {
                    cap = new GameObject("Cap");
                    cap.AddComponent<MeshFilter>().sharedMesh = _octa;
                    cap.AddComponent<MeshRenderer>();
                }
                else
                {
                    cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    var cc = cap.GetComponent<Collider>(); if (cc != null) Destroy(cc);
                }
                cap.layer = backdropLayer;
                cap.transform.SetParent(go.transform, false);
                // Local space is parent-scaled; counter it so the cap reads proportional.
                cap.transform.localPosition = new Vector3(0f, 0.5f + 0.18f, 0f);
                cap.transform.localScale    = new Vector3(0.85f, (w / h) * 0.9f, 0.85f);
                var cr = cap.GetComponent<Renderer>();
                cr.sharedMaterial    = _floaterMat;
                cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                cr.receiveShadows    = false;
                RegisterDecor(cr, col);
            }

            _floaters.Add(new Floater
            {
                t          = go.transform,
                axis       = Vector3.up,
                spin       = 0f,                       // towers don't spin
                bobPhase   = 0f,
                bobAmp     = 0f,                       // and don't bob
                baseY      = cy,
                driftAngle = ang * Mathf.Rad2Deg,
                radius     = radius,
                drift      = driftSpeed * 0.12f,       // barely-there parallax drift
                centerXZ   = new Vector3(_center.x, 0f, _center.z),
            });
        }
    }

    // ── Build: floating art shapes ──────────────────────────────────────────────
    void BuildFloaters()
    {
        for (int i = 0; i < shapeCount; i++)
        {
            int kind = i % 4;   // cube, octahedron, tetrahedron, tumbled cube
            GameObject go;
            if (kind == 0 || kind == 3)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var c = go.GetComponent<Collider>(); if (c != null) Destroy(c);
                if (kind == 3) go.transform.localRotation = Random.rotation;   // tumbled cube
            }
            else
            {
                go = new GameObject("DecorShape");
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = kind == 1 ? _octa : _tetra;
                go.AddComponent<MeshRenderer>();
            }

            go.name              = "Floater";
            go.layer             = backdropLayer;
            go.transform.SetParent(transform, false);

            var rend = go.GetComponent<Renderer>();
            rend.sharedMaterial    = _floaterMat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows    = false;
            RegisterDecor(rend, palette[Random.Range(0, palette.Length)]);

            float ang    = Random.value * Mathf.PI * 2f;
            float radius = Random.Range(radiusRange.x, radiusRange.y);
            float y      = Random.Range(-heightRange, heightRange) * 0.5f;
            Vector3 pos  = _center + new Vector3(Mathf.Cos(ang) * radius, y, Mathf.Sin(ang) * radius);
            float size   = Random.Range(sizeRange.x, sizeRange.y);

            go.transform.position   = pos;
            go.transform.localScale = Vector3.one * size;
            if (kind != 3) go.transform.localRotation = Random.rotation;

            _floaters.Add(new Floater
            {
                t          = go.transform,
                axis       = Random.onUnitSphere,
                spin       = spinSpeed * Random.Range(0.4f, 1.6f) * (Random.value < 0.5f ? -1f : 1f),
                bobPhase   = Random.value * Mathf.PI * 2f,
                bobAmp     = bobAmplitude * Random.Range(0.5f, 1.5f),
                baseY      = pos.y,
                driftAngle = ang * Mathf.Rad2Deg,
                radius     = radius,
                drift      = driftSpeed,
                centerXZ   = new Vector3(_center.x, 0f, _center.z),
            });
        }
    }

    // ── Build: god-ray shafts ───────────────────────────────────────────────────
    void BuildShafts()
    {
        _shaftTex = BuildShaftTexture();
        _shaftMat = BuildShaftMaterial(_shaftTex);
        Mesh cross = BuildCrossQuad();
        EnsurePatchMat();   // base fog reuses the fog-bank material

        for (int i = 0; i < shaftCount; i++)
        {
            var go = new GameObject("LightColumn");
            go.layer = backdropLayer;
            go.transform.SetParent(transform, false);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = cross;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial    = _shaftMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows    = false;

            // Giant vertical column rising from below the floor, far out.
            // Stratified angle for an even ring (no clustering).
            float sAng = (i + Random.Range(0.25f, 0.75f)) / Mathf.Max(1, shaftCount) * Mathf.PI * 2f;
            float sD   = Random.Range(shaftDistance.x, shaftDistance.y);
            go.transform.position   = _center + new Vector3(Mathf.Cos(sAng) * sD, shaftBaseY, Mathf.Sin(sAng) * sD);
            var rot = Quaternion.Euler(0f, Random.value * 360f, 0f);   // upright, random yaw
            go.transform.rotation   = rot;
            go.transform.localScale = new Vector3(Random.Range(shaftWidth.x,  shaftWidth.y),
                                                  Random.Range(shaftLength.x, shaftLength.y),
                                                  Random.Range(shaftWidth.x,  shaftWidth.y));

            _shafts.Add(new Shaft
            {
                t         = go.transform,
                r         = r,
                phase     = Random.value * Mathf.PI * 2f,
                baseAlpha = shaftColor.a * Random.Range(0.7f, 1.1f),
                swayPhase = Random.value * Mathf.PI * 2f,
                baseRot   = rot,
            });

            AddColumnBaseFog(sAng, sD);   // thick fog pool the column rises out of
        }
    }

    // ── Animate ─────────────────────────────────────────────────────────────────
    void Update()
    {
        if (_dirty) { _dirty = false; Rebuild(); }

        float dt = Time.deltaTime;
        Vector3 camPos = _camT != null ? _camT.position : _center;

        // Depth fog rides with the camera; params pushed live for runtime tuning.
        if (_depthFog != null)
        {
            _depthFog.position = camPos;
            PushDepthFogParams();
        }

        bool running = GameFlowManager.Instance != null
                       && GameFlowManager.Instance.phase == GamePhase.Running;
        _combat = Mathf.MoveTowards(_combat, running ? 1f : 0f, combatLerpSpeed * dt);

        float motion = 1f + combatMotionBoost * _combat;
        float t = Time.time;

        for (int i = 0; i < _floaters.Count; i++)
        {
            var f = _floaters[i];
            if (f.t == null) continue;

            if (f.spin != 0f) f.t.Rotate(f.axis, f.spin * motion * dt, Space.World);

            // Slow orbit around the focus + per-shape bob (towers: drift≈0, bob=0).
            f.driftAngle += f.drift * motion * dt;
            float rad = f.driftAngle * Mathf.Deg2Rad;
            float bob = Mathf.Sin(t * bobSpeed + f.bobPhase) * f.bobAmp;
            f.t.position = f.centerXZ + new Vector3(Mathf.Cos(rad) * f.radius,
                                                    f.baseY + bob,
                                                    Mathf.Sin(rad) * f.radius);
            _floaters[i] = f;
        }

        float glow = 1f + combatGlowBoost * _combat;
        for (int i = 0; i < _shafts.Count; i++)
        {
            var s = _shafts[i];
            if (s.r == null) continue;

            float a = s.baseAlpha * glow *
                      (1f + shaftPulse * Mathf.Sin(t * shaftPulseSpeed + s.phase));
            var col = shaftColor; col.a = Mathf.Max(0f, a);

            s.r.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", col);
            s.r.SetPropertyBlock(_mpb);

            // Gentle sway.
            float sway = Mathf.Sin(t * 0.2f + s.swayPhase) * 2.5f;
            s.t.rotation = s.baseRot * Quaternion.Euler(sway, 0f, sway * 0.6f);
        }

        // Distant fog banks: drift slowly around the focus + face the camera.
        for (int i = 0; i < _patches.Count; i++)
        {
            var pch = _patches[i];
            if (pch.t == null) continue;
            pch.angle += pch.drift * dt;
            float rad = pch.angle * Mathf.Deg2Rad;
            pch.t.position = _center + new Vector3(Mathf.Cos(rad) * pch.dist, pch.height, Mathf.Sin(rad) * pch.dist);
            pch.t.rotation = Quaternion.LookRotation(pch.t.position - camPos, Vector3.up);   // billboard

            float a = pch.baseAlpha * (0.94f + 0.06f * Mathf.Sin(t * 0.25f + pch.phase));  // subtle density breathing only
            var col = new Color(fogColor.r, fogColor.g, fogColor.b, Mathf.Clamp01(a));
            pch.r.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", col);
            pch.r.SetPropertyBlock(_mpb);
            _patches[i] = pch;
        }

        // Sun glow: face the camera + gentle pulse.
        if (_sunGlow != null && _sunGlowR != null)
        {
            _sunGlow.rotation = Quaternion.LookRotation(_sunGlow.position - camPos, Vector3.up);
            float gp = 1f + sunGlowPulse * Mathf.Sin(t * 0.6f);
            var gc = sunGlowColor; gc.a = Mathf.Clamp01(gc.a * gp);
            _sunGlowR.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", gc);
            _sunGlowR.SetPropertyBlock(_mpb);
        }

        // Distance haze: lerp each decor renderer toward the fog colour by its
        // distance from the camera. This is what actually reads as "fog".
        if (enableFog)
        {
            float   span   = Mathf.Max(0.01f, fogEnd - fogStart);
            for (int i = 0; i < _decor.Count; i++)
            {
                var d = _decor[i];
                if (d.r == null) continue;
                float dist = Vector3.Distance(camPos, d.r.transform.position);
                float h    = Mathf.Clamp01((dist - fogStart) / span) * fogMaxStrength;
                MpbColor.Set(d.r, Color.Lerp(d.baseColor, fogColor, h));
            }
        }
    }

    // ── Mesh / texture builders ─────────────────────────────────────────────────

    static Mesh BuildOctahedron()
    {
        var m = new Mesh { name = "Octahedron" };
        Vector3[] v =
        {
            Vector3.up, Vector3.down,
            Vector3.left, Vector3.right,
            Vector3.forward, Vector3.back,
        };
        m.vertices = v;
        m.triangles = new[]
        {
            0,4,3, 0,3,5, 0,5,2, 0,2,4,   // top fan
            1,3,4, 1,5,3, 1,2,5, 1,4,2,   // bottom fan
        };
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    static Mesh BuildTetrahedron()
    {
        var m = new Mesh { name = "Tetrahedron" };
        Vector3[] v =
        {
            new Vector3( 0f,    0.75f,  0f),
            new Vector3(-0.7f, -0.4f,  -0.4f),
            new Vector3( 0.7f, -0.4f,  -0.4f),
            new Vector3( 0f,   -0.4f,   0.8f),
        };
        m.vertices = v;
        m.triangles = new[] { 0,2,1, 0,3,2, 0,1,3, 1,2,3 };
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // Two perpendicular quads (a "+" cross) so the shaft never disappears edge-on
    // as the camera orbits. Local +Y is the beam length (0..1), X/Z is the width.
    static Mesh BuildCrossQuad()
    {
        var m = new Mesh { name = "ShaftCross" };
        Vector3[] v =
        {
            new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(0.5f, 1f, 0f), new Vector3(-0.5f, 1f, 0f),
            new Vector3(0f, 0f, -0.5f), new Vector3(0f, 0f, 0.5f), new Vector3(0f, 1f, 0.5f), new Vector3(0f, 1f, -0.5f),
        };
        Vector2[] uv =
        {
            new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1),
            new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1),
        };
        m.vertices  = v;
        m.uv        = uv;
        m.triangles = new[] { 0,1,2, 0,2,3, 4,5,6, 4,6,7 };
        m.RecalculateBounds();
        return m;
    }

    static Mesh BuildQuad()
    {
        var m = new Mesh { name = "FogQuad" };
        m.vertices  = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
        };
        m.uv        = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        m.RecalculateBounds();
        return m;
    }

    // Soft round fog puff: white RGB, alpha highest at the centre fading to 0 at
    // the edge (squared for a softer falloff). _BaseColor tints it to the fog grey.
    static Texture2D BuildSoftRadialTexture()
    {
        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            { name = "FogPuff", wrapMode = TextureWrapMode.Clamp };
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float dx = (x / (float)(N - 1) - 0.5f) * 2f;
            float dy = (y / (float)(N - 1) - 0.5f) * 2f;
            float d  = Mathf.Sqrt(dx * dx + dy * dy);          // 0 centre … 1 edge (at mid-edge)
            float a  = Mathf.SmoothStep(1f, 0f, Mathf.Clamp01(d));  // full centre, soft broad edge
            px[y * N + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    static Texture2D BuildShaftTexture()
    {
        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "ShaftGradient", wrapMode = TextureWrapMode.Clamp };
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
        {
            float v = y / (float)(N - 1);
            float lengthFall = Mathf.Sin(v * Mathf.PI);     // fade in/out along the beam
            for (int x = 0; x < N; x++)
            {
                float u = x / (float)(N - 1);
                float widthFall = Mathf.Cos((u - 0.5f) * Mathf.PI);   // bright centre, soft edges
                float a = Mathf.Clamp01(widthFall) * lengthFall;
                px[y * N + x] = new Color(1f, 1f, 1f, a * a);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    static Material BuildShaftMaterial(Texture2D tex)
    {
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        var m  = new Material(sh) { name = "ShaftAdditive" };
        if (m.HasProperty("_BaseMap"))   m.SetTexture("_BaseMap", tex);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        if (m.HasProperty("_Surface"))
        {
            m.SetFloat("_Surface", 1f);                 // transparent
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.One);  // additive
            if (m.HasProperty("_Cull")) m.SetInt("_Cull", (int)CullMode.Off);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
        return m;
    }
}
