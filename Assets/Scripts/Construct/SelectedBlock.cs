using UnityEngine;

public class SelectableBlock : MonoBehaviour
{
    public BlockData data;

    /// <summary>
    /// Synergy theme color assigned at shop spawn time. Carried through
    /// placement onto PlacedBlockInstance.color and ultimately into
    /// SynergyEvaluator.OnPiecePlaced. Set to None for turrets / debug
    /// blocks that shouldn't participate in synergies.
    /// </summary>
    public BlockColor color = BlockColor.None;

    /// <summary>
    /// Final shop price pre-computed by ShopController at spawn time.
    /// Includes cell count, rarity, block type, round scaling, and a one-time
    /// random fluctuation.  Read this when buying — do not recompute.
    /// </summary>
    public int cachedPrice;
}