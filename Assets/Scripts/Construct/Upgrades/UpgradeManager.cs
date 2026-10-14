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

    [Tooltip("OFF: GameFlowManager's end-of-wave offer does nothing and something else (the roguelite director) decides when and what to offer.")]
    public bool autoOffer = true;

    // Extra gate on what can be offered (unlocks, run rules). Null = everything.
    public Func<UpgradeCard, bool> eligibility;

    // Run-scoped ownership state.
    readonly List<UpgradeCard> _owned = new();
    readonly List<ItemEffectInstance> _instances = new();
    readonly Dictionary<UpgradeCard, int> _ownedCounts = new();

    public IReadOnlyList<UpgradeCard> Owned => _owned;
    public int OwnedCount(UpgradeCard c)
        => (c != null && _ownedCounts.TryGetValue(c, out var n)) ? n : 0;

    public event Action<UpgradeCard> OnCardPicked;

    // The live effect instances, in pick order — the relic bar reads Status / CanUse.
    public IReadOnlyList<ItemEffectInstance> Instances => _instances;

    public ItemEffectInstance InstanceOf(UpgradeCard card)
    {
        for (int i = 0; i < _instances.Count; i++)
            if (_instances[i] != null && _instances[i].Card == card) return _instances[i];
        return null;
    }

    // Hand a card over directly — a reward, a purchase.
    public void Grant(UpgradeCard card) => OnCardChosen(card);

    // Take a card back out and undo it (a consumed item, a swap).
    public bool Remove(UpgradeCard card)
    {
        if (card == null || !_owned.Contains(card)) return false;
        var inst = InstanceOf(card);
        if (inst != null) { inst.Release(GameFlowManager.Instance); _instances.Remove(inst); }
        _owned.Remove(card);
        if (_ownedCounts.TryGetValue(card, out int n)) { if (n <= 1) _ownedCounts.Remove(card); else _ownedCounts[card] = n - 1; }
        RogueRun.RaiseRelicsChanged();
        return true;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        for (int i = 0; i < _instances.Count; i++) _instances[i]?.Tick(dt);
    }

    void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    public bool HasCardsToOffer => GetEligibleCards().Count > 0;

    // ── Mid-level save ────────────────────────────────────────────────────────
    public List<string> OwnedNames()
    {
        var names = new List<string>();
        foreach (var c in _owned) if (c != null) names.Add(c.name);
        return names;
    }

    // Re-acquires each card (effects included), resolved against the card pool.
    public void RestoreOwned(List<string> names)
    {
        if (names == null) return;
        foreach (var n in names)
        {
            var card = cardPool.Find(c => c != null && c.name == n);
            if (card != null) OnCardChosen(card);
        }
    }

    // Top-level entry point. Called by GameFlowManager once a wave is done.
    public void OfferEndOfWave(Xoshiro256StarStar rng)
    {
        if (!autoOffer) return;
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
        RogueRun.RaiseRelicsChanged();
    }

    // Weighted sampling without replacement.
    public List<UpgradeCard> RollOffer(Xoshiro256StarStar rng) => RollOffer(rng, offerCount, null);

    // `weightOf`: replaces a card's own weight (the roguelite weights by rarity per
    // source). Cards it answers 0 for are skipped.
    public List<UpgradeCard> RollOffer(Xoshiro256StarStar rng, int count, Func<UpgradeCard, float> weightOf,
                                       ICollection<UpgradeCard> exclude = null)
    {
        var eligible = GetEligibleCards();
        if (exclude != null) eligible.RemoveAll(c => exclude.Contains(c));
        var offer = new List<UpgradeCard>();

        var weights = new List<float>(eligible.Count);
        for (int i = 0; i < eligible.Count; i++)
            weights.Add(weightOf != null ? Mathf.Max(0f, weightOf(eligible[i])) : eligible[i].weight);
        for (int i = eligible.Count - 1; i >= 0; i--)
            if (weights[i] <= 0f) { eligible.RemoveAt(i); weights.RemoveAt(i); }

        int n = Mathf.Min(count, eligible.Count);

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
            if (eligibility != null && !eligibility(c)) continue;

            list.Add(c);
        }
        return list;
    }

    public void ResetForNewRun()
    {
        ReleaseAll();
        _owned.Clear();
        _ownedCounts.Clear();
        RogueRun.RaiseRelicsChanged();
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
