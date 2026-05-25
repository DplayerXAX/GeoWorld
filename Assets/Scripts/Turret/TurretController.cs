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

    void Awake() => ResolveBulletPrefab();

    void Update()
    {
        var flow = GameFlowManager.Instance;
        if (flow == null || flow.phase != GamePhase.Running) return;

        _fireTimer -= Time.deltaTime;

        if (!InRange(_target)) _target = FindClosest();
        if (_target == null || _fireTimer > 0f) return;

        Fire(_target);
        _fireTimer = fireInterval;
    }

    bool InRange(EnemySurfaceUnit e)
    {
        return e != null && e.CurrentHealth > 0
            && (e.transform.position - transform.position).sqrMagnitude <= attackRange * attackRange;
    }

    EnemySurfaceUnit FindClosest()
    {
        EnemySurfaceUnit best = null;
        float bestSqr = attackRange * attackRange;

        foreach (var e in FindObjectsByType<EnemySurfaceUnit>(FindObjectsSortMode.None))
        {
            if (e == null || e.CurrentHealth <= 0) continue;
            float sqr = (e.transform.position - transform.position).sqrMagnitude;
            if (sqr <= bestSqr) { best = e; bestSqr = sqr; }
        }
        return best;
    }

    void Fire(EnemySurfaceUnit target)
    {
        if (ResolveBulletPrefab() == null)
        {
            Debug.LogError("[TurretController] No bullet prefab found. Put Bullet.prefab in a Resources folder, or assign bulletPrefab.", this);
            return;
        }

        Vector3 spawn = transform.position + muzzleOffset;
        Vector3 dir = target.transform.position - spawn;
        Quaternion rot = dir.sqrMagnitude > 0.001f ? Quaternion.LookRotation(dir) : Quaternion.identity;

        var bullet = Instantiate(bulletPrefab, spawn, rot);
        bullet.SetActive(true);

        if (!bullet.TryGetComponent(out TurretBullet projectile))
            projectile = bullet.AddComponent<TurretBullet>();
        projectile.enabled = true;
        projectile.Init(target, bulletSpeed, bulletDamage, bulletLifetime);
    }

    GameObject ResolveBulletPrefab()
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
