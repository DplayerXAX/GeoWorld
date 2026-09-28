using System.Collections.Generic;
using UnityEngine;

// Puts a LevelEnvironment into the scene for the length of a level: the block
// overlay's globals (GeoWorld/BlockWeather) and the rain soaking into them, the
// sky, the sun, the height fog and far haze, the scenery, the rain and motes, the
// ambience. Created by GameFlowManager at level start; what it changed on things
// that outlive it (the sun, a shared sky material) is put back when it goes.
//
// The board tells it when it changes (BlockSurface → BoardChanged): the fog keeps
// the board clear and follows its lowest block, the far haze keeps its clear ring
// round everything built, the splashes land on the tops open to the sky.
public partial class LevelEnvironmentDriver : MonoBehaviour
{
    public static LevelEnvironment Current { get; private set; }
    public static LevelEnvironmentDriver Instance { get; private set; }

    LevelEnvironment _env;
    float _t;

    Light      _sun;
    float      _sunIntensity0;
    Color      _sunColor0;
    Quaternion _sunRot0;

    Material _skyOriginal;   // only set when WE cloned the sky (no BackgroundReactor did)
    Material _skyClone;

    HeightFog _heightFog;
    MistBank  _haze;
    Rect      _hazeRect;
    bool      _hasHaze;

    OrbitCamera _orbit;
    float _cs = 1f;
    readonly List<Vector3> _tops = new();   // tops of blocks open to the sky

    static readonly int WearTint   = Shader.PropertyToID("_GeoWearTint");
    static readonly int WearStr    = Shader.PropertyToID("_GeoWearStrength");
    static readonly int CrackStr   = Shader.PropertyToID("_GeoCrackStrength");
    static readonly int AOStr      = Shader.PropertyToID("_GeoAOStrength");
    static readonly int AORadius   = Shader.PropertyToID("_GeoAORadius");
    static readonly int Wetness    = Shader.PropertyToID("_GeoWetness");
    static readonly int Flood      = Shader.PropertyToID("_GeoFlood");
    static readonly int Puddles    = Shader.PropertyToID("_GeoPuddles");
    static readonly int RainId     = Shader.PropertyToID("_GeoRain");
    static readonly int Streaks    = Shader.PropertyToID("_GeoStreaks");
    static readonly int Corrosion  = Shader.PropertyToID("_GeoCorrosion");

    public static LevelEnvironmentDriver Apply(LevelEnvironment env)
    {
        var d = new GameObject("LevelEnvironment").AddComponent<LevelEnvironmentDriver>();
        d.Init(env != null ? env : LevelEnvironment.Default);
        return d;
    }

    void Init(LevelEnvironment env)
    {
        _env = env;
        Current  = env;
        Instance = this;
        _cs = GridSystem.instance != null ? GridSystem.instance.cellSize : 1f;

        Shader.SetGlobalColor(WearTint, env.wearTint);
        Shader.SetGlobalFloat(WearStr,  env.wearStrength);
        Shader.SetGlobalFloat(CrackStr, env.crackStrength);
        Shader.SetGlobalFloat(AOStr,    env.contactShadow);
        Shader.SetGlobalFloat(AORadius, env.contactRadius);
        Shader.SetGlobalFloat(Streaks,  env.streaks);
        Shader.SetGlobalFloat(Corrosion, env.corrosion);
        Shader.SetGlobalFloat(Puddles,  env.puddles);
        Shader.SetGlobalFloat(RainId,   env.rain ? Mathf.Clamp01(env.rainIntensity) : 0f);
        UpdateWet();

        ApplySun();
        ApplySky();

        if (env.heightFogEnabled)
            _heightFog = HeightFog.Create(transform, env.heightFog, FloorY(null), _cs);

        if (env.rain && env.rainIntensity > 0f)
        {
            BuildRain();
            if (env.splashes) BuildSplashes();
        }
        if (env.motes != LevelEnvironment.Motes.None && env.moteDensity > 0f) BuildMotes();

        if (env.ambience != null && env.ambience.IsValid()) env.ambience.Post(gameObject);
    }

    // After GameFlowManager.Start has placed the board and pointed the camera at it.
    void Start()
    {
        if (_env.backdrop != LevelEnvironment.Backdrop.None && _env.backdropCount > 0)
        {
            var grid = GridSystem.instance;
            if (grid != null) BoardChanged(grid);
            Vector3 centre = BoardCentre();
            EnvironmentBackdrop.Build(transform, _env, centre, FloorY(grid), _cs);
        }
    }

    void Update()
    {
        _t += Time.deltaTime;
        UpdateWet();
        UpdateSplashes();
    }

    void LateUpdate()
    {
        FollowCamera();
    }

    // ── Rain soaking in ──────────────────────────────────────────────────────
    // Dry → soaked over wetUpSeconds (from wetAtStart); only then does water
    // stand, rising over floodSeconds — Lagarde's order: a surface has to be wet
    // through before it can puddle.
    void UpdateWet()
    {
        if (_env.wetness <= 0f)
        {
            Shader.SetGlobalFloat(Wetness, 0f);
            Shader.SetGlobalFloat(Flood, 0f);
            return;
        }
        float soak  = _env.wetUpSeconds <= 0f ? 1f
                    : Mathf.Lerp(_env.wetAtStart, 1f, _t / _env.wetUpSeconds);
        float flood = 0f;
        if (_env.puddles > 0f && _t > _env.wetUpSeconds)
            flood = _env.floodSeconds <= 0f ? 1f : Mathf.Clamp01((_t - _env.wetUpSeconds) / _env.floodSeconds);

        Shader.SetGlobalFloat(Wetness, _env.wetness * Mathf.SmoothStep(0f, 1f, soak));
        Shader.SetGlobalFloat(Flood,   Mathf.SmoothStep(0f, 1f, flood));
    }

    // ── Sun ──────────────────────────────────────────────────────────────────
    void ApplySun()
    {
        _sun = RenderSettings.sun;
        if (_sun == null)
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { _sun = l; break; }
        if (_sun == null) return;

        _sunIntensity0 = _sun.intensity;
        _sunColor0     = _sun.color;
        _sunRot0       = _sun.transform.rotation;
        _sun.intensity = _sunIntensity0 * _env.sunIntensity;
        _sun.color     = _sunColor0 * _env.sunTint;
        // Light travels AWAY from where it comes from.
        if (_env.overrideSunDirection)
            _sun.transform.rotation = Quaternion.Euler(_env.sunPitch, _env.sunYaw + 180f, 0f);
    }

    // ── Sky ──────────────────────────────────────────────────────────────────
    void ApplySky()
    {
        bool density = !Mathf.Approximately(_env.skyHazeDensity, 1f);
        if (!_env.overrideSky && !density) return;

        var sky = RenderSettings.skybox;
        if (sky == null || sky.shader == null || sky.shader.name != "Custom/ManifoldSkybox") return;

        // BackgroundReactor normally hands the scene a runtime copy already; if it
        // didn't, make one — never write into the project's material asset.
        if (!sky.name.EndsWith("(runtime)"))
        {
            _skyOriginal = sky;
            _skyClone    = new Material(sky) { name = sky.name + " (runtime)" };
            RenderSettings.skybox = sky = _skyClone;
        }

        if (_env.overrideSky)
        {
            sky.SetColor("_ZenithColor",  _env.skyZenith);
            sky.SetColor("_HorizonColor", _env.skyHorizon);
            sky.SetColor("_FogColor",     _env.skyHaze);
        }
        if (density) sky.SetFloat("_FogDensity", sky.GetFloat("_FogDensity") * _env.skyHazeDensity);
        DynamicGI.UpdateEnvironment();   // ambient light comes from the sky
    }

    // ── Board ────────────────────────────────────────────────────────────────
    /// <summary>Called by BlockSurface after every board edit.</summary>
    public static void NotifyBoard(GridSystem grid)
    {
        if (Instance != null) Instance.BoardChanged(grid);
    }

    void BoardChanged(GridSystem grid)
    {
        if (grid == null) return;
        _cs = grid.cellSize;

        _tops.Clear();
        foreach (var ins in grid.GetAllInstances())
        {
            if (ins == null) continue;
            foreach (var c in ins.occupiedCells)
                if (!grid.IsOccupied(c + Vector3Int.up))
                    _tops.Add(grid.GridToWorld(c) + Vector3.up * (_cs * 0.5f));
        }

        bool fog = _heightFog != null || _env.farHazeEnabled;
        if (!fog) return;

        // What must stay clear: every block top, and the endpoints.
        var keep = new List<Vector3>(_tops);
        var gfm = GameFlowManager.Instance;
        if (gfm != null)
        {
            foreach (var c in gfm.AllStarts) keep.Add(grid.GridToWorld(c) + Vector3.up * (_cs * 0.5f));
            foreach (var c in gfm.AllEnds)   keep.Add(grid.GridToWorld(c) + Vector3.up * (_cs * 0.5f));
        }
        MistBank.SetProtected(keep, _cs);

        float floor = FloorY(grid);
        if (_heightFog != null) _heightFog.Apply(_env.heightFog, floor, _cs);

        if (_env.farHazeEnabled && keep.Count > 0) UpdateHaze(keep, floor);
    }

    // The far haze keeps a clear ring round everything built. Rebuilt only when the
    // board reaches toward the edge of that ring, not on every edit.
    void UpdateHaze(List<Vector3> keep, float floor)
    {
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        foreach (var p in keep)
        {
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
        }
        float guard = 2f * _cs;
        if (_hasHaze && minX >= _hazeRect.xMin + guard && maxX <= _hazeRect.xMax - guard
                     && minZ >= _hazeRect.yMin + guard && maxZ <= _hazeRect.yMax - guard) return;

        float pad = _env.farHazeClear * _cs;
        _hazeRect = Rect.MinMaxRect(minX - pad, minZ - pad, maxX + pad, maxZ + pad);
        _hasHaze  = true;

        // Clear ground: the whole rectangle, a point a cell, at the board's floor.
        var clear = new List<Vector3>();
        float y = floor + _cs;
        for (float x = _hazeRect.xMin; x <= _hazeRect.xMax; x += _cs)
            for (float z = _hazeRect.yMin; z <= _hazeRect.yMax; z += _cs)
                clear.Add(new Vector3(x, y, z));

        if (_haze != null) Destroy(_haze.gameObject);
        _haze = MistBank.CreateHorizon(transform, "FarHaze", clear, clear, _cs, _env.farHaze,
                                       _env.farHazeMargin, 104729, null, 0f, _env.farHazeSink);
    }

    // Underside of the lowest thing on the board (blocks, endpoints) — the height
    // fog's top is measured from it, like the level map's.
    float FloorY(GridSystem grid)
    {
        if (grid == null) grid = GridSystem.instance;
        if (grid == null) return 0f;
        int minY = int.MaxValue;
        foreach (var ins in grid.GetAllInstances())
            if (ins != null) foreach (var c in ins.occupiedCells) minY = Mathf.Min(minY, c.y);
        var gfm = GameFlowManager.Instance;
        if (gfm != null)
        {
            foreach (var c in gfm.AllStarts) minY = Mathf.Min(minY, c.y);
            foreach (var c in gfm.AllEnds)   minY = Mathf.Min(minY, c.y);
        }
        if (minY == int.MaxValue) minY = 0;
        return grid.Origin.y + minY * grid.cellSize;
    }

    Vector3 BoardCentre()
    {
        if (_tops.Count > 0)
        {
            Vector3 lo = _tops[0], hi = _tops[0];
            foreach (var p in _tops) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
            return (lo + hi) * 0.5f;
        }
        if (_orbit == null) _orbit = FindFirstObjectByType<OrbitCamera>();
        return _orbit != null ? _orbit.FocusPoint : Vector3.zero;
    }

    Vector3 Focus()
    {
        if (_orbit == null) _orbit = FindFirstObjectByType<OrbitCamera>();
        return _orbit != null ? _orbit.FocusPoint
             : (Camera.main != null ? Camera.main.transform.position : Vector3.zero);
    }

    void OnDestroy()
    {
        if (_sun != null)
        {
            _sun.intensity = _sunIntensity0;
            _sun.color     = _sunColor0;
            _sun.transform.rotation = _sunRot0;
        }
        if (_skyOriginal != null && RenderSettings.skybox == _skyClone) RenderSettings.skybox = _skyOriginal;
        if (_skyClone != null) Destroy(_skyClone);
        if (_env != null && _env.ambience != null && _env.ambience.IsValid()) _env.ambience.Stop(gameObject);
        DestroyParticleMaterials();
        if (_heightFog != null || (_env != null && _env.farHazeEnabled)) MistBank.SetProtected(null, _cs);

        // Neutral for whatever comes next (no AO / wear / water leaking into another scene).
        Shader.SetGlobalFloat(AOStr, 0f);
        Shader.SetGlobalFloat(Wetness, 0f);
        Shader.SetGlobalFloat(Flood, 0f);
        Shader.SetGlobalFloat(RainId, 0f);
        Shader.SetGlobalFloat(Streaks, 0f);
        Shader.SetGlobalFloat(Corrosion, 0f);
        if (Current == _env) Current = null;
        if (Instance == this) Instance = null;
    }
}
