using System.Collections.Generic;
using UnityEngine;

// 启发 (Enlightenment) — grants turret upgrade actions while this TIER's cube
// holds. Turrets start with ZERO upgrades allowed; this is the only source of
// upgrade allowance. Assign ONE instance per tier into
// EnlightenmentRule.perTierEffects (index 0 = tier 1 / 2×2×2, index 1 = tier 2
// / 3×3×3, index 2 = tier 3 / 4×4×4), each with its own `allowedUpgrades`
// (1, 2, 3) — NOT the flat `effect` field, since the grant must vary by tier.
//
// NOTE: Enlightenment is a tiered rule that REVOKES then re-APPLIES its effect
// on every tier-up (2³→3³→4³) — since each tier is a SEPARATE asset instance
// here, that correctly reads as "release the old tier's grant, acquire the new
// tier's grant" on TowerUpgradeGate (a multiset — see its own comment), so the
// effective allowance steps 1→2→3 without ever visibly dropping to 0.
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Effects/Unlock Tower Upgrade",
                 fileName = "UnlockTowerUpgradeEffect")]
public class UnlockTowerUpgradeEffect : GameEffect
{
    [Tooltip("Turret upgrade actions granted while THIS tier's cube holds (e.g. 1 for the 2³ tier asset, 2 for 3³, 3 for 4³).")]
    [Min(0)] public int allowedUpgrades = 1;

    bool _held;

    // Tracked so ResetAllHeld() can clear a stale _held left over from a run
    // that ended (scene unloaded) while this tier's cube was still active —
    // ScriptableObject assets persist across scene loads, a plain instance
    // field does not reset itself between runs.
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
