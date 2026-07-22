using UnityEngine;

// Base for an optional, self-contained systemic mechanic a level can opt into
// (Chaos Block, Enlightenment Shrine, ...). Each concrete mechanic is BOTH the
// "is this mechanic on" flag AND its own parameter sheet, in one asset: add an
// instance to LevelDefinition.mechanics to enable it for that level (with
// whatever values that instance carries); leave it out to disable it. Multiple
// levels can share one asset (same tuning) or each get their own.
//
// The controller for a mechanic (e.g. ChaosBlockController) looks itself up via
// LevelDefinition.GetMechanic<T>() instead of reading level-specific bool/param
// fields directly — this is what lets new mechanics be added without touching
// LevelDefinition itself.
public abstract class LevelMechanicConfig : ScriptableObject
{
}
