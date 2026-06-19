using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// World-space (3D) selection panel for placed blocks / turrets / the spawn point.
// Built entirely at runtime as UGUI: a World-Space Canvas that floats above the
// selected block and billboards toward the camera. PlacementController drives it via
// Show / ShowReadonly / Hide (each call carries the block's world position).
//
// Just put this component on an empty GameObject and assign it to
// PlacementController.infoPanel. Font / sizes / colours are fields below.
public class BlockInfoPanel : MonoBehaviour
{
<<<<<<< Updated upstream
    [Header("Texts (TextMeshPro)")]
    public TMP_Text titleText;
    public TMP_Text bodyText;     // multiline stats; rich-text enabled
    public TMP_Text lockedNote;   // optional: "Locked during combat"

    [Header("Buttons")]
    public Button   pickUpButton;
    public Button   sellButton;
    public Button   upgradeAButton;
    public Button   upgradeBButton;
    public TMP_Text sellLabel;    // the Sell button's label (shows "Sell +N")
    public TMP_Text upgradeALabel;
    public TMP_Text upgradeBLabel;

    [Header("Font (optional — applied to every text above at Awake)")]
=======
    [Header("Font (TextMeshPro — leave null for TMP default)")]
>>>>>>> Stashed changes
    public TMP_FontAsset font;
    public float titleSize  = 26f;
    public float bodySize   = 18f;
    public float buttonSize = 18f;

    [Header("Colors")]
    public Color panelColor = new Color(0.949f, 0.937f, 0.902f, 0.92f);  // paper
    public Color titleColor = new Color(0.086f, 0.086f, 0.086f);          // ink
    public Color bodyColor  = new Color(0.086f, 0.086f, 0.086f);
    public Color buttonColor = new Color(0.086f, 0.086f, 0.086f, 1f);
    public Color buttonTextColor = new Color(0.949f, 0.937f, 0.902f);
    public Color sellColor  = new Color(0.886f, 0.141f, 0.106f);          // signal red

    [Header("World placement")]
    [Tooltip("How wide the panel is in WORLD units.")]
    public float worldWidth = 3.2f;
    [Tooltip("Metres above the block's pivot to float the panel.")]
    public float heightOffset = 1.6f;
    public bool  faceCamera = true;
    public float fadeSpeed = 14f;

<<<<<<< Updated upstream
    CanvasGroup _cg;
    Action      _onPickUp, _onSell, _onUpgradeA, _onUpgradeB;
    float       _target;
=======
    const float DesignWidth = 320f;   // internal px width before world scaling
>>>>>>> Stashed changes

    Camera        _cam;
    Canvas        _canvas;
    CanvasGroup   _cg;
    RectTransform _panel;
    TMP_Text      _titleText, _bodyText, _lockedNote, _sellLabel;
    Button        _pickUpButton, _sellButton;
    Action        _onPickUp, _onSell;
    Vector3       _anchor;
    float         _target;

    void Awake() => BuildUI();

    void LateUpdate()
    {
        if (_canvas == null) return;
        if (_cam == null) { _cam = Camera.main; if (_canvas) _canvas.worldCamera = _cam; }

<<<<<<< Updated upstream
        EnsureUpgradeButtons();

        if (font != null)
            foreach (var t in new[] { titleText, bodyText, lockedNote, sellLabel, upgradeALabel, upgradeBLabel })
                if (t != null) t.font = font;

        if (pickUpButton != null) pickUpButton.onClick.AddListener(() => _onPickUp?.Invoke());
        if (sellButton   != null) sellButton.onClick.AddListener(() => _onSell?.Invoke());
        if (upgradeAButton != null) upgradeAButton.onClick.AddListener(() => _onUpgradeA?.Invoke());
        if (upgradeBButton != null) upgradeBButton.onClick.AddListener(() => _onUpgradeB?.Invoke());

        _cg.alpha = 0f;
        _cg.interactable = _cg.blocksRaycasts = false;
    }

    void EnsureUpgradeButtons()
    {
        if (pickUpButton == null) return;

        if (upgradeAButton == null)
            upgradeAButton = CreateRuntimeUpgradeButton("Upgrade A Button", pickUpButton.transform.GetSiblingIndex() + 1);
        if (upgradeBButton == null)
            upgradeBButton = CreateRuntimeUpgradeButton("Upgrade B Button", pickUpButton.transform.GetSiblingIndex() + 2);

        if (upgradeALabel == null && upgradeAButton != null)
            upgradeALabel = upgradeAButton.GetComponentInChildren<TMP_Text>(true);
        if (upgradeBLabel == null && upgradeBButton != null)
            upgradeBLabel = upgradeBButton.GetComponentInChildren<TMP_Text>(true);
    }

    Button CreateRuntimeUpgradeButton(string objectName, int siblingIndex)
    {
        var button = Instantiate(pickUpButton, pickUpButton.transform.parent);
        button.name = objectName;
        button.transform.SetSiblingIndex(siblingIndex);
        button.onClick.RemoveAllListeners();
        button.gameObject.SetActive(false);
        return button;
    }

    void Update()
    {
=======
>>>>>>> Stashed changes
        _cg.alpha = Mathf.Lerp(_cg.alpha, _target, 1f - Mathf.Exp(-fadeSpeed * Time.unscaledDeltaTime));
        bool on = _target > 0.5f && _cg.alpha > 0.5f;
        _cg.interactable = _cg.blocksRaycasts = on;
        _canvas.enabled = _cg.alpha > 0.01f;
        if (!_canvas.enabled) return;

        transform.position = _anchor + Vector3.up * heightOffset;
        if (faceCamera && _cam != null)
            transform.rotation = Quaternion.LookRotation(transform.position - _cam.transform.position, Vector3.up);
    }

<<<<<<< Updated upstream
    // Editable block / turret: title + stats + Pick up / Sell.
    public void Show(
        string title,
        string body,
        bool canEdit,
        string sellText,
        Action onPickUp,
        Action onSell,
        string upgradeAText = null,
        bool canUpgradeA = false,
        Action onUpgradeA = null,
        string upgradeBText = null,
        bool canUpgradeB = false,
        Action onUpgradeB = null)
    {
        if (titleText) titleText.text = title;
        if (bodyText)  bodyText.text  = body;
        if (sellLabel) sellLabel.text = sellText;

        if (pickUpButton) { pickUpButton.gameObject.SetActive(true); pickUpButton.interactable = canEdit; }
        if (sellButton)   { sellButton.gameObject.SetActive(true);   sellButton.interactable   = canEdit; }
        SetupUpgradeButton(upgradeAButton, upgradeALabel, upgradeAText, canUpgradeA);
        SetupUpgradeButton(upgradeBButton, upgradeBLabel, upgradeBText, canUpgradeB);
        if (lockedNote)   lockedNote.gameObject.SetActive(!canEdit);
=======
    // ── Public API (called by PlacementController) ───────────────────────────────

    public void Show(Vector3 worldPos, string title, string body, bool canEdit,
                     string sellText, Action onPickUp, Action onSell)
    {
        _anchor = worldPos;
        _titleText.text = title;
        _bodyText.text  = body;
        _sellLabel.text = sellText;

        _pickUpButton.gameObject.SetActive(true);
        _sellButton.gameObject.SetActive(true);
        _pickUpButton.interactable = canEdit;
        _sellButton.interactable   = canEdit;
        _lockedNote.gameObject.SetActive(!canEdit);
>>>>>>> Stashed changes

        _onPickUp = onPickUp;
        _onSell   = onSell;
        _onUpgradeA = onUpgradeA;
        _onUpgradeB = onUpgradeB;
        _target   = 1f;
    }

    public void ShowReadonly(Vector3 worldPos, string title, string body)
    {
<<<<<<< Updated upstream
        if (titleText) titleText.text = title;
        if (bodyText)  bodyText.text  = body;
        if (pickUpButton) pickUpButton.gameObject.SetActive(false);
        if (sellButton)   sellButton.gameObject.SetActive(false);
        if (upgradeAButton) upgradeAButton.gameObject.SetActive(false);
        if (upgradeBButton) upgradeBButton.gameObject.SetActive(false);
        if (lockedNote)   lockedNote.gameObject.SetActive(false);
=======
        _anchor = worldPos;
        _titleText.text = title;
        _bodyText.text  = body;
        _pickUpButton.gameObject.SetActive(false);
        _sellButton.gameObject.SetActive(false);
        _lockedNote.gameObject.SetActive(false);
>>>>>>> Stashed changes

        _onPickUp = _onSell = _onUpgradeA = _onUpgradeB = null;
        _target   = 1f;
    }

<<<<<<< Updated upstream
    void SetupUpgradeButton(Button button, TMP_Text label, string text, bool canUpgrade)
    {
        if (button == null) return;

        bool show = !string.IsNullOrEmpty(text);
        button.gameObject.SetActive(show);
        button.interactable = show && canUpgrade;
        if (label != null) label.text = text;
    }

    public void Hide() { _target = 0f; _onPickUp = _onSell = _onUpgradeA = _onUpgradeB = null; }
=======
    public void Hide() { _target = 0f; _onPickUp = _onSell = null; }

    // ── Build (runtime) ──────────────────────────────────────────────────────────

    void BuildUI()
    {
        _cam = Camera.main;

        var canvasGO = new GameObject("BlockInfoCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        _canvas = canvasGO.GetComponent<Canvas>();
        _canvas.renderMode  = RenderMode.WorldSpace;
        _canvas.worldCamera = _cam;
        _canvas.sortingOrder = 50;

        var crt = (RectTransform)canvasGO.transform;
        crt.sizeDelta = new Vector2(DesignWidth, 600f);
        float scale = worldWidth / DesignWidth;
        crt.localScale = new Vector3(scale, scale, scale);
        crt.localPosition = Vector3.zero;

        EnsureEventSystem();

        // Panel (auto-sizes to content, centred on the canvas).
        _panel = NewRect("Panel", crt);
        _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(0.5f, 0.5f);
        _panel.sizeDelta = new Vector2(DesignWidth, 100f);
        _cg = _panel.gameObject.AddComponent<CanvasGroup>();
        _cg.alpha = 0f;

        var bg = _panel.gameObject.AddComponent<Image>();
        bg.color = panelColor;

        var vlg = _panel.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(16, 16, 14, 14);
        vlg.spacing = 8f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        var fitter = _panel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _titleText = NewText("Title", _panel, titleSize, titleColor, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        _bodyText  = NewText("Body",  _panel, bodySize,  bodyColor,  FontStyles.Normal, TextAlignmentOptions.TopLeft);
        _bodyText.textWrappingMode = TextWrappingModes.Normal;

        _lockedNote = NewText("Locked", _panel, bodySize, sellColor, FontStyles.Italic, TextAlignmentOptions.TopLeft);
        _lockedNote.text = "Locked during combat";
        _lockedNote.gameObject.SetActive(false);

        // Buttons row.
        var row = NewRect("Buttons", _panel);
        var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10f; hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = false;
        row.gameObject.AddComponent<LayoutElement>().minHeight = buttonSize * 2.2f;

        _pickUpButton = NewButton(row, "Pick up", buttonColor, buttonTextColor, out _);
        _pickUpButton.onClick.AddListener(() => _onPickUp?.Invoke());
        _sellButton   = NewButton(row, "Sell",    sellColor,   buttonTextColor, out _sellLabel);
        _sellButton.onClick.AddListener(() => _onSell?.Invoke());
    }

    static void EnsureEventSystem()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null) return;
        new GameObject("EventSystem",
            typeof(UnityEngine.EventSystems.EventSystem),
            typeof(UnityEngine.EventSystems.StandaloneInputModule));
    }

    Button NewButton(RectTransform parent, string label, Color bgColor, Color textColor, out TMP_Text labelText)
    {
        var rt = NewRect("Button", parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = bgColor;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        rt.gameObject.AddComponent<LayoutElement>().minHeight = buttonSize * 2f;

        labelText = NewText("Label", rt, buttonSize, textColor, FontStyles.Bold, TextAlignmentOptions.Center);
        labelText.text = label;
        var lrt = labelText.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
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
>>>>>>> Stashed changes
}
