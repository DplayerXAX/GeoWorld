using System.Collections.Generic;
using UnityEngine;

// Hover feedback for the left HUD synergy panel: while the pointer sits over
// a synergy row, pulse-tint that ActiveSynergy's claimed pieces in the 3D
// scene so the player can see exactly which blocks are contributing. Purely
// additive on top of each rule's own visualizer decoration (flowers, gears,
// vines, ...) — this doesn't touch or replace that, it just brightens the
// underlying block renderers.
//
// One active synergy hovered at a time (HudSidePanels calls SetHovered each
// frame with whatever row is under the cursor, or null).
public static class SynergyHoverHighlight
{
    static ActiveSynergy _current;
    static readonly List<(Renderer r, Color orig)> _tinted = new();
    static float _t;

    public static void SetHovered(ActiveSynergy active)
    {
        if (_current == active) return;
        Clear();
        _current = active;
        if (_current?.claimedPieces == null) return;

        var grid = GridSystem.instance;
        if (grid == null) return;

        var seen = new HashSet<GameObject>();
        foreach (var p in _current.claimedPieces)
        {
            if (p?.cells == null || p.cells.Length == 0) continue;
            var ins = grid.GetInstanceAt(p.cells[0]);
            if (ins?.visualObject == null || !seen.Add(ins.visualObject)) continue;

            var rends = ins.visualObject.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (r == null) continue;
                _tinted.Add((r, MpbColor.Get(r)));
            }
        }
        _t = 0f;
    }

    public static void Clear()
    {
        for (int i = 0; i < _tinted.Count; i++)
        {
            var (r, orig) = _tinted[i];
            if (r != null) MpbColor.Set(r, orig);
        }
        _tinted.Clear();
        _current = null;
    }

    // Call once per frame (no-ops when nothing is hovered).
    public static void Tick()
    {
        if (_current == null || _tinted.Count == 0) return;
        _t += Time.unscaledDeltaTime;
        float pulse = 0.5f + 0.5f * Mathf.Sin(_t * 6f);
        float k = 0.35f + 0.35f * pulse;
        for (int i = 0; i < _tinted.Count; i++)
        {
            var (r, orig) = _tinted[i];
            if (r == null) continue;
            MpbColor.Set(r, Color.Lerp(orig, Color.white, k));
        }
    }
}
