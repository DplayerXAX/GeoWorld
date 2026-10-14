using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The panel the build phase was missing: what is coming, how well the defence meets
// it, and what happened last time. Top-right, only while building.
//
//   NEXT WAVE   the enemy types of the coming wave, in spawn order, each with its
//               count, health and what it does (the same dossier the portal preview
//               shows — see WavePreview) — plus how much of the route the turrets can
//               hit (RouteCoverage), so a change to the route or a turret answers
//               itself here the moment it is made.
//   (The report on the wave just fought is BattleReportPanel's: a strip under the
//   objectives, top-left.)
//
// Built at runtime, like the other HUD overlays that have no scene art. Click a tab
// to switch; the dash collapses it to its title row.
[DisallowMultipleComponent]
public class WaveIntelHud : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Spawn()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TrySpawn();
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m) => TrySpawn();

    static void TrySpawn()
    {
        // PlacementController sets Instance in Awake; GameFlowManager only in Start,
        // which has not run yet when this fires — so it cannot be the scene test.
        if (PlacementController.Instance == null) return;           // gameplay scene only
        if (FindFirstObjectByType<WaveIntelHud>() != null) return;
        new GameObject("WaveIntelHud").AddComponent<WaveIntelHud>();
    }

    // For PlacementController: a click on the panel is the panel's, not the board's.
    static RectTransform _panelRt;
    static bool _visible;
    public static bool IsPointerOver(Vector2 screenPos)
        => _visible && _panelRt != null && RectTransformUtility.RectangleContainsScreenPoint(_panelRt, screenPos, null);

    enum Tab { Next, Market }

    const float PanelWidth = 372f;

    static readonly Color Ink    = GeoPalette.Ink;
    static readonly Color Muted  = new Color(0.416f, 0.416f, 0.416f);
    static readonly Color BarBg  = new Color(0.086f, 0.086f, 0.086f, 0.10f);

    Canvas      _canvas;
    CanvasGroup _group;
    RectTransform _panel, _content;
    TabButton _tabNext, _tabMarket, _collapse;

    Tab   _tab = Tab.Next;
    bool  _collapsed;
    bool  _wasRunning;
    float _shown;
    int   _stamp = int.MinValue;

    class TabButton { public Image bg; public TMP_Text label; public Button button; }

    void Start()
    {
        Build();
    }

    void OnDestroy()
    {
        RogueRun.MarketChanged -= OnMarket;
        RogueRun.RelicsChanged -= OnMarket;
        _visible = false; _panelRt = null;
    }

    void OnMarket() { _stamp = int.MinValue; }

    void Update()
    {
        var flow = GameFlowManager.Instance;
        if (flow == null || _group == null) return;

        bool running = flow.phase == GamePhase.Running;
        if (running && !_wasRunning)
        {
            // Fighting: the next build phase opens on what is coming, and the old heat goes.
            _tab = Tab.Next;
            _stamp = int.MinValue;
        }
        _wasRunning = running;

        bool build = (flow.phase == GamePhase.Build || flow.phase == GamePhase.ReadyToRun)
                  && !IntroDirector.Playing && !SettingsScreen.Open && !GameFlowManager.SettlementUp;

        _shown = Mathf.MoveTowards(_shown, build ? 1f : 0f, Time.unscaledDeltaTime * 6f);
        _group.alpha          = _shown;
        _group.blocksRaycasts = _shown > 0.5f;
        _group.interactable   = _shown > 0.5f;
        _visible = _shown > 0.5f;
        if (_shown <= 0.001f) return;

        int stamp = Stamp(flow);
        if (stamp == _stamp) return;
        _stamp = stamp;
        Rebuild(flow);
    }

    int Stamp(GameFlowManager flow)
    {
        unchecked
        {
            int h = flow.UpcomingWaveNumber;
            h = h * 31 + flow.RoundIndex;
            h = h * 31 + (int)flow.phase;
            h = h * 31 + RouteCoverage.Version;
            h = h * 31 + (int)_tab;
            if (_tab == Tab.Market)
            {
                h = h * 31 + RogueMarket.Version;
                h = h * 31 + (ResourceManager.Instance != null ? ResourceManager.Instance.BlockCurrency : 0);
            }
            h = h * 31 + (_collapsed ? 1 : 0);
            return h;
        }
    }

    // ── Content ──────────────────────────────────────────────────────────────

    void Rebuild(GameFlowManager flow)
    {
        for (int i = _content.childCount - 1; i >= 0; i--)
        {
            var c = _content.GetChild(i).gameObject;
            c.SetActive(false);        // out of the layout this frame, gone the next
            Destroy(c);
        }

        Style(_tabNext,   _tab == Tab.Next);
        if (_tabMarket != null) Style(_tabMarket, _tab == Tab.Market);
        _collapse.label.text = _collapsed ? "+" : "–";
        _content.gameObject.SetActive(!_collapsed);

        if (!_collapsed)
        {
            if (_tab == Tab.Market) BuildMarket();
            else                    BuildNext(flow);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(_panel);
    }

    void BuildNext(GameFlowManager flow)
    {
        var fc = flow.GetNextWaveForecast();
        if (!fc.valid || fc.groups == null || fc.groups.Count == 0)
        {
            AddText("<b>NEXT WAVE</b>\nNo wave intel yet.", 20f, Ink);
        }
        else
        {
            int starts = flow.AllStarts != null ? flow.AllStarts.Count : 1;
            string per = starts > 1 ? $" from each of {starts} spawn points" : "";
            string wlabel = RunConfig.Mode == GameMode.Rogue ? RogueRun.WaveLabel(fc.waveNumber - 1) : fc.waveNumber.ToString();
            AddText($"<b>WAVE {wlabel}</b>   <size=80%><color=#6A6A6A>{fc.totalCount} enemies{per}</color></size>", 21f, Ink);

            foreach (var g in fc.groups)
            {
                if (g.prefab == null) continue;
                AddText($"<b>{Pretty(g.name)}</b>  ×{g.count}   <size=80%><color=#6A6A6A>{(g.hp > 0 ? g.hp : g.prefab.maxHealth)} HP</color></size>", 20f, Ink);
                string mech = EnemyDossier.Mechanic(g.prefab);
                if (!string.IsNullOrEmpty(g.note)) mech = (mech == "No special ability" ? "" : mech + "\n") + g.note;
                AddText(mech, 17f, Muted);
            }
        }

        AddSpace(2f);
        AddText("<b>ROUTE COVERAGE</b>", 19f, Ink);
        var cov = RouteCoverage.Last;
        if (!cov.valid)
        {
            AddText("No route yet — connect a start to an end.", 17f, Muted);
            return;
        }
        if (cov.turrets == 0)
        {
            AddText("No turrets standing. Place one where the route passes.", 17f, Muted);
            return;
        }

        float c = cov.Coverage;
        AddBar(c, CoverageColor(c), $"{Mathf.RoundToInt(c * 100f)}% of the route",
               $"{cov.avgDepth:0.0} turrets / block");
        AddText(GapText(cov), 17f, Muted);
        AddText("Route colours: <mark=#FF5748cc>   </mark> none can hit  <mark=#FFD14Dcc>   </mark> one  <mark=#61FF8Ccc>   </mark> two or more", 15f, Muted);
    }

    // ── The relic market (roguelite) ──────────────────────────────────────────
    void BuildMarket()
    {
        var rm = ResourceManager.Instance;
        int have = rm != null ? rm.BlockCurrency : 0;
        AddText($"<b>RELIC MARKET</b>   <size=80%><color=#6A6A6A>block currency · {have} held</color></size>", 21f, Ink);

        bool any = false;
        foreach (var o in RogueMarket.Offers)
        {
            if (o?.card == null) continue;
            any = true;
            var c = o.card;
            string rar = ColorUtility.ToHtmlStringRGB(RogueItems.ColorOf(c.rarity));
            string cat = ColorUtility.ToHtmlStringRGB(RogueItems.ColorOf(c.category));

            AddSpace(2f);
            AddText($"<b>{c.displayName}</b>   <size=75%><color=#{rar}>{c.rarity.ToString().ToUpper()}</color> · <color=#{cat}>{RogueItems.NameOf(c.category)}</color></size>", 20f, Ink);
            AddText(c.description, 16f, Muted);

            var offer = o;
            if (o.sold) AddButton("SOLD", null, false);
            else        AddButton($"Buy   {o.price}", () => { RogueMarket.Buy(offer); _stamp = int.MinValue; }, have >= o.price);
        }
        if (!any) AddText("Nothing for sale.", 17f, Muted);

        AddSpace(4f);
        int cost = RogueMarket.RerollCost;
        AddButton($"Restock   {cost}", () => { RogueMarket.Reroll(); _stamp = int.MinValue; }, have >= cost);
    }

    static string Pretty(string n)
    {
        if (string.IsNullOrEmpty(n)) return "Enemy";
        string s = n.Replace("(Clone)", "").Replace("Enemy", "").Trim();
        return string.IsNullOrEmpty(s) ? n : s;
    }

    static Color CoverageColor(float c)
        => c < 0.4f ? new Color(1.00f, 0.34f, 0.28f) : (c < 0.75f ? new Color(1.00f, 0.78f, 0.28f) : new Color(0.30f, 0.82f, 0.48f));

    static string GapText(RouteCoverage.Result cov)
    {
        if (cov.longestGap <= 0) return "Every block of the route is within reach of a turret.";
        string where = "";
        if (cov.gapRouteLength > 0)
        {
            float start = (cov.gapEnd - cov.longestGap + 1f) / cov.gapRouteLength;
            float end   = (cov.gapEnd + 1f) / cov.gapRouteLength;
            where = start < 0.15f ? " at the start" : (end > 0.85f ? " at the end" : " mid-route");
        }
        return $"Longest stretch no turret can hit: <b>{cov.longestGap}</b> blocks{where}.";
    }

    // ── UI ───────────────────────────────────────────────────────────────────

    void Build()
    {
        var go = new GameObject("WaveIntelCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        _canvas = go.GetComponent<Canvas>();
        _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 80;   // under the HUD (90) and the tutorial/shop overlays
        var sc = go.GetComponent<CanvasScaler>();
        sc.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight  = 0.5f;
        _group = go.AddComponent<CanvasGroup>();
        _group.alpha = 0f;

        _panel = NewRect("Panel", go.transform);
        _panelRt = _panel;
        _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(1f, 1f);
        _panel.anchoredPosition = new Vector2(-48f, -16f);   // clear of the edge handle of the controls panel
        _panel.sizeDelta = new Vector2(PanelWidth, 60f);

        var bg = _panel.gameObject.AddComponent<Image>();
        bg.sprite        = UIRoundedRect.Get(18);
        bg.type          = Image.Type.Sliced;
        bg.color         = new Color(0.949f, 0.937f, 0.902f, 0.94f);
        bg.raycastTarget = true;    // clicks on the panel stay on the panel

        var vlg = _panel.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(14, 14, 12, 14);
        vlg.spacing = 7f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        var fit = _panel.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fit.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        var header = NewRect("Header", _panel);
        var hl = header.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 6f;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childControlWidth = hl.childControlHeight = true;
        hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
        header.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

        RogueUiGate.EnsureEventSystem();   // the tabs and buttons are UGUI
        bool rogue = RunConfig.Mode == GameMode.Rogue;
        _tabNext   = MakeTab(header, "NEXT", 100f, () => SetTab(Tab.Next));
        if (rogue)
        {
            _tabMarket = MakeTab(header, "MARKET", 104f, () => SetTab(Tab.Market));
            RogueRun.MarketChanged += OnMarket;
            RogueRun.RelicsChanged += OnMarket;
        }
        var spacer = NewRect("Spacer", header);
        spacer.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        _collapse  = MakeTab(header, "–", 36f, () => { _collapsed = !_collapsed; _stamp = int.MinValue; });

        _content = NewRect("Content", _panel);
        var cl = _content.gameObject.AddComponent<VerticalLayoutGroup>();
        cl.spacing = 5f;
        cl.childAlignment = TextAnchor.UpperLeft;
        cl.childControlWidth = cl.childControlHeight = true;
        cl.childForceExpandWidth = true; cl.childForceExpandHeight = false;
    }

    void SetTab(Tab t)
    {
        _tab = t;
        _collapsed = false;
        _stamp = int.MinValue;
    }

    TabButton MakeTab(RectTransform parent, string text, float width, UnityEngine.Events.UnityAction onClick)
    {
        var rt = NewRect("Tab", parent);
        rt.gameObject.AddComponent<LayoutElement>().preferredWidth = width;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = UIRoundedRect.Get(10);
        img.type   = Image.Type.Sliced;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        var t = NewText("Label", rt, 17f, Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        t.text = text;
        var trt = t.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        return new TabButton { bg = img, label = t, button = btn };
    }

    static void Style(TabButton b, bool active)
    {
        b.bg.color    = active ? Ink : new Color(0f, 0f, 0f, 0.06f);
        b.label.color = active ? new Color(0.949f, 0.937f, 0.902f) : new Color(Ink.r, Ink.g, Ink.b, 0.65f);
    }

    TMP_Text AddText(string rich, float size, Color color)
    {
        var t = NewText("Text", _content, size, color, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        t.textWrappingMode = TextWrappingModes.Normal;
        t.text = rich;
        return t;
    }

    void AddSpace(float h)
    {
        var rt = NewRect("Space", _content);
        rt.gameObject.AddComponent<LayoutElement>().preferredHeight = h;
    }

    // A labelled bar: `fill` of the width in `color`, label on the left, value on the right.
    void AddBar(float fill, Color color, string label, string right)
    {
        var row = NewRect("Bar", _content);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

        var back = NewRect("Back", row);
        Stretch(back);
        var bi = back.gameObject.AddComponent<Image>();
        bi.color = BarBg; bi.raycastTarget = false;

        var fillRt = NewRect("Fill", row);
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = new Vector2(Mathf.Clamp01(fill), 1f);
        fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
        var fi = fillRt.gameObject.AddComponent<Image>();
        fi.color = new Color(color.r, color.g, color.b, 0.55f); fi.raycastTarget = false;

        var l = NewText("Label", row, 16f, Ink, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        l.text = label;
        l.textWrappingMode = TextWrappingModes.NoWrap;
        l.overflowMode = TextOverflowModes.Ellipsis;
        var lrt = l.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(8f, 0f); lrt.offsetMax = new Vector2(-96f, 0f);

        var v = NewText("Value", row, 16f, Ink, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        v.text = right;
        v.textWrappingMode = TextWrappingModes.NoWrap;
        var vrt = v.rectTransform;
        vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
        vrt.offsetMin = new Vector2(8f, 0f); vrt.offsetMax = new Vector2(-8f, 0f);
    }

    void AddButton(string text, UnityEngine.Events.UnityAction onClick, bool enabled = true)
    {
        var rt = NewRect("Button", _content);
        rt.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = UIRoundedRect.Get(10);
        img.type   = Image.Type.Sliced;
        img.color  = enabled ? Ink : new Color(Ink.r, Ink.g, Ink.b, 0.28f);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.interactable = enabled;
        if (enabled) btn.onClick.AddListener(onClick);

        var t = NewText("Label", rt, 17f, new Color(0.949f, 0.937f, 0.902f), FontStyles.Bold, TextAlignmentOptions.Center);
        t.text = text;
        Stretch(t.rectTransform);
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static TMP_Text NewText(string name, Transform parent, float size, Color color,
                            FontStyles style, TextAlignmentOptions align)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize = size; t.color = color; t.fontStyle = style; t.alignment = align;
        t.raycastTarget = false; t.richText = true;
        return t;
    }
}
