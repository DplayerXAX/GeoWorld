using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// Runtime UGUI settings overlay (rewritten from IMGUI so gamepad Navigate/Submit/Cancel
// work via the scene's InputSystemUIInputModule — real Slider/Toggle components are
// natively gamepad-navigable once selected, no custom input code needed). Same house
// style as LevelClearScreen.cs / PauseMenu.cs. Auto-spawns a persistent instance —
// open from anywhere with SettingsScreen.Open = true (Title menu, Pause menu).
[DisallowMultipleComponent]
public class SettingsScreen : MonoBehaviour
{
    static bool _open;
    public static bool Open
    {
        get => _open;
        set { if (_open != value) { _open = value; Instance?.OnOpenChanged(); } }
    }
    static SettingsScreen Instance;

    static readonly string[] Sections = { "AUDIO", "DISPLAY", "CONTROLS" };
    static readonly string[] FrameLabels = { "Off", "30", "60", "120", "144" };

    // The page, measured from its own top-left corner. Every mark on it — title,
    // cube slot, guides, content column — is derived from these, so the setting-out
    // lines land on the layout instead of near it.
    const float PanelW      = 1240f;
    const float PanelH      = 760f;
    const float Margin      = 26f;

    // The spine. Everything on this page is placed as "left of it" or "right of it":
    // the cube on one side, what the cube controls on the other.
    const float DividerX    = 470f;
    const float HeaderY     = 92f;    // short rule closing the left column's header

    // The title starts LEFT of the spine and runs across it — see BuildUI. Close
    // enough to the rule that the crossing reads as deliberate; further left and it
    // just looks like a heading that happens to be wide.
    const float TitleX      = 500f;

    const float CubeBox     = 300f;
    const float CubeBoxX    = (DividerX - CubeBox) * 0.5f;   // centred in its column
    const float CubeBoxY    = 150f;

    const float ContentX    = DividerX + 34f;
    const float ContentTop  = 152f;
    const float ContentBot  = 84f;

    const float GuideFade   = 5f;

    /// <summary>
    /// Where the shared cube parks on this page, in canvas coordinates.
    ///
    /// Derived rather than typed in, because the cube lives on a DIFFERENT canvas and
    /// cannot be anchored to this panel. A hand-tuned pair of numbers here would come
    /// unstuck the first time the panel changed size, and it would come unstuck
    /// silently.
    /// </summary>
    public static Vector2 CubeSlot =>
        new Vector2(-PanelW * 0.5f + CubeBoxX + CubeBox * 0.5f,
                     PanelH * 0.5f - CubeBoxY - CubeBox * 0.5f);

    int _section;
    Canvas _canvas;
    SettingsCube _cube;
    TMP_Text     _cubeCaption;
    CanvasGroup  _guides;
    GameObject[] _sectionRoots = new GameObject[3];
    GameObject _firstControlAudio, _firstControlDisplay, _firstControlControls;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Spawn()
    {
        if (FindFirstObjectByType<SettingsScreen>() != null) return;
        var go = new GameObject("SettingsScreen");
        DontDestroyOnLoad(go);
        go.AddComponent<SettingsScreen>();
    }

    void Awake()
    {
        Instance = this;

        // The cube is SHARED with the pause menu: one object that travels here from
        // the middle of the menu, rather than a second cube appearing where the first
        // one vanished. Subscribed once, for this screen's whole life — it is a
        // DontDestroyOnLoad singleton, so there is no second subscription to make.
        _cube = SettingsCube.EnsureShared();
        if (_cube != null) _cube.FaceChosen += SelectSection;

        BuildUI();
        OnOpenChanged();
    }

    void OnDestroy()
    {
        if (_cube != null) _cube.FaceChosen -= SelectSection;
    }

    void Update()
    {
        if (!_open) return;

        // Asked for every frame, BEFORE the rebind branch below returns — the cube
        // has to stay put while the player is capturing a key, and a request skipped
        // for even one frame reads to the shell as "nobody wants me" and hides it.
        SettingsCube.ShowAt(CubeSlot, CubeBox, Sections);

        if (_guides != null)
            _guides.alpha = Mathf.Lerp(_guides.alpha, 1f,
                                       1f - Mathf.Exp(-GuideFade * Time.unscaledDeltaTime));

        // Rebinding owns the keyboard while it's listening — including Escape, which
        // cancels the rebind rather than closing the whole screen.
        if (_listeningForKey) { UpdateRebind(); return; }

        if (Input.GetKeyDown(KeyCode.Escape) || GamepadInput.CancelDown) Open = false;
    }

    void OnOpenChanged()
    {
        if (_canvas != null) _canvas.enabled = _open;
        if (_open)
        {
            // The guides start absent and are drawn in as the cube travels over. They
            // are setting-out marks for something that is not there yet — drawing them
            // first would be marking out an empty space.
            if (_guides != null) _guides.alpha = 0f;
            RefreshAll();
            FocusSection();
        }
        else
        {
            EventSystem.current?.SetSelectedGameObject(null);

            // Deliberately does NOT unfold. Settings was opened FROM the menu, and
            // the menu is still there on the same paper — unfolding here would drop
            // the player two levels out for a one-level "back", and the menu they
            // were using would vanish without being dismissed.
            //
            // PauseMenu.CloseMenu owns the unfold, because the fold belongs to the
            // menu as a whole rather than to any page inside it.
        }
    }

    // Called both by the cube (FaceChosen) and by code. Turning the cube from here
    // is guarded on the value already matching, so the event and the setter cannot
    // ping-pong.
    void SelectSection(int i)
    {
        if (i < 0 || i >= _sectionRoots.Length) return;

        _section = i;
        for (int s = 0; s < _sectionRoots.Length; s++) _sectionRoots[s].SetActive(s == i);

        if (_cubeCaption != null) _cubeCaption.text = Sections[i];

        // Only turn the cube when something ELSE asked for this section — when the
        // cube itself raised it, it is already facing the right way and re-driving it
        // would fight the rotation the player is mid-way through.
        if (_cube != null && _cube.Current != i) _cube.Select(i);

        FocusSection();
    }

    void FocusSection()
    {
        var go = _section switch
        {
            0 => _firstControlAudio,
            1 => _firstControlDisplay,
            _ => _firstControlControls,
        };
        if (go != null) EventSystem.current?.SetSelectedGameObject(go);
    }

    // ── Live-refresh widgets from GameSettings (called on open + after Reset) ──
    Slider _masterSlider, _musicSlider, _sfxSlider, _panSlider, _lookSlider;
    Toggle _fullscreenToggle, _vsyncToggle, _smoothEditToggle, _freeMoveToggle;
    TMP_Text _qualityLabel, _frameCapLabel;
    int _qualityIndex, _frameCapIndex;

    void RefreshAll()
    {
        _masterSlider.SetValueWithoutNotify(GameSettings.MasterVolume);
        _musicSlider.SetValueWithoutNotify(GameSettings.MusicVolume);
        _sfxSlider.SetValueWithoutNotify(GameSettings.SfxVolume);
        _panSlider.SetValueWithoutNotify(GameSettings.CameraPanSpeed);
        _lookSlider.SetValueWithoutNotify(GameSettings.LookSensitivity);
        _fullscreenToggle.SetIsOnWithoutNotify(GameSettings.Fullscreen);
        _vsyncToggle.SetIsOnWithoutNotify(GameSettings.VSync);
        _smoothEditToggle.SetIsOnWithoutNotify(GameSettings.SmoothBlockEditing);
        _freeMoveToggle.SetIsOnWithoutNotify(GameSettings.FreeMove);

        _qualityIndex = Mathf.Clamp(GameSettings.QualityLevel, 0, QualitySettings.names.Length - 1);
        _qualityLabel.text = QualitySettings.names.Length > 0 ? QualitySettings.names[_qualityIndex] : "-";

        _frameCapIndex = Mathf.Max(0, System.Array.IndexOf(GameSettings.FrameCaps, GameSettings.FrameCap));
        _frameCapLabel.text = FrameLabels[Mathf.Clamp(_frameCapIndex, 0, FrameLabels.Length - 1)];

        _listeningForKey = false;   // never reopen mid-capture
        RefreshRebindLabel();
    }

    // ── UI build ──────────────────────────────────────────────────────────────
    void BuildUI()
    {
        var canvasGo = new GameObject("SettingsCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 850;   // above Pause (800), below LevelClearScreen/Intro
        var sc = canvasGo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;
        EnsureEventSystem();

        var dim = NewRect("Dim", canvasGo.transform);
        dim.anchorMin = Vector2.zero; dim.anchorMax = Vector2.one;
        dim.offsetMin = dim.offsetMax = Vector2.zero;
        dim.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.05f, 0.06f, 0.78f);

        var panel = NewRect("Panel", canvasGo.transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(PanelW, PanelH);
        var bg = panel.gameObject.AddComponent<Image>();
        bg.color = GeoPalette.Paper;
        bg.sprite = UIRoundedRect.Get(24);
        bg.type = Image.Type.Sliced;

        // The page's fixed rules — the spine, the header rule, the box round the
        // content. Drawn first, because everything else is placed against them.
        BuildFrame(panel);

        // ESC, in the top-left corner. The way out belongs in the corner you already
        // look at to work out where you are, and labelling it with the key that also
        // does the job means it never has to be explained.
        BuildEscChip(panel);

        // SETTING, in big hollow capitals, deliberately CROSSING the spine.
        //
        // Outlined rather than filled: at this size a solid word is a black slab that
        // outweighs everything underneath it, while an outline is a drawn letter — the
        // same construction language as the rules and the corner brackets. And running
        // it across the divider is what stops the two columns reading as two separate
        // panels that happen to be touching.
        var titleT = NewText("Title", panel, 96f, Color.white, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        titleT.text = "SETTING";
        titleT.characterSpacing = 6f;
        HollowOut(titleT);
        var trt = titleT.rectTransform;
        trt.anchorMin = trt.anchorMax = new Vector2(0f, 1f); trt.pivot = new Vector2(0f, 1f);
        trt.anchoredPosition = new Vector2(TitleX, -10f);
        trt.sizeDelta = new Vector2(PanelW - TitleX - Margin, 118f);

        // The cube's landing slot, centred in the left column.
        //
        // Nothing three-dimensional is built here: the cube lives on its own canvas so
        // it can travel in from the middle of the pause menu. What this page owns is
        // the space it arrives in — the brackets round it and the line tying it to the
        // spine.
        BuildCubeGuides(panel);

        // The name of the face currently forward, under the cube. Three-quarter view
        // shows several labels at once and turns the front one off-square, so the
        // plainly readable copy lives here.
        _cubeCaption = NewText("CubeCaption", panel, 26f, GeoPalette.Ink,
                               FontStyles.Bold, TextAlignmentOptions.Center);
        var capRt = _cubeCaption.rectTransform;
        capRt.anchorMin = capRt.anchorMax = new Vector2(0f, 1f);
        capRt.pivot     = new Vector2(0f, 1f);
        capRt.anchoredPosition = new Vector2(CubeBoxX, -(CubeBoxY + CubeBox + 26f));
        capRt.sizeDelta = new Vector2(CubeBox, 34f);

        // Right column, inset from the box that frames it.
        var content = NewRect("Content", panel);
        content.anchorMin = new Vector2(0f, 0f); content.anchorMax = new Vector2(1f, 1f);
        content.offsetMin = new Vector2(ContentX, ContentBot + 18f);
        content.offsetMax = new Vector2(-(Margin + 18f), -ContentTop);

        _sectionRoots[0] = BuildAudioSection(content);
        _sectionRoots[1] = BuildDisplaySection(content);
        _sectionRoots[2] = BuildControlsSection(content);

        // RESET, at the foot of the LEFT column and the width of the cube above it.
        //
        // It belongs to the cube's side of the spine: it acts on the settings, not on
        // the page. The page's own exit is the ESC chip in the corner — there is no
        // longer a BACK button down here, because two ways out at opposite corners is
        // one way out too many.
        var resetBtn = BuildTextButton(panel, "RESET", false, () =>
        {
            GameSettings.ResetDefaults();   // saves + applies internally
            RefreshAll();
        });
        var brt = resetBtn.GetComponent<RectTransform>();
        brt.anchorMin = brt.anchorMax = new Vector2(0f, 0f); brt.pivot = new Vector2(0f, 0f);
        brt.anchoredPosition = new Vector2(CubeBoxX, 34f);
        brt.sizeDelta = new Vector2(CubeBox, 48f);

        SelectSection(0);
        _canvas.enabled = false;
    }

    GameObject BuildAudioSection(RectTransform parent)
    {
        var root = NewRect("Audio", parent);
        StretchFull(root);
        var vlg = root.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 22f;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        _masterSlider = BuildSliderRow(root, "Master", 0f, 1f, v => { GameSettings.MasterVolume = v; GameSettings.ApplyAudio(); GameSettings.Save(); });
        _musicSlider  = BuildSliderRow(root, "Music",  0f, 1f, v => { GameSettings.MusicVolume  = v; GameSettings.ApplyAudio(); GameSettings.Save(); });
        _sfxSlider    = BuildSliderRow(root, "SFX",    0f, 1f, v => { GameSettings.SfxVolume    = v; GameSettings.ApplyAudio(); GameSettings.Save(); });

        _firstControlAudio = _masterSlider.gameObject;
        return root.gameObject;
    }

    GameObject BuildDisplaySection(RectTransform parent)
    {
        var root = NewRect("Display", parent);
        StretchFull(root);
        var vlg = root.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 22f;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        _fullscreenToggle = BuildToggleRow(root, "Fullscreen", null, v => { GameSettings.Fullscreen = v; GameSettings.ApplyDisplay(); GameSettings.Save(); });
        _vsyncToggle      = BuildToggleRow(root, "V-Sync",     null, v => { GameSettings.VSync      = v; GameSettings.ApplyDisplay(); GameSettings.Save(); });

        BuildChoiceRow(root, "Quality", out _qualityLabel,
            () => { _qualityIndex = (_qualityIndex - 1 + QualitySettings.names.Length) % QualitySettings.names.Length; ApplyQuality(); },
            () => { _qualityIndex = (_qualityIndex + 1) % QualitySettings.names.Length; ApplyQuality(); });

        BuildChoiceRow(root, "Frame cap", out _frameCapLabel,
            () => { _frameCapIndex = (_frameCapIndex - 1 + FrameLabels.Length) % FrameLabels.Length; ApplyFrameCap(); },
            () => { _frameCapIndex = (_frameCapIndex + 1) % FrameLabels.Length; ApplyFrameCap(); });

        _firstControlDisplay = _fullscreenToggle.gameObject;
        return root.gameObject;
    }

    // One ink rule, measured from its parent's TOP-LEFT corner — the same origin the
    // whole page is laid out from, so the marks land on the layout instead of near it.
    void Rule(RectTransform root, float x, float y, float w, float h, float a)
    {
        var rt = NewRect("Rule", root);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = GeoPalette.WithAlpha(GeoPalette.Ink, a);
        img.raycastTarget = false;
    }

    // The page's permanent rules.
    //
    // Separate from the cube's guides below, and deliberately NOT faded with them:
    // these are the page itself, not marks for something that is on its way. Fading
    // the spine in every time settings opened would say the layout was still being
    // decided.
    void BuildFrame(RectTransform panel)
    {
        var root = NewRect("Frame", panel);
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;

        const float T = 2f;

        // The spine, full height: cube on one side, what it controls on the other.
        Rule(root, DividerX, Margin, T, PanelH - Margin * 2f, 0.30f);

        // Short rule closing the left column's header, under the ESC chip.
        Rule(root, Margin, HeaderY, DividerX - Margin, T, 0.22f);

        // The content box. Four rules rather than a sliced sprite, so it is the same
        // weight of line as every other mark on the page — a border drawn in a
        // different medium is a border that reads as a different drawing.
        float cl = ContentX - 18f, cr = PanelW - Margin;
        float ct = ContentTop - 18f, cb = PanelH - ContentBot;
        Rule(root, cl,     ct, cr - cl, T,       0.22f);
        Rule(root, cl,     cb, cr - cl, T,       0.22f);
        Rule(root, cl,     ct, T,       cb - ct, 0.22f);
        Rule(root, cr - T, ct, T,       cb - ct, 0.22f);
    }

    // The marks around the cube's slot.
    //
    // Deliberately NOT a closed box: an open frame reads as construction lines, a
    // closed one reads as a picture border and would fight the content box across the
    // spine. Faded in from nothing while the cube travels over (see Update) — they
    // are setting-out marks for a solid that has not landed yet.
    void BuildCubeGuides(RectTransform panel)
    {
        var root = NewRect("Guides", panel);
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        _guides = root.gameObject.AddComponent<CanvasGroup>();
        _guides.alpha          = 0f;
        _guides.interactable   = false;
        _guides.blocksRaycasts = false;

        const float T = 2f, Run = 62f, Out = 18f;

        float l = CubeBoxX - Out;
        float r = CubeBoxX + CubeBox + Out;
        float t = CubeBoxY - Out;
        float b = CubeBoxY + CubeBox + Out;

        // Four corner brackets around the slot.
        Rule(root, l,       t,       Run, T,   0.34f);
        Rule(root, l,       t,       T,   Run, 0.34f);
        Rule(root, r - Run, t,       Run, T,   0.34f);
        Rule(root, r - T,   t,       T,   Run, 0.34f);
        Rule(root, l,       b,       Run, T,   0.34f);
        Rule(root, l,       b - Run, T,   Run, 0.34f);
        Rule(root, r - Run, b,       Run, T,   0.34f);
        Rule(root, r - T,   b - Run, T,   Run, 0.34f);

        // The tie: out of the cube's right-hand side and into the spine. Without it
        // the cube is a picture in the margin; with it, it is plainly the thing the
        // column on the other side answers to.
        float mid = CubeBoxY + CubeBox * 0.5f;
        Rule(root, r, mid, DividerX - r, T, 0.26f);

        // Ticks down the spine at roughly the row pitch. Graph paper, essentially —
        // the marks a drawing is set out on.
        for (int i = 0; i < 7; i++)
            Rule(root, DividerX - 9f, ContentTop + 24f + i * 68f, 18f, T, 0.14f);
    }

    // Turn a TMP label into a drawn outline: hollow face, ink rule round the glyphs.
    //
    // Goes through fontMaterial FIRST, and that is the whole point of the method.
    // TMP_Text.faceColor / outlineColor / outlineWidth write straight to
    // m_sharedMaterial — which, until you have touched fontMaterial, IS the font
    // asset's material. Setting them on a fresh label repaints every other label in
    // the game that uses the same font, and nothing on screen points back to here as
    // the cause. Reading fontMaterial once swaps in an instance of our own, and the
    // properties then land where they should.
    static void HollowOut(TMP_Text t)
    {
        var mat = t.fontMaterial;
        if (mat == null) return;

        // A faint wash rather than nothing at all. Defensive: if a font asset's shader
        // has no outline pass, a fully transparent face would render the title
        // INVISIBLE — and a title that vanishes on one font is a far worse failure
        // than one that is very slightly filled.
        mat.SetColor(ShaderUtilities.ID_FaceColor,    new Color(0.1f, 0.1f, 0.1f, 0.08f));
        mat.SetColor(ShaderUtilities.ID_OutlineColor, GeoPalette.Ink);
        mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.16f);

        // Without this the outline is clipped by the glyphs' own mesh padding, which
        // TMP sizes for the face alone — the letters come out with their strokes
        // shaved flat on the outside.
        t.UpdateMeshPadding();
    }

    // ESC, top left: the way out, labelled with the key that also does it.
    void BuildEscChip(RectTransform panel)
    {
        var rt = NewRect("Esc", panel);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot     = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(Margin, -20f);
        rt.sizeDelta = new Vector2(104f, 50f);

        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = UIRoundedRect.Get(10); img.type = Image.Type.Sliced;
        img.color  = new Color(0f, 0f, 0f, 0.10f);

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors; colors.highlightedColor = GeoPalette.Gold; btn.colors = colors;
        btn.onClick.AddListener(() => Open = false);

        var t = NewText("Label", rt, 24f, GeoPalette.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        t.text = "ESC";
        var lrt = t.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
    }

    void ApplyQuality()
    {
        _qualityLabel.text = QualitySettings.names.Length > 0 ? QualitySettings.names[_qualityIndex] : "-";
        GameSettings.QualityLevel = _qualityIndex;
        GameSettings.ApplyDisplay(); GameSettings.Save();
    }

    void ApplyFrameCap()
    {
        _frameCapLabel.text = FrameLabels[_frameCapIndex];
        GameSettings.FrameCap = GameSettings.FrameCaps[Mathf.Clamp(_frameCapIndex, 0, GameSettings.FrameCaps.Length - 1)];
        GameSettings.ApplyDisplay(); GameSettings.Save();
    }

    GameObject BuildControlsSection(RectTransform parent)
    {
        var root = NewRect("Controls", parent);
        StretchFull(root);
        var vlg = root.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 22f;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        _panSlider  = BuildSliderRow(root, "Camera pan speed", 2f, 20f,  v => { GameSettings.CameraPanSpeed  = v; GameSettings.ApplyInput(); GameSettings.Save(); });
        _lookSlider = BuildSliderRow(root, "Look sensitivity", 40f, 300f, v => { GameSettings.LookSensitivity = v; GameSettings.ApplyInput(); GameSettings.Save(); });
        _smoothEditToggle = BuildToggleRow(root, "Smooth block editing",
            "Held blocks glide between cells instead of snapping instantly.",
            v => { GameSettings.SmoothBlockEditing = v; GameSettings.Save(); });
        _freeMoveToggle   = BuildToggleRow(root, "Free move",
            "Off: the held block snaps to the nearest cell touching your build. On: it follows the mouse freely.",
            v => { GameSettings.FreeMove = v; GameSettings.Save(); });

        BuildRebindRow(root, "Fast forward", "Cycles game speed — the same as the fast forward button.");

        _firstControlControls = _panSlider.gameObject;
        return root.gameObject;
    }

    // ── Key rebinding ─────────────────────────────────────────────────────────

    TMP_Text _rebindLabel;
    bool     _listeningForKey;

    void BuildRebindRow(RectTransform parent, string label, string description)
    {
        var row = NewRect("Row", parent);
        row.gameObject.AddComponent<LayoutElement>().minHeight = 62f;
        var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 16f; hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

        var textBlock = NewRect("Text", row);
        textBlock.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var vlg = textBlock.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 2f; vlg.childAlignment = TextAnchor.MiddleLeft;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        var labelT = NewText("Label", textBlock, 22f, GeoPalette.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        labelT.text = label;
        var descT = NewText("Desc", textBlock, 15f, GeoPalette.WithAlpha(GeoPalette.Ink, 0.6f), FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        descT.text = description;
        descT.textWrappingMode = TextWrappingModes.Normal;

        var btnRt = NewRect("Bind", row);
        btnRt.gameObject.AddComponent<LayoutElement>().preferredWidth = 130f;
        var bg = btnRt.gameObject.AddComponent<Image>();
        bg.sprite = UIRoundedRect.Get(12); bg.type = Image.Type.Sliced;
        bg.color = new Color(0f, 0f, 0f, 0.18f);

        var btn = btnRt.gameObject.AddComponent<Button>();
        btn.targetGraphic = bg;
        var colors = btn.colors; colors.highlightedColor = GeoPalette.Gold; btn.colors = colors;
        btn.onClick.AddListener(() => { _listeningForKey = true; RefreshRebindLabel(); });

        _rebindLabel = NewText("Key", btnRt, 20f, GeoPalette.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        StretchFull(_rebindLabel.rectTransform);
        RefreshRebindLabel();
    }

    void RefreshRebindLabel()
    {
        if (_rebindLabel == null) return;
        _rebindLabel.text = _listeningForKey ? "Press a key…" : GameSettings.FastForwardKey.ToString();
    }

    // Polls every key while listening. Uses Input.GetKeyDown over the whole KeyCode
    // range rather than a fixed candidate list, so any key the player reaches for is
    // offered — and then refused by name if it's already spoken for, which is more
    // useful than silently not responding.
    void UpdateRebind()
    {
        if (!_listeningForKey) return;

        if (Input.GetKeyDown(KeyCode.Escape))   // back out without changing anything
        {
            _listeningForKey = false;
            RefreshRebindLabel();
            return;
        }

        foreach (KeyCode k in System.Enum.GetValues(typeof(KeyCode)))
        {
            if (k == KeyCode.None || !Input.GetKeyDown(k)) continue;
            // Mouse buttons would collide with clicking the button itself.
            if (k >= KeyCode.Mouse0 && k <= KeyCode.Mouse6) continue;

            if (!GameSettings.IsKeyAvailable(k))
            {
                _rebindLabel.text = $"{k} taken";
                return;   // stay in listening mode so they can just try another
            }

            GameSettings.FastForwardKey = k;
            GameSettings.Save();
            _listeningForKey = false;
            RefreshRebindLabel();
            return;
        }
    }

    // ── Row builders ──────────────────────────────────────────────────────────
    static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    Slider BuildSliderRow(RectTransform parent, string label, float min, float max, System.Action<float> onChanged)
    {
        var row = NewRect("Row", parent);
        row.gameObject.AddComponent<LayoutElement>().minHeight = 54f;
        var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 16f; hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

        var labelT = NewText("Label", row, 22f, GeoPalette.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        labelT.text = label;
        labelT.gameObject.AddComponent<LayoutElement>().preferredWidth = 260f;

        var sliderRt = NewRect("Slider", row);
        var sliderLe = sliderRt.gameObject.AddComponent<LayoutElement>();
        sliderLe.flexibleWidth = 1f;
        sliderLe.preferredHeight = 20f;   // HorizontalLayoutGroup controls height — give it one, or it collapses to 0
        var slider = BuildSlider(sliderRt, min, max);
        slider.onValueChanged.AddListener(v => onChanged(v));
        return slider;
    }

    Toggle BuildToggleRow(RectTransform parent, string label, string description, System.Action<bool> onChanged)
    {
        var row = NewRect("Row", parent);
        row.gameObject.AddComponent<LayoutElement>().minHeight = string.IsNullOrEmpty(description) ? 46f : 62f;
        var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 16f; hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

        // Text block takes all the flexible space, which is what actually pushes
        // the toggle to sit flush against the row's right edge instead of right
        // after a fixed-width label.
        var textBlock = NewRect("Text", row);
        textBlock.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var vlg = textBlock.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 2f; vlg.childAlignment = TextAnchor.MiddleLeft;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        var labelT = NewText("Label", textBlock, 22f, GeoPalette.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        labelT.text = label;

        if (!string.IsNullOrEmpty(description))
        {
            var descT = NewText("Desc", textBlock, 15f, GeoPalette.WithAlpha(GeoPalette.Ink, 0.6f), FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            descT.text = description;
            descT.textWrappingMode = TextWrappingModes.Normal;
        }

        var toggleRt = NewRect("Toggle", row);
        toggleRt.gameObject.AddComponent<LayoutElement>().preferredWidth = 60f;
        var bgImg = toggleRt.gameObject.AddComponent<Image>();
        bgImg.sprite = UIRoundedRect.Get(14); bgImg.type = Image.Type.Sliced;
        bgImg.color = new Color(0f, 0f, 0f, 0.18f);

        var checkRt = NewRect("Check", toggleRt);
        checkRt.anchorMin = Vector2.zero; checkRt.anchorMax = Vector2.one;
        checkRt.offsetMin = new Vector2(4f, 4f); checkRt.offsetMax = new Vector2(-4f, -4f);
        var checkImg = checkRt.gameObject.AddComponent<Image>();
        checkImg.sprite = UIRoundedRect.Get(10); checkImg.type = Image.Type.Sliced;
        checkImg.color = GeoPalette.Signal;

        var toggle = toggleRt.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = bgImg;
        toggle.graphic = checkImg;
        toggle.onValueChanged.AddListener(v => onChanged(v));
        return toggle;
    }

    void BuildChoiceRow(RectTransform parent, string label, out TMP_Text valueLabel,
                         System.Action onPrev, System.Action onNext)
    {
        var row = NewRect("Row", parent);
        row.gameObject.AddComponent<LayoutElement>().minHeight = 46f;
        var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 16f; hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

        var labelT = NewText("Label", row, 22f, GeoPalette.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        labelT.text = label;
        labelT.gameObject.AddComponent<LayoutElement>().preferredWidth = 260f;

        BuildSmallArrowButton(row, "‹", onPrev);   // ‹

        var valueT = NewText("Value", row, 20f, GeoPalette.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        valueT.gameObject.AddComponent<LayoutElement>().preferredWidth = 140f;
        valueLabel = valueT;

        BuildSmallArrowButton(row, "›", onNext);   // ›
    }

    void BuildSmallArrowButton(RectTransform parent, string glyph, System.Action onClick)
    {
        var rt = NewRect("Arrow", parent);
        rt.gameObject.AddComponent<LayoutElement>().preferredWidth = 40f;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = UIRoundedRect.Get(10); img.type = Image.Type.Sliced;
        img.color = new Color(0f, 0f, 0f, 0.12f);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors; colors.highlightedColor = GeoPalette.Gold; btn.colors = colors;
        btn.onClick.AddListener(() => onClick());

        var t = NewText("Label", rt, 22f, GeoPalette.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        t.text = glyph;
        var lrt = t.rectTransform; lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
    }

    Slider BuildSlider(RectTransform parent, float min, float max)
    {
        var bgRt = NewRect("Track", parent);
        bgRt.anchorMin = new Vector2(0f, 0.5f); bgRt.anchorMax = new Vector2(1f, 0.5f);
        bgRt.sizeDelta = new Vector2(0f, 4f);
        var bgImg = bgRt.gameObject.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.16f);

        var fillArea = NewRect("Fill Area", parent);
        fillArea.anchorMin = new Vector2(0f, 0.5f); fillArea.anchorMax = new Vector2(1f, 0.5f);
        fillArea.sizeDelta = new Vector2(-10f, 4f);
        var fillRt = NewRect("Fill", fillArea);
        // Fixed full-stretch rect, never resized — Slider's normal fill mode works by
        // rewriting fillRect's anchors each frame (anchorMin=(0,0), anchorMax=(value,1)
        // ALWAYS, regardless of what's set here), which made the fill inherit a
        // completely different — and much taller — effective height than Track. Using
        // Image.Type.Filled sidesteps that: Slider only sets fillAmount on this type,
        // the RectTransform itself is untouched, so it stays exactly 4px tall.
        fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
        var fillImg = fillRt.gameObject.AddComponent<Image>();
        fillImg.color = GeoPalette.Signal;
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;

        var handleArea = NewRect("Handle Slide Area", parent);
        handleArea.anchorMin = Vector2.zero; handleArea.anchorMax = Vector2.one;
        handleArea.offsetMin = new Vector2(5f, 0f); handleArea.offsetMax = new Vector2(-5f, 0f);
        var handleRt = NewRect("Handle", handleArea);
        // Slider only ever overwrites anchorMin.x/anchorMax.x when it moves the handle —
        // the Y anchor is left at NewRect's default (0,0), i.e. the BOTTOM edge of the
        // slide area, not the middle. Left unset, the handle renders pinned to the
        // bottom of the track instead of centred on it.
        handleRt.anchorMin = handleRt.anchorMax = new Vector2(0f, 0.5f);
        handleRt.pivot = new Vector2(0.5f, 0.5f);
        handleRt.sizeDelta = new Vector2(12f, 16f);
        var handleImg = handleRt.gameObject.AddComponent<Image>();
        handleImg.sprite = UIRoundedRect.Get(4); handleImg.type = Image.Type.Sliced;
        handleImg.color = GeoPalette.Gold;

        var slider = parent.gameObject.AddComponent<Slider>();
        slider.fillRect = fillRt;
        slider.handleRect = handleRt;
        slider.targetGraphic = handleImg;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min; slider.maxValue = max;
        return slider;
    }

    // Unused since the tabs became the cube's faces. Kept because it is the only
    // description of the panel's button style, and the next control that needs one
    // should look like this rather than inventing a fifth variant.
    Button BuildTabButton(RectTransform parent, string label, System.Action onClick)
    {
        var rt = NewRect("Tab", parent);
        rt.gameObject.AddComponent<LayoutElement>().minHeight = 50f;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = UIRoundedRect.Get(12); img.type = Image.Type.Sliced;
        img.color = new Color(0f, 0f, 0f, 0.08f);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors; colors.highlightedColor = GeoPalette.Gold; colors.pressedColor = GeoPalette.Signal; btn.colors = colors;
        btn.onClick.AddListener(() => onClick());

        var t = NewText("Label", rt, 20f, GeoPalette.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        t.text = label;
        var lrt = t.rectTransform; lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(16f, 0f); lrt.offsetMax = Vector2.zero;
        return btn;
    }

    Button BuildTextButton(RectTransform parent, string label, bool primary, System.Action onClick)
    {
        var go = new GameObject("Button", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        var img = go.AddComponent<Image>();
        img.sprite = UIRoundedRect.Get(12); img.type = Image.Type.Sliced;
        img.color = primary ? GeoPalette.Ink : new Color(0f, 0f, 0f, 0.12f);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors; colors.highlightedColor = GeoPalette.Gold; btn.colors = colors;
        btn.onClick.AddListener(() => onClick());

        var t = NewText("Label", rt, 20f, primary ? GeoPalette.Paper : GeoPalette.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        t.text = label;
        var lrt = t.rectTransform; lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        return btn;
    }

    static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        new GameObject("EventSystem",
            typeof(EventSystem),
            typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
    }

    RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    TMP_Text NewText(string name, Transform parent, float size, Color color, FontStyles style, TextAlignmentOptions align)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize = size; t.color = color; t.fontStyle = style; t.alignment = align;
        t.raycastTarget = false; t.richText = true;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }
}
