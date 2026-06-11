using UnityEngine;

// 启发 (Enlightenment) — unlocks the turret-upgrade feature while the cube holds.
//
// Assign to EnlightenmentRule.effect so it fires from tier 1 (2×2×2) upward — the
// rule's smallest activation is 2³, so any active Enlightenment unlocks it. The
// upgrade feature itself isn't built yet; this just flips TowerUpgradeGate, which
// the future UI will read.
//
// NOTE: Enlightenment is a tiered rule that REVOKES then re-APPLIES its effect on
// every tier-up (2³→3³→4³). The _held guard + ref-counted gate keep that a no-op
// in net (stays unlocked) instead of flickering the feature off.
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Effects/Unlock Tower Upgrade",
                 fileName = "UnlockTowerUpgradeEffect")]
public class UnlockTowerUpgradeEffect : GameEffect
{
    bool _held;

    public override void Apply(GameFlowManager game)
    {
        if (_held) return;
        TowerUpgradeGate.Acquire();
        _held = true;
        Debug.Log("[Enlightenment] Turret-upgrade feature UNLOCKED (2³ cube). UI not implemented yet.");
    }

    public override void Revoke(GameFlowManager game)
    {
        if (!_held) return;
        TowerUpgradeGate.Release();
        _held = false;
    }
}
