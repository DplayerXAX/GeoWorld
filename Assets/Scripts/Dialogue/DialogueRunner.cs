using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Self-contained visual-novel dialogue player. Builds its whole UGUI at
// runtime, so it drops into any project with zero scene setup:
//
//     DialogueRunner.Instance.Play(myConversation);
//
// Features: standing portraits (left/right/centre with non-speaker dimming),
// character name box, topic header, typewriter text with a blinking continue
// indicator, and choice branches. Data lives in ScriptableObjects
// (DialogueCharacter / DialogueConversation).
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
    [Tooltip("Opacity the passive tutorial dialogue box fades to while the shop is expanded (F).")]
    [Range(0f, 1f)] public float passiveShopDim = 0.25f;
    [Tooltip("Opacity while the player is placing a block (any dialogue, not just gated).")]
    [Range(0f, 1f)] public float editDim = 0.3f;

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
    TMP_Text      _nameText, _topicText, _bodyText;
    Image         _continueBar;   // "光条" — the round pulsing ball planted after the last character
    TMP_Text      _peekHint;      // bottom-right small print advertising the Shift-peek
    Button        _skipButton;
    RectTransform _choiceBox;
    readonly List<GameObject> _choiceButtons = new();

    // ── State ─────────────────────────────────────────────────────────────────────
    DialogueConversation _convo;
    int     _line;
    bool    _typing, _choiceMode;
    float   _typed, _alphaTarget, _blink;
    int     _lineFrame = -1;   // frame a line was shown — ignore the click that opened it
    bool    _passive;          // display-only (tutorial): no click-advance, no input block
    bool    _lineGated;        // THIS LINE (line.actionGateId set) waits for CompleteGate, even in an otherwise-normal conversation

    // Effective passive-ness for the CURRENT line: either the whole conversation
    // was started passive (gameplay's TutorialDirector), or just this one line
    // demands a real action (DialogueLine.actionGateId — e.g. a LevelSelect
    // hands-on tutorial mixed into an otherwise click-through conversation).
    // Everything that used to key off `_passive` alone (input pass-through, the
    // action icon, the skip button, click-to-advance) now keys off this instead.
    bool Gated => _passive || _lineGated;

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

    // passive = display-only: ignores clicks and doesn't block game input. Used by
    // the tutorial so its dialogue can only be advanced by COMPLETING the step,
    // never by clicking the box away.
    public void Play(DialogueConversation conversation, bool passive = false)
    {
        if (conversation == null) { Finish(null); return; }
        // Box is currently hidden (fresh appearance, not a mid-conversation chain via
        // autoNext/choice) — play the pop-in. Checking alpha rather than IsPlaying
        // catches the very first Play() too, when nothing has run yet this session.
        bool freshAppearance = _group.alpha < 0.05f;
        _passive    = passive;
        _convo      = conversation;
        _line       = 0;
        IsPlaying   = true;
        _alphaTarget = 1f;
        _topicText.text = conversation.topic;
        ResetPortraits();   // clear any portrait left over from the previous conversation
        ShowLine(0);
        if (freshAppearance) StartCoroutine(BoxAppearAnim());
    }

    // Simple pop-in: box scales up from slightly-shrunk to full size, easing out.
    // Both box and boxText pivot at bottom-centre, so it reads as the box popping
    // up off the bottom edge rather than growing from its middle.
    IEnumerator BoxAppearAnim()
    {
        const float dur = 0.22f;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);   // ease-out cubic
            float s = Mathf.LerpUnclamped(0.9f, 1f, k);
            if (_boxRect != null)     _boxRect.localScale     = new Vector3(s, s, 1f);
            if (_boxTextRect != null) _boxTextRect.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        if (_boxRect != null)     _boxRect.localScale     = Vector3.one;
        if (_boxTextRect != null) _boxTextRect.localScale = Vector3.one;
    }

    // Hide all portraits + restore their scale. Called at the start of each
    // conversation so a new speaker doesn't show alongside the previous one's
    // standing portrait (within ONE conversation ShowLine accumulates speakers).
    void ResetPortraits()
    {
        if (_portraits == null) return;
        for (int s = 0; s < _portraits.Length; s++)
            if (_portraits[s] != null)
            {
                _portraits[s].enabled = false;
                _portraits[s].rectTransform.localScale = Vector3.one;
            }
    }

    public void Stop() => Finish(_convo);

    // Is the player mid block-placement, in either scene that has one?
    static bool EditingAnywhere()
    {
        var pc = PlacementController.Instance;
        if (pc != null && pc.mode == PlacementMode.Edit) return true;
        var map = LevelMapController.Instance;
        return map != null && map.BuildMode;
    }

    // ── Loop ──────────────────────────────────────────────────────────────────────

    void Update()
    {
        if (_group == null) return;

        // Passive tutorial dialogue dims (doesn't hide) while the shop is expanded, so
        // the opaque letterbox bars (rendered above this canvas) don't fight it for
        // attention — same box, same position, just lower opacity.
        float dimMul = 1f;
        if (Gated && ShopController.Instance != null && ShopController.Instance.IsExpanded)
            dimMul = passiveShopDim;

        // Not limited to gated lines — any dialogue is in the way while editing.
        bool editing = EditingAnywhere();
        if (editing) dimMul = Mathf.Min(dimMul, editDim);

        bool peeking = PeekWorld.Held;   // Shift held — see PeekWorld

        float targetAlpha = peeking ? 0f : _alphaTarget * dimMul;
        _group.alpha = Mathf.MoveTowards(_group.alpha, targetAlpha, fadeSpeed * Time.unscaledDeltaTime);

        // Gated dialogue must NOT block the game — the player needs to interact
        // with the real world (click a block, press F, ...) to complete the step
        // that advances it, and the dialogue box can't be sitting there eating
        // that click. Same for a peek or an edit-mode dim.
        _group.blocksRaycasts = _group.interactable = !Gated && !peeking && !editing && _alphaTarget > 0.5f;

        if (!IsPlaying) return;

        // Typewriter (uses maxVisibleCharacters so rich text isn't sliced).
        if (_typing)
        {
            _typed += typeSpeed * Time.unscaledDeltaTime;
            int total = _bodyText.textInfo.characterCount;
            _bodyText.maxVisibleCharacters = Mathf.Min(total, Mathf.FloorToInt(_typed));
            if (_bodyText.maxVisibleCharacters >= total)
            {
                _typing = false;
                AudioManager.Instance?.StopTextBlip();
            }
        }

        bool showBar = !_typing && !_choiceMode;
        _blink += Time.unscaledDeltaTime;

        // Three sine waves at incommensurate frequencies/phases, summed rather than
        // one clean sine — a single sine reads as a metronome (which was the actual
        // "不明显"/mechanical complaint: perfectly even, perfectly predictable). This
        // never quite repeats on a timescale a player would notice, closer to how a
        // candle or a firefly actually flickers than a blinking cursor does.
        float wobble = Mathf.Sin(_blink * 2.6f)
                      + Mathf.Sin(_blink * 4.3f + 1.7f) * 0.55f
                      + Mathf.Sin(_blink * 1.15f + 0.4f) * 0.4f;
        float pulse = 0.55f + 0.45f * Mathf.Clamp01(wobble * 0.36f + 0.5f);
        // A slow, separate drift so the bar doesn't sit dead still between pulses.
        float bobY = Mathf.Sin(_blink * 1.4f + 0.9f) * 3f;

        _continueBar.gameObject.SetActive(showBar);
        if (showBar)
        {
            PositionContinueBar(bobY);

            var core = accentColor; core.a = pulse;
            _continueBar.color = core;
            // Ball, not a bar — width and height pulse together off the same
            // diameter, so it stays a perfect circle at every point in the breathe.
            float d = Mathf.Lerp(20f, 32f, pulse);
            _continueBar.rectTransform.sizeDelta = new Vector2(d, d);
        }

        // Skip button: only for conversations the author explicitly marked skippable,
        // and never on gated dialogue — the player has no other way back into a
        // tutorial step's dialogue, so skipping it here would strand the step with
        // no visible instructions and no way to reopen them. (Skipping is still
        // safe in the sense that it won't leave operations permanently locked —
        // Stop()/Finish() clears the pending gate the same as finishing normally —
        // but it WOULD desync "you skipped past a step you never actually did".)
        _skipButton.gameObject.SetActive(IsPlaying && !Gated && _convo != null && _convo.skippable);

        // Advance on click / key (ignored while choices are up — buttons handle it).
        // Skip the very frame a line opened, so the click that triggered this
        // conversation (e.g. a tutorial Input step's Mouse0) doesn't instantly
        // advance/dismiss the line it just brought up.
        // `!peeking`: while the box is hidden to look at the world, a click is aimed
        // at the world, not at dismissing a line the player can't currently read.
        if (!Gated && !_choiceMode && !peeking && Time.frameCount != _lineFrame
            && (Input.GetMouseButtonDown(0) || (advanceKey != KeyCode.None && Input.GetKeyDown(advanceKey))
                || GamepadInput.ConfirmDown))
        {
            if (_typing)
            {
                _typing = false;
                _bodyText.maxVisibleCharacters = _bodyText.textInfo.characterCount;
                AudioManager.Instance?.StopTextBlip();
            }
            else Advance();
        }
    }

    // Plants the light bar right after the last visible character of the body
    // text, instead of a fixed box corner — so it shows up wherever the player's
    // eye already is (end of the line they just finished reading) rather than
    // somewhere they have to go looking for.
    //
    // ci.bottomRight is in the body text's OWN local space, and the bar is parented
    // directly to _bodyText.rectTransform (see BuildBoxText) — so this can drive its
    // localPosition straight from the character's local coords with no cross-object
    // space conversion.
    void PositionContinueBar(float bobY)
    {
        var ti = _bodyText.textInfo;
        int shown = _bodyText.maxVisibleCharacters;
        if (ti.characterCount == 0 || shown <= 0) return;

        int idx = Mathf.Clamp(shown - 1, 0, ti.characterCount - 1);
        var ci = ti.characterInfo[idx];
        // Typed text can end on whitespace/a line break, which TMP marks not-visible
        // and gives degenerate geometry — walk back to the last real glyph so the bar
        // never plants itself at a stray (0,0).
        while (idx > 0 && !ci.isVisible) { idx--; ci = ti.characterInfo[idx]; }
        if (!ci.isVisible) return;

        // Larger gap + drop than before — the bar itself got bigger, and sitting
        // right on the baseline crowded the text it was supposed to be a footnote to.
        const float gapX = 16f, dropY = -20f;
        var pos = new Vector3(ci.bottomRight.x + gapX, ci.bottomRight.y + dropY + bobY, 0f);
        _continueBar.rectTransform.localPosition = pos;
    }

    void Advance()
    {
        _line++;
        if (_convo != null && _line < _convo.lines.Count) { ShowLine(_line); return; }

        if (_convo != null && _convo.choices != null && _convo.choices.Count > 0) { ShowChoices(); return; }
        if (_convo != null && _convo.autoNext != null) { Play(_convo.autoNext, _passive); return; }
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
                // Per-portrait size (pivot is the bottom, so it grows upward).
                _portraits[slot].rectTransform.localScale =
                    Vector3.one * line.character.GetPortraitScale(line.portrait);
            }
            for (int s = 0; s < _portraits.Length; s++)
            {
                if (!_portraits[s].enabled) continue;
                float b = (s == slot) ? 1f : portraitDim;
                _portraits[s].color = new Color(b, b, b, 1f);
            }

            // Keep the text column clear of whichever side is actually showing a
            // portrait right now. Measured from the PORTRAIT'S OWN actual rendered
            // width (sprite aspect × its authored per-portrait scale), not a guessed
            // flat constant — a fixed guess only happens to clear whatever portrait
            // it was tuned against; any other sprite/scale can still poke into the
            // text. This can never fall short regardless of art size.
            bool  portraitShown = _portraits[slot].enabled;
            float portraitW     = portraitShown
                ? PortraitRenderedWidth(_portraits[slot].sprite, line.character.GetPortraitScale(line.portrait))
                : 0f;
            var brt = _bodyText.rectTransform;
            float padLeft, padRight;
            if (!portraitShown)
            {
                padLeft = padRight = TextPadNormal;
            }
            else if (slot == (int)PortraitSlot.Left)
            {
                padLeft = portraitW + PortraitTextGap; padRight = TextPadNormal;
            }
            else if (slot == (int)PortraitSlot.Right)
            {
                padLeft = TextPadNormal; padRight = portraitW + PortraitTextGap;
            }
            else   // Center — the portrait stands over the middle, so both sides need
                   // to give way, not just one; text ends up a narrower centred column.
            {
                padLeft = padRight = portraitW * 0.5f + PortraitTextGap;
            }
            brt.offsetMin = new Vector2(padLeft, brt.offsetMin.y);
            brt.offsetMax = new Vector2(-padRight, brt.offsetMax.y);
        }

        // Text (typewriter via maxVisibleCharacters).
        _bodyText.text = line.text;
        _bodyText.ForceMeshUpdate();
        _bodyText.maxVisibleCharacters = 0;
        _typed  = 0f;
        _typing = true;
        AudioManager.Instance?.StartTextBlip();
        _lineFrame = Time.frameCount;   // ignore the click that opened this line

        // This one line waits for CompleteGate(actionGateId) instead of a click —
        // see the Gated property. Recomputed per line, so a single conversation can
        // freely mix normal click-through narration with hands-on steps.
        _lineGated = !string.IsNullOrEmpty(line.actionGateId);

        if (!string.IsNullOrEmpty(line.actionGateId)) OnLineEvent?.Invoke(line.actionGateId);
        if (!string.IsNullOrEmpty(line.eventId))      OnLineEvent?.Invoke(line.eventId);
    }

    // Called by whatever system actually performed the real-world action a gated
    // line (DialogueLine.actionGateId) is asking for — e.g. LevelMapController
    // after a walk completes, a build panel opens, or a block gets placed. A
    // mismatched or stale id (nothing gated right now, or gated on something
    // else) is just a silent no-op — callers are expected to fire this
    // unconditionally on every occurrence of the action, not just when they
    // suspect a tutorial is watching.
    public void CompleteGate(string id)
    {
        if (!IsPlaying || _choiceMode || string.IsNullOrEmpty(id)) return;
        if (_convo == null || _line < 0 || _line >= _convo.lines.Count) return;
        if (_convo.lines[_line].actionGateId != id) return;

        if (_typing)
        {
            _typing = false;
            _bodyText.maxVisibleCharacters = _bodyText.textInfo.characterCount;
            AudioManager.Instance?.StopTextBlip();
        }
        Advance();
    }

    void ShowChoices()
    {
        _choiceMode = true;
        _continueBar.gameObject.SetActive(false);
        _skipButton.gameObject.SetActive(false);   // choosing IS the way forward now — nothing left to skip
        ClearChoices();

        Button firstButton = null;
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
            if (firstButton == null) firstButton = btn;
        }
        _choiceBox.gameObject.SetActive(true);

        // Gamepad: give Navigate/Submit a starting focus the instant choices appear.
        if (firstButton != null)
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(firstButton.gameObject);
    }

    void Finish(DialogueConversation convo)
    {
        IsPlaying    = false;
        _choiceMode  = false;
        _alphaTarget = 0f;
        _typing      = false;
        AudioManager.Instance?.StopTextBlip();   // guard: convo can end mid-line (e.g. externally Stop()ped)
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
        _canvas.sortingOrder = 50;   // below the shop letterbox bars (55) so the bars frame the dialogue

        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        EnsureEventSystem();

        var root = NewRect("Root", canvasGO.transform);
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        _group = root.gameObject.AddComponent<CanvasGroup>();

        // Layering (bottom → top): box panel, portraits, box TEXT, choices. So the
        // standing portrait sits on the panel but the dialogue text stays on top of
        // the portrait (never covered). Choices are above everything.
        BuildBox(root);
        BuildPortraits(root);
        BuildBoxText(root);
        BuildChoices(root);
    }

    void BuildPortraits(RectTransform root)
    {
        _portraits = new Image[3];
        // Pushed closer to the screen edge (was 60px in) so they read as standing at
        // the sides instead of crowding into the middle of the text column.
        _portraits[(int)PortraitSlot.Left]   = MakePortrait(root, new Vector2(0f,   0f), new Vector2(0f,   0f), new Vector2(12f,  0f));
        _portraits[(int)PortraitSlot.Right]  = MakePortrait(root, new Vector2(1f,   0f), new Vector2(1f,   0f), new Vector2(-12f, 0f));
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

    RectTransform _boxRect, _boxTextRect;
    // Body-text horizontal padding: normal on the side with no portrait, wide on the
    // speaking portrait's side so the text column never runs under the standing art.
    const float TextPadNormal = 34f;
    const float PortraitTextGap = 24f;   // breathing room between the portrait's edge and the text

    // Portrait box is a fixed 560×840 with preserveAspect=true, so the sprite is
    // letterboxed to fit — actual on-screen width depends on the SPRITE's own aspect,
    // not just the 560 box width. Same math Image.preserveAspect uses internally.
    static float PortraitRenderedWidth(Sprite sprite, float scale)
    {
        if (sprite == null) return 0f;
        const float boxW = 560f, boxH = 840f;
        float spriteAspect = sprite.rect.width / Mathf.Max(1f, sprite.rect.height);
        float boxAspect     = boxW / boxH;
        float w = spriteAspect > boxAspect ? boxW : boxH * spriteAspect;
        return w * scale;
    }

    // The panel only (background + accent rule). Built BELOW the portraits so the
    // standing portrait sits on top of the box. The TEXT is built separately, above
    // the portraits (BuildBoxText), so the dialogue text is never covered.
    void BuildBox(RectTransform root)
    {
        var box = NewRect("Box", root);
        box.anchorMin = new Vector2(0f, 0f); box.anchorMax = new Vector2(1f, 0f);
        box.pivot = new Vector2(0.5f, 0f);
        box.sizeDelta = new Vector2(-120f, 300f);
        box.anchoredPosition = new Vector2(0f, 40f);
        var bg = box.gameObject.AddComponent<Image>();
        bg.color = boxColor;
        _boxRect = box;

        // Accent rule along the top of the box.
        var rule = NewImage("Rule", box, accentColor, false).rectTransform;
        rule.anchorMin = new Vector2(0f, 1f); rule.anchorMax = new Vector2(1f, 1f);
        rule.pivot = new Vector2(0.5f, 1f); rule.sizeDelta = new Vector2(0f, 5f); rule.anchoredPosition = Vector2.zero;
    }

    // Name / topic / body / continue — an overlay matching the box rect, parented to
    // root AFTER the portraits so the text renders ON TOP of the standing portrait.
    void BuildBoxText(RectTransform root)
    {
        var box = NewRect("BoxText", root);
        box.anchorMin = _boxRect.anchorMin; box.anchorMax = _boxRect.anchorMax;
        box.pivot = _boxRect.pivot; box.sizeDelta = _boxRect.sizeDelta;
        box.anchoredPosition = _boxRect.anchoredPosition;
        _boxTextRect = box;

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
        brt.offsetMin = new Vector2(TextPadNormal, 28f); brt.offsetMax = new Vector2(-TextPadNormal, -58f);

        // Continue light — a ball, not a bar. Parented to the BODY TEXT itself (not
        // a fixed box corner), so PositionContinueBar() can plant it right after
        // wherever the last visible character actually lands — bottom-right of the
        // text, not a random corner of the box the player might not be looking at.
        //
        // UIRoundedRect.Get(radius) generates its texture at size = radius*2+4, so
        // whatever radius you pass, the sprite is ALREADY a near-perfect circle (a
        // 2px flat inset is negligible at this scale). Type.Simple (not Sliced)
        // scales it as one whole image instead of 9-slicing borders that would
        // otherwise get squished/uneven as the ball shrinks on the down-pulse.
        _continueBar = NewImage("ContinueBar", _bodyText.rectTransform, accentColor, false);
        _continueBar.sprite = UIRoundedRect.Get(16);
        _continueBar.type   = Image.Type.Simple;
        var crt = _continueBar.rectTransform;
        crt.anchorMin = crt.anchorMax = Vector2.zero; crt.pivot = new Vector2(0f, 0.5f);
        crt.sizeDelta = new Vector2(24f, 24f);
        _continueBar.gameObject.SetActive(false);

        // Skip — bare text, no button chrome. Opposite corner from the continue/
        // action indicators so it never fights them for attention. Only ever shown
        // for conversations authored skippable=true (see DialogueConversation) and
        // never on passive dialogue. The hit target (skipImg) is fully transparent —
        // it exists only so Button has something to raycast against, not to draw a
        // frame around the text.
        // Small print advertising the Shift-peek (PeekWorld) — same corner/register
        // as the block detail panel's own footnote.
        _peekHint = NewText("PeekHint", box, textSize * 0.6f,
            new Color(textColor.r, textColor.g, textColor.b, 0.5f),
            FontStyles.Italic, TextAlignmentOptions.BottomRight);
        _peekHint.text = "Hold Left Shift to hide";
        var phrt = _peekHint.rectTransform;
        phrt.anchorMin = new Vector2(1f, 0f); phrt.anchorMax = new Vector2(1f, 0f); phrt.pivot = new Vector2(1f, 0f);
        phrt.sizeDelta = new Vector2(420f, 26f); phrt.anchoredPosition = new Vector2(-24f, 6f);

        var skipRt = NewRect("Skip", box);
        skipRt.anchorMin = new Vector2(1f, 0f); skipRt.anchorMax = new Vector2(1f, 0f); skipRt.pivot = new Vector2(1f, 0f);
        // y raised clear of the peek-hint line above — skip and the hint share this corner.
        skipRt.sizeDelta = new Vector2(152f, 64f); skipRt.anchoredPosition = new Vector2(-24f, 34f);
        var skipImg = skipRt.gameObject.AddComponent<Image>();
        skipImg.color = new Color(0f, 0f, 0f, 0f);
        _skipButton = skipRt.gameObject.AddComponent<Button>();
        _skipButton.targetGraphic = skipImg;
        _skipButton.onClick.AddListener(Stop);   // Stop() = Finish(_convo): ends the WHOLE conversation, not just this line

        var skipLabel = NewText("Label", skipRt, textSize * 1.1f, Color.black, FontStyles.Bold, TextAlignmentOptions.Center);
        skipLabel.text = "skip>>";
        StretchInto(skipLabel.rectTransform, 4f, 0f);

        _skipButton.gameObject.SetActive(false);
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
            typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
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
