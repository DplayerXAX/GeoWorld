using UnityEngine;

public class EnemySplitOnAlive : MonoBehaviour
{
    [Header("Split Trigger")]
    [Tooltip("Unique block cells this enemy must travel after spawning before it splits. 1 = split after entering the next block cell.")]
    [Min(1)] public int blocksBeforeSplit = 1;
    [Tooltip("How many children to create when splitting.")]
    [Min(1)] public int childCount = 2;
    [Tooltip("If enabled, split children can keep splitting forever. Each individual enemy still splits only once.")]
    public bool unlimitedGenerations = false;
    [Tooltip("Maximum split generation. 1 means only the original can split.")]
    [Min(0)] public int maxGeneration = 1;
    [Tooltip("Global cap for live split-capable enemies. When this many splitters are alive, further splitting is skipped.")]
    [Min(1)] public int maxActiveSplitEnemies = 40;

    [Header("Children")]
    [Tooltip("Optional child prefab. Leave empty to clone this enemy.")]
    public EnemySurfaceUnit childPrefab;
    [Min(0.01f)] public float childHealthMultiplier = 0.5f;
    [Min(0.01f)] public float childSpeedMultiplier = 1.12f;
    [Min(0.1f)] public float childScaleMultiplier = 0.72f;
    [Min(0f)] public float childRewardMultiplier = 0f;
    [Min(0f)] public float scatterRadius = 0.35f;

    EnemySurfaceUnit _enemy;
    int _blocksSinceSpawn;
    bool _hasSplit;
    int _generation;

    public int Generation => _generation;

    void Awake()
    {
        _enemy = GetComponent<EnemySurfaceUnit>();
    }

    void OnEnable()
    {
        if (_enemy == null)
            _enemy = GetComponent<EnemySurfaceUnit>();
        if (_enemy != null)
            _enemy.OnBlockTraveled += HandleBlockTraveled;
    }

    void OnDisable()
    {
        if (_enemy != null)
            _enemy.OnBlockTraveled -= HandleBlockTraveled;
    }

    void HandleBlockTraveled(EnemySurfaceUnit enemy, int totalBlocksTraveled)
    {
        if (_hasSplit || _enemy == null || _enemy.CurrentHealth <= 0) return;

        _blocksSinceSpawn++;
        if (_blocksSinceSpawn >= blocksBeforeSplit)
            TrySplit();
    }

    public void SetGeneration(int generation)
    {
        _generation = Mathf.Max(0, generation);
        _blocksSinceSpawn = 0;
        _hasSplit = false;
    }

    void TrySplit()
    {
        if (_hasSplit || _enemy == null || _enemy.CurrentHealth <= 0) return;
        if (!unlimitedGenerations && _generation >= maxGeneration) return;

        var manager = EnemyBaseManager.Instance;
        if (manager == null) return;

        int availableSlots = maxActiveSplitEnemies - manager.CountActiveSplitEnemies();
        if (availableSlots <= 0)
        {
            _hasSplit = true;
            return;
        }

        int spawned = manager.SpawnSplitChildren(
                _enemy,
                childPrefab,
                Mathf.Min(childCount, availableSlots),
                childHealthMultiplier,
                childSpeedMultiplier,
                childScaleMultiplier,
                childRewardMultiplier,
                scatterRadius);

        if (spawned > 0)
            _hasSplit = true;
    }
}
