using System.Collections.Generic;
using UnityEngine;

// Two expandable edge panels for the gameplay HUD (IMGUI, constructivist style):
//   • LEFT  — active synergies (live from SynergyEvaluator).
//   • RIGHT — keyboard controls (editable list).
// Click the edge handle to slide a panel open / shut. Auto-spawns; only draws in
// gameplay (GameFlowManager present) and hides while the settings overlay is open.
[DisallowMultipleComponent]
public class HudSidePanels : MonoBehaviour
{
    [System.Serializable]
    public class Control { public string key; public string action; }

    public List<Control> controls = new();

    // Set true while the cursor is over an open panel / handle, so world clicks
    // (placement) don't fire underneath. Read by PlacementController.
    public static bool PointerOver;

    bool  _synOpen, _ctrlOpen;
    float _synT, _ctrlT;
    GUIStyle _title, _row, _key, _handle;
    float _builtScale = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Spawn()
    {
        if (FindFirstObjectByType<HudSidePanels>() != null) return;
        var go = new GameObject("HudSidePanels");
        DontDestroyOnLoad(go);
        go.AddComponent<HudSidePanels>();
    }

    void Awake() => EnsureDefaults();

    void EnsureDefaults()
    {
        if (controls.Count > 0) return;
        void A(string k, string a) => controls.Add(new Control { key = k, action = a });
        A("W A S D", "Move block");
        A("Q / E",   "Raise / lower");
        A("1 / 2 / 3", "Rotate block");
        A("LMB",     "Select / place");
        A("Space",   "Start wave");
        A("RMB drag","Rotate camera");
        A("Scroll",  "Zoom");
        A("Esc",     "Pause / settings");
    }

    void Update()
    {
        float k = 1f - Mathf.Exp(-12f * Time.deltaTime);
        _synT  = Mathf.Lerp(_synT,  _synOpen  ? 1f : 0f, k);
        _ctrlT = Mathf.Lerp(_ctrlT, _ctrlOpen ? 1f : 0f, k);
    }

    void OnGUI()
    {
        if (GameFlowManager.Instance == null || SettingsScreen.Open) return;
        float s = Mathf.Max(0.5f, Screen.height / 1080f);
        Build(s);
        PointerOver = false;

        DrawLeft(s);
        DrawRight(s);
    }

    // ── Left: synergies ──────────────────────────────────────────────────────────

    void DrawLeft(float s)
    {
        float pw = 280f * s, ph = 400f * s, hw = 34f * s, hh = 150f * s;
        float py = (Screen.height - ph) * 0.5f;
        float px = -pw + pw * _synT;

        if (_synT > 0.01f) PanelBody(new Rect(px, py, pw, ph), s, true, () =>
        {
            GUI.Label(new Rect(px + 22f * s, py + 16f * s, pw, 34f * s), "SYNERGIES", _title);
            float y = py + 64f * s;
            var ev = SynergyEvaluator.Instance;
            int n = 0;
            if (ev != null)
                foreach (var a in ev.Actives)
                {
                    if (a?.rule == null) continue;
                    var c = GeoPalette.Accents[n % GeoPalette.Accents.Length];
                    Fill(new Rect(px + 22f * s, y + 6f * s, 16f * s, 16f * s), c);
                    _row.normal.textColor = GeoPalette.Ink;
                    string nm = string.IsNullOrEmpty(a.rule.displayName) ? a.rule.name : a.rule.displayName;
                    GUI.Label(new Rect(px + 48f * s, y, pw - 70f * s, 28f * s),
                              a.tier > 1 ? $"{nm}  ·  T{a.tier}" : nm, _row);
                    y += 32f * s; n++;
                }
            if (n == 0)
            {
                _row.normal.textColor = new Color(0.4f, 0.4f, 0.4f);
                GUI.Label(new Rect(px + 22f * s, y, pw - 40f * s, 28f * s), "No active synergies", _row);
            }
        });

        // Handle on the panel's right edge.
        var handle = new Rect(px + pw, py + (ph - hh) * 0.5f, hw, hh);
        if (Handle(handle, "SYNERGIES", s, false)) _synOpen = !_synOpen;
        Hit(new Rect(px, py, pw + hw, ph), _synT);
    }

    // ── Right: controls ──────────────────────────────────────────────────────────

    void DrawRight(float s)
    {
        float pw = 300f * s, ph = 440f * s, hw = 34f * s, hh = 150f * s;
        float py = (Screen.height - ph) * 0.5f;
        float px = Screen.width - pw * _ctrlT;

        if (_ctrlT > 0.01f) PanelBody(new Rect(px, py, pw, ph), s, false, () =>
        {
            GUI.Label(new Rect(px + 26f * s, py + 16f * s, pw, 34f * s), "CONTROLS", _title);
            float y = py + 64f * s;
            foreach (var c in controls)
            {
                if (c == null) continue;
                _key.normal.textColor = GeoPalette.Signal;
                GUI.Label(new Rect(px + 22f * s, y, 110f * s, 28f * s), c.key, _key);
                _row.normal.textColor = GeoPalette.Ink;
                GUI.Label(new Rect(px + 140f * s, y, pw - 160f * s, 28f * s), c.action, _row);
                y += 32f * s;
            }
        });

        var handle = new Rect(px - hw, py + (ph - hh) * 0.5f, hw, hh);
        if (Handle(handle, "CONTROLS", s, true)) _ctrlOpen = !_ctrlOpen;
        Hit(new Rect(px - hw, py, pw + hw, ph), _ctrlT);
    }

    // ── Primitives ───────────────────────────────────────────────────────────────

    void PanelBody(Rect r, float s, bool leftSpine, System.Action content)
    {
        Fill(r, GeoPalette.Paper);
        Fill(new Rect(r.x, r.y, r.width, 5f * s), GeoPalette.Ink);
        float spineX = leftSpine ? r.xMax - 6f * s : r.x;
        Fill(new Rect(spineX, r.y, 6f * s, r.height), GeoPalette.Signal);
        content?.Invoke();
    }

    bool Handle(Rect r, string label, float s, bool rightSide)
    {
        if (r.Contains(Event.current.mousePosition)) PointerOver = true;
        Fill(r, GeoPalette.Ink);
        Fill(rightSide ? new Rect(r.x, r.y, 5f * s, r.height) : new Rect(r.xMax - 5f * s, r.y, 5f * s, r.height),
             GeoPalette.Signal);

        var m = GUI.matrix;
        Vector2 pivot = r.center;
        GUIUtility.RotateAroundPivot(rightSide ? 90f : -90f, pivot);
        _handle.normal.textColor = GeoPalette.Paper;
        GUI.Label(new Rect(pivot.x - r.height * 0.5f, pivot.y - r.width * 0.5f, r.height, r.width), label, _handle);
        GUI.matrix = m;

        return GUI.Button(r, GUIContent.none, GUIStyle.none);
    }

    // Mark PointerOver when the cursor sits on an open panel region.
    void Hit(Rect r, float openT)
    {
        if (openT > 0.05f && r.Contains(Event.current.mousePosition)) PointerOver = true;
    }

    static void Fill(Rect r, Color c)
    {
        Color p = GUI.color; GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = p;
    }

    void Build(float s)
    {
        if (_builtScale == s && _title != null) return;
        _builtScale = s;
        _title  = new GUIStyle { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft,   fontSize = Mathf.RoundToInt(22f * s) };
        _row    = new GUIStyle { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft,   fontSize = Mathf.RoundToInt(16f * s) };
        _key    = new GUIStyle { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft,   fontSize = Mathf.RoundToInt(15f * s) };
        _handle = new GUIStyle { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(16f * s) };
    }
}
