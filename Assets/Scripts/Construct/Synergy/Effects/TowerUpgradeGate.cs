using System;
using System.Collections.Generic;
using UnityEngine;

// Gate for the turret-upgrade feature (启发 / Enlightenment unlock).
//
// Turrets start with ZERO upgrade actions allowed. An active Enlightenment
// cube grants some via a per-tier UnlockTowerUpgradeEffect instance (2×2×2 → 1,
// 3×3×3 → 2, 4×4×4 → 3, see EnlightenmentRule.perTierEffects). Each grant
// contributes its own value here; AllowedUpgrades is the LARGEST currently
// active grant (grants don't stack additively — the biggest cube you've got
// active sets the ceiling). TurretController checks AllowedUpgrades against
// its own TotalUpgradesUsed before allowing any upgrade click.
//
// Multiset (not a plain ref-count) so the tier-up pattern — Enlightenment
// REVOKES the old tier's effect then re-APPLIES the new one — composes
// correctly: Release(1) drops the tier-1 grant, Acquire(2) adds the tier-2
// one, and AllowedUpgrades recomputes to 2 without ever visibly dropping to 0
// in between.
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

    // (newAllowedUpgrades) — fires whenever the effective allowance changes.
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

    // Hard reset (e.g. new run). Safe to call any time.
    public static void ResetAll()
    {
        bool changed = _active.Count > 0;
        _active.Clear();
        if (changed) OnChanged?.Invoke(0);
    }
}
