using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// Runtime UGUI pause menu (rewritten from IMGUI so gamepad Navigate/Submit/Cancel work
// via the scene's InputSystemUIInputModule). Same house style as LevelClearScreen.cs /
// DialogueRunner.cs: Canvas + CanvasScaler built in code, NewRect/NewText helpers,
// UIRoundedRect.Get(radius) for rounded panels/buttons.
public class PauseMenu : MonoBehaviour
{
    [Header("Hotkey")]
    public KeyCode toggleKey = KeyCode.Escape;

    [Tooltip("Scene loaded by the 'Back to Title' button (must be in Build Settings).")]
    public string titleScene = "Title";

    [Header("Look")]
    public Color overlayColor = new Color(0f, 0f, 0f, 0.6f);
    public float panelWidth = 340f;
    public float buttonHeight = 58f;

    [Header("Top-right controls")]
    public bool showControls = true;

    [Header("Top-right icons (sprites)")]
    [Tooltip("Shown while the game is running (click = pause).")]
    public Sprite pauseIcon;
    [Tooltip("Shown while paused (click = resume).")]
    public Sprite resumeIcon;
    [Tooltip("Speed-cycle button icon.")]
    public Sprite fastForwardIcon;
    [Tooltip("Speed-level pip, lit.")]
    public Sprite speedPipFilled;
    [Tooltip("Speed-level pip, unlit.")]
    public Sprite speedPipEmpty;

    [Header("Top-right icon colors")]
    [Tooltip("Icon tint while the shop (black letterbox bar) is open.")]
    public Color iconColorOnShop = Color.white;
    [Tooltip("Icon tint the rest of the time.")]
    public Color iconColorDefault = Color.black;

    bool _paused;
    float _prevTimeScale = 1f;

    Canvas _canvas;
    GameObject _overlayGo, _panelGo, _controlsGo;
    Button _resumeButton;
    Image _pauseIconImg;
    Image _fastForwardIconImg;
    Image[] _speedPips = new Image[3];

    public bool IsPaused => _paused;
    public static bool Paused;

    void Awake() => BuildUI();

    void Update()
    {
        if (IntroDirector.Playing) { SetCanvasVisible(false); return; }   // hidden behind the intro overlay

        if ((Input.GetKeyDown(toggleKey) || GamepadInput.TogglePauseDown) && !SettingsScreen.Open)
            SetPaused(!_paused);

        _controlsGo.SetActive(showControls);
        if (showControls) UpdateTopRightControls();

        bool showPanel = _paused && !SettingsScreen.Open;
        _overlayGo.SetActive(showPanel);
        _panelGo.SetActive(showPanel);
        SetCanvasVisible(true);
    }

    void SetCanvasVisible(bool visible)
    {
        if (_canvas != null) _canvas.enabled = visible;
    }

    void OnDisable()
    {
        if (_paused)
        {
            Time.timeScale = _prevTimeScale;
            _paused = false;
        }
        Paused = false;
    }

    public void SetPaused(bool paused)
    {
        if (paused == _paused) return;
        _paused = paused;
        Paused = paused;

        if (_paused)
        {
            _prevTimeScale = Time.timeScale > 0.0001f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            EventSystem.current?.SetSelectedGameObject(_resumeButton.gameObject);
        }
        else
        {
            Time.timeScale = _prevTimeScale;
            EventSystem.current?.SetSelectedGameObject(null);
        }
    }

    void GoToTitle()
    {
        Time.timeScale = 1f;
        _paused = false; Paused = false;
        LoadingScreen.Go(titleScene);
    }

    void CycleSpeed()
    {
        float cur = _paused ? _prevTimeScale : Time.timeScale;
        float next = cur >= 2.95f ? 1f : cur >= 1.95f ? 3f : 2f;
        if (_paused) _prevTimeScale = next;
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

        float speed = _paused ? _prevTimeScale : Time.timeScale;
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

        // Dim overlay.
        _overlayGo = NewRect("Overlay", canvasGo.transform).gameObject;
        var overlayRt = (RectTransform)_overlayGo.transform;
        overlayRt.anchorMin = Vector2.zero; overlayRt.anchorMax = Vector2.one;
        overlayRt.offsetMin = overlayRt.offsetMax = Vector2.zero;
        var overlayImg = _overlayGo.AddComponent<Image>();
        overlayImg.color = overlayColor;
        _overlayGo.SetActive(false);

        // Paper panel.
        var panel = NewRect("Panel", canvasGo.transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(panelWidth, 5f * (buttonHeight + 14f) + 90f);
        _panelGo = panel.gameObject;

        var bg = panel.gameObject.AddComponent<Image>();
        bg.color = GeoPalette.Paper;
        bg.sprite = UIRoundedRect.Get(24);
        bg.type = Image.Type.Sliced;

        var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(28, 28, 26, 26);
        vlg.spacing = 12f;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperCenter;

        var title = NewText("Title", panel, 34f, GeoPalette.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        title.text = "PAUSED";
        title.gameObject.AddComponent<LayoutElement>().minHeight = 46f;

        _resumeButton = BuildButton(panel, "Resume", GeoPalette.Signal, () => SetPaused(false));
        BuildButton(panel, "Settings", GeoPalette.Blue, () => SettingsScreen.Open = true);
        BuildButton(panel, "Restart", GeoPalette.Blue, () =>
        {
            Time.timeScale = 1f;
            _paused = false;
            GameFlowManager.Instance?.RestartGame();
        });
        BuildButton(panel, "Back to Title", GeoPalette.Blue, GoToTitle);
        BuildButton(panel, "Quit", new Color(0.86f, 0.36f, 0.32f), QuitGame);

        _panelGo.SetActive(false);

        // Top-right speed / pause controls — round paper chips, same visual language
        // as ShopController's bottom-right shop-open button (54px circle, paper fill,
        // gold tint on hover).
        _controlsGo = NewRect("TopRightControls", canvasGo.transform).gameObject;
        var crt = (RectTransform)_controlsGo.transform;
        crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(1f, 1f);
        crt.sizeDelta = new Vector2(ChipSize * 2f + ChipGap + 20f, ChipSize + PipGap + PipRowHeight);
        crt.anchoredPosition = new Vector2(-16f, -16f);

        var hlg = _controlsGo.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = ChipGap; hlg.childAlignment = TextAnchor.UpperRight;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

        BuildSpeedButton(crt, CycleSpeed);
        _pauseIconImg = BuildIconButton(crt, pauseIcon, () => SetPaused(!_paused));
    }

    const float ChipSize     = 80f;
    const float ChipGap      = 12f;    // gap between the two icons (pause / speed)
    const float PipGap       = -4f;    // gap between the fast-forward icon and its pip row — pulled up, overlapping its bottom edge slightly
    const float PipRowHeight = 16f;

    // Bare icon (no background chip) that IS the button — clicking anywhere the icon's
    // rect covers (its full square, not just opaque pixels) triggers onClick.
    Image BuildChip(RectTransform parent, Sprite icon, System.Action onClick, out Button btn)
    {
        var iconRt = NewRect("Icon", parent);
        // Top-center anchor/pivot so the icon's TOP edge sits at its parent's top —
        // matters when parent isn't a LayoutGroup (BuildIconButton's plain rect); a
        // LayoutGroup parent (BuildSpeedButton) overrides these anyway.
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

    // Fast-forward chip with 3 speed-level pips in a slim strip underneath.
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

    // Pause/resume icon — top-aligned to the same height as the speed icon above its
    // pip row, so the two icons sit on one visual line.
    Image BuildIconButton(RectTransform parent, Sprite initial, System.Action onClick)
    {
        var rt = NewRect("PauseButton", parent);
        var rootLe = rt.gameObject.AddComponent<LayoutElement>();
        rootLe.preferredWidth  = ChipSize;
        rootLe.preferredHeight = ChipSize + PipGap + PipRowHeight;   // matches BuildSpeedButton's total height, keeps both icons top-aligned

        return BuildChip(rt, initial, onClick, out _);
    }

    Button BuildButton(RectTransform parent, string label, Color color, System.Action onClick)
    {
        var rt = NewRect("Button", parent);
        rt.gameObject.AddComponent<LayoutElement>().minHeight = buttonHeight;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = UIRoundedRect.Get(16); img.type = Image.Type.Sliced;
        img.color = color;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors; colors.highlightedColor = GeoPalette.Gold; colors.pressedColor = GeoPalette.Ink; btn.colors = colors;
        btn.onClick.AddListener(() => onClick());

        var t = NewText("Label", rt, 22f, GeoPalette.Paper, FontStyles.Bold, TextAlignmentOptions.Center);
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
