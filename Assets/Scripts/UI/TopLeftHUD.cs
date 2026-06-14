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
        BuildStyles();
        UpdateMouseCell();

        // ── Bottom-left status panel: HP + currencies ──
        // panelY is now the margin from the BOTTOM edge (was top in the old layout).
        int rows = 3;                                  // HP, BLOCK, TURRET
        int h    = padding * 2 + rows * rowHeight;
        int py   = Screen.height - h - panelY;

        Color prev = GUI.color;
        GUI.color  = bgColor;
        GUI.DrawTexture(new Rect(panelX - padding, py - padding, panelW + padding * 2, h),
                        Texture2D.whiteTexture);
        GUI.color  = prev;

        int y = py;

        // ── HP (read live from PlayerHealth) ──
        var hp = PlayerHealth.Instance;
        y = DrawHpRow(y, hp != null ? hp.CurrentLives : 0, hp != null ? hp.maxLives : 0);

        // ── Currencies (READ LIVE EVERY FRAME — no cache) ──
        var rm = ResourceManager.Instance;
        int block   = rm != null ? rm.BlockCurrency  : 0;
        int turret  = rm != null ? rm.TurretCurrency : 0;

        y = DrawCurrencyRow(y, "BLOCK",  block,  PerRoundBlockIncome(), blockColor);
        y = DrawCurrencyRow(y, "TURRET", turret, PerRoundTurretIncome(),  turretColor);

        // ── Mouse cell — its own readout, bottom-centre above the tray ──
        DrawCellReadout();
    }

    int DrawHpRow(int y, int lives, int max)
    {
        _valueStyle.normal.textColor = heartColor;
        GUI.Label(new Rect(panelX, y, panelW, rowHeight), $"♥ {lives} / {max}", _valueStyle);
        return y + rowHeight;
    }

    int DrawCurrencyRow(int y, string label, int value, int perRound, Color valColor)
    {
        _labelStyle.normal.textColor = labelColor;
        GUI.Label(new Rect(panelX, y + 2, 70, rowHeight), label, _labelStyle);

        _valueStyle.normal.textColor = valColor;
        GUI.Label(new Rect(panelX + 70, y, panelW - 70 - 60, rowHeight),
                  value.ToString(), _valueStyle);

        if (perRound > 0)
        {
            _hintStyle.normal.textColor = hintColor;
            GUI.Label(new Rect(panelX + panelW - 60, y + 3, 60, rowHeight),
                      $"+{perRound}/rd", _hintStyle);
        }
        return y + rowHeight;
    }

    // CELL coordinate — only while placing a block, docked to the shop rift's
    // top-centre (just above its top edge). Hidden otherwise.
    void DrawCellReadout()
    {
        var pc = PlacementController.Instance;
        if (pc == null || pc.currentBlock == null) return;        // only when placing

        var shop = ShopController.Instance;
        if (shop == null || !shop.ShopVisible) return;

        float w = 180f, hgt = 26f;
        Vector2 tc = shop.ShopTopCenter;
        float x  = tc.x - w * 0.5f;
        float yy = tc.y - hgt - 6f;                               // above the shop's top edge

        Color prev = GUI.color;
        GUI.color  = bgColor;
        GUI.DrawTexture(new Rect(x, yy, w, hgt), Texture2D.whiteTexture);
        GUI.color  = prev;

        _labelStyle.normal.textColor = labelColor;
        GUI.Label(new Rect(x + 10f, yy, 50f, hgt), "CELL", _labelStyle);

        if (_mouseHitValid && GridSystem.instance != null)
        {
            _valueStyle.normal.textColor = valueColor;
            GUI.Label(new Rect(x + 56f, yy, w - 56f, hgt),
                      $"({_mouseCell.x}, {_mouseCell.y}, {_mouseCell.z})", _valueStyle);
        }
        else
        {
            _hintStyle.normal.textColor = hintColor;
            GUI.Label(new Rect(x + 56f, yy, w - 56f, hgt), "—", _hintStyle);
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

    void BuildStyles()
    {
        if (_stylesBuilt) return;
        _stylesBuilt = true;

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
        };
        _valueStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 16,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
        };
        _hintStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 10,
            fontStyle = FontStyle.Normal,
            alignment = TextAnchor.MiddleRight,
        };
    }
}
