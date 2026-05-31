using System.Collections.Generic;
using UnityEngine;

// 异端 (Heresy) — placeholder. Mechanics TBD.
//
// Possible directions discussed in design (pick one when ready, replace
// TryEvaluate accordingly):
//   • Isolated pieces — pieces of `color` with NO same-color neighbors
//     (heretics that stand alone). Counter-pressure to Order/Harmony.
//   • Pattern-breaker — fires when two pieces of OPPOSING colors share a
//     face (e.g., Order red touching Heresy purple).
//   • Anti-cube — N pieces of `color` placed in a non-cube, non-loop, non-
//     line shape (a "messy" cluster). Rewards builds that resist tidy
//     synergies.
//   • Inverse of another rule — fires when a target synergy is NOT active.
//
// This rule never fires right now. SynergyEvaluator will simply skip it,
// no harm. Fill in TryEvaluate when design locks.
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Rules/Heresy Rule (TBD)",
                 fileName = "HeresyRule")]
public class HeresyRule : SynergyRule
{
    void Reset()
    {
        priority = 10;   // lowest — should fire last among current rules
    }

    public override bool TryEvaluate(BoardSnapshot board, HashSet<PlacedPiece> pool,
                                     out HashSet<PlacedPiece> claimed, out int tier)
    {
        claimed = null;
        tier    = 0;
        return false;   // TODO: implement once 异端 mechanics are locked
    }
}
