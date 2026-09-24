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
        if (_paperGo != null && _paperGo.activeSelf != showMenu) _paperGo.SetActive(showMenu);

        // The cube, opened. It arrives whole from the wipe, then comes apart — and the
        // six options ride out on its faces.
        //
        // Guarded on !SettingsScreen.Open so the two screens never fight over it in
        // the same frame: while settings is up, THAT page is the one asking, the cube
        // closes back into a solid and goes to sit in its left-hand column.
        if (showMenu)
        {
            SettingsCube.ShowAt(Vector2.zero, MenuCubeSize, null, explode: true);
            SettingsCube.SetPulled(_focusedOption);
            LayOutOptions();
            RefreshField();
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

        // Built BEFORE the panel so it draws under the words, and guarded, because it
        // is decoration and the six options are the controls.
        try { BuildPaperFurniture(canvasGo.transform); }
        catch (System.Exception e) { Debug.LogWarning($"[PauseMenu] Paper furniture skipped: {e.Message}"); }

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
        // Indexed by FACE now, in SettingsCube's viewer order — top, front, right,
        // back, left, bottom. CLOSE takes the lid because that is where the eye starts
        // and it is the way out; QUIT takes the underside, furthest from it.
        _firstMenuButton = HexButton(panel, 0, "CLOSE",        GeoPalette.Ink, CloseMenu);
                           HexButton(panel, 1, "SETTINGS",     GeoPalette.Ink, OpenSettings);
                           HexButton(panel, 2, "RESTART",      GeoPalette.Ink, () =>
                           {
                               LeaveMenuHard();
                               GameFlowManager.Instance?.RestartGame();
                           });
                           HexButton(panel, 3, "SELECT LEVEL", GeoPalette.Ink, GoToLevelSelect);
                           HexButton(panel, 4, "TITLE",        GeoPalette.Ink, GoToTitle);
                           HexButton(panel, 5, "QUIT",         GeoPalette.Ink, QuitGame);

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

    // ── The room the menu stands in ──────────────────────────────────────────
    //
    // Not a drawing any more. The cube opens into a ONE-POINT PERSPECTIVE field:
    // hairline rays converging on the middle, and hexagonal rings receding into it on
    // a log scale — each twice as far out as the last — so the recession has no final
    // ring and no loop to spot. The rings are HEXAGONS because that is the cube's own
    // silhouette: the space recedes in the shape of the thing standing in it.
    //
    // The flat grids, the slicing bars and the title block that used to be here are
    // gone. A flat ruled ground and a perspective field are two accounts of the same
    // space and only one of them can be true; the bars were the heaviest marks on a
    // sheet whose whole job is to stay under six words; and a table of figures is the
    // least spatial object there is to put on a screen that wants to feel like depth.

    GameObject _paperGo;
    Material   _fieldMat;

    void BuildPaperFurniture(Transform canvas)
    {
        _paperGo = NewRect("Paper", canvas).gameObject;
        var root = (RectTransform)_paperGo.transform;
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        _paperGo.SetActive(false);

        var field = NewRect("DeepField", root);
        field.anchorMin = Vector2.zero; field.anchorMax = Vector2.one;
        field.offsetMin = field.offsetMax = Vector2.zero;
        var fieldImg = field.gameObject.AddComponent<RawImage>();
        fieldImg.raycastTarget = false;

        var sh = Shader.Find("GeoWorld/DeepField");
        if (sh != null)
        {
            _fieldMat = new Material(sh) { name = "DeepField" };
            fieldImg.material = _fieldMat;
        }
        else
        {
            // No shader, no field. Bare paper is a perfectly good backdrop and it is
            // already there; drawing a white sheet over it would be worse than nothing.
            fieldImg.enabled = false;
            Debug.LogWarning("[PauseMenu] GeoWorld/DeepField missing — the menu falls back to bare paper.");
        }

        // Nothing out here at all. The corner brackets went first, then the
        // registration crosses, and now the edge stamp: what is left on the paper is
        // the field, the cube and six words, and there was nothing the label told
        // anyone that the screen was not already saying.
        //
        // The CanvasGroup that used to fade these in went with them — a group with
        // nothing in it is a lever nobody is holding.
    }

    // The field drifts outward for ever, on UNSCALED time.
    //
    // The menu freezes the game, and the shader's own _Time freezes with it — a space
    // that stops dead the instant you open the menu is the one thing an endless one
    // must not do.
    void RefreshField()
    {
        // Driven by the CUBE's own opening, not by a clock of its own. One gesture,
        // one curve: the room arrives at the rate the solid comes apart, which is why
        // it reads as the cube making room rather than as two effects that happen to
        // start together.
        float open = SettingsCube.Opening;
        float eased = open * open * (3f - 2f * open);

        if (_fieldMat == null) return;
        _fieldMat.SetFloat("_Aspect", Screen.width / Mathf.Max(1f, (float)Screen.height));
        _fieldMat.SetFloat("_Phase", -Time.unscaledTime * 0.08f);
        // Out to 1.3, which clears the far corner of a wide screen. Starting just
        // above zero rather than at it keeps the first frame from flashing a dot.
        _fieldMat.SetFloat("_Open", Mathf.Lerp(0.03f, 1.30f, eased));
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
    // The ring is now a FALLBACK. Ordinarily each word hangs off the face of the
    // cube it belongs to, and the cube decides where that is; these numbers only get
    // used when the cube's stage failed to build. This is the pause menu — it has to
    // stay usable when the decoration does not.
    const float HexRadius  = 460f;
    const float HexButtonW = 560f;
    const float HexButtonH = 120f;

    // The cube's slot, before it opens. SettingsCube widens both this and its own
    // camera by the same factor as the faces travel, so the cube stays the same size
    // on screen and only the space between its pieces grows.
    const float MenuCubeSize = 300f;

    // WHERE THE BACKDROP'S SIX SECTORS ARE.
    //
    // DeepField cuts the field at ang * 6, with ang = atan2(y,x)/2pi + 0.5 — so the
    // cuts land on the hexagon's vertices and each sector is centred on the edge
    // midpoint between two of them, at k*60 - 150 degrees. One option per sector, sat
    // on that centre line.
    //
    // Indexed by FACE, and each face's plate really does land in the sector its word
    // is given: +Y projects to 90 degrees, -Z to about -148, +X to -8, +Z to 32, -X to
    // 172, -Y to -90. Snapping the words to the sector centres is what turns those six
    // uneven bearings into a set.
    static readonly float[] FaceSectorDeg = { 90f, -150f, -30f, 30f, 150f, -90f };

    // Sized in MIRRORED PAIRS, because the sectors are. The old ramp ran 104 down to
    // 32 with every word a different size; on a scattered layout that read as six
    // unrelated things, and on a regular one it would read as a mistake. The hierarchy
    // is kept — the way out is biggest, the destructive one smallest — but the two
    // options facing each other across the field now match.
    static readonly float[] OptionFont = { 84f, 62f, 62f, 54f, 54f, 46f };

    // Mirrored too: each word leans the way the one opposite it leans back.
    static readonly float[] OptionTilt = { 0f, 5f, -5f, 5f, -5f, 0f };

    // Clearance between the ring the plates land on and the near edge of the words.
    const float StandoffPad = 40f;

    readonly RectTransform[] _optionRects  = new RectTransform[6];
    readonly CanvasGroup[]   _optionGroups = new CanvasGroup[6];

    // Half the width and height each word actually occupies, measured once.
    //
    // A radial standoff alone is not enough: a word is pushed out from its own MIDDLE,
    // and SELECT LEVEL is six times wider than it is tall. Offset by one distance in
    // every direction and the long words still lie across the plate they were supposed
    // to have cleared — which is exactly what they were doing.
    readonly Vector2[] _optionHalf = new Vector2[6];

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

    // The six words, hung off the six faces.
    //
    // Re-solved EVERY FRAME rather than laid out once, because the faces are moving:
    // they burst outward when the menu opens, lean with the pointer, and the one under
    // the pointer comes further forward. A word that stayed put while its face
    // travelled would stop being that face's label and go back to being a list item.
    void LayOutOptions()
    {
        bool haveCentre = SettingsCube.TryProjectCentre(out var centre);

        // ONE RING for all six, taken from whichever plate has travelled furthest.
        //
        // Measured rather than fixed, so the words spread outward with the burst and
        // stand still once it has landed — and taken as the MAXIMUM, so the ring clears
        // every plate however far through the opening it is. Six equal sectors deserve
        // six equally distant names; the old per-plate standoff put one word 112px out
        // and another 350px out, and that difference was most of what read as a jumbled
        // layout.
        float ring = 0f;
        if (haveCentre)
            for (int i = 0; i < _optionRects.Length; i++)
                if (SettingsCube.TryProjectFace(i, out var fp))
                    ring = Mathf.Max(ring, (fp - centre).magnitude);

        // ...to the plate's OUTER EDGE, not its middle. A plate is about 170px across;
        // standing the words a fixed 40 past its centre left them lying over the far
        // half of it.
        ring += SettingsCube.PlateHalfPx;

        for (int i = 0; i < _optionRects.Length; i++)
        {
            var rt = _optionRects[i];
            if (rt == null) continue;

            float open = SettingsCube.FaceOpen(i);

            if (haveCentre)
            {
                float a = FaceSectorDeg[i] * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));

                // The word's own half-extent, applied along the way it is being pushed.
                // A word is placed by its MIDDLE, so without this the wide ones lie
                // back across the ring while the short ones float clear of it. Same
                // term as before; its job now is keeping the NEAR EDGES of six
                // differently shaped words on one line.
                Vector2 half = _optionHalf[i];
                rt.anchoredPosition = centre
                                    + dir * (ring + StandoffPad)
                                    + new Vector2(dir.x * half.x, dir.y * half.y);
            }
            else
            {
                rt.anchoredPosition = HexPos(i);
                open = 1f;
            }

            if (_optionGroups[i] != null) _optionGroups[i].alpha = open;
        }
    }

    // One option, parked at corner `corner` of the hexagon.
    //
    // Placed absolutely rather than through a layout group: a layout group's whole
    // job is to decide positions, and here the positions are the point.
    Button HexButton(RectTransform panel, int face, string label, Color color,
                     System.Action onClick)
    {
        // Built here rather than through BuildButton, because these are WORDS ON
        // PAPER and not chips. The wipe has already turned the screen to paper; a
        // filled rectangle behind each option would put six boxes on a surface whose
        // entire point is that it is bare, and at this size the type carries itself.
        var rt = NewRect($"Option{face}", panel);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(HexButtonW, HexButtonH);
        rt.anchoredPosition = HexPos(face);
        // Fixed at build time, not driven per frame: the tilt belongs to the word, and
        // re-writing the whole transform every frame just to keep it would fight
        // LayOutOptions for the same field.
        rt.localRotation = Quaternion.Euler(0f, 0f, OptionTilt[face]);

        // Faded in by its own face's arrival, so the six words land in the order the
        // faces do instead of all appearing at once.
        var group = rt.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        _optionRects[face]  = rt;
        _optionGroups[face] = group;

        // WHITE, then tinted to `color` by the button's normalColor.
        //
        // Not black-with-a-gold-highlight: Selectable tinting MULTIPLIES the graphic's
        // own colour, and black times anything is black — the hover would have been
        // silently dead. The word is white so the state colours are the ones that
        // actually show.
        var t = NewText("Label", rt, OptionFont[face], Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
        // The game's own stamped face. This is the one screen that is entirely
        // typography — six words on a bare sheet — so the words have to be in the
        // game's lettering rather than in whatever TMP happened to default to.
        GeoFont.ApplyStamp(t);
        t.text = label;
        // Measured now, with the real font and the real size. GetPreferredValues does
        // not need a layout pass to have run, which matters because this is the frame
        // the object is built on.
        _optionHalf[face] = t.GetPreferredValues() * 0.5f;
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
        int here = face;
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
