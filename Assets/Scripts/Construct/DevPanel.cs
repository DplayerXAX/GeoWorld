using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Dev / playtest cheat panel. Toggle with F1. Drop on any GameObject in the
// scene. Resolves dependencies via existing singletons (no inspector wiring).
public class DevPanel : MonoBehaviour
{
    [Header("Hotkeys")]
    public KeyCode toggleKey    = KeyCode.F1;
    public KeyCode slowMoKey    = KeyCode.F2;
    public KeyCode normalKey    = KeyCode.F3;
    public KeyCode fastFwdKey   = KeyCode.F4;
    public KeyCode quickSaveKey = KeyCode.F5;
    public KeyCode browseKey    = KeyCode.F6;
    public KeyCode quickLoadKey = KeyCode.F9;

    [Header("Cheat amounts")]
    public int blockCurrencyGrant  = 100;
    public int turretCurrencyGrant = 10;

    bool     _visible;
    bool     _browseVisible;
    string   _lastStatus = "";
    GUIStyle _btn;
    GUIStyle _label;
    GUIStyle _header;

    // Browse state
    List<string> _entries = new();
    Vector2 _browseScroll;
    readonly Dictionary<string, Texture2D> _thumbCache = new();

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))    _visible = !_visible;
        if (Input.GetKeyDown(slowMoKey))    Time.timeScale = 0.25f;
        if (Input.GetKeyDown(normalKey))    Time.timeScale = 1f;
        if (Input.GetKeyDown(fastFwdKey))   Time.timeScale = 4f;
        if (Input.GetKeyDown(quickSaveKey)) QuickSave();
        if (Input.GetKeyDown(quickLoadKey)) QuickLoad();
        if (Input.GetKeyDown(browseKey))    ToggleBrowse();
    }

    void OnDisable()
    {
        foreach (var t in _thumbCache.Values)
            if (t != null) Destroy(t);
        _thumbCache.Clear();
    }

    void QuickSave()
    {
        var path = SnapshotManager.QuickSave();
        _lastStatus = path != null ? "saved: " + Path.GetFileName(path) : "save failed";
        if (_browseVisible) RefreshEntries();
    }

    void QuickLoad()
    {
        bool ok = SnapshotManager.QuickLoad();
        _lastStatus = ok ? "loaded latest" : "no snapshot";
    }

    void ToggleBrowse()
    {
        _browseVisible = !_browseVisible;
        if (_browseVisible) RefreshEntries();
    }

    void RefreshEntries()
    {
        _entries = SnapshotManager.ListAll();
    }

    void OnGUI()
    {
        if (!_visible) return;
        EnsureStyles();

        DrawMainPanel();
        if (_browseVisible) DrawBrowsePanel();
    }

    // ── Main panel ───────────────────────────────────────────────────────────
    void DrawMainPanel()
    {
        const float w = 240f;
        var rect = new Rect(10, 10, w, 560);
        GUILayout.BeginArea(rect, GUI.skin.box);

        GUILayout.Label("DEV PANEL  (F1 toggle)", _header);
        GUILayout.Space(6);

        GUILayout.Label("Resources", _label);
        if (GUILayout.Button($"+{blockCurrencyGrant} Block Currency", _btn))
            ResourceManager.Instance?.RefundBlock(blockCurrencyGrant);
        if (GUILayout.Button($"+{turretCurrencyGrant} Turret Currency", _btn))
            ResourceManager.Instance?.AddTurretCurrency(turretCurrencyGrant);

        GUILayout.Space(6);

        GUILayout.Label("World", _label);
        if (GUILayout.Button("Clear All Blocks", _btn))
            ClearAllBlocks();
        if (GUILayout.Button("Force End Run", _btn))
            GameFlowManager.Instance?.AbortRun();
        if (GUILayout.Button("Add Next Endpoint", _btn))
            GameFlowManager.Instance?.AddNextEndpoint();

        GUILayout.Space(6);

        GUILayout.Label($"Time scale: {Time.timeScale:F2}x  (F2/F3/F4)", _label);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("0.25x", _btn)) Time.timeScale = 0.25f;
        if (GUILayout.Button("1x",    _btn)) Time.timeScale = 1f;
        if (GUILayout.Button("4x",    _btn)) Time.timeScale = 4f;
        GUILayout.EndHorizontal();

        GUILayout.Space(6);

        GUILayout.Label("Snapshot  (F5 save / F9 load latest)", _label);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save",        _btn)) QuickSave();
        if (GUILayout.Button("Load Latest", _btn)) QuickLoad();
        GUILayout.EndHorizontal();
        if (GUILayout.Button(_browseVisible ? "Hide Browser (F6)" : "Browse... (F6)", _btn))
            ToggleBrowse();
        if (!string.IsNullOrEmpty(_lastStatus))
            GUILayout.Label(_lastStatus, _label);

        GUILayout.Space(8);

        var rm = ResourceManager.Instance;
        var gfm = GameFlowManager.Instance;
        if (rm != null)
            GUILayout.Label($"BC: {rm.BlockCurrency}   TC: {rm.TurretCurrency}", _label);
        if (gfm != null)
        {
            GUILayout.Label($"Phase: {gfm.phase}", _label);
            GUILayout.Label($"Round: {gfm.RoundIndex}   Loops: {gfm.ActiveLoopCount}", _label);
            GUILayout.Label($"Runs since EP: {gfm.RunsSinceLastEndpoint}", _label);
        }

        GUILayout.EndArea();
    }

    // ── Browse panel ─────────────────────────────────────────────────────────
    void DrawBrowsePanel()
    {
        const float w = 340f;
        const float h = 560f;
        var rect = new Rect(260, 10, w, h);
        GUILayout.BeginArea(rect, GUI.skin.box);

        GUILayout.BeginHorizontal();
        GUILayout.Label($"Snapshots ({_entries.Count})", _header);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("↻", _btn, GUILayout.Width(28))) RefreshEntries();
        GUILayout.EndHorizontal();
        GUILayout.Space(4);

        _browseScroll = GUILayout.BeginScrollView(_browseScroll);

        if (_entries.Count == 0)
            GUILayout.Label("(no snapshots — F5 to save one)", _label);

        string toDelete = null;

        foreach (var jsonPath in _entries)
        {
            GUILayout.BeginHorizontal(GUI.skin.box);

            // Thumbnail
            var pngPath = Path.ChangeExtension(jsonPath, ".png");
            var thumb   = GetThumb(pngPath);
            if (thumb != null)
                GUILayout.Label(thumb, GUILayout.Width(80), GUILayout.Height(80));
            else
                GUILayout.Box("no\nthumb", _label, GUILayout.Width(80), GUILayout.Height(80));

            GUILayout.BeginVertical();
            var fname = Path.GetFileNameWithoutExtension(jsonPath);
            GUILayout.Label(fname.Length > 28 ? fname.Substring(0, 28) + "…" : fname, _label);
            GUILayout.Label(File.GetLastWriteTime(jsonPath).ToString("MM-dd HH:mm:ss"), _label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Load", _btn))
            {
                var snap = SnapshotManager.LoadFromFile(jsonPath);
                if (snap != null) { SnapshotManager.Restore(snap); _lastStatus = "loaded " + fname; }
            }
            if (GUILayout.Button("Del", _btn, GUILayout.Width(40)))
                toDelete = jsonPath;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();

        if (toDelete != null)
        {
            SnapshotManager.Delete(toDelete);
            var pngPath = Path.ChangeExtension(toDelete, ".png");
            if (_thumbCache.TryGetValue(pngPath, out var t)) { if (t != null) Destroy(t); _thumbCache.Remove(pngPath); }
            RefreshEntries();
            _lastStatus = "deleted " + Path.GetFileNameWithoutExtension(toDelete);
        }
    }

    Texture2D GetThumb(string pngPath)
    {
        if (_thumbCache.TryGetValue(pngPath, out var cached)) return cached;
        if (!File.Exists(pngPath)) { _thumbCache[pngPath] = null; return null; }

        var bytes = File.ReadAllBytes(pngPath);
        var tex = new Texture2D(2, 2);
        if (!tex.LoadImage(bytes)) { Destroy(tex); _thumbCache[pngPath] = null; return null; }
        _thumbCache[pngPath] = tex;
        return tex;
    }

    void EnsureStyles()
    {
        if (_btn != null) return;
        _btn   = new GUIStyle(GUI.skin.button) { fontSize = 12 };
        _label = new GUIStyle(GUI.skin.label)  { fontSize = 12 };
        _label.normal.textColor = Color.white;
        _header = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
        _header.normal.textColor = new Color(0.6f, 0.95f, 1f);
    }

    static void ClearAllBlocks()
    {
        var grid = GameFlowManager.Instance?.gridSystem;
        if (grid == null) return;

        foreach (var ins in grid.GetAllInstances())
            grid.RemoveInstance(ins);

        PathFlowManager.Instance?.ClearAll();
        LoopManager.Instance?.ClearAllLoops();
        PlacementController.Instance?.ClearUndoHistory();
        GameFlowManager.Instance?.EvaluateGrid();
    }
}
