using System.Collections.Generic;
using UnityEngine;

// Works out what a level will actually throw at you, for the level-select intel
// panel: which enemy types can show up, and which special mechanics are on.
//
// Levels get their waves one of two ways, so both are covered:
//   • Authored  — LevelDefinition.waves holds WaveDefinition assets; the roster
//                 is the union of their spawn groups' prefabs.
//   • Generated — no authored waves; WaveGenerator spends a budget on
//                 BalanceTable.enemies, so the roster is every record whose
//                 minRound can come up within the level's rounds.
public static class LevelRoster
{
    // Distinct enemy prefabs this level can spawn, in first-seen order.
    public static List<EnemySurfaceUnit> Enemies(LevelDefinition lv, BalanceTable balance)
    {
        var list = new List<EnemySurfaceUnit>();
        if (lv == null) return list;

        // Authored waves first — if the designer listed them, that IS the roster.
        bool anyAuthored = false;
        if (lv.waves != null)
        {
            foreach (var w in lv.waves)
            {
                if (w == null || w.groups == null) continue;
                anyAuthored = true;
                foreach (var g in w.groups)
                    if (g?.prefab != null && !list.Contains(g.prefab)) list.Add(g.prefab);
            }
        }
        if (anyAuthored && list.Count > 0) return list;

        // Generated: every archetype unlocked within this level's round count.
        if (balance?.enemies == null) return list;
        int lastRound = Mathf.Max(0, lv.wavesToClear - 1);
        foreach (var rec in balance.enemies)
        {
            if (rec?.prefab == null) continue;
            if (rec.minRound > lastRound) continue;
            if (!list.Contains(rec.prefab)) list.Add(rec.prefab);
        }
        return list;
    }

    // Short title for the collapsed row + full text shown on hover.
    public readonly struct MechanicEntry
    {
        public readonly string title;
        public readonly string description;
        public MechanicEntry(string title, string description) { this.title = title; this.description = description; }
    }

    // Level-wide hazards that aren't an enemy type — the things a player would
    // want warned about before entering.
    public static List<MechanicEntry> SpecialMechanics(LevelDefinition lv)
    {
        var list = new List<MechanicEntry>();
        if (lv == null) return list;

        var chaos = lv.GetMechanic<ChaosBlockMechanicConfig>();
        if (chaos != null)
        {
            string when = chaos.startWave > 1 ? $" from wave {chaos.startWave}" : "";
            list.Add(new MechanicEntry("Chaos Block",
                $"Spawns each round{when} and drains {chaos.currencyDrain} block currency every wave it survives. Destroy it with turrets."));
        }

        var shrine = lv.GetMechanic<ShrineMechanicConfig>();
        if (shrine != null)
        {
            string when = shrine.startWave > 1 ? $" from wave {shrine.startWave}" : "";
            list.Add(new MechanicEntry("Enlightenment Shrine",
                $"Sprouts each round{when} next to your build and grants adjacent turrets a free, reversible upgrade while they stay touching it."));
        }

        if (lv.allowedColors != null && lv.allowedColors.Length > 0)
        {
            var names = new string[lv.allowedColors.Length];
            for (int i = 0; i < lv.allowedColors.Length; i++) names[i] = lv.allowedColors[i].ToString();
            list.Add(new MechanicEntry("Synergies",
                $"Only {string.Join(" / ", names)} blocks appear here."));
        }

        if (lv.fixedEndpoints)
            list.Add(new MechanicEntry("Fixed Route", "The start and end points are preset."));

        return list;
    }
}
