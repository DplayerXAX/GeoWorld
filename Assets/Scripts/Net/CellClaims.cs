using System.Collections.Generic;
using UnityEngine;

// Who called which cells first.
//
// This is what "抢占" actually needs. Occupancy alone can't decide a race: two
// players pressing place on the same cell in the same tick both see it empty
// locally, both pass their own validation, and both commit — one of them ends up
// with a block that isn't really there. A claim table on the AUTHORITY resolves it,
// because arrival order at one machine is a total order even when nothing else is.
//
// A claim is granted before the block exists and released if the placement is
// abandoned, so it also covers the gap between "I asked" and "it's built" —
// the window a second player could otherwise slip into.
public static class CellClaims
{
    // cell → the player who holds it.
    static readonly Dictionary<Vector3Int, int> _claims = new();

    /// <summary>
    /// All-or-nothing: a multi-cell block whose footprint is partly taken must fail
    /// entirely, not claim the free half and leave a fragment reserved forever.
    /// </summary>
    public static bool TryClaim(IList<Vector3Int> cells, int playerId)
    {
        if (cells == null || cells.Count == 0) return false;

        for (int i = 0; i < cells.Count; i++)
            if (_claims.TryGetValue(cells[i], out int holder) && holder != playerId)
                return false;

        for (int i = 0; i < cells.Count; i++) _claims[cells[i]] = playerId;
        return true;
    }

    /// <summary>Give cells back — the placement was cancelled or rejected downstream.</summary>
    public static void Release(IList<Vector3Int> cells, int playerId)
    {
        if (cells == null) return;
        for (int i = 0; i < cells.Count; i++)
            if (_claims.TryGetValue(cells[i], out int holder) && holder == playerId)
                _claims.Remove(cells[i]);
    }

    public static void ReleaseAll(int playerId)
    {
        var gone = new List<Vector3Int>();
        foreach (var kv in _claims) if (kv.Value == playerId) gone.Add(kv.Key);
        foreach (var c in gone) _claims.Remove(c);
    }

    /// <summary>
    /// True when someone ELSE holds this cell. Placement previews consult this so a
    /// contested cell reads as unavailable before the losing player commits to it.
    /// </summary>
    public static bool HeldByOther(Vector3Int cell, int playerId) =>
        _claims.TryGetValue(cell, out int holder) && holder != playerId;

    public static int HolderOf(Vector3Int cell) =>
        _claims.TryGetValue(cell, out int holder) ? holder : -1;

    public static void Clear() => _claims.Clear();
}
