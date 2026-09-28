using System;
using System.Collections.Generic;
using UnityEngine;

// What an upgrade card DOES — split into a definition and an instance.
//
// ── WHY TWO CLASSES ──────────────────────────────────────────────────────────
//
// A ScriptableObject is a shared asset: one object, however many times it is
// used, and in the editor it outlives Play mode. The synergy effects keep their
// runtime state on the asset itself (HarmonyAttackSpeedEffect's _buffed and _cells
// sets, for instance), and the codebase has already had to patch that once —
// TowerUpgradeGate.ResetAll exists purely because asset state leaked from one run
// into the next. For items it would be worse: two copies of the same card would
// share ONE state, so a "stacks every wave" item picked twice would count once.
//
// So the asset (ItemEffect) holds only DATA and is never mutated at runtime. Picking
// a card makes a fresh ItemEffectInstance, and that is where counters, stacks and
// event subscriptions live. Two copies, two instances.
//
// ── CLEANUP IS AUTOMATIC ─────────────────────────────────────────────────────
//
// An instance registers what it changes through Modify() and Track(), and Release()
// undoes all of it. Nobody writing an item has to remember which stats it touched
// or which events it subscribed to — which is the thing everyone eventually
// forgets, and which is how buffs outlive the run they were earned in.
public abstract class ItemEffect : ScriptableObject
{
    public abstract ItemEffectInstance CreateInstance();
}

public abstract class ItemEffectInstance
{
    public UpgradeCard Card { get; internal set; }

    readonly List<(ModifierSet set, Stat stat)> _mods = new();
    readonly List<Action> _undo = new();
    bool _live;

    // Called once, when the card is picked. Register everything here.
    protected abstract void OnAcquire(GameFlowManager game);

    // Called once, before automatic cleanup, for anything Modify/Track cannot
    // express. Most items never need it.
    protected virtual void OnRelease(GameFlowManager game) { }

    // Bend a stat for as long as this item is held. `target` defaults to the whole
    // run; pass a turret's Mods to bend that turret only. Calling again for the same
    // stat and target replaces the previous value rather than stacking on it.
    protected void Modify(Stat stat, float add = 0f, float mul = 1f, ModifierSet target = null)
    {
        var set = target ?? Modifiers.Global;
        set.Set(stat, this, add, mul);
        if (!_mods.Contains((set, stat))) _mods.Add((set, stat));
    }

    protected void Unmodify(Stat stat, ModifierSet target = null)
    {
        var set = target ?? Modifiers.Global;
        set.Remove(stat, this);
        _mods.Remove((set, stat));
    }

    // Register how to undo something — typically an event unsubscribe:
    //     PlacementController.BlockPlaced += OnPlaced;
    //     Track(() => PlacementController.BlockPlaced -= OnPlaced);
    protected void Track(Action undo)
    {
        if (undo != null) _undo.Add(undo);
    }

    internal void Acquire(GameFlowManager game)
    {
        if (_live) return;
        _live = true;
        OnAcquire(game);
    }

    // Idempotent — safe to call from both a run reset and a scene teardown.
    internal void Release(GameFlowManager game)
    {
        if (!_live) return;
        _live = false;

        try { OnRelease(game); }
        catch (Exception e) { Debug.LogError($"[Item] OnRelease threw on '{Card?.displayName}': {e}"); }

        // In reverse, so undo runs in the opposite order to setup.
        for (int i = _undo.Count - 1; i >= 0; i--)
        {
            try { _undo[i](); }
            catch (Exception e) { Debug.LogError($"[Item] undo threw on '{Card?.displayName}': {e}"); }
        }
        _undo.Clear();

        foreach (var (set, stat) in _mods) set.Remove(stat, this);
        _mods.Clear();
    }
}

// The common case, with no code: "+20% turret damage", "turrets cost 15% less".
// Authored entirely in the inspector as a list of stat changes.
[CreateAssetMenu(menuName = "GeoWorld/Items/Stat Modifier", fileName = "StatModifierItem")]
public class StatModifierItem : ItemEffect
{
    // PERCENT, not a raw multiplier. A struct added to a list in the inspector
    // starts zeroed, and a zeroed multiplier means "× 0" — so either every new row
    // silently wipes its stat out, or zero has to be special-cased to mean "no
    // change", and then "leaks cost no lives" (× 0) can never be authored. With a
    // percent, 0 already means no change and -100 means × 0. No special case.
    [Serializable]
    public struct Change
    {
        public Stat stat;
        [Tooltip("Added to the base value, before percentages.")]
        public float add;
        [Tooltip("Percent change to the result. 20 = +20%, -15 = -15%, -100 = zero it out.")]
        public float percent;
    }

    public List<Change> changes = new();

    public override ItemEffectInstance CreateInstance() => new Instance(this);

    sealed class Instance : ItemEffectInstance
    {
        readonly StatModifierItem _def;
        public Instance(StatModifierItem def) => _def = def;

        protected override void OnAcquire(GameFlowManager game)
        {
            // Two changes to the same stat on one card fold together, since
            // Modify keeps one entry per (stat, source).
            var acc = new Dictionary<Stat, (float add, float mul)>();
            foreach (var c in _def.changes)
            {
                (float add, float mul) v = acc.TryGetValue(c.stat, out var had) ? had : (0f, 1f);
                acc[c.stat] = (v.add + c.add, v.mul * Mathf.Max(0f, 1f + c.percent / 100f));
            }
            foreach (var kv in acc) Modify(kv.Key, kv.Value.add, kv.Value.mul);
        }
    }
}
