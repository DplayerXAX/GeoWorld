using System;
using System.Collections.Generic;
using UnityEngine;

// ── Resource economy ──────────────────────────────────────────────────────────
//
// Two resource pools:
//
//   blockCurrency  spent at the block shop to buy path blocks.
//
//   Shop price formula (computed once per shop spawn, stored in cachedPrice):
//     price = cells × cellBasePrice × rarityMult × typeMult × roundMult × fluctuation
//       rarityMult : Common 1.0 / Uncommon 1.4 / Rare 2.0
//       typeMult   : Lift 1.4 / Shadow 0.85 / others 1.0
//       roundMult  : 1 + RoundIndex × roundPriceScale
//       fluctuation: Random [0.82, 1.22], rolled once at shop spawn Slay the Spire style
//
//   turretCurrency spent by the turret system (another team).
//                    Slowly regenerates during combat.
//
// INCOME
//   GrantRoundIncome() called by GameFlowManager.StartTurn() each build phase.
//   Turret currency regenerates at turretRegenPerSecond while combat is active.
//
// PHASE LOCKING (driven by GameFlowManager)
//   SetCombatActive(true)  call when Running phase starts (enables regen)
//   SetCombatActive(false) call when transitioning back to Build
//
// UI
//   Subscribe to OnBlockCurrencyChanged(int) and OnTurretCurrencyChanged(int).
//   Subscribe to OnInsufficientFunds(BlockType) for "can't afford" feedback.
//
// BATTLE-SYSTEM API (other team)
//   OnEnemyPassedBlock(BlockType) earns turret currency per block walked over
//   OnWaveComplete()              wave-end bonus + triggers round income
// ─────────────────────────────────────────────────────────────────────────────
public class ResourceManager : MonoBehaviour
{
    public static ResourceManager Instance;

    [Header("Balance")]
    [Tooltip("Central balance asset. When set, overrides all Inspector defaults below on Awake — those values become fallbacks for when this slot is empty.")]
    public BalanceTable balance;

    public bool testing = false;
    [Header("Block Currency")]
    [Tooltip("Block currency the player starts the game with.")]
    public int startingBlockCurrency = 80;
    [Tooltip("Block currency earned at the start of each build phase.")]
    public int blockCurrencyPerRound = 30;

    [Header("Block Shop pricing")]
    [Tooltip("Base price per cell. Single=10, I2=20, L4=40 before modifiers.")]
    public int cellBasePrice = 10;
    [Tooltip("Price multiplier increase per completed round. 0.06 = +6%/round.")]
    public float roundPriceScale = 0.06f;

    [Header("Turret Currency")]
    public int startingTurretCurrency = 5;
    [Tooltip("Turret currency earned at the start of each build phase.")]
    public int turretCurrencyPerRound = 2;
    [Tooltip("Turret currency gained per second during combat.")]
    public float turretRegenPerSecond = 0.25f;

    // ── Events ────────────────────────────────────────────────────────────────
    /// <summary>Fires when block currency amount changes. Subscribe for UI updates.</summary>
    public event Action<int>       OnBlockCurrencyChanged;
    /// <summary>Fires when turret currency amount changes.</summary>
    public event Action<int>       OnTurretCurrencyChanged;
    /// <summary>Fires when a purchase attempt fails due to insufficient funds.</summary>
    public event Action<BlockType> OnInsufficientFunds;

    // ── Properties ────────────────────────────────────────────────────────────
    public int BlockCurrency  => _blockCurrency;
    public int TurretCurrency => _turretCurrency;

    // ── State ─────────────────────────────────────────────────────────────────
    int   _blockCurrency;
    int   _turretCurrency;
    float _turretRegenAccum;
    bool  _combatActive;

    // Number of each type currently placed on the grid drives price scaling.
    readonly Dictionary<BlockType, int> _placedCounts = new();

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Awake()
    {
        Instance = this;

        // Pull authoritative values from the BalanceTable asset if assigned.
        // Inspector fields stay as fallbacks for when no balance is wired.
        if (balance != null)
        {
            startingBlockCurrency  = balance.blockStartingCurrency;
            startingTurretCurrency = balance.turretStartingCurrency;
            cellBasePrice          = balance.cellBasePrice;
            roundPriceScale        = balance.roundPriceScale;
            turretRegenPerSecond   = balance.turretRegenPerSecond;
            // Per-round income values come from balance.GetXxxIncomeForRound()
            // dynamically (not stored as fields here) so the curve takes
            // effect immediately when the asset is edited.
        }

        _blockCurrency  = startingBlockCurrency;
        _turretCurrency = startingTurretCurrency;
        if (testing)
        {
            _blockCurrency = 9999;
            _turretCurrency = 9999;

        }
        foreach (BlockType t in Enum.GetValues(typeof(BlockType)))
            _placedCounts[t] = 0;
    }

    void Update()
    {
        if (!_combatActive) return;

        _turretRegenAccum += turretRegenPerSecond * Time.deltaTime;
        if (_turretRegenAccum >= 1f)
        {
            int gain = Mathf.FloorToInt(_turretRegenAccum);
            _turretRegenAccum -= gain;
            AddTurretCurrency(gain);
        }
    }

    // ── Phase toggle ──────────────────────────────────────────────────────────
    /// <summary>
    /// Called by GameFlowManager when entering/leaving combat.
    /// Enables turret currency regeneration while true.
    /// </summary>
    public void SetCombatActive(bool active)
    {
        _combatActive     = active;
        if (!active) _turretRegenAccum = 0f;
    }

    // ── Round income ──────────────────────────────────────────────────────────
    /// <summary>Called by GameFlowManager.StartTurn() at the start of each build phase.</summary>
    public void GrantRoundIncome()
    {
        int round = GameFlowManager.Instance != null ? GameFlowManager.Instance.RoundIndex : 0;
        int blockInc = balance != null
            ? balance.GetBlockIncomeForRound(round)
            : blockCurrencyPerRound;
        int turretInc = balance != null
            ? balance.GetTurretIncomeForRound(round)
            : turretCurrencyPerRound;

        _blockCurrency  += blockInc;
        _turretCurrency += turretInc;
        OnBlockCurrencyChanged?.Invoke(_blockCurrency);
        OnTurretCurrencyChanged?.Invoke(_turretCurrency);
    }

    // ── Block shop ────────────────────────────────────────────────────────────

    // Rarity multipliers: more-powerful shapes cost exponentially more.
    static float RarityMult(BlockRarity r) => r switch
    {
        BlockRarity.Common   => 1.0f,
        BlockRarity.Uncommon => 1.4f,
        BlockRarity.Rare     => 2.0f,
        _                    => 1.0f
    };

    // Lift enables vertical routing longer paths stronger blocks pricier.
    // Shadow is slightly cheaper since it adds less path utility.
    static float TypeMult(BlockType t) => t switch
    {
        BlockType.Lift   => 1.4f,
        BlockType.Shadow => 0.85f,
        _                => 1.0f
    };

    /// <summary>
    /// Computes the final shop price for <paramref name="data"/>.
    /// <paramref name="fluctuation"/> is a one-time random [0.82, 1.22] rolled
    /// at shop spawn time (Slay-the-Spire style) pass the value stored on
    /// SelectableBlock.cachedPrice rather than calling this on every frame.
    /// </summary>
    public int ComputePrice(BlockData data, float fluctuation)
    {
        if (data == null) return 0;
        int round = GameFlowManager.Instance?.RoundIndex ?? 0;

        // Route through the balance asset's identical formula when wired.
        if (balance != null) return balance.ComputePrice(data, round, fluctuation);

        int   cells     = (data.cells != null && data.cells.Length > 0) ? data.cells.Length : 1;
        float roundMult = 1f + round * roundPriceScale;
        float raw       = cells * cellBasePrice
                          * RarityMult(data.rarity)
                          * TypeMult(data.blockType)
                          * roundMult
                          * fluctuation;
        return Mathf.Max(1, Mathf.RoundToInt(raw));
    }

    /// <summary>Returns true if the player can afford <paramref name="price"/>.</summary>
    public bool CanAfford(int price) => _blockCurrency >= price;

    /// <summary>Type-aware affordability: turrets check turret pool, others check block pool.</summary>
    public bool CanAfford(int price, BlockType type) =>
        TurretTypes.Is(type) ? _turretCurrency >= price : _blockCurrency >= price;

    /// <summary>Adds <paramref name="amount"/> back to block currency (used by undo).</summary>
    public void RefundBlock(int amount)
    {
        if (amount <= 0) return;
        _blockCurrency += amount;
        OnBlockCurrencyChanged?.Invoke(_blockCurrency);
    }

    /// <summary>Number of blocks of this type currently on the grid (used by DebugUI / shop).</summary>
    public int PlacedCount(BlockType type) =>
        _placedCounts.TryGetValue(type, out int c) ? c : 0;

    /// <summary>
    /// Deducts <paramref name="price"/> from the pool that matches <paramref name="type"/>:
    /// turret currency for Turret blocks, block currency for everything else.
    /// Returns false (and fires OnInsufficientFunds) if insufficient.
    /// Only call for NEW purchases repositioning is always free.
    /// </summary>
    public bool TryBuy(int price, BlockType type)
    {
        bool isTurret = TurretTypes.Is(type);
        int  pool     = isTurret ? _turretCurrency : _blockCurrency;
        if (pool < price)
        {
            OnInsufficientFunds?.Invoke(type);
            Debug.Log($"[Resource] Can't afford {type} (need {price}, have {pool})");
            return false;
        }
        if (isTurret)
        {
            _turretCurrency -= price;
            OnTurretCurrencyChanged?.Invoke(_turretCurrency);
        }
        else
        {
            _blockCurrency -= price;
            OnBlockCurrencyChanged?.Invoke(_blockCurrency);
        }
        Debug.Log($"[Resource] Bought {type} for {price} ¤ {(isTurret ? _turretCurrency : _blockCurrency)} remaining");
        return true;
    }

    // ── Placed-count tracking (for price scaling) ─────────────────────────────
    /// <summary>
    /// Call when ANY block is successfully placed on the grid (new purchase or reposition).
    /// Increments the count used for price scaling.
    /// </summary>
    public void OnBlockPlaced(BlockType type)
    {
        if (!_placedCounts.ContainsKey(type)) _placedCounts[type] = 0;
        _placedCounts[type]++;
    }

    /// <summary>
    /// Call when a block is removed from the grid (picked up for reposition or destroyed).
    /// Decrements the count temporarily lowers the price for that type.
    /// </summary>
    public void OnBlockRemoved(BlockType type)
    {
        if (_placedCounts.ContainsKey(type))
            _placedCounts[type] = Mathf.Max(0, _placedCounts[type] - 1);
    }

    // ── Turret currency ───────────────────────────────────────────────────────
    /// <summary>Attempt to spend turret currency. Returns false if insufficient.</summary>
    public bool TrySpendTurret(int amount)
    {
        if (_turretCurrency < amount) return false;
        _turretCurrency -= amount;
        OnTurretCurrencyChanged?.Invoke(_turretCurrency);
        return true;
    }

    public void AddTurretCurrency(int amount)
    {
        _turretCurrency += amount;
        OnTurretCurrencyChanged?.Invoke(_turretCurrency);
    }

    /// <summary>Adds <paramref name="amount"/> to block currency. Used by passive
    /// income effects (Abundance synergy, upgrade cards, etc.).</summary>
    public void AddBlockCurrency(int amount)
    {
        if (amount == 0) return;
        _blockCurrency += amount;
        OnBlockCurrencyChanged?.Invoke(_blockCurrency);
    }

    /// <summary>Subtracts <paramref name="amount"/> from block currency, clamped at 0.
    /// Unlike TryBuy this is a PENALTY, not a purchase — it always succeeds (no
    /// insufficient-funds gate, no OnInsufficientFunds). Used by level hazards
    /// (e.g. Chaos Block) that tax the player rather than sell them something.</summary>
    public void DrainBlockCurrency(int amount)
    {
        if (amount <= 0) return;
        _blockCurrency = Mathf.Max(0, _blockCurrency - amount);
        OnBlockCurrencyChanged?.Invoke(_blockCurrency);
    }

    // ── Battle-system API (other team calls these) ────────────────────────────
    /// <summary>Call each time an enemy walks over a block. Earns turret currency per BalanceTable.turretPerPassedBlock (default 1).</summary>
    public void OnEnemyPassedBlock(BlockType blockType)
    {
        int amt = balance != null ? balance.turretPerPassedBlock : 1;
        if (amt > 0) AddTurretCurrency(amt);
    }

    /// <summary>Call at wave end. Grants bonus turret currency + round income for next build.</summary>
    public void OnWaveComplete()
    {
        int bonus = balance != null ? balance.turretBonusPerWaveComplete : 3;
        if (bonus > 0) AddTurretCurrency(bonus);
        GrantRoundIncome();
    }

    // OnGUI removed use DebugUI.cs for all display.
}
