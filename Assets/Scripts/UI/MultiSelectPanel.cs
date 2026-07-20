using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Floating action panel for a box-select ("N blocks selected" + Move as a
// whole / Sell all). Same house style AND same construction as BlockInfoPanel
// (world-space canvas that billboards toward the camera, flat un-rounded
// paper panel, ink/signal buttons) — floats above the centroid of the
// selected blocks instead of a single block's position. Runtime-built, no
// scene setup, single shared instance reused across shows (there's only
// ever one multi-selection live at a time).
public static class MultiSelectPanel
{
    const float DesignWidth   = 320f;   // internal px width before world scaling, matches BlockInfoPanel
    const float WorldWidth    = 3.2f;
    const float HeightOffset  = 1.6f;
    const float CameraPull    = 2.5f;
    const float FadeSpeed     = 14f;

    static GameObject    _root;
    static Canvas        _canvas;
    static CanvasGroup   _cg;
    static RectTransform _panel;
    static TMP_Text      _title;
    static Button        _moveButton, _sellButton;
    static Camera        _cam;
    static Vector3       _anchor;
    static float         _target;
    static bool          _justShown;

    static Action _onMove, _onSell;

    class Driver : MonoBehaviour
    {
        void LateUpdate()
        {
            if (_canvas == null) return;
            if (_cam == null) { _cam = Camera.main; if (_canvas != null) _canvas.worldCamera = _cam; }

            _cg.alpha = Mathf.Lerp(_cg.alpha, _target, 1f - Mathf.Exp(-FadeSpeed * Time.unscaledDeltaTime));
            bool on = _target > 0.5f && _cg.alpha > 0.5f;
            _cg.interactable = _cg.blocksRaycasts = on;

            _canvas.enabled = _cg.alpha > 0.01f;
            if (!_canvas.enabled || _cam == null) return;

            _root.transform.position = BlockInfoPanel.StableSpot(_cam, _anchor, HeightOffset, 0f, CameraPull,
                                                                  _root.transform.position, _justShown);
            _justShown = false;
            _root.transform.rotation = Quaternion.LookRotation(_root.transform.position - _cam.transform.position, Vector3.up);
        }
    }

    static void Ensure()
    {
        if (_root != null) return;

        _cam  = Camera.main;
        _root = new GameObject("MultiSelectPanel", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        UnityEngine.Object.DontDestroyOnLoad(_root);
        _canvas = _root.GetComponent<Canvas>();
        _canvas.renderMode   = RenderMode.WorldSpace;
        _canvas.worldCamera  = _cam;
        _canvas.sortingOrder = 100;

        var crt = (RectTransform)_root.transform;
        crt.sizeDelta = new Vector2(DesignWidth, 600f);
        float scale = WorldWidth / DesignWidth;
        crt.localScale = new Vector3(scale, scale, scale);

        BlockInfoPanel.EnsureEventSystem();

        _panel = NewRect("Panel", crt);
        _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(0.5f, 0.5f);
        _panel.sizeDelta = new Vector2(DesignWidth, 100f);
        _cg = _panel.gameObject.AddComponent<CanvasGroup>();
        _cg.alpha = 0f;

        // House style (matches BlockInfoPanel exactly): flat, un-rounded paper
        // panel, ink title, ink buttons with paper labels, signal red reserved
        // for the destructive (sell) action only.
        var bg = _panel.gameObject.AddComponent<Image>();
        bg.color = GeoPalette.WithAlpha(GeoPalette.Paper, 0.92f);

        var vlg = _panel.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(16, 16, 14, 14);
        vlg.spacing = 8f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        var fitter = _panel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _title = NewText("Title", _panel, 26f, GeoPalette.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        _title.gameObject.AddComponent<LayoutElement>().minHeight = 34f;

        var row = NewRect("Buttons", _panel);
        var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10f; hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = false;
        row.gameObject.AddComponent<LayoutElement>().minHeight = 40f;

        _moveButton = NewButton(row, "Move as a whole", GeoPalette.Ink,    out _);
        _sellButton = NewButton(row, "Sell all",        GeoPalette.Signal, out _);

        _moveButton.onClick.AddListener(() => _onMove?.Invoke());
        _sellButton.onClick.AddListener(() => _onSell?.Invoke());

        BlockInfoPanel.ApplyRenderOnTop(_root);
        _root.AddComponent<Driver>();

        _canvas.enabled = false;
    }

    // `worldAnchor` = world-space centroid of the selected blocks (the panel
    // floats above this point, pulled toward the camera, same "depth" trick
    // BlockInfoPanel uses).
    public static void Show(int count, Vector3 worldAnchor, Action onMove, Action onSell)
    {
        Ensure();
        _onMove = onMove;
        _onSell = onSell;

        _title.text = count == 1 ? "1 block selected" : $"{count} blocks selected";
        _anchor = worldAnchor;

        if (_target < 0.5f) _justShown = true;
        _target = 1f;
    }

    public static void Hide()
    {
        _target = 0f;
        _onMove = _onSell = null;
    }

    public static bool IsShown => _target > 0.5f;

    // Explicit rect check (doesn't rely on PlacementController.infoPanel being
    // wired — that gate only works because EventSystem.IsPointerOverGameObject()
    // happens to also cover this panel in the current scene; this is independent
    // of that coincidence).
    public static bool IsPointerOver(Vector2 screenPos)
        => IsShown && _canvas != null && _canvas.enabled && _panel != null
        && RectTransformUtility.RectangleContainsScreenPoint(_panel, screenPos, _canvas.worldCamera);

    // ── UI primitives ────────────────────────────────────────────────────────
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
        t.raycastTarget = false;
        return t;
    }

    static Button NewButton(RectTransform parent, string label, Color color, out TMP_Text text)
    {
        var rt = NewRect("Button_" + label, parent);
        rt.gameObject.AddComponent<LayoutElement>().minHeight = 40f;

        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors; colors.highlightedColor = GeoPalette.Gold; colors.pressedColor = new Color(0.5f, 0.5f, 0.5f); btn.colors = colors;

        text = NewText("Label", rt, 16f, GeoPalette.Paper, FontStyles.Bold, TextAlignmentOptions.Center);
        var lrt = text.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        text.text = label;

        return btn;
    }
}
