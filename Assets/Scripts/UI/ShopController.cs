using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
public enum RiftShapePreset { Rectangle, Triangle, Lens, Oval, Slash, Crack, Diamond, Custom }

public class ShopController : MonoBehaviour
{
    public static ShopController Instance;

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Camera")]
    public Camera  shopCam;
    public Vector3 cameraOffsetSmall = new Vector3(0f, 3f, -10f);
    public Vector3 cameraOffsetLarge = new Vector3(0f, 3f, -14f);

    [Header("Rift Shape & Position")]
    [Tooltip("Rift centre in normalised screen space (0=left/top, 1=right/bottom). Clamped at runtime so the rift never overflows.")]
    public Vector2 riftScreenPos  = new Vector2(0.5f, 0.85f);
    [Tooltip("Extra Y offset added to the rift center ONLY when collapsed (small / hint state). Positive = lower on screen, can go past 0.5 to hug the bottom edge. The expanded position stays at riftScreenPos.y.")]
    public float riftCollapsedYOffset = 0.05f;
    [Tooltip("Strip width as a fraction of screen width when fully open.")]
    [Range(0.1f, 1.0f)] public float riftWidth  = 0.45f;
    [Tooltip("Strip height as a fraction of screen height when fully open.")]
    [Range(0.05f, 1.0f)] public float riftHeight = 0.12f;
    [Tooltip("Minimum gap between the rift and the screen edges (pixels).")]
    public float screenEdgeMargin = 12f;
    [Tooltip("Scale while collapsed. 0 = completely hidden when closed (recommended for rectangle preset).")]
    public float   riftHintScale  = 0f;
    [Tooltip("Open/close speed.")]
    public float   expandSpeed    = 7f;

    [Header("Toggle Key")]
    public KeyCode shopToggleKey = KeyCode.F;

    [Header("Letterbox (cinematic bars)")]
    [Tooltip("ON: shop is shown as a bottom black bar with a matching top bar (movie letterbox). Collapsed = bars fully hidden. F expands.")]
    public bool  letterbox = true;
    [Tooltip("Height of EACH bar as a fraction of screen height when fully open.")]
    [Range(0.05f, 0.35f)] public float barHeight = 0.16f;
    public Color barColor = new Color(0f, 0f, 0f, 1f);

    [Header("Shop World Area")]
    public Vector3 shopCenter   = new Vector3(-25f, 4f, 5f);
    [Tooltip("Dedicated layer the shop blocks/lights live on so ONLY shopCam renders them and the main camera culls it (stops the shop showing in 3D when you orbit). Must exist in Project Settings ▸ Tags and Layers.")]
    public string shopLayerName = "ShopItem";
    int _shopLayer = -1;
    public float   blockSpacing = 1.6f;
    [Tooltip("Lighting anchor for the block half (LEFT side of the strip).")]
    public Vector3 blockRowOffset  = new Vector3(-4f, 2.5f, 0f);
    [Tooltip("Lighting anchor for the turret half (RIGHT side of the strip).")]
    public Vector3 turretRowOffset = new Vector3( 4f, 2.5f, 0f);

    [Header("Float Animation set to 0 for a static clickable shop")]
    [Tooltip("Per-axis drift range in world units. 0 = items sit completely still.")]
    public Vector3 driftAmplitude = Vector3.zero;
    public Vector3 driftSpeed     = new Vector3(0.50f, 0.70f, 0.60f);
    [Tooltip("Peak wobble angle in degrees on each axis. 0 = no rotation animation.")]
    public float   tumbleAmplitude = 0f;
    public float   tumbleSpeed     = 0.45f;

    [Header("Rift Atmosphere")]
    [Tooltip("Inner colour of the rift. Deep cosmic by default.")]
    public Color shopBackground     = new Color(0.04f, 0.05f, 0.14f, 1f);
    [Tooltip("Light over the block row. Kept near-neutral so synergy colours read the same in shop preview as on the placed board.")]
    public Color blockLightColor    = new Color(1.00f, 0.96f, 0.88f, 1f);
    [Tooltip("Light over the turret row. Slightly cool to separate from blocks, but close to neutral so turret previews stay readable.")]
    public Color turretLightColor   = new Color(0.85f, 0.94f, 1.00f, 1f);
    public float shopLightIntensity = 1.8f;
    public float shopLightRange     = 18f;
    [Tooltip("Inner vignette strength. 0 = no darkening at edges (recommended for rectangle panel).")]
    [Range(0f, 1f)] public float innerVignetteStrength = 0.15f;

    [Header("Rift Shape")]
    public RiftShapePreset shapePreset = RiftShapePreset.Crack;
    [Tooltip("Extra polygon rotation when collapsed. 0 = no spin (recommended for clean rectangle panel).")]
    [Range(0f, 360f)] public float openSpinDegrees = 0f;
    [Tooltip("Sprite physics outline only used when shapePreset = Custom.")]
    public Sprite riftSprite;
    [Tooltip("Rotate the rift opening on-screen. 0 = naturally horizontal for the wide rectangle preset.")]
    [Range(-180f, 180f)] public float riftRotationDeg = 0f;
    [Tooltip("RenderTexture width. Aspect should match the on-screen strip (riftWidth × Screen.width : riftHeight × Screen.height) to avoid item stretch.")]
    public int rtWidth  = 1280;
    [Tooltip("RenderTexture height.")]
    public int rtHeight = 288;

    [Header("Rift Edge FX ")]
    public Color riftEdgeColor  = new Color(0.92f, 0.96f, 1.00f, 0.85f);
    public Color riftGlowColor  = new Color(0.40f, 0.55f, 0.80f, 0.06f);
    public int   riftGlowLayers = 3;
    [Range(0f, 5f)] public float edgePulseSpeed = 1.4f;

    [Header("Cosmic Energy ")]
    public Color energyRayColor   = new Color(0.75f, 0.85f, 1.00f, 0.15f);
    [Range(0, 32)] public int rayCount = 0;          // 0 = no rays for the clean rect panel
    public Vector2 rayLengthRange = new Vector2(0.40f, 0.90f);
    [Range(0f, 2f)] public float rayPulseSpeed = 0.60f;
    [Range(0f, 1f)] public float raySpinSpeed  = 0.05f;

    public Color sparkleColor   = new Color(0.75f, 0.85f, 1.00f, 0.5f);
    [Range(0, 80)] public int sparkleCount = 0;      // 0 = no sparkles for clean panel
    [Range(0.5f, 2.5f)] public float sparkleRadius = 1.15f;
    [Range(0.2f, 4f)] public float sparkleTwinkleSpeed = 1.1f;

    [Header("Item Hover")]
    [Range(1f, 1.5f)] public float hoverScale     = 1.08f;
    [Range(1f, 20f)]  public float hoverLerpSpeed = 12f;

    [Header("Tooltip")]
    public Color tooltipBg = new Color(0.949f, 0.937f, 0.902f, 0.94f);   // paper
    [Tooltip("Overall hover-tooltip size multiplier.")]
    public float tooltipScale = 2f;
    [Tooltip("Refresh button icon. If set, replaces the 'Refresh' text (cost still shown).")]
    public Sprite refreshIcon;
    [Tooltip("Shop-open icon. When the shop is collapsed, this button (same spot as Refresh) opens the shop.")]
    public Sprite shopButtonIcon;
    [Tooltip("Refresh button diameter (px).")]
    public float refreshButtonSize = 54f;
    [Tooltip("Refresh button offset from the rift's top-right corner (x = left, y = up).")]
    public Vector2 refreshButtonOffset = new Vector2(0f, 6f);

    [Header("Block style")]
    [Tooltip("Render shop blocks flat / unlit (2D look — no scene light or shadow).")]
    public bool flatBlocks = true;
    [Tooltip("Optional unlit material (needs a _BaseColor property). Null = auto URP Unlit.")]
    public Material flatMaterial;

    [Header("Hover correction")]
    public bool flipHoverX;
    public bool flipHoverY;

    // Built-in shape presets. Y axis is the long axis; rotation handled separately
    // by riftRotationDeg. All shapes are normalised so the largest |coord| = 1.

    // Normalised square on-screen aspect comes from riftWidth × riftHeight.
    // Rounded rectangle (normalized). Corner radii differ on x/y so they read roughly
    // round once the wide/short rift footprint stretches them. seg = arc smoothness.
    static readonly Vector2[] RiftShape_Rectangle = MakeRoundedRect(0.06f, 0.5f, 6);

    static Vector2[] MakeRoundedRect(float rx, float ry, int seg)
    {
        var pts = new Vector2[(seg + 1) * 4];
        int idx = 0;
        idx = RoundedArc(pts, idx, -1f + rx,  1f - ry, rx, ry,  180f,   90f, seg);   // top-left
        idx = RoundedArc(pts, idx,  1f - rx,  1f - ry, rx, ry,   90f,    0f, seg);   // top-right
        idx = RoundedArc(pts, idx,  1f - rx, -1f + ry, rx, ry,    0f,  -90f, seg);   // bottom-right
        idx = RoundedArc(pts, idx, -1f + rx, -1f + ry, rx, ry,  -90f, -180f, seg);   // bottom-left
        return pts;
    }

    static int RoundedArc(Vector2[] a, int idx, float cx, float cy, float rx, float ry,
                          float a0, float a1, int seg)
    {
        for (int i = 0; i <= seg; i++)
        {
            float ang = Mathf.Deg2Rad * Mathf.Lerp(a0, a1, (float)i / seg);
            a[idx++] = new Vector2(cx + Mathf.Cos(ang) * rx, cy + Mathf.Sin(ang) * ry);
        }
        return idx;
    }

    // Equilateral triangle, pointing up. 3-fold symmetric.
    static readonly Vector2[] RiftShape_Triangle =
    {
        new( 0.000f,  1.000f),
        new( 0.866f, -0.500f),
        new(-0.866f, -0.500f),
    };

    static readonly Vector2[] RiftShape_Lens =
    {
        new( 0.00f,  1.00f), new( 0.15f,  0.70f), new( 0.30f,  0.35f),
        new( 0.38f,  0.00f), new( 0.30f, -0.35f), new( 0.15f, -0.70f),
        new( 0.00f, -1.00f), new(-0.15f, -0.70f), new(-0.30f, -0.35f),
        new(-0.38f,  0.00f), new(-0.30f,  0.35f), new(-0.15f,  0.70f),
    };

    // Rounded oval no sharp tips, friendly silhouette.
    static readonly Vector2[] RiftShape_Oval =
    {
        new( 0.20f,  1.00f), new( 0.45f,  0.85f), new( 0.62f,  0.60f),
        new( 0.70f,  0.30f), new( 0.72f,  0.00f), new( 0.70f, -0.30f),
        new( 0.62f, -0.60f), new( 0.45f, -0.85f), new( 0.20f, -1.00f),
        new(-0.20f, -1.00f), new(-0.45f, -0.85f), new(-0.62f, -0.60f),
        new(-0.70f, -0.30f), new(-0.72f,  0.00f), new(-0.70f,  0.30f),
        new(-0.62f,  0.60f), new(-0.45f,  0.85f), new(-0.20f,  1.00f),
    };

    static readonly Vector2[] RiftShape_Slash =
    {
        new( 0.00f,  1.00f), new( 0.10f,  0.65f), new( 0.18f,  0.30f),
        new( 0.22f,  0.00f), new( 0.18f, -0.30f), new( 0.10f, -0.65f),
        new( 0.00f, -1.00f), new(-0.10f, -0.65f), new(-0.18f, -0.30f),
        new(-0.22f,  0.00f), new(-0.18f,  0.30f), new(-0.10f,  0.65f),
    };

    // No jagged noise. Reads as a "crack opening" laid horizontally.
    static readonly Vector2[] RiftShape_Crack =
    {
        new(-1.00f,  0.00f),  // left tip
        new(-0.70f,  1.00f),  // top-left transition
        new( 0.70f,  1.00f),  // top-right transition
        new( 1.00f,  0.00f),  // right tip
        new( 0.70f, -1.00f),  // bottom-right transition
        new(-0.70f, -1.00f),  // bottom-left transition
    };

    static readonly Vector2[] RiftShape_Diamond =
    {
        new( 0.00f,  1.00f), new( 0.55f,  0.30f), new( 0.80f,  0.00f),
        new( 0.55f, -0.30f), new( 0.00f, -1.00f), new(-0.55f, -0.30f),
        new(-0.80f,  0.00f), new(-0.55f,  0.30f),
    };

    // ── State ─────────────────────────────────────────────────────────────────

    class ShopItem
    {
        public GameObject      root;
        public SelectableBlock sb;
        public Vector3         basePos;       // anchor drift oscillates around this
        public Vector3         driftPhase;    // independent X/Y/Z phase offsets
        public Vector3         tumblePhase;
    }

    readonly List<ShopItem> _items = new();
    ShopItem _hovered;

    bool    _expanded;
    float   _riftScale;         // current animated scale
    float   _riftTarget;        // target scale
    float   _expandT;           // 0=fully collapsed, 1=fully expanded (lerp'd alongside scale)
    Vector3 _currentOffset;     // animated camera offset

    // Computed each Update used by hover/click and GL draw
    Vector2   _riftScreenCenter;
    float     _riftScreenSize;   // legacy: max(_riftSizeX, _riftSizeY)
    float     _riftSizeX;        // half-extent in screen X
    float     _riftSizeY;        // half-extent in screen Y (multiplied by _riftScale at use site)
    Vector2[] _screenVerts;

    // ── Shop screen anchors (GUI coords) for HUD elements that dock to the rift ──
    public bool    ShopVisible   => _riftScale > 0.1f;
    public bool    IsExpanded    => _expanded;
    // Current top letterbox-bar height in pixels (0 when not in letterbox / collapsed).
    public float   TopBarHeight  => letterbox ? Screen.height * barHeight * _riftScale : 0f;
    public Vector2 ShopTopCenter => new Vector2(_riftScreenCenter.x,
                                                _riftScreenCenter.y - _riftSizeY * _riftScale);
    public Vector2 ShopTopRight  => new Vector2(_riftScreenCenter.x + _riftSizeX,
                                                _riftScreenCenter.y - _riftSizeY * _riftScale);
    public Vector2 ShopTopLeft    => new Vector2(_riftScreenCenter.x - _riftSizeX,
                                                 _riftScreenCenter.y - _riftSizeY * _riftScale);
    public Vector2 ShopBottomRight => new Vector2(_riftScreenCenter.x + _riftSizeX,
                                                  _riftScreenCenter.y + _riftSizeY * _riftScale);

    // Counts down after a failed purchase attempt drives red rift edge flash.
    float _cantAffordFlash;

    // GL rendering
    RenderTexture _shopRT;
    Material      _riftMat;    
    Material      _colorMat;   

    Light _blockLight;
    Light _turretLight;

    // Active rift polygon in normalised coords (Y-up, centred at origin).
    // Built from the active preset (or riftSprite if shapePreset = Custom).
    // riftRotationDeg is applied on-the-fly in UpdateScreenVerts.
    Vector2[] _runtimeRiftShape;

    GUIStyle _ttTitle, _ttPrice, _ttSub, _hintStyle;
    bool     _stylesBuilt;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        Instance       = this;
        _currentOffset = cameraOffsetSmall;
        _shopLayer     = LayerMask.NameToLayer(shopLayerName);

        if (shopCam != null)
        {
            shopCam.enabled         = true;
            shopCam.clearFlags      = CameraClearFlags.SolidColor;
            shopCam.backgroundColor = shopBackground;
        }

        _blockLight  = CreateRowLight("BlockShopLight",  shopCenter + blockRowOffset  + Vector3.up * 2f, blockLightColor);
        _turretLight = CreateRowLight("TurretShopLight", shopCenter + turretRowOffset + Vector3.up * 2f, turretLightColor);

        ApplyCameraTransform();
    }

    Light CreateRowLight(string name, Vector3 pos, Color col)
    {
        var go = new GameObject(name);
        go.transform.position = pos;
        var l = go.AddComponent<Light>();
        l.type      = LightType.Point;
        l.color     = col;
        l.intensity = shopLightIntensity;
        l.range     = shopLightRange;
        if (_shopLayer >= 0) { l.cullingMask = 1 << _shopLayer; go.layer = _shopLayer; }
        return l;
    }

    // Ensure shopCam renders the shop layer and every other camera culls it — so
    // the shop blocks never appear in the main 3D view when the player orbits.
    void IsolateShopLayer()
    {
        if (_shopLayer < 0)
        {
            Debug.LogWarning($"[Shop] layer '{shopLayerName}' not found — shop blocks will show in the main view. Add it in Project Settings ▸ Tags and Layers.");
            return;
        }
        int mask = 1 << _shopLayer;
        if (shopCam != null) shopCam.cullingMask |= mask;          // shopCam keeps rendering the shop
        foreach (var cam in Camera.allCameras)
            if (cam != shopCam) cam.cullingMask &= ~mask;          // everyone else culls it
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform) SetLayerRecursive(t.gameObject, layer);
    }

    void Start()
    {
        BuildShapeFromSprite();
        RebuildRT();
        IsolateShopLayer();
    }

    void RebuildRT()
    {
        if (shopCam != null) shopCam.targetTexture = null;
        if (_shopRT != null) { _shopRT.Release(); Destroy(_shopRT); }

        // sRGB read/write so the snapshot matches the main camera's colours. A default
        // (linear) RT in a Linear-colour-space project makes the shop read darker.
        _shopRT              = new RenderTexture(Mathf.Max(32, rtWidth), Mathf.Max(32, rtHeight),
                                                 16, RenderTextureFormat.ARGB32,
                                                 RenderTextureReadWrite.sRGB);
        _shopRT.antiAliasing = 2;
        _shopRT.filterMode   = FilterMode.Bilinear;

        if (shopCam != null)
        {
            shopCam.targetTexture = _shopRT;
            shopCam.rect          = new Rect(0, 0, 1, 1);
            // aspect auto-derived from RT dimensions do not set manually
        }
        if (_riftMat != null) _riftMat.mainTexture = _shopRT;
    }

    void OnValidate()
    {
        if (shopCam != null)
        {
            shopCam.clearFlags      = CameraClearFlags.SolidColor;
            shopCam.backgroundColor = shopBackground;
            ApplyCameraTransform();
        }
        if (_blockLight != null)
        {
            _blockLight.color     = blockLightColor;
            _blockLight.intensity = shopLightIntensity;
            _blockLight.range     = shopLightRange;
            _blockLight.transform.position = shopCenter + blockRowOffset + Vector3.up * 2f;
        }
        if (_turretLight != null)
        {
            _turretLight.color     = turretLightColor;
            _turretLight.intensity = shopLightIntensity;
            _turretLight.range     = shopLightRange;
            _turretLight.transform.position = shopCenter + turretRowOffset + Vector3.up * 2f;
        }
        // Rebuild shape when sprite or RT dims change in the Inspector.
        if (Application.isPlaying)
        {
            BuildShapeFromSprite();
            RebuildRT();
        }
    }

    void OnDestroy()
    {
        if (shopCam != null) shopCam.targetTexture = null;
        if (_shopRT   != null) { _shopRT.Release(); Destroy(_shopRT); }
        if (_riftMat  != null) Destroy(_riftMat);
        if (_colorMat != null) Destroy(_colorMat);
        if (_flatMat  != null) Destroy(_flatMat);
    }

    void Update()
    {
        HandleToggleKey();
        AnimateRift();
        ApplyCameraTransform();
        UpdateScreenVerts();   // must run before UpdateHover / IsMouseInShopView
        AnimateItems();
        UpdateHover();
        UpdateLetterboxBars();
        if (_cantAffordFlash > 0f) _cantAffordFlash -= Time.deltaTime;
    }

    // ── Toggle & visibility ───────────────────────────────────────────────────

    void HandleToggleKey()
    {
        if (GameFlowManager.SettlementUp) { _expanded = false; return; }   // locked during clear settlement
        if (!Input.GetKeyDown(shopToggleKey) && !GamepadInput.ToggleShopDown) return;
        // Toggle off what the player actually SEES, not a possibly-stale _expanded
        // (grab/Collapse/RestoreItem/combat paths can desync it → a press did nothing).
        bool visiblyOpen = _riftScale > 0.5f;
        _expanded = !visiblyOpen;   // openable during combat too (buy / place new pieces)
    }

    public void Collapse()      => _expanded = false;
    public void OnCombatStart() => _expanded = false;

    // ── Letterbox bars + content (UGUI, behind the HUD so currency draws over them) ──
    Canvas        _lbCanvas;
    RectTransform _lbTop, _lbBottom, _lbContent;
    RawImage      _lbContentImg;

    void UpdateLetterboxBars()
    {
        if (!letterbox || GameFlowManager.SettlementUp)
        {
            if (_lbCanvas != null) _lbCanvas.enabled = false;
            return;
        }
        if (_lbCanvas == null) BuildLetterboxBars();

        // Transparent shop-camera clear so only the blocks show on the black bar.
        if (shopCam != null) shopCam.backgroundColor = new Color(0f, 0f, 0f, 0f);

        float H    = Screen.height * barHeight * _riftScale;
        bool  show = H > 0.5f;
        _lbCanvas.enabled = show;
        if (!show) return;

        _lbTop.GetComponent<Image>().color    = barColor;
        _lbBottom.GetComponent<Image>().color = barColor;
        _lbTop.sizeDelta    = new Vector2(0f, H);
        _lbBottom.sizeDelta = new Vector2(0f, H);

        _lbContent.sizeDelta = new Vector2(0f, H);     // bottom bar, full width
        if (_lbContentImg.texture != _shopRT) _lbContentImg.texture = _shopRT;
    }

    void BuildLetterboxBars()
    {
        var go = new GameObject("ShopLetterbox", typeof(Canvas), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        _lbCanvas = go.GetComponent<Canvas>();
        _lbCanvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        _lbCanvas.sortingOrder = 55;   // below the HUD (currency 90, objectives 92, …)

        _lbTop    = MakeBar("TopBar",    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        _lbBottom = MakeBar("BottomBar", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));

        // Shop content (RT) — above the bars, still under the HUD canvases.
        _lbContent = new GameObject("Content", typeof(RectTransform), typeof(RawImage)).GetComponent<RectTransform>();
        _lbContent.SetParent(_lbCanvas.transform, false);
        _lbContent.anchorMin = new Vector2(0f, 0f); _lbContent.anchorMax = new Vector2(1f, 0f);
        _lbContent.pivot = new Vector2(0.5f, 0f); _lbContent.anchoredPosition = Vector2.zero;
        _lbContentImg = _lbContent.GetComponent<RawImage>();
        _lbContentImg.raycastTarget = false;
        _lbContentImg.texture = _shopRT;
    }

    RectTransform MakeBar(string name, Vector2 aMin, Vector2 aMax, Vector2 pivot)
    {
        var rt = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        rt.SetParent(_lbCanvas.transform, false);
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, 0f);
        var img = rt.GetComponent<Image>();
        img.color = barColor; img.raycastTarget = false;
        return rt;
    }

    // ── Rift animation ────────────────────────────────────────────────────────

    void AnimateRift()
    {
        // Shop can open during combat too (buy / place new pieces), so no combat gate.
        _riftTarget = letterbox ? (_expanded ? 1f : 0f)        // bars: fully out / fully hidden
                                : (_expanded ? 0.6f : riftHintScale);

        float t    = 1f - Mathf.Exp(-expandSpeed * Time.deltaTime);
        _riftScale     = Mathf.Lerp(_riftScale, _riftTarget, t);
        _currentOffset = Vector3.Lerp(_currentOffset,
                             _expanded ? cameraOffsetLarge : cameraOffsetSmall, t);

        // Track expanded-ness for screen-position offset interpolation.
        float expandTarget = _expanded ? 1f : 0f;
        _expandT = Mathf.Lerp(_expandT, expandTarget, t);
    }

    void ApplyCameraTransform()
    {
        if (shopCam == null) return;
        shopCam.transform.position = shopCenter + _currentOffset;
        shopCam.transform.LookAt(shopCenter);
        // Roll the camera by the rift's current total rotation (base + open
        // animation) so items stay upright while the polygon spins.
        float rot = CurrentRotationDeg();
        if (Mathf.Abs(rot) > 0.01f)
            shopCam.transform.Rotate(Vector3.forward, rot, Space.Self);
    }

    // Recomputes _riftScreenCenter, _riftScreenSize, _screenVerts every frame.
    void UpdateScreenVerts()
    {
        // ── Letterbox: the shop is the bottom bar (full width, animated height). ──
        if (letterbox)
        {
            _riftSizeX      = Screen.width * 0.5f;
            _riftSizeY      = Screen.height * barHeight * 0.5f;   // half a full bar
            _riftScreenSize = Mathf.Max(_riftSizeX, _riftSizeY);

            float H   = _riftSizeY * 2f * _riftScale;             // current animated bar height
            float top = Screen.height - H;
            _riftScreenCenter = new Vector2(Screen.width * 0.5f, Screen.height - H * 0.5f);

            if (_screenVerts == null || _screenVerts.Length != 4) _screenVerts = new Vector2[4];
            _screenVerts[0] = new Vector2(0f,            top);    // bottom-bar rect (GUI coords)
            _screenVerts[1] = new Vector2(Screen.width,  top);
            _screenVerts[2] = new Vector2(Screen.width,  Screen.height);
            _screenVerts[3] = new Vector2(0f,            Screen.height);
            return;
        }

        _riftSizeX        = Screen.width  * riftWidth  * 0.5f;
        _riftSizeY        = Screen.height * riftHeight * 0.5f;
        _riftScreenSize   = Mathf.Max(_riftSizeX, _riftSizeY);   // legacy fields rely on this

        // Y offset: collapsed state drops further toward the bottom of the
        // screen, expanded snaps back to riftScreenPos.y. Lerp via _expandT
        // (0 collapsed 1 expanded) so the motion mirrors the scale anim.
        float yOff = riftCollapsedYOffset * (1f - _expandT);

        // Clamp center keeping the rift on screen but use the EFFECTIVE
        // half-extent (scaled by _riftScale on Y, since collapse-anim shrinks
        // it). This lets the collapsed hint slide much closer to the bottom
        // edge than the fully-expanded footprint would allow.
        float effectiveSizeY = _riftSizeY * Mathf.Max(0.05f, _riftScale);

        float cx = Mathf.Clamp(riftScreenPos.x * Screen.width,
                               _riftSizeX + screenEdgeMargin,
                               Screen.width - _riftSizeX - screenEdgeMargin);
        float cy = Mathf.Clamp((riftScreenPos.y + yOff) * Screen.height,
                               effectiveSizeY + screenEdgeMargin,
                               Screen.height - effectiveSizeY - screenEdgeMargin);
        _riftScreenCenter = new Vector2(cx, cy);

        var shape = _runtimeRiftShape ?? BuiltinShape();
        int n     = shape.Length;
        if (_screenVerts == null || _screenVerts.Length != n)
            _screenVerts = new Vector2[n];

        for (int i = 0; i < n; i++)
        {
            Vector2 rv = Rotate2D(shape[i], CurrentRotationDeg());
            _screenVerts[i] = new Vector2(_riftScreenCenter.x + rv.x * _riftSizeX,
                                          _riftScreenCenter.y - rv.y * _riftSizeY * _riftScale);
        }
    }

   
    // All shop items on a single horizontal row: blocks first, small gap,
    // then turrets. Designed for the bottom-strip rift layout.
    public void SetShopItems(BlockData[] blockDatas, BlockData[] turretDatas,
                             BlockColor[] blockColors, BlockColor[] turretColors,
                             GameObject cubePrefab, GridSystem grid)
    {
        ClearItems();

        int blockN  = blockDatas  != null ? blockDatas.Length  : 0;
        int turretN = turretDatas != null ? turretDatas.Length : 0;
        int totalN  = blockN + turretN;
        if (totalN == 0) return;

        // Small visual gap separating blocks from turrets when both present.
        float gap        = (blockN > 0 && turretN > 0) ? blockSpacing * 0.3f : 0f;
        float totalWidth = (totalN - 1) * blockSpacing + gap;
        float xCursor    = shopCenter.x - totalWidth * 0.5f;
        int   idx        = 0;

        for (int i = 0; i < blockN; i++)
        {
            var sCol = (blockColors != null && i < blockColors.Length) ? blockColors[i] : BlockColor.None;
            SpawnOne(blockDatas[i], sCol, new Vector3(xCursor, shopCenter.y, shopCenter.z),
                     cubePrefab, grid, isTurret: false, idx++);
            xCursor += blockSpacing;
        }
        xCursor += gap;
        for (int i = 0; i < turretN; i++)
        {
            var sCol = (turretColors != null && i < turretColors.Length) ? turretColors[i] : BlockColor.None;
            SpawnOne(turretDatas[i], sCol, new Vector3(xCursor, shopCenter.y, shopCenter.z),
                     cubePrefab, grid, isTurret: true, idx++);
            xCursor += blockSpacing;
        }
    }

    Material _flatMat;
    // Shared unlit material for the 2D/flat block style (color comes per-cube via MpbColor's _BaseColor).
    Material FlatMat()
    {
        if (flatMaterial != null) return flatMaterial;
        if (_flatMat == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            _flatMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        }
        return _flatMat;
    }

    void SpawnOne(BlockData data, BlockColor synergyColor, Vector3 pos,
                  GameObject cubePrefab, GridSystem grid, bool isTurret, int globalIndex)
    {
        if (data == null || data.cells == null) return;

        var root = new GameObject($"Shop_{data.blockType}_{globalIndex}");
        root.transform.position = pos;

        // Tint: synergy color drives the visual when set; turrets / None
        // fall back to PlacementController's BlockType palette so they still
        // read as distinct objects.
        Color col;
        if (synergyColor != BlockColor.None)
        {
            col = BlockColorPalette.Get(synergyColor);
        }
        else
        {
            BlockType bt = data.blockType;
            col = PlacementController.Instance != null
                ? PlacementController.Instance.PickPaletteColor(bt)
                : (isTurret
                    ? new Color(0.25f, 0.85f, 0.95f)
                    : new Color(0.85f, 0.18f, 0.12f));
        }


        foreach (var cell in data.cells)
        {
            var c = Instantiate(cubePrefab, root.transform);
            c.transform.localPosition = (Vector3)cell * grid.cellSize;
            var rend = c.GetComponent<Renderer>();
            if (flatBlocks && rend != null)
            {
                rend.sharedMaterial   = FlatMat();
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (isTurret) rend.enabled = false;
            }
            MpbColor.Set(rend, col);
        }

        var sb = root.AddComponent<SelectableBlock>();
        sb.data  = data;
        sb.color = synergyColor;

        float fluc     = Random.Range(0.82f, 1.22f);
        sb.cachedPrice = ResourceManager.Instance != null
                         ? ResourceManager.Instance.ComputePrice(data, fluc) : 0;

        //if (isTurret) AttachTurretBeacon(root, grid.cellSize, cubePrefab, data.blockType,
        //                                 flatBlocks ? FlatMat() : null);

        if (isTurret)
        {
            if (data.turretPrefab != null)
            {
                GameObject visual = Instantiate(
                    data.turretPrefab,
                    root.transform);

                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one * 50f;

                //foreach (var r in visual.GetComponentsInChildren<Renderer>())
                //    MpbColor.Set(r, currentColor);
            }
        }

        if (_shopLayer >= 0) SetLayerRecursive(root, _shopLayer);   // keep off the main camera

        const float TAU = Mathf.PI * 2f;
        _items.Add(new ShopItem
        {
            root        = root,
            sb          = sb,
            basePos     = pos,
            driftPhase  = new Vector3(Random.Range(0f, TAU), Random.Range(0f, TAU), Random.Range(0f, TAU)),
            tumblePhase = new Vector3(Random.Range(0f, TAU), Random.Range(0f, TAU), Random.Range(0f, TAU)),
        });
    }

    public void ClearItems()
    {
        foreach (var item in _items) if (item.root != null) Destroy(item.root);
        _items.Clear();
        _hovered = null;
    }

    // Same visual rule as placed turrets: hide the cube body, show a single
    // floating diamond. Keeps shop preview consistent with what gets placed.
    static void AttachTurretBeacon(GameObject root, float cs, GameObject cubePrefab, BlockType turretType,
                                   Material flatOverride = null)
    {
        if (root == null) return;

        Vector3 centroid = Vector3.zero;
        int     n        = 0;
        foreach (Transform child in root.transform)
        {
            centroid += child.position;
            n++;
        }
        if (n == 0) return;
        centroid /= n;

        foreach (var r in root.GetComponentsInChildren<Renderer>())
            r.enabled = false;


        var marker  = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.name = "TurretBeacon";
        marker.transform.SetParent(root.transform, worldPositionStays: false);
        marker.transform.position      = centroid;
        marker.transform.localScale    = Vector3.one * (0.62f * cs);
        marker.transform.localRotation = Quaternion.Euler(45f, 45f, 0f);

        var col = marker.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var rend = marker.GetComponent<Renderer>();
        if (rend != null)
        {
            // Flat/unlit override (2D style) when requested, else match cubePrefab's material.
            if (flatOverride != null)
            {
                rend.sharedMaterial = flatOverride;
            }
            else
            {
                var prefabRend = cubePrefab != null ? cubePrefab.GetComponentInChildren<Renderer>() : null;
                if (prefabRend != null && prefabRend.sharedMaterial != null)
                    rend.sharedMaterial = prefabRend.sharedMaterial;
            }

            // Per-subtype color: Basic = cyan, Slow = blue-violet, AOE = orange.
            // Same palette as placed beacons via TurretTypes.DisplayColor.
            MpbColor.Set(rend, TurretTypes.DisplayColor(turretType));
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        TurretBeacon tb=marker.AddComponent<TurretBeacon>();
    }

    /// <summary>Immediate removal use for programmatic cleanup (ClearItems, etc.).</summary>
    public void RemoveItem(GameObject go) =>
        _items.RemoveAll(item => item.root == go);

    /// <summary>
    /// Removes the item from the shop list and plays a pop-shrink animation
    /// before destroying the GameObject.  Returns true if the object was found
    /// in the shop (and will be destroyed by the coroutine); false if it wasn't
    /// a shop item (caller is responsible for destroying it).
    /// </summary>
    public bool RemoveItemAnimated(GameObject go)
    {
        bool found = _items.RemoveAll(item => item.root == go) > 0;
        if (found && go != null) StartCoroutine(ShrinkOut(go));
        return found;
    }

    // Short pop-then-shrink sequence: scales up 25 % briefly then collapses.
    System.Collections.IEnumerator ShrinkOut(GameObject go)
    {
        const float dur = 0.28f;
        float       t   = 0f;
        Vector3     s0  = go.transform.localScale;
        Vector3     p0  = go.transform.position;

        while (t < dur && go != null)
        {
            t += Time.deltaTime;
            float frac = Mathf.Clamp01(t / dur);

            // 0.2: pop up to 1.25×   |   0.2: shrink to 0
            float scale = frac < 0.20f
                ? Mathf.Lerp(1f,    1.25f, frac / 0.20f)
                : Mathf.Lerp(1.25f, 0f,   (frac - 0.20f) / 0.80f);

            go.transform.localScale = s0 * scale;
            go.transform.position   = p0 + Vector3.up * (frac * frac * 0.6f);
            yield return null;
        }
        if (go != null) Destroy(go);
    }

    public bool TryHandleClick()
    {
        if (!IsMouseInShopView()) return false;
        if (_hovered == null) return true;

        var rm = ResourceManager.Instance;
        if (rm != null && !rm.CanAfford(_hovered.sb.cachedPrice, _hovered.sb.data.blockType))
        {
            _cantAffordFlash = 0.55f;   // trigger red rift-edge flash
            return true;                // consume click, stay in shop
        }

        // Tutorial: block buying the wrong item (flash, stay in shop, item kept).
        if (!TutorialDirector.CanPurchase(_hovered.sb.data))
        {
            _cantAffordFlash = 0.55f;
            return true;
        }

        // Hide the item while held RestoreItem re-shows it on cancel.
        _hovered.root.SetActive(false);
        PlacementController.Instance?.GrabFromShop(_hovered.sb);
        Collapse();
        return true;
    }

    /// <summary>
    /// Called by PlacementController when the player cancels placement of a
    /// shop item without placing it.  Makes the item visible again and reopens
    /// the rift so the player can see it returned.
    /// </summary>
    public void RestoreItem(GameObject go)
    {
        if (go == null) return;
        go.SetActive(true);
        _expanded = true;   // reopen rift
    }

    public bool IsMouseInShopView()
    {
        if (_riftScale < 0.05f || _screenVerts == null) return false;
        // VirtualCursor.Position has y=0 at bottom (mouse convention); GUI space has y=0 at top.
        Vector2 mp = new Vector2(VirtualCursor.Position.x,
                                 Screen.height - VirtualCursor.Position.y);
        return PointInPolygon(mp, _screenVerts);
    }

    // ── Animations ────────────────────────────────────────────────────────────

    void AnimateItems()
    {
        float t = Time.time;
        float lerpK = 1f - Mathf.Exp(-hoverLerpSpeed * Time.deltaTime);
        foreach (var item in _items)
        {
            if (item.root == null) continue;

            // Drift: bounded sin around basePos so items never escape their slot.
            Vector3 drift = new Vector3(
                Mathf.Sin(t * driftSpeed.x + item.driftPhase.x) * driftAmplitude.x,
                Mathf.Sin(t * driftSpeed.y + item.driftPhase.y) * driftAmplitude.y,
                Mathf.Sin(t * driftSpeed.z + item.driftPhase.z) * driftAmplitude.z
            );
            item.root.transform.position = item.basePos + drift;

            Vector3 euler = new Vector3(
                Mathf.Sin(t * tumbleSpeed         + item.tumblePhase.x),
                Mathf.Sin(t * tumbleSpeed * 0.73f + item.tumblePhase.y),
                Mathf.Sin(t * tumbleSpeed * 1.31f + item.tumblePhase.z)
            ) * tumbleAmplitude;
            item.root.transform.rotation = Quaternion.Euler(euler);

            // Hover feedback: pop up to hoverScale, ease back when not hovered.
            float target = (item == _hovered) ? hoverScale : 1f;
            float cur    = item.root.transform.localScale.x;
            float next   = Mathf.Lerp(cur, target, lerpK);
            item.root.transform.localScale = Vector3.one * next;
        }
    }

    [Tooltip("SphereCast radius for shop hover — bigger = easier to hover small items. 0 = exact raycast.")]
    public float hoverRadius = 0.35f;

    [Header("Debug")]
    public bool logHover;

    void UpdateHover()
    {
        _hovered = null;
        if (shopCam == null || !IsMouseInShopView()) return;

        Vector3 vp  = ScreenToShopViewport(VirtualCursor.Position);
        Ray     ray = shopCam.ViewportPointToRay(vp);
        RaycastHit hit;
        bool got = hoverRadius > 0f
            ? Physics.SphereCast(ray, hoverRadius, out hit, 1000f)
            : Physics.Raycast(ray, out hit, 1000f);
        if (!got) return;

        var sb = hit.transform.GetComponentInParent<SelectableBlock>();
        if (sb == null) return;
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].sb != sb) continue;
            _hovered = _items[i];
            if (logHover)
                Debug.Log($"[Shop] hover idx={i}  name={sb.gameObject.name}  vp={vp}");
            return;
        }
    }

    Vector3 ScreenToShopViewport(Vector2 mp)
    {
        // 1. Offset from rift centre in normalised rift-screen units (still in rotated space).
        // X uses _riftSizeX (full width). Y uses _riftSizeY * _riftScale because
        // the polygon's Y is scaled by _riftScale.
        float guiY = Screen.height - mp.y;
        float rx   = (mp.x               - _riftScreenCenter.x) / (_riftSizeX + 0.001f);
        float ry   = (_riftScreenCenter.y - guiY)                / (_riftSizeY * _riftScale + 0.001f);

        // 2. Undo the on-screen rotation to get into the shape's native (unrotated)
        //    space. UpdateScreenVerts applied +CurrentRotationDeg(), so invert.
        Vector2 local = Rotate2D(new Vector2(rx, ry), -CurrentRotationDeg());

        // 3. Map to camera viewport using the same shape bounds as DrawContent.
        //    ViewportPointToRay uses y=0 at bottom, y=1 at top no D3D flip here.
        var   shape = _runtimeRiftShape ?? BuiltinShape();
        float minX  = float.MaxValue, maxX = float.MinValue;
        float minY  = float.MaxValue, maxY = float.MinValue;
        foreach (var p in shape)
        {
            if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
        }
        // No U flip: camera.right = world +X, viewport X maps directly to shape X.
        float vpx = Mathf.Clamp01((local.x - minX) / Mathf.Max(maxX - minX, 0.001f));
        float vpy = Mathf.Clamp01((local.y - minY) / Mathf.Max(maxY - minY, 0.001f));
        if (flipHoverX) vpx = 1f - vpx;
        if (flipHoverY) vpy = 1f - vpy;
        return new Vector3(vpx, vpy, 0f);
    }

    // ── Public query (DebugUI) ────────────────────────────────────────────────

    public struct ShopItemInfo
    {
        public string displayName;
        public int    price;
        public bool   affordable;
    }

    public List<ShopItemInfo> GetShopInfos()
    {
        var result = new List<ShopItemInfo>();
        var rm     = ResourceManager.Instance;
        foreach (var item in _items)
        {
            if (item.sb?.data == null) continue;
            int price = item.sb.cachedPrice;
            result.Add(new ShopItemInfo
            {
                displayName = TurretTypes.Is(item.sb.data.blockType) ? TurretTypes.DisplayName(item.sb.data.blockType) : item.sb.data.DisplayName,
                price       = price,
                affordable  = rm != null && rm.CanAfford(price, item.sb.data.blockType),
            });
        }
        return result;
    }

    // ── OnGUI ─────────────────────────────────────────────────────────────────

    [Header("Debug")]
    [Tooltip("Show a small RT thumbnail to the right of the rift (remove once working).")]
    public bool debugShowRTPreview = true;

    void OnGUI()
    {
        if (SettingsScreen.Open || IntroDirector.Playing || GameFlowManager.SettlementUp) return;   // hidden behind settings / intro / clear settlement
        BuildStyles();
        if (_riftScale > 0.005f) DrawRift();
        if (_riftScale > 0.5f)   DrawPriceLabels();
        if (_hovered != null && _riftScale > 0.1f) DrawTooltip(_hovered);
        DrawRiftLabel();

        // ── Debug: raw RT preview ─────────────────────────────────────────────
        if (debugShowRTPreview && _shopRT != null)
        {
            float pw = 120f, ph = 240f;
            float px = riftScreenPos.x * Screen.width + Screen.height * riftHeight * 0.6f;
            float py = riftScreenPos.y * Screen.height - ph * 0.5f;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(px, py, pw, ph), _shopRT, ScaleMode.StretchToFill, false);
            GUI.color = new Color(1, 1, 0, 0.7f);
            GUI.DrawTexture(new Rect(px, py, pw, 1), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(px, py + ph - 1, pw, 1), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(px, py, 1, ph), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(px + pw - 1, py, 1, ph), Texture2D.whiteTexture);
            GUI.color = Color.white;

            if (_ttSub != null)
            {
                _ttSub.normal.textColor = Color.yellow;
                GUI.Label(new Rect(px, py - 16f, 120f, 16f), "RT preview (debug)", _ttSub);
            }
        }
        //if(_expanded)
        DrawRefreshButton();
        
    }

    void DrawRefreshButton()
    {
        if (PlacementController.Instance == null) return;

        // Collapsed → a shop-open button in the same spot; click expands the shop.
        if (!ShopVisible) { DrawShopOpenButton(); return; }

        int   cost = PlacementController.Instance.RefreshCost;
        float s    = Mathf.Max(0.5f, Screen.height / 1080f);   // scale with screen
        float d    = refreshButtonSize * s;                    // circle diameter

        // Dock to the shop rift's bottom-right corner, inset by refreshButtonOffset.
        Vector2 br = ShopBottomRight;
        Rect r = new Rect(br.x - d - refreshButtonOffset.x * s,
                          br.y - d - refreshButtonOffset.y * s, d, d);

        bool      hover  = r.Contains(Event.current.mousePosition);
        Texture2D circle = UIRoundedRect.CircleTex();
        Color     prev   = GUI.color;

        // Round paper button (no border; gold-tinted on hover).
        Color fill = hover ? Color.Lerp(tooltipBg, GeoPalette.Gold, 0.28f) : tooltipBg;
        GUI.color = fill;
        GUI.DrawTexture(r, circle, ScaleMode.StretchToFill, true);

        // Icon (tinted ink so it reads on paper); fallback glyph if none.
        if (refreshIcon != null && refreshIcon.texture != null)
        {
            float pad = 3f * s;
            GUI.color = GeoPalette.Ink;
            GUI.DrawTexture(new Rect(r.x + pad, r.y + pad, d - pad * 2f, d - pad * 2f),
                            refreshIcon.texture, ScaleMode.ScaleToFit, true);
        }
        else
        {
            var gs = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(26f * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            gs.normal.textColor = GeoPalette.Ink;
            GUI.color = Color.white;
            GUI.Label(r, "⟳", gs);
        }

        // Hover → reveal the cost in a small paper tag (shop tooltip style) to the right.
        if (hover)
        {
            float pw = 36f * s, ph = 32f * s;
            Rect tag = new Rect(r.xMax - 8f * s, r.center.y - ph * 0.5f, pw, ph);
            GUI.color = tooltipBg;
            GUI.DrawTexture(tag, Texture2D.whiteTexture);
            GUI.color = GeoPalette.Signal;                                   // accent spine
            GUI.DrawTexture(new Rect(tag.x, tag.y, 4f * s, tag.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            var cs = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(17f * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            cs.normal.textColor = GeoPalette.Ink;
            GUI.Label(tag, cost.ToString(), cs);
        }

        GUI.color = prev;

        if (GUI.Button(r, GUIContent.none, GUIStyle.none))
            PlacementController.Instance.TryRefreshShop();
    }

    // Shown when the shop is collapsed: a round paper button (shop sprite) at the
    // bottom-right corner. Clicking it opens the shop. Hidden during combat.
    void DrawShopOpenButton()
    {
        float s = Mathf.Max(0.5f, Screen.height / 1080f);
        float d = refreshButtonSize * s;
        Vector2 br = ShopBottomRight;   // bottom-right corner (Screen.width, Screen.height) when collapsed
        Rect r = new Rect(br.x - d - refreshButtonOffset.x * s,
                          br.y - d - refreshButtonOffset.y * s, d, d);

        bool  hover  = r.Contains(Event.current.mousePosition);
        Color prev   = GUI.color;
        var   circle = UIRoundedRect.CircleTex();

        GUI.color = hover ? Color.Lerp(tooltipBg, GeoPalette.Gold, 0.28f) : tooltipBg;
        GUI.DrawTexture(r, circle, ScaleMode.StretchToFill, true);

        if (shopButtonIcon != null && shopButtonIcon.texture != null)
        {
            float pad = 3f * s;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(r.x + pad, r.y + pad, d - pad * 2f, d - pad * 2f),
                            shopButtonIcon.texture, ScaleMode.ScaleToFit, true);
        }
        else
        {
            var gs = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(22f * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            gs.normal.textColor = GeoPalette.Ink;
            GUI.color = Color.white;
            GUI.Label(r, "S", gs);
        }
        GUI.color = prev;

        if (GUI.Button(r, GUIContent.none, GUIStyle.none))
            _expanded = true;   // open the shop
    }

    // ── GL rift rendering ─────────────────────────────────────────────────────

    void DrawRift()
    {
        if (_shopRT == null || _screenVerts == null) return;
        if (Event.current.type != EventType.Repaint) return;

        EnsureMaterials();

        // ── Letterbox: bars AND content are drawn as UGUI (UpdateLetterboxBars),
        //    so they sit under the HUD. Nothing to draw here in IMGUI. ──
        if (letterbox) return;

        GL.PushMatrix();
        GL.LoadPixelMatrix();

        DrawGlow();
        DrawContent();
        DrawInnerVignette();   // dark fade toward the rift's inner edge
        DrawEdge();
        DrawRadialRays();
        DrawSparkles();

        GL.PopMatrix();
    }

    void EnsureMaterials()
    {
        // ── RT content material ───────────────────────────────────────────────
        // URP may not include "Unlit/Texture"; try several fallbacks in order.
        if (_riftMat == null || _riftMat.shader == null || !_riftMat.shader.isSupported)
        {
            if (_riftMat != null) Destroy(_riftMat);
            Shader sh = Shader.Find("Sprites/Default")
                     ?? Shader.Find("Unlit/Texture")
                     ?? Shader.Find("UI/Default");
            if (sh == null)
            {
                Debug.LogError("[Shop] No texture shader found for rift content (Sprites/Default / Unlit/Texture / UI/Default).");
            }
            else
            {
                _riftMat           = new Material(sh);
                _riftMat.hideFlags = HideFlags.HideAndDontSave;
            }
        }
        if (_riftMat != null && _shopRT != null) _riftMat.mainTexture = _shopRT;

        // ── Colour / line material ────────────────────────────────────────────
        if (_colorMat != null) return;
        Shader col = Shader.Find("Hidden/Internal-Colored")
                  ?? Shader.Find("Sprites/Default");
        if (col == null) { Debug.LogError("[Shop] No line shader found."); return; }
        _colorMat = new Material(col);
        _colorMat.hideFlags = HideFlags.HideAndDontSave;
        _colorMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _colorMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _colorMat.SetInt("_Cull",     (int)UnityEngine.Rendering.CullMode.Off);
        _colorMat.SetInt("_ZWrite",   0);
    }

    // Soft outer glow: draw polygon at increasing scale with decreasing alpha.
    void DrawGlow()
    {
        int n = _screenVerts.Length;
        _colorMat.SetPass(0);
        for (int g = riftGlowLayers; g >= 1; g--)
        {
            float gScale = 1f + g * 0.06f;
            float alpha  = riftGlowColor.a * (1f - (float)(g - 1) / riftGlowLayers);
            GL.Begin(GL.TRIANGLES);
            GL.Color(new Color(riftGlowColor.r, riftGlowColor.g, riftGlowColor.b, alpha));
            for (int i = 0; i < n; i++)
            {
                int     j  = (i + 1) % n;
                Vector2 si = ScaleFromCenter(_screenVerts[i], gScale);
                Vector2 sj = ScaleFromCenter(_screenVerts[j], gScale);
                GL.Vertex3(_riftScreenCenter.x, _riftScreenCenter.y, 0);
                GL.Vertex3(si.x, si.y, 0);
                GL.Vertex3(sj.x, sj.y, 0);
            }
            GL.End();
        }
    }

    // RT content clipped to rift polygon, fan-triangulated from centre.
    void DrawContent()
    {
        if (_riftMat == null) return;
        // Iterate the subdivided shape so the triangle fan follows the noisy
        // crack outline. UV bounds stay from the base shape so the RT content
        // doesn't stretch as vertices wobble.
        var shape     = _runtimeRiftShape ?? BuiltinShape();
        var baseShape = shape;
        int n         = shape.Length;

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var p in baseShape)
        {
            if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
        }
        float rx = Mathf.Max(maxX - minX, 0.001f);
        float ry = Mathf.Max(maxY - minY, 0.001f);

        float scaleY = Mathf.Max(_riftScale, 0.0001f);
        System.Func<Vector2, Vector2> toUV =
            p =>
            {
                float uvx = (p.x - minX) / rx;
                float uvy = (maxY - p.y) / ry;
                uvy = 0.5f + (uvy - 0.5f) * scaleY;
                return new Vector2(uvx, uvy);
            };

        Vector2 centerUV = toUV(Vector2.zero);

        _riftMat.SetPass(0);
        GL.Begin(GL.TRIANGLES);
        GL.Color(Color.white);   // required when material uses vertex colour (Sprites/Default)
        for (int i = 0; i < n; i++)
        {
            int     j   = (i + 1) % n;
            Vector2 uvi = toUV(shape[i]);
            Vector2 uvj = toUV(shape[j]);

            GL.TexCoord2(centerUV.x, centerUV.y);
            GL.Vertex3(_riftScreenCenter.x, _riftScreenCenter.y, 0);

            GL.TexCoord2(uvi.x, uvi.y);
            GL.Vertex3(_screenVerts[i].x, _screenVerts[i].y, 0);

            GL.TexCoord2(uvj.x, uvj.y);
            GL.Vertex3(_screenVerts[j].x, _screenVerts[j].y, 0);
        }
        GL.End();
    }

    // Pulsing outline + bright inner hair-line for depth.
    void DrawEdge()
    {
        int   n     = _screenVerts.Length;
        float pulse = 0.70f + 0.30f * Mathf.Sin(Time.time * edgePulseSpeed);

        // Flash red briefly when the player can't afford the hovered item.
        Color edgeCol = _cantAffordFlash > 0f
            ? Color.Lerp(riftEdgeColor, new Color(1f, 0.18f, 0.18f, 1f),
                         _cantAffordFlash / 0.55f)
            : riftEdgeColor;

        _colorMat.SetPass(0);

        // Outer line
        GL.Begin(GL.LINES);
        GL.Color(new Color(edgeCol.r, edgeCol.g, edgeCol.b, edgeCol.a * pulse));
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            GL.Vertex3(_screenVerts[i].x, _screenVerts[i].y, 0);
            GL.Vertex3(_screenVerts[j].x, _screenVerts[j].y, 0);
        }
        GL.End();

        // Inner bright hair-line (slight inset)
        GL.Begin(GL.LINES);
        GL.Color(new Color(1f, 1f, 0.75f, 0.40f * pulse));
        for (int i = 0; i < n; i++)
        {
            int     j  = (i + 1) % n;
            Vector2 si = ScaleFromCenter(_screenVerts[i], 0.96f);
            Vector2 sj = ScaleFromCenter(_screenVerts[j], 0.96f);
            GL.Vertex3(si.x, si.y, 0);
            GL.Vertex3(sj.x, sj.y, 0);
        }
        GL.End();
    }

    // Fan with vertex-color gradient: transparent at centre, black at the
    // polygon edge. Reads as the rift opening up into darkness.
    void DrawInnerVignette()
    {
        if (innerVignetteStrength <= 0f || _screenVerts == null) return;
        int n = _screenVerts.Length;

        _colorMat.SetPass(0);
        GL.Begin(GL.TRIANGLES);
        Color centreCol = new Color(0f, 0f, 0f, 0f);
        Color edgeCol   = new Color(0f, 0f, 0f, innerVignetteStrength);
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            GL.Color(centreCol);
            GL.Vertex3(_riftScreenCenter.x, _riftScreenCenter.y, 0);
            GL.Color(edgeCol);
            GL.Vertex3(_screenVerts[i].x, _screenVerts[i].y, 0);
            GL.Vertex3(_screenVerts[j].x, _screenVerts[j].y, 0);
        }
        GL.End();
    }

    void DrawRadialRays()
    {
        if (rayCount <= 0) return;
        _colorMat.SetPass(0);
        GL.Begin(GL.LINES);

        float t       = Time.time;
        float baseRot = t * raySpinSpeed;
        for (int i = 0; i < rayCount; i++)
        {
            float angle = (i / (float)rayCount) * Mathf.PI * 2f + baseRot;

            // Each ray's length pulses independently.
            float phase  = i * 1.713f;
            float pulse  = 0.5f + 0.5f * Mathf.Sin(t * rayPulseSpeed + phase);
            float len    = Mathf.Lerp(rayLengthRange.x, rayLengthRange.y, pulse)
                           * _riftScreenSize;

            // Start a bit out from center so the rays look like they emerge
            // from the rift mouth, not a single dot.
            Vector2 dir   = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 start = _riftScreenCenter + dir * (0.30f * _riftScreenSize);
            Vector2 end   = _riftScreenCenter + dir * len;

            // Fade: bright at start, fully transparent at tip.
            float aBase = energyRayColor.a * (0.4f + 0.6f * pulse) * _riftScale;
            GL.Color(new Color(energyRayColor.r, energyRayColor.g, energyRayColor.b, aBase));
            GL.Vertex3(start.x, start.y, 0);
            GL.Color(new Color(energyRayColor.r, energyRayColor.g, energyRayColor.b, 0f));
            GL.Vertex3(end.x,   end.y,   0);
        }
        GL.End();
    }
    void DrawSparkles()
    {
        if (sparkleCount <= 0 || _riftScale < 0.02f) return;
        _colorMat.SetPass(0);
        GL.Begin(GL.LINES);

        float t = Time.time;
        for (int i = 0; i < sparkleCount; i++)
        {
            float angle  = i * 2.3998f;   
            float radNorm = 0.65f + ((i * 31 + 17) % 100) / 100f * 0.85f;
            float radius  = radNorm * sparkleRadius * _riftScreenSize;
            Vector2 pos   = _riftScreenCenter + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

            float twink = 0.5f + 0.5f * Mathf.Sin(t * sparkleTwinkleSpeed + i * 0.91f);
            twink = twink * twink * twink;
            if (twink < 0.03f) continue;

            float r = (1.8f + 1.6f * twink) * Mathf.Clamp01(_riftScale);
            GL.Color(new Color(sparkleColor.r, sparkleColor.g, sparkleColor.b,
                               sparkleColor.a * twink));
            GL.Vertex3(pos.x - r, pos.y,     0); GL.Vertex3(pos.x + r, pos.y,     0);
            GL.Vertex3(pos.x,     pos.y - r, 0); GL.Vertex3(pos.x,     pos.y + r, 0);
        }
        GL.End();
    }

    // ── Rift label ────────────────────────────────────────────────────────────

    void DrawRiftLabel()
    {
        if (GameFlowManager.Instance?.phase == GamePhase.Running) return;
        if (_riftScale < 0.01f || _screenVerts == null || _screenVerts.Length == 0) return;

        Vector2 apex  = _screenVerts[0];   // index 0 = top apex
        float   alpha = _expanded
                        ? 0.90f
                        : Mathf.Clamp01(_riftScale / (riftHintScale + 0.01f)) * 0.70f;

        _hintStyle.normal.textColor = new Color(1f, 0.88f, 0.38f, alpha);
        string lbl = _expanded ? $"SHOP  [{shopToggleKey}]" : $"[{shopToggleKey}]";
        GUI.Label(new Rect(apex.x - 42f, apex.y - 22f, 110f, 20f), lbl, _hintStyle);
    }

    // ── Tooltip ───────────────────────────────────────────────────────────────

    void DrawTooltip(ShopItem item)
    {
        var rm = ResourceManager.Instance;
        if (rm == null) return;

        BlockType type    = item.sb.data.blockType;
        int       price   = item.sb.cachedPrice;
        bool      afford  = rm.CanAfford(price, type);
        int       placed  = rm.PlacedCount(type);
        int       pool    = TurretTypes.Is(type) ? rm.TurretCurrency : rm.BlockCurrency;
        int       deficit = price - pool;

        // Pin to the side of the rift's screen-space bounds so the tooltip
        // never covers items. Prefer right; fall back to left if right is
        // off-screen; finally try below / above as last resorts.
        float       ts = Mathf.Max(0.5f, tooltipScale);
        float       bw = 170f * ts;
        bool        hasTheme = item.sb.color != BlockColor.None;
        // Tooltip rows: title (shape + colored tag) + price
        //             + 2-line synergy description if themed.
        float       bh       = (hasTheme ? 82f : 44f) * ts;
        float       pad      = 12f * ts;

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        if (_screenVerts != null)
            foreach (var v in _screenVerts)
            {
                if (v.x < minX) minX = v.x;
                if (v.x > maxX) maxX = v.x;
                if (v.y < minY) minY = v.y;
                if (v.y > maxY) maxY = v.y;
            }
        if (minX > maxX)   // no verts yet
        {
            minX = _riftScreenCenter.x - _riftScreenSize; maxX = _riftScreenCenter.x + _riftScreenSize;
            minY = _riftScreenCenter.y - _riftScreenSize; maxY = _riftScreenCenter.y + _riftScreenSize;
        }

        float tx, ty;
        ty = _riftScreenCenter.y - bh * 0.5f;
        if (maxX + pad + bw <= Screen.width - 4f)              tx = maxX + pad;            // right
        else if (minX - pad - bw >= 4f)                        tx = minX - pad - bw;       // left
        else
        {
            tx = Mathf.Clamp(_riftScreenCenter.x - bw * 0.5f, 4f, Screen.width - bw - 4f);
            ty = (maxY + pad + bh <= Screen.height - 4f) ? maxY + pad : minY - pad - bh;
        }
        tx = Mathf.Clamp(tx, 4f, Screen.width  - bw - 4f);
        ty = Mathf.Clamp(ty, 4f, Screen.height - bh - 4f);

        float inset = 8f * ts;
        GUI.color = tooltipBg;
        GUI.DrawTexture(new Rect(tx - inset, ty - inset, bw, bh), Texture2D.whiteTexture);
        GUI.color = GeoPalette.Ink;                                       // ink top rule
        GUI.DrawTexture(new Rect(tx - inset, ty - inset, bw, 5f * ts), Texture2D.whiteTexture);
        GUI.color = afford ? GeoPalette.Blue : GeoPalette.Signal;         // accent spine on the left
        GUI.DrawTexture(new Rect(tx - inset, ty - inset, 4f * ts, bh), Texture2D.whiteTexture);
        GUI.color = Color.white;

        string shape = TurretTypes.Is(type) ? TurretTypes.DisplayName(type) : item.sb.data.ShapeName;
        if (string.IsNullOrEmpty(shape)) shape = "Block";

        string title;
        if (hasTheme)
        {
            var themeRgb = BlockColorPalette.Get(item.sb.color);
            string hex   = ColorUtility.ToHtmlStringRGB(themeRgb);
            title = $"{shape}  ·  <color=#{hex}>{item.sb.color}</color>";
        }
        else
        {
            title = shape;
        }

        _ttTitle.fontSize = Mathf.RoundToInt(10f * ts);
        _ttPrice.fontSize = Mathf.RoundToInt(10f * ts);
        _ttSub.fontSize   = Mathf.RoundToInt(9f  * ts);

        _ttTitle.richText = true;
        GUI.Label(new Rect(tx, ty, bw - 16f * ts, 16f * ts), title, _ttTitle);

        float yCursor = ty + 18f * ts;
        _ttPrice.normal.textColor = afford ? GeoPalette.Blue : GeoPalette.Signal;
        string sfx       = TurretTypes.Is(type) ? " T" : " B";
        string priceText = afford ? $"{price}{sfx}" : $"{price}{sfx}  (-{deficit})";
        GUI.Label(new Rect(tx, yCursor, bw - 16f * ts, 14f * ts), priceText, _ttPrice);
        yCursor += 18f * ts;

        if (hasTheme)
        {
            string desc = BlockColorPalette.Description(item.sb.color);
            if (!string.IsNullOrEmpty(desc))
            {
                _ttSub.normal.textColor = new Color(0.30f, 0.30f, 0.30f);   // soft ink on paper
                _ttSub.wordWrap         = true;
                GUI.Label(new Rect(tx, yCursor, bw - 16f * ts, 36f * ts), desc, _ttSub);
            }
        }
    }

    // Per-item floating price tag, always visible above each shop block.
    void DrawPriceLabels()
    {
        var rm = ResourceManager.Instance;
        if (rm == null || shopCam == null) return;

        // Size everything EXPLICITLY here. `_ttSub` is a shared GUIStyle that
        // DrawTooltip mutates (fontSize = 9 × tooltipScale), so price tags were
        // stuck at the small base size 9 until the player hovered an item once —
        // which is why the floating price "sometimes" showed small instead of max.
        float ts = Mathf.Max(0.5f, tooltipScale);
        float k  = ts * 0.5f;                 // 1.0 at the default tooltipScale (= 2)
        float w  = 64f * k, h = 18f * k;
        int   prevFs   = _ttSub.fontSize;
        var   prevCol0 = _ttSub.normal.textColor;
        var   prevAlgn = _ttSub.alignment;
        _ttSub.fontSize  = Mathf.RoundToInt(9f * ts);
        _ttSub.alignment = TextAnchor.MiddleCenter;

        foreach (var item in _items)
        {
            if (item.root == null || item.sb?.data == null) continue;

            Vector3 vp3 = shopCam.WorldToViewportPoint(item.root.transform.position);
            if (vp3.z < 0f) continue;

            Vector2 screenPos = ShopViewportToScreen(vp3);

            BlockType type   = item.sb.data.blockType;
            int       price  = item.sb.cachedPrice;
            bool      afford = rm.CanAfford(price, type);
            string    sfx    = TurretTypes.Is(type) ? "T" : "B";
            Color     col    = afford ? new Color(0.55f, 1f, 0.60f, 1f)
                                       : new Color(1f,    0.45f, 0.45f, 1f);

            var rect = new Rect(screenPos.x - w * 0.5f, screenPos.y - 34f * k, w, h);

            GUI.color = new Color(0f, 0f, 0f, 0.60f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            _ttSub.normal.textColor = col;
            GUI.Label(rect, $"{price}¤{sfx}", _ttSub);
        }

        _ttSub.fontSize         = prevFs;
        _ttSub.normal.textColor = prevCol0;
        _ttSub.alignment        = prevAlgn;
    }

    // Forward map: shop camera viewport (0..1) screen position inside rift polygon.
    // Mirrors the UV mapping used by DrawContent so item positions track the
    // visible RT content.
    Vector2 ShopViewportToScreen(Vector3 shopVp)
    {
        var baseShape = _runtimeRiftShape ?? BuiltinShape();
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var p in baseShape)
        {
            if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
        }
        float rx = Mathf.Max(maxX - minX, 0.001f);
        float ry = Mathf.Max(maxY - minY, 0.001f);

        // Inverse of toUV in DrawContent: UV.y is V-flipped.
        Vector2 native = new Vector2(minX + shopVp.x * rx,
                                     minY + shopVp.y * ry);
        Vector2 rotated = Rotate2D(native, CurrentRotationDeg());
        // Match UpdateScreenVerts: X full, Y scaled by _riftScale.
        return new Vector2(_riftScreenCenter.x + rotated.x * _riftSizeX,
                           _riftScreenCenter.y - rotated.y * _riftSizeY * _riftScale);
    }

    // ── Style builder ─────────────────────────────────────────────────────────

    void BuildStyles()
    {
        if (_stylesBuilt) return;
        _stylesBuilt = true;

        _ttTitle = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Bold };
        _ttTitle.normal.textColor = GeoPalette.Ink;

        _ttPrice = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Bold };
        _ttPrice.normal.textColor = Color.green;

        _ttSub = new GUIStyle(GUI.skin.label) { fontSize = 9 };
        _ttSub.normal.textColor = new Color(0.65f, 0.65f, 0.65f);

        _hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold };
    }

    // ── GL helpers ────────────────────────────────────────────────────────────

    Vector2 ScaleFromCenter(Vector2 pt, float scale) =>
        _riftScreenCenter + (pt - _riftScreenCenter) * scale;

    // Outward-facing normal at vertex i (points away from rift centre).
    Vector2 OutwardNormal(int i)
    {
        int     n    = _screenVerts.Length;
        Vector2 toPrev = (_screenVerts[i] - _screenVerts[(i - 1 + n) % n]).normalized;
        Vector2 toNext = (_screenVerts[(i + 1) % n] - _screenVerts[i]).normalized;
        Vector2 avg    = (toPrev + toNext).normalized;
        Vector2 c1     = new Vector2(-avg.y,  avg.x);
        Vector2 c2     = new Vector2( avg.y, -avg.x);
        return Vector2.Dot(c1, _screenVerts[i] - _riftScreenCenter) >= 0 ? c1 : c2;
    }

    // Position at fraction t [0,1] around the rift perimeter.
    Vector2 PointOnPerimeter(float[] segs, float total, float t)
    {
        float target = t * total;
        float acc    = 0f;
        int   n      = _screenVerts.Length;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            if (acc + segs[i] >= target)
                return Vector2.Lerp(_screenVerts[i], _screenVerts[j],
                                    Mathf.Clamp01((target - acc) / segs[i]));
            acc += segs[i];
        }
        return _screenVerts[0];
    }

    // ── Shape builder ─────────────────────────────────────────────────────────

    Vector2[] BuiltinShape() => shapePreset switch
    {
        RiftShapePreset.Rectangle => RiftShape_Rectangle,
        RiftShapePreset.Triangle  => RiftShape_Triangle,
        RiftShapePreset.Lens      => RiftShape_Lens,
        RiftShapePreset.Oval      => RiftShape_Oval,
        RiftShapePreset.Slash     => RiftShape_Slash,
        RiftShapePreset.Crack     => RiftShape_Crack,
        RiftShapePreset.Diamond   => RiftShape_Diamond,
        _                         => RiftShape_Crack,
    };

    // Total polygon rotation = configured base + animated "spin open" amount.
    // Used everywhere we need the rift's current world-orientation: vertex
    // placement, camera counter-roll, hover unprojection.
    float CurrentRotationDeg() =>
        riftRotationDeg + (1f - Mathf.Clamp01(_riftScale)) * openSpinDegrees;

    void BuildShapeFromSprite()
    {
        if (shapePreset != RiftShapePreset.Custom)
        {
            _runtimeRiftShape = BuiltinShape();
            return;
        }

        if (riftSprite == null)
        {
            _runtimeRiftShape = null;   // falls back to Lens via BuiltinShape
            return;
        }

        var pts   = new List<Vector2>();
        int count = riftSprite.GetPhysicsShapeCount();
        if (count == 0 || riftSprite.GetPhysicsShape(0, pts) < 3)
        {
            Debug.LogWarning("[ShopController] riftSprite has no physics shape. " +
                             "Enable 'Generate Physics Shape' in its import settings. " +
                             "Falling back to built-in rift shape.");
            _runtimeRiftShape = null;
            return;
        }

        // Centre and normalise so the largest half-extent = 1.
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var p in pts)
        {
            if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
        }
        float cx  = (minX + maxX) * 0.5f;
        float cy  = (minY + maxY) * 0.5f;
        float ext = Mathf.Max((maxX - minX) * 0.5f, (maxY - minY) * 0.5f, 0.001f);

        _runtimeRiftShape = new Vector2[pts.Count];
        for (int i = 0; i < pts.Count; i++)
            _runtimeRiftShape[i] = new Vector2((pts[i].x - cx) / ext,
                                                (pts[i].y - cy) / ext);

        Debug.Log($"[ShopController] Rift shape loaded from sprite '{riftSprite.name}' " +
                  $"{pts.Count} vertices.");
    }

    // Rotate a 2D vector by degrees (counter-clockwise).
    static Vector2 Rotate2D(Vector2 v, float deg)
    {
        if (Mathf.Abs(deg) < 0.001f) return v;
        float rad = deg * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
        return new Vector2(cos * v.x - sin * v.y,
                           sin * v.x + cos * v.y);
    }

    // Ray-casting point-in-polygon (even-odd rule).
    static bool PointInPolygon(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        int  n      = poly.Length;
        int  j      = n - 1;
        for (int i = 0; i < n; j = i++)
        {
            if (((poly[i].y > p.y) != (poly[j].y > p.y))
                && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y)
                          / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
        }
        return inside;
    }
}
