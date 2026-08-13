using UnityEngine;

public enum BasicTurretUpgradePath { Power, Burst }
public enum AoeTurretUpgradePath { Fire, Gravity }

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
    [Tooltip("Uniform scale applied to the spawned bullet instance (the prefab asset itself is left untouched).")]
    public float bulletScale = 1f;
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
    float _debuffFireRateMult  = 1f;   // reversible enemy attack-speed debuff (1 = none)
    float _shrineFireRateMult  = 1f;   // reversible Enlightenment-shrine aura buff (1 = none)
    float _shrineDamageMult    = 1f;   // reversible Enlightenment-shrine aura buff (1 = none)

    [SerializeField, Range(0, 3)] int _powerPathLevel;
    [SerializeField, Range(0, 3)] int _burstPathLevel;
    [SerializeField, Range(0, 3)] int _aoeFirePathLevel;
    [SerializeField, Range(0, 3)] int _aoeGravityPathLevel;

    public int PowerPathLevel => _powerPathLevel;
    public int BurstPathLevel => _burstPathLevel;
    public int AoeFirePathLevel => _aoeFirePathLevel;
    public int AoeGravityPathLevel => _aoeGravityPathLevel;

    // Upgrade actions spent so far, across ALL paths/modes. Turrets start with
    // zero allowance (see TowerUpgradeGate) — an active Enlightenment cube is
    // the only source of AllowedUpgrades, so this stays capped at whatever the
    // biggest currently-active cube grants (1 / 2 / 3 for tier 1/2/3).
    public int TotalUpgradesUsed =>
        _powerPathLevel + _burstPathLevel + _aoeFirePathLevel + _aoeGravityPathLevel;

    // True whenever the turret's own upgrade actions have caught up with (or
    // exceed) the currently-active Enlightenment allowance — the SAME check
    // CanUpgradeBasicPath/CanUpgradeAoePath run first, exposed here as its own
    // named condition so the UI can distinguish "blocked by Enlightenment" from
    // "blocked because this specific path is maxed" without string-matching a
    // `reason` message.
    public bool BlockedByEnlightenmentGate => TotalUpgradesUsed >= TowerUpgradeGate.AllowedUpgrades;

    public bool AoeBurnGroundEnabled => mode == Mode.Aoe && _aoeFirePathLevel >= 1;
    // Level 1's burn was lingering nearly as long as level 2's upgraded one for free —
    // nerfed the base duration down from 2s so the level-2 upgrade (3s) reads as a
    // real step up rather than a marginal one.
    public float AoeBurnGroundDuration => _aoeFirePathLevel >= 2 ? 3f : 1.4f;
    public float AoeBurnTickInterval => 0.75f;
    public int AoeBurnDamagePerTick => Mathf.Max(1, Mathf.CeilToInt(bulletDamage * (_aoeFirePathLevel >= 3 ? 0.67f : 0.34f)));

    public bool AoeGravityWellEnabled => mode == Mode.Aoe && _aoeGravityPathLevel >= 1;
    public float AoeGravityRadius => aoeRadius * (_aoeGravityPathLevel >= 2 ? 1.33f : 1f);
    public float AoeGravityDuration => _aoeGravityPathLevel >= 3 ? 0.8f : 0.45f;
    public float AoeGravityPullSpeed => _aoeGravityPathLevel >= 3 ? 1.35f : 0.85f;
    public int AoeGravityFinalDamage => _aoeGravityPathLevel >= 3 ? Mathf.Max(1, Mathf.CeilToInt(bulletDamage * 0.5f)) : 0;

    // Reversible fire-rate multiplier from synergies (e.g. Harmony turrets-on-
    // the-synergy buff). >1 = faster. Set back to 1 to remove. Kept separate from
    // the PERMANENT AddAttackSpeed path so a synergy can cleanly grant/revoke.
    public void SetSynergyFireRateMultiplier(float multiplier)
    {
        _synergyFireRateMult = Mathf.Max(0.01f, multiplier);
    }

    // Reversible fire-rate multiplier from ENEMY debuffs (EnemyTurretSuppressor,
    // via TurretSuppressionEffect). <1 = slower. Its own channel so a suppressor
    // and a Harmony buff compose instead of clobbering each other's restore-to-1.
    public void SetDebuffFireRateMultiplier(float multiplier)
    {
        _debuffFireRateMult = Mathf.Max(0.01f, multiplier);
    }

    public float DebuffFireRateMultiplier => _debuffFireRateMult;

    // Reversible aura buff from a nearby Enlightenment shrine (ShrineController).
    // Its OWN channels so it composes with (never clobbers) the synergy buff and
    // enemy debuff. Call SetShrineBuff(1, 1) to clear — the shrine controller
    // re-asserts this every frame from turret↔shrine adjacency, so a turret that
    // walks out of the aura (or whose shrine vanishes) is cleared automatically.
    public void SetShrineBuff(float fireRateMult, float damageMult)
    {
        _shrineFireRateMult = Mathf.Max(0.01f, fireRateMult);
        _shrineDamageMult   = Mathf.Max(0.01f, damageMult);
    }

    public bool  ShrineBuffActive => _shrineFireRateMult > 1.0001f || _shrineDamageMult > 1.0001f;
    public float ShrineFireRateMultiplier => _shrineFireRateMult;

    // Damage a bullet fired RIGHT NOW deals — base bulletDamage scaled by the
    // reversible shrine channel (base is left untouched so the buff reverts cleanly).
    // TurretBullet reads this at spawn, so removing the aura instantly stops boosting
    // NEW shots without needing to rewind any permanent stat.
    public int EffectiveBulletDamage => Mathf.Max(1, Mathf.RoundToInt(bulletDamage * _shrineDamageMult));

    // Current fire-rate multipliers (1 = none) and the effective shots/sec after
    // them — used by the selection panel to show the live buff.
    public float SynergyFireRateMultiplier => _synergyFireRateMult;
    public float FireRateMultiplier => _synergyFireRateMult * _debuffFireRateMult * _shrineFireRateMult;
    public float EffectiveFireRate => fireInterval > 0.0001f ? FireRateMultiplier / fireInterval : 0f;

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

    // bulletPrefabOverride: pass BlockData.bulletPrefab here. TurretController is
    // added at runtime (AddComponent on a placed block) so it has no prefab of its
    // own to bind a bullet in the Inspector — BlockData does, since it's a real
    // ScriptableObject asset. Leave null to keep whatever's already set / fall back
    // to the Resources-folder lookup in ResolveBulletPrefab().
    // bulletScaleOverride: pass BlockData.bulletScale. 0 means "unset" (BlockData
    // assets predating this field default to 0) — leaves bulletScale at whatever
    // Configure() is called with, or the Inspector/AddComponent default of 1.
    public void Configure(BlockType type, GameObject bulletPrefabOverride = null, float bulletScaleOverride = 0f)
    {
        if (!TurretTypes.Is(type)) return;

        if (bulletPrefabOverride != null) bulletPrefab = bulletPrefabOverride;
        if (bulletScaleOverride > 0f) bulletScale = bulletScaleOverride;

        mode = TurretTypes.Mode(type);

        // Pull authoritative per-mode stats from BalanceTable.
        //
        // BalanceTable.Active, not just the `balance` field: this component is
        // AddComponent'd onto the placed block by PlacementController.AttachTurret,
        // so there's no prefab and no Inspector pass to ever fill that field in. It
        // was always null, which meant the whole turret section of the balance table
        // was dead and every turret silently ran on the field defaults below.
        var table = balance != null ? balance : BalanceTable.Active;
        if (table != null)
        {
            var s = table.GetTurretStats(mode);
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
        if (TotalUpgradesUsed >= TowerUpgradeGate.AllowedUpgrades)
        {
            reason = TowerUpgradeGate.AllowedUpgrades <= 0
                ? "Requires an active Enlightenment cube."
                : "No upgrades left — form a bigger Enlightenment cube.";
            return false;
        }
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

    // Restores a turret's upgrade levels (e.g. picking it up and re-placing
    // it) — deliberately bypasses CanUpgradeBasicPath's live Enlightenment gate.
    // These upgrades were already earned; re-placing the same turret must not
    // silently strip them just because no cube happens to be active THIS instant.
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

        for (int i = 1; i <= powerLevel; i++) { _powerPathLevel = i; ApplyBasicUpgrade(BasicTurretUpgradePath.Power, i); }
        for (int i = 1; i <= burstLevel; i++) { _burstPathLevel = i; ApplyBasicUpgrade(BasicTurretUpgradePath.Burst, i); }
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

    public bool CanUpgradeAoePath(AoeTurretUpgradePath path, out string reason)
    {
        reason = null;
        if (TotalUpgradesUsed >= TowerUpgradeGate.AllowedUpgrades)
        {
            reason = TowerUpgradeGate.AllowedUpgrades <= 0
                ? "Requires an active Enlightenment cube."
                : "No upgrades left — form a bigger Enlightenment cube.";
            return false;
        }
        if (mode != Mode.Aoe)
        {
            reason = "Only AOE Turret can use these upgrades.";
            return false;
        }

        int level = GetAoePathLevel(path);
        if (level >= 3)
        {
            reason = "Max level.";
            return false;
        }

        int otherLevel = path == AoeTurretUpgradePath.Fire ? _aoeGravityPathLevel : _aoeFirePathLevel;
        if (level == 2 && otherLevel >= 3)
        {
            reason = "Only one path can reach level 3.";
            return false;
        }

        return true;
    }

    public bool TryUpgradeAoePath(AoeTurretUpgradePath path)
    {
        if (!CanUpgradeAoePath(path, out _)) return false;

        int nextLevel = GetAoePathLevel(path) + 1;
        if (path == AoeTurretUpgradePath.Fire) _aoeFirePathLevel = nextLevel;
        else                                   _aoeGravityPathLevel = nextLevel;

        ApplyAoeUpgrade(path, nextLevel);
        return true;
    }

    // Same restore-bypass reasoning as SetBasicUpgradeLevels — see its comment.
    public void SetAoeUpgradeLevels(int fireLevel, int gravityLevel)
    {
        _aoeFirePathLevel = 0;
        _aoeGravityPathLevel = 0;
        if (mode != Mode.Aoe) return;

        fireLevel = Mathf.Clamp(fireLevel, 0, 3);
        gravityLevel = Mathf.Clamp(gravityLevel, 0, 3);
        if (fireLevel == 3 && gravityLevel == 3)
            gravityLevel = 2;

        for (int i = 1; i <= fireLevel; i++) { _aoeFirePathLevel = i; ApplyAoeUpgrade(AoeTurretUpgradePath.Fire, i); }
        for (int i = 1; i <= gravityLevel; i++) { _aoeGravityPathLevel = i; ApplyAoeUpgrade(AoeTurretUpgradePath.Gravity, i); }
    }

    public int GetAoePathLevel(AoeTurretUpgradePath path) =>
        path == AoeTurretUpgradePath.Fire ? _aoeFirePathLevel : _aoeGravityPathLevel;

    void ApplyAoeUpgrade(AoeTurretUpgradePath path, int level)
    {
        if (path == AoeTurretUpgradePath.Fire)
        {
            // Lv1 enables burning ground, Lv2 extends duration, Lv3 increases burn damage.
            return;
        }

        // Lv1 enables gravity well, Lv2 increases pull radius, Lv3 adds final crush damage.
        // Pull radius is derived from AoeGravityRadius so the base blast radius stays stable.
    }

    public string NextAoeUpgradeDescription(AoeTurretUpgradePath path)
    {
        int next = GetAoePathLevel(path) + 1;
        if (next > 3) return "Max level";

        if (path == AoeTurretUpgradePath.Fire)
        {
            return next switch
            {
                1 => "Burning ground",
                2 => "Burn lasts longer",
                3 => "Burn damage up",
                _ => "Max level",
            };
        }

        return next switch
        {
            1 => "Gravity pull",
            2 => "Pull radius +33%",
            3 => "Final crush blast",
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
        _fireTimer = fireInterval / FireRateMultiplier;
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

    // Per-mode projectile size, multiplied on top of bulletScale. The shared prefab
    // is sized for the basic turret, and at that size the AOE shot read as the same
    // pellet — nothing about it said "this one splashes".
    float ModeBulletScale => mode switch
    {
        Mode.Aoe => 2.3f,    // an energy ball, not a bullet
        _        => 1.6f,
    };

    void Fire(EnemySurfaceUnit target)
    {
        // Slow fires a BEAM, not a projectile: the effect is a hold, and a hold
        // has to land the instant the turret decides to apply it. Damage and the
        // debuff are applied here rather than on a bullet's impact — there is no
        // impact to wait for. Line of sight was already cleared by CanShoot.
        if (mode == Mode.Slow)
        {
            TurretShotFx.Beam(MuzzlePosition, target.transform.position);
            target.TakeDamage(EffectiveBulletDamage);
            EnemySlowEffect.Apply(target, slowDuration, slowMultiplier);
            return;
        }

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
        bullet.transform.localScale *= bulletScale * ModeBulletScale;

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

    // Tints ONLY the turret's own visual (this GameObject + children), never
    // transform.parent — that's the block, which has to keep showing its synergy
    // colour. The division of labour is: block = which synergy you're on, gun on
    // top = which turret type it is. Tinting the parent made every turret repaint
    // its whole block and fight the synergy palette.
    void ApplyModeColor()
    {
        Color color = TurretTypes.DisplayColor(mode);

        // MaterialPropertyBlock instead of r.material.color — the latter clones a
        // unique material per turret renderer (extra allocs, no GPU instancing).
        foreach (var r in GetComponentsInChildren<Renderer>())
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
