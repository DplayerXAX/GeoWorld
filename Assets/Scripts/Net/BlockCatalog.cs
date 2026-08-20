using System.Collections.Generic;
using UnityEngine;

// Stable id ↔ BlockData lookup.
//
// Networking can't send an object reference, and it can't send "index 2 of my
// shop" either — the shop is rolled per client, so that index names a different
// block on every machine. So every block that can cross the wire needs a name both
// sides agree on, and the asset's own name is the one thing already guaranteed
// identical in both builds.
//
// Sources, in order: whatever the scene registers (PlacementController's own block
// array), then an optional Resources folder.
//
// Resources ALONE was the original design and it was wrong. The folder has to be
// created and populated by hand, and when it isn't the catalog is silently empty:
// IdOf returns "", every preview goes out with a blank id, Resolve refuses it, and
// no ghost ever appears — with nothing logged anywhere. Registering the blocks the
// game already has means the common case needs no setup at all, and a block that
// still can't be catalogued now says so.
public static class BlockCatalog
{
    public const string ResourceFolder = "GeoWorldBlocks";

    static Dictionary<string, BlockData> _byId;

    static void EnsureBuilt()
    {
        if (_byId != null) return;
        _byId = new Dictionary<string, BlockData>();
        Add(Resources.LoadAll<BlockData>(ResourceFolder));
    }

    /// <summary>
    /// Register blocks the scene owns. Safe to call repeatedly — the same asset
    /// registering twice is not an error, only two DIFFERENT assets sharing a name.
    /// </summary>
    public static void RegisterAll(BlockData[] data)
    {
        EnsureBuilt();
        Add(data);
    }

    static void Add(BlockData[] data)
    {
        if (data == null) return;
        foreach (var b in data)
        {
            if (b == null) continue;
            if (_byId.TryGetValue(b.name, out var existing))
            {
                // Two different assets under one name resolve to whichever landed
                // first — a desync waiting to happen, so say so loudly now.
                if (existing != b)
                    Debug.LogError($"[BlockCatalog] Two different blocks are both named '{b.name}'. Networked placement of it will be ambiguous.");
                continue;
            }
            _byId[b.name] = b;
        }
    }

    /// <summary>Wire id for a block. Empty when it isn't catalogued.</summary>
    public static string IdOf(BlockData data)
    {
        if (data == null) return "";
        EnsureBuilt();
        if (_byId.ContainsKey(data.name)) return data.name;

        // Logged once per block rather than swallowed: an uncatalogued block is
        // invisible to everyone else, and silence is how that went unnoticed.
        if (_warned.Add(data.name))
            Debug.LogWarning($"[BlockCatalog] '{data.name}' is not catalogued — other players will not see it.");
        return "";
    }

    static readonly HashSet<string> _warned = new();

    public static BlockData Resolve(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        EnsureBuilt();
        return _byId.TryGetValue(id, out var b) ? b : null;
    }

    /// <summary>Drops the cache — call after adding assets at edit time.</summary>
    public static void Invalidate() { _byId = null; _warned.Clear(); }
}
