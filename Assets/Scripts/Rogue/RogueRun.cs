using System;
using System.Collections.Generic;
using UnityEngine;

// The roguelite mode's run state, and the small set of hook points the rest of the
// game exposes to items.
//
// ── WHY A STATIC CLASS OF HOOKS ──────────────────────────────────────────────
//
// Most items bend a number (damage, price, income) and go through Modifiers. A few
// change a RULE — "the next purchase is free", "10 block currency counts as 1 turret
// currency", "enemies take the longest path" — and a rule has to be read by the
// system that owns it. Those systems read the flags and delegates below, and nothing
// else: they never learn that items exist, and an item never edits them. An item sets
// a flag when it is acquired and (through ItemEffectInstance.Track) clears it when it
// is released, exactly like a stat.
//
// Statics survive a scene load, so RogueDirector.Awake calls Reset() — a flag left
// set by the last run is the same bug as a leftover +50% damage.
public static class RogueRun
{
    // ── Mode ─────────────────────────────────────────────────────────────────
    public static bool Active => RunConfig.Mode == GameMode.Rogue;
    public static RogueConfig Config => RogueConfig.Get();

    // ── Act structure ────────────────────────────────────────────────────────
    // `wave` is 0-based: the number of waves already cleared == the wave coming.
    public static int WavesPerAct => Mathf.Max(2, Config.wavesPerAct);
    public static int ActOf(int wave) => Mathf.Max(0, wave) / WavesPerAct;
    public static int WaveInAct(int wave) => Mathf.Max(0, wave) % WavesPerAct;
    public static bool IsBossWave(int wave) => WaveInAct(wave) == WavesPerAct - 1;
    public static bool IsEliteWave(int wave) =>
        WaveInAct(wave) == Mathf.Clamp(Config.eliteWaveInAct, 1, WavesPerAct - 1) - 1 && !IsBossWave(wave);
    public static bool IsFinalWave(int wave) => IsBossWave(wave) && ActOf(wave) == Mathf.Max(1, Config.acts) - 1;

    // "2-3", or "1-5 BOSS" — for the HUD and the intel panel.
    public static string WaveLabel(int wave, bool withTag = true)
    {
        string s = $"{ActOf(wave) + 1}-{WaveInAct(wave) + 1}";
        if (!withTag) return s;
        if (IsBossWave(wave)) return s + " BOSS";
        if (IsEliteWave(wave)) return s + " ELITE";
        return s;
    }

    // ── Run flags (set by items; cleared by Reset and by the item's release) ─
    public static int   FreePurchases;           // Trader Joy — shop purchases that cost nothing
    public static bool  MorphosActive;           // 10 block currency stands in for 1 turret currency
    public const  int   MorphosRate = 10;
    public static bool  LuckyRounding;           // fractions round UP when they are gains
    public static float AffinityChance;          // chance a shop slot takes the last sold colour
    public static float LeakNegateChance;        // chance a leaking enemy costs no life
    public static bool  FullRefund;              // sells pay the full price
    public static bool  AvoidOverlap;            // enemies queue instead of stacking

    // Enemy route rewriting (Labyrinth). Applied when a wave starts and on reroute.
    public static Func<List<FaceNode>, List<FaceNode>> PathTransform;

    // ── Events ───────────────────────────────────────────────────────────────
    public static event Action FreePurchaseUsed;
    public static event Action<int> WaveCleared;     // total waves cleared, after this one
    public static event Action<int> WaveStarted;     // 0-based index of the wave that begins
    public static event Action RelicsChanged;        // owned set changed (acquired / used / released)
    public static event Action MarketChanged;        // merchant stock or prices changed

    public static void RaiseWaveCleared(int cleared) => WaveCleared?.Invoke(cleared);
    public static void RaiseWaveStarted(int index)   => WaveStarted?.Invoke(index);
    public static void RaiseRelicsChanged()          => RelicsChanged?.Invoke();
    public static void RaiseMarketChanged()          => MarketChanged?.Invoke();

    public static bool ConsumeFreePurchase()
    {
        if (FreePurchases <= 0) return false;
        FreePurchases--;
        FreePurchaseUsed?.Invoke();
        return true;
    }

    // ── Multicast-with-results hooks ─────────────────────────────────────────
    // A delegate field would return only its LAST subscriber's value, and several
    // items can touch the same thing; so these are lists, combined here.

    static readonly List<Func<TurretController, float>> _shotMult = new();
    public static void AddShotMult(Func<TurretController, float> f)    { if (f != null) _shotMult.Add(f); }
    public static void RemoveShotMult(Func<TurretController, float> f) => _shotMult.Remove(f);

    // Multiplier on the damage of ONE shot (0 = a miss). Read by TurretController.RollDamage.
    public static float ShotMult(TurretController t)
    {
        float m = 1f;
        for (int i = 0; i < _shotMult.Count; i++) m *= _shotMult[i](t);
        return Mathf.Max(0f, m);
    }

    static readonly List<Func<EnemySurfaceUnit, float>> _healScale = new();
    public static void AddHealScale(Func<EnemySurfaceUnit, float> f)    { if (f != null) _healScale.Add(f); }
    public static void RemoveHealScale(Func<EnemySurfaceUnit, float> f) => _healScale.Remove(f);

    // Healing an enemy receives, scaled. Whole numbers only, so the fraction is
    // rolled rather than rounded: 1 heal at ×0.9 lands nine times in ten, not never.
    public static int ScaleHeal(EnemySurfaceUnit e, int amount)
    {
        if (_healScale.Count == 0 || amount <= 0) return amount;
        float m = 1f;
        for (int i = 0; i < _healScale.Count; i++) m *= _healScale[i](e);
        float v = Mathf.Max(0f, amount * m);
        int n = Mathf.FloorToInt(v);
        if (UnityEngine.Random.value < v - n) n++;
        return n;
    }

    static readonly List<Func<EnemySurfaceUnit, float>> _cooldownScale = new();
    public static void AddCooldownScale(Func<EnemySurfaceUnit, float> f)    { if (f != null) _cooldownScale.Add(f); }
    public static void RemoveCooldownScale(Func<EnemySurfaceUnit, float> f) => _cooldownScale.Remove(f);

    // How much longer an enemy's active ability takes to come back (1 = unchanged).
    public static float AbilityCooldownScale(EnemySurfaceUnit e)
    {
        float m = 1f;
        for (int i = 0; i < _cooldownScale.Count; i++) m *= _cooldownScale[i](e);
        return Mathf.Max(0.05f, m);
    }

    // The last life lost. Each saver answers with the lives to be left on (0 = no).
    static readonly List<Func<int>> _deathSaves = new();
    public static void AddDeathSave(Func<int> f)    { if (f != null) _deathSaves.Add(f); }
    public static void RemoveDeathSave(Func<int> f) => _deathSaves.Remove(f);

    public static bool TryDeathSave(out int lives)
    {
        for (int i = 0; i < _deathSaves.Count; i++)
        {
            int l = _deathSaves[i]();
            if (l > 0) { lives = l; return true; }
        }
        lives = 0;
        return false;
    }

    // ── Rounding ─────────────────────────────────────────────────────────────
    // Every GAIN (income, a refund, a percentage paid back) goes through here, so
    // Lucky Number has exactly one place to act. Costs and damage do not: rounding a
    // price UP would make the item a penalty.
    public static int Gain(float x)
    {
        if (x <= 0f) return 0;
        return LuckyRounding ? Mathf.CeilToInt(x - 0.0001f) : Mathf.RoundToInt(x);
    }

    // ── Run stats (the summary, the score) ───────────────────────────────────
    public static int BossesDefeated, ElitesDefeated, Leaks, RelicsBought, Rerolls;
    public static int ActiveSpawnPoints = 1;

    public static int Score(int wavesCleared)
    {
        int lives = PlayerHealth.Instance != null ? PlayerHealth.Instance.CurrentLives : 0;
        int relics = UpgradeManager.Instance != null ? UpgradeManager.Instance.Owned.Count : 0;
        return Mathf.Max(0, wavesCleared) * 150
             + BossesDefeated * 600
             + ElitesDefeated * 120
             + relics * 25
             + lives * 40
             + RunStats.Kills * 6;
    }

    public static string SummaryLine(int wavesCleared)
    {
        int relics = UpgradeManager.Instance != null ? UpgradeManager.Instance.Owned.Count : 0;
        int next = Mathf.Max(0, wavesCleared);
        return $"Reached  {WaveLabel(next, false)}   ·   {wavesCleared} wave{(wavesCleared == 1 ? "" : "s")} cleared   ·   {relics} relic{(relics == 1 ? "" : "s")}   ·   score {Score(wavesCleared)}";
    }

    // ── Reset ────────────────────────────────────────────────────────────────
    public static void Reset()
    {
        FreePurchases = 0;
        MorphosActive = false;
        LuckyRounding = false;
        AffinityChance = 0f;
        LeakNegateChance = 0f;
        FullRefund = false;
        AvoidOverlap = false;
        PathTransform = null;
        _shotMult.Clear();
        _healScale.Clear();
        _cooldownScale.Clear();
        _deathSaves.Clear();
        BossesDefeated = ElitesDefeated = Leaks = RelicsBought = Rerolls = 0;
        ActiveSpawnPoints = 1;
    }
}
