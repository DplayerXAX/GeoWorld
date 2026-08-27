using System;
using System.Collections.Generic;
using UnityEngine;

// Submit → route → validate → apply. The one path a board-changing action may take.
//
// Deliberately transport-free. A router is anything that can get a command from the
// issuing machine to the authority; LocalRouter is the degenerate case where those
// are the same machine, which is exactly single-player. Swapping in a networked
// router later changes NOTHING above or below this class — that's the point of
// building it before picking a stack.
//
// Flow:
//   input code            → CommandBus.Submit(cmd)
//   router (local or net) → CommandBus.Deliver(cmd)   [authority only]
//   Deliver               → Validate → Apply → Applied event (everyone)
public interface ICommandRouter
{
    /// <summary>Carry a locally-issued command to the authority.</summary>
    void Send(GameCommand cmd);
}

public static class CommandBus
{
    // ── Routing ──────────────────────────────────────────────────────────────

    // Straight through: submit lands in Deliver on the same frame, same machine.
    // Single-player therefore runs the identical validate/apply path multiplayer
    // does, so a bug in that path shows up in the mode that gets played most.
    class LocalRouter : ICommandRouter
    {
        public void Send(GameCommand cmd) => Deliver(cmd);
    }

    static ICommandRouter _router = new LocalRouter();

    public static void SetRouter(ICommandRouter router) => _router = router ?? new LocalRouter();

    // ── Hooks the game fills in ──────────────────────────────────────────────

    /// <summary>
    /// Authority-side rule check. Returns null/empty to accept, or a reason to
    /// reject. Set by whatever owns the rules (PlacementController, GameFlowManager);
    /// the bus itself knows no rules on purpose — it would end up duplicating them.
    /// </summary>
    public static Func<GameCommand, string> Validator;

    /// <summary>Authority-side mutation. Only ever called after Validator accepted.</summary>
    public static Action<GameCommand> Applier;

    /// <summary>Raised on every machine once a command has been applied.</summary>
    public static event Action<GameCommand> Applied;

    /// <summary>Raised on the ISSUING machine when the authority refused.</summary>
    public static event Action<GameCommand, string> Rejected;

    // ── Submit / deliver ─────────────────────────────────────────────────────

    static readonly Dictionary<int, int> _nextSeq  = new();   // playerId → next sequence to issue
    static readonly Dictionary<int, int> _lastSeen = new();   // playerId → highest applied sequence

    /// <summary>
    /// Issue a command as the local player. Stamps the player id and sequence here
    /// rather than trusting the caller — a caller that can choose its own player id
    /// is a caller that can act as someone else.
    /// </summary>
    public static void Submit(GameCommand cmd)
    {
        cmd.playerId = MultiplayerSession.LocalId;

        _nextSeq.TryGetValue(cmd.playerId, out int seq);
        cmd.sequence = seq;
        _nextSeq[cmd.playerId] = seq + 1;

        _router.Send(cmd);
    }

    /// <summary>
    /// Authority entry point. A networked router calls this on the host with commands
    /// received from clients; LocalRouter calls it directly.
    /// </summary>
    public static void Deliver(GameCommand cmd)
    {
        if (!MultiplayerSession.IsHost) return;   // only the authority applies

        // Duplicate suppression. A resend after a dropped ack is normal, and applying
        // a placement twice would charge twice and leave a ghost block.
        if (_lastSeen.TryGetValue(cmd.playerId, out int seen) && cmd.sequence <= seen && cmd.sequence != 0)
            return;

        string reject = Validator?.Invoke(cmd);
        if (!string.IsNullOrEmpty(reject))
        {
            Rejected?.Invoke(cmd, reject);
            return;
        }

        _lastSeen[cmd.playerId] = cmd.sequence;
        Applier?.Invoke(cmd);
        Applied?.Invoke(cmd);
    }

    /// <summary>
    /// Client-side entry point for a command the authority has already accepted —
    /// applied without re-validating, because the authority's verdict is the only
    /// one that counts and re-running the rules on a client with a slightly
    /// different board is how desyncs start.
    /// </summary>
    public static void ApplyRemote(GameCommand cmd)
    {
        if (MultiplayerSession.IsHost) return;   // the host already applied it in Deliver
        Applier?.Invoke(cmd);
        Applied?.Invoke(cmd);
    }

    /// <summary>Wipes sequence bookkeeping. Call on run start — see GameFlowManager.</summary>
    public static void Reset()
    {
        _nextSeq.Clear();
        _lastSeen.Clear();
    }
}
