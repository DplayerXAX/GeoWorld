using UnityEngine;

public class TurretBullet : MonoBehaviour
{
    public float hitRadius = 0.4f;

    EnemySurfaceUnit _target;
    float _speed, _life;
    int _damage;

    public void Init(EnemySurfaceUnit target, float speed, int damage, float lifetime)
    {
        _target = target;
        _speed = speed;
        _damage = damage;
        _life = lifetime;
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

        if ((to - transform.position).sqrMagnitude <= hitRadius * hitRadius)
        {
            _target.TakeDamage(_damage);
            Destroy(gameObject);
            return;
        }

        Vector3 dir = to - transform.position;
        if (dir.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(dir);
    }
}
