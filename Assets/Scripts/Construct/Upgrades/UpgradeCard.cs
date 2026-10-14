using System.Collections.Generic;
using UnityEngine;

public enum Rarity { Common, Uncommon, Rare }

// What kind of thing a relic is. Purely descriptive (the relic bar and the market
// group and colour by it); no rule reads it.
public enum ItemCategory { Economic, Buff, Chaotic, Special, Trash, Healing }

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
    public ItemCategory category = ItemCategory.Buff;
    [Tooltip("Roguelite: block currency to buy it at the relic market (0 = by rarity).")]
    [Min(0)] public int basePrice = 0;
    [Tooltip("Roguelite: furthest wave any run must have reached before this can appear. 0 = from the start.")]
    [Min(0)] public int unlockWave = 0;
    [Tooltip("Roguelite: may be offered as a starting relic.")]
    public bool starter = false;
    [Tooltip("Theme colors this card relates to (e.g. card that boosts Order synergies). Drives future card↔synergy interactions — purely metadata for now.")]
    public List<BlockColor> tags = new();

    [Header("Behavior")]
    [Tooltip("What the card does. An ItemEffect asset — StatModifierItem for plain stat changes, or a custom subclass. Each pick makes its own instance, so two copies of one card never share state.")]
    public ItemEffect effect;

    [Header("Pool")]
    [Tooltip("Relative selection weight when offered. 0 excludes from the pool.")]
    [Min(0f)] public float weight = 1f;

    [Tooltip("Maximum copies of this card that can be owned simultaneously. 0 = unlimited.")]
    [Min(0)] public int maxCopies = 1;
}
