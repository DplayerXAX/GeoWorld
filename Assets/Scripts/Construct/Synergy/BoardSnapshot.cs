using System.Collections.Generic;
using UnityEngine;

// Live index over all PlacedPieces currently on the board.
//
// Owned and maintained by SynergyEvaluator. Mutated as pieces are placed
// and removed (no full rebuilds). Rule subclasses query this read-only when
// evaluating their patterns.
//
// Scope:
//   • Cell ownership lookup     (which piece occupies cell X)
//   • Per-color piece lists     (with Universal/Joker treatment)
//   • Piece adjacency           (face-sharing across pieces' cells)
//   • Connected components      (BFS over piece graph, restricted to a pool)
//
// What lives in rules instead (NOT here):
//   • Cube detection            (Enlightenment)
//   • Cycle detection           (Abundance)
//   • Straight-line detection   (Exploration)
// Those are pattern-specific and would bloat this class. Keep BoardSnapshot
// generic — anything used by ≥2 rules can be promoted here later.
public sealed class BoardSnapshot
{
    // Six axis-aligned face directions in 3D. Face-sharing is the locked
    // adjacency definition (Q2).
    static readonly Vector3Int[] _faceDirs =
    {
        new(+1, 0, 0), new(-1, 0, 0),
        new(0, +1, 0), new(0, -1, 0),
        new(0, 0, +1), new(0, 0, -1),
    };

    readonly Dictionary<Vector3Int, PlacedPiece>      _cellOwner = new();
    readonly List<PlacedPiece>                        _all       = new();
    readonly Dictionary<BlockColor, List<PlacedPiece>> _byColor   = new();

    // ── Mutation ────────────────────────────────────────────────────────

    // Returns false if any of the piece's cells are already occupied.
    // SynergyEvaluator should treat that as a bug (PlacementController
    // is responsible for ensuring cells are free before placement).
    public bool AddPiece(PlacedPiece p)
    {
        if (p == null) return false;

        for (int i = 0; i < p.cells.Length; i++)
            if (_cellOwner.ContainsKey(p.cells[i])) return false;

        for (int i = 0; i < p.cells.Length; i++)
            _cellOwner[p.cells[i]] = p;

        _all.Add(p);
        BucketFor(p.color).Add(p);
        return true;
    }

    public bool RemovePiece(PlacedPiece p)
    {
        if (p == null) return false;
        if (!_all.Remove(p)) return false;

        for (int i = 0; i < p.cells.Length; i++)
        {
            if (_cellOwner.TryGetValue(p.cells[i], out var owner) && owner == p)
                _cellOwner.Remove(p.cells[i]);
        }
        BucketFor(p.color).Remove(p);
        return true;
    }

    public void Clear()
    {
        _cellOwner.Clear();
        _all.Clear();
        _byColor.Clear();
    }

    // ── Basic reads ─────────────────────────────────────────────────────

    public int PieceCount => _all.Count;
    public IReadOnlyList<PlacedPiece> AllPieces => _all;

    public bool IsOccupied(Vector3Int cell) => _cellOwner.ContainsKey(cell);

    public PlacedPiece GetOwner(Vector3Int cell)
        => _cellOwner.TryGetValue(cell, out var p) ? p : null;

    public IReadOnlyList<PlacedPiece> PiecesOfExactColor(BlockColor c)
        => BucketFor(c);

    // "Pieces that count as color C" — includes Universal jokers by default.
    // Rules should use this when they want jokers as part of their pool.
    // Q3 locked Joker semantics: a joker piece may participate in a single
    // rule per evaluation; the claim-tracking layer in SynergyEvaluator
    // enforces that (each piece can only be claimed by one active rule).
    public IEnumerable<PlacedPiece> PiecesUsableAs(BlockColor c, bool includeJoker = true)
    {
        if (c == BlockColor.None) yield break;

        var exact = BucketFor(c);
        for (int i = 0; i < exact.Count; i++) yield return exact[i];

        if (includeJoker && c != BlockColor.Universal)
        {
            var jokers = BucketFor(BlockColor.Universal);
            for (int i = 0; i < jokers.Count; i++) yield return jokers[i];
        }
    }

    // ── Adjacency ───────────────────────────────────────────────────────

    public bool ArePiecesAdjacent(PlacedPiece a, PlacedPiece b)
    {
        if (a == null || b == null || a == b) return false;

        for (int i = 0; i < a.cells.Length; i++)
        {
            var c = a.cells[i];
            for (int d = 0; d < _faceDirs.Length; d++)
            {
                if (_cellOwner.TryGetValue(c + _faceDirs[d], out var owner) && owner == b)
                    return true;
            }
        }
        return false;
    }

    // All pieces that share a face with any cell of `p` (excludes p itself).
    // Allocates a fresh HashSet per call — cheap for typical board sizes,
    // but if this shows up in a profiler, accept a reusable buffer instead.
    public HashSet<PlacedPiece> NeighborsOf(PlacedPiece p)
    {
        var result = new HashSet<PlacedPiece>();
        if (p == null) return result;

        for (int i = 0; i < p.cells.Length; i++)
        {
            var c = p.cells[i];
            for (int d = 0; d < _faceDirs.Length; d++)
            {
                if (_cellOwner.TryGetValue(c + _faceDirs[d], out var owner) && owner != null && owner != p)
                    result.Add(owner);
            }
        }
        return result;
    }

    // ── Connected components ────────────────────────────────────────────

    // BFS over piece adjacency restricted to `pool`. Returns one HashSet
    // per connected sub-graph. Pieces outside `pool` are ignored even if
    // they bridge two pool pieces.
    public List<HashSet<PlacedPiece>> ConnectedComponents(IEnumerable<PlacedPiece> pool)
    {
        var poolSet = pool as HashSet<PlacedPiece> ?? new HashSet<PlacedPiece>(pool);
        var components = new List<HashSet<PlacedPiece>>();
        var visited    = new HashSet<PlacedPiece>();
        var queue      = new Queue<PlacedPiece>();

        foreach (var seed in poolSet)
        {
            if (!visited.Add(seed)) continue;

            var component = new HashSet<PlacedPiece> { seed };
            queue.Clear();
            queue.Enqueue(seed);

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var n in NeighborsOf(cur))
                {
                    if (!poolSet.Contains(n)) continue;
                    if (visited.Add(n))
                    {
                        component.Add(n);
                        queue.Enqueue(n);
                    }
                }
            }
            components.Add(component);
        }
        return components;
    }

    // ── Internals ───────────────────────────────────────────────────────

    List<PlacedPiece> BucketFor(BlockColor c)
    {
        if (!_byColor.TryGetValue(c, out var list))
        {
            list = new List<PlacedPiece>();
            _byColor[c] = list;
        }
        return list;
    }
}
