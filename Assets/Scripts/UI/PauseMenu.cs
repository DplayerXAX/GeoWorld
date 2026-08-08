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
            && !SettingsScreen.Open && !BlockTetris3D.Active)
        {
            if (_menuOpen) SetMenuOpen(false);   // Esc backs out of the menu first
            else           SetPaused(!_paused);  // otherwise toggles the planning pause
        }

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
        Time.timeScale = 1f;
        _paused = _menuOpen = false; Paused = false;
        LoadingScreen.Go(titleScene);
    }

    void GoToLevelSelect()
    {
        Time.timeScale = 1f;
        _paused = _menuOpen = false; Paused = false;
        LoadingScreen.Go(levelSelectScene);
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
        _overlayGo.AddComponent<Image>().color = overlayColor;
        _overlayGo.SetActive(false);

        // Paper panel (system menu).
        const float buttonSpacing = 6f;   // tighter gap between menu buttons
        var panel = NewRect("Panel", canvasGo.transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(panelWidth, 6f * (buttonHeight + buttonSpacing) + 90f);
        _panelGo = panel.gameObject;
        panel.gameObject.AddComponent<Image>().color = GeoPalette.Paper;

        var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(28, 28, 26, 26);
        vlg.spacing = buttonSpacing;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperCenter;

        var title = NewText("Title", panel, 34f, GeoPalette.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        title.text = "MENU";
        title.gameObject.AddComponent<LayoutElement>().minHeight = 46f;

        _firstMenuButton = BuildButton(panel, "Close", GeoPalette.Ink, () => SetMenuOpen(false));
        BuildButton(panel, "Settings", GeoPalette.Ink, () => SettingsScreen.Open = true);
        BuildButton(panel, "Restart", GeoPalette.Ink, () =>
        {
            Time.timeScale = 1f;
            _paused = _menuOpen = false; Paused = false;
            GameFlowManager.Instance?.RestartGame();
        });
        BuildButton(panel, "Title", GeoPalette.Ink, GoToTitle);
        BuildButton(panel, "Select Level", GeoPalette.Ink, GoToLevelSelect);
        BuildButton(panel, "Quit", GeoPalette.Signal, QuitGame);

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

        BuildSettingsButton(crt, () => SetMenuOpen(!_menuOpen));
        BuildSpeedButton(crt, CycleSpeed);
        _pauseIconImg = BuildIconButton(crt, pauseIcon, () => SetPaused(!_paused));
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
