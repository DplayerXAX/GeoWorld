using System.Collections.Generic;
using UnityEngine;

// Enlightenment — grants turret upgrade actions while this tier's cube holds.
// Turrets start with zero upgrades allowed; this is the only source. Assign
// one instance per tier into EnlightenmentRule.perTierEffects with its own
// `allowedUpgrades` (1, 2, 3), not the flat `effect` field.
//
// Enlightenment revokes then re-applies on every tier-up; since each tier is
// a separate asset instance, that reads as release-old/acquire-new on
// TowerUpgradeGate's multiset, stepping the allowance 1->2->3 without ever
// visibly dropping to 0.
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Effects/Unlock Tower Upgrade",
                 fileName = "UnlockTowerUpgradeEffect")]
public class UnlockTowerUpgradeEffect : GameEffect
{
    [Tooltip("Turret upgrade actions granted while THIS tier's cube holds (e.g. 1 for the 2³ tier asset, 2 for 3³, 3 for 4³).")]
    [Min(0)] public int allowedUpgrades = 1;

    bool _held;

    // Tracked so ResetAllHeld() can clear stale _held from a run that ended
    // mid-hold — ScriptableObject assets persist across scene loads.
    static readonly List<UnlockTowerUpgradeEffect> _instances = new();

    void OnEnable()  { if (!_instances.Contains(this)) _instances.Add(this); }
    void OnDisable() { _instances.Remove(this); }

    public static void ResetAllHeld()
    {
        for (int i = 0; i < _instances.Count; i++)
            if (_instances[i] != null) _instances[i]._held = false;
    }

    public override void Apply(GameFlowManager game)
    {
        if (_held) return;
        TowerUpgradeGate.Acquire(allowedUpgrades);
        _held = true;
    }

    public override void Revoke(GameFlowManager game)
    {
        if (!_held) return;
        TowerUpgradeGate.Release(allowedUpgrades);
        _held = false;
    }
}
