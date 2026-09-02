using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// Host-authoritative combat.
//
// Until now the board was shared and the FIGHT was not: every machine ran its own
// spawner, its own enemies and its own clock, so four players watched four different
// battles that happened to be on the same terrain. Enemies stood in different places,
// kills were counted separately, and one player could already be dead while another
// was winning. That is not latency — it is four games.
//
// The fix is not to make the four simulations agree. Deterministic lockstep would
// need every float in pathing, movement and damage to land identically on four CPUs,
// and it drifts. Instead ONE machine simulates and the others watch:
//
//   host    runs EnemyBaseManager exactly as it does in single-player
//   clients run no spawner at all; they hold proxies driven by the host's snapshots
//
// Sent as a whole snapshot rather than as events. Spawn/damage/death events would
// need reliable ordered delivery and a reconciliation path for every one that went
// missing; a snapshot is self-correcting by construction — miss one and the next
// fixes everything it would have told you.
[DisallowMultipleComponent]
public class CombatSync : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Spawn()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        TrySpawn();
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene s,
                              UnityEngine.SceneManagement.LoadSceneMode m) => TrySpawn();

    static void TrySpawn()
    {
        if (GameFlowManager.Instance == null) return;          // gameplay scene only
        if (FindFirstObjectByType<CombatSync>() != null) return;
        new GameObject("CombatSync").AddComponent<CombatSync>();
    }

    public static CombatSync Instance { get; private set; }

    /// <summary>
    /// True when this machine must NOT run combat simulation of its own.
    /// Everything that spawns, moves, damages or kills an enemy checks this.
    /// </summary>
    public static bool IsSpectator =>
        MultiplayerSession.ConnectedCount > 1 && !MultiplayerSession.IsHost;

    // 15Hz, the same rate the block ghosts use. Enemies move on a musical beat —
    // several hundred milliseconds a step — so the interpolator has far more to work
    // with than it would with free-running movement, and a faster rate would buy
    // nothing but bandwidth.
    const float SendInterval = 1f / 15f;

    // Dropped after this long unheard from. Long enough to ride out a couple of
    // missed unreliable packets, short enough that a killed enemy does not linger.
    const float StaleAfter = 0.8f;

    float _sendTimer;

    void Awake()  => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; ClearProxies(); }

    void Update()
    {
        if (MultiplayerSession.ConnectedCount <= 1) return;

        if (MultiplayerSession.IsHost) BroadcastSnapshot();
        else                           ReapStaleProxies();
    }

    // ── Host → clients ───────────────────────────────────────────────────────

    void BroadcastSnapshot()
    {
        _sendTimer -= Time.unscaledDeltaTime;
        if (_sendTimer > 0f) return;
        _sendTimer = SendInterval;

        var net = NgoNetwork.Instance;
        var mgr = EnemyBaseManager.Instance;
        if (net == null || mgr == null) return;

        var list = mgr.ActiveEnemies;
        var snap = new List<EnemyWire>(list.Count);

        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            if (e == null) continue;
            snap.Add(new EnemyWire
            {
                id       = e.GetInstanceID(),
                pos      = e.transform.position,
                rot      = e.transform.rotation,
                health   = (short)Mathf.Clamp(e.CurrentHealth, 0, short.MaxValue),
                maxHealth= (short)Mathf.Clamp(e.maxHealth, 0, short.MaxValue),
            });
        }

        net.BroadcastEnemies(snap, PlayerHealth.Instance != null ? PlayerHealth.Instance.CurrentLives : 0);
    }

    // ── Clients ──────────────────────────────────────────────────────────────

    class Proxy
    {
        public EnemySurfaceUnit unit;
        public Vector3    from, to;
        public Quaternion rotFrom, rotTo;
        public float      t, dur;
        public float      lastSeen;
    }

    readonly Dictionary<int, Proxy> _proxies = new();

    /// <summary>Called from NgoNetwork when a snapshot lands. Client side only.</summary>
    public void ApplySnapshot(List<EnemyWire> enemies, int hostLives)
    {
        var mgr = EnemyBaseManager.Instance;
        if (mgr == null) return;

        foreach (var w in enemies)
        {
            if (!_proxies.TryGetValue(w.id, out var p))
            {
                var unit = mgr.SpawnProxy();
                if (unit == null) continue;
                _proxies[w.id] = p = new Proxy
                {
                    unit = unit,
                    from = w.pos, to = w.pos,
                    rotFrom = w.rot, rotTo = w.rot,
                };
                unit.transform.SetPositionAndRotation(w.pos, w.rot);
            }

            // Interpolate FROM WHERE THE PROXY ACTUALLY IS, not from the previous
            // snapshot's position. If a packet was dropped the proxy is somewhere
            // between the two, and restarting the lerp from stale data would snap it
            // backwards before moving it forwards again.
            p.from    = p.unit.transform.position;
            p.rotFrom = p.unit.transform.rotation;
            p.to      = w.pos;
            p.rotTo   = w.rot;
            p.t       = 0f;
            p.dur     = SendInterval;
            p.lastSeen = Time.unscaledTime;

            p.unit.ApplyRemoteHealth(w.health, w.maxHealth);
        }

        // The host's word on how the run is going. Lives are the one number that
        // decides whether the game is over, so it cannot be four separate opinions.
        PlayerHealth.Instance?.ApplyRemoteLives(hostLives);
    }

    void LateUpdate()
    {
        if (!IsSpectator) return;

        foreach (var kv in _proxies)
        {
            var p = kv.Value;
            if (p.unit == null) continue;

            p.t += Time.unscaledDeltaTime;
            float k = p.dur > 0f ? Mathf.Clamp01(p.t / p.dur) : 1f;
            p.unit.transform.SetPositionAndRotation(
                Vector3.Lerp(p.from, p.to, k),
                Quaternion.Slerp(p.rotFrom, p.rotTo, k));
        }
    }

    // A proxy the host stopped mentioning is a proxy that died or reached the end.
    // Either way it should go — and going quietly is right, because the host already
    // played whatever effect belonged to it on its own machine and the client sees
    // the same thing via its own death FX hook.
    void ReapStaleProxies()
    {
        var gone = new List<int>();
        foreach (var kv in _proxies)
            if (Time.unscaledTime - kv.Value.lastSeen > StaleAfter) gone.Add(kv.Key);

        foreach (var id in gone)
        {
            var p = _proxies[id];
            if (p.unit != null) p.unit.KillProxy();
            _proxies.Remove(id);
        }
    }

    void ClearProxies()
    {
        foreach (var kv in _proxies) if (kv.Value.unit != null) Destroy(kv.Value.unit.gameObject);
        _proxies.Clear();
    }
}

// One enemy on the wire. Kept to the minimum that can draw and read as an enemy:
// where it is, which way it faces, and how hurt it is.
public struct EnemyWire : INetworkSerializable
{
    public int        id;
    public Vector3    pos;
    public Quaternion rot;
    public short      health;
    public short      maxHealth;

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref id);
        s.SerializeValue(ref pos);
        s.SerializeValue(ref rot);
        s.SerializeValue(ref health);
        s.SerializeValue(ref maxHealth);
    }
}
