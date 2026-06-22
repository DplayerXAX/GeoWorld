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

    [Header("Colors")]
    public Color panelColor      = new Color(0.949f, 0.937f, 0.902f, 0.96f);  // paper
    public Color titleColor      = new Color(0.086f, 0.086f, 0.086f);          // ink
    public Color bodyColor       = new Color(0.30f, 0.30f, 0.30f);
    public Color accentColor     = new Color(0.886f, 0.141f, 0.106f);          // signal
    public Color buttonColor     = new Color(0.086f, 0.086f, 0.086f);
    public Color buttonTextColor = new Color(0.949f, 0.937f, 0.902f);

    [Header("Layout")]
    public float width        = 460f;
    public float rightMargin  = 60f;
    public int   cornerRadius = 26;
    public float fadeSpeed    = 12f;

    CanvasGroup _cg;
    TMP_Text    _title, _status, _best, _desc, _enterLabel;
    Button      _enter;
    Action      _onEnter;
    float       _target;

    void Awake() { BuildUI(); }

    void Update()
    {
        if (_cg == null) return;
        _cg.alpha = Mathf.Lerp(_cg.alpha, _target, 1f - Mathf.Exp(-fadeSpeed * Time.unscaledDeltaTime));
        bool on = _target > 0.5f && _cg.alpha > 0.5f;
        _cg.interactable = _cg.blocksRaycasts = on;
    }

    // ── API (called by LevelMapController) ───────────────────────────────────────
    public void Show(string title, string desc, string status, string best, bool canEnter, Action onEnter)
    {
        _title.text  = title;
        _status.text = status;
        SetOptional(_desc, desc);
        SetOptional(_best, best);
        _enterLabel.text   = canEnter ? "Enter" : "Locked";
        _enter.interactable = canEnter;

        _onEnter = onEnter;
        _target  = 1f;
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
        var canvasGO = new GameObject("LevelInfoCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 80;
        var sc = canvasGO.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 1f;

        EnsureEventSystem();

        var panel = NewRect("Panel", canvasGO.transform);
        panel.anchorMin = panel.anchorMax = new Vector2(1f, 0.5f);   // right-centre
        panel.pivot = new Vector2(1f, 0.5f);
        panel.anchoredPosition = new Vector2(-rightMargin, 0f);
        panel.sizeDelta = new Vector2(width, 100f);
        _cg = panel.gameObject.AddComponent<CanvasGroup>();
        _cg.alpha = 0f;

        var bg = panel.gameObject.AddComponent<Image>();
        bg.color = panelColor;
        bg.sprite = UIRoundedRect.Get(cornerRadius);
        bg.type   = Image.Type.Sliced;

        var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(28, 28, 24, 24);
        vlg.spacing = 12f;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        var fit = panel.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _title  = NewText("Title",  panel, titleSize, titleColor, FontStyles.Bold,   TextAlignmentOptions.TopLeft, false);
        AddRule(panel);
        _status = NewText("Status", panel, bodySize,  accentColor, FontStyles.Bold,  TextAlignmentOptions.TopLeft, false);
        _best   = NewText("Best",   panel, bodySize,  bodyColor,  FontStyles.Normal, TextAlignmentOptions.TopLeft, false);
        _desc   = NewText("Desc",   panel, bodySize,  bodyColor,  FontStyles.Normal, TextAlignmentOptions.TopLeft, true);

        // Enter button.
        var brt = NewRect("Enter", panel);
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
