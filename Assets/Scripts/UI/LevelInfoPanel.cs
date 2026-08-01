using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// Level-select detail panel — self-building UGUI + TMP, no manual wiring.
// Assign to LevelMapController.infoPanel. LevelMapController calls Show / Hide.
public class LevelInfoPanel : MonoBehaviour
{
    [Header("Font (leave null for TMP default)")]
    public TMP_FontAsset font;
    public float titleSize  = 34f;
    public float bodySize   = 20f;
    public float buttonSize = 22f;

    [Header("Use your existing panel (UI Image) — drag its RectTransform here")]
    [Tooltip("If set, lays content into this panel instead of building one.")]
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
    [Tooltip("Max panel size at the 1920×1080 reference; shrinks to fit smaller windows.")]
    public Vector2 panelSize   = new Vector2(420f, 700f);
    public float rightMargin   = 60f;
    public float verticalMargin = 60f;
    [Range(0.2f, 0.9f)] public float maxWidthFraction = 0.5f;
    public Vector2 contentPad  = new Vector2(36f, 40f);
    public int   cornerRadius  = 26;
    public float fadeSpeed     = 12f;

    [Tooltip("Set the hosting Canvas to ScaleWithScreenSize @1920×1080.")]
    public bool autoScaleCanvas = true;

    [Header("Enemy roster")]
    [Tooltip("Only needed for levels with no authored waves (rolls from BalanceTable.enemies).")]
    public BalanceTable balance;
    public float thumbSize = 46f;
    public float tooltipWidth = 340f;

    [Header("Build keepsake")]
    [Tooltip("Cube prefab for the keepsake thumbnail. Leave null to skip it.")]
    public GameObject cubePrefab;
    public float buildThumbHeight = 330f;

    CanvasGroup _cg;
    TMP_Text    _title, _status, _best, _desc, _enterLabel;
    RectTransform _roster;
    Image       _buildThumb;
    TMP_Text    _buildThumbLabel;
    Button      _enter;
    // Read by LevelSelectTutorialGuide to anchor its "click here" arrow beside
    // the Enter button during the ls.enter tutorial gate.
    public RectTransform EnterButtonRect => _enter != null ? (RectTransform)_enter.transform : null;
    // Read by LevelMapController to keep a click that lands on this panel from
    // ALSO raycasting into the 3D map underneath (see PointerOverInfoPanel there).
    // Scoped to this panel's own rect specifically rather than a blanket
    // EventSystem.IsPointerOverGameObject() check — LevelSelect also hosts
    // DialogueRunner's full-screen CanvasGroup, whose blocksRaycasts toggles
    // on/off per dialogue line, so a global "is the pointer over ANY UI" query
    // doesn't reliably isolate "is it over THIS panel".
    public RectTransform PanelRect => _panel != null ? _panel : targetPanel;
    public bool IsShown => _cg != null && _cg.blocksRaycasts;
    Action      _onEnter;
    float       _target;

    RectTransform _panel;       // null when using targetPanel
    RectTransform _canvasRect;

    // Shared tooltip, reused for every mechanic/threat row.
    RectTransform _tooltipRt;
    TMP_Text      _tooltipText;
    RectTransform _tooltipAnchor;   // hovered row; null = hidden

    void Awake() { BuildUI(); }

    void Update()
    {
        ApplyResponsiveLayout();

        if (_cg == null) return;
        _cg.alpha = Mathf.Lerp(_cg.alpha, _target, 1f - Mathf.Exp(-fadeSpeed * Time.unscaledDeltaTime));
        bool on = _target > 0.5f && _cg.alpha > 0.5f;
        _cg.interactable = _cg.blocksRaycasts = on;

        // Re-track every frame so panel resize/fade can't strand the tooltip.
        if (_tooltipAnchor != null)
        {
            if (!on) HideTooltip(_tooltipAnchor);
            else PositionTooltip(_tooltipAnchor);
        }
    }

    // Per-frame so a resizable/free-aspect window keeps the panel fitted.
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

    // `level` is optional — pass it to show the enemy/mechanic roster.
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
        ShowBuildThumb(level);

        _onEnter = onEnter;
        _target  = 1f;
    }

    // The keepsake board from the last time this level was cleared
    // (LevelRecord.buildSnapshot — see GameFlowManager.DoLevelClear).
    void ShowBuildThumb(LevelDefinition lv)
    {
        if (_buildThumb == null) return;

        var rec = lv != null ? SaveSystem.Profile.GetRecord(lv.levelId) : null;
        var snap = rec?.buildSnapshot;
        if (snap == null || snap.blocks == null || snap.blocks.Count == 0 || cubePrefab == null)
        {
            // SetActive, not Image.enabled — a disabled-but-active Image still
            // reserves its LayoutElement height.
            _buildThumb.gameObject.SetActive(false);
            if (_buildThumbLabel != null) _buildThumbLabel.gameObject.SetActive(false);
            return;
        }

        _buildThumb.gameObject.SetActive(true);
        // Keyed on the snapshot timestamp so a re-clear invalidates the old image.
        var sprite = LevelBuildThumbnail.GetOrCreate($"{lv.levelId}_{snap.timestamp}", snap, cubePrefab);
        LevelBuildThumbnail.Apply(_buildThumb, sprite);

        if (_buildThumbLabel != null)
        {
            _buildThumbLabel.gameObject.SetActive(true);
            string when = FormatSnapshotDate(snap.timestamp);
            _buildThumbLabel.text = string.IsNullOrEmpty(when)
                ? $"LAST CLEAR — your build (wave {rec.bestWave})"
                : $"LAST CLEAR — your build, {when}";
        }
    }

    // GridSnapshot.timestamp is "yyyy-MM-dd_HH-mm-ss" — shown as just the date.
    static string FormatSnapshotDate(string stamp)
    {
        if (string.IsNullOrEmpty(stamp)) return "";
        int us = stamp.IndexOf('_');
        return us > 0 ? stamp.Substring(0, us) : "";
    }

    // Rebuilt per selection. Threats are icon-only in a wrapping grid; mechanics
    // show only their title — both show full detail on hover instead of inline.
    void BuildRoster(LevelDefinition lv)
    {
        if (_roster == null) return;

        for (int i = _roster.childCount - 1; i >= 0; i--) Destroy(_roster.GetChild(i).gameObject);
        HideTooltip(_tooltipAnchor);   // destroyed rows don't fire OnPointerExit
        if (lv == null) { _roster.gameObject.SetActive(false); return; }

        var enemies  = LevelRoster.Enemies(lv, balance);
        var specials = LevelRoster.SpecialMechanics(lv);
        if (enemies.Count == 0 && specials.Count == 0) { _roster.gameObject.SetActive(false); return; }

        _roster.gameObject.SetActive(true);

        if (enemies.Count > 0)
        {
            AddHeading(_roster, "THREATS");
            BuildThreatGrid(_roster, enemies);
        }

        if (specials.Count > 0)
        {
            AddHeading(_roster, "MECHANICS");
            foreach (var m in specials) AddMechanicRow(_roster, m);
        }
    }

    void AddHeading(RectTransform parent, string label)
    {
        var t = NewText("Heading", parent, bodySize * 0.8f, accentColor, FontStyles.Bold,
                        TextAlignmentOptions.TopLeft, false);
        t.text = label;
        t.gameObject.AddComponent<LayoutElement>().minHeight = bodySize * 1.3f;
    }

    // Flexible constraint wraps columns to the container width, like flex-wrap.
    void BuildThreatGrid(RectTransform parent, System.Collections.Generic.List<EnemySurfaceUnit> enemies)
    {
        var grid = NewRect("ThreatGrid", parent);
        var glg = grid.gameObject.AddComponent<GridLayoutGroup>();
        glg.cellSize   = new Vector2(thumbSize, thumbSize);
        glg.spacing    = new Vector2(8f, 8f);
        glg.startAxis  = GridLayoutGroup.Axis.Horizontal;
        glg.constraint = GridLayoutGroup.Constraint.Flexible;
        glg.childAlignment = TextAnchor.UpperLeft;
        grid.gameObject.AddComponent<LayoutElement>();   // lets the VerticalLayoutGroup above measure it
        grid.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        foreach (var e in enemies) AddThreatIcon(grid, e);
    }

    void AddThreatIcon(RectTransform parent, EnemySurfaceUnit prefab)
    {
        var cell = NewRect("Threat", parent);

        // Own hit box, separate from the portrait Image (which starts disabled
        // until EnemyThumbnail's render lands) so hover works immediately.
        var hit = MakeHoverFrame(cell.gameObject, Mathf.Max(4, (int)(thumbSize * 0.14f)));

        var iconRt = NewRect("Icon", cell);
        StretchInto(iconRt, 5f, 5f);
        var icon = iconRt.gameObject.AddComponent<Image>();
        icon.color = new Color(1f, 1f, 1f, 0f);
        icon.enabled = false;
        icon.raycastTarget = false;
        EnemyThumbnail.Request(prefab, icon);

        string body = $"<b>{prefab.name}</b>  <size=90%><color=#9A9A9A>{prefab.maxHealth} HP</color></size>\n" +
                      $"<size=90%><color=#9A9A9A>{EnemyDossier.Mechanic(prefab)}</color></size>";
        var trigger = cell.gameObject.AddComponent<TooltipTrigger>();
        trigger.Init(this, body, hit);
    }

    void AddMechanicRow(RectTransform parent, LevelRoster.MechanicEntry m)
    {
        var row = NewRect("Mechanic", parent);
        var hit = MakeHoverFrame(row.gameObject, 6);
        row.gameObject.AddComponent<LayoutElement>().minHeight = bodySize * 1.1f;

        var t = NewText("Title", row, bodySize * 0.85f, bodyColor, FontStyles.Bold,
                        TextAlignmentOptions.Left, false);
        t.text = "• " + m.title;
        StretchInto(t.rectTransform, 10f, 0f);

        var trigger = row.gameObject.AddComponent<TooltipTrigger>();
        trigger.Init(this, m.description, hit);
    }

    // Hollow border marking a row/icon as hoverable; also doubles as its hit box.
    Image MakeHoverFrame(GameObject go, int radius)
    {
        var img = go.AddComponent<Image>();
        img.sprite = UIRoundedRect.GetFrame(radius, 2);
        img.type   = Image.Type.Sliced;
        img.color  = new Color(accentColor.r, accentColor.g, accentColor.b, 0.35f);
        return img;
    }

    // ── Hover tooltip ────────────────────────────────────────────────────────

    // Tells the shared tooltip what to show/where, and brightens its own frame.
    class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        LevelInfoPanel _panel;
        string _text;
        Image  _frame;
        Color  _restColor;

        public void Init(LevelInfoPanel panel, string text, Image frame)
        {
            _panel = panel; _text = text; _frame = frame;
            if (_frame != null) _restColor = _frame.color;
        }

        public void OnPointerEnter(PointerEventData e)
        {
            _panel.ShowTooltip((RectTransform)transform, _text);
            if (_frame != null) _frame.color = new Color(_restColor.r, _restColor.g, _restColor.b, 1f);
        }

        public void OnPointerExit(PointerEventData e)
        {
            _panel.HideTooltip((RectTransform)transform);
            if (_frame != null) _frame.color = _restColor;
        }
    }

    void ShowTooltip(RectTransform anchor, string text)
    {
        if (_tooltipRt == null || anchor == null) return;
        _tooltipAnchor = anchor;
        _tooltipText.text = text;
        _tooltipRt.gameObject.SetActive(true);
        PositionTooltip(anchor);
    }

    // `which` guards a stale exit/enter race: only clears if it's still the
    // tooltip's current owner, so a late exit can't yank a newer hover's tooltip.
    void HideTooltip(RectTransform which)
    {
        if (_tooltipRt == null || which == null || _tooltipAnchor != which) return;
        _tooltipAnchor = null;
        _tooltipRt.gameObject.SetActive(false);
    }

    // X hugs the panel's left edge (fixed, regardless of which row/icon is
    // hovered); Y follows the hovered row's centre.
    void PositionTooltip(RectTransform anchor)
    {
        if (_tooltipRt == null || anchor == null) return;
        var tooltipParent = (RectTransform)_tooltipRt.parent;
        var canvas = tooltipParent.GetComponentInParent<Canvas>();
        var cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

        var panelRect = _panel != null ? _panel : targetPanel;
        RectTransform xSource = panelRect != null ? panelRect : anchor;
        var xr = xSource.rect;
        Vector3 worldLeft = xSource.TransformPoint(new Vector3(xr.xMin, 0f, 0f));

        var ar = anchor.rect;
        Vector3 worldMidY = anchor.TransformPoint(new Vector3(0f, (ar.yMin + ar.yMax) * 0.5f, 0f));

        // Screen space, not world, so scale/rotation differences between the
        // panel and the row can't skew the mix.
        Vector2 screenLeft = RectTransformUtility.WorldToScreenPoint(cam, worldLeft);
        Vector2 screenMidY = RectTransformUtility.WorldToScreenPoint(cam, worldMidY);
        Vector2 screenPt   = new Vector2(screenLeft.x - 10f, screenMidY.y);

        // World position, not anchoredPosition — anchoredPosition offsets from the
        // tooltip's own anchor point, but ScreenPointToLocalPointInRectangle
        // returns a point relative to the parent's pivot; those don't match unless
        // the pivot happens to sit at the anchor, which it doesn't here.
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(tooltipParent, screenPt, cam, out var worldPt))
            _tooltipRt.position = worldPt;
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
        BuildTooltip();
    }

    // Parented to this component's own root (outside the panel's layout group)
    // so it can float free and draw on top; built last for draw order.
    void BuildTooltip()
    {
        var rt = NewRect("Tooltip", transform);
        _tooltipRt = rt;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);   // grows left from its anchor
        rt.sizeDelta = new Vector2(tooltipWidth, 0f);

        var bg = rt.gameObject.AddComponent<Image>();
        bg.sprite = UIRoundedRect.Get(Mathf.Max(4, cornerRadius / 2));
        bg.type = Image.Type.Sliced;
        bg.color = panelColor;
        bg.raycastTarget = false;

        var vlg = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(18, 18, 14, 14);
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        rt.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _tooltipText = NewText("Text", rt, bodySize, bodyColor, FontStyles.Normal,
                               TextAlignmentOptions.TopLeft, true);
        _tooltipText.raycastTarget = false;

        rt.gameObject.SetActive(false);
    }

    static void StretchInto(RectTransform rt, float padX, float padY)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padX, padY); rt.offsetMax = new Vector2(-padX, -padY);
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

        // Keepsake caption + thumbnail; ShowBuildThumb() turns them on if cleared.
        _buildThumbLabel = NewText("BuildThumbLabel", parent, bodySize * 0.8f, accentColor,
                                   FontStyles.Bold, TextAlignmentOptions.TopLeft, false);
        _buildThumbLabel.gameObject.AddComponent<LayoutElement>().minHeight = bodySize * 1.3f;
        _buildThumbLabel.gameObject.SetActive(false);

        var thumbRt = NewRect("BuildThumb", parent);
        _buildThumb = thumbRt.gameObject.AddComponent<Image>();
        thumbRt.gameObject.AddComponent<LayoutElement>().preferredHeight = buildThumbHeight;
        thumbRt.gameObject.SetActive(false);

        _desc   = NewText("Desc",   parent, bodySize,  bodyColor,  FontStyles.Normal, TextAlignmentOptions.TopLeft, true);

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

    // Matches every other full-screen panel's scaler (DialogueRunner, SettingsScreen,
    // PauseMenu, LevelClearScreen), so they all scale together.
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
