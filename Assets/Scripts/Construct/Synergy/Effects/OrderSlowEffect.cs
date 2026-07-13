using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Order — slows enemies standing on the synergy's claimed cells. A coroutine
// re-applies a short EnemySlow each tick, so the slow lingers `refreshDuration`
// after stepping off rather than expiring mid-tick.
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Effects/Order Slow",
                 fileName = "OrderSlowEffect")]
public class OrderSlowEffect : GameEffect
{
    [Header("Slow")]
    [Tooltip("Speed enemies move at while on the synergy. 0.5 = half speed.")]
    [Range(0.05f, 1f)] public float speedMultiplier = 0.5f;

    [Tooltip("How often the synergy re-scans for enemies on it.")]
    [Min(0.02f)] public float tickInterval = 0.15f;

    [Tooltip("How long the slow lingers after an enemy steps off (should be ≥ a couple of ticks).")]
    [Min(0.05f)] public float refreshDuration = 0.4f;

    // Enemies currently standing on the synergy (as of the last tick) — live
    // number for the synergy HUD (HudSidePanels).
    public int AffectedEnemyCount { get; private set; }

    Coroutine        _routine;
    GameFlowManager  _runner;
    readonly HashSet<Vector3Int> _cells = new();

    public override void Apply(GameFlowManager game)
    {
        if (game == null) return;
        Stop();
        _runner  = game;
        _routine = game.StartCoroutine(Tick());
    }

    public override void Revoke(GameFlowManager game) => Stop();

    void Stop()
    {
        if (_routine != null && _runner != null) _runner.StopCoroutine(_routine);
        _routine = null;
        _runner  = null;
    }

    IEnumerator Tick()
    {
        var wait = new WaitForSeconds(tickInterval);
        while (true)
        {
            yield return wait;

            // Enemies only exist during combat — skip the scan otherwise.
            if (_runner == null || _runner.phase != GamePhase.Running) { AffectedEnemyCount = 0; continue; }

            SynergyEffectUtil.CollectClaimedCells(this, _cells);
            if (_cells.Count == 0) { AffectedEnemyCount = 0; continue; }

            var mgr = EnemyBaseManager.Instance;
            if (mgr == null) { AffectedEnemyCount = 0; continue; }

            var enemies = mgr.ActiveEnemies;
            int affected = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (e == null) continue;
                var cell = e.CurrentCell;
                if (cell.HasValue && _cells.Contains(cell.Value))
                {
                    EnemySlowEffect.Apply(e, refreshDuration, speedMultiplier);
                    affected++;
                }
            }
            AffectedEnemyCount = affected;
        }
    }
}
