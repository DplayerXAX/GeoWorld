using System;
using System.Collections.Generic;
using UnityEngine;

// Gate for the turret-upgrade feature (Enlightenment unlock).
//
// Turrets start with zero upgrade actions allowed. An active Enlightenment
// cube grants some via a per-tier UnlockTowerUpgradeEffect (see
// EnlightenmentRule.perTierEffects). AllowedUpgrades is the LARGEST currently
// active grant, not a sum — the biggest active cube sets the ceiling.
//
// A multiset (not a plain ref-count) so tier-up — revoke old, apply new —
// composes correctly without the allowance ever visibly dropping to 0.
public static class TowerUpgradeGate
{
    static readonly List<int> _active = new();

    public static int AllowedUpgrades
    {
        get
        {
            int max = 0;
            for (int i = 0; i < _active.Count; i++)
                if (_active[i] > max) max = _active[i];
            return max;
        }
    }

    public static bool Unlocked => AllowedUpgrades > 0;

    // Fires with the new AllowedUpgrades whenever it changes.
    public static event Action<int> OnChanged;

    public static void Acquire(int upgrades)
    {
        int before = AllowedUpgrades;
        _active.Add(Mathf.Max(0, upgrades));
        if (AllowedUpgrades != before) OnChanged?.Invoke(AllowedUpgrades);
    }

    public static void Release(int upgrades)
    {
        int before = AllowedUpgrades;
        _active.Remove(upgrades);   // removes ONE matching entry
        if (AllowedUpgrades != before) OnChanged?.Invoke(AllowedUpgrades);
    }

    // Hard reset for a new run.
    public static void ResetAll()
    {
        bool changed = _active.Count > 0;
        _active.Clear();
        if (changed) OnChanged?.Invoke(0);
    }
}
