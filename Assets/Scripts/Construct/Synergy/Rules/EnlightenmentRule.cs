using System.Collections.Generic;
using UnityEngine;

// 启发 (Enlightenment) — pieces of `color` (plus jokers) form a perfect
// axis-aligned cube of cells.
//
// Tiers:
//   • Tier 1: 2×2×2 cube  ( 8 cells)
//   • Tier 2: 3×3×3 cube  (27 cells)
//   • Tier 3: 4×4×4 cube  (64 cells)
//
// "Perfect" = every cell in the N×N×N region is occupied, AND every owning
// piece is fully contained inside the region. Pieces that span the boundary
// disqualify the cube — keeps the visual contract clean.
//
// On absorption, larger cubes upgrade the tier in place. Use tiered effects
// (override ApplyAt/RevokeAt) if you want different rewards per tier;
// otherwise the single `effect` fires/refires on tier change.
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Rules/Enlightenment Rule",
                 fileName = "EnlightenmentRule")]
public class EnlightenmentRule : SynergyRule
{
    [Header("Enlightenment — N×N×N cube")]
    [Tooltip("Smallest cube side accepted as a hit (tier 1). 2 = 8 cells.")]
    [Min(2)] public int minSide = 2;

    [Tooltip("Largest cube side considered. Tiers beyond this are not detected.")]
    [Min(2)] public int maxSide = 4;

    [Header("Tiered effects (optional)")]
    [Tooltip("Per-tier override effects. If set and length > tier-1, used instead of base `effect`. Useful for 2³ small reward, 3³ medium, 4³ big.")]
    public GameEffect[] perTierEffects;

    void Reset()
    {
        absorbAdditionalPieces = true;
        priority               = 60;   // > Order, snags cube pieces first
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

            if (TryFindCube(board, usable, side, out var match))
            {
                claimed = match;
                tier    = side - minSide + 1;
                return true;
            }
        }
        return false;
    }

    // Brute-force cube search. For each cell of each pool piece, try every
    // possible cube origin where that cell could sit. For typical board
    // sizes (~dozens of pieces, side ≤ 4) this is well under 1ms.
    static bool TryFindCube(BoardSnapshot board, HashSet<PlacedPiece> usable,
                            int side, out HashSet<PlacedPiece> matched)
    {
        matched = null;
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
                        return true;
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

        // Verify all matched pieces sit FULLY inside the cube — no cell hangs out.
        foreach (var p in matched)
        {
            for (int i = 0; i < p.cells.Length; i++)
            {
                var c = p.cells[i];
                if (c.x < origin.x || c.x >= origin.x + side ||
                    c.y < origin.y || c.y >= origin.y + side ||
                    c.z < origin.z || c.z >= origin.z + side)
                {
                    return false;
                }
            }
        }
        return true;
    }
}
