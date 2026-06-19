using UnityEngine;

public class EnemySplitOnAlive : MonoBehaviour
{
    [Header("Split Trigger")]
    [Tooltip("Seconds this enemy stays alive before it splits.")]
    [Min(0.1f)] public float splitInterval = 5f;
    [Tooltip("How many children to create when splitting.")]
    [Min(1)] public int childCount = 2;
    [Tooltip("If enabled, split children can keep splitting forever. Each individual enemy still splits only once.")]
    public bool unlimitedGenerations = false;
    [Tooltip("Maximum split generation. 1 means only the original can split.")]
    [Min(0)] public int maxGeneration = 1;

    [Header("Children")]
    [Tooltip("Optional child prefab. Leave empty to clone this enemy.")]
    public EnemySurfaceUnit childPrefab;
    [Min(0.01f)] public float childHealthMultiplier = 0.5f;
    [Min(0.01f)] public float childSpeedMultiplier = 1.12f;
    [Min(0.1f)] public float childScaleMultiplier = 0.72f;
    [Min(0f)] public float childRewardMultiplier = 0f;
    [Min(0f)] public float scatterRadius = 0.35f;

    EnemySurfaceUnit _enemy;
    float _age;
    bool _hasSplit;
    int _generation;

    public int Generation => _generation;

    void Awake()
    {
        _enemy = GetComponent<EnemySurfaceUnit>();
    }

    void Update()
    {
        if (_hasSplit || _enemy == null || _enemy.CurrentHealth <= 0) return;

        _age += Time.deltaTime;
        if (_age >= splitInterval)
            TrySplit();
    }

    public void SetGeneration(int generation)
    {
        _generation = Mathf.Max(0, generation);
        _age = 0f;
        _hasSplit = false;
    }

    void TrySplit()
    {
        if (_hasSplit || _enemy == null || _enemy.CurrentHealth <= 0) return;
        if (!unlimitedGenerations && _generation >= maxGeneration) return;

        int spawned = EnemyBaseManager.Instance != null
            ? EnemyBaseManager.Instance.SpawnSplitChildren(
                _enemy,
                childPrefab,
                childCount,
                childHealthMultiplier,
                childSpeedMultiplier,
                childScaleMultiplier,
                childRewardMultiplier,
                scatterRadius)
            : 0;

        if (spawned > 0)
            _hasSplit = true;
    }
}
