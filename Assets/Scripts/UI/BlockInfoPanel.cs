using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

// World-space (3D) SELECTION panel for placed blocks / turrets / the spawn point.
// Built entirely at runtime as UGUI: a World-Space Canvas that floats above the
// selected block and billboards toward the camera. PlacementController drives it via
// Show / ShowReadonly / Hide (each call carries the block's world position).
//
// Upgrades live in a SEPARATE panel (UpgradePanel) — this one is title + stats +
// Pick up / Sell only. Put this component on an empty GameObject and assign it to
// PlacementController.infoPanel.
public class BlockInfoPanel : MonoBehaviour
{
    [Header("Font (TextMeshPro — leave null for TMP default)")]
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
    [Tooltip("World units above the block.")]
    public float heightOffset = 1.4f;
    [Tooltip("Sideways offset along the camera's right (negative = left of the block). Selection = left, upgrade = right.")]
    public float lateralOffset = -1.8f;
    [Tooltip("Pull toward the camera (world units). Higher = floats more in front of the blocks (more 'depth') and reads bigger.")]
    public float cameraPull = 2.5f;
    public bool  faceCamera = true;
    public float fadeSpeed = 14f;
    [Tooltip("Also force-draw over geometry (belt-and-suspenders with cameraPull).")]
    public bool  renderOnTop = true;

    const float DesignWidth = 320f;   // internal px width before world scaling

    Camera        _cam;
    Canvas        _canvas;
    CanvasGroup   _cg;
    RectTransform _panel;
    TMP_Text      _titleText, _bodyText, _lockedNote, _sellLabel, _shiftHint;
    Button        _pickUpButton, _sellButton;
    Action        _onPickUp, _onSell;
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

        // Holding the middle mouse button temporarily ducks the panel so it doesn't block
        // the view of the block/scene behind it — instant hide/show, no fade, so it never
        // fights the alpha animation driven by _target.
        bool middleHeld = Input.GetMouseButton(2);
        _canvas.enabled = _cg.alpha > 0.01f && !middleHeld;
        if (!_canvas.enabled || _cam == null) return;

        transform.position = StableSpot(_cam, _anchor, heightOffset, lateralOffset, cameraPull,
                                        transform.position, _justShown);
        _justShown = false;
        if (faceCamera)
            transform.rotation = Quaternion.LookRotation(transform.position - _cam.transform.position, Vector3.up);
    }

    // Stable world spot: fixed offset above + beside the block, then pulled toward the
    // camera so it floats clearly in front (the "depth"). Smoothed so orbiting the
    // camera glides instead of jumping. Snaps on the first frame after a new selection.
    internal static Vector3 StableSpot(Camera cam, Vector3 anchor, float up, float lateral,
                                       float pull, Vector3 current, bool snap)
    {
        Vector3 camPos = cam.transform.position;
        Vector3 target = anchor + Vector3.up * up + cam.transform.right * lateral;
        target += (camPos - target).normalized * pull;     // bring toward the camera
        return snap ? target
                    : Vector3.Lerp(current, target, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
    }

    // ── Public API (called by PlacementController) ───────────────────────────────

    public void Show(Vector3 worldPos, string title, string body, bool canEdit,
                     string sellText, Action onPickUp, Action onSell, string lockedReason = "Locked during combat")
    {
        _anchor = worldPos;
        _titleText.text = title;
        _bodyText.text  = body;
        _sellLabel.text = sellText;

        _pickUpButton.gameObject.SetActive(true);
        _sellButton.gameObject.SetActive(true);
        _pickUpButton.interactable = canEdit;
        _sellButton.interactable   = canEdit;
        _lockedNote.text = lockedReason;
        _lockedNote.gameObject.SetActive(!canEdit);
        _shiftHint.gameObject.SetActive(true);   // only this (editable) panel actually responds to Shift — see PlacementController.UpdateInfoPanel

        if (_target < 0.5f)
        {
            _justShown = true;   // snap into place on a fresh selection
            // Gamepad: give Navigate/Submit a starting focus the instant the panel opens.
            var focus = canEdit ? _pickUpButton : _sellButton;
            if (focus != null && focus.gameObject.activeInHierarchy)
                EventSystem.current?.SetSelectedGameObject(focus.gameObject);
        }
        _onPickUp = onPickUp;
        _onSell   = onSell;
        _target   = 1f;
    }

    public void ShowReadonly(Vector3 worldPos, string title, string body)
    {
        if (_target < 0.5f) _justShown = true;
        _anchor = worldPos;
        _titleText.text = title;
        _bodyText.text  = body;
        _pickUpButton.gameObject.SetActive(false);
        _sellButton.gameObject.SetActive(false);
        _lockedNote.gameObject.SetActive(false);
        _shiftHint.gameObject.SetActive(false);   // readonly panels (spawn/chaos/shrine) don't hide on Shift

        _onPickUp = _onSell = null;
        _target   = 1f;
    }

    public void Hide()
    {
        _target = 0f; _onPickUp = _onSell = null;
        // Drop focus if it was one of our buttons, so a stale selection doesn't
        // linger into whatever's shown next.
        var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (sel == _pickUpButton?.gameObject || sel == _sellButton?.gameObject)
            EventSystem.current.SetSelectedGameObject(null);
    }

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
        _canvas.sortingOrder = 100;

        var crt = (RectTransform)canvasGO.transform;
        crt.sizeDelta = new Vector2(DesignWidth, 600f);
        float scale = worldWidth / DesignWidth;
        crt.localScale = new Vector3(scale, scale, scale);
        crt.localPosition = Vector3.zero;

        EnsureEventSystem();

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

        // Small print — the Shift-peek this panel responds to (see
        // PlacementController.UpdateInfoPanel) isn't otherwise discoverable. Last
        // row of the vertical layout and right-aligned, so it sits in the panel's
        // bottom-right corner: present enough to be found, tucked far enough out of
        // the reading path that it doesn't compete with the title/body/buttons.
        _shiftHint = NewText("ShiftHint", _panel, bodySize * 0.7f,
            new Color(bodyColor.r, bodyColor.g, bodyColor.b, 0.55f),
            FontStyles.Italic, TextAlignmentOptions.BottomRight);
        _shiftHint.text = "Long press Left Shift to hide it temporarily";
        _shiftHint.textWrappingMode = TextWrappingModes.Normal;

        if (renderOnTop) ApplyRenderOnTop(gameObject);
    }

    // Force every UGUI graphic under `root` to draw over 3D geometry (ZTest Always),
    // so world-space panels aren't occluded by the blocks they float above.
    internal static void ApplyRenderOnTop(GameObject root)
    {
        int always = (int)UnityEngine.Rendering.CompareFunction.Always;
        foreach (var g in root.GetComponentsInChildren<Graphic>(true))
        {
            var m = new Material(g.materialForRendering);
            if (m.HasProperty("unity_GUIZTestMode")) m.SetInt("unity_GUIZTestMode", always);
            if (m.HasProperty("_ZTestMode"))         m.SetInt("_ZTestMode", always);
            g.material = m;
        }
    }

    internal static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        new GameObject("EventSystem",
            typeof(EventSystem),
            typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
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
}
