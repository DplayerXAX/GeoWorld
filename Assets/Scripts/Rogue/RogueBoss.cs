using UnityEngine;

// The roguelite's bosses: the Watcher (WatcherModel) as a WALKER.
//
// The level boss (BossController) perches over the board and fires a beam from the
// air; here it is an ordinary wave enemy that happens to be enormous — it takes the
// road the wave takes, the turrets shoot it like anything else, and if it reaches
// the end it costs lives. That keeps it inside every rule the player already knows,
// and it makes "kill it before the core" the whole question.
//
// It is built ONCE, as an inactive template. EnemyBaseManager instantiates the
// template per spawn (like any prefab) and then activates the clone, so a clone is
// fully configured — hit points, speed, size — before its own Awake and Update run.
public static class RogueBoss
{
    static EnemySurfaceUnit _template;

    public static EnemySurfaceUnit Template()
    {
        if (_template != null) return _template;

        float cs = GridSystem.instance != null ? GridSystem.instance.cellSize : 1f;
        float scale = Mathf.Max(0.5f, RogueRun.Config.bossScale);

        var go = new GameObject("RogueBossTemplate");
        go.SetActive(false);

        var unit = go.AddComponent<EnemySurfaceUnit>();
        unit.maxHealth = 100;                 // the real value comes from RogueWaves.ApplySpawn
        unit.baseSpeedMultiplier = 0.55f;
        unit.rewardOnKill = 0;
        unit.targetPriority = 0;

        // The model is authored in units of one body; the root is scaled up by
        // ApplySpawn (stats.scale), so the legs are cut to reach the road at that scale.
        float faceOffset = 0.75f * cs;        // 0.5 cell + the manager's faceClearance
        var model = WatcherModel.Build(go.transform, cs, faceOffset / scale, null);
        if (model != null) model.autoTick = true;

        // Bullets find enemies by trigger collider.
        var col = go.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 0.55f * cs;

        go.AddComponent<RogueBossBehaviour>();
        _template = unit;
        return unit;
    }
}

// What a boss does beyond walking: every so often it blinds the nearest synergy for
// a few seconds, with the same beam the level boss uses. A defence that leans on one
// synergy has to be able to carry on without it.
[DisallowMultipleComponent]
public class RogueBossBehaviour : MonoBehaviour
{
    EnemySurfaceUnit _unit;
    ActiveSynergy _blinded;
    float _until, _timer;

    void Awake()
    {
        _unit = GetComponent<EnemySurfaceUnit>();
        _timer = Mathf.Max(1f, RogueRun.Config.bossAbilityInterval * 0.6f);
    }

    void Update()
    {
        if (_unit == null || _unit.CurrentHealth <= 0) { Release(); return; }

        if (_blinded != null && Time.time >= _until) Release();

        _timer -= Time.deltaTime;
        if (_timer > 0f) return;
        _timer = Mathf.Max(2f, RogueRun.Config.bossAbilityInterval);
        Blind();
    }

    void Blind()
    {
        var ev   = SynergyEvaluator.Instance;
        var grid = GridSystem.instance;
        if (ev == null || grid == null) return;

        ActiveSynergy best = null; float bestD = float.MaxValue; Vector3 bestAt = default;
        foreach (var a in ev.Actives)
        {
            if (a == null || a.Suppressed || a.claimedPieces == null || a.claimedPieces.Count == 0) continue;
            Vector3 sum = Vector3.zero; int n = 0;
            foreach (var p in a.claimedPieces)
                if (p?.cells != null) foreach (var c in p.cells) { sum += grid.GridToWorld(c); n++; }
            if (n == 0) continue;
            Vector3 at = sum / n;
            float d = (at - transform.position).sqrMagnitude;
            if (d < bestD) { bestD = d; best = a; bestAt = at; }
        }
        if (best == null) return;

        TurretShotFx.Beam(transform.position, bestAt);
        ev.SetSuppressed(best, true);
        _blinded = best;
        _until = Time.time + Mathf.Max(1f, RogueRun.Config.bossAbilityDuration);
        PlacementController.Instance?.ShowPlacementPopup(
            $"The Watcher blinds {(best.rule != null ? best.rule.displayName : "a synergy")}!");
    }

    void Release()
    {
        if (_blinded == null) return;
        SynergyEvaluator.Instance?.SetSuppressed(_blinded, false);
        _blinded = null;
    }

    void OnDestroy() => Release();
}
