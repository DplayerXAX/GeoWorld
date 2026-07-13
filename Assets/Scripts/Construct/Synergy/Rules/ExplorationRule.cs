using System.Collections.Generic;
using UnityEngine;

// Exploration — the longest uninterrupted run of same-color cells along any
// axis reaches `minLength`. Measured in cells, not pieces, so multi-cell
// pieces contribute their full length. Claimed pieces may extend beyond the
// line; only in-line cells are decorated via ICellHighlightFilter.
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Rules/Exploration Rule",
                 fileName = "ExplorationRule")]
public class ExplorationRule : SynergyRule, ICellHighlightFilter
{
    [Header("Exploration — straight line of N+ cells")]
    [Tooltip("Minimum cell count in the longest run for the rule to fire.")]
    [Min(2)] public int minLength = 7;

    static readonly Vector3Int[] _axes =
    {
        new(1, 0, 0),
        new(0, 1, 0),
        new(0, 0, 1),
    };

    [System.NonSerialized] readonly HashSet<Vector3Int> _lineCells = new();

    void Reset()
    {
        absorbAdditionalPieces = true;
        priority               = 20;
    }

    public bool ShouldHighlight(Vector3Int worldCell) => _lineCells.Contains(worldCell);

    public override bool TryEvaluate(BoardSnapshot board, HashSet<PlacedPiece> pool,
                                     out HashSet<PlacedPiece> claimed, out int tier)
    {
        claimed = null;
        tier    = 0;

        if (color == BlockColor.None || color == BlockColor.Universal)
        {
            _lineCells.Clear();
            return false;
        }

        var usable = new HashSet<PlacedPiece>();
        foreach (var p in board.PiecesUsableAs(color))
            if (pool.Contains(p)) usable.Add(p);

        if (usable.Count == 0) { _lineCells.Clear(); return false; }

        // Flatten to a cell set for fast adjacency walking.
        var cells = new HashSet<Vector3Int>();
        foreach (var p in usable)
            for (int i = 0; i < p.cells.Length; i++)
                cells.Add(p.cells[i]);

        if (cells.Count < minLength) { _lineCells.Clear(); return false; }

        HashSet<Vector3Int> bestRun = null;
        int bestLen = 0;

        for (int a = 0; a < _axes.Length; a++)
        {
            var axis = _axes[a];
            foreach (var seed in cells)
            {
                // Only start counting at run heads — skip if predecessor exists.
                if (cells.Contains(seed - axis)) continue;

                int len = 0;
                var run = new HashSet<Vector3Int>();
                var c = seed;
                while (cells.Contains(c))
                {
                    run.Add(c);
                    len++;
                    c += axis;
                }

                if (len >= minLength && len > bestLen)
                {
                    bestLen = len;
                    bestRun = run;
                }
            }
        }

        if (bestRun == null) { _lineCells.Clear(); return false; }

        // Snapshot the line cells for the cell-highlight filter.
        _lineCells.Clear();
        foreach (var c in bestRun) _lineCells.Add(c);

        var pieces = new HashSet<PlacedPiece>();
        foreach (var rc in bestRun)
        {
            var owner = board.GetOwner(rc);
            if (owner != null && usable.Contains(owner)) pieces.Add(owner);
        }
        if (pieces.Count == 0) { _lineCells.Clear(); return false; }

        claimed = pieces;
        tier    = 1;
        return true;
    }
}
