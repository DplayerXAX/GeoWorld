using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The "pick 1 of N" screen. UpgradeManager (and the roguelite director) call Show()
// with an offer and a callback; the player clicks a card and the callback gets it —
// or null if the offer was allowed to be skipped and was.
//
// Built at runtime, like the other overlays that carry no scene art. It is MODAL in
// the way the rest of the game understands: while it is up RogueUiGate.ModalOpen is
// true, the board ignores clicks and Space does not start the wave.
//
// Public contract (unchanged for existing callers):
//   • Show(cards, onPicked)  — UpgradeManager calls this with the offer.
//   • Pick(index)            — what a card's button calls.
public class UpgradeChoiceUI : MonoBehaviour
{
    public static UpgradeChoiceUI Instance;

    public static bool Open => Instance != null && Instance._currentOffer != null;

    /// <summary>The instance in the scene, made if there is none.</summary>
    public static UpgradeChoiceUI Ensure()
    {
        if (Instance != null) return Instance;
        var found = FindFirstObjectByType<UpgradeChoiceUI>();
        if (found != null) { Instance = found; return found; }
        return new GameObject("UpgradeChoiceUI").AddComponent<UpgradeChoiceUI>();
    }

    Action<UpgradeCard> _onPicked;
    IList<UpgradeCard>  _currentOffer;

    Canvas        _canvas;
    CanvasGroup   _group;
    RectTransform _cardRow, _skipRt;
    TMP_Text      _title, _subtitle, _skipLabel;
    readonly List<GameObject> _built = new();
    float _t;

    void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public bool IsShowing => _currentOffer != null;

    public void Show(IList<UpgradeCard> cards, Action<UpgradeCard> onPicked)
        => Show(cards, onPicked, "CHOOSE A RELIC", null, false);

    public void Show(IList<UpgradeCard> cards, Action<UpgradeCard> onPicked,
                     string title, string subtitle, bool allowSkip, string skipLabel = "SKIP")
    {
        if (cards == null || cards.Count == 0)
        {
            onPicked?.Invoke(null);
            return;
        }

        // One at a time: an offer made while another is up is shown after it.
        if (_currentOffer != null)
        {
            var wait = new Queued { cards = cards, cb = onPicked, title = title, sub = subtitle, skip = allowSkip, skipLabel = skipLabel };
            _queue.Enqueue(wait);
            return;
        }

        EnsureUi();
        Clear();

        _currentOffer = cards;
        _onPicked     = onPicked;
        _title.text    = title;
        _subtitle.text = subtitle ?? "";
        _subtitle.gameObject.SetActive(!string.IsNullOrEmpty(subtitle));

        for (int i = 0; i < cards.Count; i++) BuildCard(cards[i], i);

        _skipRt.gameObject.SetActive(allowSkip);
        _skipLabel.text = skipLabel;

        _t = 0f;
        _canvas.enabled = true;
        _group.alpha = 0f;
    }

    class Queued { public IList<UpgradeCard> cards; public Action<UpgradeCard> cb; public string title, sub, skipLabel; public bool skip; }
    readonly Queue<Queued> _queue = new();

    // Hook this up to a card's button; `index` is into the offer passed to Show().
    public void Pick(int index)
    {
        if (_currentOffer == null) return;

        var card = (index >= 0 && index < _currentOffer.Count) ? _currentOffer[index] : null;

        var cb = _onPicked;
        _currentOffer = null;
        _onPicked     = null;
        if (_canvas != null) _canvas.enabled = false;

        cb?.Invoke(card);

        // Whatever was waiting.
        if (_queue.Count > 0)
        {
            var q = _queue.Dequeue();
            Show(q.cards, q.cb, q.title, q.sub, q.skip, q.skipLabel);
        }
    }

    // Optional: cancel the current offer without picking (the run ended mid-choice).
    public void Cancel()
    {
        _currentOffer = null;
        _onPicked     = null;
        _queue.Clear();
        if (_canvas != null) _canvas.enabled = false;
    }

    // ── UI ───────────────────────────────────────────────────────────────────

    void Update()
    {
        if (_currentOffer == null || _group == null) return;
        _t += Time.unscaledDeltaTime;
        _group.alpha = Mathf.Clamp01(_t / 0.25f);
    }

    void EnsureUi()
    {
        if (_canvas != null) return;

        _canvas = RogueUiGate.NewCanvas("UpgradeChoiceCanvas", transform, 4000, out _group);

        var dim = RogueUiGate.NewRect("Dim", _canvas.transform);
        RogueUiGate.Stretch(dim);
        var di = dim.gameObject.AddComponent<Image>();
        di.color = new Color(0.04f, 0.05f, 0.09f, 0.70f);        // raycast target: swallows clicks under it

        _title = RogueUiGate.NewText("Title", _canvas.transform, 58f, GeoPalette.Paper, FontStyles.Bold);
        Place(_title.rectTransform, new Vector2(0f, 340f), new Vector2(1500f, 90f));

        _subtitle = RogueUiGate.NewText("Subtitle", _canvas.transform, 28f, new Color(1f, 1f, 1f, 0.75f));
        Place(_subtitle.rectTransform, new Vector2(0f, 270f), new Vector2(1500f, 50f));

        _cardRow = RogueUiGate.NewRect("Cards", _canvas.transform);
        Place(_cardRow, new Vector2(0f, 20f), new Vector2(1500f, 520f));
        var h = _cardRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 36f;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = h.childControlHeight = false;
        h.childForceExpandWidth = h.childForceExpandHeight = false;

        // Skip.
        _skipRt = RogueUiGate.NewRect("Skip", _canvas.transform);
        Place(_skipRt, new Vector2(0f, -330f), new Vector2(360f, 64f));
        var si = _skipRt.gameObject.AddComponent<Image>();
        si.sprite = UIRoundedRect.Get(12); si.type = Image.Type.Sliced;
        si.color = new Color(1f, 1f, 1f, 0.14f);
        var sb = _skipRt.gameObject.AddComponent<Button>();
        sb.targetGraphic = si;
        sb.onClick.AddListener(() => Pick(-1));
        _skipLabel = RogueUiGate.NewText("Label", _skipRt, 26f, GeoPalette.Paper, FontStyles.Bold);
        RogueUiGate.Stretch(_skipLabel.rectTransform);

        _canvas.enabled = false;
    }

    void Clear()
    {
        foreach (var g in _built) if (g != null) Destroy(g);
        _built.Clear();
    }

    void BuildCard(UpgradeCard c, int index)
    {
        var rt = RogueUiGate.NewRect("Card" + index, _cardRow);
        rt.sizeDelta = new Vector2(440f, 500f);
        _built.Add(rt.gameObject);

        var rarity = RogueItems.ColorOf(c.rarity);
        var cat    = RogueItems.ColorOf(c.category);

        var bg = rt.gameObject.AddComponent<Image>();
        bg.sprite = UIRoundedRect.Get(22); bg.type = Image.Type.Sliced;
        bg.color = GeoPalette.Paper;

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = bg;
        var cols = btn.colors;
        cols.highlightedColor = new Color(1f, 0.97f, 0.85f);
        cols.pressedColor     = new Color(0.9f, 0.85f, 0.7f);
        btn.colors = cols;
        int captured = index;
        btn.onClick.AddListener(() => Pick(captured));

        // A rarity band across the top.
        var band = RogueUiGate.NewRect("Band", rt);
        band.anchorMin = new Vector2(0f, 1f); band.anchorMax = new Vector2(1f, 1f); band.pivot = new Vector2(0.5f, 1f);
        band.sizeDelta = new Vector2(0f, 74f);
        var bi = band.gameObject.AddComponent<Image>();
        bi.sprite = UIRoundedRect.Get(22); bi.type = Image.Type.Sliced; bi.color = rarity; bi.raycastTarget = false;

        var tag = RogueUiGate.NewText("Tag", band, 24f, Color.white, FontStyles.Bold);
        tag.text = $"{c.rarity.ToString().ToUpper()}   ·   {RogueItems.NameOf(c.category)}";
        RogueUiGate.Stretch(tag.rectTransform);

        // Initials, large — the card's stand-in for art.
        var seal = RogueUiGate.NewRect("Seal", rt);
        seal.anchorMin = seal.anchorMax = seal.pivot = new Vector2(0.5f, 1f);
        seal.anchoredPosition = new Vector2(0f, -112f);
        seal.sizeDelta = new Vector2(110f, 110f);
        var sImg = seal.gameObject.AddComponent<Image>();
        sImg.sprite = UIRoundedRect.Get(55); sImg.type = Image.Type.Sliced; sImg.color = cat; sImg.raycastTarget = false;
        var init = RogueUiGate.NewText("Initials", seal, 46f, Color.white, FontStyles.Bold);
        init.text = Initials(c.displayName);
        RogueUiGate.Stretch(init.rectTransform);

        var name = RogueUiGate.NewText("Name", rt, 38f, GeoPalette.Ink, FontStyles.Bold);
        name.text = c.displayName;
        var nrt = name.rectTransform;
        nrt.anchorMin = nrt.anchorMax = nrt.pivot = new Vector2(0.5f, 1f);
        nrt.anchoredPosition = new Vector2(0f, -190f);
        nrt.sizeDelta = new Vector2(400f, 52f);

        var desc = RogueUiGate.NewText("Desc", rt, 26f, GeoPalette.Ink, FontStyles.Normal, TextAlignmentOptions.Top);
        desc.text = c.description;
        var drt = desc.rectTransform;
        drt.anchorMin = new Vector2(0f, 0f); drt.anchorMax = new Vector2(1f, 1f);
        drt.offsetMin = new Vector2(28f, 24f); drt.offsetMax = new Vector2(-28f, -250f);
    }

    static string Initials(string name)
    {
        if (string.IsNullOrEmpty(name)) return "?";
        var parts = name.Split(' ');
        if (parts.Length >= 2) return ("" + char.ToUpper(parts[0][0]) + char.ToUpper(parts[1][0]));
        return name.Length >= 2 ? ("" + char.ToUpper(name[0]) + char.ToLower(name[1])) : name.ToUpper();
    }

    static void Place(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }
}
