using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Bottom-left resource HUD, built at runtime as UGUI: lives / block currency /
// turret currency, each shown as an icon + value (+ per-round income). Plus a cell
// readout that docks above the shop while placing. Semi-transparent panel; TMP text
// with adjustable font/size. Keep this component in the scene and assign the sprites.
public class TopLeftHUD : MonoBehaviour
{
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

    [Header("Colors")]
    public Color panelColor  = new Color(0f, 0f, 0f, 0.42f);
    public Color heartColor  = new Color(1.00f, 0.45f, 0.45f);
    public Color blockColor  = new Color(0.55f, 0.95f, 1.00f);
    public Color turretColor = new Color(1.00f, 0.65f, 0.30f);
    public Color valueColor  = Color.white;
    public Color incomeColor = new Color(0.65f, 0.65f, 0.65f);

    [Header("Layout")]
    public Vector2 panelMargin = new Vector2(16f, 16f);   // from the bottom-left corner
    public float   cellGap     = 8f;                       // readout gap above the shop top

    [Header("Mouse / cell readout")]
    public Camera worldCamera;
    public float rayMaxDistance = 200f;
    public float groundPlaneY = 0f;
    [Range(0f, 0.3f)] public float cellHysteresis = 0.07f;
    public KeyCode toggleKey = KeyCode.None;

    bool _visible = true;
    Vector3Int _mouseCell;
    bool _mouseHitValid;
    Vector3Int _lastCommittedCell;
    bool _hasCommittedCell;

    Canvas        _canvas;
    TMP_Text      _livesVal, _blockVal, _blockInc, _turretVal, _turretInc, _cellVal;
    GameObject    _panel, _cellReadout;
    RectTransform _cellRect;

    void Start() => BuildUI();

    void Update()
    {
        if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey)) _visible = !_visible;
        if (_canvas == null) return;

        _canvas.enabled = _visible;
        if (!_visible) return;

        UpdateMouseCell();
        RefreshValues();
        PositionCellReadout();
    }

    void RefreshValues()
    {
        var rm = ResourceManager.Instance;
        var hp = PlayerHealth.Instance;
        _livesVal.text  = hp != null ? $"{hp.CurrentLives} / {hp.maxLives}" : "0 / 0";
        _blockVal.text  = (rm != null ? rm.BlockCurrency  : 0).ToString();
        _turretVal.text = (rm != null ? rm.TurretCurrency : 0).ToString();
        _blockInc.text  = $"+{PerRoundBlockIncome()}";
        _turretInc.text = $"+{PerRoundTurretIncome()}";
    }

    void PositionCellReadout()
    {
        bool show = !(SettingsScreen.Open || PauseMenu.Paused)
            && PlacementController.Instance != null && PlacementController.Instance.currentBlock != null
            && ShopController.Instance != null && ShopController.Instance.ShopVisible;

        _cellReadout.SetActive(show);
        if (!show) return;

        Vector2 gui = ShopController.Instance.ShopTopCenter;   // GUI coords (top-left origin)
        float sf = Mathf.Max(0.0001f, _canvas.scaleFactor);
        float sx = gui.x;
        float sy = Screen.height - gui.y;                      // → screen (bottom-left origin)
        _cellRect.anchoredPosition = new Vector2(sx / sf, sy / sf + cellGap);

        _cellVal.text = _mouseHitValid ? $"({_mouseCell.x}, {_mouseCell.y}, {_mouseCell.z})" : "---";
    }

    // ── Build ──────────────────────────────────────────────────────────────────

    void BuildUI()
    {
        var canvasGO = new GameObject("TopLeftHUDCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);

        _canvas = canvasGO.GetComponent<Canvas>();
        _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 90;

        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 1f;

        BuildPanel();
        BuildCellReadout();
    }

    void BuildPanel()
    {
        var panel = NewRect("ResourcePanel", _canvas.transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0f, 0f);   // bottom-left
        panel.anchoredPosition = panelMargin;
        _panel = panel.gameObject;

        var bg = panel.gameObject.AddComponent<Image>();
        bg.color = panelColor;
        bg.raycastTarget = false;

        var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 12, 8, 8);
        vlg.spacing = 4f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = false; vlg.childForceExpandHeight = false;

        var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _livesVal  = BuildRow(panel, heartIcon,  heartColor,  out _);
        _blockVal  = BuildRow(panel, blockIcon,  blockColor,  out _blockInc);
        _turretVal = BuildRow(panel, turretIcon, turretColor, out _turretInc);
    }

    // icon + value (+ optional income). Returns the value text; income via out.
    TMP_Text BuildRow(RectTransform parent, Sprite icon, Color valCol, out TMP_Text income)
    {
        var row = NewRect("Row", parent);
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.childAlignment = TextAnchor.MiddleLeft; h.spacing = 8f;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = false;
        row.gameObject.AddComponent<LayoutElement>().minHeight = iconSize;

        var img = NewImage("Icon", row, Color.white, false);
        img.sprite = icon;
        img.enabled = icon != null;
        img.color=valCol;
        var ile = img.gameObject.AddComponent<LayoutElement>();
        ile.minWidth = ile.preferredWidth = ile.minHeight = ile.preferredHeight = iconSize;

        var val = NewText("Value", row, valueSize, valCol, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        val.text = "0";
        var vle = val.gameObject.AddComponent<LayoutElement>();
        vle.minWidth = vle.preferredWidth = 56f;

        income = NewText("Income", row, incomeSize, incomeColor, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        income.text = "";
        return val;
    }

    void BuildCellReadout()
    {
        var rt = NewRect("CellReadout", _canvas.transform);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);   // bottom-left origin for screen positioning
        rt.pivot = new Vector2(0.5f, 0f);                     // bottom-centre over the point
        rt.sizeDelta = new Vector2(160f, 30f);
        _cellRect = rt;
        _cellReadout = rt.gameObject;

        var bg = rt.gameObject.AddComponent<Image>();
        bg.color = panelColor;
        bg.raycastTarget = false;

        var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(8, 8, 4, 4);
        h.spacing = 6f; h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = false;

        if (gridIcon != null)
        {
            var img = NewImage("Icon", rt, Color.white, false);
            img.sprite = gridIcon;
            var ile = img.gameObject.AddComponent<LayoutElement>();
            ile.minWidth = ile.preferredWidth = ile.minHeight = ile.preferredHeight = 18f;
        }

        _cellVal = NewText("Value", rt, valueSize, valueColor, FontStyles.Bold, TextAlignmentOptions.Midline);
        _cellVal.text = "---";
        _cellReadout.SetActive(false);
    }

    // ── Mouse cell (unchanged) ────────────────────────────────────────────────────

    void UpdateMouseCell()
    {
        _mouseHitValid = false;
        var cam = worldCamera != null ? worldCamera : Camera.main;
        if (cam == null) return;
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
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

    // ── UI primitives ────────────────────────────────────────────────────────────

    RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    Image NewImage(string name, Transform parent, Color color, bool raycast)
    {
        var rt = NewRect(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = raycast;
        return img;
    }

    TMP_Text NewText(string name, Transform parent, float size, Color color,
                     FontStyles style, TextAlignmentOptions align)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize      = size;
        t.color         = color;
        t.fontStyle     = style;
        t.alignment     = align;
        t.raycastTarget = false;
        t.enableWordWrapping = false;
        return t;
    }
}
