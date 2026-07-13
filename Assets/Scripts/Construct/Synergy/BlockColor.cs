// 6 themed colors + Universal (joker) + None (untagged).
//
// Each BlockData asset picks exactly one color. Universal grey blocks
// substitute for any color in a single rule evaluation but cannot satisfy
// two different rules with the same substitution (Joker semantics).
//
// `None` is the default for un-authored BlockData — pieces with color
// `None` never participate in any synergy. Set to a real color in the
// inspector when the block becomes part of the synergy roster.
public enum BlockColor
{
    None = 0,

    Universal,      // Joker / wildcard

    Order,          // 3+ same-color pieces connected -> debuff enemies on path
    Harmony,        // all same-color pieces connected -> buff turrets
    Abundance,      // same-color pieces form a closed loop -> periodic income
    Heresy,         // TBD
    Enlightenment,  // same-color pieces form an N x N x N cube -> turret upgrade rewards
    Exploration,    // same-color pieces form a straight line
}
