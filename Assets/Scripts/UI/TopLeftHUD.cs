using UnityEngine;
using TMPro;

// The gameplay HUD's numbers: lives, next wave, block / turret currency with their
// per-round income, and the cell readout while placing. The LAYOUT lives in the
// scene — a HudView (the GameplayHUD prefab, placed with GeoWorld ▸ UI ▸ Place
// Gameplay HUD) — so every panel can be moved and resized in the editor; this only
// writes into it. The icon / colour / font fields below are what that menu builds
// the layout with the first time.
public class TopLeftHUD : MonoBehaviour
{
    public static TopLeftHUD Instance;

    [Header("Icons (Sprites)")]
    public Sprite heartIcon;
    public Sprite blockIcon;
    public Sprite turretIcon;
    public Sprite gridIcon;
    public float  iconSize = 26f;

    [Header("Font (TextMeshPro — leave null for TMP default)")]
    public TMP_FontAsset font;
    public float valueSize  = 18f;
    public float incomeSize = 13f;
    public float waveSize   = 18f;   // next-wave text size

    [Header("Colors")]
    public Color panelColor  = new Color(0f, 0f, 0f, 0.42f);
    public Color heartColor  = new Color(1.00f, 0.45f, 0.45f);
    public Color blockColor  = new Color(0.55f, 0.95f, 1.00f);
    public Color turretColor = new Color(1.00f, 0.65f, 0.30f);
    public Color valueColor  = Color.white;
    public Color incomeColor = new Color(0.65f, 0.65f, 0.65f);
    public Color waveColor      = new Color(0f, 0f, 0f);            // next-wave number
    public Color waveLabelColor = new Color(0.6f, 0.6f, 0.6f);      // the "WAVE" label

    [Header("Layout")]
    public Vector2 panelMargin = new Vector2(16f, 16f);   // from the bottom-left corner
    public float   cellGap     = 8f;                       // readout gap above the shop top
    public float   topMargin  = 14f;                       // top-centre panel gap from the top edge
    public float   topInset   = 28f;                       // padding for lives/wave inside the panel

    [Header("Top panel (one integrated sprite behind lives + wave)")]
    public Sprite  topPanelSprite;
    public Vector2 topPanelSize = new Vector2(380f, 36f);

    [Header("Mouse / cell readout")]
    public Camera worldCamera;
    public float rayMaxDistance = 200f;
    public float groundPlaneY = 0f;
    [Range(0f, 0.3f)] public float cellHysteresis = 0.07f;
    public KeyCode toggleKey = KeyCode.None;

    [Header("Layout (scene)")]
    [Tooltip("The authored HUD layout (GameplayHUD prefab). Found in the scene if left empty.")]
    public HudView view;

    bool _visible = true;
    Vector3Int _mouseCell;
    bool _mouseHitValid;
    Vector3Int _lastCommittedCell;
    bool _hasCommittedCell;
    bool _warned;

    void Awake() => Instance = this;
    void Start() => BindView();

    bool BindView()
    {
        if (view == null) view = FindFirstObjectByType<HudView>(FindObjectsInactive.Include);
        if (view == null && !_warned)
        {
            _warned = true;
            Debug.LogWarning("[HUD] No HudView in the scene — run GeoWorld ▸ UI ▸ Place Gameplay HUD and save the scene.");
        }
        if (view != null && view.cellReadout != null) view.cellReadout.gameObject.SetActive(false);
        return view != null;
    }

    // ── Currency-fly-in target points (screen space — the HUD canvas is ScreenSpaceOverlay,
    // so a UI RectTransform's .position IS its screen pixel position). ─────────────────
    public Vector2 BlockCounterScreenPos  => view != null && view.blockValue  != null ? (Vector2)view.blockValue.transform.position  : (Vector2)Input.mousePosition;
    public Vector2 TurretCounterScreenPos => view != null && view.turretValue != null ? (Vector2)view.turretValue.transform.position : (Vector2)Input.mousePosition;

    public void PulseBlockCounter()  { if (view != null && view.blockRow  != null) StartCoroutine(PulseRoutine(view.blockRow)); }
    public void PulseTurretCounter() { if (view != null && view.turretRow != null) StartCoroutine(PulseRoutine(view.turretRow)); }

    System.Collections.IEnumerator PulseRoutine(Transform row)
    {
        const float dur = 0.28f;
        Vector3 baseScale = Vector3.one;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = t / dur;
            // Quick pop out, ease back — 1 -> 1.35 -> 1.
            float s = 1f + Mathf.Sin(k * Mathf.PI) * 0.35f;
            row.localScale = baseScale * s;
            yield return null;
        }
        row.localScale = baseScale;
    }

    void Update()
    {
        if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey)) _visible = !_visible;
        if (view == null && !BindView()) return;

        bool on = _visible && !IntroDirector.Playing && !GameFlowManager.SettlementUp;   // hidden during intro / clear settlement
        if (view.canvas != null) view.canvas.enabled = on;
        if (!on) return;

        UpdateMouseCell();
        RefreshValues();
        PositionCellReadout();
    }

    void RefreshValues()
    {
        var rm  = ResourceManager.Instance;
        var hp  = PlayerHealth.Instance;
        var gfm = GameFlowManager.Instance;
        if (view.livesValue != null)
            view.livesValue.text = hp != null ? $"{hp.CurrentLives} / {hp.maxLives}" : "0 / 0";
        if (view.waveValue != null)
        {
            // "Wave 2/4" — the level's wave count when it has one; endless has no end.
            int wave  = gfm != null ? gfm.UpcomingWaveNumber : 1;
            var lv    = RunConfig.Mode == GameMode.Level ? RunConfig.Level : null;
            int total = lv != null && lv.wavesToClear > 0 ? lv.wavesToClear : 0;
            string lblHex = ColorUtility.ToHtmlStringRGB(waveLabelColor);
            string count  = total > 0 ? $"{Mathf.Min(wave, total)}/{total}" : wave.ToString();
            view.waveValue.text = $"<color=#{lblHex}>Wave</color>  <b>{count}</b>";
        }
        if (view.blockValue   != null) view.blockValue.text   = (rm != null ? rm.BlockCurrency  : 0).ToString();
        if (view.turretValue  != null) view.turretValue.text  = (rm != null ? rm.TurretCurrency : 0).ToString();
        if (view.blockIncome  != null) view.blockIncome.text  = $"+{PerRoundBlockIncome()}";
        if (view.turretIncome != null) view.turretIncome.text = $"+{PerRoundTurretIncome()}";
    }

    void PositionCellReadout()
    {
        if (view.cellReadout == null) return;
        bool show = !(SettingsScreen.Open || PauseMenu.Paused)
            && PlacementController.Instance != null && PlacementController.Instance.currentBlock != null;
        if (view.dockCellReadoutToShop)
            show &= ShopController.Instance != null && ShopController.Instance.ShopVisible;

        view.cellReadout.gameObject.SetActive(show);
        if (!show) return;

        if (view.dockCellReadoutToShop)
        {
            // Just above the top of the shop panel (screen space; the pivot decides the rest).
            Vector2 gui = ShopController.Instance.ShopTopCenter;   // GUI coords (top-left origin)
            float sf = view.canvas != null ? Mathf.Max(0.0001f, view.canvas.scaleFactor) : 1f;
            view.cellReadout.position = new Vector3(gui.x, Screen.height - gui.y + cellGap * sf, 0f);
        }

        if (view.cellValue != null)
            view.cellValue.text = _mouseHitValid ? $"({_mouseCell.x}, {_mouseCell.y}, {_mouseCell.z})" : "---";
    }

    // ── Mouse cell (unchanged) ────────────────────────────────────────────────────

    void UpdateMouseCell()
    {
        _mouseHitValid = false;
        var cam = worldCamera != null ? worldCamera : Camera.main;
        if (cam == null) return;
        Ray ray = cam.ScreenPointToRay(VirtualCursor.Position);
        if (Physics.Raycast(ray, out RaycastHit hit, rayMaxDistance))
        {
            _mouseHitValid = true;
            ProcessCellUpdate(hit.point);
        }
        else
        {
            var plane = new Plane(Vector3.up, new Vector3(0, groundPlaneY, 0));
            if (plane.Raycast(ray, out float t))
            {
                _mouseHitValid = true;
                ProcessCellUpdate(ray.GetPoint(t));
            }
        }
    }

    void ProcessCellUpdate(Vector3 worldHit)
    {
        if (GridSystem.instance == null) return;
        Vector3Int rawCell = GridSystem.instance.WorldToGrid(worldHit);
        if (_hasCommittedCell && rawCell != _lastCommittedCell)
        {
            float cs = GridSystem.instance.cellSize;
            Vector3 lastCenter = GridSystem.instance.GridToWorld(_lastCommittedCell);
            Vector3 d = worldHit - lastCenter;
            float band = cs * cellHysteresis;
            if (Mathf.Abs(d.x) <= cs * 0.5f + band && Mathf.Abs(d.y) <= cs * 0.5f + band && Mathf.Abs(d.z) <= cs * 0.5f + band)
                return;
        }
        _mouseCell = rawCell;
        _lastCommittedCell = rawCell;
        _hasCommittedCell = true;
    }

    int PerRoundBlockIncome()  => ResourceManager.Instance?.balance?.GetBlockIncomeForRound(GameFlowManager.Instance?.RoundIndex ?? 0) ?? 0;
    int PerRoundTurretIncome() => ResourceManager.Instance?.balance?.GetTurretIncomeForRound(GameFlowManager.Instance?.RoundIndex ?? 0) ?? 0;
}
