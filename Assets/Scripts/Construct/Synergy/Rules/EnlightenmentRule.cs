using System.Collections.Generic;
using UnityEngine;

// 启发 (Enlightenment) — pieces of `color` (plus jokers) cover an axis-
// aligned cube of cells.
//
// Tiers:
//   • Tier 1: 2×2×2 cube  ( 8 cells)
//   • Tier 2: 3×3×3 cube  (27 cells)
//   • Tier 3: 4×4×4 cube  (64 cells)
//
// Pieces are claimed whole if any of their cells lies inside the cube —
// they may extend beyond freely. ICellHighlightFilter exposes which cells
// are actually in-cube so visualizers only decorate those cells; overhang
// cells stay visually plain until absorbed into a bigger cube.
//
// On absorption, larger cubes upgrade the tier in place. Use tiered effects
// (override ApplyAt/RevokeAt) if you want different rewards per tier;
// otherwise the single `effect` fires/refires on tier change.
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Rules/Enlightenment Rule",
                 fileName = "EnlightenmentRule")]
public class EnlightenmentRule : SynergyRule, ICellHighlightFilter
{
    [Header("Enlightenment — N×N×N cube")]
    [Tooltip("Smallest cube side accepted as a hit (tier 1). 2 = 8 cells.")]
    [Min(2)] public int minSide = 2;

    [Tooltip("Largest cube side considered. Tiers beyond this are not detected.")]
    [Min(2)] public int maxSide = 4;

    [Header("Tiered effects (optional)")]
    [Tooltip("Per-tier override effects. If set and length > tier-1, used instead of base `effect`. Useful for 2³ small reward, 3³ medium, 4³ big.")]
    public GameEffect[] perTierEffects;

    // ── ICellHighlightFilter state (last successful cube) ──────────────
    // Set on every successful TryEvaluate. Used by visualizers via the
    // ICellHighlightFilter cast to decorate only cells inside the cube.
    [System.NonSerialized] Vector3Int _cubeOrigin;
    [System.NonSerialized] int        _cubeSide;

    void Reset()
    {
        absorbAdditionalPieces = true;
        priority               = 60;   // > Order, snags cube pieces first
    }

    public bool ShouldHighlight(Vector3Int worldCell)
    {
        if (_cubeSide <= 0) return false;
        return worldCell.x >= _cubeOrigin.x && worldCell.x < _cubeOrigin.x + _cubeSide
            && worldCell.y >= _cubeOrigin.y && worldCell.y < _cubeOrigin.y + _cubeSide
            && worldCell.z >= _cubeOrigin.z && worldCell.z < _cubeOrigin.z + _cubeSide;
    }

    public override void ApplyAt(GameFlowManager game, int tier)
    {
        var e = PickTierEffect(tier);
        e?.Apply(game);
    }

    public override void RevokeAt(GameFlowManager game, int tier)
    {
        var e = PickTierEffect(tier);
        e?.Revoke(game);
    }

    GameEffect PickTierEffect(int tier)
    {
        if (perTierEffects != null && tier >= 1 && tier <= perTierEffects.Length && perTierEffects[tier - 1] != null)
            return perTierEffects[tier - 1];
        return effect;
    }

    public override bool TryEvaluate(BoardSnapshot board, HashSet<PlacedPiece> pool,
                                     out HashSet<PlacedPiece> claimed, out int tier)
    {
        claimed = null;
        tier    = 0;

        if (color == BlockColor.None || color == BlockColor.Universal) return false;

        var usable = new HashSet<PlacedPiece>();
        int totalCells = 0;
        foreach (var p in board.PiecesUsableAs(color))
            if (pool.Contains(p)) { usable.Add(p); totalCells += p.cells.Length; }

        // Try largest cube first so we prefer higher tiers.
        for (int side = maxSide; side >= minSide; side--)
        {
            int needed = side * side * side;
            if (totalCells < needed) continue;

            if (TryFindCube(board, usable, side, out var match, out var origin))
            {
                claimed     = match;
                tier        = side - minSide + 1;
                _cubeOrigin = origin;
                _cubeSide   = side;
                return true;
            }
        }
        // No cube found — clear cached highlight zone so stale data doesn't
        // leak into the next evaluation.
        _cubeSide = 0;
        return false;
    }

    // Brute-force cube search. For each cell of each pool piece, try every
    // possible cube origin where that cell could sit. For typical board
    // sizes (~dozens of pieces, side ≤ 4) this is well under 1ms.
    static bool TryFindCube(BoardSnapshot board, HashSet<PlacedPiece> usable,
                            int side, out HashSet<PlacedPiece> matched, out Vector3Int foundOrigin)
    {
        matched      = null;
        foundOrigin  = Vector3Int.zero;
        if (side <= 0) return false;

        foreach (var p in usable)
        {
            for (int i = 0; i < p.cells.Length; i++)
            {
                var cell = p.cells[i];
                for (int dx = 0; dx < side; dx++)
                for (int dy = 0; dy < side; dy++)
                for (int dz = 0; dz < side; dz++)
                {
                    var origin = cell - new Vector3Int(dx, dy, dz);
                    if (TryCubeAt(board, usable, origin, side, out matched))
                    {
                        foundOrigin = origin;
                        return true;
                    }
                }
            }
        }
        return false;
    }

    static bool TryCubeAt(BoardSnapshot board, HashSet<PlacedPiece> usable,
                          Vector3Int origin, int side, out HashSet<PlacedPiece> matched)
    {
        matched = new HashSet<PlacedPiece>();

        for (int x = 0; x < side; x++)
        for (int y = 0; y < side; y++)
        for (int z = 0; z < side; z++)
        {
            var cell = origin + new Vector3Int(x, y, z);
            var owner = board.GetOwner(cell);
            if (owner == null || !usable.Contains(owner)) return false;
            matched.Add(owner);
        }

        // Pieces may extend BEYOND the cube — we just need every cell inside
        // the cube to be filled by pool pieces. The whole piece is claimed
        // (lock-wise) but only the in-cube cells get visualizer decoration
        // via ICellHighlightFilter.
        return true;
    }
}
