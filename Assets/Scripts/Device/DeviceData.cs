using UnityEngine;

// Player-placed DEVICES: blocks that do something to the level rather than just
// shaping it. An oscillator that rides a block up and down, a portal pair enemies
// will route through, a trap that takes out the first thing to step on it.
//
// Deliberately a field ON BlockData rather than a new BlockType, because that
// inherits the entire existing pipeline for free — shop rolls, pricing, the
// placement ghost, sell, undo, save/restore all already work on BlockData and
// need no knowledge of devices at all. Exactly how bulletPrefab hangs a turret's
// configuration off a block without turrets needing their own placement path.
//
// Authoring: make a DeviceData asset, then point a BlockData's `device` field at
// it. Nothing is wired into any level yet — that's a roster/shop decision.
public enum DeviceKind
{
    // Carries the block it sits on up and down between two heights. Its travel
    // corridor is RESERVED: nothing else may be placed in the cells it sweeps,
    // because a lift that shears through a wall is a bug you can't unsee.
    Oscillator,

    // Half of a pair. Enemy pathfinding treats the two mouths as adjacent, so a
    // portal genuinely shortens routes rather than being decoration the AI ignores.
    Portal,

    // Kills the first ORDINARY enemy to walk onto it, then spends itself. Ordinary
    // on purpose — a one-shot that deletes an elite or a boss is either useless or
    // absurd depending on the wave, and neither is interesting.
    Trap,
}

[CreateAssetMenu(menuName = "Game/Device", fileName = "Device")]
public class DeviceData : ScriptableObject
{
    public DeviceKind kind = DeviceKind.Oscillator;

    [Header("Presentation")]
    public string displayName = "";
    [TextArea] public string description = "";
    [Tooltip("Tint applied to the placed block so a device reads apart from plain terrain.")]
    public Color accentColor = new(0.42f, 0.80f, 0.95f);

    [Header("Oscillator")]
    [Tooltip("Cells travelled above the placed position. The block sweeps from its placed cell up to this many cells higher.")]
    [Range(1, 8)] public int travelCells = 3;
    [Tooltip("Seconds for one full round trip.")]
    [Min(0.5f)] public float cycleSeconds = 4f;
    [Tooltip("Seconds held still at each end — a lift that never pauses is almost impossible to time a route around.")]
    [Min(0f)] public float dwellSeconds = 0.6f;

    [Header("Portal")]
    [Tooltip("Portals with the SAME pairKey link to each other. Two per key; a third placement of the same key refuses to link (see PortalDevice).")]
    public string pairKey = "A";
    [Tooltip("Extra path cost, in hops, for stepping through. 0 = free, which makes any portal strictly better than walking and removes the decision.")]
    [Min(0)] public int traversalCost = 2;

    [Header("Trap")]
    [Tooltip("Enemies with more than this max health are ignored — that's what 'ordinary' means here.")]
    [Min(1)] public int maxTargetHealth = 6;
    [Tooltip("Uses before the device is spent. 1 = single-shot.")]
    [Min(1)] public int charges = 1;

    public string Title => string.IsNullOrEmpty(displayName) ? kind.ToString() : displayName;
}
