using System;
using UnityEngine;

// Unified debug overlay — draws all game state in one panel.
// Attach this to any persistent GameObject in the scene (e.g. GameManager).
// Remove or disable when real UI is ready.
public class DebugUI : MonoBehaviour
{
    [Header("Panel")]
    public int   panelX     = 12;
    public int   panelY     = 12;
    public int   panelW     = 260;
    public KeyCode toggleKey = KeyCode.F1;   // hide/show the whole panel

    bool _visible = true;

    // ── Shared styles (built once in OnGUI) ──────────────────────────────────
    GUIStyle _header;
    GUIStyle _row;
    GUIStyle _rowGood;
    GUIStyle _rowBad;
    GUIStyle _phaseLabel;
    bool     _stylesBuilt;

    void Update()
    {
        if (Input.GetKeyDown(toggleKey)) _visible = !_visible;
    }

    void OnGUI()
    {
        if (!_visible) return;

        BuildStyles();

        // ── Measure content height first ─────────────────────────────────────
        int lines = CountLines();
        int h     = lines * 18 + 20;   // 18px per row + padding

        // Semi-transparent dark background
        GUI.color = new Color(0, 0, 0, 0.55f);
        GUI.DrawTexture(new Rect(panelX - 4, panelY - 4, panelW + 8, h + 8), Texture2D.whiteTexture);
        GUI.color = Color.white;

        int y = panelY;

        y = DrawGameState(y);
        y = DrawDivider(y);
        y = DrawResources(y);
        y = DrawDivider(y);
        y = DrawShop(y);
    }

    // ── Game State ────────────────────────────────────────────────────────────

    int DrawGameState(int y)
    {
        var gfm = GameFlowManager.Instance;
        if (gfm == null)
        {
            GUI.Label(new Rect(panelX, y, panelW, 18), "GameFlowManager: not found", _row);
            return y + 18;
        }

        // Phase — coloured pill
        Color phaseColor = gfm.phase switch
        {
            GamePhase.Build       => new Color(0.55f, 0.90f, 0.55f),
            GamePhase.ReadyToRun  => new Color(1.00f, 0.85f, 0.20f),
            GamePhase.Running     => new Color(1.00f, 0.45f, 0.30f),
            _                     => Color.grey
        };

        _phaseLabel.normal.textColor = phaseColor;
        GUI.Label(new Rect(panelX, y, panelW, 18),
            $"●  {gfm.phase.ToString().ToUpper()}", _phaseLabel);
        y += 20;

        // Loop layers
        int loopCur = gfm.ActiveLoopCount;
        int loopMax = gfm.maxLoopLayers;
        string bar  = LoopBar(loopCur, loopMax);
        GUI.Label(new Rect(panelX, y, panelW, 18),
            $"Loops   {bar}  {loopCur}/{loopMax}", _row);
        y += 18;

        // Runs until next endpoint
        int runs    = gfm.RunsSinceLastEndpoint;
        int need    = gfm.runsPerEndpoint;
        string prog = $"Runs to endpoint  {runs}/{need}";
        GUI.Label(new Rect(panelX, y, panelW, 18), prog, _row);
        y += 18;

        // Round index (how many extra endpoints added)
        GUI.Label(new Rect(panelX, y, panelW, 18),
            $"Endpoint wave  {gfm.RoundIndex}", _row);
        y += 18;

        return y;
    }

    // ── Resources ─────────────────────────────────────────────────────────────

    int DrawResources(int y)
    {
        var rm = ResourceManager.Instance;

        GUI.Label(new Rect(panelX, y, panelW, 18), "RESOURCES", _header);
        y += 20;

        if (rm == null)
        {
            GUI.Label(new Rect(panelX, y, panelW, 18), "ResourceManager: not found", _row);
            return y + 18;
        }

        GUI.Label(new Rect(panelX, y, panelW, 18),
            $"Block  ¤  {rm.BlockCurrency,5}    +{rm.blockCurrencyPerRound}/round", _rowGood);
        y += 18;

        bool regen  = rm.BlockCurrency >= 0;   // always show regen state
        string regenStr = $"(+{rm.turretRegenPerSecond:F1}/s  ";

        // Hack: access combat state through the property that was exposed
        // ResourceManager doesn't expose _combatActive directly,
        // so we infer from GameFlowManager.
        bool combatActive = GameFlowManager.Instance?.phase == GamePhase.Running;
        regenStr += combatActive ? "REGEN)" : "idle)";

        GUI.Label(new Rect(panelX, y, panelW, 18),
            $"Turret ¤  {rm.TurretCurrency,5}    +{rm.turretCurrencyPerRound}/round  {regenStr}",
            combatActive ? _rowGood : _row);
        y += 18;

        return y;
    }

    // ── Shop Prices ───────────────────────────────────────────────────────────

    int DrawShop(int y)
    {
        var shop = ShopController.Instance;

        GUI.Label(new Rect(panelX, y, panelW, 18), "SHOP  (current items)", _header);
        y += 20;

        if (shop == null) return y;

        var infos = shop.GetShopInfos();
        if (infos.Count == 0)
        {
            GUI.Label(new Rect(panelX, y, panelW, 18), "  (empty)", _row);
            y += 18;
            return y;
        }

        // Column header
        GUI.Label(new Rect(panelX, y, panelW, 18), "  Block              Price", _row);
        y += 18;

        foreach (var info in infos)
        {
            string line  = $"  {info.displayName,-16}  {info.price,4} ¤";
            var    style = info.affordable ? _rowGood : _rowBad;

            GUI.Label(new Rect(panelX, y, panelW, 18), line, style);

            string mark = info.affordable ? "✓" : "✗";
            GUI.Label(new Rect(panelX + panelW - 22, y, 20, 18), mark, style);

            y += 18;
        }

        return y;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    int DrawDivider(int y)
    {
        GUI.color = new Color(1, 1, 1, 0.18f);
        GUI.DrawTexture(new Rect(panelX, y + 2, panelW, 1), Texture2D.whiteTexture);
        GUI.color = Color.white;
        return y + 8;
    }

    // Counts total rows needed so we can size the background box.
    int CountLines()
    {
        int n = 0;
        n += 4;    // game state: phase + loops + runs + round
        n += 1;    // divider
        n += 3;    // resources header + 2 rows
        n += 1;    // divider
        // shop: header + column header + actual item count (or 1 for "empty")
        int shopItems = ShopController.Instance?.GetShopInfos().Count ?? 0;
        n += 2 + Mathf.Max(1, shopItems);
        return n;
    }

    static string LoopBar(int cur, int max)
    {
        const string filled = "█";
        const string empty  = "░";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < max; i++)
            sb.Append(i < cur ? filled : empty);
        return sb.ToString();
    }

    void BuildStyles()
    {
        if (_stylesBuilt) return;
        _stylesBuilt = true;

        _header = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 11,
            fontStyle = FontStyle.Bold,
        };
        _header.normal.textColor = new Color(0.75f, 0.75f, 0.75f);

        _row = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
        };
        _row.normal.textColor = new Color(0.88f, 0.88f, 0.88f);

        _rowGood = new GUIStyle(_row);
        _rowGood.normal.textColor = new Color(0.55f, 1.00f, 0.60f);

        _rowBad = new GUIStyle(_row);
        _rowBad.normal.textColor = new Color(1.00f, 0.38f, 0.38f);

        _phaseLabel = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 13,
            fontStyle = FontStyle.Bold,
        };
    }
}
