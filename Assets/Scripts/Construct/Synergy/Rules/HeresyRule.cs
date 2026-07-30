using System.Collections.Generic;
using UnityEngine;

// Heresy — placeholder, mechanics TBD. Never fires currently;
// SynergyEvaluator simply skips it.
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
        return false;   // TODO: implement once Heresy mechanics are locked
    }
}
