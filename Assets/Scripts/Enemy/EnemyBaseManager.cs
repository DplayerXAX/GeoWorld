using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyBaseManager : MonoBehaviour
{
    public static EnemyBaseManager Instance;

    [Header("Wave")]
    public EnemySurfaceUnit enemyPrefab;
    public int spawnCount = 3;
    public float spawnInterval = 0.75f;

    [Header("Enemy Pacing")]
    public bool useSurfaceUnitTempo = true;
    public float enemyBpm = 120f;
    [Range(0.5f, 0.95f)] public float enemyMoveRatio = 0.8f;

    public bool WaveActive => _waveActive;
    public int ActiveEnemyCount => _activeEnemies.Count;
    public int SpawnedCount => _spawnedCount;
    public int TargetSpawnCount => spawnCount;

    readonly List<EnemySurfaceUnit> _activeEnemies = new();
    Coroutine _spawnRoutine;
    bool _waveActive;
    int _spawnedCount;
    List<FaceNode> _currentPath;

    void Awake()
    {
        Instance = this;
    }

    public void BeginWave(List<FaceNode> path, SurfaceUnit tempoSource = null)
    {
        if (path == null || path.Count == 0)
        {
            Debug.LogWarning("[EnemyBaseManager] Cannot begin wave without a valid path.");
            return;
        }

        CancelWave();

        _currentPath = new List<FaceNode>(path);
        _spawnedCount = 0;
        _waveActive = true;

        if (useSurfaceUnitTempo && tempoSource != null)
        {
            enemyBpm = tempoSource.bpm;
            enemyMoveRatio = tempoSource.moveRatio;
        }

        _spawnRoutine = StartCoroutine(SpawnWave());
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
        _currentPath = null;
        _waveActive = false;
    }

    IEnumerator SpawnWave()
    {
        int count = Mathf.Max(0, spawnCount);
        float delay = Mathf.Max(0f, spawnInterval);

        while (_spawnedCount < count && _waveActive)
        {
            SpawnEnemy();
            _spawnedCount++;

            if (_spawnedCount < count && delay > 0f)
                yield return new WaitForSeconds(delay);
            else
                yield return null;
        }

        _spawnRoutine = null;
    }

    void SpawnEnemy()
    {
        if (_currentPath == null || _currentPath.Count == 0) return;

        EnemySurfaceUnit enemy;
        if (enemyPrefab != null)
        {
            enemy = Instantiate(enemyPrefab);
        }
        else
        {
            enemy = CreateDefaultEnemy();
        }

        var first = _currentPath[0];
        enemy.transform.position = GridSystem.instance.GridToWorld(first.cell)
                                   + first.normal * (GridSystem.instance.cellSize * 0.5f);
        enemy.OnReachedEnd += HandleEnemyReachedEnd;
        enemy.OnDied += HandleEnemyDied;
        _activeEnemies.Add(enemy);
        enemy.SetPath(_currentPath, enemyBpm, enemyMoveRatio);
    }

    EnemySurfaceUnit CreateDefaultEnemy()
    {
        var root = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        root.name = "EnemySurfaceUnit";
        root.transform.localScale = Vector3.one * 0.45f;

        var renderer = root.GetComponent<Renderer>();
        if (renderer != null)
            renderer.material.color = new Color(1f, 0.22f, 0.16f);

        return root.AddComponent<EnemySurfaceUnit>();
    }

    void HandleEnemyReachedEnd(EnemySurfaceUnit enemy)
    {
        Debug.Log("[EnemyBaseManager] Enemy reached endpoint.");
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

        if (_spawnRoutine == null && _activeEnemies.Count == 0 && _spawnedCount >= Mathf.Max(0, spawnCount))
            _waveActive = false;
    }
}
