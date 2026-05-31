using UnityEngine;

// Shared base for any "thing that mutates run state" — used by both the
// block-synergy system (tier activations) and the upgrade card system
// (per-wave picks). One abstract, both worlds.
//
// Lifecycle:
//   • Apply(game) is called when the effect becomes active.
//   • Revoke(game) is called when it becomes inactive.
//
// For synergy effects, Revoke matters — tiers de-activate when block counts
// drop below threshold (player removes a block, etc). Override Revoke and
// undo whatever Apply did (unsubscribe events, restore multipliers, etc).
//
// For upgrade-card effects, Revoke is usually a no-op — cards picked stay
// active for the run. The base class's empty default covers this case.
public abstract class GameEffect : ScriptableObject
{
    public abstract void Apply(GameFlowManager game);

    public virtual void Revoke(GameFlowManager game) { /* default: no-op */ }
}
