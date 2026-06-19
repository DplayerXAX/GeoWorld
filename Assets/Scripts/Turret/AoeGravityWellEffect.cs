using System.Collections.Generic;
using UnityEngine;

public class AoeGravityWellEffect : MonoBehaviour
{
    static readonly List<EnemySurfaceUnit> _targets = new();

    float _radius;
    float _duration;
    float _pullSpeed;
    int _finalDamage;
    bool _finalDamageApplied;

    public static void Spawn(Vector3 position, float radius, float duration, float pullSpeed, int finalDamage)
    {
        if (radius <= 0f || duration <= 0f || pullSpeed <= 0f) return;

        var go = new GameObject("AOE Gravity Well");
        go.transform.position = position;
        var well = go.AddComponent<AoeGravityWellEffect>();
        well.Init(radius, duration, pullSpeed, finalDamage);
    }

    void Init(float radius, float duration, float pullSpeed, int finalDamage)
    {
        _radius = radius;
        _duration = duration;
        _pullSpeed = pullSpeed;
        _finalDamage = Mathf.Max(0, finalDamage);
    }

    void Update()
    {
        _duration -= Time.deltaTime;

        PullEnemies();

        if (_duration > 0f) return;

        ApplyFinalDamage();
        Destroy(gameObject);
    }

    void PullEnemies()
    {
        var mgr = EnemyBaseManager.Instance;
        var enemies = mgr != null ? mgr.ActiveEnemies : null;
        if (enemies == null) return;

        float r2 = _radius * _radius;
        Vector3 center = transform.position;

        for (int i = 0; i < enemies.Count; i++)
        {
            var enemy = enemies[i];
            if (enemy == null || enemy.CurrentHealth <= 0) continue;

            Vector3 toCenter = center - enemy.transform.position;
            float sqr = toCenter.sqrMagnitude;
            if (sqr <= 0.001f || sqr > r2) continue;

            float distance01 = Mathf.Clamp01(Mathf.Sqrt(sqr) / _radius);
            float strength = 1f - distance01;
            enemy.AddExternalDisplacement(toCenter.normalized * (_pullSpeed * strength * Time.deltaTime));
        }
    }

    void ApplyFinalDamage()
    {
        if (_finalDamageApplied || _finalDamage <= 0) return;
        _finalDamageApplied = true;

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
            if (_targets[i] != null) _targets[i].TakeDamage(_finalDamage);
    }
}
