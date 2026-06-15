using UnityEngine;

public enum BasicTurretUpgradePath { Power, Burst }

public class TurretController : MonoBehaviour
{
    const string BulletAssetPath = "Assets/Prefab/Bullet.prefab";
    const float MinFireInterval = 0.05f;
    const float BasicUpgradePercent = 0.33f;

    public enum Mode { Basic, Slow, Aoe }

    [Header("Balance")]
    [Tooltip("Central balance asset. When set, Configure(BlockType) overrides damage / range / fireRate / slow / AOE stats from BalanceTable.GetTurretStats(mode). Leave empty to use the Inspector defaults below.")]
    public BalanceTable balance;

    [Header("Turret")]
    public Mode mode = Mode.Basic;

    [Header("Targeting")]
    public float attackRange = 3.5f;
    public float fireInterval = 1f;
    public float lineOfSightPadding = 0.05f;

    [Header("Bullet")]
    public GameObject bulletPrefab;
    public string bulletResourcePath = "Bullet";
    public float bulletSpeed = 9f;
    public int bulletDamage = 1;
    public float bulletLifetime = 3f;
    public Vector3 muzzleOffset = new Vector3(0f, 0.6f, 0f);
    [Min(1)] public int projectilesPerShot = 1;

    [Header("Effects")]
    public float slowDuration = 2.5f;
    public float slowMultiplier = 0.1f;
    public float aoeRadius = 1.75f;

    EnemySurfaceUnit _target;
    float _fireTimer;
    float _synergyFireRateMult = 1f;   // reversible synergy attack-speed buff (1 = none)

    [SerializeField, Range(0, 3)] int _powerPathLevel;
    [SerializeField, Range(0, 3)] int _burstPathLevel;

    public int PowerPathLevel => _powerPathLevel;
    public int BurstPathLevel => _burstPathLevel;

    // Reversible fire-rate multiplier from synergies (e.g. Harmony turrets-on-
    // the-synergy buff). >1 = faster. Set back to 1 to remove. Kept separate from
    // the PERMANENT AddAttackSpeed path so a synergy can cleanly grant/revoke.
    public void SetSynergyFireRateMultiplier(float multiplier)
    {
        _synergyFireRateMult = Mathf.Max(0.01f, multiplier);
    }

    public void AddAttackSpeed(float percent)
    {
        if (percent <= 0f) return;

        fireInterval = Mathf.Max(MinFireInterval, fireInterval / (1f + percent));
        _fireTimer = Mathf.Min(_fireTimer, fireInterval);
    }

    public void AddDamagePercent(float percent)
    {
        if (percent <= 0f) return;

        bulletDamage = Mathf.Max(bulletDamage + 1, Mathf.CeilToInt(bulletDamage * (1f + percent)));
    }

    public void AddRangePercent(float percent)
    {
        if (percent <= 0f) return;

        attackRange *= 1f + percent;
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

        // Pull authoritative per-mode stats from BalanceTable when wired.
        // Inspector defaults stay as fallback for un-wired turrets.
        if (balance != null)
        {
            var s = balance.GetTurretStats(mode);
            attackRange  = s.range;
            fireInterval = s.fireRate > 0f ? Mathf.Max(MinFireInterval, 1f / s.fireRate) : fireInterval;
            bulletDamage = Mathf.Max(1, Mathf.RoundToInt(s.damage));
            projectilesPerShot = 1;
            if (mode == Mode.Slow)
            {
                slowDuration   = s.slowDuration;
                slowMultiplier = s.slowFactor;
            }
            else if (mode == Mode.Aoe)
            {
                aoeRadius = s.aoeRadius;
            }
        }

        ApplyModeColor();
    }

    public bool CanUpgradeBasicPath(BasicTurretUpgradePath path, out string reason)
    {
        reason = null;
        if (mode != Mode.Basic)
        {
            reason = "Only Basic Turret can use these upgrades.";
            return false;
        }

        int level = GetBasicPathLevel(path);
        if (level >= 3)
        {
            reason = "Max level.";
            return false;
        }

        int otherLevel = path == BasicTurretUpgradePath.Power ? _burstPathLevel : _powerPathLevel;
        if (level == 2 && otherLevel >= 3)
        {
            reason = "Only one path can reach level 3.";
            return false;
        }

        return true;
    }

    public bool TryUpgradeBasicPath(BasicTurretUpgradePath path)
    {
        if (!CanUpgradeBasicPath(path, out _)) return false;

        int nextLevel = GetBasicPathLevel(path) + 1;
        if (path == BasicTurretUpgradePath.Power) _powerPathLevel = nextLevel;
        else                                      _burstPathLevel = nextLevel;

        ApplyBasicUpgrade(path, nextLevel);
        return true;
    }

    public void SetBasicUpgradeLevels(int powerLevel, int burstLevel)
    {
        _powerPathLevel = 0;
        _burstPathLevel = 0;
        if (mode != Mode.Basic) return;

        projectilesPerShot = 1;

        powerLevel = Mathf.Clamp(powerLevel, 0, 3);
        burstLevel = Mathf.Clamp(burstLevel, 0, 3);
        if (powerLevel == 3 && burstLevel == 3)
            burstLevel = 2;

        for (int i = 0; i < powerLevel; i++)
            TryUpgradeBasicPath(BasicTurretUpgradePath.Power);
        for (int i = 0; i < burstLevel; i++)
            TryUpgradeBasicPath(BasicTurretUpgradePath.Burst);
    }

    public int GetBasicPathLevel(BasicTurretUpgradePath path) =>
        path == BasicTurretUpgradePath.Power ? _powerPathLevel : _burstPathLevel;

    void ApplyBasicUpgrade(BasicTurretUpgradePath path, int level)
    {
        if (path == BasicTurretUpgradePath.Power)
        {
            if (level == 1 || level == 3) AddDamagePercent(BasicUpgradePercent);
            else if (level == 2)         AddRangePercent(BasicUpgradePercent);
            return;
        }

        if (level == 1)      AddAttackSpeed(BasicUpgradePercent);
        else if (level == 2) projectilesPerShot = Mathf.Max(projectilesPerShot, 2);
        else if (level == 3) projectilesPerShot = Mathf.Max(projectilesPerShot, 3);
    }

    public string NextBasicUpgradeDescription(BasicTurretUpgradePath path)
    {
        int next = GetBasicPathLevel(path) + 1;
        if (next > 3) return "Max level";

        if (path == BasicTurretUpgradePath.Power)
        {
            return next switch
            {
                1 => "Damage +33%",
                2 => "Range +33%",
                3 => "Damage +33%",
                _ => "Max level",
            };
        }

        return next switch
        {
            1 => "Speed +33%",
            2 => "Shoot 2 bullets",
            3 => "Shoot 3 bullets",
            _ => "Max level",
        };
    }

    public bool CanShoot(EnemySurfaceUnit target)
    {
        return InRange(target) && !IsShotBlocked(MuzzlePosition, target.transform.position);
    }

    public bool IsShotBlocked(Vector3 from, Vector3 to, Transform extraIgnoreRoot = null)
    {
        Vector3 dir = to - from;
        float distance = dir.magnitude;
        if (distance <= 0.001f) return false;

        return TryGetBlockingHit(from, dir / distance, distance, out float hd, extraIgnoreRoot)
            && hd < distance - lineOfSightPadding;
    }

    // Nearest PLACED-BLOCK hit along a ray, using the SAME filtering as the
    // line-of-sight check (ignores the turret's own block, enemies, and the
    // optional extra root). Returns false if nothing blocks within maxDistance.
    // Shared by IsShotBlocked and the range-shadow indicator so the visual
    // matches the actual targeting judgment exactly.
    static readonly RaycastHit[] _losBuffer = new RaycastHit[32];

    // Cached PlacedBlock layer mask. 0 = layer not set up → use the fallback path.
    static int _blockMask = int.MinValue;
    static int BlockMask()
    {
        if (_blockMask == int.MinValue)
        {
            int layer = GridSystem.BlockLayer;
            _blockMask = layer >= 0 ? (1 << layer) : 0;
        }
        return _blockMask;
    }

    public bool TryGetBlockingHit(Vector3 from, Vector3 dir, float maxDistance,
                                  out float hitDistance, Transform extraIgnoreRoot = null)
    {
        hitDistance = maxDistance;
        bool found  = false;
        int  mask   = BlockMask();

        if (mask != 0)
        {
            // Fast path: raycast ONLY the PlacedBlock layer, so every hit IS a
            // block — no IsPlacedBlockHit scan. Just skip the firing turret's own.
            int n = Physics.RaycastNonAlloc(from, dir, _losBuffer, maxDistance, mask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var hit = _losBuffer[i];
                if (IsOwnOrIgnored(hit.collider, extraIgnoreRoot)) continue;
                if (hit.distance < hitDistance) { hitDistance = hit.distance; found = true; }
            }
            return found;
        }

        // Fallback (PlacedBlock layer not defined): old all-layers + per-hit filter.
        int m = Physics.RaycastNonAlloc(from, dir, _losBuffer, maxDistance, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < m; i++)
        {
            var hit = _losBuffer[i];
            if (IsIgnoredHit(hit.collider, extraIgnoreRoot)) continue;
            if (!IsPlacedBlockHit(hit.collider)) continue;
            if (hit.distance < hitDistance) { hitDistance = hit.distance; found = true; }
        }
        return found;
    }

    // Lean filter for the layer-masked fast path: hits are already known to be
    // blocks, so only exclude the firing turret's own block (and an extra root).
    bool IsOwnOrIgnored(Collider col, Transform extraIgnoreRoot)
    {
        if (col == null) return true;
        if (extraIgnoreRoot != null
            && (col.transform == extraIgnoreRoot || col.transform.IsChildOf(extraIgnoreRoot)))
            return true;
        Transform root = transform.parent != null ? transform.parent : transform;
        return col.transform == root || col.transform.IsChildOf(root);
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
        if (_fireTimer > 0f) return;   // not ready — skip the (expensive) target search entirely

        _target = FindClosest();
        if (_target == null) return;

        Fire(_target);
        _fireTimer = fireInterval / _synergyFireRateMult;
    }

    bool InRange(EnemySurfaceUnit e)
    {
        return e != null && e.CurrentHealth > 0
            && (e.transform.position - Origin).sqrMagnitude <= attackRange * attackRange;
    }

    EnemySurfaceUnit FindClosest()
    {
        // Use the wave manager's cached live list instead of scanning the whole
        // scene with FindObjectsOfType every call.
        var mgr     = EnemyBaseManager.Instance;
        var enemies = mgr != null ? mgr.ActiveEnemies : null;
        if (enemies == null) return null;

        EnemySurfaceUnit best = null;
        float bestSqr = attackRange * attackRange;
        int bestPriority = int.MinValue;

        for (int i = 0; i < enemies.Count; i++)
        {
            var e = enemies[i];
            if (e == null || e.CurrentHealth <= 0) continue;

            if (!CanShoot(e)) continue;   // InRange short-circuits before the LOS raycast

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

        Vector3 spawn = MuzzlePosition;
        Vector3 dir = target.transform.position - spawn;
        Quaternion rot = dir.sqrMagnitude > 0.001f ? Quaternion.LookRotation(dir) : Quaternion.identity;

        int count = Mathf.Max(1, projectilesPerShot);
        Vector3 right = Vector3.Cross(Vector3.up, dir.normalized);
        if (right.sqrMagnitude < 0.001f) right = transform.right;
        right.Normalize();

        for (int i = 0; i < count; i++)
        {
            float centered = i - (count - 1) * 0.5f;
            SpawnBullet(target, spawn + right * centered * 0.18f, rot);
        }
    }

    void SpawnBullet(EnemySurfaceUnit target, Vector3 spawn, Quaternion rot)
    {
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

        // MaterialPropertyBlock instead of r.material.color — the latter clones a
        // unique material per turret renderer (extra allocs, no GPU instancing).
        foreach (var r in root.GetComponentsInChildren<Renderer>())
            MpbColor.Set(r, color);
    }

    Vector3 Origin => transform.parent != null ? transform.parent.position : transform.position;
    Vector3 MuzzlePosition => Origin + muzzleOffset;

    // World position bullets originate from — also the apex of the range / shadow
    // indicator so the visual lines up with where line-of-sight is actually cast.
    public Vector3 MuzzleWorldPosition => MuzzlePosition;

    bool IsIgnoredHit(Collider col, Transform extraIgnoreRoot)
    {
        if (col == null) return true;
        if (col.GetComponentInParent<EnemySurfaceUnit>() != null) return true;
        if (extraIgnoreRoot != null
            && (col.transform == extraIgnoreRoot || col.transform.IsChildOf(extraIgnoreRoot)))
            return true;

        Transform root = transform.parent != null ? transform.parent : transform;
        return col.transform == root || col.transform.IsChildOf(root);
    }

    bool IsPlacedBlockHit(Collider col)
    {
        var grid = GridSystem.instance;
        if (grid == null) return true;

        foreach (var ins in grid.GetAllInstances())
        {
            Transform placed = ins?.visualObject != null ? ins.visualObject.transform : null;
            if (placed != null && (col.transform == placed || col.transform.IsChildOf(placed)))
                return true;
        }

        return false;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.15f, 0.35f);
        Gizmos.DrawWireSphere(Origin, attackRange);
    }
}
