using System.Collections.Generic;
using UnityEngine;

public partial class PlacementController
{
    // =========================
    // SELECTION INFO PANEL
    // =========================

    // True when the cursor is over the info panel this frame. Used in Update to
    // swallow the click so the panel's own IMGUI buttons handle it instead of
    // the world-selection raycast (which would deselect). Input.mousePosition is
    // bottom-left origin; GUI rects are top-left, hence the Y flip.
    bool IsPointerOverSelectionPanel()
    {
        if (_panelRect.width <= 0f || _panelRect.height <= 0f) return false;
        Vector2 p = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        return _panelRect.Contains(p);
    }

    void DrawSelectionPanel()
    {
        // Spawn point ("起点") selected → show the upcoming-wave intel panel
        // instead of block/turret stats. Hidden during combat (forecast is for
        // the NEXT wave, which is ambiguous mid-run).
        if (mode == PlacementMode.Select && _selectedEndpoint != null && _selectedEndpointIsStart
            && (GameFlowManager.Instance == null || GameFlowManager.Instance.phase != GamePhase.Running))
        {
            DrawStartPanel();
            return;
        }

        var ins = selectedInstance;
        if (mode != PlacementMode.Select || ins == null
            || ins.visualObject == null || ins.data == null)
        {
            _panelRect = default;
            return;
        }

        EnsurePanelStyles();

        bool  isTurret = TurretTypes.Is(ins.data.blockType);
        float panelW   = 236f;
        float margin   = 12f;
        // Hug the content: use last repaint's measured height for THIS selection;
        // fall back to a (generous) estimate on the first frame of a new selection
        // so nothing clips before the first measurement lands.
        float panelH = (_selPanelFor == ins && _selPanelHeight > 1f)
            ? _selPanelHeight
            : EstimatePanelHeight(ins, isTurret);
        float x = Screen.width  - panelW - margin;
        float y = (Screen.height - panelH) * 0.5f;
        _panelRect = new Rect(x, y, panelW, panelH);

        // Pop-in: scale the panel up from its center (with a little overshoot) when
        // a new target is selected. Restart the timer on selection change.
        if (_panelAnimFor != ins) { _panelAnimStart = Time.time; _panelAnimFor = ins; }
        float pt  = selectionPopDuration > 1e-4f ? (Time.time - _panelAnimStart) / selectionPopDuration : 1f;
        float pop = Mathf.Lerp(0.6f, 1f, EaseOutBack(Mathf.Clamp01(pt)));
        Matrix4x4 prevGuiMatrix = GUI.matrix;
        GUIUtility.ScaleAroundPivot(new Vector2(pop, pop), _panelRect.center);

        GUILayout.BeginArea(_panelRect);
        GUILayout.BeginVertical(_panelBox, GUILayout.Width(panelW));

        GUILayout.Label(isTurret
            ? TurretTypes.DisplayName(ins.data.blockType)
            : ins.data.DisplayName, _panelTitle);

        PanelDivider();

        if (isTurret) DrawTurretStats(ins);
        else          DrawBlockStats(ins);

        PanelDivider();
        DrawPanelButtons(ins, isTurret);

        GUILayout.EndVertical();
        // Measure the real content height (repaint only) so next frame hugs it.
        if (Event.current.type == EventType.Repaint)
        {
            _selPanelHeight = GUILayoutUtility.GetLastRect().height;
            _selPanelFor    = ins;
        }
        GUILayout.EndArea();
        GUI.matrix = prevGuiMatrix;
    }

    // Spawn-point intel panel: upcoming wave number, total enemy count, and the
    // per-type breakdown ("Runner ×7"). No pick-up / sell — endpoints aren't
    // editable. Forecast is cached per round (non-destructive to the run RNG).
    void DrawStartPanel()
    {
        EnsurePanelStyles();

        var gfm   = GameFlowManager.Instance;
        int round = gfm != null ? gfm.RoundIndex : 0;
        if (gfm != null && _startForecastRound != round)
        {
            _startForecast      = gfm.GetNextWaveForecast();
            _startForecastRound = round;
        }
        var fc = _startForecast;

        float panelW = 236f;
        float panelH = EstimateStartPanelHeight(fc);
        float margin = 12f;
        float x = Screen.width  - panelW - margin;
        float y = (Screen.height - panelH) * 0.5f;
        _panelRect = new Rect(x, y, panelW, panelH);

        GUILayout.BeginArea(_panelRect, GUIContent.none, _panelBox);

        GUILayout.Label("Spawn Point", _panelTitle);
        PanelDivider();

        PanelRow("Wave",    fc.waveNumber.ToString());
        PanelRow("Enemies", fc.valid ? fc.totalCount.ToString() : "—");

        GUILayout.Space(4f);
        GUILayout.Label("Incoming", _panelLabel);
        GUILayout.Space(2f);

        if (fc.valid && fc.groups != null && fc.groups.Count > 0)
        {
            for (int i = 0; i < fc.groups.Count; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(fc.groups[i].name, _panelValue);
                GUILayout.FlexibleSpace();
                GUILayout.Label("×" + fc.groups[i].count, _panelValue);
                GUILayout.EndHorizontal();
            }
        }
        else
        {
            GUILayout.Label("Composition unknown", _panelLabel);
        }

        GUILayout.EndArea();
    }

    float EstimateStartPanelHeight(GameFlowManager.WaveForecast fc)
    {
        const float row = 22f, title = 30f, divider = 12f, padding = 28f;
        int header = 1;   // "Incoming" sub-header
        int lines  = (fc.valid && fc.groups != null && fc.groups.Count > 0)
            ? fc.groups.Count : 1;
        // rows: Wave + Enemies (2) + header + per-type lines.
        return padding + title + divider + (2 + header + lines) * row + 10f;
    }

    // Slightly-generous content-height estimate so the panel hugs its content
    // (no big empty gap) without ever clipping the bottom buttons.
    float EstimatePanelHeight(PlacedBlockInstance ins, bool isTurret)
    {
        const float row = 26f, title = 36f, divider = 12f, button = 34f, buttonGap = 8f, padding = 30f;
        int   rows  = 0;
        float extra = 0f;

        if (isTurret)
        {
            var t = ins.visualObject.GetComponentInChildren<TurretController>();
            if (t == null) rows = 1;
            else
            {
                rows = 2;   // Damage, Fire rate
                if      (t.mode == TurretController.Mode.Slow) rows += 2;
                else if (t.mode == TurretController.Mode.Aoe)  rows += 1;
            }
        }
        else
        {
            rows = 2;       // Synergy swatch + activation progress
            if (ins.color != BlockColor.None)
            {
                string d = BlockColorPalette.Description(ins.color);
                if (!string.IsNullOrEmpty(d)) extra += d.Split('\n').Length * (row - 2f);
            }
        }

        bool combatLocked = GameFlowManager.Instance != null
            && GameFlowManager.Instance.phase == GamePhase.Running && !isTurret;
        if (combatLocked) extra += row;

        return padding + title + divider + rows * row + extra
             + 12f + divider + (button * 2f + buttonGap) + 12f;
    }

    void DrawBlockStats(PlacedBlockInstance ins)
    {
        if (ins.color == BlockColor.None)
        {
            PanelRow("Synergy", "None");
            return;
        }

        Color theme = BlockColorPalette.Get(ins.color);

        // ONE line: color swatch + synergy type + activation progress (e.g.
        // "Order  2/3"). The whole line turns the theme color once active.
        bool   hasProg = TryGetSynergyProgress(ins, out string ruleName, out int cur, out int req, out bool active);
        string name    = (hasProg && !string.IsNullOrEmpty(ruleName)) ? ruleName : ins.color.ToString();
        string txt     = name;
        if (hasProg) txt += req > 0 ? $"   {cur}/{req}" : "   active";

        GUILayout.BeginHorizontal();
        Rect sw = GUILayoutUtility.GetRect(16f, 16f, GUILayout.Width(16f), GUILayout.Height(16f));
        var prev = GUI.color;
        GUI.color = theme;
        GUI.DrawTexture(sw, Texture2D.whiteTexture);
        GUI.color = prev;
        GUILayout.Space(8f);
        var prevC = GUI.contentColor;
        if (active) GUI.contentColor = theme;
        GUILayout.Label(txt, _panelProgress);
        GUI.contentColor = prevC;
        GUILayout.EndHorizontal();

        // Flavor description.
        string desc = BlockColorPalette.Description(ins.color);
        if (!string.IsNullOrEmpty(desc))
        {
            GUILayout.Space(3f);
            GUILayout.Label(desc, _panelLabel);
        }
    }

    // Activation progress for the selected block's color. Returns the matching
    // rule's display name + (current/required), and whether it's already active
    // (this piece sits in a live claim → caller themes the text).
    bool TryGetSynergyProgress(PlacedBlockInstance ins, out string ruleName,
                               out int cur, out int req, out bool active)
    {
        ruleName = null; cur = 0; req = 0; active = false;

        var ev    = SynergyEvaluator.Instance;
        var piece = ins?.placedPiece;
        if (ev == null || piece == null) return false;

        // Active: this piece is part of a live claim.
        var actives = ev.Actives;
        for (int i = 0; i < actives.Count; i++)
        {
            var a = actives[i];
            if (a?.rule == null || a.claimedPieces == null || !a.claimedPieces.Contains(piece)) continue;
            active   = true;
            ruleName = SynergyRuleName(a.rule);
            if (a.rule.TryGetActivationProgress(ev.Board, piece, out int c, out int r) && r > 0) { req = r; cur = r; }
            else { req = 0; cur = 0; }   // active but no simple count → name only
            return true;
        }

        // Not active: best-matching rule for this color (closest to firing).
        var rules = ev.rules;
        if (rules == null) return false;
        float best = -1f;
        for (int i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (rule == null || rule.color != ins.color) continue;
            if (!rule.TryGetActivationProgress(ev.Board, piece, out int c, out int r) || r <= 0) continue;
            float ratio = (float)c / r;
            if (ratio > best)
            {
                best     = ratio;
                ruleName = SynergyRuleName(rule);
                req      = r;
                cur      = Mathf.Clamp(c, 0, r);
            }
        }
        return best >= 0f;
    }

    static string SynergyRuleName(SynergyRule rule)
        => !string.IsNullOrEmpty(rule.displayName) ? rule.displayName : rule.name;

    // Pop easings: Back overshoots slightly (UI pop), Cubic just decelerates.
    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float xm = x - 1f;
        return 1f + c3 * xm * xm * xm + c1 * xm * xm;
    }

    static float EaseOutCubic(float x)
    {
        float xm = 1f - x;
        return 1f - xm * xm * xm;
    }

    void DrawTurretStats(PlacedBlockInstance ins)
    {
        var turret = ins.visualObject.GetComponentInChildren<TurretController>();
        if (turret == null)
        {
            GUILayout.Label("(turret stats unavailable)", _panelLabel);
            return;
        }

        float fireRate = turret.fireInterval > 0.0001f ? 1f / turret.fireInterval : 0f;

        PanelRow("Damage",    turret.bulletDamage.ToString());
        PanelRow("Fire rate", fireRate.ToString("0.0") + "/s");

        if (turret.mode == TurretController.Mode.Slow)
        {
            PanelRow("Slow to",  Mathf.RoundToInt(turret.slowMultiplier * 100f) + "% spd");
            PanelRow("Duration", turret.slowDuration.ToString("0.#") + "s");
        }
        else if (turret.mode == TurretController.Mode.Aoe)
        {
            PanelRow("AOE radius", turret.aoeRadius.ToString("0.#"));
        }
    }

    void DrawPanelButtons(PlacedBlockInstance ins, bool isTurret)
    {
        bool combatLocked = GameFlowManager.Instance != null
            && GameFlowManager.Instance.phase == GamePhase.Running
            && !isTurret;

        GUI.enabled = !combatLocked;

        int refund = ComputeSellRefund(ins);

        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Pick up", _panelButton,GUILayout.Height(34f),GUILayout.ExpandWidth(true)))
        {
            _panelPickUpRequested = true;
        }

        GUILayout.Space(8f);

        var prevBg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.86f, 0.36f, 0.32f);

        if (GUILayout.Button($"Sell +{refund}",_panelButton, GUILayout.Height(34f),GUILayout.ExpandWidth(true)))
        {
            _panelSellRequested = true;
        }

        GUI.backgroundColor = prevBg;

        GUILayout.EndHorizontal();

        GUI.enabled = true;

        if (combatLocked)
            GUILayout.Label("Locked during combat", _panelLabel);
    }

    void PanelRow(string label, string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, _panelLabel, GUILayout.Width(72f));
        GUILayout.Label(value, _panelValue);
        GUILayout.EndHorizontal();
    }

    void PanelDivider()
    {
        GUILayout.Space(4f);
        Rect r = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true), GUILayout.Height(1f));
        var prev = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.18f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = prev;
        GUILayout.Space(4f);
    }

    void EnsurePanelStyles()
    {
        if (_panelBox != null) return;

        _panelBox = new GUIStyle(GUI.skin.box)
        {
            padding   = new RectOffset(12, 12, 10, 10),
            alignment = TextAnchor.UpperLeft,
        };

        _panelTitle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 22,
            fontStyle = FontStyle.Bold,
            wordWrap  = true,
        };
        _panelTitle.normal.textColor = Color.white;

        _panelLabel = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            wordWrap = true,
        };
        _panelLabel.normal.textColor = new Color(0.78f, 0.78f, 0.80f);

        _panelValue = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 16,
            fontStyle = FontStyle.Bold,
            wordWrap  = true,
        };
        _panelValue.normal.textColor = Color.white;

        _panelProgress = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 21,
            fontStyle = FontStyle.Bold,
            wordWrap  = true,
        };
        _panelProgress.normal.textColor = Color.white;

        _panelButton = new GUIStyle(GUI.skin.button)
        {
            fontSize  = 16,
            fontStyle = FontStyle.Bold,
        };
    }

}
