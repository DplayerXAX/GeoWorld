using UnityEngine;

// Shared constructivist / silkscreen palette — paper, ink, signal red, gold, blue.
// Used by the title screen + stage; reuse it anywhere a menu wants the house look.
public static class GeoPalette
{
    public static readonly Color Paper  = new Color(0.949f, 0.937f, 0.902f); // F2EFE6
    public static readonly Color Ink    = new Color(0.086f, 0.086f, 0.086f); // 161616
    public static readonly Color Signal = new Color(0.886f, 0.141f, 0.106f); // E2241B
    public static readonly Color Gold   = new Color(0.910f, 0.698f, 0.227f); // E8B23A
    public static readonly Color Blue   = new Color(0.169f, 0.424f, 0.690f); // 2B6CB0

    // Cycling accents for procedural props / shard tints.
    public static readonly Color[] Accents = { Signal, Gold, Blue, Ink };

    public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
}
