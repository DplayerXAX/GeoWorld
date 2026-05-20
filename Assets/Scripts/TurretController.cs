using UnityEngine;

public class TurretController : MonoBehaviour
{
    const string BulletAssetPath = "Assets/Prefab/Bullet.prefab";

    [Header("Targeting")]
    public float attackRange = 5f;
    public float fireInterval = 0.6f;

    [Header("Bullet")]
    public GameObject bulletPrefab;
    public string bulletResourcePath = "Bullet";
    public float bulletSpeed = 9f;
    public int bulletDamage = 1;
    public float bulletLifetime = 3f;
    public Vector3 muzzleOffset = new Vector3(0f, 0.6f, 0f);

    EnemySurfaceUnit _target;
    float _fireTimer;

    void Awake()
    {
        FindBulletPrefab();
    }

    void Update()
    {
        if (GameFlowManager.Instance == null || GameFlowManager.Instance.phase != GamePhase.Running)
            return;

        _fireTimer -= Time.deltaTime;

        if (!IsTargetValid(_target))
            _target = FindTarget();

        if (_target == null)
            return;

        if (_fireTimer <= 0f)
        {
            FireAt(_target);
            _fireTimer = Mathf.Max(0.05f, fireInterval);
        }
    }

    EnemySurfaceUnit FindTarget()
    {
        EnemySurfaceUnit best = null;
        float bestSqrDist = attackRange * attackRange;
        var enemies = FindObjectsByType<EnemySurfaceUnit>(FindObjectsSortMode.None);

        foreach (var enemy in enemies)
        {
            if (!IsTargetValid(enemy)) continue;

            float sqrDist = (enemy.transform.position - transform.position).sqrMagnitude;
            if (sqrDist <= bestSqrDist)
            {
                best = enemy;
                bestSqrDist = sqrDist;
            }
        }

        return best;
    }

    bool IsTargetValid(EnemySurfaceUnit enemy)
    {
        if (enemy == null || enemy.CurrentHealth <= 0) return false;
        return (enemy.transform.position - transform.position).sqrMagnitude <= attackRange * attackRange;
    }

    void FireAt(EnemySurfaceUnit target)
    {
        if (bulletPrefab == null && FindBulletPrefab() == null)
            return;

        Vector3 spawnPos = transform.position + muzzleOffset;
        Vector3 dir = target.transform.position - spawnPos;
        Quaternion rot = dir.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(dir.normalized, Vector3.up)
            : Quaternion.identity;
        GameObject bullet = Instantiate(bulletPrefab, spawnPos, rot);
        bullet.SetActive(true);

        var projectile = bullet.GetComponent<TurretBullet>();
        if (projectile == null)
            projectile = bullet.AddComponent<TurretBullet>();

        projectile.enabled = true;
        projectile.Init(target, bulletSpeed, bulletDamage, bulletLifetime);
    }

    GameObject FindBulletPrefab()
    {
        if (bulletPrefab != null) return bulletPrefab;

        if (!string.IsNullOrEmpty(bulletResourcePath))
            bulletPrefab = Resources.Load<GameObject>(bulletResourcePath);

#if UNITY_EDITOR
        if (bulletPrefab == null)
            bulletPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(BulletAssetPath);
#endif

        return bulletPrefab;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.15f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
