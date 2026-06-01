using UnityEngine;

// Optional interface for SynergyRule subclasses that want PER-CELL visual
// filtering — i.e. the rule claims a whole piece (so other rules can't
// touch it), but only SOME of that piece's cells should get the visualizer
// decoration.
//
// Example: Enlightenment claims any piece with cells in the 2×2×2 cube,
// but visually only the in-cube cells should display the radiating-frame
// face overlay. Cells of the same piece that hang outside the cube get
// claimed (locked) but stay visually plain — they only "light up" later
// when absorbed into a bigger 3×3×3 cube.
//
// Rules that don't care (Order / Harmony / Abundance / Exploration) skip
// this interface; FaceMaterialVisualizerBase defaults to decorating every
// cell of every claimed piece.
public interface ICellHighlightFilter
{
    /// <summary>
    /// True = this cell should get the rule's visualizer decoration.
    /// False = this cell is claimed (locked) but visually inert.
    /// </summary>
    bool ShouldHighlight(Vector3Int worldCell);
}
