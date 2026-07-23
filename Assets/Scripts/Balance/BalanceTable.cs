using System;
using System.Collections.Generic;
using UnityEngine;

// Central balance / number-tuning table for the entire game.
//
// Author one .asset in the project (Assets > Create > GeoWorld > Balance Table)
// and drag it into the systems that need it. The table is intentionally a
// READ-ONLY data source — runtime code queries values, never writes.
//
// Sections (top → bottom matches the user's design flow):
//   1. ECONOMY    — block / turret currency, shop pricing, kill reward
//   2. PLAYER     — starting lives etc.
//   3. ENEMIES    — per-type stats + wave generator pool entries
//   4. WAVE       — budget curve used by WaveGenerator
//   5. TURRETS    — per-mode stats (cost, damage, range, fireRate, …)
//
// Per-round arrays cover R0..R(N-1) explicitly (campaign-friendly), then a
// linear formula extends past the array end for endless mode.
//
// Wiring (do AFTER this table feels right in Inspector — separate PR):
//   ResourceManager     → reads startingCurrency / income / shop multipliers
//   WaveGenerator       → reads waveBaseBudget / growth / variance / pool
//   EnemyBaseManager    → reads enemy stat overrides at spawn
//   TurretController    → reads damage / range / fireRate / cost per Mode
//   PlayerHealth        → reads startingLives
[CreateAssetMenu(menuName = "GeoWorld/Balance Table", fileName = "BalanceTable")]
public class BalanceTable : ScriptableObject
{
    // ═══════════════════════════════════════════════════════════════════════
    // 1. ECONOMY — Block currency
    // ═══════════════════════════════════════════════════════════════════════

    [Header("Block Currency")]
    [Tooltip("Block currency the player starts each run with.")]
    public int blockStartingCurrency = 80;

    [Tooltip("Block currency granted at the start of each build phase. Index = round number (0-based). When the round exceeds this array, blockIncomeEndlessGrowth extends linearly.")]
    public int[] blockIncomeByRound =
    {
        30, // R0
        32, // R1
        35, // R2
        38, // R3
        42, // R4
        46, // R5
        50, // R6
        55, // R7
        60, // R8
        66, // R9
        72, // R10
        78, // R11
        85, // R12
        92, // R13
        100 // R14
    };

    [Tooltip("Past the end of blockIncomeByRound, each additional round adds this much income (linear, for endless mode).")]
    public int blockIncomeEndlessGrowth = 8;

    // ═══════════════════════════════════════════════════════════════════════
    // 1. ECONOMY — Turret currency
    // ═══════════════════════════════════════════════════════════════════════

    [Header("Turret Currency")]
    public int turretStartingCurrency = 5;

    [Tooltip("Per-round turret currency income. Smaller numbers than block income because turrets also regen during combat.")]
    public int[] turretIncomeByRound =
    {
        2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9
    };
    public int turretIncomeEndlessGrowth = 1;

    [Tooltip("Turret currency gained per second during the combat phase (ResourceManager.Update regen tick).")]
    public float turretRegenPerSecond = 0.25f;

    [Tooltip("Turret currency granted each time an enemy walks across a block. Default 0 — per-cell income with long paths × many enemies floods the pool. Use the wave-complete + regen channels instead. Set >0 if you want a 'damage tax' feel.")]
    public int turretPerPassedBlock = 0;

    [Tooltip("Lump-sum turret currency awarded when a wave completes.")]
    public int turretBonusPerWaveComplete = 2;

    // Note: per-enemy kill reward lives on EnemyRecord.rewardOnKill (and
    // grants BLOCK currency, not turret) — there's no global kill-reward
    // multiplier here on purpose.

    // ═══════════════════════════════════════════════════════════════════════
    // 1. ECONOMY — Shop pricing
    // ═══════════════════════════════════════════════════════════════════════

    [Header("Shop Pricing")]
    [Tooltip("Base price per cell. Single = 10, I3 = 30, L4 = 40 before modifiers.")]
    public int cellBasePrice = 10;

    [Tooltip("Per-round price inflation. 0.06 = +6%/round so R10 prices are 60% higher than R0.")]
    public float roundPriceScale = 0.06f;

    [Tooltip("[min, max] random multiplier rolled once per shop spawn (Slay-the-Spire style).")]
    public Vector2 fluctuationRange = new(0.82f, 1.22f);

    [Header("Pricing — Rarity multipliers")]
    public float commonRarityMult   = 1.0f;
    public float uncommonRarityMult = 1.4f;
    public float rareRarityMult     = 2.0f;

    [Header("Pricing — Type multipliers")]
    public float liftTypeMult   = 1.4f;
    public float shadowTypeMult = 0.85f;
    [Tooltip("Basic turret price multiplier. A 1-cell turret is cells(1) × cellBasePrice(10) × this, so 0.8 ≈ 8 before the per-shop fluctuation roll — i.e. 2 cheaper than the flat 10 it used to be.")]
    public float turretTypeMult = 0.8f;
    // Other block types (Slow / AOE turrets included) use 1.0 implicitly.

    // ═══════════════════════════════════════════════════════════════════════
    // 2. PLAYER
    // ═══════════════════════════════════════════════════════════════════════

    [Header("Player")]
    [Tooltip("Lives the player starts each run with. Each enemy that escapes deducts 1.")]
    public int startingLives = 10;

    // ═══════════════════════════════════════════════════════════════════════
    // 3. ENEMY ROSTER
    // ═══════════════════════════════════════════════════════════════════════

    [Header("Enemy Roster")]
    [Tooltip("All enemy archetypes available to WaveGenerator. Drag a prefab into each record so the generator can spawn it.")]
    public List<EnemyRecord> enemies = new()
    {
        new EnemyRecord
        {
            name = "Runner",     maxHealth = 2,  speedMultiplier = 1.00f, rewardOnKill = 1,
            spawnCost = 2, minRound = 0, spawnWeight = 2.0f,
            groupSize = new Vector2Int(3, 6), spawnInterval = 0.5f
        },
        new EnemyRecord
        {
            name = "Soldier",    maxHealth = 4,  speedMultiplier = 0.85f, rewardOnKill = 2,
            spawnCost = 3, minRound = 0, spawnWeight = 1.5f,
            groupSize = new Vector2Int(3, 5), spawnInterval = 0.5f
        },
        new EnemyRecord
        {
            name = "Swarm",      maxHealth = 1,  speedMultiplier = 1.50f, rewardOnKill = 1,
            spawnCost = 1, minRound = 1, spawnWeight = 1.5f,
            groupSize = new Vector2Int(6, 10), spawnInterval = 0.25f
        },
        new EnemyRecord
        {
            name = "Tank",       maxHealth = 12, speedMultiplier = 0.60f, rewardOnKill = 5,
            spawnCost = 7, minRound = 2, spawnWeight = 0.8f,
            groupSize = new Vector2Int(1, 3), spawnInterval = 1.0f
        },
        new EnemyRecord
        {
            name = "Elite",      maxHealth = 25, speedMultiplier = 0.70f, rewardOnKill = 10,
            spawnCost = 14, minRound = 5, spawnWeight = 0.5f,
            groupSize = new Vector2Int(1, 2), spawnInterval = 1.5f
        },
    };

    // ═══════════════════════════════════════════════════════════════════════
    // 4. WAVE BUDGET (WaveGenerator)
    // ═══════════════════════════════════════════════════════════════════════

    [Header("Wave Budget")]
    [Tooltip("Total wave 'spending power' at R0. With default enemy costs (Runner=2, Soldier=3, Tank=7), 15 buys roughly 7 runners or 5 soldiers — a meaningful wave instead of 3 runners.")]
    public float waveBaseBudget = 15f;

    [Tooltip("Extra budget added per round. Linear.")]
    public float waveBudgetGrowth = 6f;

    [Tooltip("±fraction applied multiplicatively to budget per wave (random).")]
    [Range(0f, 0.5f)] public float waveBudgetVariance = 0.15f;

    [Tooltip("Hard cap so late-game waves don't explode.")]
    public float waveBudgetMax = 120f;

    [Tooltip("Seconds between sub-groups within a generated wave.")]
    public float waveGroupSpacing = 1.5f;

    [Tooltip("Allow buying an entry whose cost slightly exceeds remaining budget. 1.0 = strict, 1.5 = up to 50% overshoot.")]
    [Range(1f, 2f)] public float waveAffordSlack = 1.2f;

    [Tooltip("Extra enemy HP per round, ADDITIVE (not compounding) — e.g. 0.2 means R0 enemies are ×1.0, R1 ×1.2, R5 ×2.0. Applied on top of each EnemyRecord.maxHealth at spawn.")]
    public float enemyHealthGrowthPerRound = 0.2f;

    // ═══════════════════════════════════════════════════════════════════════
    // 5. TURRET STATS
    // ═══════════════════════════════════════════════════════════════════════

    [Header("Turret — Basic (single-target, cheap)")]
    public TurretRecord basicTurret = new()
    {
        cost = 3, damage = 1.0f, range = 4.0f, fireRate = 1.5f,
    };

    [Header("Turret — Slow (low DMG, applies slow debuff)")]
    public TurretRecord slowTurret = new()
    {
        cost = 5, damage = 0.3f, range = 3.0f, fireRate = 1.0f,
        slowFactor = 0.5f, slowDuration = 1.5f,
    };

    [Header("Turret — AOE (high DMG, slow fire, radius splash)")]
    public TurretRecord aoeTurret = new()
    {
        cost = 8, damage = 2.0f, range = 3.5f, fireRate = 0.6f,
        aoeRadius = 1.5f,
    };

    // ═══════════════════════════════════════════════════════════════════════
    // Records (inner types)
    // ═══════════════════════════════════════════════════════════════════════

    [Serializable]
    public class EnemyRecord
    {
        [Tooltip("Display name — also appears in logs / UI.")]
        public string name = "Runner";

        [Tooltip("Prefab used by EnemyBaseManager / WaveGenerator. Null = falls back to the procedural default enemy.")]
        public EnemySurfaceUnit prefab;

        [Header("Combat")]
        [Min(1)] public int maxHealth = 3;
        [Tooltip("Sets EnemySurfaceUnit.baseSpeedMultiplier at spawn — 1.0 = default speed, 0.5 = half speed (tank), 1.5 = swarm-fast. Goes through the canonical field so WaveGenerator's BiasTauntAheadOfFast can still see the fast / slow rating from the prefab list.")]
        [Min(0.01f)] public float speedMultiplier = 1.0f;
        [Tooltip("BLOCK currency awarded when this enemy is killed. Combat-success income that feeds the building budget (counterpart to turret currency, which earns from combat engagement: per-block-pass, wave-complete, regen).")]
        public int rewardOnKill = 1;
        [Tooltip("Sets EnemySurfaceUnit.targetPriority at spawn — used by WaveGenerator.BiasTauntAheadOfFast to push taunt enemies to the front of the spawn order. 0 = normal, >0 = taunt.")]
        public int targetPriority = 0;

        [Header("Wave-generator metadata")]
        [Tooltip("Budget cost per spawn. Group size × cost is deducted.")]
        [Min(1)] public int spawnCost = 2;
        [Tooltip("First round (inclusive) where this enemy may appear.")]
        [Min(0)] public int minRound = 0;
        [Tooltip("Relative selection probability among eligible entries. 0 = disabled.")]
        [Min(0f)] public float spawnWeight = 1f;
        [Tooltip("Group size sampled uniformly in [x, y].")]
        public Vector2Int groupSize = new(3, 6);
        [Tooltip("Seconds between spawns inside this group.")]
        [Min(0f)] public float spawnInterval = 0.5f;
    }

    [Serializable]
    public class TurretRecord
    {
        [Tooltip("UNUSED — shop price does NOT come from here. Every block (turrets included) is priced by ComputePrice: cells × cellBasePrice × rarity × type × round × fluctuation. To change what a turret costs, edit the Type multipliers above (turretTypeMult / slowTurretTypeMult / aoeTurretTypeMult), not this field.")]
        [Min(1)] public int cost = 3;

        [Tooltip("Damage per shot.")]
        public float damage = 1.0f;
        [Tooltip("World-space targeting range in cells.")]
        public float range = 4.0f;
        [Tooltip("Shots per second.")]
        public float fireRate = 1.5f;

        [Header("Slow-mode only")]
        [Tooltip("Speed multiplier applied to hit enemies. 0.5 = half speed.")]
        [Range(0f, 1f)] public float slowFactor = 1.0f;
        [Tooltip("Duration of the slow debuff in seconds.")]
        public float slowDuration = 0.0f;

        [Header("AOE-mode only")]
        [Tooltip("Splash radius around the primary hit (in cells).")]
        public float aoeRadius = 0.0f;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Query API
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Block currency granted at the start of build phase for the given round.</summary>
    public int GetBlockIncomeForRound(int round)
    {
        if (round < 0) return 0;
        if (blockIncomeByRound == null || blockIncomeByRound.Length == 0) return 0;
        if (round < blockIncomeByRound.Length) return blockIncomeByRound[round];
        int last      = blockIncomeByRound[blockIncomeByRound.Length - 1];
        int extraRounds = round - (blockIncomeByRound.Length - 1);
        return last + extraRounds * blockIncomeEndlessGrowth;
    }

    /// <summary>Turret currency granted at the start of build phase for the given round.</summary>
    public int GetTurretIncomeForRound(int round)
    {
        if (round < 0) return 0;
        if (turretIncomeByRound == null || turretIncomeByRound.Length == 0) return 0;
        if (round < turretIncomeByRound.Length) return turretIncomeByRound[round];
        int last      = turretIncomeByRound[turretIncomeByRound.Length - 1];
        int extraRounds = round - (turretIncomeByRound.Length - 1);
        return last + extraRounds * turretIncomeEndlessGrowth;
    }

    /// <summary>Returns the cumulative block income from round 0 to <paramref name="upToRound"/> (inclusive). Useful for "if player saved everything, how rich could they be" analysis.</summary>
    public int GetCumulativeBlockIncome(int upToRound)
    {
        int sum = blockStartingCurrency;
        for (int r = 0; r <= upToRound; r++) sum += GetBlockIncomeForRound(r);
        return sum;
    }

    /// <summary>Returns the cumulative turret income from round 0 to <paramref name="upToRound"/> (inclusive). Ignores in-combat regen, pass rewards, and kill rewards.</summary>
    public int GetCumulativeTurretIncome(int upToRound)
    {
        int sum = turretStartingCurrency;
        for (int r = 0; r <= upToRound; r++) sum += GetTurretIncomeForRound(r);
        return sum;
    }

    /// <summary>Wave generator total spending budget for the given round (before random variance).</summary>
    public float GetWaveBudgetForRound(int round)
    {
        float raw = waveBaseBudget + waveBudgetGrowth * Mathf.Max(0, round);
        return Mathf.Min(raw, waveBudgetMax);
    }

    /// <summary>Additive (non-compounding) HP multiplier for the given round: R0 = 1.0, R1 = 1+growth, R2 = 1+2*growth, …</summary>
    public float GetEnemyHealthMultiplier(int round) => 1f + Mathf.Max(0, round) * enemyHealthGrowthPerRound;

    /// <summary>Turret stat record for a given mode. Returns basicTurret for unknown modes.</summary>
    public TurretRecord GetTurretStats(TurretController.Mode mode) => mode switch
    {
        TurretController.Mode.Slow => slowTurret,
        TurretController.Mode.Aoe  => aoeTurret,
        _                          => basicTurret,
    };

    /// <summary>Rarity price multiplier from the pricing rules. Mirrors ResourceManager's switch.</summary>
    public float GetRarityMult(BlockRarity r) => r switch
    {
        BlockRarity.Common   => commonRarityMult,
        BlockRarity.Uncommon => uncommonRarityMult,
        BlockRarity.Rare     => rareRarityMult,
        _                    => 1f,
    };

    /// <summary>Type price multiplier from the pricing rules. Mirrors ResourceManager's switch.</summary>
    public float GetTypeMult(BlockType t) => t switch
    {
        BlockType.Lift   => liftTypeMult,
        BlockType.Shadow => shadowTypeMult,
        BlockType.Turret => turretTypeMult,
        _                => 1f,
    };

    /// <summary>Compute final shop price for a block (matches ResourceManager.ComputePrice formula). Useful for previewing balance from editor scripts without going through ResourceManager.</summary>
    public int ComputePrice(BlockData data, int round, float fluctuation)
    {
        if (data == null) return 0;
        int   cells     = (data.cells != null && data.cells.Length > 0) ? data.cells.Length : 1;
        float roundMult = 1f + Mathf.Max(0, round) * roundPriceScale;
        float raw       = cells * cellBasePrice
                          * GetRarityMult(data.rarity)
                          * GetTypeMult(data.blockType)
                          * roundMult
                          * fluctuation;
        return Mathf.Max(1, Mathf.RoundToInt(raw));
    }
}
