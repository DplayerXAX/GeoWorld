using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

// The NetworkManager, built in code and kept alive across the scene load.
//
// Not a scene object, for one hard reason: the connection has to SURVIVE the lobby →
// gameplay load. A NetworkManager sitting in the lobby scene dies with that scene and
// drops every client with it; one placed in both scenes is two different managers and
// the handshake never completes. So there is exactly one, created on demand and
// carried across — which also means neither scene has any networking to wire up.
//
// Everything transport-shaped lives here and nowhere else: NgoNetwork owns the
// protocol, this owns the socket.
[DisallowMultipleComponent]
public class NetBootstrap : MonoBehaviour
{
    public const ushort DefaultPort = 7777;

    static NetBootstrap _inst;

    static NgoNetwork _net;

    /// <summary>
    /// The protocol layer, created on first touch. A property rather than a field
    /// set by Host()/Join(): the lobby subscribes to its events BEFORE anyone has
    /// hosted or joined, and reading a null there left a client connected but deaf
    /// to the host telling it the match had started.
    /// </summary>
    public static NgoNetwork Net { get { Ensure(); return _net; } }

    UnityTransport _transport;
    NetworkManager _manager;

    static bool _building;

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

        _net  = go.AddComponent<NgoNetwork>();
        _inst = boot;
        _building = false;
    }

    /// <summary>True once a host or client session is actually on the wire.</summary>
    public static bool Online => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    public static void Host(ushort port = DefaultPort)
    {
        Ensure();
        // 0.0.0.0 so the listener accepts on every interface — bound to a specific
        // LAN address it works locally and refuses everyone else, which looks
        // exactly like a firewall problem and is the harder thing to diagnose.
        _inst._transport.SetConnectionData("0.0.0.0", port, "0.0.0.0");
        Net.StartHost();
    }

    public static void Join(string address, ushort port = DefaultPort)
    {
        Ensure();
        string addr = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
        _inst._transport.SetConnectionData(addr, port);
        Net.StartClient();
    }

    public static void Shutdown()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        // The session goes back to being a local one-player host, so anything that
        // asks ownership questions after a disconnect still gets a valid answer
        // rather than a half-torn-down roster.
        CommandBus.SetRouter(null);
        MultiplayerSession.BeginLocal();
    }

    /// <summary>This machine's LAN address, for the host to read out to everyone else.</summary>
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
