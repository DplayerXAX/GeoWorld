using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// The relic catalog of the roguelite mode.
//
// Each relic is a small ItemEffectInstance (see Construct/Upgrades/ItemEffect.cs): it
// registers what it changes through Modify() and Track() and the framework undoes all
// of it when the relic is released. Nothing here has to remember what it touched.
//
// The catalog builds UpgradeCard ScriptableObjects at runtime from the table at the
// bottom, so adding a relic is one class and one line — there is no asset to author
// and no list in a scene to keep in step.
//
// ── CATEGORIES (the brainstorm sheet's columns) ──────────────────────────────
//   Economic · Buff · Chaotic ("powerful but chaotic") · Special (a rule bent) ·
//   Trash ("could be useful?") · Healing
public sealed class RogueItemDef
{
    public string       id, name, description;
    public ItemCategory category;
    public Rarity       rarity;
    public int          unlockWave;           // best wave any run must have reached; 0 = from the start
    public bool         starter;              // may be offered as a starting relic
    public Func<ItemEffectInstance> make;
}

public static class RogueItems
{
    static List<RogueItemDef> _defs;
    static List<UpgradeCard>  _cards;

    public static IReadOnlyList<RogueItemDef> Defs { get { Build(); return _defs; } }
    public static IReadOnlyList<UpgradeCard>  Cards { get { Build(); return _cards; } }

    public static UpgradeCard Find(string id)
    {
        Build();
        foreach (var c in _cards) if (c != null && c.name == id) return c;
        return null;
    }

    // Has any run got far enough for this relic to be in the pool?
    public static bool IsUnlocked(UpgradeCard c)
    {
        if (c == null) return false;
        if (c.unlockWave <= 0) return true;
        return SaveSystem.Profile != null && SaveSystem.Profile.rogueBestWave >= c.unlockWave;
    }

    public static List<UpgradeCard> NewlyUnlocked(int oldBest, int newBest)
    {
        Build();
        var l = new List<UpgradeCard>();
        foreach (var c in _cards)
            if (c != null && c.unlockWave > oldBest && c.unlockWave <= newBest) l.Add(c);
        return l;
    }

    static Color RarityColor(Rarity r) => r switch
    {
        Rarity.Rare     => new Color(0.91f, 0.70f, 0.23f),   // GeoPalette.Gold
        Rarity.Uncommon => new Color(0.17f, 0.42f, 0.69f),   // GeoPalette.Blue
        _               => new Color(0.42f, 0.44f, 0.48f),
    };
    public static Color ColorOf(Rarity r) => RarityColor(r);

    public static Color ColorOf(ItemCategory c) => c switch
    {
        ItemCategory.Economic => new Color(0.91f, 0.70f, 0.23f),
        ItemCategory.Buff     => new Color(0.20f, 0.72f, 0.35f),
        ItemCategory.Chaotic  => new Color(0.89f, 0.14f, 0.11f),
        ItemCategory.Special  => new Color(0.60f, 0.30f, 0.88f),
        ItemCategory.Trash    => new Color(0.55f, 0.55f, 0.55f),
        _                     => new Color(0.95f, 0.45f, 0.62f),   // Healing
    };

    public static string NameOf(ItemCategory c) => c switch
    {
        ItemCategory.Economic => "ECONOMIC",
        ItemCategory.Buff     => "BUFF",
        ItemCategory.Chaotic  => "CHAOTIC",
        ItemCategory.Special  => "SPECIAL",
        ItemCategory.Trash    => "JUNK",
        _                     => "HEALING",
    };

    // ── Build ────────────────────────────────────────────────────────────────

    public static void Build()
    {
        if (_cards != null && _cards.Count > 0 && _cards[0] != null) return;

        _defs  = new List<RogueItemDef>();
        _cards = new List<UpgradeCard>();

        void D(string id, string name, string desc, ItemCategory cat, Rarity rar,
               Func<ItemEffectInstance> make, int unlock = 0, bool starter = false)
            => _defs.Add(new RogueItemDef { id = id, name = name, description = desc, category = cat, rarity = rar,
                                           make = make, unlockWave = unlock, starter = starter });

        // ── Economic ─────────────────────────────────────────────────────────
        D("tribute_gate",  "Tribute Gate",   "Whenever an enemy enters one of your active synergies, gain 1 block currency (up to 30 a wave).",
          ItemCategory.Economic, Rarity.Uncommon, () => new ItemTributeGate());
        D("traders_joy",   "Trader Joy",     "Your next shop purchase is free. Then this relic is spent.",
          ItemCategory.Economic, Rarity.Common,   () => new ItemTradersJoy(), starter: true);
        D("morphos",       "Morphos",        "When you cannot afford a turret, every missing turret currency can be paid with 10 block currency.",
          ItemCategory.Economic, Rarity.Uncommon, () => new ItemMorphos());
        D("recurrence",    "Recurrence",     "Whenever you buy something, gain 15% of its price back.",
          ItemCategory.Economic, Rarity.Uncommon, () => new ItemRecurrence());
        D("clearance",     "Clearance",      "Every time you refresh the shop, the new stock is 20% off.",
          ItemCategory.Economic, Rarity.Common,   () => new ItemClearance());
        D("recycle",       "Recycle",        "If you start a wave nearly broke (under 5 of each currency), the next round's income is doubled.",
          ItemCategory.Economic, Rarity.Uncommon, () => new ItemRecycle());
        D("grow",          "Grow",           "+0.01 turret damage for every 10 block currency you earn this round.",
          ItemCategory.Economic, Rarity.Common,   () => new ItemGrow());
        D("lucky_number",  "Lucky Number",   "Every fraction that is a GAIN rounds up instead of down: refunds, bonuses, paybacks.",
          ItemCategory.Economic, Rarity.Common,   () => new ItemLucky());
        D("affinity",      "Affinity",       "Shop blocks are 40% likelier to take the colour of the last block you sold.",
          ItemCategory.Economic, Rarity.Common,   () => new ItemAffinity());
        D("dividend",      "Dividend",       "At the start of each wave, gain 1 block currency for every 20 you hold (up to 5).",
          ItemCategory.Economic, Rarity.Uncommon, () => new ItemDividend(), unlock: 4);
        D("bulk_order",    "Bulk Order",     "Blocks cost 10% less.",
          ItemCategory.Economic, Rarity.Common,   () => new ItemStats((Stat.BlockPrice, 0f, 0.90f)));
        D("salvage",       "Salvage Rights", "Selling refunds 30% more.",
          ItemCategory.Economic, Rarity.Common,   () => new ItemStats((Stat.SellRefund, 0f, 1.30f)));
        D("tithe",         "Tithe",          "Turret currency regenerates 40% faster during combat.",
          ItemCategory.Economic, Rarity.Uncommon, () => new ItemStats((Stat.TurretRegen, 0f, 1.40f)), unlock: 3);
        D("bounty_board",  "Bounty Board",   "Kills pay 30% more block currency.",
          ItemCategory.Economic, Rarity.Uncommon, () => new ItemStats((Stat.KillReward, 0f, 1.30f)), starter: true);
        D("harvest",       "Harvest",        "ONE USE: gain 10 turret currency for every turret on the board.",
          ItemCategory.Economic, Rarity.Rare,     () => new ItemHarvest(), unlock: 4);

        // ── Buff ─────────────────────────────────────────────────────────────
        D("concentric_ring", "Concentric Ring", "+10% turret damage for each different synergy you have active (up to +60%).",
          ItemCategory.Buff, Rarity.Uncommon, () => new ItemConcentricRing(), starter: true);
        D("prism",         "Prism",          "A turret that keeps firing at the same target gains +10% damage per shot, up to +120%.",
          ItemCategory.Buff, Rarity.Rare,     () => new ItemPrism(), unlock: 6);
        D("greedy",        "Greedy",         "+1% turret damage for every turret currency you hold (up to +30%).",
          ItemCategory.Buff, Rarity.Common,   () => new ItemGreedy(), starter: true);
        D("mountain",      "Mountain",       "Turrets fire faster the higher they stand: +5% per layer above the lowest block, up to +25%.",
          ItemCategory.Buff, Rarity.Common,   () => new ItemMountain());
        D("unity",         "Unity",          "A turret deals +7% damage for every other turret within its range.",
          ItemCategory.Buff, Rarity.Uncommon, () => new ItemUnity(), starter: true);
        D("isolation",     "Isolation",      "A turret with no other turret in its range deals +0.2 damage.",
          ItemCategory.Buff, Rarity.Common,   () => new ItemIsolation(), starter: true);
        D("whetstone",     "Whetstone",      "+12% turret damage.",
          ItemCategory.Buff, Rarity.Common,   () => new ItemStats((Stat.TurretDamage, 0f, 1.12f)), starter: true);
        D("long_lens",     "Long Lens",      "+12% turret range.",
          ItemCategory.Buff, Rarity.Common,   () => new ItemStats((Stat.TurretRange, 0f, 1.12f)));
        D("metronome",     "Metronome",      "+10% turret fire rate.",
          ItemCategory.Buff, Rarity.Common,   () => new ItemStats((Stat.TurretFireRate, 0f, 1.10f)));
        D("overclock",     "Overclock",      "+25% fire rate, but turrets cost 20% more.",
          ItemCategory.Buff, Rarity.Uncommon, () => new ItemStats((Stat.TurretFireRate, 0f, 1.25f), (Stat.TurretPrice, 0f, 1.20f)), unlock: 2);
        D("echo",          "Echo",           "Every fifth shot a turret fires deals double damage.",
          ItemCategory.Buff, Rarity.Rare,     () => new ItemEcho(), unlock: 8);

        // ── Powerful, but chaotic ────────────────────────────────────────────
        D("labyrinth",     "Labyrinth",      "Enemies move 50% faster, but take the longest route they can find and never cross a block twice.",
          ItemCategory.Chaotic, Rarity.Rare,     () => new ItemLabyrinth(), unlock: 4);
        D("thorn",         "Thorn in Flesh", "Lose 1 life at the start of each round (never below 1). Turrets fire 15% faster for each life lost that round.",
          ItemCategory.Chaotic, Rarity.Uncommon, () => new ItemThorn());
        D("anxiety",       "Anxiety",        "Turrets miss 25% of their shots, but 20% of the rest deal double damage.",
          ItemCategory.Chaotic, Rarity.Common,   () => new ItemAnxiety());
        D("crowded",       "Crowded",        "Enemies wait rather than overlap, and move 20% faster.",
          ItemCategory.Chaotic, Rarity.Uncommon, () => new ItemCrowded(), unlock: 2);
        D("glass_cannon",  "Glass Cannon",   "+60% turret damage. Every enemy that gets through costs double.",
          ItemCategory.Chaotic, Rarity.Uncommon, () => new ItemStats((Stat.TurretDamage, 0f, 1.60f), (Stat.LeakDamage, 0f, 2f)), unlock: 3);
        D("wild_card",     "Wild Card",      "Each wave, one random turret deals 2.5× damage and another only half.",
          ItemCategory.Chaotic, Rarity.Uncommon, () => new ItemWildCard(), unlock: 3);
        D("expectation",   "Expectation",    "While you have NO active synergy, turret damage is doubled.",
          ItemCategory.Chaotic, Rarity.Uncommon, () => new ItemExpectation());
        D("nuke",          "Nuke",           "ONE USE: pick a spot and destroy everything within 3 cells — blocks, turrets, enemies, spawn points. Not your home.",
          ItemCategory.Chaotic, Rarity.Rare,     () => new ItemNuke(), unlock: 8);

        // ── Special mechanics ────────────────────────────────────────────────
        D("tender_sprout", "Tender Sprout",  "Harmony blocks also count toward Abundance, and Abundance blocks toward Harmony.",
          ItemCategory.Special, Rarity.Uncommon, () => new ItemTenderSprout(), starter: true);
        D("mirror",        "Mirror",         "While every placed block is symmetrical: enemies have 10% less HP and speed, and 10% of leaks cost nothing.",
          ItemCategory.Special, Rarity.Rare,     () => new ItemMirror(), unlock: 6);
        D("user_experience", "User Experience", "Activating a synergy you have not activated before lets you sell at full price for the rest of the round.",
          ItemCategory.Special, Rarity.Common,   () => new ItemUserExperience());
        D("pain_releaser", "Pain Releaser",  "Every different debuff on an enemy reduces the healing it receives by 10%.",
          ItemCategory.Special, Rarity.Common,   () => new ItemPainReleaser());
        D("compressor",    "Compressor",     "The slower an enemy is, the longer its ability takes to recharge (up to +40%).",
          ItemCategory.Special, Rarity.Common,   () => new ItemCompressor());
        D("second_wind",   "Second Wind",    "Once: when your last life would be lost, keep 3.",
          ItemCategory.Special, Rarity.Rare,     () => new ItemSecondWind(), unlock: 5);

        // ── Trash, but could be useful ───────────────────────────────────────
        D("loose_change",  "Loose Change",   "+4 block currency at the start of every round.",
          ItemCategory.Trash, Rarity.Common,   () => new ItemLooseChange());
        D("lead_boots",    "Lead Boots",     "Enemies move 12% slower. Your turrets fire 6% slower.",
          ItemCategory.Trash, Rarity.Common,   () => new ItemStats((Stat.EnemySpeed, 0f, 0.88f), (Stat.TurretFireRate, 0f, 0.94f)));

        // ── Healing ──────────────────────────────────────────────────────────
        D("heart",         "Heart",          "+2 maximum lives, and heal 2.",
          ItemCategory.Healing, Rarity.Uncommon, () => new ItemHeart(), starter: true);
        D("last_resort",   "Last Resort",    "If you finish a wave on exactly 1 life, heal 1.",
          ItemCategory.Healing, Rarity.Common,   () => new ItemLastResort());
        D("bandage",       "Bandage",        "Heal 1 life after every second wave.",
          ItemCategory.Healing, Rarity.Common,   () => new ItemBandage());
        D("rations",       "Rations",        "A wave in which nothing gets through heals 1 life.",
          ItemCategory.Healing, Rarity.Uncommon, () => new ItemRations(), unlock: 2);

        foreach (var d in _defs)
        {
            var card = ScriptableObject.CreateInstance<UpgradeCard>();
            card.hideFlags   = HideFlags.DontSave;
            card.name        = d.id;
            card.displayName = d.name;
            card.description = d.description;
            card.rarity      = d.rarity;
            card.category    = d.category;
            card.unlockWave  = d.unlockWave;
            card.starter     = d.starter;
            card.weight      = 1f;
            card.maxCopies   = 1;
            var fx = ScriptableObject.CreateInstance<CodeItem>();
            fx.hideFlags = HideFlags.DontSave;
            fx.factory = d.make;
            card.effect = fx;
            _cards.Add(card);
        }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// Item base
// ═════════════════════════════════════════════════════════════════════════════
abstract class RItem : ItemEffectInstance
{
    protected static GameFlowManager Game => GameFlowManager.Instance;
    protected static PlayerHealth    Hp   => PlayerHealth.Instance;
    protected static ResourceManager Rm   => ResourceManager.Instance;

    // Subscriptions that undo themselves when the relic is released.
    protected void OnTurnStart(Action a)   { GameFlowManager.OnTurnStarted += a; Track(() => GameFlowManager.OnTurnStarted -= a); }
    protected void OnWaveBegin(Action a)   { GameFlowManager.OnWaveBegan += a;   Track(() => GameFlowManager.OnWaveBegan -= a); }
    protected void OnWaveClear(Action<int> a) { RogueRun.WaveCleared += a;       Track(() => RogueRun.WaveCleared -= a); }

    protected void OnTier(Action<SynergyRule, int, int> a)
    {
        var ev = SynergyEvaluator.Instance;
        if (ev == null) return;
        ev.OnTierChanged += a;
        Track(() => { if (SynergyEvaluator.Instance != null) SynergyEvaluator.Instance.OnTierChanged -= a; });
    }

    // Distinct synergy colours with an effective (non-jammed) instance on the board.
    protected static int ActiveSynergyKinds()
    {
        var ev = SynergyEvaluator.Instance;
        if (ev == null) return 0;
        var set = new HashSet<BlockColor>();
        foreach (var a in ev.Actives)
            if (a != null && !a.Suppressed && a.rule != null) set.Add(a.rule.color);
        return set.Count;
    }

    protected void Changed() => RogueRun.RaiseRelicsChanged();
}

// A relic that is nothing but stat changes.
sealed class ItemStats : RItem
{
    readonly (Stat stat, float add, float mul)[] _s;
    public ItemStats(params (Stat stat, float add, float mul)[] s) { _s = s; }
    protected override void OnAcquire(GameFlowManager g) { foreach (var c in _s) Modify(c.stat, c.add, c.mul); }
}

// ═════════════════════════════════════════════════════════════════════════════
// Economic
// ═════════════════════════════════════════════════════════════════════════════

sealed class ItemTributeGate : RItem
{
    const int CapPerWave = 30;
    readonly Dictionary<EnemySurfaceUnit, ActiveSynergy> _in = new();
    float _t; int _paid;

    protected override void OnAcquire(GameFlowManager g) => OnWaveBegin(() => { _paid = 0; _in.Clear(); });

    protected override void OnTick(float dt)
    {
        _t += dt;
        if (_t < 0.12f) return;
        _t = 0f;

        var ev = SynergyEvaluator.Instance; var mgr = EnemyBaseManager.Instance;
        if (ev == null || mgr == null || Rm == null) return;

        var list = mgr.ActiveEnemies;
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            if (e == null || e.CurrentHealth <= 0 || e.IsAlly) continue;
            var c = e.CurrentCell;
            ActiveSynergy now = c.HasValue ? ev.FindActiveAtCell(c.Value) : null;
            _in.TryGetValue(e, out var was);
            if (now != was)
            {
                if (now != null && !now.Suppressed && _paid < CapPerWave)
                {
                    _paid++;
                    Rm.AddBlockCurrency(1);
                    CurrencyFlyFx.Fly(e.transform.position, false, 1);
                }
                _in[e] = now;
            }
        }
    }

    public override string Status => $"{_paid}/{CapPerWave}";
}

sealed class ItemTradersJoy : RItem
{
    bool _used;

    protected override void OnAcquire(GameFlowManager g)
    {
        RogueRun.FreePurchases++;
        RogueRun.FreePurchaseUsed += OnUsed;
        Track(() => RogueRun.FreePurchaseUsed -= OnUsed);
        Track(() => { if (!_used) RogueRun.FreePurchases = Mathf.Max(0, RogueRun.FreePurchases - 1); });
    }

    void OnUsed() { if (_used) return; _used = true; Changed(); }
    public override string Status => _used ? "spent" : "READY";
}

sealed class ItemMorphos : RItem
{
    protected override void OnAcquire(GameFlowManager g)
    {
        RogueRun.MorphosActive = true;
        Track(() => RogueRun.MorphosActive = false);
    }
}

sealed class ItemRecurrence : RItem
{
    protected override void OnAcquire(GameFlowManager g)
    {
        ResourceManager.Purchased += OnBought;
        Track(() => ResourceManager.Purchased -= OnBought);
    }

    void OnBought(int price, BlockType type)
    {
        int back = RogueRun.Gain(price * 0.15f);
        if (back <= 0 || Rm == null) return;
        if (TurretTypes.Is(type)) Rm.AddTurretCurrency(back);
        else                      Rm.AddBlockCurrency(back);
    }
}

sealed class ItemClearance : RItem
{
    protected override void OnAcquire(GameFlowManager g)
    {
        PlacementController.ShopRefreshed += OnRefresh;
        Track(() => PlacementController.ShopRefreshed -= OnRefresh);
    }
    void OnRefresh() => ShopController.Instance?.ScalePrices(0.8f);
}

sealed class ItemRecycle : RItem
{
    bool _armed;

    protected override void OnAcquire(GameFlowManager g)
    {
        OnWaveBegin(() =>
        {
            if (_armed || Rm == null) return;
            if (Rm.BlockCurrency < 5 && Rm.TurretCurrency < 5)
            {
                _armed = true;
                Modify(Stat.BlockIncome, 0f, 2f);
                Modify(Stat.TurretIncome, 0f, 2f);
                Changed();
            }
        });
        ResourceManager.IncomeGranted += OnIncome;
        Track(() => ResourceManager.IncomeGranted -= OnIncome);
    }

    void OnIncome()
    {
        if (!_armed) return;
        _armed = false;
        Unmodify(Stat.BlockIncome);
        Unmodify(Stat.TurretIncome);
        Changed();
    }

    public override string Status => _armed ? "×2 NEXT" : null;
}

sealed class ItemGrow : RItem
{
    int _earned;

    protected override void OnAcquire(GameFlowManager g)
    {
        ResourceManager.BlockEarned += OnEarn;
        Track(() => ResourceManager.BlockEarned -= OnEarn);
        OnWaveClear(_ => { _earned = 0; Apply(); });
        Apply();
    }

    void OnEarn(int n) { _earned += Mathf.Max(0, n); Apply(); }
    void Apply() => Modify(Stat.TurretDamage, 0.01f * (_earned / 10), 1f);
    public override string Status => $"+{0.01f * (_earned / 10):0.00}";
}

sealed class ItemLucky : RItem
{
    protected override void OnAcquire(GameFlowManager g)
    {
        RogueRun.LuckyRounding = true;
        Track(() => RogueRun.LuckyRounding = false);
    }
}

sealed class ItemAffinity : RItem
{
    protected override void OnAcquire(GameFlowManager g)
    {
        RogueRun.AffinityChance = Mathf.Max(RogueRun.AffinityChance, 0.40f);
        Track(() => RogueRun.AffinityChance = 0f);
    }
}

sealed class ItemDividend : RItem
{
    protected override void OnAcquire(GameFlowManager g) => OnWaveBegin(() =>
    {
        if (Rm == null) return;
        int gain = Mathf.Min(5, Rm.BlockCurrency / 20);
        if (gain > 0) Rm.AddBlockCurrency(gain);
    });
}

sealed class ItemHarvest : RItem
{
    bool _spent;
    public override bool HasActive => true;
    public override bool CanUse => !_spent;
    public override string Status => _spent ? "spent" : "USE";

    public override void Use(GameFlowManager game)
    {
        if (_spent || Rm == null) return;
        int n = 0;
        foreach (var t in TurretController.All) if (t != null && t.isActiveAndEnabled) n++;
        _spent = true;
        if (n > 0) Rm.AddTurretCurrency(10 * n);
        PlacementController.Instance?.ShowPlacementPopup($"Harvest: +{10 * n} turret currency");
        Changed();
    }

    protected override void OnAcquire(GameFlowManager g) { }
}

// ═════════════════════════════════════════════════════════════════════════════
// Buff
// ═════════════════════════════════════════════════════════════════════════════

sealed class ItemConcentricRing : RItem
{
    protected override void OnAcquire(GameFlowManager g) { OnTier((r, a, b) => Refresh()); Refresh(); }

    void Refresh()
    {
        int n = ActiveSynergyKinds();
        Modify(Stat.TurretDamage, 0f, 1f + 0.10f * Mathf.Min(n, 6));
        Changed();
    }
    public override string Status => $"+{Mathf.Min(ActiveSynergyKinds(), 6) * 10}%";
}

sealed class ItemPrism : RItem
{
    const int MaxStacks = 12;
    readonly Dictionary<TurretController, (EnemySurfaceUnit target, int n)> _chain = new();

    protected override void OnAcquire(GameFlowManager g)
    {
        TurretController.AnyFired += OnFired;
        Track(() => TurretController.AnyFired -= OnFired);
        OnWaveBegin(() => _chain.Clear());
    }

    void OnFired(TurretController t, EnemySurfaceUnit target)
    {
        if (t == null) return;
        _chain.TryGetValue(t, out var c);
        int n = (target != null && c.target == target) ? Mathf.Min(c.n + 1, MaxStacks) : 0;
        _chain[t] = (target, n);
        Modify(Stat.TurretDamage, 0f, 1f + 0.10f * n, t.Mods);
    }
}

sealed class ItemGreedy : RItem
{
    protected override void OnAcquire(GameFlowManager g)
    {
        if (Rm != null)
        {
            Rm.OnTurretCurrencyChanged += OnChanged;
            var rm = Rm;
            Track(() => { if (rm != null) rm.OnTurretCurrencyChanged -= OnChanged; });
        }
        Apply();
    }

    void OnChanged(int _) => Apply();
    void Apply() => Modify(Stat.TurretDamage, 0f, 1f + Mathf.Min(0.30f, 0.01f * (Rm != null ? Rm.TurretCurrency : 0)));
    public override string Status => $"+{Mathf.Min(30, Rm != null ? Rm.TurretCurrency : 0)}%";
}

sealed class ItemMountain : RItem
{
    float _t;

    protected override void OnTick(float dt)
    {
        _t += dt;
        if (_t < 0.5f) return;
        _t = 0f;

        var grid = GridSystem.instance;
        if (grid == null) return;
        int baseY = int.MaxValue;
        foreach (var ins in grid.GetAllInstances())
        {
            if (ins?.occupiedCells == null) continue;
            foreach (var c in ins.occupiedCells) if (c.y < baseY) baseY = c.y;
        }
        if (baseY == int.MaxValue) return;

        foreach (var t in TurretController.All)
        {
            if (t == null) continue;
            int y = grid.WorldToGrid(t.transform.position).y;
            float pct = Mathf.Clamp((y - baseY) * 0.05f, 0f, 0.25f);
            Modify(Stat.TurretFireRate, 0f, 1f + pct, t.Mods);
        }
    }

    protected override void OnAcquire(GameFlowManager g) { }
}

sealed class ItemUnity : RItem
{
    float _t;
    protected override void OnAcquire(GameFlowManager g) { }

    protected override void OnTick(float dt)
    {
        _t += dt;
        if (_t < 0.5f) return;
        _t = 0f;

        var all = TurretController.All;
        for (int i = 0; i < all.Count; i++)
        {
            var t = all[i];
            if (t == null) continue;
            int n = NeighboursIn(t, all);
            Modify(Stat.TurretDamage, 0f, 1f + 0.07f * Mathf.Min(n, 10), t.Mods);
        }
    }

    internal static int NeighboursIn(TurretController t, List<TurretController> all)
    {
        float r = Mathf.Max(0.5f, t.EffectiveRange), r2 = r * r;
        int n = 0;
        for (int j = 0; j < all.Count; j++)
        {
            var u = all[j];
            if (u == null || u == t) continue;
            if ((u.transform.position - t.transform.position).sqrMagnitude <= r2) n++;
        }
        return n;
    }
}

sealed class ItemIsolation : RItem
{
    float _t;
    protected override void OnAcquire(GameFlowManager g) { }

    protected override void OnTick(float dt)
    {
        _t += dt;
        if (_t < 0.5f) return;
        _t = 0f;

        var all = TurretController.All;
        for (int i = 0; i < all.Count; i++)
        {
            var t = all[i];
            if (t == null) continue;
            Modify(Stat.TurretDamage, ItemUnity.NeighboursIn(t, all) == 0 ? 0.2f : 0f, 1f, t.Mods);
        }
    }
}

sealed class ItemEcho : RItem
{
    readonly Dictionary<TurretController, int> _n = new();
    Func<TurretController, float> _f;

    protected override void OnAcquire(GameFlowManager g)
    {
        _f = t =>
        {
            if (t == null) return 1f;
            _n.TryGetValue(t, out int n);
            n++;
            _n[t] = n;
            return n % 5 == 0 ? 2f : 1f;
        };
        RogueRun.AddShotMult(_f);
        Track(() => RogueRun.RemoveShotMult(_f));
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// Chaotic
// ═════════════════════════════════════════════════════════════════════════════

sealed class ItemLabyrinth : RItem
{
    protected override void OnAcquire(GameFlowManager g)
    {
        Modify(Stat.EnemySpeed, 0f, 1.5f);
        RogueRun.PathTransform = RogueRoutes.Stretch;
        Track(() => { if (RogueRun.PathTransform == (Func<List<FaceNode>, List<FaceNode>>)RogueRoutes.Stretch) RogueRun.PathTransform = null; });
    }
}

sealed class ItemThorn : RItem
{
    int _lost, _last;

    protected override void OnAcquire(GameFlowManager g)
    {
        _last = Hp != null ? Hp.CurrentLives : 0;
        OnTurnStart(OnTurn);
        if (Hp != null)
        {
            var hp = Hp;
            hp.OnLivesChanged += OnLives;
            Track(() => { if (hp != null) hp.OnLivesChanged -= OnLives; });
        }
    }

    void OnTurn()
    {
        _lost = 0;
        if (Hp != null) _lost += Hp.Sacrifice(1, 1);
        _last = Hp != null ? Hp.CurrentLives : 0;
        Apply();
    }

    void OnLives(int lives)
    {
        if (lives < _last) { _lost += _last - lives; Apply(); }
        _last = lives;
    }

    void Apply() { Modify(Stat.TurretFireRate, 0f, 1f + 0.15f * _lost); Changed(); }
    public override string Status => _lost > 0 ? $"+{_lost * 15}%" : null;
}

sealed class ItemAnxiety : RItem
{
    Func<TurretController, float> _f;

    protected override void OnAcquire(GameFlowManager g)
    {
        _f = _ =>
        {
            if (UnityEngine.Random.value < 0.25f) return 0f;       // a miss
            return UnityEngine.Random.value < 0.20f ? 2f : 1f;     // a double
        };
        RogueRun.AddShotMult(_f);
        Track(() => RogueRun.RemoveShotMult(_f));
    }
}

sealed class ItemCrowded : RItem
{
    protected override void OnAcquire(GameFlowManager g)
    {
        Modify(Stat.EnemySpeed, 0f, 1.2f);
        RogueRun.AvoidOverlap = true;
        Track(() => RogueRun.AvoidOverlap = false);
    }
}

sealed class ItemWildCard : RItem
{
    readonly List<TurretController> _marked = new();

    protected override void OnAcquire(GameFlowManager g)
    {
        OnWaveBegin(Roll);
        OnWaveClear(_ => Clear());
    }

    void Roll()
    {
        Clear();
        var pool = new List<TurretController>();
        foreach (var t in TurretController.All)
            if (t != null && t.isActiveAndEnabled
                && t.mode != TurretController.Mode.Debuff && t.mode != TurretController.Mode.Gravity) pool.Add(t);
        if (pool.Count == 0) return;

        var up = pool[UnityEngine.Random.Range(0, pool.Count)];
        Modify(Stat.TurretDamage, 0f, 2.5f, up.Mods);
        _marked.Add(up);
        pool.Remove(up);
        if (pool.Count > 0)
        {
            var down = pool[UnityEngine.Random.Range(0, pool.Count)];
            Modify(Stat.TurretDamage, 0f, 0.5f, down.Mods);
            _marked.Add(down);
        }
    }

    void Clear()
    {
        foreach (var t in _marked) if (t != null) Unmodify(Stat.TurretDamage, t.Mods);
        _marked.Clear();
    }
}

sealed class ItemExpectation : RItem
{
    protected override void OnAcquire(GameFlowManager g) { OnTier((r, a, b) => Apply()); Apply(); }
    void Apply() { Modify(Stat.TurretDamage, 0f, ActiveSynergyKinds() == 0 ? 2f : 1f); Changed(); }
    public override string Status => ActiveSynergyKinds() == 0 ? "×2" : "off";
}

sealed class ItemNuke : RItem
{
    const float RadiusCells = 3.2f;
    bool _spent;

    public override bool HasActive => true;
    public override bool CanUse => !_spent && !RogueTargeting.Active
        && Game != null && (Game.phase == GamePhase.Build || Game.phase == GamePhase.ReadyToRun);
    public override string Status => _spent ? "spent" : "USE";

    public override void Use(GameFlowManager game)
    {
        if (!CanUse) return;
        RogueTargeting.Begin(RadiusCells, "Click to detonate   -   Esc to cancel", cell =>
        {
            RogueNukeEffect.Detonate(cell, RadiusCells);
            _spent = true;
            Changed();
        });
    }

    protected override void OnAcquire(GameFlowManager g) { }
}

// ═════════════════════════════════════════════════════════════════════════════
// Special mechanics
// ═════════════════════════════════════════════════════════════════════════════

sealed class ItemTenderSprout : RItem
{
    protected override void OnAcquire(GameFlowManager g)
    {
        BoardSnapshot.AddAlias(BlockColor.Abundance, BlockColor.Harmony);
        BoardSnapshot.AddAlias(BlockColor.Harmony, BlockColor.Abundance);
        Track(() =>
        {
            BoardSnapshot.RemoveAlias(BlockColor.Abundance, BlockColor.Harmony);
            BoardSnapshot.RemoveAlias(BlockColor.Harmony, BlockColor.Abundance);
            SynergyEvaluator.Instance?.ForceReEvaluate();
        });
        SynergyEvaluator.Instance?.ForceReEvaluate();
    }
}

sealed class ItemMirror : RItem
{
    float _t;
    bool _sym;

    protected override void OnAcquire(GameFlowManager g)
    {
        Track(() => RogueRun.LeakNegateChance = 0f);
    }

    protected override void OnTick(float dt)
    {
        _t += dt;
        if (_t < 0.5f) return;
        _t = 0f;

        bool s = BoardIsSymmetric();
        if (s == _sym) return;
        _sym = s;
        if (s)
        {
            Modify(Stat.EnemyHealth, 0f, 0.9f);
            Modify(Stat.EnemySpeed,  0f, 0.9f);
            RogueRun.LeakNegateChance = 0.10f;
        }
        else
        {
            Unmodify(Stat.EnemyHealth);
            Unmodify(Stat.EnemySpeed);
            RogueRun.LeakNegateChance = 0f;
        }
        Changed();
    }

    // Invariant under a reflection across x, across z, or a half turn — about the
    // middle of the whole build.
    static bool BoardIsSymmetric()
    {
        var grid = GridSystem.instance;
        if (grid == null) return false;

        var cells = new HashSet<Vector3Int>();
        foreach (var ins in grid.GetAllInstances())
            if (ins?.occupiedCells != null) foreach (var c in ins.occupiedCells) cells.Add(c);
        if (cells.Count < 4) return false;

        int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
        foreach (var c in cells)
        {
            if (c.x < minX) minX = c.x; if (c.x > maxX) maxX = c.x;
            if (c.z < minZ) minZ = c.z; if (c.z > maxZ) maxZ = c.z;
        }
        if (minX == maxX && minZ == maxZ) return false;

        bool mx = true, mz = true, rot = true;
        foreach (var c in cells)
        {
            int fx = minX + maxX - c.x, fz = minZ + maxZ - c.z;
            if (mx  && !cells.Contains(new Vector3Int(fx, c.y, c.z))) mx = false;
            if (mz  && !cells.Contains(new Vector3Int(c.x, c.y, fz))) mz = false;
            if (rot && !cells.Contains(new Vector3Int(fx, c.y, fz)))  rot = false;
            if (!mx && !mz && !rot) return false;
        }
        return mx || mz || rot;
    }

    public override string Status => _sym ? "SYMMETRIC" : "-";
}

sealed class ItemUserExperience : RItem
{
    readonly HashSet<BlockColor> _seen = new();

    protected override void OnAcquire(GameFlowManager g)
    {
        OnTier((rule, from, to) =>
        {
            if (rule == null || from != 0 || to <= 0) return;
            if (_seen.Add(rule.color)) { RogueRun.FullRefund = true; Changed(); }
        });
        OnTurnStart(() => { if (RogueRun.FullRefund) { RogueRun.FullRefund = false; Changed(); } });
        Track(() => RogueRun.FullRefund = false);
    }

    public override string Status => RogueRun.FullRefund ? "FULL" : null;
}

sealed class ItemPainReleaser : RItem
{
    Func<EnemySurfaceUnit, float> _f;
    protected override void OnAcquire(GameFlowManager g)
    {
        _f = e => e == null ? 1f : Mathf.Max(0f, 1f - 0.10f * e.DebuffCount);
        RogueRun.AddHealScale(_f);
        Track(() => RogueRun.RemoveHealScale(_f));
    }
}

sealed class ItemCompressor : RItem
{
    Func<EnemySurfaceUnit, float> _f;
    protected override void OnAcquire(GameFlowManager g)
    {
        _f = e => e == null ? 1f : 1f + 0.40f * (1f - Mathf.Clamp01(e.TemporarySpeedMultiplier));
        RogueRun.AddCooldownScale(_f);
        Track(() => RogueRun.RemoveCooldownScale(_f));
    }
}

sealed class ItemSecondWind : RItem
{
    bool _used;
    Func<int> _f;

    protected override void OnAcquire(GameFlowManager g)
    {
        _f = () => { if (_used) return 0; _used = true; Changed(); return 3; };
        RogueRun.AddDeathSave(_f);
        Track(() => RogueRun.RemoveDeathSave(_f));
    }

    public override string Status => _used ? "spent" : "READY";
}

// ═════════════════════════════════════════════════════════════════════════════
// Trash, but could be useful
// ═════════════════════════════════════════════════════════════════════════════

sealed class ItemLooseChange : RItem
{
    protected override void OnAcquire(GameFlowManager g) => OnTurnStart(() => Rm?.AddBlockCurrency(4));
}

// ═════════════════════════════════════════════════════════════════════════════
// Healing
// ═════════════════════════════════════════════════════════════════════════════

sealed class ItemHeart : RItem
{
    protected override void OnAcquire(GameFlowManager g)
    {
        Hp?.AddMaxLives(2, fill: false);
        Hp?.Heal(2);
        Track(() => Hp?.AddMaxLives(-2));
    }
}

sealed class ItemLastResort : RItem
{
    protected override void OnAcquire(GameFlowManager g) => OnWaveClear(_ =>
    {
        if (Hp != null && Hp.CurrentLives == 1) Hp.Heal(1);
    });
}

sealed class ItemBandage : RItem
{
    int _n;
    protected override void OnAcquire(GameFlowManager g) => OnWaveClear(_ =>
    {
        _n++;
        if (_n % 2 == 0) Hp?.Heal(1);
        Changed();
    });
    public override string Status => _n % 2 == 0 ? "0/2" : "1/2";
}

sealed class ItemRations : RItem
{
    int _at;
    protected override void OnAcquire(GameFlowManager g)
    {
        OnWaveBegin(() => _at = Hp != null ? Hp.CurrentLives : 0);
        OnWaveClear(_ => { if (Hp != null && Hp.CurrentLives >= _at && _at > 0) Hp.Heal(1); });
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// Labyrinth: lengthening a route
// ═════════════════════════════════════════════════════════════════════════════
//
// Finding the LONGEST simple path is intractable in general, and nobody needs the
// longest — only one that is plainly longer, found fast. So: start from the shortest
// route and, one hop at a time, try to swap that hop for a detour through ground the
// route has not touched. The detour search is a bounded depth-first walk; the total
// work is capped, so a big board costs a moment, never a hang.
public static class RogueRoutes
{
    const int MaxDetourNodes = 9;       // intermediate nodes in one detour
    const int ExpansionsPerHop = 400;   // DFS steps spent looking at one hop
    const float MaxGrowth = 3.2f;       // never longer than this × the original

    public static List<FaceNode> Stretch(List<FaceNode> path)
    {
        if (path == null || path.Count < 2) return path;

        int limit = Mathf.CeilToInt(path.Count * MaxGrowth);
        var used = new HashSet<Vector3Int>();
        foreach (var n in path) used.Add(n.cell);

        var result = new List<FaceNode>(path.Count * 2);
        for (int i = 0; i < path.Count - 1; i++)
        {
            result.Add(path[i]);
            if (result.Count + (path.Count - 1 - i) >= limit) continue;   // out of room: copy the rest as it is

            var detour = FindDetour(path[i], path[i + 1], used);
            if (detour == null) continue;
            foreach (var d in detour) { result.Add(d); used.Add(d.cell); }
        }
        result.Add(path[path.Count - 1]);
        return result;
    }

    // The longest a → … → b through nodes whose BLOCK has not been used, found within
    // the expansion budget. Null when there is none (the hop stays direct).
    static List<FaceNode> FindDetour(FaceNode a, FaceNode b, HashSet<Vector3Int> used)
    {
        List<FaceNode> best = null;
        var stack = new List<FaceNode>();
        var local = new HashSet<Vector3Int>();
        int budget = ExpansionsPerHop;

        void Walk(FaceNode at)
        {
            if (budget-- <= 0) return;
            foreach (var nb in at.neighbors)
            {
                if (budget <= 0) return;
                if (nb == null) continue;

                if (nb == b)
                {
                    if (stack.Count >= 1 && (best == null || stack.Count > best.Count)) best = new List<FaceNode>(stack);
                    continue;
                }
                if (stack.Count >= MaxDetourNodes) continue;
                if (used.Contains(nb.cell) || local.Contains(nb.cell)) continue;

                local.Add(nb.cell);
                stack.Add(nb);
                Walk(nb);
                stack.RemoveAt(stack.Count - 1);
                local.Remove(nb.cell);
            }
        }

        Walk(a);
        return best;
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// Nuke
// ═════════════════════════════════════════════════════════════════════════════
public static class RogueNukeEffect
{
    public static void Detonate(Vector3Int centre, float radiusCells)
    {
        var grid = GridSystem.instance;
        var gfm  = GameFlowManager.Instance;
        if (grid == null || gfm == null) return;

        float cs = grid.cellSize;
        float r2 = radiusCells * radiusCells;
        Vector3 world = grid.GridToWorld(centre);

        // Blocks and turrets: collected first, removed after — removal edits the grid.
        var doomed = new List<PlacedBlockInstance>();
        foreach (var ins in grid.GetAllInstances())
        {
            if (ins?.occupiedCells == null) continue;
            foreach (var c in ins.occupiedCells)
            {
                float dx = c.x - centre.x, dy = c.y - centre.y, dz = c.z - centre.z;
                if (dx * dx + dy * dy + dz * dz <= r2) { doomed.Add(ins); break; }
            }
        }
        foreach (var ins in doomed)
        {
            if (ins.visualObject != null) BlockDissolveFx.Play(ins.visualObject, 1.0f);
            var cells = ins.occupiedCells.ToArray();
            ResourceManager.Instance?.OnBlockRemoved(ins.data.blockType);
            SynergyEvaluator.Instance?.OnPieceRemoved(ins.placedPiece);
            PathFlowManager.Instance?.RemoveFlowsOverlapping(cells);
            LoopManager.Instance?.RemoveLoopsOverlapping(cells);
            grid.RemoveInstance(ins);
        }

        // Spawn points (never the last one, never the home).
        foreach (var s in new List<Vector3Int>(gfm.AllStarts))
        {
            float dx = s.x - centre.x, dy = s.y - centre.y, dz = s.z - centre.z;
            if (dx * dx + dy * dy + dz * dz <= r2) gfm.RemoveStartAt(s);
        }

        // Enemies: everything dies but a boss, which is only hurt.
        var mgr = EnemyBaseManager.Instance;
        if (mgr != null)
        {
            var list = new List<EnemySurfaceUnit>(mgr.ActiveEnemies);
            float wr = radiusCells * cs;
            foreach (var e in list)
            {
                if (e == null || e.CurrentHealth <= 0) continue;
                if ((e.transform.position - world).sqrMagnitude > wr * wr) continue;
                bool boss = e.GetComponent<RogueBossBehaviour>() != null;
                e.TakeDamage(boss ? Mathf.Max(1, e.maxHealth * 3 / 10) : e.maxHealth * 10);
            }
        }

        TurretShotFx.Blast(world, radiusCells * cs, new Color(1f, 0.55f, 0.15f), 0.8f);
        CameraShake.Damage();
        gfm.EvaluateGrid();
    }
}
