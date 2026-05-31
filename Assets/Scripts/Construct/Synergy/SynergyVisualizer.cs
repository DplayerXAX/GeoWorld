using UnityEngine;

// Per-rule visual overlay for claimed pieces.
//
// SynergyRule.visualizer points at one of these. When a piece gets claimed
// by the rule, OnPieceClaimed runs (typically spawns a decoration prefab,
// adds an overlay material child, swaps shader, etc.). When the piece
// leaves the claim (synergy revoked or piece removed), OnPieceReleased
// runs to tear down.
//
// The default OnPieceReleased just destroys whatever GameObject was
// returned from OnPieceClaimed. Override when teardown is more complex
// (e.g. restore swapped materials, fade out instead of destroy).
//
// SynergyVisualFX dispatches these calls — never invoke directly. It diffs
// the active-synergy claim sets each OnClaimChanged tick and routes per-
// piece claim/release events to the right visualizer.
public abstract class SynergyVisualizer : ScriptableObject
{
    // Called when a piece is newly claimed by the rule that owns this
    // visualizer. Return the spawned GameObject (or null) so the FX
    // manager can hand it back to OnPieceReleased for cleanup.
    public abstract GameObject OnPieceClaimed(PlacedBlockInstance instance, ActiveSynergy active);

    // Default teardown: destroy whatever was spawned. Override if the
    // visualizer mutates state on the host that needs explicit reversal.
    public virtual void OnPieceReleased(PlacedBlockInstance instance, ActiveSynergy active, GameObject spawned)
    {
        if (spawned != null) Object.Destroy(spawned);
    }
}
