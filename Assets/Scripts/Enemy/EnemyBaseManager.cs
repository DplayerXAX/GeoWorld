using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyBaseManager : MonoBehaviour
{
    public static EnemyBaseManager Instance;

    [Header("Wave (fallback when no WaveDefinition is supplied)")]
    public EnemySurfaceUnit enemyPrefab;
    public int spawnCount = 3;
    public float spawnInterval = 0.75f;

    [Header("Default enemy visual")]
    [Tooltip("Extra world-space distance the enemy floats above each block face (added on top of 0.5×cellSize). Bump this up if the shards visually dip into the block surface. Propagated to EnemySurfaceUnit.faceClearance.")]
    public float enemyFaceClearance = 0.25f;
    [Tooltip("Material applied to each main geometric shard. Use a flat-color URP Unlit/Lit material — each shard is per-instance tinted via MaterialPropertyBlock so they all share one asset (1 draw call).")]
    public Material enemyShardMaterial;
    [Tooltip("Optional bright material for the small accent 'sparkle' shards. Falls back to enemyShardMaterial when null.")]
    public Material enemyAccentMaterial;
    [Tooltip("Outline material applied to every shard. Same one you use on blocks for a consistent cartoon look. Null = no outline (the enemy will blend into the painterly background).")]
    public Material enemyOutlineMaterial;

    // Fired once when the last enemy is gone AND spawning has finished.
    // GameFlowManager listens to know when to transition back to Build.
    public event Action OnWaveCompleted;

    IList<SpawnGroup> _currentGroups;
    int _spawnTotal;

    [Header("Enemy Pacing")]
    public bool useSurfaceUnitTempo = true;
    public float enemyBpm = 120f;
    [Range(0.5f, 0.95f)] public float enemyMoveRatio = 0.8f;

    public bool WaveActive => _waveActive;
    public int ActiveEnemyCount => _activeEnemies.Count;
    public int SpawnedCount => _spawnedCount;
    public int TargetSpawnCount => _spawnTotal > 0 ? _spawnTotal : spawnCount;

    readonly List<EnemySurfaceUnit> _activeEnemies = new();
    Coroutine _spawnRoutine;
    bool _waveActive;
    int _spawnedCount;
    List<FaceNode> _currentPath;

    void Awake()
    {
        Instance = this;
    }

    public void BeginWave(List<FaceNode> path, SurfaceUnit tempoSource = null /* legacy */)
        => BeginWave(path, (IList<SpawnGroup>)null, tempoSource);

    public void BeginWave(List<FaceNode> path, IList<SpawnGroup> groups, SurfaceUnit tempoSource = null)
    {
        if (path == null || path.Count == 0)
        {
            Debug.LogWarning("[EnemyBaseManager] Cannot begin wave without a valid path.");
            return;
        }

        CancelWave();

        _currentPath   = new List<FaceNode>(path);
        _currentGroups = groups;
        _spawnedCount  = 0;
        _spawnTotal    = CountSpawns(groups);
        if (_spawnTotal == 0) _spawnTotal = Mathf.Max(0, spawnCount);  // fall back to legacy
        _waveActive    = true;

        if (useSurfaceUnitTempo && tempoSource != null)
        {
            enemyBpm = tempoSource.bpm;
            enemyMoveRatio = tempoSource.moveRatio;
        }

        _spawnRoutine = StartCoroutine(SpawnWave());
    }

    static int CountSpawns(IList<SpawnGroup> groups)
    {
        if (groups == null) return 0;
        int total = 0;
        for (int i = 0; i < groups.Count; i++)
            if (groups[i] != null) total += Mathf.Max(0, groups[i].count);
        return total;
    }

    public void CancelWave()
    {
        if (_spawnRoutine != null)
        {
            StopCoroutine(_spawnRoutine);
            _spawnRoutine = null;
        }

        foreach (var enemy in _activeEnemies)
            if (enemy != null)
                Destroy(enemy.gameObject);

        _activeEnemies.Clear();
        _currentPath   = null;
        _currentGroups = null;
        _spawnTotal    = 0;
        _waveActive    = false;
    }

    IEnumerator SpawnWave()
    {
        if (_currentGroups != null && _currentGroups.Count > 0)
        {
            foreach (var group in _currentGroups)
            {
                if (!_waveActive) yield break;
                if (group == null) continue;

                if (group.preDelay > 0f)
                    yield return new WaitForSeconds(group.preDelay);

                int n     = Mathf.Max(0, group.count);
                float dly = Mathf.Max(0f, group.interval);

                for (int i = 0; i < n; i++)
                {
                    if (!_waveActive) yield break;

                    SpawnEnemy(group.prefab);
                    _spawnedCount++;

                    bool last = (i == n - 1);
                    if (!last && dly > 0f)
                        yield return new WaitForSeconds(dly);
                    else
                        yield return null;
                }
            }
        }
        else
        {
            // Legacy path: single homogeneous group using inspector fields.
            int count = Mathf.Max(0, spawnCount);
            float delay = Mathf.Max(0f, spawnInterval);

            while (_spawnedCount < count && _waveActive)
            {
                SpawnEnemy(null);
                _spawnedCount++;

                if (_spawnedCount < count && delay > 0f)
                    yield return new WaitForSeconds(delay);
                else
                    yield return null;
            }
        }

        _spawnRoutine = null;
    }

    void SpawnEnemy(EnemySurfaceUnit prefabOverride)
    {
        if (_currentPath == null || _currentPath.Count == 0) return;

        EnemySurfaceUnit source = prefabOverride != null ? prefabOverride : enemyPrefab;

        EnemySurfaceUnit enemy;
        if (source != null)
        {
            enemy = Instantiate(source);
        }
        else
        {
            enemy = CreateDefaultEnemy();
        }

        enemy.faceClearance = enemyFaceClearance;

        var first = _currentPath[0];
        enemy.transform.position = GridSystem.instance.GridToWorld(first.cell)
                                   + first.normal * (GridSystem.instance.cellSize * 0.5f + enemyFaceClearance);
        enemy.OnReachedEnd += HandleEnemyReachedEnd;
        enemy.OnDied += HandleEnemyDied;
        _activeEnemies.Add(enemy);
        enemy.SetPath(_currentPath, enemyBpm, enemyMoveRatio);
    }

    // Lazy-built fallback outline material — used if `enemyOutlineMaterial`
    // wasn't assigned in the Inspector. Looks up `GeoWorld/BlockOutline` at
    // runtime and bakes a sensible dark default. Shared across all enemies.
    static Material _runtimeOutlineFallback;
    static Material GetRuntimeOutlineFallback()
    {
        if (_runtimeOutlineFallback != null) return _runtimeOutlineFallback;
        var sh = Shader.Find("GeoWorld/BlockOutline");
        if (sh == null)
        {
            Debug.LogWarning("[EnemyBaseManager] Shader 'GeoWorld/BlockOutline' not found — enemies will spawn without cartoon outlines.");
            return null;
        }
        _runtimeOutlineFallback = new Material(sh);
        _runtimeOutlineFallback.name = "EnemyOutline_Runtime";
        _runtimeOutlineFallback.SetColor("_OutlineColor", new Color(0.04f, 0.04f, 0.08f, 1f));
        _runtimeOutlineFallback.SetFloat("_OutlineWidth", 0.07f);
        return _runtimeOutlineFallback;
    }

    EnemySurfaceUnit CreateDefaultEnemy()
    {
        // Build the chaotic / abstract enemy visual: off-axis core cube,
        // shards radiating outward, tilted satellites. Each enemy is unique
        // (RNG seeded by instance ID) but stable per-instance.
        var root = new GameObject("EnemySurfaceUnit");
        root.transform.localScale = Vector3.one * 0.78f;

        // Outline FIRST (component order matters — we'll trigger Apply
        // manually from EnemyChaoticVisual.Start once children exist).
        // If the Inspector slot is empty, fall back to a runtime-built
        // BlockOutline material so the enemy always reads from the background.
        var outlineMat = enemyOutlineMaterial != null
            ? enemyOutlineMaterial
            : GetRuntimeOutlineFallback();
        if (outlineMat != null)
        {
            var applier = root.AddComponent<BlockOutlineApplier>();
            applier.outlineMaterial      = outlineMat;
            applier.applyOnAwake         = false;
            applier.includeChildren      = true;
            applier.skipIfAlreadyPresent = true;
        }

        var visual = root.AddComponent<EnemyChaoticVisual>();
        visual.shardMaterial  = enemyShardMaterial;
        visual.accentMaterial = enemyAccentMaterial;

        return root.AddComponent<EnemySurfaceUnit>();
    }

    void HandleEnemyReachedEnd(EnemySurfaceUnit enemy)
    {
        if (PlayerHealth.Instance != null)
        {
            PlayerHealth.Instance.TakeDamage(1);
            Debug.Log($"[EnemyBaseManager] Enemy escaped. Lives → {PlayerHealth.Instance.CurrentLives}");
        }
        else
        {
            Debug.LogWarning("[EnemyBaseManager] Enemy escaped but no PlayerHealth in scene!");
        }
        RemoveEnemy(enemy, destroy: true);
    }

    void HandleEnemyDied(EnemySurfaceUnit enemy)
    {
        RemoveEnemy(enemy, destroy: true);
    }

    void RemoveEnemy(EnemySurfaceUnit enemy, bool destroy)
    {
        if (enemy == null) return;

        enemy.OnReachedEnd -= HandleEnemyReachedEnd;
        enemy.OnDied -= HandleEnemyDied;
        _activeEnemies.Remove(enemy);

        if (destroy)
            Destroy(enemy.gameObject);

        if (_spawnRoutine == null && _activeEnemies.Count == 0 && _spawnedCount >= _spawnTotal)
        {
            if (_waveActive)
            {
                _waveActive = false;
                OnWaveCompleted?.Invoke();
            }
        }
    }
}
