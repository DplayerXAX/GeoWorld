using System.Collections.Generic;
using UnityEngine;

// Enlightenment — pieces of `color` (plus jokers) cover an axis-aligned
// cube of cells. Tier 1: 2^3, tier 2: 3^3, tier 3: 4^3.
//
// Pieces are claimed whole if any cell lies inside the cube; overhang cells
// stay outside via ICellHighlightFilter until absorbed into a bigger cube.
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

    // Last successful cube, set on every successful TryEvaluate.
    [System.NonSerialized] Vector3Int _cubeOrigin;
    [System.NonSerialized] int        _cubeSide;

    public Vector3Int CurrentCubeOrigin => _cubeOrigin;
    public int        CurrentCubeSide   => _cubeSide;

    void Reset()
    {
        absorbAdditionalPieces = true;
        allowMultipleInstances = false; // one cube levels up (2³→3³→4³), never two at once
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

    // Progress = total usable same-color CELLS toward the smallest cube (minSide³).
    public override bool TryGetActivationProgress(BoardSnapshot board, PlacedPiece piece,
                                                  out int current, out int required)
    {
        required = Mathf.Max(1, minSide * minSide * minSide);
        current  = 0;
        if (board == null) return true;
        foreach (var p in board.PiecesUsableAs(color))
            if (p?.cells != null) current += p.cells.Length;
        return true;
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

        // Largest cube first — prefer higher tiers.
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
        // No cube found — clear cached highlight so stale data doesn't leak.
        _cubeSide = 0;
        return false;
    }

    // Brute-force: for each cell of each pool piece, try every cube origin
    // where that cell could sit.
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

        // Pieces may extend beyond the cube; only in-cube cells get
        // visualizer decoration via ICellHighlightFilter.
        return true;
    }
}
