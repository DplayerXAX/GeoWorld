using UnityEngine;

// Runtime representation of one BlockData asset placed on the board.
//
// A "piece" is the Tetris-style unit the synergy system reasons about — Q1
// in the design discussion locked this as "one BlockData placement = one
// piece", not "one cell = one piece". A multi-cell shape like O2x2 is a
// single piece that happens to occupy 4 cells.
//
// Color is a RUNTIME property — not on BlockData. The shop / spawn pipeline
// assigns a random BlockColor to each token when the player receives it,
// and PlacementController forwards that color into the piece at placement.
//
// PlacedPiece is intentionally a plain data carrier:
//   • Identity comes from `id` (monotonic, assigned by SynergyEvaluator).
//   • Cells are the FINAL world cell positions after rotation/translation,
//     so BoardSnapshot can look them up without re-applying transforms.
public sealed class PlacedPiece
{
    public readonly int          id;
    public readonly BlockData    data;
    public readonly BlockColor   color;
    public readonly Vector3Int[] cells;

    public PlacedPiece(int id, BlockData data, BlockColor color, Vector3Int[] cells)
    {
        this.id    = id;
        this.data  = data;
        this.color = color;
        this.cells = cells ?? System.Array.Empty<Vector3Int>();
    }

    public override string ToString() => $"#{id} {color} ({cells.Length}c)";

    // Identity-based equality — two piece instances are never equal.
    public override bool Equals(object obj) => ReferenceEquals(this, obj);
    public override int  GetHashCode()      => id;
}
