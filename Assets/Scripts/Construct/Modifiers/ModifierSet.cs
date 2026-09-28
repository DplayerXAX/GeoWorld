using System;
using System.Collections.Generic;
using UnityEngine;

// Every number an item, a synergy or an aura is allowed to bend.
//
// Only list a stat here once something actually READS it through Modifiers.Eval —
// an entry nobody reads is a promise the game does not keep, and an item built on
// it would silently do nothing. Where each one is read is noted beside it.
public enum Stat
{
    // Turret. Rate-like stats have a base of 1 (a multiplier), so "+30% attack
    // speed" can be written either as add 0.3 or as mul 1.3.
    TurretDamage,     // TurretController.EffectiveBulletDamage (and the AoE burn/gravity damage)
    TurretFireRate,   // TurretController.FireRateMultiplier — base 1
    TurretRange,      // TurretController.EffectiveRange

    // Economy.
    BlockPrice,       // ResourceManager.ComputePrice, non-turret blocks
    TurretPrice,      // ResourceManager.ComputePrice, turrets
    SellRefund,       // PlacementController.ComputeSellRefund — base 1, scales the refund fraction

    // Player.
    LeakDamage,       // EnemyBaseManager — lives lost per enemy that reaches an end point. Rounded.
}

// A stack of modifiers on one thing — the whole run (Modifiers.Global), or one
// turret (TurretController.Mods).
//
// WHY A STACK, NOT CHANNELS. TurretController used to keep one hand-written field
// per source — a synergy multiplier, an enemy-debuff multiplier, a shrine
// multiplier — each with its own setter, each multiplied into the result by hand.
// That works for three sources known in advance. Items are neither: any number of
// them may touch the same stat, and none of them should have to edit the turret to
// do it. Here, anything that wants to bend a stat adds an entry under its own key,
// and removes exactly that entry when it stops.
//
// ONE ENTRY PER (stat, source). Set() replaces rather than appends, so a reconciler
// that re-asserts its value every frame (the shrine aura does) cannot pile up
// duplicate copies of itself. The source is any object — an effect asset, an item
// instance, a private key.
//
// Evaluation is (base + Σadd) × Πmul. Additions go first so a flat "+1 damage" is
// itself multiplied by "+50% damage" — the usual order, and the one that makes
// flat bonuses worth taking late.
public sealed class ModifierSet
{
    struct Entry
    {
        public object source;
        public float  add;
        public float  mul;
    }

    readonly Dictionary<Stat, List<Entry>> _byStat = new();

    // Fires after any change to a stat, so a UI or a cache can refresh instead of
    // polling.
    public event Action<Stat> Changed;

    public void Set(Stat stat, object source, float add = 0f, float mul = 1f)
    {
        if (source == null) { Debug.LogWarning("[Modifiers] Set with a null source is unremovable — ignored."); return; }

        if (!_byStat.TryGetValue(stat, out var list)) _byStat[stat] = list = new List<Entry>();

        for (int i = 0; i < list.Count; i++)
        {
            if (!ReferenceEquals(list[i].source, source)) continue;
            if (Mathf.Approximately(list[i].add, add) && Mathf.Approximately(list[i].mul, mul)) return;
            list[i] = new Entry { source = source, add = add, mul = mul };
            Changed?.Invoke(stat);
            return;
        }

        list.Add(new Entry { source = source, add = add, mul = mul });
        Changed?.Invoke(stat);
    }

    public void Remove(Stat stat, object source)
    {
        if (!_byStat.TryGetValue(stat, out var list)) return;
        for (int i = list.Count - 1; i >= 0; i--)
            if (ReferenceEquals(list[i].source, source))
            {
                list.RemoveAt(i);
                Changed?.Invoke(stat);
            }
    }

    // Everything this source contributes, on every stat. What an item or effect
    // calls when it goes away, so it never has to remember which stats it touched.
    public void RemoveSource(object source)
    {
        foreach (var kv in _byStat)
        {
            var list = kv.Value;
            bool hit = false;
            for (int i = list.Count - 1; i >= 0; i--)
                if (ReferenceEquals(list[i].source, source)) { list.RemoveAt(i); hit = true; }
            if (hit) Changed?.Invoke(kv.Key);
        }
    }

    public void Clear()
    {
        var had = new List<Stat>(_byStat.Keys);
        _byStat.Clear();
        foreach (var s in had) Changed?.Invoke(s);
    }

    public float Add(Stat stat)
    {
        float a = 0f;
        if (_byStat.TryGetValue(stat, out var list))
            for (int i = 0; i < list.Count; i++) a += list[i].add;
        return a;
    }

    public float Mul(Stat stat)
    {
        float m = 1f;
        if (_byStat.TryGetValue(stat, out var list))
            for (int i = 0; i < list.Count; i++) m *= list[i].mul;
        return m;
    }

    // One source's multiplier on one stat — for UI that wants to show "this much
    // of the bonus comes from the shrine".
    public float MulOf(Stat stat, object source)
    {
        if (_byStat.TryGetValue(stat, out var list))
            for (int i = 0; i < list.Count; i++)
                if (ReferenceEquals(list[i].source, source)) return list[i].mul;
        return 1f;
    }

    public bool Has(Stat stat) => _byStat.TryGetValue(stat, out var list) && list.Count > 0;

    public float Apply(Stat stat, float value) => (value + Add(stat)) * Mul(stat);
}

// The run-wide set, and the one place a stat is actually evaluated.
//
// Static on purpose, like DeviceRegistry and CellClaims: it is the run's state,
// read from everywhere, and GameFlowManager clears it when a run starts — which it
// must, because statics survive a scene load and a leftover +50% damage from the
// last run is exactly the kind of bug nobody spots.
public static class Modifiers
{
    public static readonly ModifierSet Global = new();

    // base → the global bonuses → the local ones (one turret's, say). Additions
    // from both sets go in before either set's multipliers.
    public static float Eval(Stat stat, float value, ModifierSet local = null)
    {
        float add = Global.Add(stat) + (local?.Add(stat) ?? 0f);
        float mul = Global.Mul(stat) * (local?.Mul(stat) ?? 1f);
        return (value + add) * mul;
    }

    public static float Mul(Stat stat, ModifierSet local = null) =>
        Global.Mul(stat) * (local?.Mul(stat) ?? 1f);
}
