using System.Collections.Generic;
using UnityEngine;

// Helpers for GameEffects that need to know WHICH pieces / cells their synergy
// currently claims. A GameEffect isn't handed its ActiveSynergy, but — exactly
// like the visualizers do with `a.rule.visualizer == this` — it can find itself
// by matching `a.rule.effect == this` against the live actives. (Order, Harmony
// and Abundance all use the single-effect dispatch, and the evaluator keeps at
// most one active per rule, so this resolves to that synergy's footprint.)
//
// Rules that implement ICellHighlightFilter (Enlightenment's cube, Abundance's
// loop) claim a WIDER region than what actually "participates" — e.g. Abundance
// locks a loop's tree-shaped tails as part of the structure but only the loop
// itself should count. Every counting helper here respects that filter: a cell
// only counts if the filter (when present) says ShouldHighlight, and a PIECE
// only counts if ANY of its cells passes.
public static class SynergyEffectUtil
{
    // Fills `into` with the union of every PARTICIPATING claimed cell across all
    // active synergies whose rule.effect == effect.
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
            var hc = a.highlightCells;
            foreach (var p in a.claimedPieces)
            {
                if (p?.cells == null) continue;
                for (int k = 0; k < p.cells.Length; k++)
                {
                    var c = p.cells[k];
                    if (hc != null && !hc.Contains(c)) continue;
                    into.Add(c);
                }
            }
        }
    }

    // Total PARTICIPATING pieces (modules) across this effect's active synergies
    // — a piece counts if at least one of its cells is inside the filtered
    // region (or always, when the rule has no filter).
    public static int CountClaimedPieces(GameEffect effect)
    {
        int n = 0;
        var ev = SynergyEvaluator.Instance;
        if (ev == null || effect == null) return 0;

        var actives = ev.Actives;
        for (int i = 0; i < actives.Count; i++)
        {
            var a = actives[i];
            if (a?.rule == null || a.rule.effect != effect) continue;
            n += CountParticipatingPieces(a);
        }
        return n;
    }

    // Same as CountClaimedPieces, but scoped to ONE ActiveSynergy instead of
    // summing every active matching an effect — use this for per-instance HUD
    // rows (multiple simultaneous loops of the same rule each need their OWN
    // count, not the rule-wide total).
    public static int CountParticipatingPieces(ActiveSynergy a)
    {
        if (a?.claimedPieces == null) return 0;
        var hc = a.highlightCells;
        int n = 0;
        foreach (var p in a.claimedPieces)
        {
            if (p == null) continue;
            if (hc == null) { n++; continue; }
            if (PieceInFilter(p, hc)) n++;
        }
        return n;
    }

    // Per-instance counterpart to CountClaimedCells — see CountParticipatingPieces.
    public static int CountParticipatingCells(ActiveSynergy a)
    {
        if (a?.claimedPieces == null) return 0;
        var hc = a.highlightCells;
        int n = 0;
        foreach (var p in a.claimedPieces)
        {
            if (p?.cells == null) continue;
            if (hc == null) { n += p.cells.Length; continue; }
            for (int k = 0; k < p.cells.Length; k++)
                if (hc.Contains(p.cells[k])) n++;
        }
        return n;
    }

    // A world position on a random PARTICIPATING cell of this effect's active
    // synergy (for spawning income/harvest motes) — so the mote/fx spawns on
    // the loop itself, not a non-counting tail. Returns false when nothing
    // participating is claimed.
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
            var hc = a.highlightCells;
            foreach (var p in a.claimedPieces)
            {
                if (p?.cells == null || p.cells.Length == 0) continue;
                if (hc == null)
                {
                    var c = p.cells[Random.Range(0, p.cells.Length)];
                    world = grid.GridToWorld(c) + Vector3.up * (grid.cellSize * 0.6f);
                    return true;
                }
                // Filtered: pick among just this piece's participating cells.
                var candidates = new List<Vector3Int>(p.cells.Length);
                for (int k = 0; k < p.cells.Length; k++)
                    if (hc.Contains(p.cells[k])) candidates.Add(p.cells[k]);
                if (candidates.Count == 0) continue;
                var cc = candidates[Random.Range(0, candidates.Count)];
                world = grid.GridToWorld(cc) + Vector3.up * (grid.cellSize * 0.6f);
                return true;
            }
        }
        return false;
    }

    // Total PARTICIPATING cells (cubes) across this effect's active synergies.
    public static int CountClaimedCells(GameEffect effect)
    {
        int n = 0;
        var ev = SynergyEvaluator.Instance;
        if (ev == null || effect == null) return 0;

        var actives = ev.Actives;
        for (int i = 0; i < actives.Count; i++)
        {
            var a = actives[i];
            if (a?.rule == null || a.rule.effect != effect) continue;
            n += CountParticipatingCells(a);
        }
        return n;
    }

    static bool PieceInFilter(PlacedPiece p, HashSet<Vector3Int> highlightCells)
    {
        if (p.cells == null) return false;
        for (int k = 0; k < p.cells.Length; k++)
            if (highlightCells.Contains(p.cells[k])) return true;
        return false;
    }
}
