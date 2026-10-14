using System.Collections.Generic;
using UnityEngine;

// A handful of small flat icons, drawn once at runtime — for HUD pieces that want an
// icon where a word would do and have no art to borrow. White with soft edges, so an
// Image's colour tints them. Each is a few lines or a polygon in a -1..1 square
// (+y up), sampled 3×3 per pixel.
public enum UiGlyph
{
    Check, Cross, Heart, Clock, Crosshair, Bolt, Flame, NoEntry, Leak, Gap, ChevronUp, ChevronDown, Dot,
}

public static class UiGlyphs
{
    const int Size = 64;
    static readonly Dictionary<UiGlyph, Sprite> _cache = new();

    public static Sprite Get(UiGlyph g)
    {
        if (_cache.TryGetValue(g, out var cached) && cached != null) return cached;

        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            wrapMode  = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var px = new Color32[Size * Size];
        const int N = 3;
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int hit = 0;
                for (int sy = 0; sy < N; sy++)
                    for (int sx = 0; sx < N; sx++)
                    {
                        float u = ((x + (sx + 0.5f) / N) / Size) * 2f - 1f;
                        float v = ((y + (sy + 0.5f) / N) / Size) * 2f - 1f;
                        if (Inside(g, u, v)) hit++;
                    }
                px[y * Size + x] = new Color32(255, 255, 255, (byte)(255 * hit / (N * N)));
            }
        tex.SetPixels32(px);
        tex.Apply();

        var s = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        s.hideFlags = HideFlags.HideAndDontSave;
        _cache[g] = s;
        return s;
    }

    // ── Shapes ───────────────────────────────────────────────────────────────

    static readonly float[] BoltPoly  = { 0.18f, 1f,  -0.55f, -0.05f,  -0.06f, -0.05f,  -0.22f, -1f,  0.55f, 0.10f,  0.06f, 0.10f };
    static readonly float[] FlamePoly = { -0.55f, -0.10f,  -0.30f, 0.35f,  0.02f, 1.00f,  0.18f, 0.45f,  0.50f, 0.10f,  0.55f, -0.10f };
    static readonly float[] LeakHead  = { 0.12f, 0.50f,  0.78f, 0f,  0.12f, -0.50f };

    static bool Inside(UiGlyph g, float x, float y)
    {
        float r = Mathf.Sqrt(x * x + y * y);
        switch (g)
        {
            case UiGlyph.Check:
                return Seg(x, y, -0.62f, 0.02f, -0.20f, -0.42f) < 0.17f
                    || Seg(x, y, -0.20f, -0.42f, 0.62f, 0.50f) < 0.17f;

            case UiGlyph.Cross:
                return Seg(x, y, -0.55f, -0.55f, 0.55f, 0.55f) < 0.17f
                    || Seg(x, y, -0.55f, 0.55f, 0.55f, -0.55f) < 0.17f;

            case UiGlyph.Heart:
            {
                float px = x * 1.15f, py = y * 1.15f + 0.12f;
                float a = px * px + py * py - 1f;
                return a * a * a - px * px * py * py * py <= 0f;
            }

            case UiGlyph.Clock:
                return Mathf.Abs(r - 0.80f) < 0.12f
                    || Seg(x, y, 0f, 0f, 0f, 0.50f) < 0.10f
                    || Seg(x, y, 0f, 0f, 0.36f, 0f) < 0.10f;

            case UiGlyph.Crosshair:
                return Mathf.Abs(r - 0.60f) < 0.10f || r < 0.13f
                    || Seg(x, y, -0.95f, 0f, -0.35f, 0f) < 0.09f || Seg(x, y, 0.35f, 0f, 0.95f, 0f) < 0.09f
                    || Seg(x, y, 0f, -0.95f, 0f, -0.35f) < 0.09f || Seg(x, y, 0f, 0.35f, 0f, 0.95f) < 0.09f;

            case UiGlyph.Bolt:
                return Poly(x, y, BoltPoly);

            case UiGlyph.Flame:
                return Poly(x, y, FlamePoly)
                    || Mathf.Sqrt(x * x + (y + 0.30f) * (y + 0.30f)) < 0.62f;

            case UiGlyph.NoEntry:
                return Mathf.Abs(r - 0.80f) < 0.12f
                    || Seg(x, y, -0.57f, 0.57f, 0.57f, -0.57f) < 0.12f;

            case UiGlyph.Leak:   // an arrow that reaches the wall
                return Seg(x, y, -0.85f, 0f, 0.30f, 0f) < 0.12f
                    || Poly(x, y, LeakHead)
                    || (x > 0.84f && x < 0.98f && Mathf.Abs(y) < 0.70f);

            case UiGlyph.Gap:    // a line with a hole in it
                return Seg(x, y, -0.92f, 0f, -0.30f, 0f) < 0.13f
                    || Seg(x, y, 0.30f, 0f, 0.92f, 0f) < 0.13f
                    || Seg(x, y, -0.30f, -0.42f, -0.30f, 0.42f) < 0.09f
                    || Seg(x, y, 0.30f, -0.42f, 0.30f, 0.42f) < 0.09f;

            case UiGlyph.ChevronDown:
                return Seg(x, y, -0.55f, 0.22f, 0f, -0.33f) < 0.15f
                    || Seg(x, y, 0f, -0.33f, 0.55f, 0.22f) < 0.15f;

            case UiGlyph.ChevronUp:
                return Seg(x, y, -0.55f, -0.22f, 0f, 0.33f) < 0.15f
                    || Seg(x, y, 0f, 0.33f, 0.55f, -0.22f) < 0.15f;

            default:             // Dot
                return r < 0.45f;
        }
    }

    static float Seg(float px, float py, float ax, float ay, float bx, float by)
    {
        float dx = bx - ax, dy = by - ay;
        float t = Mathf.Clamp01(((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy));
        float qx = ax + dx * t - px, qy = ay + dy * t - py;
        return Mathf.Sqrt(qx * qx + qy * qy);
    }

    // Even-odd point-in-polygon; `p` is x,y pairs.
    static bool Poly(float x, float y, float[] p)
    {
        bool inside = false;
        int n = p.Length / 2;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            float xi = p[2 * i], yi = p[2 * i + 1], xj = p[2 * j], yj = p[2 * j + 1];
            if (((yi > y) != (yj > y)) && (x < (xj - xi) * (y - yi) / (yj - yi) + xi))
                inside = !inside;
        }
        return inside;
    }
}
