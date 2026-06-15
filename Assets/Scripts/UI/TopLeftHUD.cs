using UnityEngine;

// Top-left HUD — always visible, production overlay.
//
// Two sections:
//   1. Currencies        — Block ¤ + Turret ¤, with per-round income hint
//   2. Mouse grid cell   — XYZ of the cell the cursor is hovering over
//
// Values are READ DIRECTLY from ResourceManager.Instance every OnGUI tick
// (not cached / not event-subscribed). This sidesteps the Awake-order race
// where TopLeftHUD.OnEnable might fire before ResourceManager.Awake, which
// would leave the cache at 0 forever.
//
// Drop on any persistent GameObject (e.g. GameManager). No prefab / Canvas
// / UGUI setup — uses IMGUI to match DebugUI / ShopController.
public class TopLeftHUD : MonoBehaviour
{
    [Header("Layout")]
    public int panelX     = 12;
    public int panelY     = 12;
    public int panelW     = 220;
    public int rowHeight  = 20;
    public int padding    = 8;

    [Header("Colors")]
    public Color blockColor  = new(0.55f, 0.95f, 1.00f);   // cyan-ish (block currency)
    public Color turretColor = new(1.00f, 0.65f, 0.30f);   // orange (turret currency)
    public Color labelColor  = new(0.85f, 0.85f, 0.85f);   // muted white
    public Color valueColor  = new(1.00f, 1.00f, 1.00f);   // bright white
    public Color hintColor   = new(0.65f, 0.65f, 0.65f);   // dim grey
    public Color bgColor     = new(0f, 0f, 0f, 0.55f);
    public Color heartColor  = new(1.00f, 0.40f, 0.40f);   // red (lives)

    [Header("Mouse coordinates")]
    [Tooltip("Camera used to raycast under the mouse. If null, Camera.main is used.")]
    public Camera worldCamera;
    [Tooltip("Length of the ray cast from the camera through the mouse pointer.")]
    public float rayMaxDistance = 200f;
    [Tooltip("Y of an invisible fallback plane the ray projects onto when nothing physical is hit. Use 0 for the ground plane.")]
    public float groundPlaneY = 0f;
    [Tooltip("Hysteresis on cell transitions (in cellSize units). When the cursor sits on the boundary between two cells, floor() flickers between them due to float precision — this band forces the mouse to move clearly INSIDE a new cell before the displayed value updates. 0 = no hysteresis (flickery), 0.05–0.10 = comfortable.")]
    [Range(0f, 0.3f)] public float cellHysteresis = 0.07f;

    [Header("Toggle")]
    [Tooltip("Optional key to hide/show the panel. Leave None for always-on.")]
    public KeyCode toggleKey = KeyCode.None;
    public bool visibleOnStart = true;

    // ── Live state ────────────────────────────────────────────────────────
    bool _visible;

    // Updated every OnGUI tick from a mouse raycast.
    Vector3Int _mouseCell;
    bool       _mouseHitValid;
    // Hysteresis tracking — last cell we COMMITTED to displaying.
    Vector3Int _lastCommittedCell;
    bool       _hasCommittedCell;

    // Styles built once.
    GUIStyle _labelStyle, _valueStyle, _hintStyle;
    bool _stylesBuilt;

    void Awake() => _visible = visibleOnStart;

    void Update()
    {
        if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey))
            _visible = !_visible;
    }

    // ── Mouse → cell ─────────────────────────────────────────────────────

    void UpdateMouseCell()
    {
        _mouseHitValid = false;

        var cam = worldCamera != null ? worldCamera : Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        Vector3 worldHit = Vector3.zero;

        // Try a physical hit first so the displayed cell matches the block
        // surface the player is hovering over.
        if (Physics.Raycast(ray, out RaycastHit hit, rayMaxDistance))
        {
            worldHit       = hit.point;
            _mouseHitValid = true;
        }
        else
        {
            // Fall back to a horizontal ground plane at groundPlaneY so
            // hovering over the sky still gives a meaningful coordinate.
            var plane = new Plane(Vector3.up, new Vector3(0, groundPlaneY, 0));
            if (plane.Raycast(ray, out float t))
            {
                worldHit       = ray.GetPoint(t);
                _mouseHitValid = true;
            }
        }

        if (!_mouseHitValid || GridSystem.instance == null) return;

        Vector3Int rawCell = GridSystem.instance.WorldToGrid(worldHit);

        // Hysteresis: when the cursor is on a cell boundary, float jitter
        // in the raycast hit point makes floor() flicker between two cells
        // frame-to-frame. Stick to the previously displayed cell unless the
        // world point is now CLEARLY inside a different cell — i.e. past
        // its center ± (half + hysteresisBand).
        if (_hasCommittedCell && rawCell != _lastCommittedCell)
        {
            float cs   = GridSystem.instance.cellSize;
            float half = cs * 0.5f;
            float band = cs * cellHysteresis;
            Vector3 lastCenter = GridSystem.instance.GridToWorld(_lastCommittedCell);
            Vector3 d = worldHit - lastCenter;
            if (Mathf.Abs(d.x) <= half + band &&
                Mathf.Abs(d.y) <= half + band &&
                Mathf.Abs(d.z) <= half + band)
            {
                // Still within the sticky band of the previous cell — keep it.
                return;
            }
        }

        _mouseCell         = rawCell;
        _lastCommittedCell = rawCell;
        _hasCommittedCell  = true;
    }

    // ── Draw ─────────────────────────────────────────────────────────────

    void OnGUI()
    {
        if (!_visible) return;
        float s = UiScale.Get();
        BuildStyles(s);
        UpdateMouseCell();

        // ── Bottom-left status panel: HP + currencies. Auto-fits its content width
        //    (so it never reaches the shop) and scales with the window. ──
        float pad    = padding   * s;
        float rh     = rowHeight * s;
        float labelW = 54f * s;
        float px     = panelX * s;

        var hp = PlayerHealth.Instance;
        int lives = hp != null ? hp.CurrentLives : 0;
        int maxL  = hp != null ? hp.maxLives     : 0;
        var rm = ResourceManager.Instance;
        int block  = rm != null ? rm.BlockCurrency  : 0;
        int turret = rm != null ? rm.TurretCurrency : 0;
        int blockPR = PerRoundBlockIncome();
        int turrPR  = PerRoundTurretIncome();

        float contentW = Mathf.Max(
            _valueStyle.CalcSize(new GUIContent($"♥ {lives} / {maxL}")).x,
            CurrencyRowWidth(block,  blockPR, s, labelW),
            CurrencyRowWidth(turret, turrPR,  s, labelW));

        int   rows = 3;                                 // HP, BLOCK, TURRET
        float h    = pad * 2f + rows * rh;
        float py   = Screen.height - h - panelY * s;

        Color prev = GUI.color;
        GUI.color  = bgColor;
        GUI.DrawTexture(new Rect(px - pad, py - pad, contentW + pad * 2f, h), Texture2D.whiteTexture);
        GUI.color  = prev;

        float y = py;
        y = DrawHpRow(y, lives, maxL, px, contentW, rh);
        y = DrawCurrencyRow(y, "BLOCK",  block,  blockPR, blockColor,  s, px, rh, labelW);
        y = DrawCurrencyRow(y, "TURRET", turret, turrPR,  turretColor, s, px, rh, labelW);

        DrawCellReadout(s);
    }

    float CurrencyRowWidth(int value, int perRound, float s, float labelW)
    {
        float w = labelW + _valueStyle.CalcSize(new GUIContent(value.ToString())).x;
        if (perRound > 0)
            w += 8f * s + _hintStyle.CalcSize(new GUIContent($"+{perRound}/rd")).x;
        return w;
    }

    float DrawHpRow(float y, int lives, int max, float px, float w, float rh)
    {
        _valueStyle.normal.textColor = heartColor;
        GUI.Label(new Rect(px, y, w, rh), $"♥ {lives} / {max}", _valueStyle);
        return y + rh;
    }

    float DrawCurrencyRow(float y, string label, int value, int perRound, Color valColor,
                          float s, float px, float rh, float labelW)
    {
        _labelStyle.normal.textColor = labelColor;
        GUI.Label(new Rect(px, y + 2f * s, labelW, rh), label, _labelStyle);

        _valueStyle.normal.textColor = valColor;
        string valStr = value.ToString();
        float valW = _valueStyle.CalcSize(new GUIContent(valStr)).x;
        GUI.Label(new Rect(px + labelW, y, valW + 4f, rh), valStr, _valueStyle);

        if (perRound > 0)
        {
            _hintStyle.alignment        = TextAnchor.MiddleLeft;
            _hintStyle.normal.textColor = hintColor;
            GUI.Label(new Rect(px + labelW + valW + 8f * s, y + 3f * s, 90f * s, rh),
                      $"+{perRound}/rd", _hintStyle);
        }
        return y + rh;
    }

    // CELL coordinate — only while placing a block, docked to the shop rift's
    // top-centre (just above its top edge). Hidden otherwise.
    void DrawCellReadout(float s)
    {
        if (SettingsScreen.Open || PauseMenu.Paused) return;      // hidden in settings / pause

        var pc = PlacementController.Instance;
        if (pc == null || pc.currentBlock == null) return;        // only when placing

        var shop = ShopController.Instance;
        if (shop == null || !shop.ShopVisible) return;

        float w = 170f * s, hgt = 26f * s;
        Vector2 tc = shop.ShopTopCenter;
        float x  = tc.x - w * 0.5f;
        float yy = tc.y - hgt - 6f * s;                           // above the shop's top edge

        Color prev = GUI.color;
        GUI.color  = bgColor;
        GUI.DrawTexture(new Rect(x, yy, w, hgt), Texture2D.whiteTexture);
        GUI.color  = prev;

        _labelStyle.normal.textColor = labelColor;
        GUI.Label(new Rect(x + 10f * s, yy, 50f * s, hgt), "CELL", _labelStyle);

        if (_mouseHitValid && GridSystem.instance != null)
        {
            _valueStyle.normal.textColor = valueColor;
            GUI.Label(new Rect(x + 54f * s, yy, w - 54f * s, hgt),
                      $"({_mouseCell.x}, {_mouseCell.y}, {_mouseCell.z})", _valueStyle);
        }
        else
        {
            _hintStyle.alignment         = TextAnchor.MiddleLeft;
            _hintStyle.normal.textColor  = hintColor;
            GUI.Label(new Rect(x + 54f * s, yy, w - 54f * s, hgt), "—", _hintStyle);
        }
    }

    // ── Per-round income lookup (BalanceTable-aware) ─────────────────────

    int PerRoundBlockIncome()
    {
        var rm = ResourceManager.Instance;
        if (rm == null) return 0;
        if (rm.balance != null)
        {
            int round = GameFlowManager.Instance != null ? GameFlowManager.Instance.RoundIndex : 0;
            return rm.balance.GetBlockIncomeForRound(round);
        }
        return rm.blockCurrencyPerRound;
    }

    int PerRoundTurretIncome()
    {
        var rm = ResourceManager.Instance;
        if (rm == null) return 0;
        if (rm.balance != null)
        {
            int round = GameFlowManager.Instance != null ? GameFlowManager.Instance.RoundIndex : 0;
            return rm.balance.GetTurretIncomeForRound(round);
        }
        return rm.turretCurrencyPerRound;
    }

    // ── Style bootstrap ──────────────────────────────────────────────────

    float _builtScale = -1f;
    void BuildStyles(float s)
    {
        if (Mathf.Approximately(_builtScale, s) && _labelStyle != null) return;
        _builtScale = s;

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = Mathf.Max(8, Mathf.RoundToInt(11f * s)),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
        };
        _valueStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = Mathf.Max(9, Mathf.RoundToInt(15f * s)),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
        };
        _hintStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = Mathf.Max(8, Mathf.RoundToInt(10f * s)),
            fontStyle = FontStyle.Normal,
            alignment = TextAnchor.MiddleLeft,
        };
    }
}
