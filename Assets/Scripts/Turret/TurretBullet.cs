using UnityEngine;

public class TurretBullet : MonoBehaviour
{
    public float hitRadius = 0.4f;
    public float enemyColliderRadius = 0.45f;

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
        _damage = turret.bulletDamage;
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
        transform.position = Vector3.MoveTowards(transform.position, to, _speed * Time.deltaTime);

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
                foreach (var e in FindObjectsOfType<EnemySurfaceUnit>())
                    if (e != null && e.CurrentHealth > 0
                        && (e.transform.position - transform.position).sqrMagnitude <= _turret.aoeRadius * _turret.aoeRadius)
                        e.TakeDamage(_damage);
                break;

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
