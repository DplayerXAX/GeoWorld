using System.Collections.Generic;
using UnityEngine;

public enum Rarity { Common, Uncommon, Rare }

// One upgrade choice the player can pick at wave end.
//
// Cards are the SECONDARY rogue layer — the primary one is block-tag
// synergies (see SynergyEvaluator). Cards either grant one-shot bonuses or
// modify how the synergy system behaves (e.g. "Development synergies
// trigger at -1 threshold").
[CreateAssetMenu(menuName = "GeoWorld/Upgrade Card", fileName = "UpgradeCard")]
public class UpgradeCard : ScriptableObject
{
    [Header("Display")]
    public string displayName = "Untitled";
    [TextArea(2, 4)] public string description;
    public Sprite icon;

    [Header("Classification")]
    public Rarity rarity = Rarity.Common;
    [Tooltip("Themes this card relates to (e.g. card that boosts Development synergies). Drives future card-synergy interactions — purely metadata for now.")]
    public List<BlockTag> tags = new();

    [Header("Behavior")]
    [Tooltip("The effect to apply when picked. Author as a separate ScriptableObject subclass of GameEffect and drag it here.")]
    public GameEffect effect;

    [Header("Pool")]
    [Tooltip("Relative selection weight when offered. 0 excludes from the pool.")]
    [Min(0f)] public float weight = 1f;

    [Tooltip("Maximum copies of this card that can be owned simultaneously. 0 = unlimited.")]
    [Min(0)] public int maxCopies = 1;
}
