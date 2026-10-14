using System.Collections.Generic;
using UnityEngine;

// The relic merchant: a few relics for sale between waves, paid in BLOCK currency.
//
// Block currency is what the player also builds with, so every relic bought is a
// block not bought — that is the whole decision. Stock is rolled from the run seed
// (and the wave, and how many times it has been re-rolled), so it is reproducible and
// cannot be re-rolled for free by leaving and coming back.
public static class RogueMarket
{
    public sealed class Offer
    {
        public UpgradeCard card;
        public int  price;
        public bool sold;
    }

    public static readonly List<Offer> Offers = new();
    public static int Version { get; private set; }

    static int _wave, _rerolls;
    static ulong _seed;

    public static int RerollCost
    {
        get { var c = RogueRun.Config; return c.rerollBase + c.rerollStep * _rerolls; }
    }

    public static int PriceOf(UpgradeCard card, int wave)
    {
        var cfg = RogueRun.Config;
        int basePrice = card.basePrice > 0 ? card.basePrice
                      : card.rarity == Rarity.Rare ? cfg.priceRare
                      : card.rarity == Rarity.Uncommon ? cfg.priceUncommon
                      : cfg.priceCommon;
        return Mathf.Max(1, Mathf.RoundToInt(basePrice * (1f + cfg.priceWaveScale * Mathf.Max(0, wave))));
    }

    public static float RarityWeight(Vector3 w, UpgradeCard c) =>
        c.rarity == Rarity.Rare ? w.z : c.rarity == Rarity.Uncommon ? w.y : w.x;

    // A new shelf for the wave about to be played.
    public static void Restock(int wave, ulong seed)
    {
        _wave = wave; _rerolls = 0; _seed = seed;
        Fill();
    }

    static void Fill()
    {
        Offers.Clear();
        var um = UpgradeManager.Instance;
        if (um != null)
        {
            var cfg = RogueRun.Config;
            var rng = new Xoshiro256StarStar(_seed ^ (ulong)(_wave * 7919 + _rerolls * 104729 + 17));
            var w = cfg.WeightsFor(RogueSource.Market);
            var picks = um.RollOffer(rng, Mathf.Max(1, cfg.marketSlots), c => RarityWeight(w, c));
            foreach (var c in picks) Offers.Add(new Offer { card = c, price = PriceOf(c, _wave) });
        }
        Version++;
        RogueRun.RaiseMarketChanged();
    }

    public static bool CanAfford(Offer o)
    {
        var rm = ResourceManager.Instance;
        return o != null && !o.sold && rm != null && rm.CanAfford(o.price, BlockType.Home);
    }

    public static bool Buy(Offer o)
    {
        var rm = ResourceManager.Instance;
        var um = UpgradeManager.Instance;
        if (o == null || o.sold || rm == null || um == null) return false;
        if (!rm.TryBuy(o.price, BlockType.Home)) return false;

        o.sold = true;
        um.Grant(o.card);
        RogueRun.RelicsBought++;
        ResourceManager.NotifyPurchased(o.price, BlockType.Home);   // Recurrence pays back on relics too
        Version++;
        RogueRun.RaiseMarketChanged();
        return true;
    }

    public static bool Reroll()
    {
        var rm = ResourceManager.Instance;
        if (rm == null) return false;
        if (!rm.TryBuy(RerollCost, BlockType.Home)) return false;
        _rerolls++;
        RogueRun.Rerolls++;
        Fill();
        return true;
    }
}
