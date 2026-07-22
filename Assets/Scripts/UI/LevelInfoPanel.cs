using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Level-select detail panel — self-building UGUI + TextMeshPro (no manual wiring).
// Put this component on an empty GameObject and assign it to LevelMapController.infoPanel.
// It builds a rounded paper panel on the RIGHT, laid out with TMP: title, status,
// best wave, description, and an Enter button. LevelMapController calls Show / Hide.
public class LevelInfoPanel : MonoBehaviour
{
    [Header("Font (leave null for TMP default)")]
    public TMP_FontAsset font;
    public float titleSize  = 34f;
    public float bodySize   = 20f;
    public float buttonSize = 22f;

    [Header("Use your existing panel (UI Image) — drag its RectTransform here")]
    [Tooltip("If set, the level info is laid out INTO this existing panel (its art stays; only the text fades). Leave null to auto-build a panel.")]
    public RectTransform targetPanel;

    [Header("Background (auto-build only; null = rounded paper)")]
    public Sprite panelSprite;

    [Header("Colors (defaults tuned for a dark panel)")]
    public Color panelColor      = new Color(0.949f, 0.937f, 0.902f, 0.96f);  // used only when no sprite
    public Color titleColor      = new Color(0.96f, 0.96f, 0.96f);             // light
    public Color bodyColor       = new Color(0.80f, 0.80f, 0.82f);
    public Color accentColor     = new Color(0.910f, 0.698f, 0.227f);          // gold rule / status
    public Color buttonColor     = new Color(0.949f, 0.937f, 0.902f);          // paper button
    public Color buttonTextColor = new Color(0.086f, 0.086f, 0.086f);          // ink label

    [Header("Layout")]
    [Tooltip("DESIRED panel size at the 1920×1080 reference. x = width, y = height. Shrunk automatically when the window can't fit it — never grown past this.")]
    public Vector2 panelSize   = new Vector2(420f, 700f);
    public float rightMargin   = 60f;
    [Tooltip("Clear space kept above and below the panel. The panel shrinks rather than run off a short window.")]
    public float verticalMargin = 60f;
    [Tooltip("Hard ceiling on how much of the window's width the panel may eat, for narrow/portrait aspects.")]
    [Range(0.2f, 0.9f)] public float maxWidthFraction = 0.5f;
    public Vector2 contentPad  = new Vector2(36f, 40f);   // inner padding (x sides, y top/bottom)
    public int   cornerRadius  = 26;
    public float fadeSpeed     = 12f;

    [Tooltip("Put the hosting Canvas on ScaleWithScreenSize @1920×1080. Off only if you're driving the scaler yourself.")]
    public bool autoScaleCanvas = true;

    [Header("Enemy roster")]
    [Tooltip("Balance asset — only needed to list enemies for levels that have NO authored waves (those roll their roster from BalanceTable.enemies). Levels with authored waves read their roster straight off them.")]
    public BalanceTable balance;
    [Tooltip("Portrait size in the roster rows.")]
    public float thumbSize = 46f;

    CanvasGroup _cg;
    TMP_Text    _title, _status, _best, _desc, _enterLabel;
    RectTransform _roster;
    Button      _enter;
    Action      _onEnter;
    float       _target;

    RectTransform _panel;       // null when laying into a targetPanel you authored
    RectTransform _canvasRect;  // the space we have to fit inside

    void Awake() { BuildUI(); }

    void Update()
    {
        ApplyResponsiveLayout();

        if (_cg == null) return;
        _cg.alpha = Mathf.Lerp(_cg.alpha, _target, 1f - Mathf.Exp(-fadeSpeed * Time.unscaledDeltaTime));
        bool on = _target > 0.5f && _cg.alpha > 0.5f;
        _cg.interactable = _cg.blocksRaycasts = on;
    }

    // Fit the panel to whatever the window currently is. Run per-frame rather than
    // once at Awake because a free-aspect Game view (and a resizable player window)
    // changes the canvas rect at runtime, and the panel has to follow.
    //
    // panelSize is a CEILING, not a fixed size: at the 1920×1080 reference the
    // clamps are inactive and you get exactly the 420×700 the panel was authored
    // at, so this changes nothing at the ratio that already looked right. It only
    // bites when the window genuinely can't accommodate it.
    void ApplyResponsiveLayout()
    {
        if (_panel == null || _canvasRect == null) return;

        float availH = _canvasRect.rect.height - verticalMargin * 2f;
        float availW = _canvasRect.rect.width * maxWidthFraction - rightMargin;
        if (availH <= 1f || availW <= 1f) return;   // canvas hasn't resolved yet

        var size = new Vector2(Mathf.Min(panelSize.x, availW),
                               Mathf.Min(panelSize.y, availH));
        if (_panel.sizeDelta != size) _panel.sizeDelta = size;
    }

    // ── API (called by LevelMapController) ───────────────────────────────────────
    public void Show(string title, string desc, string status, string best, bool canEnter, Action onEnter)
        => Show(title, desc, status, best, canEnter, onEnter, null);

    // `level` is optional — pass it to list the enemies and special mechanics
    // waiting inside. Null keeps the plain title/desc panel.
    public void Show(string title, string desc, string status, string best, bool canEnter,
                     Action onEnter, LevelDefinition level)
    {
        _title.text  = title;
        _status.text = status;
        SetOptional(_desc, desc);
        SetOptional(_best, best);
        _enterLabel.text   = canEnter ? "Enter" : "Locked";
        _enter.interactable = canEnter;

        BuildRoster(level);

        _onEnter = onEnter;
        _target  = 1f;
    }

    // Rebuilt per selection rather than pooled: the panel shows one level at a
    // time and switching is a click, not a per-frame cost.
    void BuildRoster(LevelDefinition lv)
    {
        if (_roster == null) return;

        for (int i = _roster.childCount - 1; i >= 0; i--) Destroy(_roster.GetChild(i).gameObject);
        if (lv == null) { _roster.gameObject.SetActive(false); return; }

        var enemies  = LevelRoster.Enemies(lv, balance);
        var specials = LevelRoster.SpecialMechanics(lv);
        if (enemies.Count == 0 && specials.Count == 0) { _roster.gameObject.SetActive(false); return; }

        _roster.gameObject.SetActive(true);

        if (enemies.Count > 0)
        {
            AddHeading(_roster, "THREATS");
            foreach (var e in enemies) AddEnemyRow(_roster, e);
        }

        if (specials.Count > 0)
        {
            AddHeading(_roster, "MECHANICS");
            foreach (var s in specials)
            {
                var t = NewText("Special", _roster, bodySize * 0.82f, bodyColor, FontStyles.Normal,
                                TextAlignmentOptions.TopLeft, true);
                t.text = "• " + s;
            }
        }
    }

    void AddHeading(RectTransform parent, string label)
    {
        var t = NewText("Heading", parent, bodySize * 0.8f, accentColor, FontStyles.Bold,
                        TextAlignmentOptions.TopLeft, false);
        t.text = label;
        t.gameObject.AddComponent<LayoutElement>().minHeight = bodySize * 1.3f;
    }

    // [portrait] [name + what it does]
    void AddEnemyRow(RectTransform parent, EnemySurfaceUnit prefab)
    {
        var row = NewRect("EnemyRow", parent);
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 10f;
        h.childAlignment = TextAnchor.UpperLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = false;
        row.gameObject.AddComponent<LayoutElement>().minHeight = thumbSize;

        var iconRt = NewRect("Icon", row);
        var icon = iconRt.gameObject.AddComponent<Image>();
        icon.color   = new Color(1f, 1f, 1f, 0f);   // stays invisible until the render lands
        icon.enabled = false;
        var le = iconRt.gameObject.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = le.minHeight = le.preferredHeight = thumbSize;
        EnemyThumbnail.Request(prefab, icon);

        var t = NewText("Info", row, bodySize * 0.82f, bodyColor, FontStyles.Normal,
                        TextAlignmentOptions.TopLeft, true);
        t.text = $"<b>{prefab.name}</b>  <size=90%><color=#9A9A9A>{prefab.maxHealth} HP</color></size>\n" +
                 $"<size=90%><color=#9A9A9A>{EnemyDossier.Mechanic(prefab)}</color></size>";
        t.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
    }

    public void Hide() { _target = 0f; _onEnter = null; }

    static void SetOptional(TMP_Text t, string s)
    {
        bool has = !string.IsNullOrEmpty(s);
        t.gameObject.SetActive(has);
        t.text = s;
    }

    // ── Build ──────────────────────────────────────────────────────────────────
    void BuildUI()
    {
        EnsureEventSystem();
        ConfigureCanvas();
        RectTransform content;

        if (targetPanel != null)
        {
            // Lay the text into your existing panel (a child container so we don't
            // disturb the panel's own children; only this content fades).
            content = NewRect("LevelInfoContent", targetPanel);
            content.anchorMin = Vector2.zero; content.anchorMax = Vector2.one;
            content.offsetMin = new Vector2(contentPad.x, contentPad.y);
            content.offsetMax = new Vector2(-contentPad.x, -contentPad.y);
            _cg = content.gameObject.AddComponent<CanvasGroup>();
            _cg.alpha = 0f;
            AddLayout(content, 0);
        }
        else
        {
            var panel = NewRect("LevelInfoPanel", transform);
            _panel = panel;

            panel.anchorMin = panel.anchorMax = new Vector2(1f, 0.5f);
            panel.pivot = new Vector2(1f, 0.5f);
            panel.anchoredPosition = new Vector2(-rightMargin, 0f);
            panel.sizeDelta = panelSize;   // ApplyResponsiveLayout clamps this down as needed

            _cg = panel.gameObject.AddComponent<CanvasGroup>();
            _cg.alpha = 0f;

            var bg = panel.gameObject.AddComponent<Image>();

            if (panelSprite != null)
            {
                bg.sprite = panelSprite;
                bg.type = panelSprite.border != Vector4.zero
                    ? Image.Type.Sliced
                    : Image.Type.Simple;
                bg.color = Color.white;
            }
            else
            {
                bg.sprite = UIRoundedRect.Get(cornerRadius);
                bg.type = Image.Type.Sliced;
                bg.color = panelColor;
            }

            content = NewRect("Content", panel);
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = new Vector2(contentPad.x, contentPad.y);
            content.offsetMax = new Vector2(-contentPad.x, -contentPad.y);

            AddLayout(content, 0);
        }

        BuildContent(content);
    }

    void AddLayout(RectTransform parent, int padX, int padY = -1)
    {
        if (padY < 0) padY = padX;
        var vlg = parent.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(padX, padX, padY, padY);
        vlg.spacing = 12f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
    }

    void BuildContent(RectTransform parent)
    {
        _title  = NewText("Title",  parent, titleSize, titleColor, FontStyles.Bold,   TextAlignmentOptions.TopLeft, false);
        AddRule(parent);
        _status = NewText("Status", parent, bodySize,  accentColor, FontStyles.Bold,  TextAlignmentOptions.TopLeft, false);
        _best   = NewText("Best",   parent, bodySize,  bodyColor,  FontStyles.Normal, TextAlignmentOptions.TopLeft, false);
        _desc   = NewText("Desc",   parent, bodySize,  bodyColor,  FontStyles.Normal, TextAlignmentOptions.TopLeft, true);

        // Threats / mechanics list, filled per selection by BuildRoster.
        _roster = NewRect("Roster", parent);
        var rv = _roster.gameObject.AddComponent<VerticalLayoutGroup>();
        rv.spacing = 8f;
        rv.childAlignment = TextAnchor.UpperLeft;
        rv.childControlWidth = rv.childControlHeight = true;
        rv.childForceExpandWidth = true; rv.childForceExpandHeight = false;
        _roster.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _roster.gameObject.SetActive(false);

        var brt = NewRect("Enter", parent);
        var img = brt.gameObject.AddComponent<Image>();
        img.color = buttonColor;
        _enter = brt.gameObject.AddComponent<Button>();
        _enter.targetGraphic = img;
        brt.gameObject.AddComponent<LayoutElement>().minHeight = buttonSize * 2.2f;
        _enter.onClick.AddListener(() => _onEnter?.Invoke());
        _enterLabel = NewText("Label", brt, buttonSize, buttonTextColor, FontStyles.Bold, TextAlignmentOptions.Center, false);
        var lrt = _enterLabel.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
    }

    void AddRule(RectTransform parent)
    {
        var rt = NewRect("Rule", parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = accentColor;
        rt.gameObject.AddComponent<LayoutElement>().minHeight = 4f;
    }

    // The hosting Canvas in LevelSelect.unity was left on Constant Pixel Size with
    // the default 800×600 reference — meaning the panel was laid out in raw screen
    // pixels and never scaled with the window at all. That's why it only read
    // correctly at 1920×1080: at any other size the 420×700 panel stayed 420×700
    // physical pixels, so it ballooned on small windows and shrank to a stamp on
    // large ones.
    //
    // Scaled @1920×1080 with match 0.5 to line up with every other full-screen
    // panel in the project (DialogueRunner, SettingsScreen, PauseMenu,
    // LevelClearScreen all use exactly this), so they all scale together instead of
    // drifting apart as the window changes.
    void ConfigureCanvas()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        _canvasRect = canvas.rootCanvas.transform as RectTransform;
        if (!autoScaleCanvas) return;

        var sc = canvas.rootCanvas.GetComponent<CanvasScaler>();
        if (sc == null) return;
        sc.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        sc.matchWidthOrHeight  = 0.5f;
    }

    static void EnsureEventSystem()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null) return;
        new GameObject("EventSystem",
            typeof(UnityEngine.EventSystems.EventSystem),
            typeof(UnityEngine.EventSystems.StandaloneInputModule));
    }

    RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    TMP_Text NewText(string name, Transform parent, float size, Color color,
                     FontStyles style, TextAlignmentOptions align, bool wrap)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize  = size;
        t.color     = color;
        t.fontStyle = style;
        t.alignment = align;
        t.raycastTarget = false;
        t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        return t;
    }
}
