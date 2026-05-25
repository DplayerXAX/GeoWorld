using System;
using System.Collections.Generic;
using UnityEngine;

// Per-tag tier ladder.
//
// Example ("Development" synergy):
//   tiers = [
//     { threshold: 3, effect: +5 income per turn      },
//     { threshold: 6, effect: +12 income per turn     },
//     { threshold: 9, effect: +25 income, sell refund },
//   ]
//
// SynergyEvaluator picks the highest tier whose `threshold` <= current count
// and runs Apply on its effect (and Revoke on the previously-active tier, if
// any). At most one tier is active per synergy at any time.
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Synergy Definition", fileName = "Synergy")]
public class SynergyDefinition : ScriptableObject
{
    [Tooltip("Which tag's placed-block count drives this synergy.")]
    public BlockTag tag;

    [Tooltip("Display name shown in HUD / tooltip. Defaults to tag name.")]
    public string displayName;

    [TextArea(2, 4)] public string flavorText;

    [Tooltip("Ordered tiers. Must be sorted by ascending threshold — Validate fixes if not.")]
    public List<SynergyTier> tiers = new();

    public string DisplayLabel => string.IsNullOrEmpty(displayName) ? tag.ToString() : displayName;

    // Returns the highest tier whose threshold is met by `count`, or null.
    public SynergyTier ResolveActive(int count)
    {
        if (tiers == null) return null;
        SynergyTier active = null;
        for (int i = 0; i < tiers.Count; i++)
        {
            if (tiers[i] == null) continue;
            if (count >= tiers[i].threshold) active = tiers[i];
            else break;   // tiers ascending, no point scanning further
        }
        return active;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (tiers != null) tiers.Sort((a, b) =>
        {
            int at = a?.threshold ?? int.MaxValue;
            int bt = b?.threshold ?? int.MaxValue;
            return at.CompareTo(bt);
        });
    }
#endif
}

[Serializable]
public class SynergyTier
{
    [Tooltip("Number of placed blocks with this tag required to activate this tier.")]
    [Min(1)] public int threshold = 3;

    [Tooltip("Effect activated when the count reaches `threshold`. Authored as a separate GameEffect SO asset.")]
    public GameEffect effect;

    [Tooltip("Optional short label shown in HUD when this tier is active. e.g. 'I', 'II', 'III'.")]
    public string tierLabel;
}
