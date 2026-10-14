using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The result of the wave just fought, as a compact strip under the objectives
// (top-left). Icons and numbers only — what a word would say, a glyph says:
//
//   header    ✓ / ✕  wave   ⌖ killed/spawned   ➜| leaked   ♥ lives lost      🔥  ⌄
//   bars      one per turret — its colour, its share of the damage, damage·kills
//   chips     a short icon per observation; hover for the sentence
//
// Hover anything for the detail: a bar says which turret and where, a chip says what
// it noticed. Click the header (or the chevron) to fold it away to its title row; the
// flame shows or hides the damage heat on the board. The fold is remembered.
//
// Built at runtime, like the objectives above it, and stacked under them (or at the
// objectives' own place when the level has none). Only while building — a fight is
// for watching.
[DisallowMultipleComponent]
public class BattleReportPanel : MonoBehaviour
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
        if (PlacementController.Instance == null) return;           // gameplay scene only
        if (FindFirstObjectByType<BattleReportPanel>() != null) return;
        new GameObject("BattleReportPanel").AddComponent<BattleReportPanel>();
    }

    // For the board: a click on the panel is the panel's.
    static RectTransform _panelRt;
    static bool _visible;
    public static bool IsPointerOver(Vector2 screenPos)
        => _visible && _panelRt != null && RectTransformUtility.RectangleContainsScreenPoint(_panelRt, screenPos, null);

    const string PrefKey = "ui.battleReport.collapsed";
    const float  PanelWidth = 360f;
    const float  TopWithoutObjectives = 200f;   // where the objectives' own panel starts

    static readonly Color Paper = new Color(0.949f, 0.937f, 0.902f, 0.92f);
    static readonly Color Ink   = GeoPalette.Ink;
    static readonly Color Muted = new Color(0.416f, 0.416f, 0.416f);
    static readonly Color Good  = new Color(0.18f, 0.62f, 0.36f);
    static readonly Color Bad   = new Color(0.82f, 0.25f, 0.18f);
    static readonly Color Warn  = new Color(0.86f, 0.58f, 0.10f);
    static readonly Color BarBg = new Color(0.086f, 0.086f, 0.086f, 0.10f);

    Canvas        _canvas;
    CanvasGroup   _group;
    RectTransform _panel, _header, _body, _tipRt;
    TMP_Text      _tip;

    bool  _collapsed;
    bool  _wasRunning;
    float _shown;
    int   _stamp = int.MinValue;

    void Start()
    {
        _collapsed = PlayerPrefs.GetInt(PrefKey, 0) == 1;
        Build();
        BattleReport.Changed += OnReport;
    }

    void OnDestroy()
    {
        BattleReport.Changed -= OnReport;
        BattleReport.SetHeatVisible(false);
        _visible = false;
        _panelRt = null;
    }

    // A wave just ended: the board shows where the fight was.
    void OnReport()
    {
        BattleReport.SetHeatVisible(true);
        _stamp = int.MinValue;
    }

    void Update()
    {
        var flow = GameFlowManager.Instance;
        if (flow == null || _group == null) return;

        bool running = flow.phase == GamePhase.Running;
        if (running && !_wasRunning)
        {
            BattleReport.SetHeatVisible(false);   // the old fight's heat goes when the next begins
            _stamp = int.MinValue;
        }
        _wasRunning = running;

        bool build = (flow.phase == GamePhase.Build || flow.phase == GamePhase.ReadyToRun)
                  && !IntroDirector.Playing && !SettingsScreen.Open && !GameFlowManager.SettlementUp;
        bool want = build && BattleReport.Last != null;

        _shown = Mathf.MoveTowards(_shown, want ? 1f : 0f, Time.unscaledDeltaTime * 6f);
        _group.alpha          = _shown;
        _group.blocksRaycasts = _shown > 0.5f;
        _group.interactable   = _shown > 0.5f;
        _visible = _shown > 0.5f;
        if (_shown <= 0.001f) return;

        // Under the objectives, wherever their panel ends this frame.
        float objBottom = LevelObjectivesTracker.PanelBottom;
        float top = objBottom > 0f ? objBottom + 10f : TopWithoutObjectives;
        _panel.anchoredPosition = new Vector2(16f, -top);

        int stamp = BattleReport.Version * 31 + (_collapsed ? 1 : 0) * 7 + (BattleReport.HeatVisible ? 1 : 0);
        if (stamp != _stamp)
        {
            _stamp = stamp;
            Rebuild();
        }
    }

    // ── Content ──────────────────────────────────────────────────────────────

    void Rebuild()
    {
        Clear(_header);
        Clear(_body);

        var r = BattleReport.Last;
        if (r == null) return;

        BuildHeader(r);
        _body.gameObject.SetActive(!_collapsed);
        if (!_collapsed) BuildBody(r);

        HideTip();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_panel);
    }

    static void Clear(RectTransform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var c = parent.GetChild(i).gameObject;
            c.SetActive(false);   // out of the layout this frame, gone the next
            Destroy(c);
        }
    }

    static string WaveLabel(BattleReport.Report r)
    {
        if (RunConfig.Mode != GameMode.Rogue) return r.wave.ToString();
        string s = RogueRun.WaveLabel(Mathf.Max(0, r.wave - 1));   // "2-3", or "1-5 BOSS"
        int sp = s.IndexOf(' ');
        return sp > 0 ? s.Substring(0, sp) : s;
    }

    void BuildHeader(BattleReport.Report r)
    {
        bool clean = r.leaked == 0;
        Icon(_header, clean ? UiGlyph.Check : UiGlyph.Cross, 28f, clean ? Good : Bad);
        Num(_header, "W" + WaveLabel(r), 20f, Ink, true);
        Space(_header, 2f);

        Icon(_header, UiGlyph.Crosshair, 22f, Ink);
        Num(_header, $"{r.killed}/{r.spawned}", 19f, Ink, false);

        if (r.leaked > 0)
        {
            Icon(_header, UiGlyph.Leak, 22f, Bad);
            Num(_header, r.leaked.ToString(), 19f, Bad, true);
        }
        if (r.livesLost > 0)
        {
            Icon(_header, UiGlyph.Heart, 22f, Bad);
            Num(_header, "-" + r.livesLost, 19f, Bad, true);
        }

        var spacer = NewRect("Spacer", _header);
        spacer.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        if (r.heat.Count > 0)
        {
            var flame = Icon(_header, UiGlyph.Flame, 26f,
                             BattleReport.HeatVisible ? new Color(0.90f, 0.35f, 0.15f) : new Color(Ink.r, Ink.g, Ink.b, 0.35f));
            flame.raycastTarget = true;
            var b = flame.gameObject.AddComponent<Button>();
            b.targetGraphic = flame;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() =>
            {
                BattleReport.SetHeatVisible(!BattleReport.HeatVisible);
                _stamp = int.MinValue;
            });
            var ht = flame.gameObject.AddComponent<BattleReportHoverTip>();
            ht.owner = this; ht.text = "Show where the damage landed, on the board";
        }

        Icon(_header, _collapsed ? UiGlyph.ChevronDown : UiGlyph.ChevronUp, 22f, new Color(Ink.r, Ink.g, Ink.b, 0.6f));
    }

    void BuildBody(BattleReport.Report r)
    {
        float total = Mathf.Max(1f, r.totalDamage);
        int rows = 0;
        foreach (var t in r.turrets)
        {
            if (t.idle || !t.deals || t.damage <= 0f || rows >= 5) continue;
            rows++;
            AddBar(t.damage / total, t.color, null, $"{t.damage:0}·{t.kills}",
                   $"{t.name} at {BattleReport.Cell(t.cell)}: {t.damage:0} damage, {t.kills} kills");
        }
        if (r.areaDamage > 0f)
            AddBar(r.areaDamage / total, new Color(0.55f, 0.55f, 0.58f), UiGlyph.Flame, $"{r.areaDamage:0}",
                   "Fire, gravity and traps: damage with no turret behind it");

        if (r.insights.Count > 0)
        {
            var row = NewRect("Chips", _body);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;

            foreach (var ins in r.insights)
            {
                Look(ins.kind, out var glyph, out var col);

                var chip = NewRect("Chip", row);
                var le = chip.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = 40f; le.preferredHeight = 32f;
                var bg = chip.gameObject.AddComponent<Image>();
                bg.sprite = UIRoundedRect.Get(10);
                bg.type   = Image.Type.Sliced;
                bg.color  = new Color(col.r, col.g, col.b, 0.16f);
                bg.raycastTarget = true;

                var ic = NewRect("Icon", chip);
                ic.anchorMin = Vector2.zero; ic.anchorMax = Vector2.one;
                ic.offsetMin = new Vector2(7f, 5f); ic.offsetMax = new Vector2(-7f, -5f);
                var img = ic.gameObject.AddComponent<Image>();
                img.sprite = UiGlyphs.Get(glyph);
                img.color = col;
                img.preserveAspect = true;
                img.raycastTarget = false;

                var ht = chip.gameObject.AddComponent<BattleReportHoverTip>();
                ht.owner = this; ht.text = ins.text;
            }
        }
    }

    static void Look(BattleReport.InsightKind k, out UiGlyph g, out Color c)
    {
        switch (k)
        {
            case BattleReport.InsightKind.Clean:        g = UiGlyph.Check;   c = Good;  break;
            case BattleReport.InsightKind.LeakDying:    g = UiGlyph.Leak;    c = Warn;  break;
            case BattleReport.InsightKind.LeakHealthy:  g = UiGlyph.Leak;    c = Bad;   break;
            case BattleReport.InsightKind.Idle:         g = UiGlyph.NoEntry; c = Warn;  break;
            case BattleReport.InsightKind.Disconnected: g = UiGlyph.NoEntry; c = Bad;   break;
            case BattleReport.InsightKind.Carry:        g = UiGlyph.Bolt;    c = Ink;   break;
            case BattleReport.InsightKind.HotSpot:      g = UiGlyph.Flame;   c = new Color(0.90f, 0.35f, 0.15f); break;
            case BattleReport.InsightKind.Gap:          g = UiGlyph.Gap;     c = Warn;  break;
            default:                                    g = UiGlyph.Dot;     c = Muted; break;
        }
    }

    // ── Hover detail ─────────────────────────────────────────────────────────

    public void ShowTip(string text)
    {
        if (_tip == null || string.IsNullOrEmpty(text)) return;
        _tip.text = text;
        _tipRt.gameObject.SetActive(true);
    }

    public void HideTip()
    {
        if (_tipRt != null) _tipRt.gameObject.SetActive(false);
    }

    // ── Building blocks ──────────────────────────────────────────────────────

    void Build()
    {
        RogueUiGate.EnsureEventSystem();   // hover and click are UGUI

        var go = new GameObject("BattleReportCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        _canvas = go.GetComponent<Canvas>();
        _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 91;   // just under the objectives (92) it hangs from
        var sc = go.GetComponent<CanvasScaler>();
        sc.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight  = 1f;   // the objectives' setting: the same units, so the two stack exactly
        _group = go.AddComponent<CanvasGroup>();
        _group.alpha = 0f;

        _panel = NewRect("Panel", go.transform);
        _panelRt = _panel;
        _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(0f, 1f);
        _panel.anchoredPosition = new Vector2(16f, -TopWithoutObjectives);
        _panel.sizeDelta = new Vector2(PanelWidth, 40f);

        var bg = _panel.gameObject.AddComponent<Image>();
        bg.sprite = UIRoundedRect.Get(18);
        bg.type   = Image.Type.Sliced;
        bg.color  = Paper;
        bg.raycastTarget = true;   // clicks on the panel stay on the panel

        var vlg = _panel.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(12, 12, 8, 10);
        vlg.spacing = 6f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        var fit = _panel.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fit.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        // Header — the whole row folds the panel.
        _header = NewRect("Header", _panel);
        var hl = _header.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 8f;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childControlWidth = hl.childControlHeight = true;
        hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
        _header.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;
        var hbg = _header.gameObject.AddComponent<Image>();
        hbg.color = new Color(1f, 1f, 1f, 0f);
        hbg.raycastTarget = true;
        var hb = _header.gameObject.AddComponent<Button>();
        hb.targetGraphic = hbg;
        hb.transition = Selectable.Transition.None;
        hb.onClick.AddListener(() =>
        {
            _collapsed = !_collapsed;
            PlayerPrefs.SetInt(PrefKey, _collapsed ? 1 : 0);
            _stamp = int.MinValue;
        });

        _body = NewRect("Body", _panel);
        var bl = _body.gameObject.AddComponent<VerticalLayoutGroup>();
        bl.spacing = 5f;
        bl.childAlignment = TextAnchor.UpperLeft;
        bl.childControlWidth = bl.childControlHeight = true;
        bl.childForceExpandWidth = true; bl.childForceExpandHeight = false;

        // The hover card hangs under the panel, out of its layout.
        _tipRt = NewRect("Tip", _panel);
        _tipRt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        _tipRt.anchorMin = new Vector2(0f, 0f);
        _tipRt.anchorMax = new Vector2(1f, 0f);
        _tipRt.pivot     = new Vector2(0.5f, 1f);
        _tipRt.anchoredPosition = new Vector2(0f, -6f);
        _tipRt.sizeDelta = new Vector2(0f, 40f);
        var tbg = _tipRt.gameObject.AddComponent<Image>();
        tbg.sprite = UIRoundedRect.Get(14);
        tbg.type   = Image.Type.Sliced;
        tbg.color  = new Color(Ink.r, Ink.g, Ink.b, 0.94f);
        tbg.raycastTarget = false;
        var tl = _tipRt.gameObject.AddComponent<VerticalLayoutGroup>();
        tl.padding = new RectOffset(12, 12, 8, 8);
        tl.childControlWidth = tl.childControlHeight = true;
        tl.childForceExpandWidth = true; tl.childForceExpandHeight = false;
        var tfit = _tipRt.gameObject.AddComponent<ContentSizeFitter>();
        tfit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        tfit.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        _tip = NewText("Text", _tipRt, 16f, new Color(0.949f, 0.937f, 0.902f), FontStyles.Normal, TextAlignmentOptions.TopLeft);
        _tip.textWrappingMode = TextWrappingModes.Normal;
        _tipRt.gameObject.SetActive(false);
    }

    // A labelled bar: its share of the damage, a colour chip (or glyph) at the left,
    // the numbers at the right.
    void AddBar(float fill, Color color, UiGlyph? glyph, string value, string tip)
    {
        var row = NewRect("Bar", _body);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;

        var back = NewRect("Back", row);
        Stretch(back);
        var bi = back.gameObject.AddComponent<Image>();
        bi.color = BarBg;
        bi.raycastTarget = true;
        var ht = back.gameObject.AddComponent<BattleReportHoverTip>();
        ht.owner = this; ht.text = tip;

        var fillRt = NewRect("Fill", row);
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = new Vector2(Mathf.Clamp(fill, 0.05f, 1f), 1f);
        fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
        var fi = fillRt.gameObject.AddComponent<Image>();
        fi.color = new Color(color.r, color.g, color.b, 0.55f);
        fi.raycastTarget = false;

        var chip = NewRect("Chip", row);
        chip.anchorMin = chip.anchorMax = new Vector2(0f, 0.5f);
        chip.pivot = new Vector2(0f, 0.5f);
        chip.anchoredPosition = new Vector2(7f, 0f);
        var ci = chip.gameObject.AddComponent<Image>();
        ci.raycastTarget = false;
        if (glyph.HasValue)
        {
            chip.sizeDelta = new Vector2(18f, 18f);
            ci.sprite = UiGlyphs.Get(glyph.Value);
            ci.color = Ink;
            ci.preserveAspect = true;
        }
        else
        {
            chip.sizeDelta = new Vector2(14f, 14f);
            ci.sprite = UIRoundedRect.Get(6);
            ci.type   = Image.Type.Sliced;
            ci.color  = color;
        }

        var v = NewText("Value", row, 16f, Ink, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        v.text = value;
        v.textWrappingMode = TextWrappingModes.NoWrap;
        var vrt = v.rectTransform;
        vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
        vrt.offsetMin = new Vector2(8f, 0f); vrt.offsetMax = new Vector2(-8f, 0f);
    }

    static Image Icon(Transform parent, UiGlyph g, float size, Color c)
    {
        var rt = NewRect("Icon", parent);
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = le.preferredHeight = size;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = UiGlyphs.Get(g);
        img.color = c;
        img.preserveAspect = true;
        img.raycastTarget = false;
        return img;
    }

    static TMP_Text Num(Transform parent, string s, float size, Color c, bool bold)
    {
        var t = NewText("Num", parent, size, c, bold ? FontStyles.Bold : FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        t.text = s;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }

    static void Space(Transform parent, float w)
    {
        var rt = NewRect("Space", parent);
        rt.gameObject.AddComponent<LayoutElement>().preferredWidth = w;
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

// Tells the report panel what the pointer is over, so it can say what it means.
public class BattleReportHoverTip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public BattleReportPanel owner;
    public string text;

    public void OnPointerEnter(PointerEventData e) { if (owner != null) owner.ShowTip(text); }
    public void OnPointerExit(PointerEventData e)  { if (owner != null) owner.HideTip(); }
}
