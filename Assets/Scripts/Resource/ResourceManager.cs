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
    //
    // Unqualified BlockCurrency/TurretCurrency mean THIS machine's player. Every
    // existing caller — the shop, the HUD, upgrades, undo, the debug panel — is asking
    // about the person sitting in front of it, so keeping these pointing at the local
    // wallet is what let per-player money land without touching any of them.
    public int BlockCurrency  => BlockCurrencyOf(MultiplayerSession.LocalId);
    public int TurretCurrency => TurretCurrencyOf(MultiplayerSession.LocalId);

    public int BlockCurrencyOf(int playerId)  => Valid(playerId) ? _block[playerId]  : 0;
    public int TurretCurrencyOf(int playerId) => Valid(playerId) ? _turret[playerId] : 0;

    static bool Valid(int id) => id >= 0 && id < MultiplayerSession.MaxPlayers;

    // ── State ─────────────────────────────────────────────────────────────────
    //
    // A wallet per slot, not a shared pool. Money is the one thing that should NOT be
    // shared on a co-op board: with one pot the fastest clicker spends everyone's
    // round, and nobody can plan a purchase because the balance moves under them.
    readonly int[] _block  = new int[MultiplayerSession.MaxPlayers];
    readonly int[] _turret = new int[MultiplayerSession.MaxPlayers];

    // How many ways income is split. LATCHED at run start rather than read live: a
    // player dropping mid-match would otherwise raise everyone else's income the
    // moment each machine noticed, and they notice at different times — which is a
    // desync in the one number the whole economy is built on.
    int _walletCount = 1;

    /// <summary>Players the income is currently being divided between.</summary>
    public int WalletCount => _walletCount;

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

        InitWallets();

        foreach (BlockType t in Enum.GetValues(typeof(BlockType)))
            _placedCounts[t] = 0;
    }

    /// <summary>
    /// Hand out starting money, split by however many are playing.
    /// Called again from GameFlowManager.Start once the session roster is settled —
    /// Awake runs before that, so the count it sees there is not yet trustworthy.
    /// </summary>
    public void InitWallets()
    {
        _walletCount = Mathf.Max(1, MultiplayerSession.ConnectedCount);

        int startBlock  = Split(startingBlockCurrency);
        int startTurret = Split(startingTurretCurrency);

        for (int i = 0; i < MultiplayerSession.MaxPlayers; i++)
        {
            _block[i]  = testing ? 9999 : startBlock;
            _turret[i] = testing ? 9999 : startTurret;
        }
        _turretRegenAccum = 0f;
        RaiseLocal();
    }

    /// <summary>
    /// One player's share of a table-wide amount. Four players each get a quarter, so
    /// the money entering the game is the same as in single-player — otherwise every
    /// extra player would be a straight difficulty reduction.
    ///
    /// Floored at 1 for any positive input: a income of 2 split four ways is 0.5, and
    /// an income that rounds to nothing is a player who can never buy anything. The
    /// table then earns slightly more than solo at high counts, which is the right way
    /// to be wrong.
    /// </summary>
    int Split(int total)
    {
        if (total <= 0) return 0;
        return Mathf.Max(1, Mathf.RoundToInt(total / (float)_walletCount));
    }

    // Events are the LOCAL player's, because every subscriber is local UI. Firing on
    // someone else's transaction would flash a currency popup for money that never
    // moved on this screen.
    void RaiseLocal()
    {
        OnBlockCurrencyChanged?.Invoke(BlockCurrency);
        OnTurretCurrencyChanged?.Invoke(TurretCurrency);
    }

    void RaiseIfLocal(int playerId, bool turret)
    {
        if (playerId != MultiplayerSession.LocalId) return;
        if (turret) OnTurretCurrencyChanged?.Invoke(TurretCurrency);
        else        OnBlockCurrencyChanged?.Invoke(BlockCurrency);
    }

    void Update()
    {
        if (!_combatActive) return;

        _turretRegenAccum += turretRegenPerSecond * Time.deltaTime;
        if (_turretRegenAccum >= 1f)
        {
            int gain = Mathf.FloorToInt(_turretRegenAccum);
            _turretRegenAccum -= gain;
            // Regen is table-wide: everyone earns from the same battle, and giving each
            // player the full rate would multiply combat income by the player count.
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

        int blockShare  = Split(blockInc);
        int turretShare = Split(turretInc);

        for (int i = 0; i < MultiplayerSession.MaxPlayers; i++)
        {
            _block[i]  += blockShare;
            _turret[i] += turretShare;
        }
        RaiseLocal();
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
    // Fallback only — used when no `balance` asset is wired. Must mirror
    // BalanceTable.GetTypeMult or the two paths silently price differently.
    static float TypeMult(BlockType t) => t switch
    {
        BlockType.Lift       => 1.4f,
        BlockType.Shadow     => 0.85f,
        BlockType.Turret     => 0.7f,
        BlockType.SlowTurret => 0.9f,
        BlockType.AoeTurret  => 1.2f,
        _                    => 1.0f
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

        if (TurretTypes.Is(data.blockType)) fluctuation = 1f;   // flat, same as BalanceTable

        int   cells     = (data.cells != null && data.cells.Length > 0) ? data.cells.Length : 1;
        float roundMult = 1f + round * roundPriceScale;
        float raw       = cells * cellBasePrice
                          * RarityMult(data.rarity)
                          * TypeMult(data.blockType)
                          * roundMult
                          * fluctuation;
        return Mathf.Max(1, Mathf.RoundToInt(raw));
    }

    /// <summary>
    /// Sandbox mode: every price is affordable and nothing is ever deducted.
    /// Driven by LevelDefinition.infiniteResources, so it's a property OF THE LEVEL
    /// rather than a global cheat toggle — a test level can't leak it into a real
    /// run, and there's no state to remember to switch back off.
    /// </summary>
    public static bool InfiniteResources =>
        RunConfig.Mode == GameMode.Level && RunConfig.Level != null && RunConfig.Level.infiniteResources;

    /// <summary>Returns true if the player can afford <paramref name="price"/>.</summary>
    public bool CanAfford(int price) => CanAfford(MultiplayerSession.LocalId, price, BlockType.Home);

    /// <summary>Type-aware affordability: turrets check turret pool, others check block pool.</summary>
    public bool CanAfford(int price, BlockType type) => CanAfford(MultiplayerSession.LocalId, price, type);

    public bool CanAfford(int playerId, int price, BlockType type)
    {
        if (InfiniteResources) return true;
        if (!Valid(playerId)) return false;
        return TurretTypes.Is(type) ? _turret[playerId] >= price : _block[playerId] >= price;
    }

    /// <summary>Adds <paramref name="amount"/> back to block currency (used by undo).</summary>
    public void RefundBlock(int amount) => RefundBlock(MultiplayerSession.LocalId, amount);

    public void RefundBlock(int playerId, int amount)
    {
        if (amount <= 0 || !Valid(playerId)) return;
        _block[playerId] += amount;
        RaiseIfLocal(playerId, turret: false);
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
    public bool TryBuy(int price, BlockType type) => TryBuy(MultiplayerSession.LocalId, price, type);

    /// <summary>
    /// Charges a SPECIFIC player. The networked placement path uses this with the
    /// command's issuer, because the machine applying a purchase is usually not the
    /// one that made it — charging locally there would bill all four players for one
    /// person's block.
    /// </summary>
    public bool TryBuy(int playerId, int price, BlockType type)
    {
        if (InfiniteResources) return true;   // sandbox — buy anything, deduct nothing
        if (!Valid(playerId)) return false;

        bool isTurret = TurretTypes.Is(type);
        var  wallet   = isTurret ? _turret : _block;
        if (wallet[playerId] < price)
        {
            // Only surfaced to the person who could not pay.
            if (playerId == MultiplayerSession.LocalId) OnInsufficientFunds?.Invoke(type);
            Debug.Log($"[Resource] p{playerId} can't afford {type} (need {price}, have {wallet[playerId]})");
            return false;
        }

        wallet[playerId] -= price;
        RaiseIfLocal(playerId, isTurret);
        Debug.Log($"[Resource] p{playerId} bought {type} for {price} ¤ {wallet[playerId]} remaining");
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
        if (InfiniteResources) return true;
        int me = MultiplayerSession.LocalId;
        if (!Valid(me) || _turret[me] < amount) return false;
        _turret[me] -= amount;
        RaiseIfLocal(me, turret: true);
        return true;
    }

    /// <summary>
    /// Table-wide turret income — kills, passed blocks, wave bonuses, regen. Split,
    /// like round income: these all come from the ONE board everybody is defending, so
    /// paying each player in full would scale the game's income with its player count.
    /// </summary>
    public void AddTurretCurrency(int amount)
    {
        int share = Split(amount);
        if (share == 0) return;
        for (int i = 0; i < MultiplayerSession.MaxPlayers; i++) _turret[i] += share;
        RaiseLocal();
    }

    /// <summary>Pays exactly one player — for anything genuinely theirs alone.</summary>
    public void AddTurretCurrencyTo(int playerId, int amount)
    {
        if (amount == 0 || !Valid(playerId)) return;
        _turret[playerId] += amount;
        RaiseIfLocal(playerId, turret: true);
    }

    /// <summary>Adds <paramref name="amount"/> to block currency. Used by passive
    /// income effects (Abundance synergy, upgrade cards, etc.).</summary>
    public void AddBlockCurrency(int amount)
    {
        int share = Split(amount);
        if (share == 0) return;
        for (int i = 0; i < MultiplayerSession.MaxPlayers; i++) _block[i] += share;
        RaiseLocal();
    }

    /// <summary>Pays exactly one player — for anything genuinely theirs alone.</summary>
    public void AddBlockCurrencyTo(int playerId, int amount)
    {
        if (amount == 0 || !Valid(playerId)) return;
        _block[playerId] += amount;
        RaiseIfLocal(playerId, turret: false);
    }

    /// <summary>Subtracts <paramref name="amount"/> from block currency, clamped at 0.
    /// Unlike TryBuy this is a PENALTY, not a purchase — it always succeeds (no
    /// insufficient-funds gate, no OnInsufficientFunds). Used by level hazards
    /// (e.g. Chaos Block) that tax the player rather than sell them something.</summary>
    // A hazard taxes the TABLE, and each player pays a share. Taking the full amount
    // from everyone would make every level hazard scale with the player count, which
    // is the opposite of what adding a friend should do.
    public void DrainBlockCurrency(int amount)
    {
        int share = Split(amount);
        if (share <= 0) return;
        for (int i = 0; i < MultiplayerSession.MaxPlayers; i++)
            _block[i] = Mathf.Max(0, _block[i] - share);
        RaiseLocal();
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
