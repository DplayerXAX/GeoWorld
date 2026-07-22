using UnityEngine;

// Chaos Block mechanic (see ChaosBlockController): a black obstacle spawns on
// the build grid every round, kept some distance from existing blocks so the
// player has to build/turret toward it. Add an instance of this asset to a
// LevelDefinition.mechanics list to enable it for that level.
[CreateAssetMenu(menuName = "GeoWorld/Level Mechanics/Chaos Block", fileName = "ChaosBlockMechanic")]
public class ChaosBlockMechanicConfig : LevelMechanicConfig
{
    [Tooltip("First wave chaos blocks are allowed to start spawning (1 = from the very first round). Uses the same 'which wave is this' counter as TutorialStep.requiredWave (GameFlowManager.UpcomingWaveNumber) — e.g. 3 means no chaos blocks during waves 1-2, then they start from wave 3 onward.")]
    [Min(1)] public int startWave = 1;

    [Tooltip("Chaos block health (turret damage to destroy it).")]
    [Min(1)] public int health = 20;

    [Tooltip("Block currency drained EACH TIME combat ends while a given chaos block is still alive. Multiple surviving blocks each drain separately.")]
    [Min(0)] public int currencyDrain = 5;

    [Tooltip("Minimum grid-cell distance kept from the nearest existing occupied cell when picking a spawn point — the whole point is that the player must build/reach toward it, not have it spawn next to an existing turret.")]
    [Min(1)] public int minDistance = 4;

    [Tooltip("MAXIMUM grid-cell distance from the nearest existing occupied cell. Without an upper bound the spawn drifts further and further out as the build grows, until it's out of turret range entirely and the drain becomes unkillable. Keep it within reach of a turret placed near the build's edge.")]
    [Min(1)] public int maxDistance = 7;

    [Tooltip("Legacy: how far beyond the build's bounding box the old box-sampling search could reach. Spawn points are now sampled around a random existing block instead (see ChaosBlockController.PickSpawnCell), so this is only a fallback clamp.")]
    [Min(1)] public int searchMargin = 6;

    [Tooltip("Hard cap on simultaneously-alive chaos blocks, however many rounds the player lets them pile up.")]
    [Min(1)] public int maxSimultaneous = 6;
}
