using System.Collections.Generic;
using UnityEngine;

// The three things the rest of the game needs to know about live devices, kept in
// one place so no other system has to hold a reference to a device to answer them:
//
//   • Which cells are RESERVED (an oscillator's travel corridor) — asked by
//     placement validation.
//   • Which face pairs are LINKED (portal mouths) — asked by the surface graph
//     while it builds adjacency.
//   • Which cells have a live TRAP — asked when an enemy steps somewhere.
//
// Devices register on placement and unregister on removal. Static because the
// askers are spread across placement, graph building and enemy movement, and
// threading a manager reference into all three would be a lot of plumbing for a
// lookup table.
public static class DeviceRegistry
{
    // ── Reserved cells ───────────────────────────────────────────────────────
    // Value = the device that owns the reservation, so removing one device can't
    // free a cell another one still needs.
    static readonly Dictionary<Vector3Int, HashSet<PlacedDevice>> _reserved = new();

    public static void Reserve(PlacedDevice owner, IEnumerable<Vector3Int> cells)
    {
        foreach (var c in cells)
        {
            if (!_reserved.TryGetValue(c, out var set)) _reserved[c] = set = new HashSet<PlacedDevice>();
            set.Add(owner);
        }
    }

    public static void ReleaseAll(PlacedDevice owner)
    {
        var empty = new List<Vector3Int>();
        foreach (var kv in _reserved)
        {
            kv.Value.Remove(owner);
            if (kv.Value.Count == 0) empty.Add(kv.Key);
        }
        foreach (var c in empty) _reserved.Remove(c);
    }

    /// <summary>True when something has claimed this cell and nothing may be built in it.</summary>
    public static bool IsReserved(Vector3Int cell) =>
        _reserved.TryGetValue(cell, out var set) && set.Count > 0;

    // ── Portal links ─────────────────────────────────────────────────────────
    static readonly List<PortalDevice> _portals = new();

    public static void RegisterPortal(PortalDevice p)   { if (!_portals.Contains(p)) _portals.Add(p); }
    public static void UnregisterPortal(PortalDevice p) => _portals.Remove(p);

    /// <summary>
    /// Every currently-linked portal pair, as the two cells they join. The graph
    /// turns these into face adjacency; nothing here knows what a FaceNode is.
    /// </summary>
    public static IEnumerable<(Vector3Int a, Vector3Int b, int cost)> PortalLinks()
    {
        for (int i = 0; i < _portals.Count; i++)
        {
            var a = _portals[i];
            if (a == null || a.Partner == null) continue;
            // Emitted once per pair, by having only the lower-index half report it.
            if (_portals.IndexOf(a.Partner) < i) continue;
            yield return (a.Cell, a.Partner.Cell, a.Data != null ? a.Data.traversalCost : 0);
        }
    }

    /// <summary>The unlinked portal waiting for a partner on `key`, or null.</summary>
    public static PortalDevice FindLonelyPortal(string key)
    {
        foreach (var p in _portals)
            if (p != null && p.Partner == null && p.Data != null && p.Data.pairKey == key)
                return p;
        return null;
    }

    // ── Traps ────────────────────────────────────────────────────────────────
    static readonly Dictionary<Vector3Int, TrapDevice> _traps = new();

    public static void RegisterTrap(Vector3Int cell, TrapDevice t) => _traps[cell] = t;

    public static void UnregisterTrap(Vector3Int cell, TrapDevice t)
    {
        if (_traps.TryGetValue(cell, out var cur) && cur == t) _traps.Remove(cell);
    }

    public static TrapDevice TrapAt(Vector3Int cell) =>
        _traps.TryGetValue(cell, out var t) ? t : null;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <summary>
    /// Wipes everything. Called on run start / restart: these are static tables and
    /// a scene reload does NOT clear them, so without this a restarted run would
    /// inherit the last run's reserved cells and phantom portals.
    /// </summary>
    public static void Clear()
    {
        _reserved.Clear();
        _portals.Clear();
        _traps.Clear();
    }
}
