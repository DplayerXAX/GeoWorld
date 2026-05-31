using System.Collections.Generic;
using UnityEngine;

// 探寻 (Exploration) — the longest uninterrupted run of same-color cells
// along any axis (X / Y / Z) reaches `minLength`.
//
// "Run" is measured in CELLS, not pieces, so multi-cell pieces (I3, I4)
// contribute their full length. The claimed set is every piece owning at
// least one cell of the longest run — those pieces may extend beyond the
// line and are still claimed in full.
//
// Tie-breaking: the FIRST axis/start found wins. Deterministic given the
// underlying iteration order; rarely matters because the longest run is
// usually unique.
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Rules/Exploration Rule",
                 fileName = "ExplorationRule")]
public class ExplorationRule : SynergyRule
{
    [Header("Exploration — straight line of N+ cells")]
    [Tooltip("Minimum cell count in the longest run for the rule to fire.")]
    [Min(2)] public int minLength = 4;

    static readonly Vector3Int[] _axes =
    {
        new(1, 0, 0),
        new(0, 1, 0),
        new(0, 0, 1),
    };

    void Reset()
    {
        absorbAdditionalPieces = true;
        priority               = 20;
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

        if (usable.Count == 0) return false;

        // Flatten to a cell set for fast adjacency walking.
        var cells = new HashSet<Vector3Int>();
        foreach (var p in usable)
            for (int i = 0; i < p.cells.Length; i++)
                cells.Add(p.cells[i]);

        if (cells.Count < minLength) return false;

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

        if (bestRun == null) return false;

        var pieces = new HashSet<PlacedPiece>();
        foreach (var rc in bestRun)
        {
            var owner = board.GetOwner(rc);
            if (owner != null && usable.Contains(owner)) pieces.Add(owner);
        }
        if (pieces.Count == 0) return false;

        claimed = pieces;
        tier    = 1;
        return true;
    }
}
