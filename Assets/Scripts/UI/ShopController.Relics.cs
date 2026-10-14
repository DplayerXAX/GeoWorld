using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The roguelite's relic merchant, in the shop strip itself.
//
// The relics for sale (RogueMarket) sit in a third group at the LEFT end of the strip —
//
//        [ relics + restock ]  |  [ blocks ]  |  [ turrets ]
//
// — as slots cloned from the block template, so they look, hover and open and close
// with the rest of the shop. They are paid for in BLOCK currency (the same purse the
// blocks come out of, so every relic bought is a block not bought) and cost a good deal
// more than a block. The last tile restocks the shelf for a price that climbs each
// time. Prices come from RogueConfig; the stock itself from RogueMarket, which the
// relic tab in the wave panel reads too — they are two windows on one shelf.
//
// Nothing here exists outside the roguelite: the group is only built when the run's
// mode is Rogue.
public partial class ShopController
{
    class RelicSlot
    {
        public RogueMarket.Offer offer;   // null = the restock tile
        public ShopItemView      view;
    }

    readonly List<RelicSlot> _relics = new();
    RelicSlot     _hoveredRelic;
    RectTransform _relicRow;
    GameObject    _relicDivider;
    bool          _relicsHooked;
    bool          _relicsDirty = true;

    static bool RelicShop => RunConfig.Mode == GameMode.Rogue;

    void OnDestroy()
    {
        if (!_relicsHooked) return;
        RogueRun.MarketChanged -= MarkRelicsDirty;
        RogueRun.RelicsChanged -= MarkRelicsDirty;
    }

    void MarkRelicsDirty() => _relicsDirty = true;

    // ── Building ──────────────────────────────────────────────────────────────

    bool EnsureRelicRow()
    {
        if (_relicRow != null) return true;
        if (view == null || view.blockRow == null) return false;

        // The container the block and turret rows sit in — the strip's own layout.
        var container = view.blockRow.parent as RectTransform;
        if (container == null) container = view.panel;
        if (container == null) return false;

        var go = new GameObject("RelicRow", typeof(RectTransform));
        _relicRow = (RectTransform)go.transform;
        _relicRow.SetParent(container, false);

        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 8f;
        hlg.childAlignment = TextAnchor.LowerCenter;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;

        // The same hairline the block and turret rows are split by.
        Transform divider = null;
        for (int i = 0; i < container.childCount; i++)
            if (container.GetChild(i).name == "Divider") { divider = container.GetChild(i); break; }
        if (divider != null)
        {
            _relicDivider = Instantiate(divider.gameObject, container);
            _relicDivider.name = "RelicDivider";
        }

        _relicRow.SetAsFirstSibling();
        if (_relicDivider != null) _relicDivider.transform.SetSiblingIndex(1);
        return true;
    }

    void RebuildRelicSlots()
    {
        _relicsDirty = false;
        foreach (var s in _relics)
            if (s.view != null) Destroy(s.view.gameObject);
        _relics.Clear();
        _hoveredRelic = null;

        if (view == null || view.blockTemplate == null || !EnsureRelicRow()) return;

        foreach (var o in RogueMarket.Offers)
            if (o != null && o.card != null) AddRelicSlot(o);
        AddRelicSlot(null);   // the restock tile
    }

    void AddRelicSlot(RogueMarket.Offer offer)
    {
        var v = Instantiate(view.blockTemplate, _relicRow);
        v.name = offer != null ? "Relic_" + offer.card.displayName : "RelicRestock";
        v.gameObject.SetActive(true);
        if (v.scaleRoot != null)   // start at rest size, as every other slot does
            v.scaleRoot.localScale = Vector3.one / Mathf.Max(1f, Mathf.Max(hoverScale, tutorialHighlightScale));

        if (v.icon != null)
        {
            v.icon.enabled = true;
            if (offer != null)
            {
                // No photograph of a relic — a seal in its category colour, with its initials.
                v.icon.sprite = UIRoundedRect.Get(20);
                v.icon.type = Image.Type.Sliced;
                v.icon.preserveAspect = false;
                v.icon.color = RogueItems.ColorOf(offer.card.category);

                var t = RogueUiGate.NewText("Seal", v.icon.rectTransform, 34f, Color.white, FontStyles.Bold);
                t.text = RelicInitials(offer.card.displayName);
                RogueUiGate.Stretch(t.rectTransform);
            }
            else if (refreshIcon != null)
            {
                v.icon.sprite = refreshIcon;
                v.icon.type = Image.Type.Simple;
                v.icon.preserveAspect = true;
                v.icon.color = Color.white;
            }
            else
            {
                v.icon.sprite = UIRoundedRect.Get(20);
                v.icon.type = Image.Type.Sliced;
                v.icon.preserveAspect = false;
                v.icon.color = new Color(0.30f, 0.34f, 0.42f, 1f);
                var t = RogueUiGate.NewText("Label", v.icon.rectTransform, 20f, Color.white, FontStyles.Bold);
                t.text = "RESTOCK";
                RogueUiGate.Stretch(t.rectTransform);
            }
        }

        _relics.Add(new RelicSlot { offer = offer, view = v });
    }

    static string RelicInitials(string name)
    {
        if (string.IsNullOrEmpty(name)) return "?";
        var parts = name.Split(' ');
        if (parts.Length >= 2 && parts[1].Length > 0) return ("" + char.ToUpper(parts[0][0]) + char.ToUpper(parts[1][0]));
        return name.Length >= 2 ? ("" + char.ToUpper(name[0]) + char.ToLower(name[1])) : name.ToUpper();
    }

    // ── Per frame ─────────────────────────────────────────────────────────────

    void UpdateRelicSlots()
    {
        if (!RelicShop || view == null) return;

        if (!_relicsHooked)
        {
            _relicsHooked = true;
            RogueRun.MarketChanged += MarkRelicsDirty;
            RogueRun.RelicsChanged += MarkRelicsDirty;
        }
        if (_relicsDirty) RebuildRelicSlots();

        var rm = ResourceManager.Instance;
        float lerpK = 1f - Mathf.Exp(-hoverLerpSpeed * Time.unscaledDeltaTime);
        float peak  = Mathf.Max(1f, Mathf.Max(hoverScale, tutorialHighlightScale));

        foreach (var s in _relics)
        {
            var v = s.view;
            if (v == null) continue;

            bool hovered = s == _hoveredRelic;
            bool sold    = s.offer != null && s.offer.sold;
            int  price   = s.offer != null ? s.offer.price : RogueMarket.RerollCost;
            bool afford  = !sold && rm != null && rm.CanAfford(price, BlockType.Home);

            if (v.price != null)
            {
                v.price.text  = sold ? "SOLD" : $"{price}¤B";
                v.price.color = afford ? v.affordableColor : v.unaffordableColor;
            }
            if (v.group != null) v.group.alpha = sold ? 0.35f : 1f;

            if (v.background != null)
            {
                Color tint = s.offer != null ? RogueItems.ColorOf(s.offer.card.rarity) : new Color(0.7f, 0.75f, 0.85f);
                tint.a = hovered && !sold ? 0.55f : 0.28f;
                v.background.color = tint;
            }

            if (v.scaleRoot != null)
            {
                float target = Mathf.Min(hovered && !sold ? hoverScale : 1f, peak) / peak;
                v.scaleRoot.localScale = Vector3.one * Mathf.Lerp(v.scaleRoot.localScale.x, target, lerpK);
            }
        }
    }

    // Called from UpdateHover, after the blocks and turrets found nothing under the cursor.
    void HoverRelics(Vector2 p)
    {
        foreach (var s in _relics)
        {
            if (s.view == null || s.view.rect == null) continue;
            if (RectTransformUtility.RectangleContainsScreenPoint(s.view.rect, p, UICamera()))
            {
                _hoveredRelic = s;
                return;
            }
        }
    }

    // ── Tooltip ───────────────────────────────────────────────────────────────

    // True when it showed the relic's card (and the block tooltip should stand down).
    bool ShowRelicTooltip()
    {
        var s = _hoveredRelic;
        if (s == null || s.view == null || _openT <= 0.5f) return false;

        var rm = ResourceManager.Instance;
        if (!view.tooltip.gameObject.activeSelf) view.tooltip.gameObject.SetActive(true);

        bool afford;
        if (s.offer != null)
        {
            var c = s.offer.card;
            int price = s.offer.price;
            afford = !s.offer.sold && rm != null && rm.CanAfford(price, BlockType.Home);
            string rar = ColorUtility.ToHtmlStringRGB(RogueItems.ColorOf(c.rarity));
            string cat = ColorUtility.ToHtmlStringRGB(RogueItems.ColorOf(c.category));

            if (view.tooltipTitle != null)
            {
                view.tooltipTitle.richText = true;
                view.tooltipTitle.text = $"{c.displayName}  ·  <color=#{rar}>{c.rarity.ToString().ToUpper()}</color>";
            }
            if (view.tooltipPrice != null)
            {
                int pool = rm != null ? rm.BlockCurrency : 0;
                view.tooltipPrice.text = s.offer.sold ? "SOLD"
                                       : afford ? $"{price} B"
                                       : $"{price} B  (-{price - pool})";
                view.tooltipPrice.color = afford ? GeoPalette.Blue : GeoPalette.Signal;
            }
            if (view.tooltipDesc != null)
            {
                view.tooltipDesc.gameObject.SetActive(true);
                view.tooltipDesc.richText = true;
                view.tooltipDesc.text = $"<color=#{cat}>{RogueItems.NameOf(c.category)}</color>\n{c.description}";
            }
        }
        else
        {
            int cost = RogueMarket.RerollCost;
            afford = rm != null && rm.CanAfford(cost, BlockType.Home);
            if (view.tooltipTitle != null) { view.tooltipTitle.richText = true; view.tooltipTitle.text = "Restock relics"; }
            if (view.tooltipPrice != null)
            {
                int pool = rm != null ? rm.BlockCurrency : 0;
                view.tooltipPrice.text  = afford ? $"{cost} B" : $"{cost} B  (-{cost - pool})";
                view.tooltipPrice.color = afford ? GeoPalette.Blue : GeoPalette.Signal;
            }
            if (view.tooltipDesc != null)
            {
                view.tooltipDesc.gameObject.SetActive(true);
                view.tooltipDesc.text = "Roll a new shelf of relics. Each restock costs more.";
            }
        }
        if (view.tooltipAccent != null) view.tooltipAccent.color = afford ? GeoPalette.Blue : GeoPalette.Signal;

        // Above the hovered slot, kept on screen.
        var corners = new Vector3[4];
        s.view.rect.GetWorldCorners(corners);
        view.tooltip.position = (corners[1] + corners[2]) * 0.5f;
        view.tooltip.GetWorldCorners(corners);
        float dx = 0f, dy = 0f;
        if (corners[0].x < 4f)                 dx = 4f - corners[0].x;
        if (corners[2].x > Screen.width - 4f)  dx = Screen.width - 4f - corners[2].x;
        if (corners[2].y > Screen.height - 4f) dy = Screen.height - 4f - corners[2].y;
        view.tooltip.position += new Vector3(dx, dy, 0f);
        return true;
    }

    // ── Buying ────────────────────────────────────────────────────────────────

    void ClickRelic(RelicSlot s)
    {
        var rm = ResourceManager.Instance;
        if (s.offer != null)
        {
            if (s.offer.sold) return;
            if (!RogueMarket.CanAfford(s.offer)) { _cantAffordFlash = 0.55f; return; }
            RogueMarket.Buy(s.offer);   // raises MarketChanged; the slots rebuild next frame
        }
        else
        {
            if (rm == null || !rm.CanAfford(RogueMarket.RerollCost, BlockType.Home)) { _cantAffordFlash = 0.55f; return; }
            RogueMarket.Reroll();
        }
    }
}
