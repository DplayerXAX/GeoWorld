using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// Two separate top-right controls:
//
//   • PLANNING PAUSE (pause icon / Esc) — sets Time.timeScale = 0 and dims the
//     screen with a NON-blocking translucent overlay. You can still orbit/zoom the
//     camera, open the shop (F) and place blocks — the world is just frozen in time.
//     Camera / placement / shop run on unscaled time so they stay responsive at 0×.
//
//   • SYSTEM MENU (gear icon) — the old pause menu (Settings / Restart / Back to
//     Title / Back to Select Level / Quit). Modal: blocking overlay, also freezes
//     time, and hides the HUD (PauseMenu.Paused) like the old pause did.
//
// Same house style as LevelClearScreen / DialogueRunner: Canvas built in code,
// NewRect/NewText helpers.
public class PauseMenu : MonoBehaviour
{
    [Header("Hotkey")]
    public KeyCode toggleKey = KeyCode.Escape;

    [Tooltip("Scene loaded by the 'Back to Title' button (must be in Build Settings).")]
    public string titleScene = "Title";

    [Tooltip("Scene loaded by the 'Back to Select Level' button (must be in Build Settings).")]
    public string levelSelectScene = "LevelSelect";

    [Header("Look")]
    [Tooltip("Blocking dim behind the system menu.")]
    public Color overlayColor = new Color(0f, 0f, 0f, 0.6f);
    [Tooltip("Non-blocking dim shown during a planning pause — light enough to still read the board.")]
    public Color pauseDimColor = new Color(0f, 0f, 0f, 0.35f);
    [Tooltip("No longer used — the menu is a ring around the cube, sized by HexRadius. Kept so the scene's serialized value doesn't move.")]
    public float panelWidth = 500f;
    public float buttonHeight = 58f;

    [Header("Top-right controls")]
    public bool showControls = true;

    [Header("Top-right icons (sprites)")]
    [Tooltip("Shown while running (click = planning pause).")]
    public Sprite pauseIcon;
    [Tooltip("Shown while planning-paused (click = resume).")]
    public Sprite resumeIcon;
    [Tooltip("Speed-cycle button icon.")]
    public Sprite fastForwardIcon;
    [Tooltip("Optional gear icon for the system menu (null = a '≡' text chip).")]
    public Sprite settingsIcon;
    [Tooltip("Speed-level pip, lit.")]
    public Sprite speedPipFilled;
    [Tooltip("Speed-level pip, unlit.")]
    public Sprite speedPipEmpty;

    [Header("Top-right icon colors")]
    [Tooltip("Icon tint while the shop (black letterbox bar) is open.")]
    public Color iconColorOnShop = Color.white;
    [Tooltip("Icon tint the rest of the time.")]
    public Color iconColorDefault = Color.black;

    bool _paused;      // planning pause (non-blocking)
    bool _menuOpen;    // system menu (modal)
    float _prevTimeScale = 1f;

    Canvas _canvas;
    GameObject _pauseDimGo, _overlayGo, _panelGo, _controlsGo;
    Button _firstMenuButton;
    Image _pauseIconImg;
    Image _fastForwardIconImg, _settingsIconImg;
    TMP_Text _settingsGlyph;
    Image[] _speedPips = new Image[3];

    public bool IsPaused => _paused;
    // True only for the MODAL system menu — HUD / tutorial hint hide on this, but
    // NOT during a planning pause (you want the HUD while building).
    public static bool Paused;

    void Awake() => BuildUI();

    void Update()
    {
        if (IntroDirector.Playing) { SetCanvasVisible(false); return; }

        // A minigame overlay owns Esc for its own "leave" — without this it and the
        // pause menu both consume the same keypress in the same frame, so leaving
        // the minigame dumped you straight into a paused map.
        if ((Input.GetKeyDown(toggleKey) || GamepadInput.TogglePauseDown)
            && !SettingsScreen.Open && !MinigameStage.AnyActive)
        {
            if (_menuOpen) CloseMenu();          // Esc backs out of the menu first
            else           SetPaused(!_paused);  // otherwise toggles the planning pause
        }

        // Fast-forward hotkey — literally the chip's own handler, so the two can
        // never drift apart. Blocked while the settings screen is up (it may be
        // listening for this very key to rebind it) and under a minigame overlay.
        if (Input.GetKeyDown(GameSettings.FastForwardKey)
            && !SettingsScreen.Open && !MinigameStage.AnyActive)
            CycleSpeed();

        _controlsGo.SetActive(showControls);
        if (showControls) UpdateTopRightControls();

        // Keep time frozen while paused even if another system (GameFlowManager
        // phase changes, DevPanel) tries to set a speed — the pause is authoritative
        // until the player resumes.
        if ((_paused || _menuOpen) && Time.timeScale != 0f) Time.timeScale = 0f;

        // Planning-pause dim: on whenever paused, even under the menu.
        _pauseDimGo.SetActive(_paused);
        bool showMenu = _menuOpen && !SettingsScreen.Open;
        _overlayGo.SetActive(showMenu);
        _panelGo.SetActive(showMenu);

        // The cube. In the middle of the ring while nothing is picked out, and
        // tucked under whichever option the player is pointing at otherwise — the
        // slide is the cube's own easing, so this only has to say where.
        //
        // Guarded on !SettingsScreen.Open so the two screens never fight over it in
        // the same frame: while settings is up, THAT page is the one asking, and the
        // cube is over on its left-hand side.
        if (showMenu)
        {
            bool focused = _focusedOption >= 0;
            SettingsCube.ShowAt(focused ? HexCubeSlot(_focusedOption) : Vector2.zero,
                                focused ? MenuFocusCubeSize : MenuCubeSize, null);
        }
        else _focusedOption = -1;   // including while settings is up, so coming back lands it home

        SetCanvasVisible(true);
    }

    void SetCanvasVisible(bool visible)
    {
        if (_canvas != null) _canvas.enabled = visible;
    }

    void OnDisable()
    {
        if (_paused || _menuOpen) Time.timeScale = _prevTimeScale;
        _paused = _menuOpen = false;
        Paused = false;
    }

    // Time is frozen while EITHER state is active; restored to the pre-pause speed
    // only once both are cleared.
    void ApplyTimeScale()
    {
        if (_paused || _menuOpen) Time.timeScale = 0f;
        else                      Time.timeScale = _prevTimeScale;
    }

    public void SetPaused(bool paused)
    {
        if (paused == _paused) return;
        if (paused && !_menuOpen) _prevTimeScale = Time.timeScale > 0.0001f ? Time.timeScale : 1f;
        _paused = paused;
        ApplyTimeScale();
    }

    // The gear folds the screen away, and the menu arrives on the paper it leaves
    // behind. Closing unfolds it again.
    //
    // The fold belongs HERE, on the icon, rather than on the Settings row inside the
    // menu: by the time that row is reachable the screen is already put away, and
    // folding a second time would be folding paper into paper. It is also what the
    // whole gesture is for — the game going quiet, not one particular page opening.
    void ToggleMenu()
    {
        if (CubeWipe.Busy) return;

        if (_menuOpen) { CloseMenu(); return; }

        // Say where the cube will land BEFORE the fold starts. The wipe spends the
        // whole close shrinking the cube's silhouette down onto this slot, and it
        // cannot ask the menu — the menu does not exist yet.
        SettingsCube.PrepareSlot(Vector2.zero, MenuCubeSize);
        CubeWipe.Close(() => SetMenuOpen(true));
    }

    // Every way out of the menu goes through here, so the unfold cannot be forgotten
    // on one of them — a close with no matching open is how a game ends up stuck on a
    // white screen.
    void CloseMenu()
    {
        SetMenuOpen(false);
        CubeWipe.Open();
    }

    // Inside the menu the screen is already folded away, so this just swaps the page.
    void OpenSettings() => SettingsScreen.Open = true;

    void SetMenuOpen(bool open)
    {
        if (open == _menuOpen) return;
        if (open && !_paused) _prevTimeScale = Time.timeScale > 0.0001f ? Time.timeScale : 1f;
        _menuOpen = open;
        Paused = open;
        ApplyTimeScale();
        EventSystem.current?.SetSelectedGameObject(open && _firstMenuButton != null ? _firstMenuButton.gameObject : null);
    }

    void GoToTitle()
    {
        LeaveMenuHard();
        LoadingScreen.Go(titleScene);
    }

    void GoToLevelSelect()
    {
        LeaveMenuHard();
        LoadingScreen.Go(levelSelectScene);
    }

    // Leaving the menu for somewhere else entirely: restore time, clear the flags,
    // and DROP THE PAPER.
    //
    // These paths used to clear the flags by hand and skip the unfold, which left the
    // wipe's canvas — full screen, sorting order 900, DontDestroyOnLoad — sitting over
    // the game forever. That is what hid the gear, the speed chip and the pause
    // button: they were still there, under a sheet of paper that outlived the scene
    // that put it up.
    //
    // Dismissed instantly rather than animated: the screen is about to be replaced by
    // a scene load, and unfolding into something already going away is a flourish
    // nobody sees.
    void LeaveMenuHard()
    {
        Time.timeScale = 1f;
        _paused = _menuOpen = false;
        Paused = false;
        SettingsScreen.Open = false;
        CubeWipe.Dismiss();
    }

    void CycleSpeed()
    {
        // Speed only means anything while time is actually running.
        float cur = (_paused || _menuOpen) ? _prevTimeScale : Time.timeScale;
        float next = cur >= 2.95f ? 1f : cur >= 1.95f ? 3f : 2f;
        if (_paused || _menuOpen) _prevTimeScale = next;
        else Time.timeScale = next;
    }

    static void QuitGame()
    {
        Time.timeScale = 1f;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void UpdateTopRightControls()
    {
        bool shopOpen = ShopController.Instance != null && ShopController.Instance.IsExpanded;
        Color fg = shopOpen ? iconColorOnShop : iconColorDefault;

        _pauseIconImg.sprite = _paused ? resumeIcon : pauseIcon;
        _pauseIconImg.color  = fg;
        _fastForwardIconImg.color = fg;
        if (_settingsIconImg != null) _settingsIconImg.color = fg;
        if (_settingsGlyph   != null) _settingsGlyph.color   = fg;

        float speed = (_paused || _menuOpen) ? _prevTimeScale : Time.timeScale;
        int level = speed >= 2.95f ? 3 : (speed >= 1.95f ? 2 : 1);
        for (int i = 0; i < _speedPips.Length; i++)
        {
            _speedPips[i].sprite = (i < level) ? speedPipFilled : speedPipEmpty;
            _speedPips[i].color  = fg;
        }
    }

    // ── UI build ──────────────────────────────────────────────────────────────
    void BuildUI()
    {
        var canvasGo = new GameObject("PauseCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 800;   // below LevelClearScreen(900)/Intro(1000), above gameplay HUD
        var sc = canvasGo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;
        EnsureEventSystem();

        // Planning-pause dim — translucent and NON-blocking (clicks fall through to
        // the board / shop so you can keep building while time is frozen).
        _pauseDimGo = NewRect("PauseDim", canvasGo.transform).gameObject;
        var dimRt = (RectTransform)_pauseDimGo.transform;
        dimRt.anchorMin = Vector2.zero; dimRt.anchorMax = Vector2.one;
        dimRt.offsetMin = dimRt.offsetMax = Vector2.zero;
        var dimImg = _pauseDimGo.AddComponent<Image>();
        dimImg.color = pauseDimColor;
        dimImg.raycastTarget = false;
        _pauseDimGo.SetActive(false);

        // Modal dim behind the system menu (blocks input).
        _overlayGo = NewRect("Overlay", canvasGo.transform).gameObject;
        var overlayRt = (RectTransform)_overlayGo.transform;
        overlayRt.anchorMin = Vector2.zero; overlayRt.anchorMax = Vector2.one;
        overlayRt.offsetMin = overlayRt.offsetMax = Vector2.zero;
        // Transparent, but still a raycast target. Its job here is only to stop
        // clicks reaching the frozen game underneath; the DIM it used to provide is
        // now the wipe's paper, and a black sheet over paper just reads as grey.
        _overlayGo.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
        _overlayGo.SetActive(false);

        // The menu: six options at the six corners of the cube's silhouette.
        //
        // A cube seen corner-on IS a hexagon, and a hexagon has exactly six corners —
        // which happens to be exactly how many options this menu has. So the ring is
        // not an arbitrary arrangement chosen to look nice around a decoration; it is
        // the shape of the thing in the middle, and each option sits where one of its
        // corners points.
        var panel = NewRect("Panel", canvasGo.transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(HexRadius * 2.6f, HexRadius * 2.6f);
        _panelGo = panel.gameObject;

        // No background of its own. The wipe has already turned the screen to paper,
        // and a second panel drawn on top of it would put a rectangle where the
        // player can plainly see there is none.
        panel.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

        // Deferred to the end of BuildUI — see the note down there. The chips are
        // built first now, because they are the controls and this is decoration.

        // Order runs clockwise from the top. Destructive last, at the bottom corner,
        // furthest from where the eye starts.
        _firstMenuButton = HexButton(panel, 0, "Close",        GeoPalette.Ink,    CloseMenu);
                           HexButton(panel, 1, "Settings",     GeoPalette.Ink,    OpenSettings);
                           HexButton(panel, 2, "Restart",      GeoPalette.Ink,    () =>
                           {
                               LeaveMenuHard();
                               GameFlowManager.Instance?.RestartGame();
                           });
                           HexButton(panel, 3, "Quit",         GeoPalette.Ink,    QuitGame);
                           HexButton(panel, 4, "Select Level", GeoPalette.Ink,    GoToLevelSelect);
                           HexButton(panel, 5, "Title",        GeoPalette.Ink,    GoToTitle);

        _panelGo.SetActive(false);

        // Top-right chips: [gear] [speed] [pause] on one row.
        _controlsGo = NewRect("TopRightControls", canvasGo.transform).gameObject;
        var crt = (RectTransform)_controlsGo.transform;
        crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(1f, 1f);
        crt.sizeDelta = new Vector2(ChipSize * 3f + ChipGap * 2f + 20f, ChipSize + PipGap + PipRowHeight);
        crt.anchoredPosition = new Vector2(-16f, -16f);

        var hlg = _controlsGo.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = ChipGap; hlg.childAlignment = TextAnchor.UpperRight;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

        BuildSettingsButton(crt, ToggleMenu);
        BuildSpeedButton(crt, CycleSpeed);
        _pauseIconImg = BuildIconButton(crt, pauseIcon, () => SetPaused(!_paused));

        // No cube is built here any more. It is SHARED with the settings page and
        // owned by SettingsCube itself, on a canvas of its own — see the note there.
        // This screen just says where it wants it, from Update, for as long as the
        // menu is up.
    }

    const float ChipSize     = 80f;
    const float ChipGap      = 4f;
    const float PipGap       = -4f;
    const float PipRowHeight = 16f;

    Image BuildChip(RectTransform parent, Sprite icon, System.Action onClick, out Button btn)
    {
        var iconRt = NewRect("Icon", parent);
        iconRt.anchorMin = iconRt.anchorMax = iconRt.pivot = new Vector2(0.5f, 1f);
        iconRt.anchoredPosition = Vector2.zero;
        iconRt.sizeDelta = new Vector2(ChipSize, ChipSize);
        var iconImg = iconRt.gameObject.AddComponent<Image>();
        iconImg.sprite = icon;
        iconImg.preserveAspect = true;
        btn = iconRt.gameObject.AddComponent<Button>();
        btn.targetGraphic = iconImg;
        var colors = btn.colors; colors.highlightedColor = GeoPalette.Gold; colors.pressedColor = GeoPalette.Gold; btn.colors = colors;
        btn.onClick.AddListener(() => onClick());
        return iconImg;
    }

    // Gear/menu chip. Uses settingsIcon if assigned, else a '≡' text glyph so it
    // works with zero art wired up.
    // Distance from the middle to each option. Large enough that the ring clears the
    // cube with air to spare — options crowding the thing they surround reads as a
    // toolbar, not as a ring. Grown with the type: at three times the size the old
    // ring had the words running into each other and into the cube.
    const float HexRadius    = 460f;
    const float HexButtonW   = 560f;
    const float HexButtonH   = 100f;
    const float HexFont      = 66f;

    // Resting in the middle, and smaller when it has gone to sit under an option —
    // there it is a mark BESIDE a word rather than the centrepiece, and at full size
    // it would not fit under the lower corners of the ring without leaving the screen.
    const float MenuCubeSize      = 300f;
    const float MenuFocusCubeSize = 210f;

    // Which option has the player's attention, or -1. Pointer hover and keyboard /
    // gamepad selection both feed this, because they are the same question asked two
    // ways and the cube should answer it the same way for either.
    int _focusedOption = -1;

    void FocusOption(int corner, bool on)
    {
        if (on) _focusedOption = corner;
        // Only the option that CLAIMED the focus may drop it. Moving the pointer
        // straight from one word to the next fires the new one's enter and the old
        // one's exit in an order that is not promised — without this the cube can be
        // sent home for a frame in the middle of a smooth walk along the ring.
        else if (_focusedOption == corner) _focusedOption = -1;
    }

    // Where corner `corner` of the hexagon sits.
    //
    // Squashed vertically so the six sit on a wide ring rather than a circle — screens
    // are wider than they are tall, and a true circle wastes the width while crowding
    // the height. Starts at the top and steps clockwise: screen Y is up, so the sign
    // on the sine is what turns "counter-clockwise maths" into "clockwise reading".
    static Vector2 HexPos(int corner)
    {
        float ang = Mathf.PI * 0.5f - corner * (Mathf.PI / 3f);
        return new Vector2(Mathf.Cos(ang) * HexRadius * 1.24f,
                           Mathf.Sin(ang) * HexRadius * 0.86f);
    }

    // Where the cube parks when an option is under the pointer: directly beneath the
    // word, close enough that the two read as one thing.
    Vector2 HexCubeSlot(int corner)
    {
        Vector2 p = HexPos(corner);
        float gap = HexButtonH * 0.5f + MenuFocusCubeSize * 0.5f + 16f;

        // Measured off the canvas rather than assumed to be 1080 tall. With the
        // scaler on match 0.5 the canvas is only 1080 units high at 16:9 — a wide
        // monitor gives it well under 950, and a limit hard-coded for the common case
        // walks the cube off the bottom of the uncommon one.
        float halfH = _canvas != null ? ((RectTransform)_canvas.transform).rect.height * 0.5f : 540f;
        float limit = halfH - MenuFocusCubeSize * 0.5f - 16f;

        // Below by preference, so the word reads as a caption over the cube. Where
        // there is no room below — the bottom corner of the ring is already near the
        // edge — it goes above instead. That is the same relationship mirrored, which
        // is better than a cube half off the screen or one sitting on the word.
        float below = p.y - gap;
        return new Vector2(p.x, below >= -limit ? below : p.y + gap);
    }

    // One option, parked at corner `corner` of the hexagon.
    //
    // Placed absolutely rather than through a layout group: a layout group's whole
    // job is to decide positions, and here the positions are the point.
    Button HexButton(RectTransform panel, int corner, string label, Color color,
                     System.Action onClick)
    {
        // Built here rather than through BuildButton, because these are WORDS ON
        // PAPER and not chips. The wipe has already turned the screen to paper; a
        // filled rectangle behind each option would put six boxes on a surface whose
        // entire point is that it is bare, and at this size the type carries itself.
        var rt = NewRect("Option", panel);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(HexButtonW, HexButtonH);
        rt.anchoredPosition = HexPos(corner);

        // WHITE, then tinted to `color` by the button's normalColor.
        //
        // Not black-with-a-gold-highlight: Selectable tinting MULTIPLIES the graphic's
        // own colour, and black times anything is black — the hover would have been
        // silently dead. The word is white so the state colours are the ones that
        // actually show.
        var t = NewText("Label", rt, HexFont, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
        // The game's own stamped face. This is the one screen that is entirely
        // typography — six words on a bare sheet — so the words have to be in the
        // game's lettering rather than in whatever TMP happened to default to.
        GeoFont.ApplyStamp(t);
        t.text = label;
        // The TEXT is the button's graphic, so the clickable area is the word you can
        // see and the highlight lands on the word itself. With no plate to tint there
        // is nothing else for it to land on.
        t.raycastTarget = true;
        var lrt = t.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = t;
        var colors = btn.colors;
        colors.normalColor      = color;
        colors.highlightedColor = GeoPalette.Gold;
        colors.selectedColor    = GeoPalette.Gold;
        colors.pressedColor     = GeoPalette.Signal;
        btn.colors = colors;
        btn.onClick.AddListener(() => onClick());

        // Hover and selection, reported to FocusOption so the cube can follow.
        //
        // An EventTrigger rather than a component of our own: it is four one-line
        // handlers on a runtime-built object, and a whole MonoBehaviour to carry them
        // would be a file to open every time someone wonders what moves the cube.
        int here = corner;
        var trig = rt.gameObject.AddComponent<EventTrigger>();
        void On(EventTriggerType type, bool on)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => FocusOption(here, on));
            trig.triggers.Add(entry);
        }
        On(EventTriggerType.PointerEnter, true);
        On(EventTriggerType.PointerExit,  false);
        On(EventTriggerType.Select,       true);
        On(EventTriggerType.Deselect,     false);

        return btn;
    }

    void BuildSettingsButton(RectTransform parent, System.Action onClick)
    {
        var rt = NewRect("SettingsButton", parent);
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = ChipSize;
        le.preferredHeight = ChipSize + PipGap + PipRowHeight;

        if (settingsIcon != null)
        {
            _settingsIconImg = BuildChip(rt, settingsIcon, onClick, out _);
            return;
        }

        _settingsGlyph = NewText("Glyph", rt, 54f, GeoPalette.Ink, FontStyles.Bold, TextAlignmentOptions.Top);
        _settingsGlyph.text = "≡";   // ≡
        var grt = _settingsGlyph.rectTransform;
        grt.anchorMin = grt.anchorMax = grt.pivot = new Vector2(0.5f, 1f);
        grt.sizeDelta = new Vector2(ChipSize, ChipSize);
        _settingsGlyph.raycastTarget = true;
        var btn = _settingsGlyph.gameObject.AddComponent<Button>();
        btn.targetGraphic = _settingsGlyph;
        var colors = btn.colors; colors.highlightedColor = GeoPalette.Gold; colors.pressedColor = GeoPalette.Gold; btn.colors = colors;
        btn.onClick.AddListener(() => onClick());
    }

    void BuildSpeedButton(RectTransform parent, System.Action onClick)
    {
        var rt = NewRect("SpeedButton", parent);
        var rootLe = rt.gameObject.AddComponent<LayoutElement>();
        rootLe.preferredWidth  = ChipSize;
        rootLe.preferredHeight = ChipSize + PipGap + PipRowHeight;

        var vlg = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = PipGap;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = vlg.childForceExpandHeight = false;

        _fastForwardIconImg = BuildChip(rt, fastForwardIcon, onClick, out _);

        var pipRow = NewRect("Pips", rt);
        var pipRowLe = pipRow.gameObject.AddComponent<LayoutElement>();
        pipRowLe.preferredWidth = ChipSize; pipRowLe.preferredHeight = PipRowHeight;
        var pipHlg = pipRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        pipHlg.spacing = 4f; pipHlg.childAlignment = TextAnchor.MiddleCenter;
        pipHlg.childControlWidth = pipHlg.childControlHeight = true;
        pipHlg.childForceExpandWidth = pipHlg.childForceExpandHeight = false;

        for (int i = 0; i < 3; i++)
        {
            var pipRt = NewRect("Pip", pipRow);
            var pipLe = pipRt.gameObject.AddComponent<LayoutElement>();
            pipLe.preferredWidth = pipLe.preferredHeight = PipRowHeight;
            var pipImg = pipRt.gameObject.AddComponent<Image>();
            pipImg.preserveAspect = true;
            pipImg.raycastTarget = false;
            _speedPips[i] = pipImg;
        }
    }

    Image BuildIconButton(RectTransform parent, Sprite initial, System.Action onClick)
    {
        var rt = NewRect("PauseButton", parent);
        var rootLe = rt.gameObject.AddComponent<LayoutElement>();
        rootLe.preferredWidth  = ChipSize;
        rootLe.preferredHeight = ChipSize + PipGap + PipRowHeight;
        return BuildChip(rt, initial, onClick, out _);
    }

    Button BuildButton(RectTransform parent, string label, Color color, System.Action onClick)
    {
        var rt = NewRect("Button", parent);
        rt.gameObject.AddComponent<LayoutElement>().minHeight = buttonHeight;
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors; colors.highlightedColor = GeoPalette.Gold; colors.pressedColor = new Color(0.5f, 0.5f, 0.5f); btn.colors = colors;
        btn.onClick.AddListener(() => onClick());

        var t = NewText("Label", rt, 22f, GeoPalette.Paper, FontStyles.Bold, TextAlignmentOptions.Center);
        t.enableAutoSizing = true;
        t.fontSizeMax = 22f;
        t.fontSizeMin = 14f;
        t.text = label;
        var lrt = t.rectTransform; lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(18f, 0f); lrt.offsetMax = new Vector2(-18f, 0f);
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
