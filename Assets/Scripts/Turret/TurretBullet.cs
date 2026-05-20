using UnityEngine;

public class TurretBullet : MonoBehaviour
{
    EnemySurfaceUnit _target;
    float _speed;
    int _damage;
    float _lifeTimer;
    bool _hit;

    public void Init(EnemySurfaceUnit target, float speed, int damage, float lifetime)
    {
        _target = target;
        _speed = Mathf.Max(0.1f, speed);
        _damage = Mathf.Max(1, damage);
        _lifeTimer = Mathf.Max(0.1f, lifetime);
        SetupCollision();
    }

    void Update()
    {
        _lifeTimer -= Time.deltaTime;
        if (_lifeTimer <= 0f || _target == null || _target.CurrentHealth <= 0)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 targetPos = _target.transform.position;
        transform.position = Vector3.MoveTowards(transform.position, targetPos, _speed * Time.deltaTime);

        Vector3 dir = targetPos - transform.position;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
    }

    void OnTriggerEnter(Collider other)
    {
        TryHit(other);
    }

    void OnCollisionEnter(Collision collision)
    {
        TryHit(collision.collider);
    }

    void TryHit(Collider other)
    {
        if (_hit || other == null) return;

        var enemy = other.GetComponentInParent<EnemySurfaceUnit>();
        if (enemy == null || enemy.CurrentHealth <= 0) return;

        _hit = true;
        enemy.TakeDamage(_damage);
        Destroy(gameObject);
    }

    void SetupCollision()
    {
        foreach (var col in GetComponentsInChildren<Collider>())
            col.isTrigger = true;

        var rb = GetComponent<Rigidbody>();
        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody>();

        rb.isKinematic = true;
        rb.useGravity = false;
    }
}
