using UnityEngine;

// Every player action that CHANGES the board, expressed as data instead of a direct
// method call.
//
// This is the piece that makes multiplayer possible at all, and the reason to build
// it before any transport is chosen. Today PlacementController mutates the world the
// instant it reads input, which cannot be networked: there's nothing to send, nothing
// to validate on an authority, nothing to replay. Once actions are values, a
// transport is just a pipe that moves them — and single-player is that pipe with
// zero length (see CommandBus.LocalRouter).
//
// Kept as a plain struct with an explicit kind rather than a class hierarchy: these
// have to survive serialization by whatever wire ends up being used, and every
// mainstream option handles a flat blittable-ish struct far more happily than
// polymorphism.
public enum GameCommandKind
{
    PlaceBlock,     // blockAssetId at cell, rotated by rotation90
    RemoveBlock,    // whatever the player owns at cell
    SellBlock,
    RefreshShop,
    StartWave,
    UpgradeTurret,
    PlaceDevice,    // same as PlaceBlock; separate so authority can price it apart
}

[System.Serializable]
public struct GameCommand
{
    public GameCommandKind kind;

    /// <summary>Who issued it. The authority uses this for ownership and budget, never the sender's word for it.</summary>
    public int playerId;

    /// <summary>Monotonic per player. Lets the authority drop duplicates from a resend.</summary>
    public int sequence;

    public Vector3Int cell;
    public Vector3Int rotation90;

    /// <summary>
    /// Stable identity of the BlockData involved, not a reference. A direct object
    /// reference can't cross the wire, and an index into a per-client shop roll
    /// would resolve to a different block on every machine.
    /// </summary>
    public string blockAssetId;

    /// <summary>What it cost. Sent because the resource pool is shared, so every
    /// machine has to make the SAME deduction — a remote machine cannot look up the
    /// price itself, since the shop that set it was rolled locally by the buyer.</summary>
    public int price;

    /// <summary>BlockColor as an int, and the exact tint packed as 0xRRGGBB.
    /// The tint is sent rather than re-derived because an uncoloured block picks its
    /// own random shade, and "random" resolves differently on every machine.</summary>
    public int colorIndex;
    public int tintRgb;

    /// <summary>Carried so a repositioned turret keeps its upgrades on every machine.</summary>
    public int upBasicPower, upBasicBurst, upAoeFire, upAoeGravity;

    public static int PackRgb(UnityEngine.Color c) =>
        (Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f) << 16) |
        (Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f) << 8)  |
         Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f);

    public static UnityEngine.Color UnpackRgb(int rgb) => new(
        ((rgb >> 16) & 0xFF) / 255f,
        ((rgb >> 8)  & 0xFF) / 255f,
         (rgb        & 0xFF) / 255f);

    public override string ToString() =>
        $"{kind} p{playerId}#{sequence} @{cell}" + (string.IsNullOrEmpty(blockAssetId) ? "" : $" [{blockAssetId}]");
}
