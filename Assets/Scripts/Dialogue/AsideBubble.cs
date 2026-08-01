using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// A tiny top-right "aside" — a portrait + one line that pops in, sits briefly, and
// fades back out. Not a real DialogueRunner conversation: no name box gating, no
// choices, doesn't pause the game. Just flavor chatter / asides.
//
// Call AsideBubble.Show(character, portraitKey, text) from anywhere — the singleton
// canvas lazily builds itself on first use and survives scene loads.
public class AsideBubble : MonoBehaviour
{
    static AsideBubble _inst;

    public static void Show(DialogueCharacter character, string portraitKey, string text, float duration = 3.5f)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (_inst == null)
        {
            var go = new GameObject("AsideBubble");
            DontDestroyOnLoad(go);
            _inst = go.AddComponent<AsideBubble>();
            _inst.Build();
        }
        _inst.Play(character, portraitKey, text, duration);
    }

    [Header("Look")]
    public TMP_FontAsset font;
    public Color textColor = new Color(0.086f, 0.086f, 0.086f, 1f);
    public Color nameColor = new Color(0.086f, 0.086f, 0.086f, 0.7f);
    public Color cardColor = new Color(0.949f, 0.937f, 0.902f, 0.96f);
    public float cardWidth = 468f;
    [Tooltip("Fixed card height, in reference pixels — does not grow/shrink with text length.")]
    public float cardHeight = 125f;
    [Tooltip("Distance from the top of the screen, in reference (1080p) pixels — kept clear of the pause icon.")]
    public float topMargin = 128f;
    public float rightMargin = 24f;

    [Header("Portrait")]
    [Tooltip("Portrait height, in reference pixels — sized independently of the card so it can tower over it.")]
    public float portraitSize = 195f;
    [Tooltip("How far the portrait pokes above the card's top edge, in reference pixels.")]
    public float portraitPeekUp = 31f;
    [Tooltip("Fraction of the portrait's width that sits ON the card — the rest pokes out past its left edge.")]
    [Range(0f, 1f)] public float portraitOverlapFraction = 0.6f;

    [Header("Body text auto-size")]
    public float bodyFontMin = 16f;
    public float bodyFontMax = 26f;
    [Tooltip("Extra gap between the portrait and the name/body text, on top of the portrait's own width, in reference pixels.")]
    public float textLeftGap = 60f;

    [Header("Timing")]
    public float slideInDuration = 0.3f;
    public float slideOutDuration = 0.3f;

    // Slide time on top of Show()'s `duration` (which is the HOLD only), so a caller
    // sequencing around a bubble — e.g. LevelMapController's farm-reveal cutscene
    // holding its camera until the line has finished — can budget for the whole
    // animation instead of just the readable part.
    public const float SlideSeconds = 0.6f;
    [Tooltip("How long the (now off-screen) popup lingers before its GameObject is destroyed.")]
    public float destroyDelayAfterHidden = 1f;

    CanvasGroup _group;
    RectTransform _root;
    RectTransform _card;
    Image _portraitImg;
    TMP_Text _nameText;
    TMP_Text _bodyText;
    Coroutine _routine;
    float _restX;    // Root.anchoredPosition.x on screen
    float _hiddenX;  // Root.anchoredPosition.x fully off the right edge

    void Build()
    {
        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 250;
        var sc = canvasGo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 1f;

        // Root anchors the whole group top-right, clear of the pause icon, and is
        // what actually slides — X animates between _hiddenX (off-screen) and _restX.
        _root = NewRect("Root", (RectTransform)canvasGo.transform);
        _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(1f, 1f);
        _restX   = -rightMargin;
        // Clear of the right edge including the portrait's overhang, whatever its aspect turns out to be.
        _hiddenX = cardWidth + portraitSize + rightMargin + 40f;
        _root.anchoredPosition = new Vector2(_hiddenX, -topMargin);

        _group = _root.gameObject.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;

        _card = NewRect("Card", _root);
        _card.anchorMin = _card.anchorMax = _card.pivot = new Vector2(1f, 1f);
        _card.anchoredPosition = Vector2.zero;
        _card.sizeDelta = new Vector2(cardWidth, cardHeight);   // fixed — doesn't grow with text

        var bg = _card.gameObject.AddComponent<Image>();
        bg.sprite = UIRoundedRect.Get(20);
        bg.type = Image.Type.Sliced;
        bg.color = cardColor;
        bg.raycastTarget = false;

        // Left padding leaves room for the portrait, which overlaps in from outside
        // the card (built below, as a Root sibling so it isn't clipped by the card).
        var textCol = NewRect("TextCol", _card);
        textCol.anchorMin = Vector2.zero; textCol.anchorMax = Vector2.one;
        textCol.offsetMin = textCol.offsetMax = Vector2.zero;
        var v = textCol.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset((int)(portraitSize * 0.55f + textLeftGap), 21, 18, 18);
        v.spacing = 3f;
        v.childAlignment = TextAnchor.UpperLeft;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;

        _nameText = NewText("Name", textCol, 22f, nameColor, FontStyles.Bold);
        _bodyText = NewText("Body", textCol, 22f, textColor, FontStyles.Normal);
        _bodyText.textWrappingMode = TextWrappingModes.Normal;
        _bodyText.enableAutoSizing = true;
        _bodyText.fontSizeMin = bodyFontMin;
        _bodyText.fontSizeMax = bodyFontMax;

        // Portrait — a Root sibling AFTER Card, so it draws on top and is free to
        // poke outside the card's bounds instead of being clipped inside it.
        var portraitRt = NewRect("Portrait", _root);
        portraitRt.anchorMin = portraitRt.anchorMax = new Vector2(1f, 1f);
        portraitRt.pivot = new Vector2(1f, 1f);
        _portraitImg = portraitRt.gameObject.AddComponent<Image>();
        _portraitImg.preserveAspect = true;
        _portraitImg.raycastTarget = false;
    }

    void Play(DialogueCharacter character, string portraitKey, string text, float duration)
    {
        if (_routine != null) StopCoroutine(_routine);

        var sprite = character != null ? character.GetPortrait(portraitKey) : null;
        _portraitImg.sprite = sprite;
        _portraitImg.gameObject.SetActive(sprite != null);
        bool hasName = character != null && !string.IsNullOrEmpty(character.displayName);
        _nameText.text = hasName ? character.displayName : "";
        _nameText.gameObject.SetActive(hasName);
        _bodyText.text = text;

        if (sprite != null)
        {
            float aspect = sprite.rect.width / Mathf.Max(1f, sprite.rect.height);
            float width  = portraitSize * aspect;
            var portraitRt = _portraitImg.rectTransform;
            portraitRt.sizeDelta = new Vector2(width, portraitSize);
            // Right portion of the portrait overlaps the card; the rest pokes out past its left edge.
            portraitRt.anchoredPosition = new Vector2(-_card.rect.width + width * portraitOverlapFraction, portraitPeekUp);
        }

        _routine = StartCoroutine(PlayRoutine(duration));
    }

    IEnumerator PlayRoutine(float duration)
    {
        _group.alpha = 1f;   // visibility is handled by position now, not fade
        SetX(_hiddenX);

        // Skip one frame first — the frame a GameObject is created on (often the very
        // first frame after a scene load) can report a huge Time.unscaledDeltaTime,
        // which would otherwise let the slide-in finish inside that single frame and
        // read as "no animation at all".
        yield return null;

        float t = 0f;
        while (t < slideInDuration)
        {
            t += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / slideInDuration), 3f);   // ease-out
            SetX(Mathf.Lerp(_hiddenX, _restX, e));
            yield return null;
        }
        SetX(_restX);

        yield return new WaitForSecondsRealtime(duration);

        t = 0f;
        while (t < slideOutDuration)
        {
            t += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            float e = Mathf.Pow(Mathf.Clamp01(t / slideOutDuration), 3f);   // ease-in
            SetX(Mathf.Lerp(_restX, _hiddenX, e));
            yield return null;
        }
        SetX(_hiddenX);

        yield return new WaitForSecondsRealtime(destroyDelayAfterHidden);

        _routine = null;
        if (_inst == this) _inst = null;
        Destroy(gameObject);
    }

    void SetX(float x) => _root.anchoredPosition = new Vector2(x, _root.anchoredPosition.y);

    RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    TMP_Text NewText(string name, Transform parent, float size, Color color, FontStyles style)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.color = color;
        t.fontStyle = style;
        t.alignment = TextAlignmentOptions.TopLeft;
        t.raycastTarget = false;
        return t;
    }
}
