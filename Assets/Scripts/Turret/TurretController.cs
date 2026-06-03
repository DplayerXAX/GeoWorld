using UnityEngine;

public class TurretController : MonoBehaviour
{
    const string BulletAssetPath = "Assets/Prefab/Bullet.prefab";
    const float MinFireInterval = 0.05f;

    public enum Mode { Basic, Slow, Aoe }

    [Header("Turret")]
    public Mode mode = Mode.Basic;

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

    [Header("Effects")]
    public float slowDuration = 2.5f;
    public float slowMultiplier = 0.1f;
    public float aoeRadius = 1.75f;

    EnemySurfaceUnit _target;
    float _fireTimer;

    public void AddAttackSpeed(float percent)
    {
        if (percent <= 0f) return;

        fireInterval = Mathf.Max(MinFireInterval, fireInterval / (1f + percent));
        _fireTimer = Mathf.Min(_fireTimer, fireInterval);
    }

    public void AddDamage(int amount)
    {
        if (amount <= 0) return;

        bulletDamage += amount;
    }

    public void AddRange(float amount)
    {
        if (amount <= 0f) return;

        attackRange += amount;
    }

    public void Configure(BlockType type)
    {
        if (!TurretTypes.Is(type)) return;

        mode = TurretTypes.Mode(type);
        ApplyModeColor();
    }

    void Awake()
    {
        ResolveBulletPrefab();
        ApplyModeColor();
    }

    void Start() => ApplyModeColor();

    void Update()
    {
        var flow = GameFlowManager.Instance;
        if (flow == null || flow.phase != GamePhase.Running) return;

        _fireTimer -= Time.deltaTime;

        _target = FindClosest();
        if (_target == null || _fireTimer > 0f) return;

        Fire(_target);
        _fireTimer = fireInterval;
    }

    bool InRange(EnemySurfaceUnit e)
    {
        return e != null && e.CurrentHealth > 0
            && (e.transform.position - Origin).sqrMagnitude <= attackRange * attackRange;
    }

    EnemySurfaceUnit FindClosest()
    {
        EnemySurfaceUnit best = null;
        float bestSqr = attackRange * attackRange;
        int bestPriority = int.MinValue;

        foreach (var e in FindObjectsOfType<EnemySurfaceUnit>())
        {
            if (e == null || e.CurrentHealth <= 0) continue;

            float sqr = (e.transform.position - Origin).sqrMagnitude;
            if (sqr > attackRange * attackRange) continue;

            int priority = e.targetPriority;
            if (priority > bestPriority || (priority == bestPriority && sqr <= bestSqr))
            {
                best = e;
                bestSqr = sqr;
                bestPriority = priority;
            }
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

        Vector3 spawn = Origin + muzzleOffset;
        Vector3 dir = target.transform.position - spawn;
        Quaternion rot = dir.sqrMagnitude > 0.001f ? Quaternion.LookRotation(dir) : Quaternion.identity;

        var bullet = Instantiate(bulletPrefab, spawn, rot);
        bullet.SetActive(true);

        if (!bullet.TryGetComponent(out TurretBullet projectile))
            projectile = bullet.AddComponent<TurretBullet>();
        projectile.enabled = true;
        projectile.Init(target, this);
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

    void ApplyModeColor()
    {
        Transform root = transform.parent != null ? transform.parent : transform;
        Color color = TurretTypes.DisplayColor(mode);

        foreach (var r in root.GetComponentsInChildren<Renderer>())
            r.material.color = color;
    }

    Vector3 Origin => transform.parent != null ? transform.parent.position : transform.position;

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.15f, 0.35f);
        Gizmos.DrawWireSphere(Origin, attackRange);
    }
}
