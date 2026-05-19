using System.Collections.Generic;
using UnityEngine;

// ── ShopController ────────────────────────────────────────────────────────────
//
// Renders the shop as a dimensional rift — a crack in screen-space through
// which floating 3D blocks are visible.  Powered by RenderTexture + GL mesh.
//
// Unity setup:
//   1. Create empty GameObject "ShopController", attach this script.
//   2. Create a Camera "ShopCamera":
//        Clear Flags  → Solid Color  (set shopBackground in Inspector)
//        Depth        → 1
//        Target Texture → leave blank (assigned at runtime)
//   3. Drag ShopCamera into shopCam field.
//
// F (configurable) — open / close the rift.
// Auto-closes during combat (Running phase).
// ─────────────────────────────────────────────────────────────────────────────
public class ShopController : MonoBehaviour
{
    public static ShopController Instance;

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Camera")]
    public Camera  shopCam;
    public Vector3 cameraOffsetSmall = new Vector3(0f,  4f,  -8f);
    public Vector3 cameraOffsetLarge = new Vector3(0f,  6f, -14f);

    [Header("Rift Shape & Position")]
    [Tooltip("Rift centre in normalised screen space (0=left/top, 1=right/bottom).")]
    public Vector2 riftScreenPos  = new Vector2(0.14f, 0.50f);
    [Tooltip("Rift height as a fraction of screen height at full open scale.")]
    public float   riftHeight     = 0.46f;
    [Tooltip("Scale while collapsed — the always-visible crack hint.")]
    public float   riftHintScale  = 0.22f;
    [Tooltip("Open/close speed.")]
    public float   expandSpeed    = 7f;

    [Header("Toggle Key")]
    public KeyCode shopToggleKey = KeyCode.F;

    [Header("Shop World Area")]
    public Vector3 shopCenter   = new Vector3(-25f, 4f, 5f);
    public float   blockSpacing = 2.8f;

    [Header("Float Animation")]
    public float bobAmplitude = 0.35f;
    public float bobSpeed     = 1.0f;
    public float rotateSpeed  = 28f;

    [Header("Rift Atmosphere")]
    public Color shopBackground     = new Color(0.28f, 0.14f, 0.01f, 1f);
    public Color shopLightColor     = new Color(1.00f, 0.82f, 0.25f, 1f);
    public float shopLightIntensity = 3.5f;
    public float shopLightRange     = 18f;

    [Header("Rift Sprite & Orientation")]
    [Tooltip("Optional sprite that defines the rift silhouette. Enable 'Generate Physics Shape' in its import settings.")]
    public Sprite riftSprite;
    [Tooltip("Rotate the rift opening on-screen. 0 = vertical, 90 = horizontal. The shop camera rolls to compensate.")]
    [Range(-180f, 180f)] public float riftRotationDeg = 0f;
    [Tooltip("RenderTexture width.  512 = portrait (default).  1024 = landscape (pair with riftRotationDeg = 90).")]
    public int rtWidth  = 512;
    [Tooltip("RenderTexture height. 1024 = portrait (default). 512  = landscape.")]
    public int rtHeight = 1024;

    [Header("Rift Edge FX")]
    public Color riftEdgeColor  = new Color(1.00f, 0.80f, 0.18f, 1.00f);
    public Color riftGlowColor  = new Color(1.00f, 0.55f, 0.05f, 0.10f);
    public int   riftGlowLayers = 4;
    [Range(0f, 5f)] public float edgePulseSpeed = 1.6f;
    public int   flowDotCount   = 22;
    [Range(0f, 2f)] public float flowSpeed      = 0.30f;
    public float spikeLength    = 16f;
    [Range(0f, 8f)] public float spikeSpeed     = 3.0f;

    [Header("Tooltip")]
    public Color tooltipBg = new Color(0.04f, 0.04f, 0.08f, 0.90f);

    // ── Rift polygon ──────────────────────────────────────────────────────────
    // Built-in fallback shape: 12-vertex vertical convex lens.
    // At runtime, _runtimeRiftShape is used instead (built from riftSprite or this default).
    static readonly Vector2[] RiftShapeDefault =
    {
        new( 0.00f,  1.00f),  //  0 top apex
        new( 0.15f,  0.70f),  //  1
        new( 0.30f,  0.35f),  //  2
        new( 0.38f,  0.00f),  //  3 widest right
        new( 0.30f, -0.35f),  //  4
        new( 0.15f, -0.70f),  //  5
        new( 0.00f, -1.00f),  //  6 bottom apex
        new(-0.15f, -0.70f),  //  7
        new(-0.30f, -0.35f),  //  8
        new(-0.38f,  0.00f),  //  9 widest left
        new(-0.30f,  0.35f),  // 10
        new(-0.15f,  0.70f),  // 11
    };

    // ── State ─────────────────────────────────────────────────────────────────

    class ShopItem
    {
        public GameObject      root;
        public SelectableBlock sb;
        public float           baseY;
        public float           phase;
    }

    readonly List<ShopItem> _items = new();
    ShopItem _hovered;

    bool    _expanded;
    float   _riftScale;         // current animated scale
    float   _riftTarget;        // target scale
    Vector3 _currentOffset;     // animated camera offset

    // Computed each Update — used by hover/click and GL draw
    Vector2   _riftScreenCenter;
    float     _riftScreenSize;
    Vector2[] _screenVerts;

    // Counts down after a failed purchase attempt — drives red rift edge flash.
    float _cantAffordFlash;

    // GL rendering
    RenderTexture _shopRT;
    Material      _riftMat;    // Unlit/Texture — draws RT content
    Material      _colorMat;   // Hidden/Internal-Colored — draws colored geometry

    Light _shopLight;

    // Active rift polygon in normalised coords (Y-up, centred at origin).
    // Built once from riftSprite physics shape (or RiftShapeDefault if no sprite).
    // riftRotationDeg is applied on-the-fly in UpdateScreenVerts.
    Vector2[] _runtimeRiftShape;

    GUIStyle _ttTitle, _ttPrice, _ttSub, _hintStyle;
    bool     _stylesBuilt;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        Instance       = this;
        _currentOffset = cameraOffsetSmall;

        if (shopCam != null)
        {
            shopCam.enabled         = true;
            shopCam.clearFlags      = CameraClearFlags.SolidColor;
            shopCam.backgroundColor = shopBackground;
        }

        var lightGO = new GameObject("ShopLight");
        lightGO.transform.position = shopCenter + Vector3.up * 4f;
        _shopLight = lightGO.AddComponent<Light>();
        _shopLight.type      = LightType.Point;
        _shopLight.color     = shopLightColor;
        _shopLight.intensity = shopLightIntensity;
        _shopLight.range     = shopLightRange;

        ApplyCameraTransform();
    }

    void Start()
    {
        BuildShapeFromSprite();
        RebuildRT();
    }

    void RebuildRT()
    {
        if (shopCam != null) shopCam.targetTexture = null;
        if (_shopRT != null) { _shopRT.Release(); Destroy(_shopRT); }

        _shopRT              = new RenderTexture(Mathf.Max(32, rtWidth), Mathf.Max(32, rtHeight),
                                                 16, RenderTextureFormat.ARGB32);
        _shopRT.antiAliasing = 2;
        _shopRT.filterMode   = FilterMode.Bilinear;

        if (shopCam != null)
        {
            shopCam.targetTexture = _shopRT;
            shopCam.rect          = new Rect(0, 0, 1, 1);
            // aspect auto-derived from RT dimensions — do not set manually
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
        if (_shopLight != null)
        {
            _shopLight.color     = shopLightColor;
            _shopLight.intensity = shopLightIntensity;
            _shopLight.range     = shopLightRange;
            _shopLight.transform.position = shopCenter + Vector3.up * 4f;
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
    }

    void Update()
    {
        HandleToggleKey();
        AnimateRift();
        ApplyCameraTransform();
        UpdateScreenVerts();   // must run before UpdateHover / IsMouseInShopView
        AnimateItems();
        UpdateHover();
        if (_cantAffordFlash > 0f) _cantAffordFlash -= Time.deltaTime;
    }

    // ── Toggle & visibility ───────────────────────────────────────────────────

    void HandleToggleKey()
    {
        if (!Input.GetKeyDown(shopToggleKey)) return;
        var phase = GameFlowManager.Instance?.phase;
        Debug.Log($"[Shop] {shopToggleKey} | phase={phase} | expanded={_expanded}");
        if (phase == GamePhase.Running) { Debug.Log("[Shop] blocked — combat."); return; }
        _expanded = !_expanded;
    }

    public void Collapse()      => _expanded = false;
    public void OnCombatStart() => _expanded = false;

    // ── Rift animation ────────────────────────────────────────────────────────

    void AnimateRift()
    {
        bool combat = GameFlowManager.Instance?.phase == GamePhase.Running;
        _riftTarget = combat ? 0f : (_expanded ? 1.0f : riftHintScale);

        float t    = 1f - Mathf.Exp(-expandSpeed * Time.deltaTime);
        _riftScale     = Mathf.Lerp(_riftScale, _riftTarget, t);
        _currentOffset = Vector3.Lerp(_currentOffset,
                             _expanded ? cameraOffsetLarge : cameraOffsetSmall, t);
    }

    void ApplyCameraTransform()
    {
        if (shopCam == null) return;
        shopCam.transform.position = shopCenter + _currentOffset;
        shopCam.transform.LookAt(shopCenter);
        // Counter-roll so shop items appear upright inside a rotated rift.
        if (Mathf.Abs(riftRotationDeg) > 0.01f)
            shopCam.transform.Rotate(Vector3.forward, -riftRotationDeg, Space.Self);
    }

    // Recomputes _riftScreenCenter, _riftScreenSize, _screenVerts every frame.
    void UpdateScreenVerts()
    {
        _riftScreenCenter = new Vector2(riftScreenPos.x * Screen.width,
                                        riftScreenPos.y * Screen.height);
        _riftScreenSize   = Screen.height * riftHeight * _riftScale;

        var shape = _runtimeRiftShape ?? RiftShapeDefault;
        int n     = shape.Length;
        if (_screenVerts == null || _screenVerts.Length != n)
            _screenVerts = new Vector2[n];

        for (int i = 0; i < n; i++)
        {
            // Apply rotation in normalised space, then map to screen.
            Vector2 rv = Rotate2D(shape[i], riftRotationDeg);
            _screenVerts[i] = new Vector2(_riftScreenCenter.x + rv.x * _riftScreenSize,
                                          _riftScreenCenter.y - rv.y * _riftScreenSize);
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void SpawnItems(BlockData[] datas, GameObject cubePrefab, GridSystem grid)
    {
        ClearItems();
        int n = datas.Length;
        for (int i = 0; i < n; i++)
        {
            var data = datas[i];
            if (data == null || data.cells == null) continue;

            float   offset = (i - (n - 1) * 0.5f) * blockSpacing;
            Vector3 pos    = shopCenter + Vector3.right * offset;

            var root = new GameObject($"Shop_{data.blockType}_{i}");
            root.transform.position = pos;

            Color col = Random.ColorHSV(0f, 1f, 0.55f, 0.9f, 0.75f, 1f);
            foreach (var cell in data.cells)
            {
                var c = Instantiate(cubePrefab, root.transform);
                c.transform.localPosition = (Vector3)cell * grid.cellSize;
                MpbColor.Set(c.GetComponent<Renderer>(), col);
            }

            var sb = root.AddComponent<SelectableBlock>();
            sb.data = data;

            float fluc     = Random.Range(0.82f, 1.22f);
            sb.cachedPrice = ResourceManager.Instance != null
                             ? ResourceManager.Instance.ComputePrice(data, fluc) : 0;

            _items.Add(new ShopItem
            {
                root  = root,
                sb    = sb,
                baseY = pos.y,
                phase = i * (Mathf.PI * 2f / Mathf.Max(n, 1)),
            });
        }
    }

    public void ClearItems()
    {
        foreach (var item in _items) if (item.root != null) Destroy(item.root);
        _items.Clear();
        _hovered = null;
    }

    /// <summary>Immediate removal — use for programmatic cleanup (ClearItems, etc.).</summary>
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

            // 0→0.2: pop up to 1.25×   |   0.2→1: shrink to 0
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

        // Affordable check — block the grab if the player can't pay.
        var rm = ResourceManager.Instance;
        if (rm != null && !rm.CanAfford(_hovered.sb.cachedPrice))
        {
            _cantAffordFlash = 0.55f;   // trigger red rift-edge flash
            return true;                // consume click, stay in shop
        }

        // Hide the item while held — RestoreItem re-shows it on cancel.
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
        // Input.mousePosition has y=0 at bottom; GUI space has y=0 at top.
        Vector2 mp = new Vector2(Input.mousePosition.x,
                                 Screen.height - Input.mousePosition.y);
        return PointInPolygon(mp, _screenVerts);
    }

    // ── Animations ────────────────────────────────────────────────────────────

    void AnimateItems()
    {
        float t = Time.time;
        foreach (var item in _items)
        {
            if (item.root == null) continue;
            float y = item.baseY + Mathf.Sin(t * bobSpeed + item.phase) * bobAmplitude;
            var   p = item.root.transform.position;
            item.root.transform.position = new Vector3(p.x, y, p.z);
            item.root.transform.Rotate(Vector3.up, rotateSpeed * Time.deltaTime, Space.Self);
        }
    }

    void UpdateHover()
    {
        _hovered = null;
        if (shopCam == null || !IsMouseInShopView()) return;

        Vector3 vp  = ScreenToShopViewport(Input.mousePosition);
        Ray     ray = shopCam.ViewportPointToRay(vp);
        if (!Physics.Raycast(ray, out RaycastHit hit)) return;

        var sb = hit.transform.GetComponentInParent<SelectableBlock>();
        if (sb == null) return;
        foreach (var item in _items)
            if (item.sb == sb) { _hovered = item; return; }
    }

    // Input.mousePosition (y=0 bottom) → shop camera viewport (0..1, y=0 bottom).
    Vector3 ScreenToShopViewport(Vector2 mp)
    {
        // 1. Offset from rift centre in normalised rift-screen units (still in rotated space).
        float guiY = Screen.height - mp.y;
        float rx   = (mp.x               - _riftScreenCenter.x) / (_riftScreenSize + 0.001f);
        float ry   = (_riftScreenCenter.y - guiY)                / (_riftScreenSize + 0.001f);

        // 2. Undo the on-screen rotation to get into the shape's native (unrotated) space.
        //    Must mirror the Rotate2D applied in UpdateScreenVerts.
        Vector2 local = Rotate2D(new Vector2(rx, ry), riftRotationDeg);

        // 3. Map to camera viewport using the same shape bounds as DrawContent.
        //    ViewportPointToRay uses y=0 at bottom, y=1 at top — no D3D flip here.
        var   shape = _runtimeRiftShape ?? RiftShapeDefault;
        float minX  = float.MaxValue, maxX = float.MinValue;
        float minY  = float.MaxValue, maxY = float.MinValue;
        foreach (var p in shape)
        {
            if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
        }
        // No U flip: camera.right = world +X, viewport X maps directly to shape X.
        return new Vector3(
            Mathf.Clamp01((local.x - minX) / Mathf.Max(maxX - minX, 0.001f)),
            Mathf.Clamp01((local.y - minY) / Mathf.Max(maxY - minY, 0.001f)),
            0f);
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
                displayName = item.sb.data.DisplayName,
                price       = price,
                affordable  = rm != null && rm.CanAfford(price),
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
        BuildStyles();
        if (_riftScale > 0.005f) DrawRift();
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
    }

    // ── GL rift rendering ─────────────────────────────────────────────────────

    void DrawRift()
    {
        if (_shopRT == null || _screenVerts == null) return;
        if (Event.current.type != EventType.Repaint) return;

        EnsureMaterials();
        GL.PushMatrix();
        GL.LoadPixelMatrix();

        DrawGlow();
        DrawContent();
        DrawEdge();
        DrawFlowDots();
        DrawEnergySpikes();

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
        var shape = _runtimeRiftShape ?? RiftShapeDefault;
        int n     = shape.Length;

        // Compute shape bounding box for UV mapping (works for any shape / aspect).
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var p in shape)
        {
            if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
        }
        float rx = Mathf.Max(maxX - minX, 0.001f);
        float ry = Mathf.Max(maxY - minY, 0.001f);

        // UV helper: shape normalised → RT UV.
        // Camera LookAt uses cross(worldUp, forward) → camera.right = world +X,
        // so no U flip needed: left shape → left RT → left items.
        // V is flipped for D3D/URP (V=0 at top of texture).
        System.Func<Vector2, Vector2> toUV =
            p => new Vector2((p.x - minX) / rx, (maxY - p.y) / ry);

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

    // Small cross-shaped dots flowing around the rift perimeter.
    void DrawFlowDots()
    {
        int n = _screenVerts.Length;

        float[] segs  = new float[n];
        float   total = 0f;
        for (int i = 0; i < n; i++)
        {
            segs[i] = Vector2.Distance(_screenVerts[i], _screenVerts[(i + 1) % n]);
            total  += segs[i];
        }
        if (total < 1f) return;

        float t = Time.time;
        _colorMat.SetPass(0);
        GL.Begin(GL.LINES);
        for (int d = 0; d < flowDotCount; d++)
        {
            float   phase = ((t * flowSpeed + (float)d / flowDotCount) % 1f + 1f) % 1f;
            Vector2 pos   = PointOnPerimeter(segs, total, phase);
            float   alpha = 0.55f + 0.45f * Mathf.Sin(t * 2.5f + d * 0.63f);
            float   r     = 2.2f * Mathf.Clamp01(_riftScale);
            GL.Color(new Color(1f, 0.96f, 0.55f, alpha));
            GL.Vertex3(pos.x - r, pos.y,     0); GL.Vertex3(pos.x + r, pos.y,     0);
            GL.Vertex3(pos.x,     pos.y - r, 0); GL.Vertex3(pos.x,     pos.y + r, 0);
        }
        GL.End();
    }

    // Short animated spikes at each vertex, pointing outward.
    void DrawEnergySpikes()
    {
        int n = _screenVerts.Length;
        _colorMat.SetPass(0);
        GL.Begin(GL.LINES);
        for (int i = 0; i < n; i++)
        {
            Vector2 outDir = OutwardNormal(i);
            float   phase  = Mathf.Sin(Time.time * spikeSpeed + i * 1.1f);
            float   len    = spikeLength * _riftScale * (0.4f + 0.6f * Mathf.Abs(phase));
            Vector2 tip    = _screenVerts[i] + outDir * len;
            float   alpha  = 0.35f + 0.65f * (0.5f + 0.5f * phase);
            GL.Color(new Color(riftEdgeColor.r, riftEdgeColor.g, riftEdgeColor.b, alpha));
            GL.Vertex3(_screenVerts[i].x, _screenVerts[i].y, 0);
            GL.Vertex3(tip.x, tip.y, 0);
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
        bool      afford  = rm.CanAfford(price);
        int       placed  = rm.PlacedCount(type);
        int       deficit = price - rm.BlockCurrency;

        var   mp = Input.mousePosition;
        float tx = Mathf.Clamp(mp.x + 16f, 4f, Screen.width  - 148f);
        float ty = Mathf.Clamp(Screen.height - mp.y - 96f, 4f, Screen.height - 96f);
        const float bw = 140f, bh = 84f;

        GUI.color = tooltipBg;
        GUI.DrawTexture(new Rect(tx - 8f, ty - 8f, bw, bh), Texture2D.whiteTexture);
        GUI.color = afford ? new Color(0.35f, 1.00f, 0.50f, 0.85f)
                           : new Color(1.00f, 0.30f, 0.30f, 0.85f);
        GUI.DrawTexture(new Rect(tx - 8f, ty - 8f, bw, 2f), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUI.Label(new Rect(tx, ty,        124f, 20f), item.sb.data.DisplayName, _ttTitle);

        _ttPrice.normal.textColor = afford ? new Color(0.45f, 1f, 0.55f)
                                           : new Color(1f, 0.38f, 0.38f);
        string priceText = afford ? $"{price} ¤" : $"{price} ¤  (need {deficit} more)";
        GUI.Label(new Rect(tx, ty + 22f, 124f, 20f), priceText, _ttPrice);

        _ttSub.normal.textColor = new Color(0.65f, 0.65f, 0.65f);
        GUI.Label(new Rect(tx, ty + 44f, 124f, 18f), $"×{placed} on grid", _ttSub);

        string hint = afford ? "Click to pick up" : "Not enough ¤";
        _ttSub.normal.textColor = afford ? new Color(0.65f, 0.65f, 0.65f)
                                         : new Color(1f, 0.45f, 0.45f);
        GUI.Label(new Rect(tx, ty + 62f, 124f, 18f), hint, _ttSub);
    }

    // ── Style builder ─────────────────────────────────────────────────────────

    void BuildStyles()
    {
        if (_stylesBuilt) return;
        _stylesBuilt = true;

        _ttTitle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
        _ttTitle.normal.textColor = Color.white;

        _ttPrice = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
        _ttPrice.normal.textColor = Color.green;

        _ttSub = new GUIStyle(GUI.skin.label) { fontSize = 10 };
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

    /// <summary>
    /// Populates _runtimeRiftShape from riftSprite's physics outline, or falls
    /// back to RiftShapeDefault.  Call on Start and whenever riftSprite changes.
    ///
    /// Sprite requirements: in Import Settings enable
    ///   Sprite Editor → Physics Shape → Generate  (or draw manually).
    /// </summary>
    void BuildShapeFromSprite()
    {
        if (riftSprite == null)
        {
            _runtimeRiftShape = null;   // UpdateScreenVerts falls back to RiftShapeDefault
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
                  $"— {pts.Count} vertices.");
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
