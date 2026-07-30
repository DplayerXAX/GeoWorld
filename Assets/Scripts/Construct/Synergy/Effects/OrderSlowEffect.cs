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
    [Header("Slow (scales with connected block count)")]
    [Tooltip("Slow added per connected block in the Order component. e.g. 0.06 = each block adds 6% slow.")]
    [Range(0f, 0.5f)] public float slowPerBlock = 0.06f;

    [Tooltip("Hard cap on the slow fraction, no matter how many blocks connect. 0.5 = at most half speed.")]
    [Range(0f, 0.9f)] public float maxSlowFraction = 0.5f;

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

    // Slow fraction for a component of `blocks` connected pieces (capped). Shared
    // by the tick and the HUD row so both read the same curve.
    public float SlowFractionFor(int blocks) => Mathf.Min(maxSlowFraction, blocks * slowPerBlock);

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

            var ev  = SynergyEvaluator.Instance;
            var mgr = EnemyBaseManager.Instance;
            if (ev == null || mgr == null) { AffectedEnemyCount = 0; continue; }

            var enemies = mgr.ActiveEnemies;
            int affected = 0;

            // Per Order component: the bigger the connected block group, the
            // stronger the slow (capped at maxSlowFraction). Handled per-active
            // rather than pooled so two separate lines each slow by their OWN size.
            var actives = ev.Actives;
            for (int i = 0; i < actives.Count; i++)
            {
                var a = actives[i];
                if (a?.rule == null || a.rule.effect != this) continue;

                CollectActiveCells(a, _cells);
                if (_cells.Count == 0) continue;

                int blocks = SynergyEffectUtil.CountParticipatingPieces(a);
                float mult = Mathf.Clamp01(1f - SlowFractionFor(blocks));

                for (int j = 0; j < enemies.Count; j++)
                {
                    var e = enemies[j];
                    if (e == null) continue;
                    var cell = e.CurrentCell;
                    if (cell.HasValue && _cells.Contains(cell.Value))
                    {
                        EnemySlowEffect.Apply(e, refreshDuration, mult);
                        affected++;
                    }
                }
            }
            AffectedEnemyCount = affected;
        }
    }

    // Participating claimed cells for a SINGLE active synergy (respecting its
    // highlight filter) — the per-active counterpart to SynergyEffectUtil.CollectClaimedCells.
    static void CollectActiveCells(ActiveSynergy a, HashSet<Vector3Int> into)
    {
        into.Clear();
        if (a?.claimedPieces == null) return;
        var hc = a.highlightCells;
        foreach (var p in a.claimedPieces)
        {
            if (p?.cells == null) continue;
            for (int k = 0; k < p.cells.Length; k++)
            {
                var c = p.cells[k];
                if (hc != null && !hc.Contains(c)) continue;
                into.Add(c);
            }
        }
    }
}
