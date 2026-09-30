using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The shop: what is on offer this round, and buying it.
//
// The LOOK lives in the scene — a ShopPanelView (the ShopUI prefab, placed with
// GeoWorld ▸ UI ▸ Place Gameplay HUD): a strip at the bottom-right with a block row
// and a turret row, slot templates, refresh / open buttons and a tooltip card. Lay
// it out in the editor; this only fills it in and opens / closes it.
//
// Items are shown as photographs of the real piece (ShopThumbnail) — taken once
// from a fixed angle, so nothing turns, drifts or stretches. Hover and click are
// tested against the slots' rects with the virtual cursor, so they work the same
// with a mouse, a gamepad, and with the game paused (no physics involved — the old
// 3D shop's colliders weren't synced while paused, so a refresh during a pause left
// new items unclickable until the game resumed).
public class ShopController : MonoBehaviour
{
    public static ShopController Instance;

    [Header("Layout (scene)")]
    [Tooltip("The authored shop layout. Found in the scene if left empty.")]
    public ShopPanelView view;

    [Header("Toggle")]
    public KeyCode shopToggleKey = KeyCode.F;
    [Tooltip("Open/close speed.")]
    public float expandSpeed = 9f;

    [Header("Icons")]
    [Tooltip("Camera turn round the piece for the icon photograph (degrees). 45 = from the front-right corner, -45 = front-left.")]
    public float iconYaw = 45f;
    [Tooltip("Camera height angle for the icon photograph (degrees down).")]
    [Range(0f, 90f)] public float iconPitch = 35f;
    [Tooltip("Frame padding round a block (1 = touching the edges).")]
    public float blockIconPadding  = 1.12f;
    [Tooltip("Frame padding round a turret — smaller = the turret fills more of its slot.")]
    public float turretIconPadding = 1.02f;
    [Tooltip("Shapes photographed from the other side (turned 180° round Y) — the ones whose telling side faces away at the usual angle.")]
    public BlockShape[] flipIconShapes = { BlockShape.Corner3D };

    [Header("Item hover")]
    [Tooltip("How much a hovered item grows. The icon's size in the layout is its LARGEST — at rest it sits this much smaller — so a hovered or tutorial-highlighted item never spills out of its slot.")]
    [Range(1f, 1.5f)] public float hoverScale     = 1.2f;
    [Range(1f, 20f)]  public float hoverLerpSpeed = 12f;

    [Header("Button icons (used by GeoWorld ▸ UI ▸ Place Gameplay HUD)")]
    public Sprite refreshIcon;
    public Sprite shopButtonIcon;

    [Header("Tutorial purchase highlight")]
    public Color tutorialHighlightColor = new Color(1f, 0.85f, 0.3f);
    public float tutorialHighlightPulseSpeed = 3f;
    [Range(0f, 1f)] public float tutorialDimAmount = 0.55f;
    public float tutorialHighlightScale = 1.15f;
    public float tutorialDimScale = 0.85f;

    // ── State ─────────────────────────────────────────────────────────────────

    class ShopItem
    {
        public GameObject      root;    // data holder (SelectableBlock) — inactive while held in hand
        public SelectableBlock sb;
        public ShopItemView    view;
        public bool            isTurret;
    }

    readonly List<ShopItem> _items = new();
    ShopItem _hovered;

    bool    _expanded;
    bool    _prevExpanded;
    float   _openT;               // 0 closed … 1 open (animated)
    Vector2 _panelHome;           // the panel's authored (open) position
    bool    _homeKnown;
    float   _cantAffordFlash;
    Color   _flashBase;
    bool    _warned;

    public bool  ShopVisible  => _openT > 0.1f;
    public bool  IsExpanded   => _expanded;
    /// <summary>Kept for callers laid out round the old letterbox bars — there are none now.</summary>
    public float TopBarHeight => 0f;

    /// <summary>Top-centre of the shop panel in GUI coordinates (y down).</summary>
    public Vector2 ShopTopCenter
    {
        get
        {
            if (view == null || view.panel == null) return new Vector2(Screen.width * 0.5f, Screen.height);
            var c = new Vector3[4];
            view.panel.GetWorldCorners(c);   // overlay canvas: world = screen pixels
            var top = (c[1] + c[2]) * 0.5f;
            return new Vector2(top.x, Screen.height - top.y);
        }
    }

    /// <summary>
    /// Screen-pixel rect (y up) the open shop strip covers, refresh tab included.
    /// False while it is closed.
    /// </summary>
    public bool TryGetScreenRect(out Rect r)
    {
        r = default;
        if (view == null || view.panel == null || _openT < 0.1f) return false;
        var c = new Vector3[4];
        view.panel.GetWorldCorners(c);   // overlay canvas: world = screen pixels
        float x0 = c[0].x, y0 = c[0].y, x1 = c[2].x, y1 = c[2].y;
        if (view.refreshButton != null)
        {
            ((RectTransform)view.refreshButton.transform).GetWorldCorners(c);
            x0 = Mathf.Min(x0, c[0].x); y0 = Mathf.Min(y0, c[0].y);
            x1 = Mathf.Max(x1, c[2].x); y1 = Mathf.Max(y1, c[2].y);
        }
        r = Rect.MinMaxRect(x0, y0, x1, y1);
        return true;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake() => Instance = this;

    void Start()
    {
        BindView();
    }

    bool BindView()
    {
        if (view == null) view = FindFirstObjectByType<ShopPanelView>(FindObjectsInactive.Include);
        if (view == null)
        {
            if (!_warned)
            {
                _warned = true;
                Debug.LogWarning("[Shop] No ShopPanelView in the scene — run GeoWorld ▸ UI ▸ Place Gameplay HUD and save the scene.");
            }
            return false;
        }
        if (!_homeKnown && view.panel != null)
        {
            _panelHome = view.panel.anchoredPosition;
            _homeKnown = true;
            if (view.blockTemplate  != null) view.blockTemplate.gameObject.SetActive(false);
            if (view.turretTemplate != null) view.turretTemplate.gameObject.SetActive(false);
            if (view.tooltip != null) view.tooltip.gameObject.SetActive(false);
            if (view.flashTarget != null) _flashBase = view.flashTarget.color;
            if (view.refreshButton != null)
                view.refreshButton.onClick.AddListener(() => PlacementController.Instance?.TryRefreshShop());
            if (view.openButton != null)
                view.openButton.onClick.AddListener(() => { if (!GameFlowManager.SettlementUp) _expanded = true; });
        }
        return true;
    }

    void Update()
    {
        if (view == null && !BindView()) return;

        HandleToggleKey();
        Animate();
        UpdateHover();
        UpdateItems();
        UpdateTooltip();
        UpdateChrome();
        if (_cantAffordFlash > 0f) _cantAffordFlash -= Time.unscaledDeltaTime;
    }

    // ── Toggle & visibility ───────────────────────────────────────────────────

    void HandleToggleKey()
    {
        if (GameFlowManager.SettlementUp) { _expanded = false; return; }   // locked during clear settlement
        if (!Input.GetKeyDown(shopToggleKey) && !GamepadInput.ToggleShopDown) return;
        // Read the VISIBLE state once the animation has settled (the flag can go
        // stale — grab, Collapse, RestoreItem and the combat hooks all write it);
        // mid-animation trust the flag, or a quick double press refuses to close.
        bool settled     = Mathf.Abs(_openT - (_expanded ? 1f : 0f)) < 0.05f;
        bool visiblyOpen = settled ? _openT > 0.5f : _expanded;
        _expanded = !visiblyOpen;   // openable during combat too (buy / place new pieces)
    }

    public void Collapse()      => _expanded = false;
    public void OnCombatStart() => _expanded = false;

    void Animate()
    {
        if (_expanded != _prevExpanded)
        {
            AudioManager.Instance?.PlayShopToggle(_expanded);
            _prevExpanded = _expanded;
        }
        float k = 1f - Mathf.Exp(-expandSpeed * Time.unscaledDeltaTime);
        _openT = Mathf.Lerp(_openT, _expanded ? 1f : 0f, k);
        if (Mathf.Abs(_openT - (_expanded ? 1f : 0f)) < 0.002f) _openT = _expanded ? 1f : 0f;

        if (view.panel != null)
        {
            float drop = view.panel.rect.height + view.closedDrop;
            view.panel.anchoredPosition = _panelHome + Vector2.down * (drop * (1f - _openT));
        }
        if (view.panelGroup != null)
        {
            view.panelGroup.alpha          = Mathf.Clamp01(_openT * 1.4f);
            view.panelGroup.blocksRaycasts = _openT > 0.5f;
            view.panelGroup.interactable   = _openT > 0.5f;
        }
    }

    void UpdateChrome()
    {
        bool hidden = SettingsScreen.Open || IntroDirector.Playing || GameFlowManager.SettlementUp;
        if (view.canvas != null) view.canvas.enabled = !hidden;
        if (hidden) return;

        if (view.openButton != null)
            view.openButton.gameObject.SetActive(_openT < 0.5f);

        if (view.refreshCost != null && PlacementController.Instance != null)
            view.refreshCost.text = PlacementController.Instance.RefreshCost.ToString();

        if (view.hintLabel != null)
        {
            bool running = GameFlowManager.Instance?.phase == GamePhase.Running;
            view.hintLabel.gameObject.SetActive(!running);
            view.hintLabel.text = _expanded ? $"SHOP  [{shopToggleKey}]" : $"[{shopToggleKey}]";
        }

        if (view.flashTarget != null)
        {
            float f = Mathf.Clamp01(_cantAffordFlash / 0.55f);
            view.flashTarget.color = Color.Lerp(_flashBase, view.flashColor, f);
        }
    }

    // ── Items ─────────────────────────────────────────────────────────────────

    // Blocks in the block row, turrets in the turret row, in roll order.
    public void SetShopItems(BlockData[] blockDatas, BlockData[] turretDatas,
                             BlockColor[] blockColors, BlockColor[] turretColors,
                             GameObject cubePrefab, GridSystem grid)
    {
        ClearItems();
        if (view == null) BindView();

        int blockN  = blockDatas  != null ? blockDatas.Length  : 0;
        int turretN = turretDatas != null ? turretDatas.Length : 0;
        int idx = 0;
        for (int i = 0; i < blockN; i++)
        {
            var sCol = (blockColors != null && i < blockColors.Length) ? blockColors[i] : BlockColor.None;
            SpawnOne(blockDatas[i], sCol, cubePrefab, grid, isTurret: false, idx++);
        }
        for (int i = 0; i < turretN; i++)
        {
            var sCol = (turretColors != null && i < turretColors.Length) ? turretColors[i] : BlockColor.None;
            SpawnOne(turretDatas[i], sCol, cubePrefab, grid, isTurret: true, idx++);
        }
    }

    void SpawnOne(BlockData data, BlockColor synergyColor, GameObject cubePrefab, GridSystem grid,
                  bool isTurret, int globalIndex)
    {
        if (data == null) return;

        // Tint: the synergy colour when it has one; otherwise the type palette.
        Color col;
        if (isTurret)                              col = TurretTypes.DisplayColor(data.blockType);
        else if (synergyColor != BlockColor.None)  col = BlockColorPalette.Get(synergyColor);
        else col = PlacementController.Instance != null
                 ? PlacementController.Instance.PickPaletteColor(data.blockType)
                 : new Color(0.85f, 0.18f, 0.12f);

        // Data holder — what PlacementController takes in hand.
        var root = new GameObject($"Shop_{data.blockType}_{globalIndex}");
        root.transform.SetParent(transform, false);
        var sb = root.AddComponent<SelectableBlock>();
        sb.data         = data;
        sb.color        = synergyColor;
        sb.displayColor = col;
        float fluc      = Random.Range(0.82f, 1.22f);
        sb.cachedPrice  = ResourceManager.Instance != null ? ResourceManager.Instance.ComputePrice(data, fluc) : 0;

        // Slot.
        ShopItemView v = null;
        var template = isTurret ? view?.turretTemplate : view?.blockTemplate;
        var parent   = isTurret ? view?.turretRow      : view?.blockRow;
        if (template != null && parent != null)
        {
            v = Instantiate(template, parent);
            v.name = root.name;
            v.gameObject.SetActive(true);
            float cs = grid != null ? grid.cellSize : 1f;
            var sprite = isTurret
                ? ShopThumbnail.Turret(data, cs, iconYaw, iconPitch, turretIconPadding)
                : ShopThumbnail.Block(data, cubePrefab, col, cs,
                                      iconYaw + (System.Array.IndexOf(flipIconShapes, data.blockShape) >= 0 ? 180f : 0f),
                                      iconPitch, blockIconPadding);
            if (v.icon != null)
            {
                v.icon.sprite = sprite;
                v.icon.preserveAspect = true;
                v.icon.color = Color.white;
                v.icon.enabled = sprite != null;
            }
            if (v.scaleRoot != null)   // start at rest size (see UpdateItems)
                v.scaleRoot.localScale = Vector3.one / Mathf.Max(1f, Mathf.Max(hoverScale, tutorialHighlightScale));
        }

        _items.Add(new ShopItem { root = root, sb = sb, view = v, isTurret = isTurret });
    }

    void UpdateItems()
    {
        var rm = ResourceManager.Instance;
        float t = Time.unscaledTime;
        float lerpK = 1f - Mathf.Exp(-hoverLerpSpeed * Time.unscaledDeltaTime);
        bool gate = TutorialDirector.IsPurchaseStepActive;

        foreach (var item in _items)
        {
            var v = item.view;
            if (v == null) continue;

            // Held in hand: the slot stays (so the row doesn't jump) but shows empty.
            bool held = item.root == null || !item.root.activeSelf;
            if (v.group != null) v.group.alpha = held ? 0.15f : 1f;

            bool isTarget = gate && TutorialDirector.IsPurchaseTarget(item.sb.data);
            if (v.icon != null)
            {
                Color tint = Color.white;
                if (gate)
                    tint = isTarget
                        ? Color.Lerp(Color.white, tutorialHighlightColor, 0.5f + 0.5f * Mathf.Sin(t * tutorialHighlightPulseSpeed))
                        : Color.Lerp(Color.white, Color.black, tutorialDimAmount);
                v.icon.color = tint;
            }

            if (v.price != null && item.sb != null)
            {
                int  price  = item.sb.cachedPrice;
                bool afford = rm == null || rm.CanAfford(price, item.sb.data.blockType);
                string sfx  = item.isTurret ? "T" : "B";
                v.price.text  = $"{price}¤{sfx}";
                v.price.color = afford ? v.affordableColor : v.unaffordableColor;
            }

            if (v.background != null)
                v.background.color = item == _hovered ? v.backgroundHoverColor : v.backgroundColor;

            if (v.scaleRoot != null)
            {
                // Relative to the largest the icon ever gets, which is its laid-out size.
                float peak   = Mathf.Max(1f, Mathf.Max(hoverScale, tutorialHighlightScale));
                float target = item == _hovered ? hoverScale : 1f;
                if (gate) target *= isTarget ? tutorialHighlightScale : tutorialDimScale;
                target = Mathf.Min(target, peak) / peak;
                float next = Mathf.Lerp(v.scaleRoot.localScale.x, target, lerpK);
                v.scaleRoot.localScale = Vector3.one * next;
            }
        }
    }

    void UpdateHover()
    {
        _hovered = null;
        if (!IsMouseInShopView()) return;
        Vector2 p = VirtualCursor.Position;
        foreach (var item in _items)
        {
            if (item.view == null || item.view.rect == null) continue;
            if (item.root == null || !item.root.activeSelf) continue;
            if (RectTransformUtility.RectangleContainsScreenPoint(item.view.rect, p, UICamera()))
            {
                _hovered = item;
                return;
            }
        }
    }

    Camera UICamera() =>
        view != null && view.canvas != null && view.canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? view.canvas.worldCamera : null;

    // ── Tooltip ───────────────────────────────────────────────────────────────

    void UpdateTooltip()
    {
        if (view.tooltip == null) return;
        bool show = _hovered != null && _openT > 0.5f && _hovered.view != null;
        if (view.tooltip.gameObject.activeSelf != show) view.tooltip.gameObject.SetActive(show);
        if (!show) return;

        var rm = ResourceManager.Instance;
        var data = _hovered.sb.data;
        BlockType type = data.blockType;
        int  price  = _hovered.sb.cachedPrice;
        bool afford = rm == null || rm.CanAfford(price, type);
        int  pool   = rm == null ? 0 : (TurretTypes.Is(type) ? rm.TurretCurrency : rm.BlockCurrency);

        string shape = TurretTypes.Is(type) ? TurretTypes.DisplayName(type) : data.ShapeName;
        if (string.IsNullOrEmpty(shape)) shape = "Block";
        bool hasTheme = _hovered.sb.color != BlockColor.None;
        if (view.tooltipTitle != null)
        {
            view.tooltipTitle.richText = true;
            view.tooltipTitle.text = hasTheme
                ? $"{shape}  ·  <color=#{ColorUtility.ToHtmlStringRGB(BlockColorPalette.Get(_hovered.sb.color))}>{_hovered.sb.color}</color>"
                : shape;
        }
        if (view.tooltipPrice != null)
        {
            string sfx = TurretTypes.Is(type) ? " T" : " B";
            view.tooltipPrice.text  = afford ? $"{price}{sfx}" : $"{price}{sfx}  (-{price - pool})";
            view.tooltipPrice.color = afford ? GeoPalette.Blue : GeoPalette.Signal;
        }
        if (view.tooltipAccent != null) view.tooltipAccent.color = afford ? GeoPalette.Blue : GeoPalette.Signal;
        if (view.tooltipDesc != null)
        {
            string desc = hasTheme ? BlockColorPalette.Description(_hovered.sb.color) : null;
            view.tooltipDesc.gameObject.SetActive(!string.IsNullOrEmpty(desc));
            if (!string.IsNullOrEmpty(desc)) view.tooltipDesc.text = desc;
        }

        // Above the hovered slot, kept on screen.
        var c = new Vector3[4];
        _hovered.view.rect.GetWorldCorners(c);
        view.tooltip.position = (c[1] + c[2]) * 0.5f;
        view.tooltip.GetWorldCorners(c);
        float dx = 0f, dy = 0f;
        if (c[0].x < 4f)                 dx = 4f - c[0].x;
        if (c[2].x > Screen.width - 4f)  dx = Screen.width - 4f - c[2].x;
        if (c[2].y > Screen.height - 4f) dy = Screen.height - 4f - c[2].y;
        view.tooltip.position += new Vector3(dx, dy, 0f);
    }

    // ── Buying ────────────────────────────────────────────────────────────────

    /// <summary>Pointer is over the open shop strip (or the open button while closed).</summary>
    public bool IsMouseInShopView()
    {
        if (view == null) return false;
        if (SettingsScreen.Open || IntroDirector.Playing || GameFlowManager.SettlementUp) return false;
        Vector2 p = VirtualCursor.Position;
        if (_openT > 0.5f && view.panel != null &&
            RectTransformUtility.RectangleContainsScreenPoint(view.panel, p, UICamera())) return true;
        // The refresh tab sticks up out of the strip, past its rect.
        if (_openT > 0.5f && view.refreshButton != null &&
            RectTransformUtility.RectangleContainsScreenPoint((RectTransform)view.refreshButton.transform, p, UICamera())) return true;
        if (_openT < 0.5f && view.openButton != null && view.openButton.gameObject.activeInHierarchy &&
            RectTransformUtility.RectangleContainsScreenPoint((RectTransform)view.openButton.transform, p, UICamera())) return true;
        return false;
    }

    public bool TryHandleClick()
    {
        if (!IsMouseInShopView()) return false;
        if (_hovered == null) return true;   // over the strip: swallow (buttons handle themselves)

        // Something already in hand — don't swap it out from under the player.
        var pc = PlacementController.Instance;
        if (pc != null && pc.mode == PlacementMode.Edit && pc.currentBlock != null) return true;

        var rm = ResourceManager.Instance;
        if (rm != null && !rm.CanAfford(_hovered.sb.cachedPrice, _hovered.sb.data.blockType))
        {
            _cantAffordFlash = 0.55f;
            return true;
        }
        if (!TutorialDirector.CanPurchase(_hovered.sb.data))
        {
            _cantAffordFlash = 0.55f;
            return true;
        }

        // Hidden while held — RestoreItem shows it again on cancel.
        _hovered.root.SetActive(false);
        PlacementController.Instance?.GrabFromShop(_hovered.sb);
        Collapse();
        return true;
    }

    /// <summary>Placement cancelled — the item goes back on the shelf and the shop reopens.</summary>
    public void RestoreItem(GameObject go)
    {
        if (go == null || !_items.Exists(i => i.root == go)) return;   // not one of ours
        go.SetActive(true);
        _expanded = true;
    }

    /// <summary>
    /// A random item the player can currently afford, or null when none is. Uses
    /// UnityEngine.Random deliberately, not the run's seeded stream — this is a
    /// convenience input, not part of the run.
    /// </summary>
    public SelectableBlock RandomAffordable()
    {
        var rm = ResourceManager.Instance;
        var pool = new List<SelectableBlock>();
        foreach (var it in _items)
        {
            if (it?.sb == null || it.sb.data == null) continue;
            if (it.root == null || !it.root.activeInHierarchy) continue;
            if (rm != null && !rm.CanAfford(it.sb.cachedPrice, it.sb.data.blockType)) continue;
            if (!TutorialDirector.CanPurchase(it.sb.data)) continue;
            pool.Add(it.sb);
        }
        if (pool.Count == 0) return null;
        return pool[Random.Range(0, pool.Count)];
    }

    public bool HasItems => _items.Count > 0;

    // ── Mid-level save (GameFlowManager.RunSave) ─────────────────────────────
    public List<ShopItemSave> CaptureItems()
    {
        var list = new List<ShopItemSave>();
        foreach (var it in _items)
        {
            if (it?.sb == null || it.sb.data == null) continue;
            list.Add(new ShopItemSave
            {
                blockAssetName = it.sb.data.name,
                color          = it.sb.color,
                price          = it.sb.cachedPrice,
                isTurret       = TurretTypes.Is(it.sb.data.blockType),
            });
        }
        return list;
    }

    public void RestoreItems(List<ShopItemSave> saved, System.Func<string, BlockData> resolve,
                             GameObject cubePrefab, GridSystem grid)
    {
        var blocks  = new List<BlockData>(); var blockCols  = new List<BlockColor>();
        var turrets = new List<BlockData>(); var turretCols = new List<BlockColor>();
        var bPrices = new List<int>();       var tPrices    = new List<int>();
        if (saved != null)
            foreach (var s in saved)
            {
                var data = s != null ? resolve(s.blockAssetName) : null;
                if (data == null) continue;
                if (s.isTurret) { turrets.Add(data); turretCols.Add(s.color); tPrices.Add(s.price); }
                else            { blocks.Add(data);  blockCols.Add(s.color);  bPrices.Add(s.price); }
            }

        SetShopItems(blocks.ToArray(), turrets.ToArray(), blockCols.ToArray(), turretCols.ToArray(), cubePrefab, grid);

        var prices = new List<int>(bPrices); prices.AddRange(tPrices);
        for (int i = 0; i < _items.Count && i < prices.Count; i++)
            if (_items[i]?.sb != null) _items[i].sb.cachedPrice = prices[i];
    }

    public void ClearItems()
    {
        foreach (var item in _items)
        {
            if (item.root != null) Destroy(item.root);
            if (item.view != null) Destroy(item.view.gameObject);
        }
        _items.Clear();
        _hovered = null;
    }

    /// <summary>Immediate removal — for programmatic cleanup.</summary>
    public void RemoveItem(GameObject go)
    {
        foreach (var it in _items)
            if (it.root == go && it.view != null) Destroy(it.view.gameObject);
        _items.RemoveAll(item => item.root == go);
    }

    /// <summary>
    /// Takes the item off the shelf with a pop-shrink. True if it was a shop item
    /// (and will be destroyed here); false if it wasn't (caller destroys it).
    /// </summary>
    public bool RemoveItemAnimated(GameObject go)
    {
        ShopItem found = null;
        foreach (var it in _items) if (it.root == go) { found = it; break; }
        if (found == null) return false;
        _items.Remove(found);
        if (_hovered == found) _hovered = null;
        if (found.view != null) StartCoroutine(ShrinkOut(found.view));
        if (go != null) Destroy(go);
        return true;
    }

    System.Collections.IEnumerator ShrinkOut(ShopItemView v)
    {
        const float dur = 0.28f;
        var target = v.scaleRoot != null ? v.scaleRoot : v.rect;
        if (v.group != null) v.group.alpha = 1f;
        Vector3 s0 = target != null ? target.localScale : Vector3.one;
        float t = 0f;
        while (t < dur && v != null && target != null)
        {
            t += Time.unscaledDeltaTime;
            float f = Mathf.Clamp01(t / dur);
            float scale = f < 0.2f ? Mathf.Lerp(1f, 1.25f, f / 0.2f) : Mathf.Lerp(1.25f, 0f, (f - 0.2f) / 0.8f);
            target.localScale = s0 * scale;
            yield return null;
        }
        if (v != null) Destroy(v.gameObject);
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
}
