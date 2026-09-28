using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

// Fixed prompt bar docked to the bottom-centre of the screen while a block is held.
// In a level it's one line — where to find the controls (the right HUD panel, see
// HudSidePanels) — rather than the whole list every time. In the LevelSelect map
// editor, which has no controls panel, it spells the keys out. PlacementHintOverlay
// handles the spatial arrows on the block itself. Auto-spawns, no scene wiring.
[DisallowMultipleComponent]
public class PlacementHintBar : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TrySpawn();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TrySpawn();

    static void TrySpawn()
    {
        // Gameplay scene (PlacementController) or LevelSelect's map editor (LevelMapController) —
        // same prompt bar, whichever one is holding a block.
        if (PlacementController.Instance == null && LevelMapController.Instance == null) return;
        if (FindFirstObjectByType<PlacementHintBar>() != null) return;
        new GameObject("PlacementHintBar").AddComponent<PlacementHintBar>();
    }

    [Header("Icons (optional — leave empty to show text only)")]
    public Sprite adjustIcon;
    public Sprite zoomIcon;
    public Sprite rotateIcon;

    [Header("Font (leave null for TMP default)")]
    public TMP_FontAsset font;
    public float fontSize = 26f;

    [Header("Look")]
    public Color textColor = new Color(0.949f, 0.937f, 0.902f, 0.95f);
    public Color cardColor = new Color(0.086f, 0.086f, 0.086f, 0.72f);
    [Tooltip("Distance up from the bottom edge, in reference (1080p) pixels.")]
    public float bottomMargin = 40f;
    [Tooltip("Padding around the prompts inside the card, in reference pixels.")]
    public Vector2 cardPadding = new Vector2(28f, 16f);

    const float DesignHeight = 1080f;

    RectTransform _canvasRt;
    RectTransform _fullBar, _shortBar;
    bool _built;

    void Update()
    {
        var pc  = PlacementController.Instance;
        var map = LevelMapController.Instance;
        if (pc == null && map == null) { if (_canvasRt != null) _canvasRt.gameObject.SetActive(false); return; }

        bool show = (pc != null && pc.mode == PlacementMode.Edit && pc.currentBlock != null)
                 || (map != null && map.IsEditingBlock);

        if (!_built)
        {
            if (!show) return;
            Build();
            _built = true;
        }

        _canvasRt.gameObject.SetActive(show);
        if (!show) return;
        bool inLevel = pc != null && pc.mode == PlacementMode.Edit && pc.currentBlock != null;
        _shortBar.gameObject.SetActive(inLevel);
        _fullBar.gameObject.SetActive(!inLevel);
    }

    void Build()
    {
        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;   // on top of other gameplay UI (objectives=92, world hints=90)
        var sc = canvasGo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, DesignHeight);
        sc.matchWidthOrHeight = 1f;

        var card = NewRect("Card", (RectTransform)canvasGo.transform);
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0f);
        card.anchoredPosition = new Vector2(0f, bottomMargin);
        _canvasRt = card;
        var bg = card.gameObject.AddComponent<Image>();
        bg.sprite = UIRoundedRect.Get(20);
        bg.type = Image.Type.Sliced;
        bg.color = cardColor;
        bg.raycastTarget = false;
        var cardFit = card.gameObject.AddComponent<ContentSizeFitter>();
        cardFit.horizontalFit = cardFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var cardLayout = card.gameObject.AddComponent<HorizontalLayoutGroup>();
        cardLayout.padding = new RectOffset((int)cardPadding.x, (int)cardPadding.x, (int)cardPadding.y, (int)cardPadding.y);
        cardLayout.childAlignment = TextAnchor.MiddleCenter;
        cardLayout.childControlWidth = cardLayout.childControlHeight = true;
        cardLayout.childForceExpandWidth = false; cardLayout.childForceExpandHeight = false;

        _fullBar = NewBar("FullBar", card);
        AddPrompt(_fullBar, adjustIcon, "WASDQE Adjust");
        AddPrompt(_fullBar, zoomIcon,   "Scroll Set Dis");
        AddPrompt(_fullBar, rotateIcon, "Hold Alt + Mouse/Wheel Rotate");
        AddPrompt(_fullBar, rotateIcon, "Tap Cancel");

        _shortBar = NewBar("ShortBar", card);
        var t = NewText("Label", _shortBar, fontSize * 0.85f, textColor);
        t.text = $"Press <b>{GameSettings.ControlsPanelKey}</b> to view controls";
        t.fontStyle = FontStyles.Normal;
    }

    RectTransform NewBar(string name, RectTransform parent)
    {
        var bar = NewRect(name, parent);
        var h = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 16f;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = false;
        var fit = bar.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return bar;
    }

    void AddPrompt(RectTransform parent, Sprite icon, string label)
    {
        var cell = NewRect("Prompt", parent);
        var h = cell.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8f;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = false;

        var iconRt = NewRect("Icon", cell);
        var iconImg = iconRt.gameObject.AddComponent<Image>();
        iconImg.sprite = icon;
        iconImg.color = icon != null ? textColor : new Color(0f, 0f, 0f, 0f);
        iconImg.preserveAspect = true;
        iconImg.raycastTarget = false;
        var le = iconRt.gameObject.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = le.minHeight = le.preferredHeight = fontSize * 1.2f;

        var t = NewText("Label", cell, fontSize, textColor);
        t.text = label;
        t.gameObject.AddComponent<LayoutElement>().preferredWidth = t.preferredWidth;
    }

    RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    TMP_Text NewText(string name, Transform parent, float size, Color color)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.color = color;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }
}
