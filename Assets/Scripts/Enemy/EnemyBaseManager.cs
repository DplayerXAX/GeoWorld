using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyBaseManager : MonoBehaviour
{
    public static EnemyBaseManager Instance;

    [Header("Wave (fallback when no WaveDefinition is supplied)")]
    [Tooltip("Default enemy prefab used by the legacy spawn path (no SpawnGroup list supplied) AND by any SpawnGroup that has a null prefab. With the prefab-only enemy design this field is REQUIRED — leaving it empty will skip spawns and log a warning.")]
    public EnemySurfaceUnit enemyPrefab;
    public int spawnCount = 3;
    public float spawnInterval = 0.75f;

    [Header("Balance")]
    [Tooltip("Central balance asset. When set, enemy stats (maxHealth, speedMultiplier, rewardOnKill) are looked up by matching SpawnGroup.prefab against BalanceTable.enemies. Leave empty to use prefab/Inspector defaults.")]
    public BalanceTable balance;

    [Header("Visual material injection (for prefabs with empty material slots)")]
    [Tooltip("Extra world-space distance the enemy floats above each block face (added on top of 0.5×cellSize). Propagated to EnemySurfaceUnit.faceClearance on every spawn.")]
    public float enemyFaceClearance = 0.25f;
    [Tooltip("Default shard material — injected into any prefab whose EnemyChaoticVisual.shardMaterial is empty. Use a flat-color URP Unlit/Lit material; each shard is tinted per-instance via MPB.")]
    public Material enemyShardMaterial;
    [Tooltip("Default accent (sparkle) material — injected when prefab's accent slot is empty. Falls back to enemyShardMaterial when null.")]
    public Material enemyAccentMaterial;
    [Tooltip("Default outline material — injected when prefab's BlockOutlineApplier.outlineMaterial is empty. Null here AND on prefab = runtime fallback (lazy-built GeoWorld/BlockOutline material with a dark default).")]
    public Material enemyOutlineMaterial;

    // Fired once when the last enemy is gone AND spawning has finished.
    // GameFlowManager listens to know when to transition back to Build.
    public event Action OnWaveCompleted;

    IList<SpawnGroup> _currentGroups;
    int _spawnTotal;

    [Header("Enemy Pacing")]
    public bool useSurfaceUnitTempo = true;
    [Tooltip("Wave-global movement BPM. 60/bpm = seconds per cell. 70 → 0.86s/cell (slower, more time for turrets to engage). 120 → 0.5s/cell (frantic).")]
    public float enemyBpm = 70f;
    [Range(0.5f, 0.95f)] public float enemyMoveRatio = 0.8f;

    public bool WaveActive => _waveActive;
    public int ActiveEnemyCount => _activeEnemies.Count;
    public IReadOnlyList<EnemySurfaceUnit> ActiveEnemies => _activeEnemies;
    public int SpawnedCount => _spawnedCount;
    public int TargetSpawnCount => _spawnTotal > 0 ? _spawnTotal : spawnCount;

    readonly List<EnemySurfaceUnit> _activeEnemies = new();
    Coroutine _spawnRoutine;
    bool _waveActive;
    int _spawnedCount;

    // One path per active START point. Each spawn "tick" emits one enemy at
    // EVERY route, so every spawn point runs the FULL wave (total enemies =
    // wave size × spawn-point count) instead of the wave being split across
    // points. Single-path waves are just a one-element list.
    List<List<FaceNode>> _currentPaths;

    void Awake()
    {
        Instance = this;
    }

    public void BeginWave(List<FaceNode> path, SurfaceUnit tempoSource = null /* legacy */)
        => BeginWave(path, (IList<SpawnGroup>)null, tempoSource);

    public void BeginWave(List<FaceNode> path, IList<SpawnGroup> groups, SurfaceUnit tempoSource = null)
        => BeginWave(path != null ? new List<List<FaceNode>> { path } : null, groups, tempoSource);

    // Multi-path overload: each entry is an independent start→end route. Every
    // spawn tick emits one enemy at EACH route, so each spawn point runs the
    // full wave. Pass a single-element list for classic one-spawn behavior.
    public void BeginWave(IList<List<FaceNode>> paths, IList<SpawnGroup> groups, SurfaceUnit tempoSource = null)
    {
        // Keep only valid (non-empty) routes.
        var valid = new List<List<FaceNode>>();
        if (paths != null)
            foreach (var p in paths)
                if (p != null && p.Count > 0)
                    valid.Add(new List<FaceNode>(p));

        if (valid.Count == 0)
        {
            Debug.LogWarning("[EnemyBaseManager] Cannot begin wave without a valid path.");
            return;
        }

        CancelWave();

        _currentPaths  = valid;
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
        _currentPaths  = null;
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
        if (_currentPaths == null || _currentPaths.Count == 0) return;

        EnemySurfaceUnit source = prefabOverride != null ? prefabOverride : enemyPrefab;

        if (source == null)
        {
            // Prefab-only design: no procedural fallback. If a SpawnGroup has
            // null prefab AND enemyPrefab is also empty, this spawn is skipped
            // with a clear warning so the missing slot is obvious in the log.
            Debug.LogWarning("[EnemyBaseManager] SpawnEnemy skipped — no prefab on the SpawnGroup and no enemyPrefab fallback assigned on this manager. Drag a prefab into either BalanceTable.enemies[N].prefab or EnemyBaseManager.enemyPrefab.");
            return;
        }

        // Full wave from EVERY spawn point: emit one enemy at each active route
        // this tick. Skip any route that went empty mid-wave (block lifted).
        for (int i = 0; i < _currentPaths.Count; i++)
        {
            var path = _currentPaths[i];
            if (path != null && path.Count > 0)
                SpawnOneEnemy(source, path);
        }
    }

    // Instantiates and launches a single enemy of `source` along `path`.
    void SpawnOneEnemy(EnemySurfaceUnit source, List<FaceNode> path)
    {
        EnemySurfaceUnit enemy = Instantiate(source);
        // Prefabs can be lean: drop in EnemyChaoticVisual + BlockOutlineApplier
        // with empty material slots and we fill them with the manager's
        // defaults here. Each prefab MAY override materials individually
        // (e.g. elite uses gold outline) — only nulls get injected.
        InjectVisualMaterials(enemy);

        enemy.faceClearance = enemyFaceClearance;

        // Apply BalanceTable overrides if a matching EnemyRecord exists for
        // this prefab. We write through the ENEMY'S CANONICAL FIELDS
        // (baseSpeedMultiplier, targetPriority, …) so other systems —
        // WaveGenerator.BiasTauntAheadOfFast, debuff stacking, etc. — still
        // see consistent values. Falls back to whatever the prefab itself
        // was authored with when no record matches.
        var record = LookupBalanceRecord(source);
        if (record != null)
        {
            enemy.SetMaxHealth(record.maxHealth);
            enemy.rewardOnKill = record.rewardOnKill;
            enemy.baseSpeedMultiplier = Mathf.Max(0.01f, record.speedMultiplier);
            enemy.targetPriority      = record.targetPriority;
        }

        var first = path[0];
        enemy.transform.position = GridSystem.instance.GridToWorld(first.cell)
                                   + first.normal * (GridSystem.instance.cellSize * 0.5f + enemyFaceClearance);
        enemy.OnReachedEnd += HandleEnemyReachedEnd;
        enemy.OnDied += HandleEnemyDied;
        _activeEnemies.Add(enemy);
        // Wave bpm is the global tempo — per-enemy speed variation is via
        // baseSpeedMultiplier above, NOT by multiplying bpm here (would
        // desync the on-beat movement feel for the whole wave).
        enemy.SetPath(path, enemyBpm, enemyMoveRatio);
    }

    // Re-route every live enemy from its CURRENT face to the end — called when the
    // grid changes mid-combat (a new block was placed), so blocked enemies find a
    // new way instead of walking into the wall.
    public void RerouteActive(SurfaceGraphBuilder graph, List<FaceNode> endFaces)
    {
        if (graph == null || endFaces == null || endFaces.Count == 0) return;

        foreach (var e in _activeEnemies)
        {
            if (e == null || e.CurrentHealth <= 0 || e.CurrentCell == null) continue;

            var startFace = graph.GetFaceNode(e.CurrentCell.Value, e.CurrentNormal ?? default);
            if (startFace == null) continue;

            var path = SurfacePathfinding.FindPath(new List<FaceNode> { startFace }, endFaces);
            if (path != null && path.Count > 0)
                e.SetPath(path, enemyBpm, enemyMoveRatio);
        }
    }

    // Fills empty material slots on a freshly-instantiated prefab so a
    // single set of materials (enemyOutlineMaterial / enemyShardMaterial /
    // enemyAccentMaterial) on this manager applies to every enemy type
    // without each prefab needing them dragged in.
    void InjectVisualMaterials(EnemySurfaceUnit enemy)
    {
        if (enemy == null) return;

        // Outline material (uses runtime fallback if manager slot is empty too).
        var applier = enemy.GetComponent<BlockOutlineApplier>();
        if (applier != null && applier.outlineMaterial == null)
        {
            applier.outlineMaterial = enemyOutlineMaterial != null
                ? enemyOutlineMaterial
                : GetRuntimeOutlineFallback();
        }

        // Shard / accent materials — EnemyChaoticVisual already has its own
        // runtime-Unlit fallback, but pulling from the manager keeps a
        // single source of truth.
        var visual = enemy.GetComponent<EnemyChaoticVisual>();
        if (visual != null)
        {
            if (visual.shardMaterial  == null) visual.shardMaterial  = enemyShardMaterial;
            if (visual.accentMaterial == null) visual.accentMaterial = enemyAccentMaterial;
        }
    }

    // Linear scan over balance.enemies — list is tiny (5-10 archetypes), so
    // no need for a hash map. Returns null if no balance, no list, or no
    // matching prefab → caller should keep prefab/Inspector defaults.
    BalanceTable.EnemyRecord LookupBalanceRecord(EnemySurfaceUnit prefab)
    {
        if (balance == null || balance.enemies == null || prefab == null) return null;
        for (int i = 0; i < balance.enemies.Count; i++)
        {
            var r = balance.enemies[i];
            if (r != null && r.prefab == prefab) return r;
        }
        return null;
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

    void HandleEnemyReachedEnd(EnemySurfaceUnit enemy)
    {
        BackgroundReactor.Instance?.TriggerDamageFlash();
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
        // Grant BLOCK currency for the kill. Design split:
        //   • Turret currency  ← combat-engagement income (per-block-pass,
        //                        wave-complete bonus, in-combat regen)
        //   • Block currency   ← combat-success income (kill rewards) so
        //                        clearing waves feeds the building budget.
        if (enemy != null && enemy.rewardOnKill > 0 && ResourceManager.Instance != null)
        {
            ResourceManager.Instance.AddBlockCurrency(enemy.rewardOnKill);
            CurrencyFlyFx.Fly(enemy.transform.position, isTurret: false, enemy.rewardOnKill);
        }

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
