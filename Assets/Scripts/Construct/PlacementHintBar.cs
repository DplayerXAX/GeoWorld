using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

// Dead-by-Daylight-style fixed prompt bar: while PlacementController is in Edit
// mode (a block is held), shows WASDQE adjust / scroll zoom / 1/2/3 rotate
// docked to the bottom-center of the SCREEN (screen-space, not world-space —
// this is a HUD reminder, not a spatial indicator; PlacementHintOverlay handles
// the spatial arrows/rings on the block itself). Auto-spawns, no scene wiring.
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
        if (PlacementController.Instance == null) return;   // gameplay scene only
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
    bool _built;

    void Update()
    {
        var pc = PlacementController.Instance;
        if (pc == null) { if (_canvasRt != null) _canvasRt.gameObject.SetActive(false); return; }

        bool show = pc.mode == PlacementMode.Edit && pc.currentBlock != null;

        if (!_built)
        {
            if (!show) return;
            Build();
            _built = true;
        }

        _canvasRt.gameObject.SetActive(show);
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

        var bar = NewRect("Bar", card);
        var h = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 16f;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = false;
        var fit = bar.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Ring colors from the live PlacementHintOverlay (1=X axis, 2=Y axis, 3=Z axis) —
        // falls back to that component's own field defaults if none is spawned yet.
        var ringOverlay = FindFirstObjectByType<PlacementHintOverlay>();
        Color cx = ringOverlay != null ? ringOverlay.axisColorX : new Color(1.00f, 0.30f, 0.30f);
        Color cy = ringOverlay != null ? ringOverlay.axisColorY : new Color(0.35f, 0.95f, 0.40f);
        Color cz = ringOverlay != null ? ringOverlay.axisColorZ : new Color(0.35f, 0.55f, 1.00f);
        string rotateLabel = $"<color=#{ColorUtility.ToHtmlStringRGB(cx)}>1</color>/" +
                              $"<color=#{ColorUtility.ToHtmlStringRGB(cy)}>2</color>/" +
                              $"<color=#{ColorUtility.ToHtmlStringRGB(cz)}>3</color> Rotate";

        AddPrompt(bar, adjustIcon, "WASDQE Adjust");
        AddPrompt(bar, zoomIcon,   "Scroll Set Dis");
        AddPrompt(bar, rotateIcon, rotateLabel);
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
        t.gameObject.AddComponent<LayoutElement>().preferredWidth = fontSize * 8f;
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
