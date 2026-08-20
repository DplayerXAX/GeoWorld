using System.Collections.Generic;

// How much each player has spent this run.
//
// The resource pool is SHARED — one wallet for the whole table — so "how much does
// this player have" has the same answer for everyone and says nothing. What is
// actually per-player is what each of them has taken out of it, and that is the
// number worth putting on a roster.
//
// Accumulated from the command stream rather than tracked at the point of purchase,
// which means every machine derives the identical figures from the identical
// commands. Nothing extra goes over the wire for it.
public static class PlayerSpend
{
    static readonly Dictionary<int, int> _spent = new();

    public static void Add(int playerId, int amount)
    {
        if (amount <= 0) return;
        _spent.TryGetValue(playerId, out int cur);
        _spent[playerId] = cur + amount;
    }

    public static int Of(int playerId) => _spent.TryGetValue(playerId, out int v) ? v : 0;

    /// <summary>Called on run start, alongside CommandBus.Reset.</summary>
    public static void Clear() => _spent.Clear();
}
