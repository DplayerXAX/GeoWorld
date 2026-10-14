using System;
using System.Collections.Generic;
using UnityEngine;

// Every number of the roguelite run in one place: its length, its economy, the threat
// curve, the elites and the bosses, what rewards cost, and the roster of enemies it
// spends the threat on.
//
// Loaded from Resources/Rogue/RogueConfig.asset. If that asset is missing the field
// defaults below are used (and the roster is built from the BalanceTable), so the mode
// runs either way — the asset exists to be tuned in the Inspector, not to be required.
//
// ── READING THE CURVE ────────────────────────────────────────────────────────
//
// `wave` everywhere is 0-based: the number of waves already cleared.
//
//   budget(w)   = budgetBase + budgetPerWave·w + budgetQuadratic·w²       (capped)
//   hp(w)       = enemy.hp × (1 + hpPerWave·w)                             (additive)
//   speed(w)    = enemy.speed × (1 + min(speedCap, speedPerWave·w))
//
// The budget is SPENT on roster entries (cost each), so a wave is "about budget
// worth of enemies"; the hp factor then makes each of them tougher. Total hit points
// a wave brings is therefore ≈ 2 · budget · hp(w) (roster cost is about hp/2).
// Print the table with the context menu "Print threat curve" on this asset.
[CreateAssetMenu(menuName = "GeoWorld/Rogue Config", fileName = "RogueConfig")]
public class RogueConfig : ScriptableObject
{
    [Serializable]
    public class EnemyEntry
    {
        public string name = "Runner";
        [Tooltip("Prefab to spawn. The same prefab may appear in several entries (a Brute is a big Runner).")]
        public EnemySurfaceUnit prefab;
        [Min(1)] public int hp = 4;
        [Tooltip("EnemySurfaceUnit.baseSpeedMultiplier.")]
        public float speed = 1f;
        [Tooltip("BLOCK currency paid on a kill.")]
        public int reward = 1;
        [Tooltip("Visual size multiplier.")]
        public float scale = 1f;
        [Header("Wave builder")]
        [Tooltip("Budget cost of one of them.")]
        [Min(1)] public int cost = 2;
        [Tooltip("First wave (0-based) it may appear on.")]
        [Min(0)] public int minWave = 0;
        public float weight = 1f;
        public int groupMin = 3, groupMax = 6;
        [Tooltip("Seconds between spawns inside a group.")]
        public float interval = 0.5f;
        [Tooltip("May be promoted to an elite.")]
        public bool eliteOk = true;
    }

    // ── Run shape ────────────────────────────────────────────────────────────
    [Header("Run shape")]
    [Min(1)] public int acts = 3;
    [Min(2)] public int wavesPerAct = 5;
    [Tooltip("1-based wave within each act that is an ELITE wave. The last wave of an act is its boss.")]
    public int eliteWaveInAct = 3;

    [Header("Start")]
    public int startBlockCurrency  = 190;
    public int startTurretCurrency = 14;
    public int startLives          = 10;
    [Tooltip("How many starter relics to choose between.")]
    [Range(2, 5)] public int starterChoices = 3;
    [Tooltip("A new endpoint every this many waves (the endless mode uses 2; the roguelite is gentler).")]
    [Min(1)] public int wavesPerEndpoint = 3;

    // ── Economy ──────────────────────────────────────────────────────────────
    [Header("Income (per build phase)")]
    public float blockIncomeBase = 32f, blockIncomePerWave = 5f;
    public float turretIncomeBase = 4f, turretIncomePerWave = 0.45f;

    // ── Threat curve ─────────────────────────────────────────────────────────
    [Header("Threat curve")]
    public float budgetBase = 14f;
    public float budgetPerWave = 6.5f;
    public float budgetQuadratic = 0.5f;
    public float budgetMax = 420f;
    [Range(0f, 0.4f)] public float budgetVariance = 0.10f;
    [Tooltip("Every spawn point runs the whole wave. Each route gets budget / routes^this, so the total grows a little with routes without multiplying.")]
    [Range(0.4f, 1f)] public float perRouteExponent = 0.75f;
    public float hpPerWave = 0.07f;
    public float speedPerWave = 0.004f;
    public float speedCap = 0.35f;
    public float groupSpacing = 1.8f;
    [Range(1f, 2f)] public float affordSlack = 1.25f;

    // ── Elites ───────────────────────────────────────────────────────────────
    [Header("Elites")]
    [Tooltip("Share of an elite wave's budget spent on elite groups.")]
    [Range(0.1f, 0.7f)] public float eliteBudgetShare = 0.35f;
    public float eliteHpMult = 2.2f;
    public float eliteScale = 1.3f;
    public float eliteRewardMult = 3f;
    [Tooltip("Armored: more HP. Swift: faster. Regenerating: heals itself. Vengeful: costs extra lives if it leaks.")]
    public float swiftMult = 1.45f;
    public float regenSeconds = 1.6f;
    public int   vengefulLeak = 2;

    // ── Bosses ───────────────────────────────────────────────────────────────
    [Header("Bosses (one per act, on its last wave)")]
    public int[] bossHp = { 150, 400, 850 };
    [Tooltip("Boss HP multiplier per act past the list.")]
    public float bossHpGrowth = 1.9f;
    public float bossSpeed = 0.55f;
    public float bossScale = 1.7f;
    public int   bossLeakDamage = 5;
    public int   bossReward = 45;
    [Tooltip("Lives restored when a boss falls (a breath between acts).")]
    public int   bossHeal = 3;
    [Tooltip("The escort's budget, as a share of a normal wave's.")]
    [Range(0f, 1f)] public float bossEscortShare = 0.5f;
    [Tooltip("Seconds between the boss blinding a synergy.")]
    public float bossAbilityInterval = 8f;
    public float bossAbilityDuration = 5f;

    // ── Rewards ──────────────────────────────────────────────────────────────
    [Header("Rarity weights  (common, uncommon, rare)")]
    public Vector3 weightsStarter = new(70f, 28f, 2f);
    public Vector3 weightsElite   = new(40f, 45f, 15f);
    public Vector3 weightsBoss    = new(10f, 50f, 40f);
    public Vector3 weightsMarket  = new(58f, 34f, 8f);

    [Header("Relic market")]
    public int marketSlots = 3;
    [Tooltip("Block currency. A 1-cell block costs about 10, so these are six to thirty blocks apiece — a relic is a real decision.")]
    public int priceCommon = 90, priceUncommon = 170, priceRare = 300;
    [Tooltip("Prices climb this fraction per wave cleared.")]
    public float priceWaveScale = 0.04f;
    public int rerollBase = 25, rerollStep = 15;
    [Tooltip("Block currency paid for passing on a free relic.")]
    public int skipBonus = 35;

    [Header("Roster")]
    public List<EnemyEntry> roster = new();

    // ── Curve ────────────────────────────────────────────────────────────────
    public float Budget(int wave)
    {
        float w = Mathf.Max(0, wave);
        return Mathf.Min(budgetMax, budgetBase + budgetPerWave * w + budgetQuadratic * w * w);
    }

    public float HpMult(int wave)    => 1f + hpPerWave * Mathf.Max(0, wave);
    public float SpeedMult(int wave) => 1f + Mathf.Min(speedCap, speedPerWave * Mathf.Max(0, wave));

    public int BossHp(int act)
    {
        if (bossHp == null || bossHp.Length == 0) return 200;
        if (act < bossHp.Length) return Mathf.Max(1, bossHp[act]);
        return Mathf.Max(1, Mathf.RoundToInt(bossHp[bossHp.Length - 1] * Mathf.Pow(bossHpGrowth, act - bossHp.Length + 1)));
    }

    public int BlockIncome(int wave)  => Mathf.Max(0, Mathf.RoundToInt(blockIncomeBase  + blockIncomePerWave  * Mathf.Max(0, wave)));
    public int TurretIncome(int wave) => Mathf.Max(0, Mathf.RoundToInt(turretIncomeBase + turretIncomePerWave * Mathf.Max(0, wave)));

    public Vector3 WeightsFor(RogueSource s) => s switch
    {
        RogueSource.Starter => weightsStarter,
        RogueSource.Elite   => weightsElite,
        RogueSource.Boss    => weightsBoss,
        _                   => weightsMarket,
    };

    // ── Roster ───────────────────────────────────────────────────────────────
    List<EnemyEntry> _resolved;

    // The roster actually used: the authored one if it has prefabs, otherwise one
    // built from the BalanceTable's enemies — so the mode is playable on a project
    // that has not authored a RogueConfig yet.
    public List<EnemyEntry> Roster
    {
        get
        {
            if (_resolved != null && _resolved.Count > 0 && _resolved[0] != null) return _resolved;

            var list = new List<EnemyEntry>();
            if (roster != null)
                foreach (var e in roster) if (e != null && e.prefab != null) list.Add(e);

            if (list.Count == 0)
            {
                var table = BalanceTable.Active;
                if (table != null && table.enemies != null)
                    foreach (var r in table.enemies)
                    {
                        if (r == null || r.prefab == null) continue;
                        list.Add(new EnemyEntry
                        {
                            name = r.name, prefab = r.prefab, hp = Mathf.Max(1, r.maxHealth), speed = r.speedMultiplier,
                            reward = r.rewardOnKill, cost = Mathf.Max(1, r.spawnCost), minWave = r.minRound,
                            weight = r.spawnWeight, groupMin = r.groupSize.x, groupMax = r.groupSize.y,
                            interval = r.spawnInterval,
                        });
                    }
            }
            if (list.Count == 0)
            {
                var d = EnemyBaseManager.Instance != null ? EnemyBaseManager.Instance.enemyPrefab : null;
                if (d != null)
                    list.Add(new EnemyEntry { name = "Enemy", prefab = d, hp = Mathf.Max(1, d.maxHealth), speed = d.baseSpeedMultiplier,
                                              reward = d.rewardOnKill, cost = 2 });
            }
            _resolved = list;
            return list;
        }
    }

    void OnEnable()  { _resolved = null; }
    void OnValidate() { _resolved = null; }

    // ── Loading ──────────────────────────────────────────────────────────────
    static RogueConfig _inst;

    public static RogueConfig Get()
    {
        if (_inst != null) return _inst;
        _inst = Resources.Load<RogueConfig>("Rogue/RogueConfig");
        if (_inst == null)
        {
            _inst = CreateInstance<RogueConfig>();
            _inst.name = "RogueConfig (defaults)";
            _inst.hideFlags = HideFlags.DontSave;
        }
        return _inst;
    }

    // ── Tuning aid ───────────────────────────────────────────────────────────
    [ContextMenu("Print threat curve")]
    void PrintCurve()
    {
        var sb = new System.Text.StringBuilder("[Rogue] threat curve (1 route)\n wave  label      budget  hp×    speed×  ≈HP total\n");
        int total = Mathf.Max(1, acts) * Mathf.Max(2, wavesPerAct) + wavesPerAct;
        for (int w = 0; w < total; w++)
        {
            int perAct = Mathf.Max(2, wavesPerAct);
            bool boss  = w % perAct == perAct - 1;
            bool elite = !boss && w % perAct == Mathf.Clamp(eliteWaveInAct, 1, perAct - 1) - 1;
            string label = $"{w / perAct + 1}-{w % perAct + 1}{(boss ? " BOSS" : elite ? " ELITE" : "")}";
            float b = Budget(w) * (boss ? bossEscortShare : 1f);
            float hp = HpMult(w);
            float extra = boss ? BossHp(w / perAct) : 0f;
            sb.AppendLine($" {w + 1,3}   {label,-11} {b,6:0.0}  {hp,5:0.00}  {SpeedMult(w),5:0.00}   {2f * b * hp + extra,7:0}");
        }
        Debug.Log(sb.ToString(), this);
    }
}

// Where a free or bought relic offer comes from — picks the rarity weights.
public enum RogueSource { Starter, Elite, Boss, Market }

// Which special rule an enemy group carries (SpawnGroup.affix).
public enum RogueAffix { None = 0, Armored = 1, Swift = 2, Regenerating = 3, Vengeful = 4, Boss = 9 }
