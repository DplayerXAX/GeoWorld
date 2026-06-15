using UnityEngine;

// Sectioned settings overlay (IMGUI), constructivist styling to match the title.
// Auto-spawns a persistent instance — open from anywhere with SettingsScreen.Open
// = true (Title menu, Pause menu). Edits GameSettings, applies + saves live.
[DisallowMultipleComponent]
public class SettingsScreen : MonoBehaviour
{
    public static bool Open;

    static readonly string[] Sections    = { "AUDIO", "DISPLAY", "CONTROLS" };
    static readonly string[] FrameLabels = { "Off", "30", "60", "120", "144" };

    int      _section;
    string   _rowLabel;
    string   _drag;
    GUIStyle _h1, _tab, _label, _val, _btn;
    float    _builtScale = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Spawn()
    {
        if (FindFirstObjectByType<SettingsScreen>() != null) return;
        var go = new GameObject("SettingsScreen");
        DontDestroyOnLoad(go);
        go.AddComponent<SettingsScreen>();
    }

    void Update()
    {
        if (Open && Input.GetKeyDown(KeyCode.Escape)) Open = false;
    }

    void OnGUI()
    {
        if (!Open) return;
        float s = Mathf.Max(0.5f, Screen.height / 1080f);
        BuildStyles(s);

        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.05f, 0.05f, 0.06f, 0.78f));

        float w = Mathf.Min(1080f * s, Screen.width  * 0.86f);
        float h = Mathf.Min(700f  * s, Screen.height * 0.86f);
        var panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        Fill(panel, GeoPalette.Paper);
        Fill(new Rect(panel.x, panel.y, 12f * s, h), GeoPalette.Signal);   // left spine
        Fill(new Rect(panel.x, panel.y, w, 6f * s), GeoPalette.Ink);       // top rule

        _h1.normal.textColor = GeoPalette.Ink;
        GUI.Label(new Rect(panel.x + 34f * s, panel.y + 18f * s, w, 50f * s), "SETTINGS", _h1);

        // Section tabs (left column).
        float tabX = panel.x + 30f * s, tabY = panel.y + 90f * s, tabW = 200f * s, tabH = 46f * s;
        for (int i = 0; i < Sections.Length; i++)
        {
            var r = new Rect(tabX, tabY + i * (tabH + 8f * s), tabW, tabH);
            bool on = i == _section;
            if (on) Fill(r, GeoPalette.Ink);
            Fill(new Rect(r.x, r.y, 6f * s, tabH), on ? GeoPalette.Signal : new Color(0, 0, 0, 0.18f));
            _tab.normal.textColor = on ? GeoPalette.Paper : GeoPalette.Ink;
            GUI.Label(new Rect(r.x + 18f * s, r.y, tabW, tabH), Sections[i], _tab);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) _section = i;
        }

        var content = new Rect(panel.x + 260f * s, panel.y + 96f * s, w - 290f * s, h - 180f * s);
        switch (_section)
        {
            case 0: DrawAudio(content, s);    break;
            case 1: DrawDisplay(content, s);  break;
            default: DrawControls(content, s); break;
        }

        float by = panel.y + h - 64f * s, bw = 150f * s, bh = 44f * s;
        if (TextButton(new Rect(panel.x + 30f * s, by, bw, bh), "RESET", false))
            GameSettings.ResetDefaults();
        if (TextButton(new Rect(panel.x + w - bw - 30f * s, by, bw, bh), "BACK", true))
            Open = false;
    }

    // ── Sections ────────────────────────────────────────────────────────────────

    void DrawAudio(Rect c, float s)
    {
        float y = c.y; bool ch = false;
        GameSettings.MasterVolume = Slider(Row(c, ref y, s, "Master"), GameSettings.MasterVolume, 0f, 1f, "P0", ref ch);
        GameSettings.MusicVolume  = Slider(Row(c, ref y, s, "Music"),  GameSettings.MusicVolume,  0f, 1f, "P0", ref ch);
        GameSettings.SfxVolume    = Slider(Row(c, ref y, s, "SFX"),    GameSettings.SfxVolume,    0f, 1f, "P0", ref ch);
        if (ch) { GameSettings.ApplyAudio(); GameSettings.Save(); }
    }

    void DrawDisplay(Rect c, float s)
    {
        float y = c.y; bool ch = false;
        GameSettings.Fullscreen   = Toggle(Row(c, ref y, s, "Fullscreen"), GameSettings.Fullscreen, ref ch);
        GameSettings.VSync        = Toggle(Row(c, ref y, s, "V-Sync"),     GameSettings.VSync,      ref ch);
        GameSettings.QualityLevel = Choice(Row(c, ref y, s, "Quality"),    GameSettings.QualityLevel, QualitySettings.names, ref ch);

        int fi = Mathf.Max(0, System.Array.IndexOf(GameSettings.FrameCaps, GameSettings.FrameCap));
        fi = Choice(Row(c, ref y, s, "Frame cap"), fi, FrameLabels, ref ch);
        GameSettings.FrameCap = GameSettings.FrameCaps[Mathf.Clamp(fi, 0, GameSettings.FrameCaps.Length - 1)];

        if (ch) { GameSettings.ApplyDisplay(); GameSettings.Save(); }
    }

    void DrawControls(Rect c, float s)
    {
        float y = c.y; bool ch = false;
        GameSettings.CameraPanSpeed  = Slider(Row(c, ref y, s, "Camera pan speed"), GameSettings.CameraPanSpeed,  2f, 20f,  "0.0", ref ch);
        GameSettings.LookSensitivity = Slider(Row(c, ref y, s, "Look sensitivity"), GameSettings.LookSensitivity, 40f, 300f, "0",  ref ch);
        if (ch) { GameSettings.ApplyInput(); GameSettings.Save(); }
    }

    // ── Row + controls ───────────────────────────────────────────────────────────

    // Draws the row's label and returns the rect for its control (right side).
    Rect Row(Rect c, ref float y, float s, string label)
    {
        float rowH = 60f * s;
        var full = new Rect(c.x, y, c.width, rowH);
        y += rowH + 14f * s;

        _rowLabel = label;
        _label.normal.textColor = GeoPalette.Ink;
        GUI.Label(new Rect(full.x, full.y, full.width * 0.45f, rowH), label, _label);
        return new Rect(full.x + full.width * 0.45f, full.y + rowH * 0.18f,
                        full.width * 0.55f, rowH * 0.64f);
    }

    float Slider(Rect r, float val, float min, float max, string fmt, ref bool changed)
    {
        float t = Mathf.InverseLerp(min, max, val);
        var track = new Rect(r.x, r.center.y - 3f, r.width - 70f, 6f);
        Fill(track, new Color(0, 0, 0, 0.16f));
        Fill(new Rect(track.x, track.y, track.width * t, track.height), GeoPalette.Signal);
        float hx = track.x + track.width * t;
        Fill(new Rect(hx - 7f, track.y - 8f, 14f, 22f), GeoPalette.Gold);

        var e = Event.current;
        var hit = new Rect(track.x - 8f, track.y - 14f, track.width + 16f, 34f);
        if (e.type == EventType.MouseDown && hit.Contains(e.mousePosition)) _drag = _rowLabel;
        if (_drag == _rowLabel)
        {
            if (e.type == EventType.MouseDrag || e.type == EventType.MouseDown)
            {
                float nt = Mathf.Clamp01((e.mousePosition.x - track.x) / Mathf.Max(1f, track.width));
                float nv = Mathf.Lerp(min, max, nt);
                if (!Mathf.Approximately(nv, val)) { val = nv; changed = true; }
                e.Use();
            }
            if (e.type == EventType.MouseUp) _drag = null;
        }

        _val.normal.textColor = GeoPalette.Ink;
        GUI.Label(new Rect(r.xMax - 64f, r.y - r.height * 0.18f, 64f, r.height * 1.36f), val.ToString(fmt), _val);
        return val;
    }

    bool Toggle(Rect r, bool on, ref bool changed)
    {
        var pill = new Rect(r.x, r.center.y - 16f, 88f, 32f);
        Fill(pill, on ? GeoPalette.Signal : new Color(0, 0, 0, 0.22f));
        _val.normal.textColor = on ? GeoPalette.Paper : GeoPalette.Ink;
        var a = _val.alignment; _val.alignment = TextAnchor.MiddleCenter;
        GUI.Label(pill, on ? "ON" : "OFF", _val);
        _val.alignment = a;
        if (GUI.Button(pill, GUIContent.none, GUIStyle.none)) { on = !on; changed = true; }
        return on;
    }

    int Choice(Rect r, int idx, string[] opts, ref bool changed)
    {
        if (opts == null || opts.Length == 0) return idx;
        idx = Mathf.Clamp(idx, 0, opts.Length - 1);

        float bh = 32f;
        var lA = new Rect(r.x, r.center.y - bh * 0.5f, bh, bh);
        var rA = new Rect(r.x + 230f, r.center.y - bh * 0.5f, bh, bh);
        if (TextButton(lA, "‹", false)) { idx = (idx - 1 + opts.Length) % opts.Length; changed = true; }
        if (TextButton(rA, "›", false)) { idx = (idx + 1) % opts.Length; changed = true; }

        _val.normal.textColor = GeoPalette.Ink;
        var a = _val.alignment; _val.alignment = TextAnchor.MiddleCenter;
        GUI.Label(new Rect(lA.xMax + 6f, r.center.y - 16f, rA.x - lA.xMax - 12f, 32f), opts[idx], _val);
        _val.alignment = a;
        return idx;
    }

    bool TextButton(Rect r, string label, bool primary)
    {
        Fill(r, primary ? GeoPalette.Ink : new Color(0, 0, 0, 0.12f));
        Fill(new Rect(r.x, r.y, 5f, r.height), GeoPalette.Signal);
        _btn.normal.textColor = primary ? GeoPalette.Paper : GeoPalette.Ink;
        var a = _btn.alignment; _btn.alignment = TextAnchor.MiddleCenter;
        GUI.Label(r, label, _btn);
        _btn.alignment = a;
        return GUI.Button(r, GUIContent.none, GUIStyle.none);
    }

    static void Fill(Rect r, Color c)
    {
        Color p = GUI.color; GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = p;
    }

    void BuildStyles(float s)
    {
        if (_builtScale == s && _h1 != null) return;
        _builtScale = s;
        _h1    = new GUIStyle { fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft,    fontSize = Mathf.RoundToInt(42f * s) };
        _tab   = new GUIStyle { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft,   fontSize = Mathf.RoundToInt(21f * s) };
        _label = new GUIStyle { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft,   fontSize = Mathf.RoundToInt(21f * s) };
        _val   = new GUIStyle { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight,  fontSize = Mathf.RoundToInt(18f * s) };
        _btn   = new GUIStyle { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(19f * s) };
    }
}
