using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// The NGO layer, built entirely on CustomMessagingManager named messages.
//
// No NetworkObjects, no NetworkVariables, no prefab registration. That's a
// deliberate choice: NGO's spawn system exists to replicate OBJECTS, and this game
// has none to replicate — the board is deterministic given the commands that built
// it, and the only per-frame traffic is a held-block preview that nothing else
// depends on. Named messages give exactly that and nothing more, which also means
// there is no NetworkManager prefab list to keep in sync with the block roster.
//
// Three messages:
//   Cmd     client → host   a GameCommand to validate and apply
//   Applied host → clients  a command the host accepted; clients apply it verbatim
//   Preview any → any       the sender's in-progress block ghost (unreliable)
[DisallowMultipleComponent]
public class NgoNetwork : MonoBehaviour
{
    public static NgoNetwork Instance { get; private set; }

    const string MsgCmd     = "geo.cmd";
    const string MsgApplied = "geo.applied";
    const string MsgPreview = "geo.preview";
    const string MsgLobby   = "geo.lobby";     // host -> all: the room as the host sees it
    const string MsgReady   = "geo.ready";     // client -> host: my lobby ready flag
    const string MsgBegin   = "geo.begin";     // host -> all: load the match now
    const string MsgName    = "geo.name";      // client -> host: what to call me

    // Held-block state as it goes over the wire. Sent unreliably and often, so it's
    // kept to the minimum that can draw a ghost.
    public struct PreviewState : INetworkSerializable
    {
        public int        playerId;
        public bool       active;
        public Vector3Int cell;
        public Quaternion rotation;
        public FixedString64Bytes blockId;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref playerId);
            s.SerializeValue(ref active);
            s.SerializeValue(ref cell);
            s.SerializeValue(ref rotation);
            s.SerializeValue(ref blockId);
        }
    }

    /// <summary>Latest preview from every remote player, keyed by player id.</summary>
    public readonly Dictionary<int, PreviewState> RemotePreviews = new();

    // The room, exactly as the host sees it. Sent whole rather than as deltas: it is
    // four names and eight bits, it changes a handful of times per session, and a
    // client that missed one delta would show a wrong roster until the next change.
    public struct LobbyState : INetworkSerializable
    {
        public FixedString64Bytes levelId;
        public ulong seed;
        public int   yourId;           // the slot the receiving client owns
        public int   connectedMask;    // one bit per slot
        public int   readyMask;
        public FixedString32Bytes n0, n1, n2, n3;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref levelId);
            s.SerializeValue(ref seed);
            s.SerializeValue(ref yourId);
            s.SerializeValue(ref connectedMask);
            s.SerializeValue(ref readyMask);
            s.SerializeValue(ref n0); s.SerializeValue(ref n1);
            s.SerializeValue(ref n2); s.SerializeValue(ref n3);
        }
    }

    /// <summary>Raised on a client when the host's room state arrives.</summary>
    public event System.Action LobbyUpdated;

    /// <summary>Raised on every machine when the host starts the match.</summary>
    public event System.Action<string> MatchBeginning;

    void Awake() => Instance = this;

    void OnDestroy()
    {
        CommandBus.Applied -= HostRelay;
        if (Instance == this) Instance = null;
    }

    // Relaying from CommandBus.Applied rather than from the router is what makes a
    // FOUR-player game work. The router only ever sees commands THIS machine issued,
    // so relaying there left a client's command applied on the host and invisible to
    // the other two clients. Applied fires once per accepted command whatever its
    // origin, which is exactly the set that has to go out.
    void HostRelay(GameCommand cmd)
    {
        if (MultiplayerSession.IsHost) BroadcastApplied(cmd);
    }

    // ── Session start ────────────────────────────────────────────────────────

    public void StartHost()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) { Debug.LogError("[Ngo] No NetworkManager in the scene."); return; }

        nm.OnClientConnectedCallback    += HostOnClientConnected;
        nm.OnClientDisconnectCallback   += HostOnClientDisconnected;
        nm.StartHost();

        MultiplayerSession.BeginLocal();                 // host takes slot 0
        MultiplayerSession.Begin(NetRole.Host, 0);
        RegisterHandlers();
        CommandBus.SetRouter(new NgoCommandRouter());
        CommandBus.Applied -= HostRelay;                 // never twice, even on a restart
        CommandBus.Applied += HostRelay;
    }

    public void StartClient()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) { Debug.LogError("[Ngo] No NetworkManager in the scene."); return; }

        nm.StartClient();
        // Slot id is assigned by the host and arrives with the first Applied /
        // Preview traffic; until then we act as a client with a provisional id.
        MultiplayerSession.Begin(NetRole.Client, 1);
        RegisterHandlers();
        CommandBus.SetRouter(new NgoCommandRouter());
    }

    void HostOnClientConnected(ulong clientId)
    {
        int slot = MultiplayerSession.Join($"Player {clientId}");
        if (slot < 0)
        {
            Debug.LogWarning($"[Ngo] Session full — refusing client {clientId}.");
            NetworkManager.Singleton.DisconnectClient(clientId);
            return;
        }
        _slotByClient[clientId] = slot;
        BroadcastLobby();          // the newcomer needs the room; the room needs the newcomer
    }

    void HostOnClientDisconnected(ulong clientId)
    {
        if (!_slotByClient.TryGetValue(clientId, out int slot)) return;
        _slotByClient.Remove(clientId);
        // Anything they'd reserved but not built goes back to the pool, or the board
        // keeps holes nobody can fill.
        CellClaims.ReleaseAll(slot);
        RemotePreviews.Remove(slot);
        MultiplayerSession.Leave(slot);
        BroadcastLobby();
    }

    readonly Dictionary<ulong, int> _slotByClient = new();

    // The messaging manager, or null when there is nothing to send through.
    //
    // IsListening on its own is NOT a sufficient guard: during shutdown, and for a
    // frame around a client dropping, the manager is already disposed while the
    // NetworkManager still reports itself as listening. Every send used to assume
    // otherwise, which is what threw out of BroadcastLobby when a peer disconnected.
    static CustomMessagingManager Msg
    {
        get
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return null;
            return nm.CustomMessagingManager;
        }
    }

    // ── Handlers ─────────────────────────────────────────────────────────────

    void RegisterHandlers()
    {
        var mm = Msg;
        if (mm == null) { Debug.LogError("[Ngo] Started with no messaging manager — no traffic will arrive."); return; }

        mm.RegisterNamedMessageHandler(MsgCmd,     OnCmd);
        mm.RegisterNamedMessageHandler(MsgApplied, OnApplied);
        mm.RegisterNamedMessageHandler(MsgPreview, OnPreview);
        mm.RegisterNamedMessageHandler(MsgLobby,   OnLobby);
        mm.RegisterNamedMessageHandler(MsgReady,   OnReady);
        mm.RegisterNamedMessageHandler(MsgBegin,   OnBegin);
        mm.RegisterNamedMessageHandler(MsgName,    OnName);
    }

    // -- Lobby ----------------------------------------------------------------

    // Client side: adopt the host's roster wholesale. A client never edits the room
    // itself — it asks (MsgReady) and waits to be told — so there is exactly one
    // machine whose idea of the room is allowed to be right.
    void OnLobby(ulong _, FastBufferReader reader)
    {
        if (MultiplayerSession.IsHost) return;
        reader.ReadValueSafe(out LobbyState st);

        MultiplayerSession.Begin(NetRole.Client, st.yourId);
        MultiplayerSession.AdoptRoster(st.connectedMask, st.readyMask, new[]
        {
            st.n0.ToString(), st.n1.ToString(), st.n2.ToString(), st.n3.ToString(),
        });
        RoomConfig.Set(st.levelId.ToString(), st.seed);
        LobbyUpdated?.Invoke();
    }

    // Host side: a client toggled its ready. The slot comes from OUR mapping, never
    // from anything the client said about itself.
    void OnReady(ulong senderClientId, FastBufferReader reader)
    {
        if (!MultiplayerSession.IsHost) return;
        reader.ReadValueSafe(out bool ready);
        if (!_slotByClient.TryGetValue(senderClientId, out int slot)) return;
        MultiplayerSession.SetReady(slot, ready);
        BroadcastLobby();
    }

    // Host side: a client wants to be called something. Applied to the slot WE
    // assigned it, then echoed to the whole room like any other roster change — so a
    // rename reaches the other three the same way a join does, instead of only
    // showing up on the machine that typed it.
    void OnName(ulong senderClientId, FastBufferReader reader)
    {
        if (!MultiplayerSession.IsHost) return;
        reader.ReadValueSafe(out FixedString32Bytes name);
        if (!_slotByClient.TryGetValue(senderClientId, out int slot)) return;
        MultiplayerSession.SetName(slot, name.ToString());
        BroadcastLobby();
    }

    public void SendNameToHost(string name)
    {
        var mm = Msg;
        if (mm == null || MultiplayerSession.IsHost) return;
        using var w = new FastBufferWriter(64, Allocator.Temp);
        w.WriteValueSafe(Short(name));
        mm.SendNamedMessage(MsgName, NetworkManager.ServerClientId, w, NetworkDelivery.ReliableSequenced);
    }

    void OnBegin(ulong _, FastBufferReader reader)
    {
        reader.ReadValueSafe(out FixedString64Bytes scene);
        MatchBeginning?.Invoke(scene.ToString());
    }

    public void BroadcastLobby()
    {
        var mm = Msg;
        if (mm == null || !MultiplayerSession.IsHost) return;

        int conn = 0, ready = 0;
        var names = new string[MultiplayerSession.MaxPlayers];
        for (int i = 0; i < MultiplayerSession.MaxPlayers; i++)
        {
            var pl = MultiplayerSession.Get(i);
            if (pl == null) continue;
            if (pl.connected) conn  |= 1 << i;
            if (pl.ready)     ready |= 1 << i;
            names[i] = pl.displayName;
        }

        // yourId is stamped PER RECIPIENT, so a client learns which slot is its own
        // from the same message that carries the roster. One round trip, and no
        // window where a client knows the room but not where it sits in it.
        foreach (var kv in _slotByClient)
        {
            // Skip anyone already gone. HostOnClientDisconnected calls this, and on
            // that path the leaver can still be in our map while NGO has already
            // dropped it — sending to a stale id throws instead of no-opping.
            if (!NetworkManager.Singleton.ConnectedClientsIds.Contains(kv.Key)) continue;

            var st = new LobbyState
            {
                levelId       = RoomConfig.LevelId ?? "",
                seed          = RoomConfig.Seed,
                yourId        = kv.Value,
                connectedMask = conn,
                readyMask     = ready,
                n0 = Short(names[0]), n1 = Short(names[1]),
                n2 = Short(names[2]), n3 = Short(names[3]),
            };
            using var w = new FastBufferWriter(256, Allocator.Temp);
            w.WriteValueSafe(st);
            mm.SendNamedMessage(MsgLobby, kv.Key, w, NetworkDelivery.ReliableSequenced);
        }
    }

    // Capped well under FixedString32Bytes' budget: it stores UTF-8, so a name of
    // multi-byte characters costs several bytes each and a round 30 would throw.
    static FixedString32Bytes Short(string s)
    {
        if (string.IsNullOrEmpty(s)) return default;
        return new FixedString32Bytes(s.Length > 12 ? s.Substring(0, 12) : s);
    }

    public void SendReadyToHost(bool ready)
    {
        var mm = Msg;
        if (mm == null || MultiplayerSession.IsHost) return;
        using var w = new FastBufferWriter(8, Allocator.Temp);
        w.WriteValueSafe(ready);
        mm.SendNamedMessage(MsgReady, NetworkManager.ServerClientId, w, NetworkDelivery.ReliableSequenced);
    }

    /// <summary>Host only: everyone loads the match scene, us included.</summary>
    public void BroadcastBegin(string scene)
    {
        var mm = Msg;
        if (mm == null || !MultiplayerSession.IsHost) return;

        using var w = new FastBufferWriter(96, Allocator.Temp);
        w.WriteValueSafe(new FixedString64Bytes(scene ?? ""));
        mm.SendNamedMessageToAll(MsgBegin, w, NetworkDelivery.ReliableSequenced);
        MatchBeginning?.Invoke(scene);   // SendNamedMessageToAll does not loop back to the host
    }

    // Host side: a client's command arrives. The sender's claimed playerId is
    // OVERWRITTEN with the slot we assigned them — a client that could name its own
    // id could spend another player's money.
    void OnCmd(ulong senderClientId, FastBufferReader reader)
    {
        if (!MultiplayerSession.IsHost) return;
        reader.ReadValueSafe(out GameCommandWire wire);

        var cmd = wire.ToCommand();
        cmd.playerId = _slotByClient.TryGetValue(senderClientId, out int slot) ? slot : cmd.playerId;

        CommandBus.Deliver(cmd);
    }

    // Client side: the host accepted a command (from anyone, including us).
    void OnApplied(ulong _, FastBufferReader reader)
    {
        reader.ReadValueSafe(out GameCommandWire wire);
        CommandBus.ApplyRemote(wire.ToCommand());
    }

    void OnPreview(ulong _, FastBufferReader reader)
    {
        reader.ReadValueSafe(out PreviewState st);
        if (st.playerId == MultiplayerSession.LocalId) return;   // our own echo
        if (st.active) RemotePreviews[st.playerId] = st;
        else           RemotePreviews.Remove(st.playerId);
    }

    // ── Sending ──────────────────────────────────────────────────────────────

    public void SendCommandToHost(GameCommand cmd)
    {
        var mm = Msg;
        if (mm == null) return;

        var wire = GameCommandWire.From(cmd);
        using var w = new FastBufferWriter(GameCommandWire.Size, Allocator.Temp);
        w.WriteValueSafe(wire);
        mm.SendNamedMessage(MsgCmd, NetworkManager.ServerClientId, w, NetworkDelivery.ReliableSequenced);
    }

    public void BroadcastApplied(GameCommand cmd)
    {
        var mm = Msg;
        if (mm == null || !MultiplayerSession.IsHost) return;

        var wire = GameCommandWire.From(cmd);
        using var w = new FastBufferWriter(GameCommandWire.Size, Allocator.Temp);
        w.WriteValueSafe(wire);
        mm.SendNamedMessageToAll(MsgApplied, w, NetworkDelivery.ReliableSequenced);
    }

    // Unreliable on purpose: a dropped preview frame is replaced by the next one
    // 60ms later, and re-sending stale ghost positions would only add latency to
    // the traffic that actually matters.
    public void BroadcastPreview(PreviewState st)
    {
        var mm = Msg;
        if (mm == null) return;

        using var w = new FastBufferWriter(128, Allocator.Temp);
        w.WriteValueSafe(st);
        if (MultiplayerSession.IsHost)
            mm.SendNamedMessageToAll(MsgPreview, w, NetworkDelivery.Unreliable);
        else
            mm.SendNamedMessage(MsgPreview, NetworkManager.ServerClientId, w, NetworkDelivery.Unreliable);
    }
}

// GameCommand is a plain struct for authoring convenience; this is its wire form,
// with the string swapped for a fixed-size buffer because FastBufferWriter needs a
// known upper bound and a managed string has none.
public struct GameCommandWire : INetworkSerializable
{
    // Room for the fixed string plus every int below, with slack — a writer that
    // runs out of buffer throws rather than truncating, so this is sized generously
    // on purpose.
    public const int Size = 224;

    public int kind, playerId, sequence;
    public Vector3Int cell, rotation90;
    public FixedString64Bytes blockAssetId;
    public int price, colorIndex, tintRgb;
    public int upBasicPower, upBasicBurst, upAoeFire, upAoeGravity;

    public static GameCommandWire From(GameCommand c) => new()
    {
        kind = (int)c.kind, playerId = c.playerId, sequence = c.sequence,
        cell = c.cell, rotation90 = c.rotation90,
        blockAssetId = c.blockAssetId ?? "",
        price = c.price, colorIndex = c.colorIndex, tintRgb = c.tintRgb,
        upBasicPower = c.upBasicPower, upBasicBurst = c.upBasicBurst,
        upAoeFire = c.upAoeFire, upAoeGravity = c.upAoeGravity,
    };

    public GameCommand ToCommand() => new()
    {
        kind = (GameCommandKind)kind, playerId = playerId, sequence = sequence,
        cell = cell, rotation90 = rotation90,
        blockAssetId = blockAssetId.ToString(),
        price = price, colorIndex = colorIndex, tintRgb = tintRgb,
        upBasicPower = upBasicPower, upBasicBurst = upBasicBurst,
        upAoeFire = upAoeFire, upAoeGravity = upAoeGravity,
    };

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref kind);
        s.SerializeValue(ref playerId);
        s.SerializeValue(ref sequence);
        s.SerializeValue(ref cell);
        s.SerializeValue(ref rotation90);
        s.SerializeValue(ref blockAssetId);
        s.SerializeValue(ref price);
        s.SerializeValue(ref colorIndex);
        s.SerializeValue(ref tintRgb);
        s.SerializeValue(ref upBasicPower);
        s.SerializeValue(ref upBasicBurst);
        s.SerializeValue(ref upAoeFire);
        s.SerializeValue(ref upAoeGravity);
    }
}

// The transport half of CommandBus. Client sends to host; host applies locally and
// then tells everyone. Nothing above this class knows which of the two it is.
public class NgoCommandRouter : ICommandRouter
{
    public void Send(GameCommand cmd)
    {
        if (MultiplayerSession.IsHost)
        {
            // No broadcast here — NgoNetwork relays from CommandBus.Applied, so this
            // path and the client-command path leave through the same one place.
            CommandBus.Deliver(cmd);
            return;
        }
        NgoNetwork.Instance?.SendCommandToHost(cmd);
    }
}
