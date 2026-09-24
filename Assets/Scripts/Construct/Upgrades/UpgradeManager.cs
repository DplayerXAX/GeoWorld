using System;
using System.Collections.Generic;
using UnityEngine;

// Owns the upgrade card pool and the player's owned-card list for the current
// run. Drives the "pick 1 of N" choice that fires at the end of every wave.
//
// Pool / offer pipeline:
//   GameFlowManager.EndRunningPhase
//     → UpgradeManager.OfferEndOfWave(rng)
//         → RollOffer(rng)            ← weighted sample without replacement
//         → UpgradeChoiceUI.Show(offer, OnPicked)
//             → user clicks card
//             → OnPicked(card)
//                 → card.effect.Apply(game)
//                 → add to _owned
//
// Persistence: cards picked stay applied for the whole run. Each pick creates
// its own ItemEffectInstance, and every instance is RELEASED — on
// ResetForNewRun, and again on OnDestroy for a scene that unloads mid-run — so
// no stat change or event subscription outlives the run that earned it.
public class UpgradeManager : MonoBehaviour
{
    public static UpgradeManager Instance;

    [Header("Pool")]
    [Tooltip("All cards in the game. Cards rolled at wave end are sampled from here, filtered by ownership / maxCopies.")]
    public List<UpgradeCard> cardPool = new();

    [Tooltip("How many cards to offer at wave end.")]
    [Min(1)] public int offerCount = 3;

    [Tooltip("If true and there are zero eligible cards, OfferEndOfWave silently skips. If false, logs a warning.")]
    public bool silentWhenEmpty = true;

    // Run-scoped ownership state.
    readonly List<UpgradeCard> _owned = new();
    readonly List<ItemEffectInstance> _instances = new();
    readonly Dictionary<UpgradeCard, int> _ownedCounts = new();

    public IReadOnlyList<UpgradeCard> Owned => _owned;
    public int OwnedCount(UpgradeCard c)
        => (c != null && _ownedCounts.TryGetValue(c, out var n)) ? n : 0;

    public event Action<UpgradeCard> OnCardPicked;

    void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    public bool HasCardsToOffer => GetEligibleCards().Count > 0;

    // Top-level entry point. Called by GameFlowManager once a wave is done.
    public void OfferEndOfWave(Xoshiro256StarStar rng)
    {
        if (rng == null)
        {
            Debug.LogWarning("[UpgradeManager] OfferEndOfWave called with null RNG.");
            return;
        }

        var offer = RollOffer(rng);
        if (offer.Count == 0)
        {
            if (!silentWhenEmpty)
                Debug.LogWarning("[UpgradeManager] No eligible cards to offer this wave.");
            return;
        }

        if (UpgradeChoiceUI.Instance != null)
        {
            UpgradeChoiceUI.Instance.Show(offer, OnCardChosen);
        }
        else
        {
            // No UI in scene yet — auto-apply the first card so the run loop
            // keeps moving during prototyping. Replace once UI is built.
            Debug.LogWarning("[UpgradeManager] No UpgradeChoiceUI in scene — auto-applying first offered card.");
            OnCardChosen(offer[0]);
        }
    }

    void OnCardChosen(UpgradeCard card)
    {
        if (card == null) return;

        _owned.Add(card);
        _ownedCounts.TryGetValue(card, out int prev);
        _ownedCounts[card] = prev + 1;

        try
        {
            var inst = card.effect != null ? card.effect.CreateInstance() : null;
            if (inst != null)
            {
                inst.Card = card;
                _instances.Add(inst);
                inst.Acquire(GameFlowManager.Instance);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[UpgradeManager] item acquire threw on '{card.displayName}': {e}");
        }

        OnCardPicked?.Invoke(card);
    }

    // Weighted sampling without replacement.
    public List<UpgradeCard> RollOffer(Xoshiro256StarStar rng)
    {
        var eligible = GetEligibleCards();
        var offer = new List<UpgradeCard>();
        int n = Mathf.Min(offerCount, eligible.Count);

        var weights = new List<float>(eligible.Count);
        for (int i = 0; i < eligible.Count; i++) weights.Add(eligible[i].weight);

        for (int i = 0; i < n; i++)
        {
            int idx = rng.PickWeighted(weights);
            if (idx < 0) break;
            offer.Add(eligible[idx]);
            eligible.RemoveAt(idx);
            weights.RemoveAt(idx);
        }
        return offer;
    }

    List<UpgradeCard> GetEligibleCards()
    {
        var list = new List<UpgradeCard>();
        if (cardPool == null) return list;

        for (int i = 0; i < cardPool.Count; i++)
        {
            var c = cardPool[i];
            if (c == null || c.weight <= 0f) continue;

            int owned = 0;
            _ownedCounts.TryGetValue(c, out owned);
            if (c.maxCopies > 0 && owned >= c.maxCopies) continue;

            list.Add(c);
        }
        return list;
    }

    public void ResetForNewRun()
    {
        ReleaseAll();
        _owned.Clear();
        _ownedCounts.Clear();
    }

    // A scene unloading mid-run never calls ResetForNewRun, and an item subscribed
    // to a static event would otherwise keep firing into a board that no longer
    // exists.
    void OnDestroy()
    {
        ReleaseAll();
        if (Instance == this) Instance = null;
    }

    // Newest first, so an item that built on an earlier one is undone before it.
    void ReleaseAll()
    {
        var game = GameFlowManager.Instance;
        for (int i = _instances.Count - 1; i >= 0; i--) _instances[i]?.Release(game);
        _instances.Clear();
    }
}
