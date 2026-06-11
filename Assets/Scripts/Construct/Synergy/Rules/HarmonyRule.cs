using System.Collections.Generic;
using UnityEngine;

// 和谐 (Harmony) — every piece of `color` (plus jokers usable as `color`)
// must form a single face-connected component.
//
// Stronger than Order (which only needs N connected), so set `minPieces`
// higher to avoid trivial activation. Default priority below Order so Order
// fires first on the same color — bump above Order if you want Harmony to
// preempt.
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

    // Progress = total usable same-colour pieces toward minPieces (they also have
    // to form ONE component to fire, but the count is the useful "how many" hint).
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
