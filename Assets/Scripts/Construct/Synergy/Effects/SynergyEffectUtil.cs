using System.Collections.Generic;
using UnityEngine;

// Helpers for GameEffects that need to know WHICH pieces / cells their synergy
// currently claims. A GameEffect isn't handed its ActiveSynergy, but — exactly
// like the visualizers do with `a.rule.visualizer == this` — it can find itself
// by matching `a.rule.effect == this` against the live actives. (Order, Harmony
// and Abundance all use the single-effect dispatch, and the evaluator keeps at
// most one active per rule, so this resolves to that synergy's footprint.)
public static class SynergyEffectUtil
{
    // Fills `into` with the union of every claimed cell across all active
    // synergies whose rule.effect == effect.
    public static void CollectClaimedCells(GameEffect effect, HashSet<Vector3Int> into)
    {
        into.Clear();
        var ev = SynergyEvaluator.Instance;
        if (ev == null || effect == null) return;

        var actives = ev.Actives;
        for (int i = 0; i < actives.Count; i++)
        {
            var a = actives[i];
            if (a?.rule == null || a.rule.effect != effect || a.claimedPieces == null) continue;
            foreach (var p in a.claimedPieces)
            {
                if (p?.cells == null) continue;
                for (int k = 0; k < p.cells.Length; k++) into.Add(p.cells[k]);
            }
        }
    }

    // Total claimed PIECES (modules) across this effect's active synergies.
    public static int CountClaimedPieces(GameEffect effect)
    {
        int n = 0;
        var ev = SynergyEvaluator.Instance;
        if (ev == null || effect == null) return 0;

        var actives = ev.Actives;
        for (int i = 0; i < actives.Count; i++)
        {
            var a = actives[i];
            if (a?.rule == null || a.rule.effect != effect || a.claimedPieces == null) continue;
            n += a.claimedPieces.Count;
        }
        return n;
    }

    // A world position on a random claimed cell of this effect's active synergy
    // (for spawning income/harvest motes). Returns false when nothing is claimed.
    public static bool TryGetClaimedCellWorld(GameEffect effect, out Vector3 world)
    {
        world = Vector3.zero;
        var ev   = SynergyEvaluator.Instance;
        var grid = GridSystem.instance;
        if (ev == null || grid == null || effect == null) return false;

        var actives = ev.Actives;
        for (int i = 0; i < actives.Count; i++)
        {
            var a = actives[i];
            if (a?.rule == null || a.rule.effect != effect || a.claimedPieces == null) continue;
            foreach (var p in a.claimedPieces)
            {
                if (p?.cells == null || p.cells.Length == 0) continue;
                var c = p.cells[Random.Range(0, p.cells.Length)];
                world = grid.GridToWorld(c) + Vector3.up * (grid.cellSize * 0.6f);
                return true;
            }
        }
        return false;
    }

    // Total claimed CELLS (cubes) across this effect's active synergies.
    public static int CountClaimedCells(GameEffect effect)
    {
        int n = 0;
        var ev = SynergyEvaluator.Instance;
        if (ev == null || effect == null) return 0;

        var actives = ev.Actives;
        for (int i = 0; i < actives.Count; i++)
        {
            var a = actives[i];
            if (a?.rule == null || a.rule.effect != effect || a.claimedPieces == null) continue;
            foreach (var p in a.claimedPieces)
                if (p?.cells != null) n += p.cells.Length;
        }
        return n;
    }
}
