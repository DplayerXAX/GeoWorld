using UnityEngine;

// Boss mechanic (see BossController): one large enemy that perches ON the board's
// blocks and stays across rounds until it is destroyed. Each round it teleports to
// its next perch (marked in advance, so the player can set turrets where it will
// land); during combat it charges and fires a laser at a synergy set — a hit
// switches that synergy off, but any block standing between the boss's eye and
// the set takes the beam instead. Add an instance of this asset to a
// LevelDefinition.mechanics list to put the boss in that level; pair it with a
// DefeatBoss objective so killing it clears the level.
[CreateAssetMenu(menuName = "GeoWorld/Level Mechanics/Boss", fileName = "BossMechanic")]
public class BossMechanicConfig : LevelMechanicConfig
{
    [Header("Presence")]
    public string displayName = "The Watcher";
    [Tooltip("Wave the boss arrives on (UpcomingWaveNumber, the same counter TutorialStep.requiredWave uses).")]
    [Min(1)] public int startWave = 1;
    [Min(1)] public int health = 600;
    [Tooltip("Size of the boss body, in cells.")]
    [Range(1f, 4f)] public float size = 2f;
    [Tooltip("How high above its perch block the boss floats, in cells.")]
    public float hover = 1.4f;

    [Header("Perches")]
    [Tooltip("ON: the level lays down 2×2 pads (Heresy-coloured, fixed) for the boss to move between — the next one glows. OFF: it picks block tops on the player's own build instead (perchCount of them).")]
    public bool usePads = true;
    [Range(2, 6)] public int padCount = 3;
    [Tooltip("How far the pads stand from the midpoint between the first start and end points, in cells.")]
    [Min(2f)] public float padRadius = 6f;
    [Tooltip("The pads' colour (alpha 0 = the Heresy colour).")]
    public Color padColor = new(0f, 0f, 0f, 0f);
    [ColorUsage(false, true)] public Color padGlow = new(1.4f, 0.7f, 1.8f, 1f);
    [Tooltip("Without pads: how many perches it moves between. Chosen on arrival, spread across the build — block tops with open air above.")]
    [Range(2, 8)] public int perchCount = 4;
    [Tooltip("Teleport to the next perch at the start of every build phase.")]
    public bool teleportEachRound = true;
    [Tooltip("Also teleport mid-combat every this many seconds. 0 = only between rounds.")]
    [Min(0f)] public float teleportInCombatSeconds = 0f;

    [Header("Laser")]
    [Tooltip("Seconds of combat between laser shots.")]
    [Min(1f)] public float laserInterval = 9f;
    [Tooltip("Seconds the aim line shows before it fires — the player's window to see what it's aiming at.")]
    [Min(0.2f)] public float laserCharge = 1.8f;
    [Tooltip("How far it can see, in cells. Synergies further away are safe.")]
    [Min(4f)] public float laserRange = 30f;
    [Tooltip("How long a severed synergy stays off, in seconds. 0 = until the current wave ends.")]
    [Min(0f)] public float suppressSeconds = 0f;
    [Tooltip("A block that takes the beam for a synergy is destroyed. Off = it just absorbs the hit.")]
    public bool blockerDestroyed = false;

    [Header("Look")]
    public Color bodyColor  = new(0.14f, 0.06f, 0.2f, 1f);
    public Color ringColor  = new(0.62f, 0.24f, 0.92f, 1f);
    public Color eyeColor   = new(1f, 0.25f, 0.3f, 1f);
    [ColorUsage(true, true)] public Color laserColor = new(2.2f, 0.45f, 0.5f, 1f);
}
