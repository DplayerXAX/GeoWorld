using System;
using System.Collections.Generic;
using UnityEngine;

// Who is playing, and which of them is us. Deliberately knows NOTHING about a
// transport: no package is committed to yet, and everything below is true whether
// the wire ends up being Netcode for GameObjects, Mirror, Photon or a hotseat.
//
// Single-player is modelled as a one-player session that is also the host, so the
// rest of the game can ask ownership/authority questions unconditionally instead
// of branching on "are we networked". A code path that only exists in multiplayer
// is a code path that is only tested in multiplayer.
public enum NetRole { Host, Client }

[Serializable]
public class NetPlayer
{
    public int    id;             // 0..MaxPlayers-1, stable for the session
    public string displayName = "";
    public Color  color = Color.white;
    public bool   connected;
    public bool   isLocal;
    public bool   ready;
}

public static class MultiplayerSession
{
    public const int MaxPlayers = 4;

    // Distinct at a glance on a shared board, and kept clear of the synergy palette
    // so "whose block is that" never reads as "which faction is that".
    static readonly Color[] SlotColors =
    {
        new(0.30f, 0.68f, 1.00f),   // blue
        new(0.98f, 0.62f, 0.20f),   // orange
        new(0.42f, 0.85f, 0.45f),   // green
        new(0.88f, 0.42f, 0.80f),   // magenta
    };

    static readonly NetPlayer[] _slots = new NetPlayer[MaxPlayers];

    public static NetRole Role { get; private set; } = NetRole.Host;
    public static int     LocalId { get; private set; }

    /// <summary>True when this build is the authority — the one allowed to APPLY commands.</summary>
    public static bool IsHost => Role == NetRole.Host;

    /// <summary>Fired whenever the roster changes (join, leave, rename, ready).</summary>
    public static event Action RosterChanged;

    public static IReadOnlyList<NetPlayer> Slots
    {
        get { EnsureSlots(); return _slots; }
    }

    public static int ConnectedCount
    {
        get
        {
            EnsureSlots();
            int n = 0;
            foreach (var p in _slots) if (p.connected) n++;
            return n;
        }
    }

    static void EnsureSlots()
    {
        for (int i = 0; i < MaxPlayers; i++)
            _slots[i] ??= new NetPlayer { id = i, color = SlotColors[i], displayName = $"Player {i + 1}" };
    }

    /// <summary>
    /// Local single-player: one connected player who is also the host. Called by
    /// default so a scene entered without any lobby still has a valid session
    /// rather than a null one.
    /// </summary>
    public static void BeginLocal()
    {
        EnsureSlots();
        foreach (var p in _slots) { p.connected = false; p.isLocal = false; p.ready = false; }

        Role = NetRole.Host;
        LocalId = 0;
        _slots[0].connected = true;
        _slots[0].isLocal   = true;
        // NOT pre-readied. `ready` means "ready for the wave about to start", and a
        // slot that begins the round already ready would make the wave gate fire on
        // its own before the player had pressed anything.
        _slots[0].ready     = false;
        RosterChanged?.Invoke();
    }

    /// <summary>Networked session start. `localId` is the slot this build owns.</summary>
    public static void Begin(NetRole role, int localId)
    {
        EnsureSlots();
        Role    = role;
        LocalId = Mathf.Clamp(localId, 0, MaxPlayers - 1);
        _slots[LocalId].connected = true;
        _slots[LocalId].isLocal   = true;
        RosterChanged?.Invoke();
    }

    /// <summary>
    /// Host-side: claim the lowest free slot for a joining peer. Returns its id, or
    /// -1 when the session is full. Slot ids are NOT reused within a session even
    /// after a leave — a returning peer getting someone else's old id would inherit
    /// their blocks.
    /// </summary>
    public static int Join(string displayName)
    {
        EnsureSlots();
        for (int i = 0; i < MaxPlayers; i++)
        {
            if (_slots[i].connected) continue;
            _slots[i].connected = true;
            if (!string.IsNullOrEmpty(displayName)) _slots[i].displayName = displayName;
            RosterChanged?.Invoke();
            return i;
        }
        return -1;
    }

    public static void Leave(int id)
    {
        if (!Valid(id)) return;
        _slots[id].connected = false;
        _slots[id].ready     = false;
        RosterChanged?.Invoke();
    }

    public static void SetReady(int id, bool ready)
    {
        if (!Valid(id)) return;
        _slots[id].ready = ready;
        RosterChanged?.Invoke();
    }

    public static bool AllReady
    {
        get
        {
            EnsureSlots();
            bool any = false;
            foreach (var p in _slots)
            {
                if (!p.connected) continue;
                any = true;
                if (!p.ready) return false;
            }
            return any;
        }
    }

    /// <summary>
    /// Replace the whole roster with the host's. Clients never edit their own copy —
    /// they take the host's wholesale — so this is the one door state comes in
    /// through, and the local slot's isLocal flag is re-derived rather than trusted
    /// from the wire.
    /// </summary>
    public static void AdoptRoster(int connectedMask, int readyMask, string[] names)
    {
        EnsureSlots();
        for (int i = 0; i < MaxPlayers; i++)
        {
            _slots[i].connected = (connectedMask & (1 << i)) != 0;
            _slots[i].ready     = (readyMask     & (1 << i)) != 0;
            _slots[i].isLocal   = i == LocalId;
            if (names != null && i < names.Length && !string.IsNullOrEmpty(names[i]))
                _slots[i].displayName = names[i];
        }
        RosterChanged?.Invoke();
    }

    public static void SetName(int id, string name)
    {
        if (!Valid(id) || string.IsNullOrWhiteSpace(name)) return;
        _slots[id].displayName = name.Trim();
        RosterChanged?.Invoke();
    }

    /// <summary>Clear every ready flag — called when a wave commits, so the next build phase starts fresh.</summary>
    public static void ClearReady()
    {
        EnsureSlots();
        foreach (var p in _slots) p.ready = false;
        RosterChanged?.Invoke();
    }

    public static int ReadyCount
    {
        get
        {
            EnsureSlots();
            int n = 0;
            foreach (var p in _slots) if (p.connected && p.ready) n++;
            return n;
        }
    }

    public static NetPlayer Get(int id)
    {
        EnsureSlots();
        return Valid(id) ? _slots[id] : null;
    }

    public static Color ColorOf(int id) => Valid(id) ? SlotColors[id] : Color.white;

    public static bool Valid(int id) => id >= 0 && id < MaxPlayers;

    /// <summary>True when `id` is a player this build is allowed to act for.</summary>
    public static bool IsLocalPlayer(int id) => Valid(id) && _slots[id] != null && _slots[id].isLocal;
}
