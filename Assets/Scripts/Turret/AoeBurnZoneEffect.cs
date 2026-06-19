using System.Collections.Generic;
using UnityEngine;

public class AoeBurnZoneEffect : MonoBehaviour
{
    static readonly List<EnemySurfaceUnit> _targets = new();

    static readonly int _BaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int _Color = Shader.PropertyToID("_Color");

    float _radius;
    float _duration;
    float _totalDuration;
    float _tickInterval;
    float _tickTimer;
    int _damagePerTick;
    Transform _visualTransform;
    Renderer _visualRenderer;
    Material _visualMaterial;

    public static void Spawn(Vector3 position, float radius, float duration, int damagePerTick, float tickInterval)
    {
        if (radius <= 0f || duration <= 0f || damagePerTick <= 0) return;

        var go = new GameObject("AOE Burn Zone");
        go.transform.position = position;
        var zone = go.AddComponent<AoeBurnZoneEffect>();
        zone.Init(radius, duration, damagePerTick, tickInterval);
    }

    void Init(float radius, float duration, int damagePerTick, float tickInterval)
    {
        _radius = radius;
        _duration = duration;
        _totalDuration = duration;
        _damagePerTick = damagePerTick;
        _tickInterval = Mathf.Max(0.1f, tickInterval);
        _tickTimer = 0f;

        CreateVisual();
    }

    void Update()
    {
        _duration -= Time.deltaTime;
        if (_duration <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        UpdateVisual();

        _tickTimer -= Time.deltaTime;
        if (_tickTimer > 0f) return;

        _tickTimer = _tickInterval;
        DamageEnemiesInZone();
    }

    void CreateVisual()
    {
        var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visual.name = "Burn Zone Visual";
        visual.transform.SetParent(transform, worldPositionStays: false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localScale = Vector3.one * (_radius * 2f);
        _visualTransform = visual.transform;

        var col = visual.GetComponent<Collider>();
        if (col != null) Destroy(col);

        _visualRenderer = visual.GetComponent<Renderer>();
        if (_visualRenderer == null) return;

        _visualMaterial = BuildTransparentMaterial(new Color(1f, 0.28f, 0.04f, 0.22f));
        _visualRenderer.sharedMaterial = _visualMaterial;
        _visualRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _visualRenderer.receiveShadows = false;
    }

    void UpdateVisual()
    {
        if (_visualMaterial == null) return;

        float life01 = _totalDuration > 0.001f ? Mathf.Clamp01(_duration / _totalDuration) : 0f;
        float pulse = 1f + Mathf.Sin(Time.time * 12f) * 0.035f;
        if (_visualTransform != null)
            _visualTransform.localScale = Vector3.one * (_radius * 2f * pulse);

        Color color = new Color(1f, 0.28f, 0.04f, 0.08f + 0.18f * life01);
        if (_visualMaterial.HasProperty(_BaseColor)) _visualMaterial.SetColor(_BaseColor, color);
        if (_visualMaterial.HasProperty(_Color)) _visualMaterial.SetColor(_Color, color);
    }

    static Material BuildTransparentMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        var material = new Material(shader);
        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_ZWrite", 0f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        if (material.HasProperty("_Cull"))
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        if (material.HasProperty(_BaseColor)) material.SetColor(_BaseColor, color);
        if (material.HasProperty(_Color)) material.SetColor(_Color, color);

        return material;
    }

    void OnDestroy()
    {
        if (_visualMaterial != null)
            Destroy(_visualMaterial);
    }

    void DamageEnemiesInZone()
    {
        var mgr = EnemyBaseManager.Instance;
        var enemies = mgr != null ? mgr.ActiveEnemies : null;
        if (enemies == null) return;

        _targets.Clear();
        float r2 = _radius * _radius;
        Vector3 center = transform.position;

        for (int i = 0; i < enemies.Count; i++)
        {
            var enemy = enemies[i];
            if (enemy != null && enemy.CurrentHealth > 0
                && (enemy.transform.position - center).sqrMagnitude <= r2)
                _targets.Add(enemy);
        }

        for (int i = 0; i < _targets.Count; i++)
            if (_targets[i] != null) _targets[i].TakeDamage(_damagePerTick);
    }
}
