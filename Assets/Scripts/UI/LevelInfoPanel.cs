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
    [Tooltip("Fixed panel size (match your right-panel art). x = width, y = height.")]
    public Vector2 panelSize   = new Vector2(420f, 700f);
    public float rightMargin   = 60f;
    public Vector2 contentPad  = new Vector2(36f, 40f);   // inner padding (x sides, y top/bottom)
    public int   cornerRadius  = 26;
    public float fadeSpeed     = 12f;

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
        EnsureEventSystem();
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

            panel.anchorMin = panel.anchorMax = new Vector2(1f, 0.5f);
            panel.pivot = new Vector2(1f, 0.5f);
            panel.anchoredPosition = new Vector2(-rightMargin, 0f);
            panel.sizeDelta = panelSize;

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
