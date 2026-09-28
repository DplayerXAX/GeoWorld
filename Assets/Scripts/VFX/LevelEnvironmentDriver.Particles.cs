using System.Collections.Generic;
using UnityEngine;

// The environment's particles: rain, the drops bursting on block tops, and the
// drifting motes (dust, pollen, snow, embers). All simulated in world space and
// emitted round wherever the camera is looking, so what is already in the air
// doesn't swing about when the camera moves.
public partial class LevelEnvironmentDriver
{
    Transform      _rainRoot;
    ParticleSystem _splash;
    float          _splashAcc;
    Transform      _motesRoot;
    Vector3        _motesOffset;
    readonly List<Material> _particleMats = new();

    // ── Rain ─────────────────────────────────────────────────────────────────
    void BuildRain()
    {
        var mat = ParticleMaterial("GeoWorld/RainStreak", "Rain", _env.rainColor, false);
        if (mat == null) return;

        _rainRoot = new GameObject("Rain").transform;
        _rainRoot.SetParent(transform, false);
        var ps = NewSystem(_rainRoot);

        var main = ps.main;
        main.startLifetime   = 1.1f;
        main.startSize       = 0.035f;
        main.maxParticles    = 6000;
        main.startColor      = new ParticleSystem.MinMaxGradient(new Color(1, 1, 1, 0.6f), Color.white);

        var em = ps.emission;
        em.rateOverTime = 1400f * _env.rainIntensity;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale     = new Vector3(34f, 0.5f, 34f);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space   = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(_env.rainWind.x * 0.8f, _env.rainWind.x * 1.2f);
        vel.y = new ParticleSystem.MinMaxCurve(-24f, -19f);
        vel.z = new ParticleSystem.MinMaxCurve(_env.rainWind.y * 0.8f, _env.rainWind.y * 1.2f);

        var r = _rainRoot.GetComponent<ParticleSystemRenderer>();
        r.renderMode    = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.035f;
        r.lengthScale   = 1f;
        r.sharedMaterial = mat;

        _rainRoot.position = Focus() + Vector3.up * 16f;
        ps.Play();
    }

    // ── Splashes ─────────────────────────────────────────────────────────────
    // A few droplets thrown up where a drop hits a block top open to the sky —
    // emitted by hand, since only the board knows where those tops are.
    void BuildSplashes()
    {
        var c = _env.rainColor; c.a = Mathf.Clamp01(c.a * 2.2f);
        var mat = ParticleMaterial("GeoWorld/SoftDot", "Splash", c, false);
        if (mat == null) return;

        var go = new GameObject("Splashes");
        go.transform.SetParent(transform, false);
        _splash = NewSystem(go.transform);

        var main = _splash.main;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.22f, 0.4f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
        main.gravityModifier = 1.4f;
        main.maxParticles    = 2000;

        var em = _splash.emission;
        em.enabled = false;
        var shape = _splash.shape;
        shape.enabled = false;

        var col = _splash.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(Fade(0f, 0.7f));

        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        _splash.Play();
    }

    void UpdateSplashes()
    {
        if (_splash == null || _tops.Count == 0) return;

        float rate = Mathf.Min(_tops.Count * 1.5f, 90f) * _env.rainIntensity;
        _splashAcc += rate * Time.deltaTime;
        var ep = new ParticleSystem.EmitParams();
        while (_splashAcc >= 1f)
        {
            _splashAcc -= 1f;
            var top = _tops[Random.Range(0, _tops.Count)];
            var at  = top + new Vector3(Random.Range(-0.45f, 0.45f) * _cs, 0.02f, Random.Range(-0.45f, 0.45f) * _cs);
            int n = Random.Range(2, 5);
            for (int i = 0; i < n; i++)
            {
                ep.position = at;
                ep.velocity = new Vector3(Random.Range(-0.7f, 0.7f), Random.Range(1f, 2.2f), Random.Range(-0.7f, 0.7f));
                _splash.Emit(ep, 1);
            }
        }
    }

    // ── Motes ────────────────────────────────────────────────────────────────
    void BuildMotes()
    {
        Color  col;
        Vector2 size, life, vx, vy, vz;
        float  rate, noise, boxY;
        bool   additive = false;
        _motesOffset = Vector3.up * 3f;

        switch (_env.motes)
        {
            case LevelEnvironment.Motes.Pollen:
                col = new Color(1f, 0.92f, 0.45f, 0.7f); size = new(0.04f, 0.08f); life = new(7f, 12f);
                vx = new(-0.1f, 0.1f); vy = new(0.02f, 0.12f); vz = new(-0.1f, 0.1f);
                rate = 18f; noise = 0.3f; boxY = 14f;
                break;
            case LevelEnvironment.Motes.Snow:
                col = new Color(1f, 1f, 1f, 0.85f); size = new(0.06f, 0.14f); life = new(8f, 11f);
                vx = new Vector2(-0.2f, 0.2f) + Vector2.one * _env.rainWind.x * 0.4f;
                vy = new(-1.2f, -0.7f);
                vz = new Vector2(-0.2f, 0.2f) + Vector2.one * _env.rainWind.y * 0.4f;
                rate = 120f; noise = 0.4f; boxY = 3f;
                _motesOffset = Vector3.up * 10f;
                break;
            case LevelEnvironment.Motes.Embers:
                col = new Color(1f, 0.55f, 0.2f, 1f); size = new(0.03f, 0.06f); life = new(3f, 6f);
                vx = new(-0.15f, 0.15f); vy = new(0.4f, 1.2f); vz = new(-0.15f, 0.15f);
                rate = 30f; noise = 0.6f; boxY = 6f; additive = true;
                _motesOffset = Vector3.down * 2f;
                break;
            default:   // Dust
                col = new Color(1f, 0.95f, 0.85f, 0.35f); size = new(0.03f, 0.07f); life = new(8f, 14f);
                vx = new(-0.08f, 0.08f); vy = new(-0.03f, 0.05f); vz = new(-0.08f, 0.08f);
                rate = 25f; noise = 0.15f; boxY = 14f;
                break;
        }

        var mat = ParticleMaterial("GeoWorld/SoftDot", "Motes", col * _env.moteTint, additive);
        if (mat == null) return;

        _motesRoot = new GameObject("Motes").transform;
        _motesRoot.SetParent(transform, false);
        var ps = NewSystem(_motesRoot);

        var main = ps.main;
        main.prewarm       = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        main.startSize     = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.maxParticles  = 3000;

        var em = ps.emission;
        em.rateOverTime = rate * _env.moteDensity;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale     = new Vector3(40f, boxY, 40f);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space   = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(vx.x, vx.y);
        vel.y = new ParticleSystem.MinMaxCurve(vy.x, vy.y);
        vel.z = new ParticleSystem.MinMaxCurve(vz.x, vz.y);

        var n = ps.noise;
        n.enabled   = noise > 0f;
        n.strength  = noise;
        n.frequency = 0.25f;
        n.scrollSpeed = 0.2f;

        var c = ps.colorOverLifetime;
        c.enabled = true;
        c.color = new ParticleSystem.MinMaxGradient(Fade(0.15f, 0.8f));

        _motesRoot.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        _motesRoot.position = Focus() + _motesOffset;
        ps.Play();
    }

    void FollowCamera()
    {
        if (_rainRoot == null && _motesRoot == null) return;
        var focus = Focus();
        if (_rainRoot  != null) _rainRoot.position  = focus + Vector3.up * 16f;
        if (_motesRoot != null) _motesRoot.position = focus + _motesOffset;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    static ParticleSystem NewSystem(Transform root)
    {
        var ps = root.gameObject.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop            = true;
        main.playOnAwake     = false;
        main.startSpeed      = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var r = root.GetComponent<ParticleSystemRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows    = false;
        return ps;
    }

    Material ParticleMaterial(string shader, string name, Color color, bool additive)
    {
        var sh = Shader.Find(shader);
        if (sh == null) { Debug.LogWarning($"[Environment] {shader} shader not found — no {name.ToLower()}."); return null; }
        var mat = new Material(sh) { name = name + " (runtime)" };
        mat.SetColor("_Color", color);
        if (mat.HasProperty("_SrcBlend"))
        {
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One
                                                       : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        }
        _particleMats.Add(mat);
        return mat;
    }

    // White, fading in over the first `inEnd` of the life and out after `outStart`.
    static Gradient Fade(float inEnd, float outStart)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  inEnd > 0f
                      ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, inEnd),
                                new GradientAlphaKey(1f, outStart), new GradientAlphaKey(0f, 1f) }
                      : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, outStart),
                                new GradientAlphaKey(0f, 1f) });
        return g;
    }

    void DestroyParticleMaterials()
    {
        foreach (var m in _particleMats) if (m != null) Destroy(m);
        _particleMats.Clear();
    }
}
