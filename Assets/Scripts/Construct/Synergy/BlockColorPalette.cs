using UnityEngine;

// Static lookup: BlockColor enum → Unity RGB for tinting tokens / placed
// blocks. Edit the constants here when art locks final hex codes.
//
// Kept as a static class (not ScriptableObject) so any code can call
// BlockColorPalette.Get(piece.color) without scene/asset wiring. If runtime
// tuning becomes important later, promote to an SO with a Get() override.
public static class BlockColorPalette
{
    public static Color Get(BlockColor c) => c switch
    {
        BlockColor.Order         => new Color(0.20f, 0.85f, 0.85f),  // 秩序 — 青
        BlockColor.Harmony       => new Color(0.20f, 0.75f, 0.30f),  // 和谐 — 翡绿
        BlockColor.Abundance     => new Color(1.00f, 0.55f, 0.10f),  // 丰饶 — 橙
        BlockColor.Heresy        => new Color(0.60f, 0.20f, 0.85f),  // 异端 — 紫
        BlockColor.Enlightenment => new Color(0.20f, 0.55f, 1.00f),  // 启发 — 青蓝
        BlockColor.Exploration   => new Color(0.88f, 0.18f, 0.20f),  // 探寻 — 朱红
        BlockColor.Universal     => new Color(0.55f, 0.55f, 0.55f),  // 万能 — 中灰
        _                        => new Color(0.70f, 0.70f, 0.70f),  // None / fallback
    };

    // Yellow used as accent / overlay (Exploration's X mark, etc.) — not a
    // theme, just a fixed accent.
    public static readonly Color AccentYellow = new(0.98f, 0.85f, 0.20f);

    // Short player-facing description of what the synergy does when active.
    // Surfaced by the shop tooltip when hovering a token of this color.
    // Keep under ~60 chars per line — tooltip wraps to ~2 lines.
    public static string Description(BlockColor c) => c switch
    {
        BlockColor.Order         => "3+ same-color connected\n→ debuff enemies on path",
        BlockColor.Harmony       => "all of color connected\n→ buff turrets",
        BlockColor.Abundance     => "form a closed loop\n→ periodic resource income",
        BlockColor.Heresy        => "TBD",
        BlockColor.Enlightenment => "form N×N×N cube\n→ upgrade rewards (tier 2/3/4)",
        BlockColor.Exploration   => "form a long straight line\n→ exploration bonus",
        BlockColor.Universal     => "joker — counts as any color\n(one synergy at a time)",
        _                        => "",
    };
}
