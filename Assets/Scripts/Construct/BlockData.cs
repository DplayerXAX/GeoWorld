using UnityEngine;

public enum BlockRarity
{
    Common,     // 普通：线条、单格
    Uncommon,   // 少见：L形、长线
    Rare,       // 稀有：T/S/2×2/3D角
}

public enum BlockType
{
    Home,
    Lift,
    Pull,
    Shadow,
    Turret,
    Empty,
    SlowTurret,
    AoeTurret
}

// Predefined Tetris-style shapes.
// All shapes lie in the XZ plane (Y = 0) as their canonical orientation.
// Cells are relative to origin (0,0,0).
//
// Rotation is handled entirely by PlacementController (keys 1 / 2 / 3 rotate
// around X / Y / Z), so mirror-images and vertical variants are NOT needed here:
//   J4  = L4  rotated 180° around Y          → removed
//   Z4  = S4  rotated  90° around Y          → removed
//   I2Y = I2  rotated  90° around Z          → removed
//   I3Y = I3  rotated  90° around Z          → removed
//   LY  = L3  rotated  90° around Z          → removed
//   TY  = T4  rotated  90° around Z          → removed
public enum BlockShape
{
    Custom,     // cells defined manually — ignore auto-fill

    // ── Single ────────────────────────────────────────────────────────────────
    Single,     // □

    // ── Lines ─────────────────────────────────────────────────────────────────
    I2,         // □□          (rotate 90° Y → vertical line of 2)
    I3,         // □□□         (rotate 90° Y → vertical line of 3)
    I4,         // □□□□

    // ── L-shape ───────────────────────────────────────────────────────────────
    L3,         // □□          (all 4 rotations reachable via keys)
                //   □
    L4,         // □□□
                //     □

    // ── T-shape ───────────────────────────────────────────────────────────────
    T4,         // □□□         (rotate 90° Z → upright T)
                //  □

    // ── S-shape ───────────────────────────────────────────────────────────────
    S4,         //  □□         (rotate 90° Y → Z4 mirror)
                // □□

    // ── Square ────────────────────────────────────────────────────────────────
    O2x2,       // □□
                // □□

    // ── 3-D corner (XZ + Y) ───────────────────────────────────────────────────
    Corner3D,   // flat L that also rises one cell — routes around edges in 3D
}

[CreateAssetMenu(menuName = "Game/Block")]
public class BlockData : ScriptableObject
{
    [Header("Type & Shape")]
    public BlockType   blockType;
    public BlockRarity rarity = BlockRarity.Common;

    // NOTE: synergy color is NOT stored on BlockData — it's assigned at
    // runtime per spawned token (random per Build phase). See
    // SynergyEvaluator.OnPiecePlaced(data, color, cells).

    [Tooltip("Pick a preset shape then click  Apply Shape → cells  in the ⋮ menu.\n" +
             "Set to Custom to define cells manually.")]
    public BlockShape blockShape = BlockShape.Custom;

    [Header("Shape  (world-relative cell offsets)")]
    public Vector3Int[] cells;

    [Header("Audio")]
    public AK.Wwise.Event onStepEvent;
    public AK.Wwise.Event previewEvent;

    // ── Display name used by ShopController tooltip ───────────────────────────
    public string DisplayName => $"{blockType}  {ShapeLabel(blockShape)}";

    // Shape-only label for the shop tooltip's title row. BlockType (music role)
    // is intentionally omitted — players browsing the shop care about shape +
    // theme color, not the audio assignment.
    public string ShapeName => ShapeLabel(blockShape);

    static string ShapeLabel(BlockShape s) => s switch
    {
        BlockShape.Custom   => "",
        BlockShape.Single   => "·",
        BlockShape.I2       => "I2",
        BlockShape.I3       => "I3",
        BlockShape.I4       => "I4",
        BlockShape.L3       => "L3",
        BlockShape.L4       => "L4",
        BlockShape.T4       => "T4",
        BlockShape.S4       => "S4",
        BlockShape.O2x2     => "2×2",
        BlockShape.Corner3D => "⌐3D",
        _                   => ""
    };

    // ── Shape library ─────────────────────────────────────────────────────────
    // All shapes defined with X = right, Y = up, Z = forward.
    // Flat pieces lie in XZ; vertical pieces grow in Y.
    static Vector3Int[] ShapeCells(BlockShape s)
    {
        static Vector3Int V(int x, int y, int z) => new Vector3Int(x, y, z);

        return s switch
        {
            BlockShape.Single   => new[] { V(0,0,0) },

            // Lines along X axis — rotate 90° around Y or Z for other orientations
            BlockShape.I2       => new[] { V(0,0,0), V(1,0,0) },
            BlockShape.I3       => new[] { V(0,0,0), V(1,0,0), V(2,0,0) },
            BlockShape.I4       => new[] { V(0,0,0), V(1,0,0), V(2,0,0), V(3,0,0) },

            // L-shapes in XZ plane — all rotations (incl. J mirror) via keys 1/2/3
            BlockShape.L3       => new[] { V(0,0,0), V(1,0,0), V(1,0,1) },
            BlockShape.L4       => new[] { V(0,0,0), V(1,0,0), V(2,0,0), V(2,0,1) },

            // T-shape — rotate 90° around Z for upright T, etc.
            BlockShape.T4       => new[] { V(0,0,0), V(1,0,0), V(2,0,0), V(1,0,1) },

            // S-shape — rotate 90° around Y for the Z mirror
            BlockShape.S4       => new[] { V(0,0,0), V(1,0,0), V(1,0,1), V(2,0,1) },

            // 2×2 square — rotation-invariant
            BlockShape.O2x2     => new[] { V(0,0,0), V(1,0,0), V(0,0,1), V(1,0,1) },

            // 3D corner — flat L that also rises one cell; all variants via rotation
            BlockShape.Corner3D => new[] { V(0,0,0), V(1,0,0), V(0,0,1), V(0,1,0) },

            _ => null
        };
    }

    // ── Editor helpers ────────────────────────────────────────────────────────

#if UNITY_EDITOR
    // Fires every time any field changes in the Inspector.
    // If blockShape is not Custom, auto-fill cells so the change is immediate.
    // Setting blockShape back to Custom preserves whatever is in cells.
    void OnValidate()
    {
        if (blockShape == BlockShape.Custom) return;
        var template = ShapeCells(blockShape);
        if (template != null) cells = template;
    }
#endif

    // Also available as a manual right-click → Apply Shape → cells,
    // useful for re-applying after accidental manual edits to cells.
    [ContextMenu("Apply Shape → cells")]
    void ApplyShape()
    {
        if (blockShape == BlockShape.Custom)
        {
            Debug.LogWarning($"[BlockData] {name}: blockShape is Custom — cells unchanged.");
            return;
        }
        var template = ShapeCells(blockShape);
        if (template == null) return;
        cells = template;
        Debug.Log($"[BlockData] {name}: applied {blockShape} ({cells.Length} cells).");
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
}
