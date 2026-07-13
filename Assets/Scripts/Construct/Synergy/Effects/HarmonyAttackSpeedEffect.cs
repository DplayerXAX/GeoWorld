using System.Collections.Generic;
using UnityEngine;

// 和谐 (Harmony) — turrets touching the synergy fire faster.
//
// Turrets are colorless blocks (never part of a color claim), so "on the
// synergy" means a turret block that is face-adjacent to any claimed cell —
// sitting on top of, or right beside, the Harmony structure. While active, each
// qualifying turret gets a reversible fire-rate multiplier; turrets that leave
// the set (or the whole synergy on revoke) are restored to normal.
//
// EVENT-DRIVEN (no per-frame polling): the buffed-turret set is only reconciled
// when something that could change it happens —
//   • a block is (re)placed                 → PlacementController.BlockPlaced
//   • the Harmony claim grows/shrinks/moves  → SynergyEvaluator.OnClaimChanged
//   • the synergy activates / deactivates    → Apply / Revoke
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Effects/Harmony Attack Speed",
                 fileName = "HarmonyAttackSpeedEffect")]
public class HarmonyAttackSpeedEffect : GameEffect
{
    [Header("Attack speed")]
    [Tooltip("Fire-rate bonus for turrets touching the synergy. 0.3 = +30% attack speed.")]
    [Min(0f)] public float attackSpeedBonus = 0.3f;

    [Header("Visual")]
    [Tooltip("Aura colour shown on turrets the synergy is speeding up.")]
    public Color auraColor = new Color(0.4f, 1f, 0.8f);

    // Turrets currently getting the fire-rate bonus — live number for the
    // synergy HUD (HudSidePanels).
    public int BuffedTurretCount => _buffed.Count;

    readonly HashSet<Vector3Int>       _cells   = new();
    readonly HashSet<TurretController> _buffed  = new();
    readonly HashSet<TurretController> _desired = new();
    readonly List<TurretController>    _scratch = new();
    readonly Dictionary<TurretController, SynergyAura> _auras = new();

    bool _subscribed;

    static readonly Vector3Int[] _neighbors =
    {
        Vector3Int.up, Vector3Int.down, Vector3Int.left,
        Vector3Int.right, Vector3Int.forward, Vector3Int.back,
    };

    public override void Apply(GameFlowManager game)
    {
        Subscribe();
        ReconcileNow();
    }

    public override void Revoke(GameFlowManager game)
    {
        Unsubscribe();
        RestoreAll();
    }

    void Subscribe()
    {
        if (_subscribed) return;
        _subscribed = true;
        PlacementController.BlockPlaced += OnBlockPlaced;
        var ev = SynergyEvaluator.Instance;
        if (ev != null) ev.OnClaimChanged += OnClaimChanged;
    }

    void Unsubscribe()
    {
        if (!_subscribed) return;
        _subscribed = false;
        PlacementController.BlockPlaced -= OnBlockPlaced;
        var ev = SynergyEvaluator.Instance;
        if (ev != null) ev.OnClaimChanged -= OnClaimChanged;
    }

    // Reconcile only when something relevant changed.
    void OnBlockPlaced(BlockData data, Vector3Int[] cells)        => ReconcileNow();
    void OnClaimChanged(SynergyRule rule, ActiveSynergy active)   => ReconcileNow();

    void ReconcileNow() => Reconcile(1f + Mathf.Max(0f, attackSpeedBonus));

    void RestoreAll()
    {
        foreach (var t in _buffed)
            if (t != null) t.SetSynergyFireRateMultiplier(1f);
        _buffed.Clear();

        foreach (var kv in _auras) if (kv.Value != null) kv.Value.Remove();
        _auras.Clear();
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

        // Buff newcomers + give them a speed aura.
        float size = grid.cellSize * 1.1f;
        foreach (var t in _desired)
            if (t != null && _buffed.Add(t))
            {
                t.SetSynergyFireRateMultiplier(mult);
                _auras[t] = SynergyBuffFx.AttachAura(t.transform, auraColor, size);
            }

        // Restore turrets that left the set (or were destroyed) + drop their aura.
        _scratch.Clear();
        foreach (var t in _buffed)
            if (t == null || !_desired.Contains(t)) _scratch.Add(t);
        for (int i = 0; i < _scratch.Count; i++)
        {
            var t = _scratch[i];
            _buffed.Remove(t);
            if (t != null) t.SetSynergyFireRateMultiplier(1f);
            if (_auras.TryGetValue(t, out var aura))
            {
                if (aura != null) aura.Remove();
                _auras.Remove(t);
            }
        }
    }
}
