using System.Collections.Generic;
using UnityEngine;

// 丰饶 (Abundance) — pieces of `color` (plus jokers) form a closed loop.
//
// "Closed loop" = the piece adjacency graph for this color contains at
// least one cycle. Detection: a connected component has a cycle iff its
// edge count >= node count (forest has exactly nodes-1 edges).
//
// On activation, claims the ENTIRE connected component containing the
// cycle, not just the cycle vertices — tails hanging off the loop are still
// LOCKED as part of the structure (a piece there can't be independently
// snatched by another rule), so the visual "the loop and its tails read as
// one abundance" holds. But per ICellHighlightFilter, only cells that
// actually lie ON the loop (not a tail) are reported as "participating" —
// SynergyEffectUtil's counting helpers (which AbundanceHarvestEffect uses
// for its per-turn payout) respect this filter, so tails claimed-but-not-
// looped don't inflate the currency count.
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Rules/Abundance Rule",
                 fileName = "AbundanceRule")]
public class AbundanceRule : SynergyRule, ICellHighlightFilter
{
    [System.NonSerialized] HashSet<Vector3Int> _loopCells = new();

    public bool ShouldHighlight(Vector3Int worldCell) => _loopCells.Contains(worldCell);

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
        if (usable.Count < 3) { _loopCells.Clear(); return false; }

        var comps = board.ConnectedComponents(usable);
        for (int i = 0; i < comps.Count; i++)
        {
            if (comps[i].Count < 3) continue;
            if (ContainsCycle(board, comps[i]))
            {
                claimed    = comps[i];
                tier       = 1;
                _loopCells = ComputeLoopCells(board, comps[i]);
                return true;
            }
        }
        _loopCells.Clear();
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

    // Which pieces actually lie ON a cycle (vs. a tree-shaped tail hanging off
    // one) — classic "peel the leaves" reduction: repeatedly strip nodes with
    // degree ≤ 1 (dead ends) inside the component; whatever's left once no more
    // can be peeled is exactly the union of every cycle in the graph. Returns
    // the WORLD CELLS of those surviving pieces.
    static HashSet<Vector3Int> ComputeLoopCells(BoardSnapshot board, HashSet<PlacedPiece> comp)
    {
        var degree = new Dictionary<PlacedPiece, int>();
        foreach (var p in comp)
        {
            int d = 0;
            foreach (var n in board.NeighborsOf(p)) if (comp.Contains(n)) d++;
            degree[p] = d;
        }

        var alive = new HashSet<PlacedPiece>(comp);
        var queue = new Queue<PlacedPiece>();
        foreach (var kv in degree) if (kv.Value <= 1) queue.Enqueue(kv.Key);

        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            if (!alive.Remove(p)) continue;
            foreach (var n in board.NeighborsOf(p))
            {
                if (!alive.Contains(n)) continue;
                degree[n]--;
                if (degree[n] <= 1) queue.Enqueue(n);
            }
        }

        var cells = new HashSet<Vector3Int>();
        foreach (var p in alive)
            if (p?.cells != null)
                foreach (var c in p.cells) cells.Add(c);
        return cells;
    }
}
