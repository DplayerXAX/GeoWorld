using System.Collections.Generic;
using UnityEngine;

// Order — N pieces of the same color, connected face-to-face. Claims the
// entire connected component on activation, so later same-color pieces
// joining the group get absorbed instead of spawning a parallel claim.
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Rules/Order Rule",
                 fileName = "OrderRule")]
public class OrderRule : SynergyRule
{
    [Header("Order — N same-color connected")]
    [Tooltip("Minimum number of pieces (own color + Universal jokers) that must form a single face-connected component for the rule to fire.")]
    [Min(2)] public int minConnected = 3;

    void Reset()
    {
        absorbAdditionalPieces = true;
        priority               = 50;
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

        if (usable.Count < minConnected) return false;

        // First component that meets the threshold wins.
        var components = board.ConnectedComponents(usable);
        for (int i = 0; i < components.Count; i++)
        {
            if (components[i].Count >= minConnected)
            {
                claimed = components[i];
                tier    = 1;
                return true;
            }
        }
        return false;
    }

    // Progress = size of the connected same-color component the piece sits
    // in, toward minConnected.
    public override bool TryGetActivationProgress(BoardSnapshot board, PlacedPiece piece,
                                                  out int current, out int required)
    {
        current  = 0;
        required = Mathf.Max(1, minConnected);
        if (board == null || piece == null) return true;

        var usable = new HashSet<PlacedPiece>(board.PiecesUsableAs(color));
        if (!usable.Contains(piece)) return true;

        var comps = board.ConnectedComponents(usable);
        for (int i = 0; i < comps.Count; i++)
            if (comps[i].Contains(piece)) { current = comps[i].Count; break; }
        return true;
    }
}
