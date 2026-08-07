using UnityEngine;

// Periodically destroys one random player-placed block after combat. Add an
// instance of this asset to LevelDefinition.mechanics to enable it for a level.
[CreateAssetMenu(menuName = "GeoWorld/Level Mechanics/Random Block Destruction",
                 fileName = "RandomBlockDestructionMechanic")]
public class RandomBlockDestructionMechanicConfig : LevelMechanicConfig
{
    [Tooltip("First completed turn that triggers destruction. 2 means the first block is destroyed after wave 2, at the start of the next Build phase.")]
    [Min(1)] public int firstTriggerAfterTurn = 2;

    [Tooltip("Completed turns between destructions. 2 triggers after turns 2, 4, 6, ... when firstTriggerAfterTurn is also 2.")]
    [Min(1)] public int turnInterval = 2;

    [Tooltip("Allow a placed turret itself to be selected as the destroyed block.")]
    public bool canDestroyTurrets = true;

    [Tooltip("Skip blocks whose removal would leave another turret with no adjacent support. Keeps the same board invariant as normal pickup and selling.")]
    public bool preserveTurretSupport = true;
}
