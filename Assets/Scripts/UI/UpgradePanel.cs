using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// World-space (3D) UPGRADE panel — a SEPARATE panel from BlockInfoPanel. Shows the
// selected turret's two upgrade paths as buttons. Built at runtime; floats beside the
// block and billboards to the camera. PlacementController drives it via Show / Hide.
//
// Put this on an empty GameObject and assign it to PlacementController.upgradePanel.
public class UpgradePanel : MonoBehaviour
{
    [Header("Font (TextMeshPro — leave null for TMP default)")]
    public TMP_FontAsset font;
    public float titleSize  = 20f;
    public float buttonSize = 16f;

    [Header("Colors")]
    public Color panelColor      = new Color(0.949f, 0.937f, 0.902f, 0.92f);  // paper
    public Color titleColor      = new Color(0.086f, 0.086f, 0.086f);          // ink
    public Color buttonColor     = new Color(0.169f, 0.424f, 0.690f);          // blue
    public Color buttonTextColor = new Color(0.949f, 0.937f, 0.902f);

    [Header("World placement")]
    public float worldWidth   = 3.4f;
    [Tooltip("World units above the block.")]
    public float heightOffset = 1.4f;
    [Tooltip("Sideways offset along the camera's right (positive = right of the block, opposite the selection panel).")]
    public float lateralOffset = 2.0f;
    [Tooltip("Pull toward the camera (world units). Higher = floats more in front.")]
    public float cameraPull   = 2.5f;
    public bool  faceCamera   = true;
    public float fadeSpeed    = 14f;
    [Tooltip("Also force-draw over geometry.")]
    public bool  renderOnTop  = true;

    const float DesignWidth = 320f;

    [Header("Gated hint (shown instead of the buttons — see Show(hint))")]
    public Color hintColor = new Color(0.55f, 0.42f, 0.10f);

    Camera        _cam;
    Canvas        _canvas;
    CanvasGroup   _cg;
    Button        _aButton, _bButton;
    TMP_Text      _aLabel, _bLabel;
    TMP_Text      _hintLabel;
    Action        _onA, _onB;
    Vector3       _anchor;
    float         _target;
    bool          _justShown;

    void Awake() => BuildUI();

    void LateUpdate()
    {
        if (_canvas == null) return;
        if (_cam == null) { _cam = Camera.main; if (_canvas) _canvas.worldCamera = _cam; }

        _cg.alpha = Mathf.Lerp(_cg.alpha, _target, 1f - Mathf.Exp(-fadeSpeed * Time.unscaledDeltaTime));
        bool on = _target > 0.5f && _cg.alpha > 0.5f;
        _cg.interactable = _cg.blocksRaycasts = on;

        // Holding the middle mouse button temporarily ducks the panel — see BlockInfoPanel.
        bool middleHeld = Input.GetMouseButton(2);
        _canvas.enabled = _cg.alpha > 0.01f && !middleHeld;
        if (!_canvas.enabled || _cam == null) return;

        transform.position = BlockInfoPanel.StableSpot(_cam, _anchor, heightOffset, lateralOffset, cameraPull,
                                                       transform.position, _justShown);
        _justShown = false;
        if (faceCamera)
            transform.rotation = Quaternion.LookRotation(transform.position - _cam.transform.position, Vector3.up);
    }

    // ── Public API ───────────────────────────────────────────────────────────────

    // `hint`: when non-empty, REPLACES the two upgrade buttons with a single
    // message line (e.g. "Activate/Upgrade Enlightenment synergy to further
    // upgrade turret!") — for when upgrading is blocked for a reason the
    // player needs to go DO something about elsewhere, not just "wait"/"maxed".
    // Leave null/empty for the normal button behavior.
    public void Show(Vector3 worldPos,
                     string aText, bool canA, Action onA,
                     string bText, bool canB, Action onB,
                     string hint = null)
    {
        if (_target < 0.5f) _justShown = true;
        _anchor = worldPos;

        bool showHint = !string.IsNullOrEmpty(hint);
        _hintLabel.gameObject.SetActive(showHint);
        if (showHint) _hintLabel.text = hint;

        _aButton.gameObject.SetActive(!showHint);
        _bButton.gameObject.SetActive(!showHint);
        if (!showHint)
        {
            SetupButton(_aButton, _aLabel, aText, canA);
            SetupButton(_bButton, _bLabel, bText, canB);
        }
        _onA = onA;
        _onB = onB;
        _target = 1f;
    }

    public void Hide() { _target = 0f; _onA = _onB = null; }

    static void SetupButton(Button btn, TMP_Text label, string text, bool can)
    {
        bool has = !string.IsNullOrEmpty(text);
        btn.gameObject.SetActive(has);
        if (!has) return;
        label.text = text;
        btn.interactable = can;
    }

    // ── Build (runtime) ──────────────────────────────────────────────────────────

    void BuildUI()
    {
        _cam = Camera.main;

        var canvasGO = new GameObject("UpgradeCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        _canvas = canvasGO.GetComponent<Canvas>();
        _canvas.renderMode  = RenderMode.WorldSpace;
        _canvas.worldCamera = _cam;
        _canvas.sortingOrder = 100;

        var crt = (RectTransform)canvasGO.transform;
        crt.sizeDelta = new Vector2(DesignWidth, 600f);
        float scale = worldWidth / DesignWidth;
        crt.localScale = new Vector3(scale, scale, scale);
        crt.localPosition = Vector3.zero;

        BlockInfoPanel.EnsureEventSystem();

        var panel = NewRect("Panel", crt);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(DesignWidth, 100f);
        _cg = panel.gameObject.AddComponent<CanvasGroup>();
        _cg.alpha = 0f;

        var bg = panel.gameObject.AddComponent<Image>();
        bg.color = panelColor;

        var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(14, 14, 12, 12);
        vlg.spacing = 8f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var title = NewText("Title", panel, titleSize, titleColor, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        title.text = "UPGRADES";

        _hintLabel = NewText("Hint", panel, buttonSize, hintColor, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        _hintLabel.textWrappingMode = TextWrappingModes.Normal;
        _hintLabel.gameObject.SetActive(false);

        _aButton = NewButton(panel, "A", out _aLabel);
        _aButton.onClick.AddListener(() => _onA?.Invoke());
        _bButton = NewButton(panel, "B", out _bLabel);
        _bButton.onClick.AddListener(() => _onB?.Invoke());

        if (renderOnTop) BlockInfoPanel.ApplyRenderOnTop(gameObject);
    }

    Button NewButton(RectTransform parent, string label, out TMP_Text labelText)
    {
        var rt = NewRect("Button", parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = buttonColor;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        rt.gameObject.AddComponent<LayoutElement>().minHeight = buttonSize * 2.2f;

        labelText = NewText("Label", rt, buttonSize, buttonTextColor, FontStyles.Bold, TextAlignmentOptions.Center);
        labelText.text = label;
        labelText.textWrappingMode = TextWrappingModes.Normal;
        var lrt = labelText.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(8f, 4f); lrt.offsetMax = new Vector2(-8f, -4f);
        return btn;
    }

    RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    TMP_Text NewText(string name, Transform parent, float size, Color color,
                     FontStyles style, TextAlignmentOptions align)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize  = size;
        t.color     = color;
        t.fontStyle = style;
        t.alignment = align;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }
}
