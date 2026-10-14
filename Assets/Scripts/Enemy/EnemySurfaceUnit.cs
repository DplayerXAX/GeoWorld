using System;
using System.Collections.Generic;
using UnityEngine;

public class EnemySurfaceUnit : MonoBehaviour
{
    [Header("Movement")]
    public float bpm = 120f;
    [Range(0.5f, 0.95f)] public float moveRatio = 0.8f;
    [Min(0.01f)] public float baseSpeedMultiplier = 1f;

    [Header("Health")]
    public int maxHealth = 3;

    [Header("Reward")]
    [Tooltip("BLOCK currency awarded when this enemy is killed (combat-success income that feeds the building budget). Set by EnemyBaseManager from BalanceTable when a matching EnemyRecord is found, otherwise uses this Inspector default.")]
    public int rewardOnKill = 1;

    [Header("Placement")]
    [Tooltip("Extra world-space clearance ABOVE the 0.5×cellSize face offset. Bump this up if the visual's bottom shards dip into the block surface. Applies to both the initial spawn position and each step along the path.")]
    public float faceClearance = 0.15f;
    [Header("Targeting")]
    public int targetPriority = 0;

    public event Action<EnemySurfaceUnit> OnReachedEnd;
    public event Action<EnemySurfaceUnit> OnDied;
    public event Action<EnemySurfaceUnit, int> OnBlockTraveled;

    // Global hooks (for objectives / score / stats) — fire for ANY enemy.
    public static event Action<EnemySurfaceUnit> AnyDied;
    // (who, how much was actually taken, which turret dealt it — null for an area
    // effect, a trap or anything else with no turret behind it). The battle report
    // is built from this.
    public static event Action<EnemySurfaceUnit, int, TurretController> AnyDamaged;
    public static event Action<EnemySurfaceUnit> AnyReachedEnd;

    [Tooltip("Extra lives lost when this one reaches an end point (on top of the usual 1). Set by elites and bosses.")]
    public int leakBonus;

    public int CurrentHealth => _health;
    public IReadOnlyList<FaceNode> Path => _path;
    public int BlocksTraveled => _blocksTraveled;

    // The grid cell the enemy is currently on (the block under its current
    // face). Null before the first step. Used by area synergies (e.g. Order
    // slow) to test whether the enemy is standing on the synergy.
    public Vector3Int? CurrentCell => _prevNode?.cell;
    // Normal of the face the enemy is currently on (for re-pathing on grid changes).
    public Vector3? CurrentNormal => _prevNode != null ? _prevNode.normal : (Vector3?)null;

    List<FaceNode> _path;
    int _index;
    int _health;
    float _secPerBeat;
    float _beatTimer;
    float _temporarySpeedMultiplier = 1f;

    bool _isMoving;
    Vector3 _moveFrom;
    Vector3 _moveTo;
    Vector3 _controlPoint;
    Quaternion _rotFrom;
    Quaternion _rotTo;
    float _moveDuration;
    float _moveTimer;
    FaceNode _prevNode;
    Vector3Int? _lastRewardCell;
    int _blocksTraveled;

    void Awake()
    {
        _health = maxHealth;
    }

    // Sets BOTH maxHealth and current _health. Call after Instantiate when
    // overriding the prefab's authored HP (e.g. from a BalanceTable record).
    // Plain `maxHealth = x` doesn't work because Awake() has already copied
    // the prefab value into _health.
    public void SetMaxHealth(int hp)
    {
        maxHealth = Mathf.Max(1, hp);
        _health   = maxHealth;
    }

    public void SetPath(List<FaceNode> path, float pathBpm, float pathMoveRatio)
    {
        if (path == null || path.Count == 0)
        {
            Debug.LogWarning("[EnemySurfaceUnit] Cannot follow an empty path.");
            return;
        }

        _path = new List<FaceNode>(path);
        bpm = Mathf.Max(1f, pathBpm);
        moveRatio = Mathf.Clamp(pathMoveRatio, 0.5f, 0.95f);
        _secPerBeat = 60f / bpm;
        _beatTimer = 0f;
        _index = 0;
        _prevNode = null;
        _lastRewardCell = null;
        _blocksTraveled = 0;
        _temporarySpeedMultiplier = 1f;

        StepToNode(_path[0]);
        _index = 1;
    }

    public List<FaceNode> GetRemainingPathSnapshot()
    {
        if (_path == null || _path.Count == 0) return null;

        int start = Mathf.Clamp(_index - 1, 0, _path.Count - 1);
        var remaining = new List<FaceNode>(_path.Count - start);
        for (int i = start; i < _path.Count; i++)
            remaining.Add(_path[i]);
        return remaining;
    }

    /// <summary>
    /// True when this unit is a puppet of the host's simulation: it does not path,
    /// does not fight back and cannot be damaged locally. Its transform and health
    /// are written by CombatSync from the snapshot stream.
    /// </summary>
    public bool IsProxy { get; private set; }

    public void MakeProxy()
    {
        IsProxy = true;
        // This unit walks itself off _path in Update. Leaving that null is all it
        // takes to hand the transform over to CombatSync — no component juggling,
        // and the same early-out the class already uses when it has nowhere to go.
        _path = null;
    }

    /// <summary>Host's word on this enemy's health. Drives the flinch and the bars.</summary>
    public void ApplyRemoteHealth(int health, int max)
    {
        if (max > 0) maxHealth = max;
        int before = _health;
        _health = Mathf.Clamp(health, 0, maxHealth);

        // Flinch on the DELTA rather than on a damage call: a spectator never runs
        // TakeDamage, and without this the whole fight would read as bloodless on
        // three of the four screens.
        if (_health < before && _health > 0) ImpactFx.Hit(transform);
    }

    /// <summary>Remove a proxy the host stopped reporting — it died or got through.</summary>
    public void KillProxy()
    {
        if (!IsProxy) return;
        EnemyDeathFx.Explode(transform.position);
        Destroy(gameObject);
    }

    public void TakeDamage(int amount, TurretController source = null)
    {
        // Damage is the host's business. A client that also applied it would kill
        // enemies the host still considers alive, and the next snapshot would raise
        // them again — the classic spectator resurrection.
        if (IsProxy) return;
        if (amount <= 0 || _health <= 0) return;

        // Inside a Prayer turret's field it takes more. Hits are small ints, so the
        // fraction is carried between hits instead of rounded away — at +20%, five
        // hits of 1 deal 6.
        if (InPrayer && _prayerMult > 1.0001f)
        {
            float due = amount * _prayerMult + _prayerCarry;
            amount       = Mathf.FloorToInt(due);
            _prayerCarry = due - amount;
        }

        int healthBefore = _health;
        _health = Mathf.Max(0, _health - amount);
        if (healthBefore > _health) AnyDamaged?.Invoke(this, healthBefore - _health, source);

        // The flinch. Before this, taking a hit and missing looked identical: the
        // health number changed and nothing on the enemy moved until it died. This is
        // the single largest gap in the game's combat feel, and it costs one line.
        if (_health > 0) ImpactFx.Hit(transform);

        if (_health == 0)
            Die();
    }

    // Restores health, capped at maxHealth. Never revives a dead enemy. Returns
    // how much was actually restored so callers can skip FX on a full-HP target.
    // Sets health outright — no hit, no heal, no events. For restoring a saved unit.
    public void SetCurrentHealth(int health) => _health = Mathf.Clamp(health, 1, Mathf.Max(1, maxHealth));

    public int Heal(int amount)
    {
        if (amount <= 0 || _health <= 0 || _health >= maxHealth) return 0;
        amount = RogueRun.ScaleHeal(this, amount);   // Pain Releaser etc.; identity otherwise
        if (amount <= 0) return 0;

        int before = _health;
        _health = Mathf.Min(maxHealth, _health + amount);
        return _health - before;
    }

    // ── Prayer field: damage taken up ───────────────────────────────────────────
    // Re-applied every frame by each Prayer Turret field the enemy stands in, and
    // lapses on its own a moment after it walks out — no field has to remember whom
    // it touched. Overlapping fields don't stack: the strongest one applies.
    float _prayerMult = 1f, _prayerUntil = -1f, _prayerCarry;

    public bool  InPrayer   => Time.time < _prayerUntil;
    public float PrayerMult => InPrayer ? _prayerMult : 1f;

    public void ApplyPrayer(float damageMult, float duration)
    {
        damageMult  = Mathf.Max(1f, damageMult);
        _prayerMult  = InPrayer ? Mathf.Max(_prayerMult, damageMult) : damageMult;
        _prayerUntil = Mathf.Max(_prayerUntil, Time.time + duration);
    }

    public void SetSpeedMultiplier(float multiplier)
    {
        _temporarySpeedMultiplier = Mathf.Max(0.01f, multiplier);
    }

    // The reversible slow/haste channel on its own (1 = untouched). Read by
    // StatusEffectWatcher to decide whether to show buff / debuff arrows —
    // baseSpeedMultiplier is deliberately excluded, since that's the archetype's
    // authored speed, not a status effect.
    public float TemporarySpeedMultiplier => _temporarySpeedMultiplier;

    // ── Status marks (for items that count an enemy's debuffs) ───────────────
    public enum Debuff { Burning, Pulled }
    float _burnUntil = -1f, _pullUntil = -1f;

    public void MarkDebuff(Debuff d, float seconds)
    {
        float until = Time.time + seconds;
        if (d == Debuff.Burning) _burnUntil = Mathf.Max(_burnUntil, until);
        else                     _pullUntil = Mathf.Max(_pullUntil, until);
    }

    /// <summary>How many DIFFERENT debuffs it carries now: slowed, in a Prayer field, burning, being pulled.</summary>
    public int DebuffCount
    {
        get
        {
            int n = 0;
            if (_temporarySpeedMultiplier < 0.999f) n++;
            if (InPrayer) n++;
            if (Time.time < _burnUntil) n++;
            if (Time.time < _pullUntil) n++;
            return n;
        }
    }

    // Which node of _path it is on. While it is being stepped onto a node (a device
    // reacts to that step) `_index` has not been advanced yet and IS that node.
    bool _stepping, _pathEdited;
    int CurrentNodeIndex => _stepping ? _index : _index - 1;

    // ── Defection (Turncoat device) ──────────────────────────────────────────
    /// <summary>Turned against its own side: walks back the way it came and fights what it meets.</summary>
    public bool IsAlly { get; private set; }
    float _allyDamage = 3f, _allyTick;

    public void TurnCoat(float contactDamage)
    {
        if (IsAlly || _path == null || _health <= 0) return;
        IsAlly = true;
        _allyDamage = Mathf.Max(1f, contactDamage);

        // The route back: every node it has stood on, reversed.
        int cur = Mathf.Clamp(CurrentNodeIndex, 0, _path.Count - 1);
        var back = new List<FaceNode>(cur + 1);
        for (int i = cur; i >= 0; i--) back.Add(_path[i]);
        _path  = back;
        _index = 1;
        _pathEdited = true;
        _temporarySpeedMultiplier = 1f;
    }

    void TickAlly()
    {
        _allyTick -= Time.deltaTime;
        if (_allyTick > 0f) return;
        _allyTick = 0.45f;

        var mgr = EnemyBaseManager.Instance;
        if (mgr == null) return;
        var list = mgr.ActiveEnemies;
        float r2 = 0.8f * 0.8f;
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            if (e == null || e == this || e.IsAlly || e.CurrentHealth <= 0) continue;
            if ((e.transform.position - transform.position).sqrMagnitude > r2) continue;
            e.TakeDamage(Mathf.RoundToInt(_allyDamage));
            ImpactFx.Hit(e.transform);
        }
    }

    // ── Teleport (Magic Spring device) ───────────────────────────────────────
    /// <summary>Jumps along its own route by `delta` nodes (negative = back toward the start).</summary>
    public void TeleportAlongPath(int delta)
    {
        if (_path == null || _path.Count == 0 || _health <= 0) return;
        int to = Mathf.Clamp(CurrentNodeIndex + delta, 0, _path.Count - 1);
        var node = _path[to];
        transform.position = FaceCenter(node);
        _prevNode = node;
        _index = Mathf.Min(_path.Count, to + 1);
        _pathEdited = true;
        _isMoving = false;
        _beatTimer = 0f;
        _moveFrom = _moveTo = _controlPoint = transform.position;
    }

    // Is another enemy standing where this one is about to step? (Crowded.)
    bool NodeTaken(FaceNode node)
    {
        var mgr = EnemyBaseManager.Instance;
        if (mgr == null) return false;
        var list = mgr.ActiveEnemies;
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            if (e == null || e == this || e.IsAlly || e.CurrentHealth <= 0) continue;
            if (e.GetComponent<ChaosBlockUnit>() != null) continue;
            var c = e.CurrentCell;
            if (c.HasValue && c.Value == node.cell && e._prevNode != null && e._prevNode.normal == node.normal)
                return true;
        }
        return false;
    }

    public void AddExternalDisplacement(Vector3 delta)
    {
        if (_health <= 0 || delta.sqrMagnitude <= 0.000001f) return;

        transform.position += delta;
        _moveFrom += delta;
        _moveTo += delta;
        _controlPoint += delta;
    }

    void Update()
    {
        if (IsProxy || _path == null || _health <= 0) return;

        if (IsAlly) TickAlly();

        _beatTimer += Time.deltaTime;
        float secPerBeat = _secPerBeat / EffectiveSpeedMultiplier;
        if (_beatTimer >= secPerBeat)
        {
            if (_index < _path.Count && RogueRun.AvoidOverlap && !IsAlly && NodeTaken(_path[_index]))
            {
                _beatTimer = secPerBeat;   // wait for the one ahead; look again next frame
            }
            else
            {
                _beatTimer -= secPerBeat;

                if (_index < _path.Count)
                {
                    // A device on the node may rewrite the route under us (a spring,
                    // a turncoat), in which case _index is already where it should be.
                    int stepIdx = _index;
                    _stepping = true; _pathEdited = false;
                    StepToNode(_path[stepIdx]);
                    _stepping = false;
                    if (!_pathEdited) _index++;
                }
                else
                {
                    ReachEnd();
                    return;
                }
            }
        }

        if (!_isMoving) return;

        _moveTimer += Time.deltaTime;
        float t = Mathf.Clamp01(_moveTimer / _moveDuration);
        t = t * t * (3f - 2f * t);

        Vector3 m1 = Vector3.Lerp(_moveFrom, _controlPoint, t);
        Vector3 m2 = Vector3.Lerp(_controlPoint, _moveTo, t);
        transform.position = Vector3.Lerp(m1, m2, t);
        transform.rotation = Quaternion.Slerp(_rotFrom, _rotTo, t);

        if (_moveTimer >= _moveDuration)
            _isMoving = false;
    }

    Vector3 FaceCenter(FaceNode node)
    {
        var gs = GridSystem.instance;
        // Half a cell + a tunable clearance so taller / bulkier enemy visuals
        // don't dip into the block surface.
        return gs.GridToWorld(node.cell) + node.normal * (gs.cellSize * 0.5f + faceClearance);
    }

    void StepToNode(FaceNode node)
    {
        _moveFrom = transform.position;
        _moveTo = FaceCenter(node);
        _moveDuration = (_secPerBeat / EffectiveSpeedMultiplier) * moveRatio;
        _moveTimer = 0f;
        _isMoving = true;

        if (_prevNode != null && Vector3.Angle(_prevNode.normal, node.normal) > 1f)
        {
            Vector3 center = (_moveFrom + _moveTo) / 2f;
            Vector3 outwardNormal = (_prevNode.normal + node.normal).normalized;
            _controlPoint = center + outwardNormal * (GridSystem.instance.cellSize * 0.4f);
        }
        else
        {
            _controlPoint = (_moveFrom + _moveTo) / 2f;
        }

        _rotFrom = transform.rotation;

        Vector3 moveDir = (_moveTo - _moveFrom).normalized;
        if (moveDir.sqrMagnitude < 0.001f)
            moveDir = transform.forward;

        Vector3 forwardOnSurface = Vector3.ProjectOnPlane(moveDir, node.normal).normalized;
        if (forwardOnSurface.sqrMagnitude < 0.001f)
            forwardOnSurface = transform.forward;

        _rotTo = Quaternion.LookRotation(forwardOnSurface, node.normal);

        RewardBlockPass(node.cell);
        _prevNode = node;   // before the devices: one may move the enemy, and that wins
        // Devices that react to being stepped on. Kept as a registry lookup rather
        // than an event so nothing has to be subscribed or unsubscribed per enemy.
        DeviceRegistry.TrapAt(node.cell)?.TryTrigger(this);
    }

    void RewardBlockPass(Vector3Int cell)
    {
        if (_lastRewardCell.HasValue && _lastRewardCell.Value == cell) return;

        bool traveledToNewBlock = _lastRewardCell.HasValue;

        var instance = GridSystem.instance?.GetInstanceAt(cell);
        if (instance?.data != null)
            ResourceManager.Instance?.OnEnemyPassedBlock(instance.data.blockType);

        _lastRewardCell = cell;

        if (traveledToNewBlock)
        {
            _blocksTraveled++;
            OnBlockTraveled?.Invoke(this, _blocksTraveled);
        }
    }

    void ReachEnd()
    {
        // A defector reaching the far end of its way back has simply run out of road:
        // it is not a leak, and costs nothing.
        if (IsAlly) { Die(); return; }

        _path = null;
        OnReachedEnd?.Invoke(this);
        AnyReachedEnd?.Invoke(this);
    }

    void Die()
    {
        _path = null;
        OnDied?.Invoke(this);
        AnyDied?.Invoke(this);
    }

    float EffectiveSpeedMultiplier =>
        Mathf.Max(0.01f, baseSpeedMultiplier * _temporarySpeedMultiplier * Modifiers.Mul(Stat.EnemySpeed));
}
