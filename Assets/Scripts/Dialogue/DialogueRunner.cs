using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Self-contained visual-novel dialogue player. Builds its whole UGUI at runtime, so
// it drops into any project with zero scene setup:
//
//     DialogueRunner.Instance.Play(myConversation);
//
// Features: standing portraits (立绘, left/right/centre with non-speaker dimming),
// character name box (coloured), topic header (话题), typewriter text with a blinking
// continue indicator (继续条), and choice branches. All data lives in ScriptableObjects
// (DialogueCharacter / DialogueConversation), and the look is field-configurable — so
// it's portable to other games.
public class DialogueRunner : MonoBehaviour
{
    [Header("Font (leave null for TMP default)")]
    public TMP_FontAsset font;
    public float textSize   = 30f;
    public float nameSize   = 30f;
    public float topicSize  = 22f;
    public float choiceSize = 26f;

    [Header("Colors")]
    public Color boxColor        = new Color(0.949f, 0.937f, 0.902f, 0.97f);  // paper
    public Color textColor       = new Color(0.086f, 0.086f, 0.086f);          // ink
    public Color topicColor      = new Color(0.40f, 0.40f, 0.40f);
    public Color accentColor     = new Color(0.886f, 0.141f, 0.106f);          // signal
    public Color choiceColor     = new Color(0.949f, 0.937f, 0.902f, 0.97f);
    public Color choiceText      = new Color(0.086f, 0.086f, 0.086f);
    public Color choiceHighlight = new Color(0.910f, 0.698f, 0.227f);          // gold

    [Header("Feel")]
    public float   typeSpeed   = 45f;   // characters per second
    public float   fadeSpeed   = 12f;
    [Range(0f, 1f)] public float portraitDim = 0.45f;
    public KeyCode advanceKey  = KeyCode.Space;

    // ── Events (for game hooks) ──────────────────────────────────────────────────
    public event Action<DialogueConversation> OnFinished;
    public event Action<string>               OnLineEvent;   // line.eventId / choice.eventId

    public bool IsPlaying { get; private set; }

    // ── Singleton (auto-creates if none in scene) ────────────────────────────────
    static DialogueRunner _instance;
    public static DialogueRunner Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<DialogueRunner>();
                if (_instance == null)
                    _instance = new GameObject("DialogueRunner").AddComponent<DialogueRunner>();
            }
            return _instance;
        }
    }

    // ── Runtime UI ────────────────────────────────────────────────────────────────
    Canvas        _canvas;
    CanvasGroup   _group;
    Image[]       _portraits;       // indexed by PortraitSlot
    Image         _nameBg;
    TMP_Text      _nameText, _topicText, _bodyText, _continue;
    RectTransform _choiceBox;
    readonly List<GameObject> _choiceButtons = new();

    // ── State ─────────────────────────────────────────────────────────────────────
    DialogueConversation _convo;
    int     _line;
    bool    _typing, _choiceMode;
    float   _typed, _alphaTarget, _blink;

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        BuildUI();
        _group.alpha = 0f;
        _alphaTarget = 0f;
        gameObject.SetActive(true);
    }

    // ── Public API ───────────────────────────────────────────────────────────────

    public void Play(DialogueConversation conversation)
    {
        if (conversation == null) { Finish(null); return; }
        _convo      = conversation;
        _line       = 0;
        IsPlaying   = true;
        _alphaTarget = 1f;
        _topicText.text = conversation.topic;
        ShowLine(0);
    }

    public void Stop() => Finish(_convo);

    // ── Loop ──────────────────────────────────────────────────────────────────────

    void Update()
    {
        if (_group == null) return;
        _group.alpha = Mathf.MoveTowards(_group.alpha, _alphaTarget, fadeSpeed * Time.unscaledDeltaTime);
        _group.blocksRaycasts = _group.interactable = _alphaTarget > 0.5f;
        if (!IsPlaying) return;

        // Typewriter (uses maxVisibleCharacters so rich text isn't sliced).
        if (_typing)
        {
            _typed += typeSpeed * Time.unscaledDeltaTime;
            int total = _bodyText.textInfo.characterCount;
            _bodyText.maxVisibleCharacters = Mathf.Min(total, Mathf.FloorToInt(_typed));
            if (_bodyText.maxVisibleCharacters >= total) _typing = false;
        }

        // Blinking continue indicator (hidden while typing / choosing).
        bool showContinue = !_typing && !_choiceMode;
        _blink += Time.unscaledDeltaTime * 3f;
        _continue.gameObject.SetActive(showContinue);
        if (showContinue)
        {
            var c = _continue.color; c.a = 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(_blink)); _continue.color = c;
        }

        // Advance on click / key (ignored while choices are up — buttons handle it).
        if (!_choiceMode && (Input.GetMouseButtonDown(0) || (advanceKey != KeyCode.None && Input.GetKeyDown(advanceKey))))
        {
            if (_typing) { _typing = false; _bodyText.maxVisibleCharacters = _bodyText.textInfo.characterCount; }
            else Advance();
        }
    }

    void Advance()
    {
        _line++;
        if (_convo != null && _line < _convo.lines.Count) { ShowLine(_line); return; }

        if (_convo != null && _convo.choices != null && _convo.choices.Count > 0) { ShowChoices(); return; }
        if (_convo != null && _convo.autoNext != null) { Play(_convo.autoNext); return; }
        Finish(_convo);
    }

    void ShowLine(int i)
    {
        var line = _convo.lines[i];

        // Name box.
        bool hasName = !string.IsNullOrEmpty(line.SpeakerName);
        _nameBg.gameObject.SetActive(hasName);
        _nameText.text  = line.SpeakerName;
        _nameText.color = line.character != null ? line.character.nameColor : textColor;

        // Portraits: speaker bright in its slot, others dimmed.
        if (line.character != null)
        {
            int slot = (int)line.slot;
            var sprite = line.character.GetPortrait(line.portrait);
            if (sprite != null)
            {
                _portraits[slot].sprite = sprite;
                _portraits[slot].enabled = true;
                _portraits[slot].SetNativeSizePreserve();
            }
            for (int s = 0; s < _portraits.Length; s++)
            {
                if (!_portraits[s].enabled) continue;
                float b = (s == slot) ? 1f : portraitDim;
                _portraits[s].color = new Color(b, b, b, 1f);
            }
        }

        // Text (typewriter via maxVisibleCharacters).
        _bodyText.text = line.text;
        _bodyText.ForceMeshUpdate();
        _bodyText.maxVisibleCharacters = 0;
        _typed  = 0f;
        _typing = true;

        if (!string.IsNullOrEmpty(line.eventId)) OnLineEvent?.Invoke(line.eventId);
    }

    void ShowChoices()
    {
        _choiceMode = true;
        _continue.gameObject.SetActive(false);
        ClearChoices();

        foreach (var ch in _convo.choices)
        {
            var choice = ch;   // capture
            var btn = MakeChoiceButton(choice.text);
            btn.onClick.AddListener(() =>
            {
                if (!string.IsNullOrEmpty(choice.eventId)) OnLineEvent?.Invoke(choice.eventId);
                ClearChoices();
                _choiceMode = false;
                if (choice.next != null) Play(choice.next);
                else Finish(_convo);
            });
        }
        _choiceBox.gameObject.SetActive(true);
    }

    void Finish(DialogueConversation convo)
    {
        IsPlaying    = false;
        _choiceMode  = false;
        _alphaTarget = 0f;
        ClearChoices();
        OnFinished?.Invoke(convo);
    }

    // ── Choice buttons ─────────────────────────────────────────────────────────────

    void ClearChoices()
    {
        foreach (var go in _choiceButtons) if (go != null) Destroy(go);
        _choiceButtons.Clear();
        if (_choiceBox != null) _choiceBox.gameObject.SetActive(false);
    }

    Button MakeChoiceButton(string label)
    {
        var rt = NewRect("Choice", _choiceBox);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = choiceColor;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors; colors.highlightedColor = choiceHighlight; colors.selectedColor = choiceHighlight; btn.colors = colors;
        rt.gameObject.AddComponent<LayoutElement>().minHeight = choiceSize * 2.1f;

        var t = NewText("Label", rt, choiceSize, choiceText, FontStyles.Bold, TextAlignmentOptions.Center);
        t.text = label;
        var lrt = t.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(16f, 6f); lrt.offsetMax = new Vector2(-16f, -6f);

        _choiceButtons.Add(rt.gameObject);
        return btn;
    }

    // ── Build (runtime UGUI) ────────────────────────────────────────────────────────

    void BuildUI()
    {
        var canvasGO = new GameObject("DialogueCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        _canvas = canvasGO.GetComponent<Canvas>();
        _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 500;   // above gameplay HUD

        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        EnsureEventSystem();

        var root = NewRect("Root", canvasGO.transform);
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        _group = root.gameObject.AddComponent<CanvasGroup>();

        BuildPortraits(root);
        BuildBox(root);
        BuildChoices(root);
    }

    void BuildPortraits(RectTransform root)
    {
        _portraits = new Image[3];
        _portraits[(int)PortraitSlot.Left]   = MakePortrait(root, new Vector2(0f,   0f), new Vector2(0f,   0f), new Vector2(60f,  0f));
        _portraits[(int)PortraitSlot.Right]  = MakePortrait(root, new Vector2(1f,   0f), new Vector2(1f,   0f), new Vector2(-60f, 0f));
        _portraits[(int)PortraitSlot.Center] = MakePortrait(root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f,   0f));
        foreach (var p in _portraits) p.enabled = false;
    }

    Image MakePortrait(RectTransform root, Vector2 anchor, Vector2 pivot, Vector2 pos)
    {
        var rt = NewRect("Portrait", root);
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot;
        rt.sizeDelta = new Vector2(560f, 840f);
        rt.anchoredPosition = pos;
        var img = rt.gameObject.AddComponent<Image>();
        img.raycastTarget = false;
        img.preserveAspect = true;
        return img;
    }

    void BuildBox(RectTransform root)
    {
        var box = NewRect("Box", root);
        box.anchorMin = new Vector2(0f, 0f); box.anchorMax = new Vector2(1f, 0f);
        box.pivot = new Vector2(0.5f, 0f);
        box.sizeDelta = new Vector2(-120f, 300f);
        box.anchoredPosition = new Vector2(0f, 40f);
        var bg = box.gameObject.AddComponent<Image>();
        bg.color = boxColor;

        // Accent rule along the top of the box.
        var rule = NewImage("Rule", box, accentColor, false).rectTransform;
        rule.anchorMin = new Vector2(0f, 1f); rule.anchorMax = new Vector2(1f, 1f);
        rule.pivot = new Vector2(0.5f, 1f); rule.sizeDelta = new Vector2(0f, 5f); rule.anchoredPosition = Vector2.zero;

        // Name box (overlaps the top edge).
        _nameBg = NewImage("NameBg", box, accentColor, false);
        var nbg = _nameBg.rectTransform;
        nbg.anchorMin = new Vector2(0f, 1f); nbg.anchorMax = new Vector2(0f, 1f); nbg.pivot = new Vector2(0f, 0f);
        nbg.sizeDelta = new Vector2(280f, nameSize * 1.8f); nbg.anchoredPosition = new Vector2(28f, -2f);
        _nameText = NewText("Name", nbg, nameSize, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
        StretchInto(_nameText.rectTransform, 14f, 0f);

        // Topic header (top-right inside the box).
        _topicText = NewText("Topic", box, topicSize, topicColor, FontStyles.Italic, TextAlignmentOptions.TopRight);
        var trt = _topicText.rectTransform;
        trt.anchorMin = new Vector2(1f, 1f); trt.anchorMax = new Vector2(1f, 1f); trt.pivot = new Vector2(1f, 1f);
        trt.sizeDelta = new Vector2(520f, topicSize * 1.6f); trt.anchoredPosition = new Vector2(-28f, -16f);

        // Body text.
        _bodyText = NewText("Body", box, textSize, textColor, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        _bodyText.textWrappingMode = TextWrappingModes.Normal;
        var brt = _bodyText.rectTransform;
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
        brt.offsetMin = new Vector2(34f, 28f); brt.offsetMax = new Vector2(-34f, -58f);

        // Continue indicator.
        _continue = NewText("Continue", box, textSize, accentColor, FontStyles.Bold, TextAlignmentOptions.Center);
        _continue.text = "▼";
        var crt = _continue.rectTransform;
        crt.anchorMin = new Vector2(1f, 0f); crt.anchorMax = new Vector2(1f, 0f); crt.pivot = new Vector2(1f, 0f);
        crt.sizeDelta = new Vector2(48f, 48f); crt.anchoredPosition = new Vector2(-24f, 18f);
        _continue.gameObject.SetActive(false);
    }

    void BuildChoices(RectTransform root)
    {
        _choiceBox = NewRect("Choices", root);
        _choiceBox.anchorMin = new Vector2(0.5f, 0f); _choiceBox.anchorMax = new Vector2(0.5f, 0f);
        _choiceBox.pivot = new Vector2(0.5f, 0f);
        _choiceBox.sizeDelta = new Vector2(760f, 0f);
        _choiceBox.anchoredPosition = new Vector2(0f, 360f);

        var vlg = _choiceBox.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 12f; vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        var fit = _choiceBox.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _choiceBox.gameObject.SetActive(false);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────

    static void EnsureEventSystem()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null) return;
        new GameObject("EventSystem",
            typeof(UnityEngine.EventSystems.EventSystem),
            typeof(UnityEngine.EventSystems.StandaloneInputModule));
    }

    static void StretchInto(RectTransform rt, float padX, float padY)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padX, padY); rt.offsetMax = new Vector2(-padX, -padY);
    }

    RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    Image NewImage(string name, Transform parent, Color color, bool raycast)
    {
        var rt = NewRect(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color; img.raycastTarget = raycast;
        return img;
    }

    TMP_Text NewText(string name, Transform parent, float size, Color color,
                     FontStyles style, TextAlignmentOptions align)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size; t.color = color; t.fontStyle = style; t.alignment = align;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }
}

// Small extension so a portrait keeps its aspect without us computing sizes.
static class DialogueImageExt
{
    public static void SetNativeSizePreserve(this Image img)
    {
        // Image.preserveAspect already handles fitting inside the rect — nothing to do,
        // but kept as a hook in case a project wants custom sizing per portrait.
    }
}
