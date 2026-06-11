using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 和谐 (Harmony) — turrets touching the synergy fire faster.
//
// Turrets are colorless blocks (never part of a color claim), so "on the
// synergy" means a turret block that is face-adjacent to any claimed cell —
// sitting on top of, or right beside, the Harmony structure. While active, a
// coroutine reconciles which turrets qualify and grants each a reversible
// fire-rate multiplier; turrets that leave the set (or the whole synergy on
// revoke) are restored to normal.
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Effects/Harmony Attack Speed",
                 fileName = "HarmonyAttackSpeedEffect")]
public class HarmonyAttackSpeedEffect : GameEffect
{
    [Header("Attack speed")]
    [Tooltip("Fire-rate bonus for turrets touching the synergy. 0.3 = +30% attack speed.")]
    [Min(0f)] public float attackSpeedBonus = 0.3f;

    [Tooltip("How often the buffed-turret set is reconciled.")]
    [Min(0.1f)] public float reconcileInterval = 0.4f;

    Coroutine       _routine;
    GameFlowManager _runner;
    readonly HashSet<Vector3Int>      _cells   = new();
    readonly HashSet<TurretController> _buffed  = new();
    readonly HashSet<TurretController> _desired = new();
    readonly List<TurretController>    _scratch = new();

    static readonly Vector3Int[] _neighbors =
    {
        Vector3Int.up, Vector3Int.down, Vector3Int.left,
        Vector3Int.right, Vector3Int.forward, Vector3Int.back,
    };

    public override void Apply(GameFlowManager game)
    {
        if (game == null) return;
        Stop(restore: false);
        _runner  = game;
        _routine = game.StartCoroutine(Tick());
    }

    public override void Revoke(GameFlowManager game) => Stop(restore: true);

    void Stop(bool restore)
    {
        if (_routine != null && _runner != null) _runner.StopCoroutine(_routine);
        _routine = null;
        _runner  = null;

        if (restore)
            foreach (var t in _buffed)
                if (t != null) t.SetSynergyFireRateMultiplier(1f);
        _buffed.Clear();
    }

    IEnumerator Tick()
    {
        var wait = new WaitForSeconds(reconcileInterval);
        while (true)
        {
            Reconcile(1f + Mathf.Max(0f, attackSpeedBonus));
            yield return wait;
        }
    }

    void Reconcile(float mult)
    {
        var grid = GridSystem.instance;
        if (grid == null) return;

        SynergyEffectUtil.CollectClaimedCells(this, _cells);

        // Desired = every turret block face-adjacent to a claimed cell.
        _desired.Clear();
        foreach (var cell in _cells)
        {
            for (int d = 0; d < _neighbors.Length; d++)
            {
                var ins = grid.GetInstanceAt(cell + _neighbors[d]);
                if (ins?.visualObject == null || ins.data == null) continue;
                if (!TurretTypes.Is(ins.data.blockType)) continue;
                var tc = ins.visualObject.GetComponentInChildren<TurretController>();
                if (tc != null) _desired.Add(tc);
            }
        }

        // Buff newcomers.
        foreach (var t in _desired)
            if (t != null && _buffed.Add(t)) t.SetSynergyFireRateMultiplier(mult);

        // Restore turrets that left the set (or were destroyed).
        _scratch.Clear();
        foreach (var t in _buffed)
            if (t == null || !_desired.Contains(t)) _scratch.Add(t);
        for (int i = 0; i < _scratch.Count; i++)
        {
            var t = _scratch[i];
            _buffed.Remove(t);
            if (t != null) t.SetSynergyFireRateMultiplier(1f);
        }
    }
}
