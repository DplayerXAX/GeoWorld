using System.Collections.Generic;
using UnityEngine;

// 秩序 (Order) — N pieces of the same color, connected face-to-face.
//
// On activation, claims the ENTIRE connected component (not just N), so
// later same-color pieces that connect to the group are absorbed into the
// same synergy rather than spawning a parallel claim. Effect (debuff
// enemies on path) is assigned via the `effect` field on the base class.
//
// To author:
//   1. Project → Create → GeoWorld → Synergy → Rules → Order Rule
//   2. Set `color` to one of the 6 themes (e.g. BlockColor.Order itself,
//      but any theme color works — you can have an "Order rule for green
//      pieces" if you want)
//   3. Drag a GameEffect asset (DebugLogEffect for smoke test, real combat
//      effect later) into the `effect` slot
//   4. Add this asset to SynergyEvaluator.rules in the scene
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Rules/Order Rule",
                 fileName = "OrderRule")]
public class OrderRule : SynergyRule
{
    [Header("Order — N same-color connected")]
    [Tooltip("Minimum number of pieces (own color + Universal jokers) that must form a single face-connected component for the rule to fire.")]
    [Min(2)] public int minConnected = 3;

    // Set sensible defaults when the asset is first created.
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

        if (color == BlockColor.None || color == BlockColor.Universal)
        {
            // Wildcard / unset rule shouldn't fire — Order needs a real theme.
            return false;
        }

        // Restrict to pieces in the pool that can count as our color (own
        // color + Universal jokers).
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

    // Progress = size of the connected same-colour component the piece sits in,
    // toward minConnected (e.g. "2/3").
    public override bool TryGetActivationProgress(BoardSnapshot board, PlacedPiece piece,
                                                  out int current, out int required)
    {
        current  = 0;
        required = Mathf.Max(1, minConnected);
        if (board == null || piece == null) return true;

        var usable = new HashSet<PlacedPiece>(board.PiecesUsableAs(color));
        if (!usable.Contains(piece)) return true;   // 0 / required

        var comps = board.ConnectedComponents(usable);
        for (int i = 0; i < comps.Count; i++)
            if (comps[i].Contains(piece)) { current = comps[i].Count; break; }
        return true;
    }
}
