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

    Universal,      // 万能灰 — Joker / wildcard

    Order,          // 秩序   — 3 同色 piece 相连 → debuff 路径上的怪
    Harmony,        // 和谐   — 同色所有 piece 连通 → buff turret
    Abundance,      // 丰饶   — 同色形成闭环 → 周期性资源
    Heresy,         // 异端   — TBD
    Enlightenment,  // 启发   — 同色 piece 拼成 N×N×N 立方 → 升级炮台等奖励
    Exploration,    // 探寻   — 同色 piece 拼成直线
}
