using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// Two expandable edge panels for the gameplay HUD, built at runtime as UGUI:
//   • LEFT  — active synergies (live from SynergyEvaluator).
//   • RIGHT — keyboard controls (editable list).
// Each panel is a FULL-SCREEN (1920×1080-canvas) Image — your texture has the panel
// art positioned and the rest transparent. Clicking the edge handle slides the whole
// image in / out by `slideDistance`. Auto-spawns; only shows in gameplay and hides
// while the settings overlay is open.
//
// To restyle: drop a HudSidePanels onto a GameObject in your gameplay scene and set
// the fields (sprites / font / sizes). The auto-spawn skips itself when one exists.
[DisallowMultipleComponent]
public class HudSidePanels : MonoBehaviour
{
    [System.Serializable]
    public class Control { public string key; public string action; }

    public List<Control> controls = new();

    [Header("Font (TextMeshPro — leave null for TMP default)")]
    public TMP_FontAsset font;
    public float titleSize  = 24f;
    public float rowSize    = 17f;
    public float handleSize = 16f;

    [Header("Panels (full-screen textures)")]
    [Tooltip("Left (synergies) full-screen panel texture. Art positioned, rest transparent.")]
    public Sprite  leftSprite;
    [Tooltip("Right (controls) full-screen panel texture. Falls back to leftSprite if null.")]
    public Sprite  rightSprite;
    [Tooltip("Tint applied to the textures. Keep white to show their true colours.")]
    public Color   panelColor = Color.white;
    [Tooltip("How far the panel art slides off-screen when closed. Match your art's column width.")]
    public float   slideDistance = 360f;
    [Tooltip("Padding (x = left/right, y = top) for the text laid over the art.")]
    public Vector2 contentInset = new Vector2(40f, 90f);

    [Header("Handle (the click-to-expand tab)")]
    public Sprite handleSprite;
    public Color  handleColor  = new Color(0.949f, 0.937f, 0.902f, 0.95f);
    public float  handleWidth  = 34f;
    public float  handleHeight = 150f;

    // Set true while the cursor is over an open panel column / handle, so world clicks
    // (placement) don't fire underneath. Read by PlacementController.
    public static bool PointerOver;

    bool _synOpen, _ctrlOpen;

    Canvas        _canvas;
    RectTransform _leftPanel, _rightPanel, _leftHandle, _rightHandle, _leftContent, _rightContent;

    class Row { public GameObject go; public RectTransform rt; public Image swatch; public TMP_Text label; public ActiveSynergy active; }
    readonly List<Row> _synRows = new();

    bool _autoSpawned;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Spawn()
    {
        if (FindFirstObjectByType<HudSidePanels>() != null) return;
        var go = new GameObject("HudSidePanels");
        DontDestroyOnLoad(go);
        go.AddComponent<HudSidePanels>()._autoSpawned = true;
    }

    void Awake()
    {
        // A scene-placed (sprite-configured) instance wins over the auto-spawned
        // default that persists from the Title scene — kill the default so we don't
        // end up with two side-panel sets (one unconfigured).
        var all = FindObjectsByType<HudSidePanels>(FindObjectsSortMode.None);
        foreach (var o in all)
            if (o != this && o._autoSpawned) Destroy(o.gameObject);

        // If this very object is a leftover duplicate auto-spawn, bail.
        if (_autoSpawned && all.Length > 1 && System.Array.Exists(all, o => o != this && !o._autoSpawned))
        {
            Destroy(gameObject);
            return;
        }

        EnsureDefaults();
        BuildUI();
    }

    void OnDisable() => SynergyHoverHighlight.Clear();
    void OnDestroy() => SynergyHoverHighlight.Clear();

    void EnsureDefaults()
    {
        if (controls.Count > 0) return;
        void A(string k, string a) => controls.Add(new Control { key = k, action = a });
        A("W A S D", "Move block");
        A("Q / E",   "Raise / lower");
        A("1 / 2 / 3", "Rotate block");
        A("Tab", "Switch mode");
        A("F", "Open / Close shop");
        A("LMB",     "Select / place");
        A("Space",   "Start wave");
        A("R",       "Refresh shop");
        A("Hold R",  "Restart level");
        A("RMB drag","Rotate camera");
        A("Scroll",  "Zoom");
        A("Esc",     "Pause / settings");
    }

    void Update()
    {
        if (_canvas == null) return;

        bool show = GameFlowManager.Instance != null && !SettingsScreen.Open;
        _canvas.enabled = show;
        if (!show) { PointerOver = false; SynergyHoverHighlight.Clear(); return; }

        UpdateSynergies();

        // Click anywhere while a panel is open → collapse it (and swallow that click
        // so it doesn't also place / select in the world).
        bool closedThisClick = false;
        if (Input.GetMouseButtonDown(0) && (_synOpen || _ctrlOpen))
        {
            _synOpen = _ctrlOpen = false;
            closedThisClick = true;
        }

        float k = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);

        _leftPanel.anchoredPosition = new Vector2(
            Mathf.Lerp(_leftPanel.anchoredPosition.x, _synOpen ? 0f : -slideDistance, k), 0f);
        _rightPanel.anchoredPosition = new Vector2(
            Mathf.Lerp(_rightPanel.anchoredPosition.x, _ctrlOpen ? 0f : slideDistance, k), 0f);

        UpdatePanelAlpha(_leftPanel, _synOpen, k);
        UpdatePanelAlpha(_rightPanel, _ctrlOpen, k);

        // Handle hides while its panel is open; reappears once closed.
        _leftHandle.gameObject.SetActive(!_synOpen);
        _rightHandle.gameObject.SetActive(!_ctrlOpen);

        UpdatePointerOver();
        if (closedThisClick) PointerOver = true;
    }

    void UpdatePanelAlpha(RectTransform rt, bool isOpen, float k)
    {
        var img = rt.GetComponent<Image>();
        Color c = img.color;
        float targetAlpha = isOpen ? 1f : 0f;
        c.a = Mathf.Lerp(c.a, targetAlpha, k);
        img.color = c;
    }

    void UpdatePointerOver()
    {
        Vector2 mp = VirtualCursor.Position;
        bool over = Contains(_leftHandle, mp) || Contains(_rightHandle, mp);
        if (_synOpen)  over |= Contains(_leftContent,  mp);
        if (_ctrlOpen) over |= Contains(_rightContent, mp);
        PointerOver = over;
    }

    static bool Contains(RectTransform rt, Vector2 screenPoint)
        => rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, screenPoint, null);

    // ── Build ──────────────────────────────────────────────────────────────────

    void BuildUI()
    {
        var canvasGO = new GameObject("HudSidePanelsCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);

        _canvas = canvasGO.GetComponent<Canvas>();
        _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 100;

        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 1f;

        EnsureEventSystem();

        BuildLeft();
        BuildRight();
    }

    static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;
        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        DontDestroyOnLoad(es);
    }

    void BuildLeft()
    {
        _leftPanel   = NewFullScreenPanel("SynergyPanel", leftSprite);
        _leftContent = NewColumn(_leftPanel, right: false);
        AddTitle(_leftContent, "SYNERGIES");
        _leftHandle  = BuildHandle("SYNERGIES", _leftPanel, right: false);
    }

    void BuildRight()
    {
        _rightPanel   = NewFullScreenPanel("ControlsPanel", rightSprite != null ? rightSprite : leftSprite);
        _rightContent = NewColumn(_rightPanel, right: true);
        AddTitle(_rightContent, "CONTROLS");

        foreach (var c in controls)
        {
            if (c == null) continue;
            var row = NewRect("Row", _rightContent);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleLeft; h.spacing = 8f;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            row.gameObject.AddComponent<LayoutElement>().minHeight = rowSize * 1.6f;

            var key = NewText("Key", row, rowSize, GeoPalette.Blue, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            key.text = c.key;
            var kle = key.gameObject.AddComponent<LayoutElement>();
            kle.minWidth = kle.preferredWidth = 110f;

            var act = NewText("Action", row, rowSize, GeoPalette.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            act.text = c.action;
            act.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        }

        _rightHandle = BuildHandle("CONTROLS", _rightPanel, right: true);
    }

    RectTransform NewFullScreenPanel(string name, Sprite sprite)
    {
        var rt = NewRect(name, _canvas.transform);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;   // fill the screen

        var img = rt.gameObject.AddComponent<Image>();
        Color startColor = panelColor;
        startColor.a = 0f;
        img.color = startColor;
        img.raycastTarget = false;                    // transparent areas must not block clicks
        ApplySprite(img, sprite);
        return rt;
    }

    // A full-height text column hugging the panel's left or right edge, sized to the
    // slide distance so it overlays the art that slides with it.
    RectTransform NewColumn(RectTransform panel, bool right)
    {
        var col = NewRect("Content", panel);
        col.anchorMin = new Vector2(right ? 1f : 0f, 0f);
        col.anchorMax = new Vector2(right ? 1f : 0f, 1f);
        col.pivot     = new Vector2(right ? 1f : 0f, 0.5f);
        col.sizeDelta = new Vector2(slideDistance, 0f);
        col.anchoredPosition = Vector2.zero;

        var vlg = col.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset((int)contentInset.x, (int)contentInset.x, (int)contentInset.y+150, 0);
        vlg.spacing = 8f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        return col;
    }

    void AddTitle(RectTransform column, string text)
    {
        var t = NewText("Title", column, titleSize, GeoPalette.Ink, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        t.text = text;
        t.gameObject.AddComponent<LayoutElement>().minHeight = titleSize * 1.6f;
    }

    RectTransform BuildHandle(string label, RectTransform panel, bool right)
    {
        var h = NewRect("Handle", panel);
        // Rides at the inner edge of the art column, so when closed it pokes out at the screen edge.
        h.anchorMin = h.anchorMax = new Vector2(right ? 1f : 0f, 0.5f);
        h.pivot = new Vector2(right ? 1f : 0f, 0.5f);
        h.anchoredPosition = new Vector2(right ? -slideDistance : slideDistance, 0f);
        h.sizeDelta = new Vector2(handleWidth, handleHeight);

        var img = h.gameObject.AddComponent<Image>();
        img.color = handleColor;
        ApplySprite(img, handleSprite);   // null → plain handleColor tab

        Color labelCol = handleColor.grayscale > 0.5f ? GeoPalette.Ink : GeoPalette.Paper;
        var lbl = NewText("Label", h, handleSize, labelCol, FontStyles.Bold, TextAlignmentOptions.Center);
        lbl.text = label;
        var lrt = lbl.rectTransform;
        lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
        lrt.pivot = new Vector2(0.5f, 0.5f);
        lrt.sizeDelta = new Vector2(handleHeight, handleWidth);   // swapped because rotated
        lrt.anchoredPosition = Vector2.zero;
        lrt.localRotation = Quaternion.Euler(0f, 0f, right ? 90f : -90f);

        var btn = h.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => { if (right) _ctrlOpen = !_ctrlOpen; else _synOpen = !_synOpen; });
        return h;
    }

    // ── Synergy rows (live) ──────────────────────────────────────────────────────

    void UpdateSynergies()
    {
        var ev = SynergyEvaluator.Instance;
        int n = 0;

        if (ev != null)
            foreach (var a in ev.Actives)
            {
                if (a?.rule == null) continue;
                var row = (n < _synRows.Count) ? _synRows[n] : MakeSynRow();
                row.go.SetActive(true);
                row.active = a;
                row.swatch.enabled = true;
                row.swatch.color   = GeoPalette.Accents[n % GeoPalette.Accents.Length];
                string nm     = string.IsNullOrEmpty(a.rule.displayName) ? a.rule.name : a.rule.displayName;
                string title  = a.tier > 1 ? $"{nm}  ·  T{a.tier}" : nm;
                string detail = BuildSynergyDetail(a);
                row.label.color = GeoPalette.Ink;
                row.label.text  = detail != null
                    ? $"{title}\n<size=70%><color=#7A7A7A>{detail}</color></size>"
                    : title;
                n++;
            }

        if (n == 0)
        {
            var row = (_synRows.Count > 0) ? _synRows[0] : MakeSynRow();
            row.go.SetActive(true);
            row.active = null;
            row.swatch.enabled = false;
            row.label.color    = new Color(0.4f, 0.4f, 0.4f);
            row.label.text     = "No active synergies";
            n = 1;
        }

        for (int i = n; i < _synRows.Count; i++) { _synRows[i].go.SetActive(false); _synRows[i].active = null; }

        UpdateSynergyHover();
    }

    // Highlights the claimed cells of whichever synergy row the pointer is
    // currently over — same manual hit-test pattern as UpdatePointerOver()
    // (rows have no raycastTarget graphic of their own, so this reuses
    // RectTransformUtility directly instead of routing through UGUI events).
    void UpdateSynergyHover()
    {
        ActiveSynergy hovered = null;
        if (_synOpen)
        {
            Vector2 mp = VirtualCursor.Position;
            for (int i = 0; i < _synRows.Count; i++)
            {
                var row = _synRows[i];
                if (row.active == null || !row.go.activeSelf) continue;
                if (Contains(row.rt, mp)) { hovered = row.active; break; }
            }
        }
        SynergyHoverHighlight.SetHovered(hovered);
        SynergyHoverHighlight.Tick();
    }

    // Live stat line for an active synergy — the numbers/affected units it's
    // currently producing, read straight off its GameEffect instance. Returns
    // null for rules with no numeric detail to show (name-only row).
    static string BuildSynergyDetail(ActiveSynergy a)
    {
        if (a?.rule == null) return null;

        // Enlightenment's grant is tier-scoped (see UnlockTowerUpgradeEffect /
        // TowerUpgradeGate), not the flat `rule.effect` — key off the rule type.
        if (a.rule is EnlightenmentRule)
        {
            int allowed = TowerUpgradeGate.AllowedUpgrades;
            return $"{allowed} turret upgrade{(allowed == 1 ? "" : "s")} allowed";
        }

        switch (a.rule.effect)
        {
            case AbundanceHarvestEffect ah:
                // Per-INSTANCE numbers, not ah.LastUnitCount/LastPayoutAmount —
                // those are the effect-wide total across every simultaneous
                // Abundance loop, which would show the same combined number on
                // every loop's row. Recompute just this active's contribution.
                int units = ah.countMode == AbundanceHarvestEffect.CountMode.Pieces
                    ? SynergyEffectUtil.CountParticipatingPieces(a)
                    : SynergyEffectUtil.CountParticipatingCells(a);
                int payout = units > 0 ? units * Mathf.Max(0, ah.blockPerUnit) + Mathf.Max(0, ah.flatBonus) : 0;
                return $"{units} piece{(units == 1 ? "" : "s")}  ·  +{payout}/turn";
            case OrderSlowEffect os:
                int blocks  = SynergyEffectUtil.CountParticipatingPieces(a);
                int slowPct = Mathf.RoundToInt(os.SlowFractionFor(blocks) * 100f);
                return $"-{slowPct}% speed  ·  {os.AffectedEnemyCount} enemy affected";
            case HarmonyAttackSpeedEffect ha:
                int spdPct = Mathf.RoundToInt(ha.attackSpeedBonus * 100f);
                return $"+{spdPct}% atk spd  ·  {ha.BuffedTurretCount} turret{(ha.BuffedTurretCount == 1 ? "" : "s")}";
            default:
                return null;
        }
    }

    Row MakeSynRow()
    {
        var rt = NewRect("Row", _leftContent);
        var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.childAlignment = TextAnchor.MiddleLeft; h.spacing = 8f;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = false;
        // Tall enough for a two-line row (name + detail) — used uniformly so
        // rows don't jump height depending on whether a synergy has a detail
        // line this frame.
        rt.gameObject.AddComponent<LayoutElement>().minHeight = rowSize * 2.8f;

        var sw = NewImage("Swatch", rt, Color.white, false);
        var swle = sw.gameObject.AddComponent<LayoutElement>();
        swle.minWidth = swle.preferredWidth = swle.minHeight = swle.preferredHeight = 16f;

        var lbl = NewText("Label", rt, rowSize, GeoPalette.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        var row = new Row { go = rt.gameObject, rt = rt, swatch = sw, label = lbl };
        _synRows.Add(row);
        return row;
    }

    // ── UI primitives ────────────────────────────────────────────────────────────

    static void ApplySprite(Image img, Sprite sprite)
    {
        if (sprite == null) return;
        img.sprite = sprite;
        img.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
    }

    RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    Image NewImage(string name, Transform parent, Color color, bool raycast)
    {
        var rt = NewRect(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = raycast;
        return img;
    }

    TMP_Text NewText(string name, Transform parent, float size, Color color,
                     FontStyles style, TextAlignmentOptions align)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize      = size;
        t.color         = color;
        t.fontStyle     = style;
        t.alignment     = align;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }
}
