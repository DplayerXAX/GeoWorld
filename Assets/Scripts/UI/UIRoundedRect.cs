using UnityEngine;
using System.Collections.Generic;

// Runtime-generated rounded-rectangle sprites for UGUI panels (9-sliced, so corners
// stay crisp at any size). Cached per radius. Assign to an Image and set
// Image.type = Sliced; the Image.color still tints it.
public static class UIRoundedRect
{
    static readonly Dictionary<int, Sprite>    _cache   = new();
    static readonly Dictionary<int, Texture2D> _circles = new();

    // A filled, anti-aliased circle texture (for IMGUI GUI.DrawTexture round buttons).
    public static Texture2D CircleTex(int size = 128)
    {
        size = Mathf.Max(8, size);
        if (_circles.TryGetValue(size, out var t) && t != null) return t;

        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false)
        {
            wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var px = new Color32[size * size];
        float c = (size - 1) * 0.5f, rad = size * 0.5f - 1f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy) - rad;
                float a = Mathf.Clamp01(0.5f - d);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        _circles[size] = tex;
        return tex;
    }

    public static Sprite Get(int radius = 24)
    {
        radius = Mathf.Max(2, radius);
        if (_cache.TryGetValue(radius, out var s) && s != null) return s;

        int size = radius * 2 + 4;
        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false)
        {
            wrapMode  = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };

        var px = new Color32[size * size];
        float half  = size * 0.5f;
        float inset = half - radius;             // flat (stretchable) centre half-extent
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float qx = Mathf.Abs(x + 0.5f - half) - inset;
                float qy = Mathf.Abs(y + 0.5f - half) - inset;
                float dx = Mathf.Max(qx, 0f), dy = Mathf.Max(qy, 0f);
                float d  = Mathf.Sqrt(dx * dx + dy * dy) - radius;   // rounded-rect SDF (<0 inside)
                float a  = Mathf.Clamp01(0.5f - d);                  // 1px anti-aliased edge
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();

        float b = radius + 1;
        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                                   100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        sprite.hideFlags = HideFlags.HideAndDontSave;
        _cache[radius] = sprite;
        return sprite;
    }
}
