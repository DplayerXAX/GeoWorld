using UnityEngine;

// Enlightenment Shrine mechanic (see ShrineController): a shrine sprouts each
// round on a free cell touching the build and grants every adjacent turret a
// reversible free upgrade. Add an instance of this asset to a
// LevelDefinition.mechanics list to enable it for that level.
[CreateAssetMenu(menuName = "GeoWorld/Level Mechanics/Enlightenment Shrine", fileName = "ShrineMechanic")]
public class ShrineMechanicConfig : LevelMechanicConfig
{
    [Tooltip("First wave shrines are allowed to start sprouting. Uses the same 'which wave is this' counter as TutorialStep.requiredWave / ChaosBlockMechanicConfig.startWave (GameFlowManager.UpcomingWaveNumber).")]
    [Min(1)] public int startWave = 1;

    [Tooltip("Hard cap on simultaneously-alive shrines.")]
    [Min(1)] public int maxSimultaneous = 3;

    [Tooltip("Attack-speed bonus granted to turrets touching a shrine. 0.33 ≈ one Basic upgrade tier's worth.")]
    [Min(0f)] public float fireRateBonus = 0.33f;

    [Tooltip("Damage bonus granted to turrets touching a shrine. 0.33 ≈ one Basic upgrade tier's worth.")]
    [Min(0f)] public float damageBonus = 0.33f;
}
