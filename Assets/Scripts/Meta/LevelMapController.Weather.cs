using System.Collections;
using UnityEngine;

// Rain over the map once the first chapter turns. It starts when the level named in
// mapRainAfterLevelId (1-2) has been cleared: the farm, and the fields the first
// levels stand in, go wet, and the levels from there on are rainy too (their own
// LevelEnvironment does that in play). On the visit right after that clear it comes
// in gradually, once the reveal cutscene and any conversation are over; every visit
// after, it is already raining when the map opens.
//
// The same streak shader as gameplay's weather (GeoWorld/RainStreak), parented to the
// map rather than to a plot, so it doesn't rise or sink with any of them.
public partial class LevelMapController
{
    [Header("Map weather")]
    [Tooltip("Rain over the farmland once mapRainAfterLevelId has been cleared.")]
    public bool mapRain = true;
    [Tooltip("The level whose clear turns the weather (1-2: chapter one is fair until then). Empty = rains from the start.")]
    public string mapRainAfterLevelId = "1-2";
    [Tooltip("Lowest (x, z) column the rain covers.")]
    public Vector2Int mapRainMin = new(-20, -8);
    [Tooltip("Width (x) and depth (z), in columns. Cover the farm and what lies beside it.")]
    public Vector2Int mapRainSize = new(62, 34);
    [Tooltip("Grid layer the drops fall onto.")]
    public int mapRainBaseLayer = 3;
    [Tooltip("Cells above that layer the drops start from.")]
    [Range(3f, 20f)] public float mapRainHeight = 9f;
    [Tooltip("Drops per second per square cell.")]
    [Range(0.1f, 3f)] public float mapRainDensity = 0.9f;
    [Tooltip("Seconds the rain takes to build up on the visit that follows the clear.")]
    [Min(0f)] public float mapRainRampSeconds = 6f;
    public Color mapRainColor = new Color(0.72f, 0.78f, 0.86f, 0.35f);

    static Material _mapRainMat;

    void BuildMapRain()
    {
        if (!mapRain || gridSystem == null) return;
        bool wet = string.IsNullOrEmpty(mapRainAfterLevelId) || previewAllRegions
                || (SaveSystem.Profile.GetRecord(mapRainAfterLevelId)?.cleared ?? false);
        if (!wet) return;

        if (_mapRainMat == null)
        {
            var sh = Shader.Find("GeoWorld/RainStreak");
            if (sh == null) return;
            _mapRainMat = new Material(sh) { name = "MapRain (runtime)", hideFlags = HideFlags.DontSave };
            if (_mapRainMat.HasProperty("_SrcBlend"))
            {
                _mapRainMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _mapRainMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }
        }
        _mapRainMat.SetColor("_Color", mapRainColor);

        float cs = gridSystem.cellSize;
        var lo = gridSystem.GridToWorld(new Vector3Int(mapRainMin.x, mapRainBaseLayer, mapRainMin.y));
        var hi = gridSystem.GridToWorld(new Vector3Int(mapRainMin.x + mapRainSize.x - 1, mapRainBaseLayer, mapRainMin.y + mapRainSize.y - 1));
        var size = new Vector3(hi.x - lo.x + cs, 0.2f, hi.z - lo.z + cs);
        float fall = Mathf.Max(3f, mapRainHeight) * cs;
        const float Speed = 21f;

        float rate = mapRainDensity * (size.x / cs) * (size.z / cs) * GraphicsQuality.Particles;
        if (rate <= 0.5f) return;

        var go = new GameObject("MapRain");
        go.transform.SetParent(transform, false);
        go.transform.position = (lo + hi) * 0.5f + Vector3.up * fall;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        float life = fall / Speed;
        var main = ps.main;
        main.loop            = true;
        main.playOnAwake     = false;
        main.startSpeed      = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime   = life;
        main.startSize       = 0.035f * cs;
        main.maxParticles    = Mathf.Max(100, Mathf.CeilToInt(rate * life * 1.3f));

        var em = ps.emission;
        em.rateOverTime = rate;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale     = size;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space   = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
        vel.y = new ParticleSystem.MinMaxCurve(-Speed * 1.08f, -Speed * 0.92f);
        vel.z = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode        = ParticleSystemRenderMode.Stretch;
        r.velocityScale     = 0.035f;
        r.lengthScale       = 1f;
        r.sharedMaterial    = _mapRainMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows    = false;

        // The visit right after the clear: it comes in as the world turns, after the
        // cutscene and any conversation. Every other visit it is already raining.
        bool comesIn = !string.IsNullOrEmpty(mapRainAfterLevelId) && _growthLevelId == mapRainAfterLevelId
                    && mapRainRampSeconds > 0.01f && !previewAllRegions;
        if (comesIn) StartCoroutine(MapRainIn(ps, rate, mapRainRampSeconds));
        else main.prewarm = true;
        ps.Play();
    }

    IEnumerator MapRainIn(ParticleSystem ps, float full, float seconds)
    {
        var em = ps.emission;
        em.rateOverTime = 0f;
        while (_decorCutscenePlaying || (DialogueRunner.Instance != null && DialogueRunner.Instance.IsPlaying))
            yield return null;
        for (float t = 0f; t < seconds && ps != null; t += Time.deltaTime)
        {
            em.rateOverTime = full * Mathf.SmoothStep(0f, 1f, t / seconds);
            yield return null;
        }
        if (ps != null) em.rateOverTime = full;
    }
}
