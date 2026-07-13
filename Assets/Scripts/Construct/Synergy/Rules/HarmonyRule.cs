using System.Collections.Generic;
using UnityEngine;

// Harmony — every piece of `color` (plus usable jokers) must form a single
// face-connected component. Stronger than Order, so default priority is
// below Order's — bump above it if Harmony should preempt.
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Rules/Harmony Rule",
                 fileName = "HarmonyRule")]
public class HarmonyRule : SynergyRule
{
    [Header("Harmony — all pieces of color connected")]
    [Tooltip("Minimum total pieces required (counts own color + Universal jokers).")]
    [Min(2)] public int minPieces = 4;

    void Reset()
    {
        absorbAdditionalPieces = true;
        priority               = 40;   // < Order(50) by default
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

        if (usable.Count < minPieces) return false;

        var comps = board.ConnectedComponents(usable);
        if (comps.Count != 1) return false;   // must be ONE component

        claimed = comps[0];
        tier    = 1;
        return true;
    }

    // Progress = total usable same-color pieces toward minPieces (still must
    // form one component to fire, but the count is a useful hint).
    public override bool TryGetActivationProgress(BoardSnapshot board, PlacedPiece piece,
                                                  out int current, out int required)
    {
        current  = 0;
        required = Mathf.Max(1, minPieces);
        if (board == null) return true;
        foreach (var p in board.PiecesUsableAs(color)) current++;
        return true;
    }
}
