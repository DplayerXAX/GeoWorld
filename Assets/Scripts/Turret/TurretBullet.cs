using System.Collections.Generic;
using UnityEngine;

public class TurretBullet : MonoBehaviour
{
    public float hitRadius = 0.4f;
    public float enemyColliderRadius = 0.45f;

    // Reused across AOE explosions so a blast allocates nothing.
    static readonly List<EnemySurfaceUnit> _aoeBuffer = new();

    EnemySurfaceUnit _target;
    TurretController _turret;
    float _speed;
    float _life;
    int _damage;
    bool _hit;

    public void Init(EnemySurfaceUnit target, TurretController turret)
    {
        _target = target;
        _turret = turret;
        _speed = turret.bulletSpeed;
        _damage = turret.EffectiveBulletDamage;   // base × reversible shrine-aura buff (base untouched)
        _life = turret.bulletLifetime;

        SetupBulletCollider();
        EnsureEnemyCollider(target);
    }

    void Update()
    {
        _life -= Time.deltaTime;
        if (_life <= 0f || _target == null || _target.CurrentHealth <= 0)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 to = _target.transform.position;
        Vector3 next = Vector3.MoveTowards(transform.position, to, _speed * Time.deltaTime);
        if (_turret != null && _turret.IsShotBlocked(transform.position, next, transform))
        {
            Destroy(gameObject);
            return;
        }

        transform.position = next;

        Vector3 dir = to - transform.position;
        if (dir.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(dir);

        CheckOverlapHit();
    }

    void OnTriggerEnter(Collider other) => TryHit(other);
    void OnCollisionEnter(Collision collision) => TryHit(collision.collider);

    void CheckOverlapHit()
    {
        foreach (var col in Physics.OverlapSphere(transform.position, hitRadius, ~0, QueryTriggerInteraction.Collide))
        {
            TryHit(col);
            if (_hit) return;
        }
    }

    void TryHit(Collider other)
    {
        if (_hit || other == null) return;
        var enemy = other.GetComponentInParent<EnemySurfaceUnit>();
        if (enemy == null || enemy.CurrentHealth <= 0) return;

        _hit = true;
        ApplyEffect(enemy);
        Destroy(gameObject);
    }

    void ApplyEffect(EnemySurfaceUnit enemy)
    {
        if (_turret == null)
        {
            enemy.TakeDamage(_damage);
            return;
        }

        switch (_turret.mode)
        {
            case TurretController.Mode.Slow:
                enemy.TakeDamage(_damage);
                EnemySlowEffect.Apply(enemy, _turret.slowDuration, _turret.slowMultiplier);
                break;

            case TurretController.Mode.Aoe:
            {
                Vector3 blastCenter = transform.position;
                var mgr = EnemyBaseManager.Instance;
                if (mgr != null)
                {
                    // Two-pass: gather in-radius enemies into a reusable buffer,
                    // THEN damage — TakeDamage can remove from the live list mid-loop.
                    _aoeBuffer.Clear();
                    var enemies = mgr.ActiveEnemies;
                    float r2 = _turret.aoeRadius * _turret.aoeRadius;
                    for (int i = 0; i < enemies.Count; i++)
                    {
                        var e = enemies[i];
                        if (e != null && e.CurrentHealth > 0
                            && (e.transform.position - blastCenter).sqrMagnitude <= r2)
                            _aoeBuffer.Add(e);
                    }
                    for (int i = 0; i < _aoeBuffer.Count; i++)
                        if (_aoeBuffer[i] != null) _aoeBuffer[i].TakeDamage(_damage);
                }

                if (_turret.AoeBurnGroundEnabled)
                {
                    AoeBurnZoneEffect.Spawn(
                        blastCenter,
                        _turret.aoeRadius,
                        _turret.AoeBurnGroundDuration,
                        _turret.AoeBurnDamagePerTick,
                        _turret.AoeBurnTickInterval);
                }

                if (_turret.AoeGravityWellEnabled)
                {
                    AoeGravityWellEffect.Spawn(
                        blastCenter,
                        _turret.AoeGravityRadius,
                        _turret.AoeGravityDuration,
                        _turret.AoeGravityPullSpeed,
                        _turret.AoeGravityFinalDamage);
                }
                break;
            }

            default:
                enemy.TakeDamage(_damage);
                break;
        }
    }

    void SetupBulletCollider()
    {
        var sphere = GetComponent<SphereCollider>();
        if (sphere == null) sphere = gameObject.AddComponent<SphereCollider>();
        sphere.radius = hitRadius;
        sphere.isTrigger = true;

        var rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    void EnsureEnemyCollider(EnemySurfaceUnit enemy)
    {
        if (enemy == null || enemy.GetComponentInChildren<Collider>() != null) return;

        var col = enemy.gameObject.AddComponent<SphereCollider>();
        col.radius = enemyColliderRadius;
        col.isTrigger = true;
    }
}
