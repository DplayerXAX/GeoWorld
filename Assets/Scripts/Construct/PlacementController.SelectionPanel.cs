using System.Collections.Generic;
using UnityEngine;

public partial class PlacementController
{
    // =========================
    // SELECTION INFO PANEL
    // =========================

    [Tooltip("World-space UGUI selection panel (title / stats / pick-up / sell). Wire it and the IMGUI panel below is skipped.")]
    public BlockInfoPanel infoPanel;
    [Tooltip("Separate world-space UGUI upgrade panel (turret upgrade paths).")]
    public UpgradePanel upgradePanel;

    Camera _hudCam;   // cached main camera for projecting the selected block to screen

    bool PointerOverInfoPanel()
        => infoPanel != null
        && UnityEngine.EventSystems.EventSystem.current != null
        && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

    // Drives the UGUI panel (call every frame from Update). Mirrors DrawSelectionPanel's
    // logic but pushes plain strings instead of laying out IMGUI.
    void UpdateInfoPanel()
    {
        if (infoPanel == null) return;

        // Spawn-point forecast (same gate as the IMGUI start panel).
        if (mode == PlacementMode.Select && _selectedEndpoint != null && _selectedEndpointIsStart
            && (GameFlowManager.Instance == null || GameFlowManager.Instance.phase != GamePhase.Running))
        {
            infoPanel.ShowReadonly(_selectedEndpoint.transform.position, "Spawn Point", BuildStartBody());
            upgradePanel?.Hide();
            return;
        }

        var ins = selectedInstance;
        if (mode != PlacementMode.Select || ins == null || ins.visualObject == null || ins.data == null)
        {
            infoPanel.Hide();
            upgradePanel?.Hide();
            return;
        }

        bool   isTurret = TurretTypes.Is(ins.data.blockType);
        string title    = isTurret ? TurretTypes.DisplayName(ins.data.blockType) : ins.data.DisplayName;
        string body     = BuildInfoBody(ins, isTurret);

        bool combatLocked = GameFlowManager.Instance != null
            && GameFlowManager.Instance.phase == GamePhase.Running && !isTurret;
        int refund = ComputeSellRefund(ins);

        Vector3 worldPos = ins.visualObject.transform.position;

        // Selection panel: title / stats / pick-up / sell.
        infoPanel.Show(worldPos, title, body, !combatLocked, $"Sell +{refund}",
            () => _panelPickUpRequested = true,
            () => _panelSellRequested   = true);

        // Separate upgrade panel.
        if (upgradePanel != null)
        {
            BuildUpgradeButtons(ins,
                out string upgradeAText, out bool canUpgradeA, out System.Action onUpgradeA,
                out string upgradeBText, out bool canUpgradeB, out System.Action onUpgradeB);

            bool hasUpgrades = !string.IsNullOrEmpty(upgradeAText) || !string.IsNullOrEmpty(upgradeBText);
            if (hasUpgrades && !combatLocked)
                upgradePanel.Show(worldPos, upgradeAText, canUpgradeA, onUpgradeA,
                                            upgradeBText, canUpgradeB, onUpgradeB);
            else
                upgradePanel.Hide();
        }
    }

    string BuildInfoBody(PlacedBlockInstance ins, bool isTurret)
    {
        var sb = new System.Text.StringBuilder();

        if (isTurret)
        {
            var t = ins.visualObject.GetComponentInChildren<TurretController>();
            if (t == null) { sb.Append("(turret stats unavailable)"); return sb.ToString(); }

            float fireRate = t.fireInterval > 0.0001f ? 1f / t.fireInterval : 0f;
            sb.AppendLine($"Damage     <b>{t.bulletDamage}</b>");
            sb.AppendLine($"Range      <b>{t.attackRange:0.#}</b>");
            sb.AppendLine($"Fire rate  <b>{fireRate:0.0}/s</b>");
            sb.AppendLine($"Bullets    <b>{Mathf.Max(1, t.projectilesPerShot)}</b>");

            if (t.mode == TurretController.Mode.Slow)
            {
                sb.AppendLine($"Slow to    <b>{Mathf.RoundToInt(t.slowMultiplier * 100f)}% spd</b>");
                sb.AppendLine($"Duration   <b>{t.slowDuration:0.#}s</b>");
            }
            else if (t.mode == TurretController.Mode.Aoe)
            {
                sb.AppendLine($"AOE radius <b>{t.aoeRadius:0.#}</b>");
                sb.AppendLine();
                sb.AppendLine($"Fire <b>{t.GetAoePathLevel(AoeTurretUpgradePath.Fire)}/3</b>" +
                              $"   Gravity <b>{t.GetAoePathLevel(AoeTurretUpgradePath.Gravity)}/3</b>");
            }
            else if (t.mode == TurretController.Mode.Basic)
            {
                sb.AppendLine();
                sb.AppendLine($"Power <b>{t.GetBasicPathLevel(BasicTurretUpgradePath.Power)}/3</b>" +
                              $"   Burst <b>{t.GetBasicPathLevel(BasicTurretUpgradePath.Burst)}/3</b>");
            }
            return sb.ToString().TrimEnd();
        }

        // Block: synergy line (themed) + flavour description.
        if (ins.color == BlockColor.None) return "Synergy  None";

        bool   hasProg = TryGetSynergyProgress(ins, out string ruleName, out int cur, out int req, out bool active);
        string name    = (hasProg && !string.IsNullOrEmpty(ruleName)) ? ruleName : ins.color.ToString();
        string prog    = hasProg ? (req > 0 ? $"  {cur}/{req}" : "  active") : "";
        string hex     = ColorUtility.ToHtmlStringRGB(BlockColorPalette.Get(ins.color));
        sb.AppendLine($"<color=#{hex}><b>{name}</b>{prog}</color>");

        string desc = BlockColorPalette.Description(ins.color);
        if (!string.IsNullOrEmpty(desc)) sb.Append(desc);
        return sb.ToString().TrimEnd();
    }

    void BuildUpgradeButtons(
        PlacedBlockInstance ins,
        out string upgradeAText,
        out bool canUpgradeA,
        out System.Action onUpgradeA,
        out string upgradeBText,
        out bool canUpgradeB,
        out System.Action onUpgradeB)
    {
        upgradeAText = upgradeBText = null;
        canUpgradeA = canUpgradeB = false;
        onUpgradeA = onUpgradeB = null;

        var turret = ins?.visualObject != null
            ? ins.visualObject.GetComponentInChildren<TurretController>()
            : null;
        if (turret == null) return;

        if (turret.mode == TurretController.Mode.Basic)
        {
            canUpgradeA = turret.CanUpgradeBasicPath(BasicTurretUpgradePath.Power, out _);
            canUpgradeB = turret.CanUpgradeBasicPath(BasicTurretUpgradePath.Burst, out _);
            upgradeAText = $"Power: {turret.NextBasicUpgradeDescription(BasicTurretUpgradePath.Power)}";
            upgradeBText = $"Burst: {turret.NextBasicUpgradeDescription(BasicTurretUpgradePath.Burst)}";
            onUpgradeA = () => _panelPowerUpgradeRequested = true;
            onUpgradeB = () => _panelBurstUpgradeRequested = true;
            return;
        }

        if (turret.mode == TurretController.Mode.Aoe)
        {
            canUpgradeA = turret.CanUpgradeAoePath(AoeTurretUpgradePath.Fire, out _);
            canUpgradeB = turret.CanUpgradeAoePath(AoeTurretUpgradePath.Gravity, out _);
            upgradeAText = $"Fire: {turret.NextAoeUpgradeDescription(AoeTurretUpgradePath.Fire)}";
            upgradeBText = $"Gravity: {turret.NextAoeUpgradeDescription(AoeTurretUpgradePath.Gravity)}";
            onUpgradeA = () => _panelAoeFireUpgradeRequested = true;
            onUpgradeB = () => _panelAoeGravityUpgradeRequested = true;
        }
    }

    string BuildStartBody()
    {
        var gfm   = GameFlowManager.Instance;
        int round = gfm != null ? gfm.RoundIndex : 0;
        if (gfm != null && _startForecastRound != round)
        {
            _startForecast      = gfm.GetNextWaveForecast();
            _startForecastRound = round;
        }
        var fc = _startForecast;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Wave     <b>{fc.waveNumber}</b>");
        sb.AppendLine($"Enemies  <b>{(fc.valid ? fc.totalCount.ToString() : "—")}</b>");
        sb.AppendLine();
        sb.AppendLine("Incoming");
        if (fc.valid && fc.groups != null && fc.groups.Count > 0)
            foreach (var g in fc.groups) sb.AppendLine($"{g.name}  ×{g.count}");
        else sb.Append("Composition unknown");
        return sb.ToString().TrimEnd();
    }

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
        if (infoPanel != null) { _panelRect = default; return; }   // UGUI panel takes over

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

        float s = UiScale.Get();
        EnsurePanelStyles();                       // FIXED reference sizes; scaled below via GUI.matrix

        bool  isTurret = TurretTypes.Is(ins.data.blockType);
        // Everything below is in REFERENCE (unscaled) units. The whole panel is
        // scaled by `s` with one GUI.matrix transform, so it stays perfectly
        // proportional at any window size (no per-element font rounding drift).
        float panelW = 240f;
        float panelH = (_selPanelFor == ins && _selPanelHeight > 1f)
            ? _selPanelHeight
            : EstimatePanelHeight(ins, isTurret);

        float margin  = 12f * s;
        float screenW = panelW * s;                // on-screen footprint
        float screenH = panelH * s;

        // Anchor the panel to the block's on-screen position so it reads as a
        // spatial label that tracks the block as the camera moves. Falls back to
        // the right edge if the block is off / behind the camera.
        if (_hudCam == null) _hudCam = Camera.main;
        float x, y;
        Vector3 sp = _hudCam != null
            ? _hudCam.WorldToScreenPoint(ins.visualObject.transform.position)
            : new Vector3(-1f, -1f, -1f);
        if (sp.z <= 0f)
        {
            x = Screen.width - screenW - margin;
            y = (Screen.height - screenH) * 0.5f;
        }
        else
        {
            x = sp.x + 32f;                                  // just right of the block
            y = (Screen.height - sp.y) - screenH * 0.5f;     // GUI y, centred on block
            if (x + screenW + margin > Screen.width)
                x = sp.x - screenW - 32f;                    // flip to the left if it'd clip
            x = Mathf.Clamp(x, margin, Screen.width  - screenW - margin);
            y = Mathf.Clamp(y, margin, Screen.height - screenH - margin);
        }
        _panelRect = new Rect(x, y, screenW, screenH);       // hit-test in screen px

        // Pop-in bounce on selection change.
        if (_panelAnimFor != ins) { _panelAnimStart = Time.time; _panelAnimFor = ins; }
        float pt  = selectionPopDuration > 1e-4f ? (Time.time - _panelAnimStart) / selectionPopDuration : 1f;
        float pop = Mathf.Lerp(0.6f, 1f, EaseOutBack(Mathf.Clamp01(pt)));

        // One uniform scale (resolution × pop) around the panel's screen centre.
        float cx = x + screenW * 0.5f, cy = y + screenH * 0.5f;
        Matrix4x4 prevGuiMatrix = GUI.matrix;
        GUIUtility.ScaleAroundPivot(new Vector2(s * pop, s * pop), new Vector2(cx, cy));

        GUILayout.BeginArea(new Rect(cx - panelW * 0.5f, cy - panelH * 0.5f, panelW, panelH));
        GUILayout.BeginVertical(_panelBox, GUILayout.Width(panelW));

        GUILayout.Label(isTurret
            ? TurretTypes.DisplayName(ins.data.blockType)
            : ins.data.DisplayName, _panelTitle);

        PanelDivider();

        if (isTurret)
        {
            DrawTurretStats(ins);
            DrawBasicTurretUpgrades(ins);
            DrawAoeTurretUpgrades(ins);
        }
        else
        {
            DrawBlockStats(ins);
        }

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
        float s = UiScale.Get();
        EnsurePanelStyles();

        var gfm   = GameFlowManager.Instance;
        int round = gfm != null ? gfm.RoundIndex : 0;
        if (gfm != null && _startForecastRound != round)
        {
            _startForecast      = gfm.GetNextWaveForecast();
            _startForecastRound = round;
        }
        var fc = _startForecast;

        // Reference (unscaled) units, scaled uniformly by GUI.matrix below.
        float panelW  = 236f;
        float panelH  = EstimateStartPanelHeight(fc);
        float margin  = 12f * s;
        float screenW = panelW * s, screenH = panelH * s;
        float x = Screen.width  - screenW - margin;
        float y = (Screen.height - screenH) * 0.5f;
        _panelRect = new Rect(x, y, screenW, screenH);

        float cx = x + screenW * 0.5f, cy = y + screenH * 0.5f;
        Matrix4x4 prevGuiMatrix = GUI.matrix;
        GUIUtility.ScaleAroundPivot(new Vector2(s, s), new Vector2(cx, cy));
        GUILayout.BeginArea(new Rect(cx - panelW * 0.5f, cy - panelH * 0.5f, panelW, panelH), GUIContent.none, _panelBox);

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
        GUI.matrix = prevGuiMatrix;
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
                rows = 3;   // Damage, Range, Fire rate
                if      (t.mode == TurretController.Mode.Slow) rows += 2;
                else if (t.mode == TurretController.Mode.Aoe)
                {
                    rows += 1;
                    extra += 148f;
                }
                else
                {
                    rows += 1;         // Bullets
                    extra += 148f;     // Basic upgrade branch controls
                }
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
        PanelRow("Range",     turret.attackRange.ToString("0.#"));
        PanelRow("Fire rate", fireRate.ToString("0.0") + "/s");
        PanelRow("Bullets",   Mathf.Max(1, turret.projectilesPerShot).ToString());

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

    void DrawBasicTurretUpgrades(PlacedBlockInstance ins)
    {
        var turret = ins.visualObject.GetComponentInChildren<TurretController>();
        if (turret == null || turret.mode != TurretController.Mode.Basic) return;

        PanelDivider();
        GUILayout.Label("Upgrades", _panelValue);
        DrawBasicUpgradeBranch(turret, BasicTurretUpgradePath.Power);
        GUILayout.Space(4f);
        DrawBasicUpgradeBranch(turret, BasicTurretUpgradePath.Burst);
    }

    void DrawBasicUpgradeBranch(TurretController turret, BasicTurretUpgradePath path)
    {
        int level = turret.GetBasicPathLevel(path);
        string name = path == BasicTurretUpgradePath.Power ? "Power" : "Burst";
        string next = turret.NextBasicUpgradeDescription(path);

        GUILayout.BeginHorizontal();
        GUILayout.Label($"{name} {level}/3", _panelValue, GUILayout.Width(88f));
        GUILayout.Label(next, _panelLabel);
        GUILayout.EndHorizontal();

        bool canUpgrade = turret.CanUpgradeBasicPath(path, out string reason);
        bool prevEnabled = GUI.enabled;
        GUI.enabled = canUpgrade;

        string buttonText = canUpgrade ? $"Upgrade {name}" : (level >= 3 ? "Max" : "Locked");
        if (GUILayout.Button(buttonText, _panelButton, GUILayout.Height(28f)))
        {
            if (path == BasicTurretUpgradePath.Power)
                _panelPowerUpgradeRequested = true;
            else
                _panelBurstUpgradeRequested = true;
        }

        GUI.enabled = prevEnabled;
        if (!canUpgrade && level < 3 && !string.IsNullOrEmpty(reason))
            GUILayout.Label(reason, _panelLabel);
    }

    void DrawAoeTurretUpgrades(PlacedBlockInstance ins)
    {
        var turret = ins.visualObject.GetComponentInChildren<TurretController>();
        if (turret == null || turret.mode != TurretController.Mode.Aoe) return;

        PanelDivider();
        GUILayout.Label("Upgrades", _panelValue);
        DrawAoeUpgradeBranch(turret, AoeTurretUpgradePath.Fire);
        GUILayout.Space(4f);
        DrawAoeUpgradeBranch(turret, AoeTurretUpgradePath.Gravity);
    }

    void DrawAoeUpgradeBranch(TurretController turret, AoeTurretUpgradePath path)
    {
        int level = turret.GetAoePathLevel(path);
        string name = path == AoeTurretUpgradePath.Fire ? "Fire" : "Gravity";
        string next = turret.NextAoeUpgradeDescription(path);

        GUILayout.BeginHorizontal();
        GUILayout.Label($"{name} {level}/3", _panelValue, GUILayout.Width(88f));
        GUILayout.Label(next, _panelLabel);
        GUILayout.EndHorizontal();

        bool canUpgrade = turret.CanUpgradeAoePath(path, out string reason);
        bool prevEnabled = GUI.enabled;
        GUI.enabled = canUpgrade;

        string buttonText = canUpgrade ? $"Upgrade {name}" : (level >= 3 ? "Max" : "Locked");
        if (GUILayout.Button(buttonText, _panelButton, GUILayout.Height(28f)))
        {
            if (path == AoeTurretUpgradePath.Fire)
                _panelAoeFireUpgradeRequested = true;
            else
                _panelAoeGravityUpgradeRequested = true;
        }

        GUI.enabled = prevEnabled;
        if (!canUpgrade && level < 3 && !string.IsNullOrEmpty(reason))
            GUILayout.Label(reason, _panelLabel);
    }

    void DrawPanelButtons(PlacedBlockInstance ins, bool isTurret)
    {
        bool combatLocked = GameFlowManager.Instance != null
            && GameFlowManager.Instance.phase == GamePhase.Running
            && !isTurret;

        GUI.enabled = !combatLocked;

        int refund = ComputeSellRefund(ins);

        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Pick up", _panelButton,GUILayout.Height(32f),GUILayout.ExpandWidth(true)))
        {
            _panelPickUpRequested = true;
        }

        GUILayout.Space(8f);

        var prevBg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.86f, 0.36f, 0.32f);

        if (GUILayout.Button($"Sell +{refund}",_panelButton, GUILayout.Height(32f),GUILayout.ExpandWidth(true)))
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
        GUILayout.Label(label, _panelLabel, GUILayout.Width(58f));
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

    // FIXED reference sizes — the whole panel is uniformly scaled by GUI.matrix in
    // Draw*Panel, so styles never need per-element scaling (which caused font-rounding
    // drift and clipping). Built once.
    void EnsurePanelStyles()
    {
        if (_panelBox != null) return;

        _panelBox = new GUIStyle(GUI.skin.box)
        {
            padding   = new RectOffset(9, 9, 8, 8),
            alignment = TextAnchor.UpperLeft,
        };

        _panelTitle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, wordWrap = true };
        _panelTitle.normal.textColor = Color.white;

        _panelLabel = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
        _panelLabel.normal.textColor = new Color(0.78f, 0.78f, 0.80f);

        _panelValue = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, wordWrap = true };
        _panelValue.normal.textColor = Color.white;

        _panelProgress = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, wordWrap = true };
        _panelProgress.normal.textColor = Color.white;

        _panelButton = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold };
    }

}
