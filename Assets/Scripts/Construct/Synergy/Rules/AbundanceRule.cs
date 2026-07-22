using System.Collections.Generic;
using UnityEngine;

// Abundance — pieces of `color` (plus jokers) form a closed loop.
//
// Detection: a connected component has a cycle iff edge count >= node count.
// Claims the entire connected component (tails included) so it's locked as
// one structure, but ICellHighlightFilter only reports cells ON the loop —
// AbundanceHarvestEffect's payout counting respects this so tails don't
// inflate the currency count.
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

        // Need at least two pieces for any loop — a lone tetromino can't ring a hole.
        if (usable.Count < 2) { _loopCells.Clear(); return false; }

        // Path 1 — cycle in the PIECE-adjacency graph (≥3 pieces meeting in a ring).
        // Smallest such cycle is a triangle of 3 pieces.
        if (usable.Count >= 3)
        {
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
        }

        // Path 2 — hollow loop in CELL space. Catches rings the piece graph can't
        // see: e.g. TWO L-pieces interlocking into a hollow square. Two pieces are
        // only ONE edge in the piece graph (never a cycle), but their cells form a
        // real ring enclosing an empty cell. Requiring an ENCLOSED empty cell keeps
        // a solid block (2×2, filled square) from counting — only true "hollow" loops.
        if (TryHollowLoop(usable, out claimed, out var hollowCells))
        {
            tier       = 1;
            _loopCells = hollowCells;
            return true;
        }

        _loopCells.Clear();
        return false;
    }

    // ── Cell-space hollow-loop detection ────────────────────────────────────────
    static readonly Vector2Int[] _dir2 = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    static int        AxisCoord(Vector3Int c, int a) => a == 0 ? c.x : a == 1 ? c.y : c.z;
    static Vector2Int InPlane  (Vector3Int c, int a) =>
        a == 0 ? new Vector2Int(c.y, c.z)
      : a == 1 ? new Vector2Int(c.x, c.z)
      :          new Vector2Int(c.x, c.y);

    // Loops are planar, so scan each of the three axis-aligned slice orientations:
    // group the usable cells by their coordinate on that axis, and within each 2D
    // slice look for an empty cell the wall fully encloses. If found, the ring
    // (wall cells left after peeling tails) IS the loop; its owner pieces are claimed.
    static bool TryHollowLoop(HashSet<PlacedPiece> usable,
                              out HashSet<PlacedPiece> claimed, out HashSet<Vector3Int> loopCells)
    {
        claimed = null; loopCells = null;

        var owner = new Dictionary<Vector3Int, PlacedPiece>();
        foreach (var p in usable)
            if (p?.cells != null)
                foreach (var c in p.cells) owner[c] = p;
        if (owner.Count < 8) return false;   // smallest hollow ring (3×3 border) = 8 cells

        for (int axis = 0; axis < 3; axis++)
        {
            var slices = new Dictionary<int, Dictionary<Vector2Int, Vector3Int>>();
            foreach (var c in owner.Keys)
            {
                int key = AxisCoord(c, axis);
                if (!slices.TryGetValue(key, out var map)) { map = new(); slices[key] = map; }
                map[InPlane(c, axis)] = c;
            }

            foreach (var kv in slices)
            {
                var map = kv.Value;                 // (u,v) → world cell for this slice's wall
                if (map.Count < 8) continue;

                // Split the slice's wall into 4-connected COMPONENTS first. Two
                // separate hollow loops sitting at the same axis coordinate (very
                // common — most builds stay on one Y level) are two disjoint wall
                // blobs; treating the whole slice as one wall would merge both
                // rings' cells into a single claim instead of letting each become
                // its own ActiveSynergy (which is what made repeated activations
                // collapse into "just the one, biggest" claim instead of stacking).
                foreach (var comp in ConnectedComponents2D(map.Keys))
                {
                    if (comp.Count < 8) continue;
                    if (!EnclosesEmpty(comp)) continue;

                    var ring = PeelToRing(comp);    // drop dangling tails → just this loop
                    if (ring.Count == 0) continue;

                    loopCells = new HashSet<Vector3Int>();
                    claimed   = new HashSet<PlacedPiece>();
                    foreach (var uv in ring)
                    {
                        var cell = map[uv];
                        loopCells.Add(cell);
                        claimed.Add(owner[cell]);       // a piece is claimed as a whole unit
                    }
                    return true;   // one ring per call — the caller's guard loop finds the rest
                }
            }
        }
        return false;
    }

    // 4-adjacency flood-fill grouping in the 2D slice plane.
    static List<HashSet<Vector2Int>> ConnectedComponents2D(IEnumerable<Vector2Int> cells)
    {
        var all      = cells as HashSet<Vector2Int> ?? new HashSet<Vector2Int>(cells);
        var visited  = new HashSet<Vector2Int>();
        var result   = new List<HashSet<Vector2Int>>();
        var q        = new Queue<Vector2Int>();

        foreach (var seed in all)
        {
            if (!visited.Add(seed)) continue;
            var comp = new HashSet<Vector2Int> { seed };
            q.Clear(); q.Enqueue(seed);
            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                foreach (var d in _dir2)
                {
                    var n = cur + d;
                    if (!all.Contains(n)) continue;
                    if (visited.Add(n)) { comp.Add(n); q.Enqueue(n); }
                }
            }
            result.Add(comp);
        }
        return result;
    }

    // Flood-fill the slice's bounding box (expanded by 1 so its border is all
    // "outside") from a corner, through NON-wall cells. Any in-box non-wall cell the
    // flood can't reach is walled off → the loop encloses a hole.
    static bool EnclosesEmpty(IEnumerable<Vector2Int> wallCells)
    {
        var wall = wallCells as HashSet<Vector2Int> ?? new HashSet<Vector2Int>(wallCells);
        int minU = int.MaxValue, minV = int.MaxValue, maxU = int.MinValue, maxV = int.MinValue;
        foreach (var c in wall)
        {
            minU = Mathf.Min(minU, c.x); maxU = Mathf.Max(maxU, c.x);
            minV = Mathf.Min(minV, c.y); maxV = Mathf.Max(maxV, c.y);
        }
        int loU = minU - 1, hiU = maxU + 1, loV = minV - 1, hiV = maxV + 1;

        var outside = new HashSet<Vector2Int>();
        var q       = new Queue<Vector2Int>();
        var start   = new Vector2Int(loU, loV);
        outside.Add(start); q.Enqueue(start);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            foreach (var d in _dir2)
            {
                var n = cur + d;
                if (n.x < loU || n.x > hiU || n.y < loV || n.y > hiV) continue;
                if (wall.Contains(n)) continue;
                if (outside.Add(n)) q.Enqueue(n);
            }
        }

        for (int u = minU; u <= maxU; u++)
            for (int v = minV; v <= maxV; v++)
            {
                var c = new Vector2Int(u, v);
                if (!wall.Contains(c) && !outside.Contains(c)) return true;   // enclosed empty
            }
        return false;
    }

    // Peel degree≤1 nodes from the in-plane 4-adjacency graph; what survives is the
    // union of all cycles (the loop), with dangling tails removed.
    static HashSet<Vector2Int> PeelToRing(IEnumerable<Vector2Int> wallCells)
    {
        var wall = wallCells as HashSet<Vector2Int> ?? new HashSet<Vector2Int>(wallCells);
        var deg  = new Dictionary<Vector2Int, int>();
        foreach (var c in wall)
        {
            int d = 0;
            foreach (var dd in _dir2) if (wall.Contains(c + dd)) d++;
            deg[c] = d;
        }

        var alive = new HashSet<Vector2Int>(wall);
        var queue = new Queue<Vector2Int>();
        foreach (var kv in deg) if (kv.Value <= 1) queue.Enqueue(kv.Key);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            if (!alive.Remove(c)) continue;
            foreach (var dd in _dir2)
            {
                var n = c + dd;
                if (!alive.Contains(n)) continue;
                deg[n]--;
                if (deg[n] <= 1) queue.Enqueue(n);
            }
        }
        return alive;
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

    // "Peel the leaves": repeatedly strip nodes with degree <= 1; what's left
    // is exactly the union of every cycle in the graph. Returns their cells.
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
