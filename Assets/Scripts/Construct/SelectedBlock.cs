using UnityEngine;

public class SelectableBlock : MonoBehaviour
{
    public BlockData data;

    /// <summary>
    /// Final shop price pre-computed by ShopController at spawn time.
    /// Includes cell count, rarity, block type, round scaling, and a one-time
    /// random fluctuation.  Read this when buying — do not recompute.
    /// </summary>
    public int cachedPrice;
}