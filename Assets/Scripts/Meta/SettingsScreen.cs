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

    int _section;
    Canvas _canvas;
    GameObject[] _tabButtons = new GameObject[3];
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
        BuildUI();
        OnOpenChanged();
    }

    void Update()
    {
        if (_open && (Input.GetKeyDown(KeyCode.Escape) || GamepadInput.CancelDown)) Open = false;
    }

    void OnOpenChanged()
    {
        if (_canvas != null) _canvas.enabled = _open;
        if (_open)
        {
            RefreshAll();
            FocusSection();
        }
        else
        {
            EventSystem.current?.SetSelectedGameObject(null);
        }
    }

    void SelectSection(int i)
    {
        _section = i;
        for (int s = 0; s < _sectionRoots.Length; s++) _sectionRoots[s].SetActive(s == i);
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
    Toggle _fullscreenToggle, _vsyncToggle, _smoothEditToggle;
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

        _qualityIndex = Mathf.Clamp(GameSettings.QualityLevel, 0, QualitySettings.names.Length - 1);
        _qualityLabel.text = QualitySettings.names.Length > 0 ? QualitySettings.names[_qualityIndex] : "-";

        _frameCapIndex = Mathf.Max(0, System.Array.IndexOf(GameSettings.FrameCaps, GameSettings.FrameCap));
        _frameCapLabel.text = FrameLabels[Mathf.Clamp(_frameCapIndex, 0, FrameLabels.Length - 1)];
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
        panel.sizeDelta = new Vector2(1080f, 700f);
        var bg = panel.gameObject.AddComponent<Image>();
        bg.color = GeoPalette.Paper;
        bg.sprite = UIRoundedRect.Get(24);
        bg.type = Image.Type.Sliced;

        var titleT = NewText("Title", panel, 42f, GeoPalette.Ink, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        titleT.text = "SETTINGS";
        var trt = titleT.rectTransform;
        trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(1f, 1f); trt.pivot = new Vector2(0f, 1f);
        trt.anchoredPosition = new Vector2(34f, -18f);
        trt.sizeDelta = new Vector2(-68f, 50f);

        // Left tab column.
        var tabCol = NewRect("Tabs", panel);
        tabCol.anchorMin = new Vector2(0f, 0f); tabCol.anchorMax = new Vector2(0f, 1f); tabCol.pivot = new Vector2(0f, 1f);
        tabCol.anchoredPosition = new Vector2(30f, -90f);
        tabCol.sizeDelta = new Vector2(200f, 300f);
        var tabVlg = tabCol.gameObject.AddComponent<VerticalLayoutGroup>();
        tabVlg.spacing = 8f;
        tabVlg.childControlWidth = tabVlg.childControlHeight = true;
        tabVlg.childForceExpandWidth = true; tabVlg.childForceExpandHeight = false;

        for (int i = 0; i < Sections.Length; i++)
        {
            int idx = i;
            var tab = BuildTabButton(tabCol, Sections[i], () => SelectSection(idx));
            _tabButtons[i] = tab.gameObject;
        }

        // Right content area.
        var content = NewRect("Content", panel);
        content.anchorMin = new Vector2(0f, 0f); content.anchorMax = new Vector2(1f, 1f);
        content.offsetMin = new Vector2(260f, 96f); content.offsetMax = new Vector2(-30f, -96f);

        _sectionRoots[0] = BuildAudioSection(content);
        _sectionRoots[1] = BuildDisplaySection(content);
        _sectionRoots[2] = BuildControlsSection(content);

        // Bottom buttons.
        var resetBtn = BuildTextButton(panel, "RESET", false, () =>
        {
            GameSettings.ResetDefaults();   // saves + applies internally
            RefreshAll();
        });
        var brt = resetBtn.GetComponent<RectTransform>();
        brt.anchorMin = brt.anchorMax = new Vector2(0f, 0f); brt.pivot = new Vector2(0f, 0f);
        brt.anchoredPosition = new Vector2(30f, 24f);
        brt.sizeDelta = new Vector2(150f, 46f);

        var backBtn = BuildTextButton(panel, "BACK", true, () => Open = false);
        var bkrt = backBtn.GetComponent<RectTransform>();
        bkrt.anchorMin = bkrt.anchorMax = new Vector2(1f, 0f); bkrt.pivot = new Vector2(1f, 0f);
        bkrt.anchoredPosition = new Vector2(-30f, 24f);
        bkrt.sizeDelta = new Vector2(150f, 46f);

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

        _fullscreenToggle = BuildToggleRow(root, "Fullscreen", v => { GameSettings.Fullscreen = v; GameSettings.ApplyDisplay(); GameSettings.Save(); });
        _vsyncToggle      = BuildToggleRow(root, "V-Sync",     v => { GameSettings.VSync      = v; GameSettings.ApplyDisplay(); GameSettings.Save(); });

        BuildChoiceRow(root, "Quality", out _qualityLabel,
            () => { _qualityIndex = (_qualityIndex - 1 + QualitySettings.names.Length) % QualitySettings.names.Length; ApplyQuality(); },
            () => { _qualityIndex = (_qualityIndex + 1) % QualitySettings.names.Length; ApplyQuality(); });

        BuildChoiceRow(root, "Frame cap", out _frameCapLabel,
            () => { _frameCapIndex = (_frameCapIndex - 1 + FrameLabels.Length) % FrameLabels.Length; ApplyFrameCap(); },
            () => { _frameCapIndex = (_frameCapIndex + 1) % FrameLabels.Length; ApplyFrameCap(); });

        _firstControlDisplay = _fullscreenToggle.gameObject;
        return root.gameObject;
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
        _smoothEditToggle = BuildToggleRow(root, "Smooth block editing", v => { GameSettings.SmoothBlockEditing = v; GameSettings.Save(); });

        _firstControlControls = _panSlider.gameObject;
        return root.gameObject;
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
        Debug.LogWarning($"Create listener for {label}!");
        return slider;
    }

    Toggle BuildToggleRow(RectTransform parent, string label, System.Action<bool> onChanged)
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
