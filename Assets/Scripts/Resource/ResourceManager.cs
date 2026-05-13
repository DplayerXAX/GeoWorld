using System;
using System.Collections.Generic;
using UnityEngine;

// ── Resource economy ──────────────────────────────────────────────────────────
//
// Four resource types, one per BlockType.  The musical chord palette and the
// economic palette are the same object — running low on Lift resources means
// your path loses that harmonic range too.
//
// SPENDING
//   PlacementController deducts resources when a NEW block is taken from the
//   tray and placed.  Repositioning an already-placed block is always free.
//
// EARNING
//   Battle system calls OnEnemyPassedBlock(blockType) each time an enemy
//   walks over a block — 1 resource of that type per pass.
//   Battle system calls OnWaveComplete(waveIndex) at wave end for a diversity
//   bonus based on path variety.
//
// UI
//   Subscribe to OnResourceChanged(BlockType, newAmount) to update displays.
//   Subscribe to OnInsufficientFunds(BlockType) for "can't afford" feedback.
// ─────────────────────────────────────────────────────────────────────────────
public class ResourceManager : MonoBehaviour
{
    public static ResourceManager Instance;

    [Header("Starting Resources")]
    public int startHome   = 8;
    public int startLift   = 5;
    public int startPull   = 6;
    public int startShadow = 12;   // Shadow is cheap — give more to start

    [Header("Placement Costs")]
    public int costHome   = 2;
    public int costLift   = 4;   // expensive: enables vertical routing → longer paths
    public int costPull   = 3;
    public int costShadow = 1;   // cheap: enemies move faster through Shadow

    [Header("Wave Completion Bonus")]
    [Tooltip("Base resources given per block type at each wave end.")]
    public int waveBaseBonus = 2;
    [Tooltip("Extra per unique block type present in the active path (diversity bonus).")]
    public int waveDiversityBonus = 3;

    // ── Events ────────────────────────────────────────────────────────────────
    /// <summary>Fires whenever a resource amount changes. Subscribe for UI updates.</summary>
    public event Action<BlockType, int> OnResourceChanged;

    /// <summary>Fires when a placement attempt fails due to insufficient funds.</summary>
    public event Action<BlockType> OnInsufficientFunds;

    // ── State ─────────────────────────────────────────────────────────────────
    readonly Dictionary<BlockType, int> _res = new();

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Awake()
    {
        Instance = this;
        _res[BlockType.Home]   = startHome;
        _res[BlockType.Lift]   = startLift;
        _res[BlockType.Pull]   = startPull;
        _res[BlockType.Shadow] = startShadow;
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    /// <summary>Current amount of the given resource type.</summary>
    public int Get(BlockType type) =>
        _res.TryGetValue(type, out int v) ? v : 0;

    /// <summary>Placement cost for a block of the given type.</summary>
    public int PlacementCost(BlockType type) => type switch
    {
        BlockType.Home   => costHome,
        BlockType.Lift   => costLift,
        BlockType.Pull   => costPull,
        BlockType.Shadow => costShadow,
        _                => 1,
    };

    /// <summary>Returns true if the player can afford to place this block type.</summary>
    public bool CanAfford(BlockType type) =>
        Get(type) >= PlacementCost(type);

    // ── Spending ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to deduct the placement cost for this block type.
    /// Returns true on success; fires OnInsufficientFunds and returns false if
    /// the player cannot afford it.
    /// </summary>
    public bool TrySpend(BlockType type)
    {
        int cost = PlacementCost(type);
        if (Get(type) < cost)
        {
            OnInsufficientFunds?.Invoke(type);
            Debug.Log($"[Resource] Can't afford {type} (need {cost}, have {Get(type)})");
            return false;
        }
        _res[type] -= cost;
        OnResourceChanged?.Invoke(type, _res[type]);
        Debug.Log($"[Resource] Spent {cost} {type} → {_res[type]} remaining");
        return true;
    }

    // ── Earning ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Add resources of a specific type.
    /// Call this directly for flat bonuses or scripted rewards.
    /// </summary>
    public void Earn(BlockType type, int amount)
    {
        if (amount <= 0) return;
        if (!_res.ContainsKey(type)) _res[type] = 0;
        _res[type] += amount;
        OnResourceChanged?.Invoke(type, _res[type]);
    }

    // ── Battle-system API (called by whoever implements enemies / waves) ───────

    /// <summary>
    /// Call this each time an enemy walks over a block of a given type.
    /// Earns 1 resource of that type.
    /// </summary>
    public void OnEnemyPassedBlock(BlockType blockType)
        => Earn(blockType, 1);

    /// <summary>
    /// Call this at the end of each wave with the set of block types present
    /// on the current path.  Gives a base bonus + diversity bonus.
    /// </summary>
    public void OnWaveComplete(IEnumerable<BlockType> pathBlockTypes)
    {
        var types = new HashSet<BlockType>(pathBlockTypes);

        // Base bonus for every resource type
        foreach (BlockType t in System.Enum.GetValues(typeof(BlockType)))
        {
            if (t == BlockType.Turret) continue;   // Turret isn't a resource type
            Earn(t, waveBaseBonus);
        }

        // Diversity bonus — reward path variety (ties to musical variety)
        if (types.Count >= 3)
        {
            Debug.Log($"[Resource] Diversity bonus! {types.Count} block types on path.");
            foreach (var t in types)
                Earn(t, waveDiversityBonus);
        }
    }

    // ── Debug ─────────────────────────────────────────────────────────────────
    void OnGUI()
    {
        // Temporary display until proper UI is added.
        // Remove or disable this when your UI is hooked up.
        int y = 10;
        GUI.Label(new Rect(10, y, 220, 20), "── Resources ──"); y += 20;
        foreach (var kv in _res)
        {
            string afford = kv.Value >= PlacementCost(kv.Key) ? "" : " ✗";
            GUI.Label(new Rect(10, y, 220, 20),
                $"{kv.Key,8}: {kv.Value,3}  (cost {PlacementCost(kv.Key)}){afford}");
            y += 18;
        }
    }
}
