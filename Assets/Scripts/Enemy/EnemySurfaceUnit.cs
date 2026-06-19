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

    public int CurrentHealth => _health;
    public IReadOnlyList<FaceNode> Path => _path;
    public int BlocksTraveled => _blocksTraveled;

    // The grid cell the enemy is currently on (the block under its current
    // face). Null before the first step. Used by area synergies (e.g. Order
    // slow) to test whether the enemy is standing on the synergy.
    public Vector3Int? CurrentCell => _prevNode?.cell;

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

    public void TakeDamage(int amount)
    {
        if (amount <= 0 || _health <= 0) return;

        _health = Mathf.Max(0, _health - amount);
        if (_health == 0)
            Die();
    }

    public void SetSpeedMultiplier(float multiplier)
    {
        _temporarySpeedMultiplier = Mathf.Max(0.01f, multiplier);
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
        if (_path == null || _health <= 0) return;

        _beatTimer += Time.deltaTime;
        float secPerBeat = _secPerBeat / EffectiveSpeedMultiplier;
        if (_beatTimer >= secPerBeat)
        {
            _beatTimer -= secPerBeat;

            if (_index < _path.Count)
            {
                StepToNode(_path[_index]);
                _index++;
            }
            else
            {
                ReachEnd();
                return;
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
        _prevNode = node;
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
        _path = null;
        OnReachedEnd?.Invoke(this);
    }

    void Die()
    {
        _path = null;
        OnDied?.Invoke(this);
    }

    float EffectiveSpeedMultiplier => Mathf.Max(0.01f, baseSpeedMultiplier * _temporarySpeedMultiplier);
}
