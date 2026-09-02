using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

// The NetworkManager, and the way two machines find each other.
//
// The manager is built in code and kept alive across the scene load, for one hard
// reason: the connection has to SURVIVE the lobby → gameplay load. A NetworkManager
// sitting in the lobby scene dies with that scene and drops every client with it; one
// placed in both scenes is two different managers and the handshake never completes.
// So there is exactly one, created on demand and carried across — which also means
// neither scene has any networking to wire up.
//
// Two ways in, and both end at the same NetworkManager:
//
//   DIRECT   an IP and a port. Same machine or same LAN only. Kept because it is the
//            fastest way to test two instances side by side and it costs no quota.
//   SESSION  Unity's Multiplayer Services. Hands back a short CODE, routes through
//            Relay, and needs no port forwarding — this is the one real players use.
//
// Everything above this class — NgoNetwork, CommandBus, CombatSync, the room UI —
// is identical either way. That was the point of keeping the transport shut in here.
[DisallowMultipleComponent]
public class NetBootstrap : MonoBehaviour
{
    public const ushort DefaultPort = 7777;
    public const int    MaxPlayers  = MultiplayerSession.MaxPlayers;

    static NetBootstrap _inst;
    static NgoNetwork   _net;
    static bool         _building;

    /// <summary>
    /// The protocol layer, created on first touch. A property rather than a field set
    /// by Host()/Join(): the lobby subscribes to its events BEFORE anyone has hosted
    /// or joined, and reading a null there left a client connected but deaf to the
    /// host telling it the match had started.
    /// </summary>
    public static NgoNetwork Net { get { Ensure(); return _net; } }

    UnityTransport _transport;
    NetworkManager _manager;

    static void Ensure()
    {
        if (_inst != null || _building) return;
        _building = true;

        // Built INACTIVE. NetworkManager.Awake runs the moment AddComponent returns
        // and complains about a NetworkConfig with no transport in it, so nothing is
        // allowed to wake until the config is finished.
        var go = new GameObject("NetBootstrap");
        go.SetActive(false);

        var boot = go.AddComponent<NetBootstrap>();
        boot._manager   = go.AddComponent<NetworkManager>();
        boot._transport = go.AddComponent<UnityTransport>();

        boot._manager.NetworkConfig = new NetworkConfig
        {
            NetworkTransport   = boot._transport,
            PlayerPrefab       = null,
            ConnectionApproval = false,

            // Scene synchronisation OFF. It exists to reconcile NetworkObjects across
            // a load, and this game has none — the board is rebuilt from its command
            // stream, not replicated. Left on, every scene change would block on a
            // synchronisation pass with nothing to synchronise. We load the same
            // scene on every machine off a named message instead, which is the same
            // outcome without the stall.
            EnableSceneManagement = false,
        };

        DontDestroyOnLoad(go);
        go.SetActive(true);

        _net      = go.AddComponent<NgoNetwork>();
        _inst     = boot;
        _building = false;
    }

    /// <summary>True once a host or client session is actually on the wire.</summary>
    public static bool Online => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    // ── Unity Services ───────────────────────────────────────────────────────

    static bool _servicesReady;

    /// <summary>
    /// Bring up Unity Services and sign in anonymously.
    ///
    /// Anonymous because this game has no accounts and does not want any: Relay only
    /// needs to know that a caller is *someone*, and asking a player to make an
    /// account before they can hand a friend a room code would be the single worst
    /// step in the flow.
    /// </summary>
    static async Task EnsureServicesAsync()
    {
        if (_servicesReady) return;

        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();

        _servicesReady = true;
    }

    // ── Session (Relay) — the real way in ────────────────────────────────────

    /// <summary>The live session, or null when offline / on a direct connection.</summary>
    public static ISession Session { get; private set; }

    /// <summary>The room code to read out to other players. Empty on a direct connection.</summary>
    public static string RoomCode => Session != null ? Session.Code : "";

    /// <summary>Set while a create/join is in flight, so the UI can say so.</summary>
    public static bool Connecting { get; private set; }

    /// <summary>Last failure, for showing the player something better than nothing.</summary>
    public static string LastError { get; private set; } = "";

    public static async void HostSession(string roomName, Action<bool> done = null)
    {
        if (Connecting) return;
        Connecting = true;
        LastError  = "";

        try
        {
            Ensure();
            await EnsureServicesAsync();

            var options = new SessionOptions
            {
                Name       = string.IsNullOrWhiteSpace(roomName) ? "GeoWorld" : roomName,
                MaxPlayers = MaxPlayers,
                // Private: the room is reached with the code its host reads out, not
                // by appearing in a public list. A browsable list is a different
                // feature and would need Lobby's quotas and moderation to go with it.
                IsPrivate  = true,
            }.WithRelayNetwork();

            // The session brings NGO up itself — it configures the transport with the
            // Relay allocation and starts the host. Calling StartHost by hand here
            // would be a second, competing attempt to open the same socket.
            Session = await MultiplayerService.Instance.CreateSessionAsync(options);

            _net.AdoptHostSession();
            done?.Invoke(true);
        }
        catch (Exception e)
        {
            LastError = Readable(e);
            Debug.LogError($"[Net] Hosting failed: {e}");
            done?.Invoke(false);
        }
        finally { Connecting = false; }
    }

    public static async void JoinSession(string code, Action<bool> done = null)
    {
        if (Connecting) return;
        Connecting = true;
        LastError  = "";

        try
        {
            Ensure();
            await EnsureServicesAsync();

            Session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim());
            _net.AdoptClientSession();
            done?.Invoke(true);
        }
        catch (Exception e)
        {
            LastError = Readable(e);
            Debug.LogError($"[Net] Joining '{code}' failed: {e}");
            done?.Invoke(false);
        }
        finally { Connecting = false; }
    }

    // The SDK's messages name services and HTTP codes. Players get told what to do
    // about it instead.
    static string Readable(Exception e)
    {
        string m = e.Message ?? "";
        if (m.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("404"))                       return "No room with that code.";
        if (m.Contains("full"))                      return "That room is full.";
        // 451 is the ACCOUNT, not the connection.
        //
        // Unity's Lobby API answers this when the service is refused for the project
        // or the organisation behind it — the service not switched on, or a legal
        // agreement still unsigned in the dashboard. It is worth its own branch
        // because it is the one failure the player can do absolutely nothing about
        // from inside the game, and the generic message sends them off to check their
        // wifi for an hour.
        //
        // Note what getting this far proves: initialisation and the anonymous sign-in
        // both succeeded, so the project IS linked and the credentials ARE good. Only
        // the multiplayer service itself is being withheld.
        if (m.Contains("451")) return "Unity's multiplayer service is blocked for this project. Enable it on the Unity dashboard — Shift+click to connect by IP meanwhile.";

        if (m.Contains("environment", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("project", StringComparison.OrdinalIgnoreCase))
            return "Unity Services isn't set up — link the project and enable Relay.";
        return "Couldn't connect. Check your internet and the code.";
    }

    // ── Direct IP — kept for local testing ───────────────────────────────────

    public static void Host(ushort port = DefaultPort)
    {
        Ensure();
        // 0.0.0.0 so the listener accepts on every interface — bound to a specific
        // LAN address it works locally and refuses everyone else, which looks exactly
        // like a firewall problem and is the harder thing to diagnose.
        _inst._transport.SetConnectionData("0.0.0.0", port, "0.0.0.0");
        _net.StartHost();
    }

    public static void Join(string address, ushort port = DefaultPort)
    {
        Ensure();
        string addr = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
        _inst._transport.SetConnectionData(addr, port);
        _net.StartClient();
    }

    // ── Teardown ─────────────────────────────────────────────────────────────

    public static async void Shutdown()
    {
        try
        {
            if (Session != null) await Session.LeaveAsync();
        }
        catch (Exception e) { Debug.LogWarning($"[Net] Leaving the session complained: {e.Message}"); }
        finally { Session = null; }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        // The session goes back to being a local one-player host, so anything that
        // asks ownership questions after a disconnect still gets a valid answer
        // rather than a half-torn-down roster.
        CommandBus.SetRouter(null);
        MultiplayerSession.BeginLocal();
    }

    /// <summary>This machine's LAN address — only meaningful on a direct connection.</summary>
    public static string LocalAddress()
    {
        try
        {
            foreach (var ip in System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList)
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                    !System.Net.IPAddress.IsLoopback(ip))
                    return ip.ToString();
        }
        catch { /* no network interface — the fallback below is the honest answer */ }
        return "127.0.0.1";
    }
}
