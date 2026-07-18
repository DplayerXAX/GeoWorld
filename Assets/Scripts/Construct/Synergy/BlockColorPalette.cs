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
        BlockColor.Order         => new Color(0.20f, 0.85f, 0.85f),  // Order — cyan
        BlockColor.Harmony       => new Color(0.20f, 0.75f, 0.30f),  // Harmony — jade green
        BlockColor.Abundance     => new Color(1.00f, 0.55f, 0.10f),  // Abundance — orange
        BlockColor.Heresy        => new Color(0.60f, 0.20f, 0.85f),  // Heresy — purple
        BlockColor.Enlightenment => new Color(0.20f, 0.55f, 1.00f),  // Enlightenment — azure
        BlockColor.Exploration   => new Color(0.88f, 0.18f, 0.20f),  // Exploration — vermilion
        BlockColor.Universal     => new Color(0.55f, 0.55f, 0.55f),  // Universal — mid grey
        _                        => new Color(0.70f, 0.70f, 0.70f),  // None / fallback
    };

    // Yellow used as accent / overlay (Exploration's X mark, etc.) — not a
    // theme, just a fixed accent.
    public static readonly Color AccentYellow = new(0.98f, 0.85f, 0.20f);

    // The six synergy themes (no None, no Universal) — iterate this instead of
    // hand-listing them at every call site.
    public static readonly BlockColor[] Themes =
    {
        BlockColor.Order, BlockColor.Harmony, BlockColor.Abundance,
        BlockColor.Heresy, BlockColor.Enlightenment, BlockColor.Exploration,
    };

    // Which palette entry an arbitrary colour is closest to.
    //
    // Compared in HSV with HUE dominant: the palette is built from distinct hues,
    // so two greens that differ only in brightness must land on the same theme —
    // plain RGB distance would split them. Low-saturation / near-black colours
    // snap to Universal instead, because hue is just noise down there.
    public static BlockColor Nearest(Color c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);
        if (s < 0.18f || v < 0.12f) return BlockColor.Universal;

        BlockColor best = BlockColor.Universal;
        float bestDist  = float.MaxValue;

        for (int i = 0; i < Themes.Length; i++)
        {
            Color.RGBToHSV(Get(Themes[i]), out float ph, out float ps, out float pv);
            float dh = Mathf.Abs(Mathf.DeltaAngle(h * 360f, ph * 360f)) / 180f;   // 0..1
            float d  = dh + Mathf.Abs(s - ps) * 0.25f + Mathf.Abs(v - pv) * 0.15f;
            if (d < bestDist) { bestDist = d; best = Themes[i]; }
        }
        return best;
    }

    // Arbitrary colour → the exact palette colour it's closest to. Use this to
    // pull hand-tinted / captured colours back onto the canonical palette.
    public static Color Snap(Color c) => Get(Nearest(c));

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
