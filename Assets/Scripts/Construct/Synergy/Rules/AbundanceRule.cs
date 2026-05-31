using System.Collections.Generic;
using UnityEngine;

// 丰饶 (Abundance) — pieces of `color` (plus jokers) form a closed loop.
//
// "Closed loop" = the piece adjacency graph for this color contains at
// least one cycle. Detection: a connected component has a cycle iff its
// edge count >= node count (forest has exactly nodes-1 edges).
//
// On activation, claims the ENTIRE connected component containing the
// cycle, not just the cycle vertices. Slightly over-claims but predictable
// — players see "the loop and its tails are all part of the abundance".
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Rules/Abundance Rule",
                 fileName = "AbundanceRule")]
public class AbundanceRule : SynergyRule
{
    void Reset()
    {
        absorbAdditionalPieces = true;
        priority               = 30;
    }

    public override bool TryEvaluate(BoardSnapshot board, HashSet<PlacedPiece> pool,
                                     out HashSet<PlacedPiece> claimed, out int tier)
    {
        claimed = null;
        tier    = 0;

        if (color == BlockColor.None || color == BlockColor.Universal) return false;

        var usable = new HashSet<PlacedPiece>();
        foreach (var p in board.PiecesUsableAs(color))
            if (pool.Contains(p)) usable.Add(p);

        // Smallest cycle uses 3 pieces (triangle in piece adjacency graph).
        if (usable.Count < 3) return false;

        var comps = board.ConnectedComponents(usable);
        for (int i = 0; i < comps.Count; i++)
        {
            if (comps[i].Count < 3) continue;
            if (ContainsCycle(board, comps[i]))
            {
                claimed = comps[i];
                tier    = 1;
                return true;
            }
        }
        return false;
    }

    // |edges| >= |nodes| ⇒ at least one cycle exists (any tree has nodes-1).
    static bool ContainsCycle(BoardSnapshot board, HashSet<PlacedPiece> comp)
    {
        int doubledEdges = 0;   // each undirected edge counted from both ends
        foreach (var p in comp)
            foreach (var n in board.NeighborsOf(p))
                if (comp.Contains(n)) doubledEdges++;
        int edges = doubledEdges / 2;
        return edges >= comp.Count;
    }
}
