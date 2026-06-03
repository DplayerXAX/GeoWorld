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

    [Header("Targeting")]
    public int targetPriority = 0;

    public event Action<EnemySurfaceUnit> OnReachedEnd;
    public event Action<EnemySurfaceUnit> OnDied;

    public int CurrentHealth => _health;
    public IReadOnlyList<FaceNode> Path => _path;

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

    void Awake()
    {
        _health = maxHealth;
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
        _temporarySpeedMultiplier = 1f;

        StepToNode(_path[0]);
        _index = 1;
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

    static Vector3 FaceCenter(FaceNode node)
    {
        var gs = GridSystem.instance;
        return gs.GridToWorld(node.cell) + node.normal * (gs.cellSize * 0.5f);
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

        var instance = GridSystem.instance?.GetInstanceAt(cell);
        if (instance?.data != null)
            ResourceManager.Instance?.OnEnemyPassedBlock(instance.data.blockType);

        _lastRewardCell = cell;
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
