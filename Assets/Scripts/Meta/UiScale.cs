using UnityEngine;

// Shared IMGUI scale factor. Scales the UI by the window HEIGHT (relative to a
// 1080p reference) — the convention for game HUDs: UI size tracks the vertical
// resolution, so a bigger window means proportionally bigger UI, and ultrawide
// windows just get more horizontal empty space (UI doesn't shrink). Clamped so it
// never gets absurdly large or tiny.
//
// (Set fitWidthToo = true to also clamp against width via min() — that prevents a
// very NARROW window from overflowing, at the cost of width-only resizes not
// growing the UI.)
public static class UiScale
{
    public const float RefW = 1920f;
    public const float RefH = 1080f;
    public static float MinScale = 0.4f;
    public static float MaxScale = 2.5f;
    public static bool  fitWidthToo = false;

    public static float Get()
    {
        float s = Screen.height / RefH;
        if (fitWidthToo) s = Mathf.Min(s, Screen.width / RefW);
        return Mathf.Clamp(s, MinScale, MaxScale);
    }
}
