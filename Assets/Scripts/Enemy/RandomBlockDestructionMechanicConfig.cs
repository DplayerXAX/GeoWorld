using UnityEngine;

// Periodically destroys random placed blocks after combat (minPerTrigger–maxPerTrigger at a time). Add an
// instance of this asset to LevelDefinition.mechanics to enable it for a level.
[CreateAssetMenu(menuName = "GeoWorld/Level Mechanics/Random Block Destruction",
                 fileName = "RandomBlockDestructionMechanic")]
public class RandomBlockDestructionMechanicConfig : LevelMechanicConfig
{
    [Tooltip("First completed turn that triggers destruction. 2 means the first block is destroyed after wave 2, at the start of the next Build phase.")]
    [Min(1)] public int firstTriggerAfterTurn = 2;

    [Tooltip("Completed turns between destructions. 2 triggers after turns 2, 4, 6, ... when firstTriggerAfterTurn is also 2.")]
    [Min(1)] public int turnInterval = 2;

    [Tooltip("Fewest blocks destroyed each time it triggers.")]
    [Min(1)] public int minPerTrigger = 1;

    [Tooltip("Most blocks destroyed each time it triggers (rolled between min and max, inclusive).")]
    [Min(1)] public int maxPerTrigger = 1;

    [Tooltip("Seconds a destroyed block takes to dissolve away (purely visual — it is off the board at once).")]
    [Min(0.05f)] public float dissolveSeconds = 1.4f;

    [Tooltip("Allow a placed turret itself to be selected as the destroyed block.")]
    public bool canDestroyTurrets = true;

    [Tooltip("Skip blocks whose removal would leave another turret with no adjacent support. Keeps the same board invariant as normal pickup and selling.")]
    public bool preserveTurretSupport = true;
}
