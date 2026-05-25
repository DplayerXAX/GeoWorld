// Themes that drive the synergy / set-bonus system.
//
// Tags are assigned in BlockData (per BlockType asset). When the player
// places blocks, SynergyEvaluator counts placed blocks by tag and fires the
// matching SynergyDefinition's tier effects.
//
// Names mirror the design brainstorm (Development / Grow / Battle /
// Balance / Risky). Rename freely once the final theme set is locked — the
// only thing that changes is BlockData assets' selected values.
//
// NOTE: "ShapePrize" is intentionally absent. Shape-based bonuses (specific
// cell patterns) are a separate concept handled by ShapePattern assets, not
// tags on individual blocks.
public enum BlockTag
{
    Development,   // economy / income / sell-for-money
    Grow,          // value-over-time, regen, auto-generate
    Battle,        // combat — slow / weaken / buff turret
    Balance,       // variety / hybrid bonuses
    Risky,         // high-risk high-reward
}
