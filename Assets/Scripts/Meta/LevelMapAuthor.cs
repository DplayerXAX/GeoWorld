using UnityEngine;

// Map-authoring helper for the scene where you build maps with the real
// PlacementController. Left-click keeps doing placement; RIGHT-click here picks a
// placed block to tag. Assign it a level (or mark Start / Waypoint), then Save —
// LevelMapIO captures the whole grid + tags to JSON for the level-select scene.
public class LevelMapAuthor : MonoBehaviour
{
    public LevelDatabase database;
    public string mapName = "map";

    GameObject _picked;          // visualObject of the right-clicked block
    Camera     _cam;
    GUIStyle   _btn, _label;
    Vector2    _scroll;

    void Start() => _cam = Camera.main;

    void Update()
    {
        if (Input.GetMouseButtonDown(1))
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam != null && Physics.Raycast(_cam.ScreenPointToRay(Input.mousePosition), out var hit))
                _picked = ResolveBlockRoot(hit.collider);
        }
    }

    static GameObject ResolveBlockRoot(Collider col)
    {
        var grid = GridSystem.instance;
        if (grid == null || col == null) return null;
        foreach (var ins in grid.GetAllInstances())
        {
            var vo = ins?.visualObject;
            if (vo != null && (col.transform == vo.transform || col.transform.IsChildOf(vo.transform)))
                return vo;
        }
        return null;
    }

    LevelNodeTag Tag()
    {
        var t = _picked.GetComponent<LevelNodeTag>();
        return t != null ? t : _picked.AddComponent<LevelNodeTag>();
    }

    void OnGUI()
    {
        EnsureStyles();
        GUILayout.BeginArea(new Rect(12f, 12f, 250f, Screen.height - 24f), GUIContent.none, GUI.skin.box);

        GUILayout.Label("LEVEL MAP AUTHOR", _label);
        GUILayout.Label("Right-click a block to tag it.", _label);
        GUILayout.Label(_picked == null ? "(none picked)" : $"Picked: {DescribeTag()}", _label);

        if (_picked != null)
        {
            GUILayout.Space(4f);
            if (GUILayout.Button("Mark START", _btn))   { var t = Tag(); t.isStart = true;  t.level = null; }
            if (GUILayout.Button("Make Waypoint", _btn)) { var t = Tag(); t.isStart = false; t.level = null; }
            if (GUILayout.Button("Clear tag", _btn))
            { var t = _picked.GetComponent<LevelNodeTag>(); if (t != null) Destroy(t); }

            GUILayout.Space(6f);
            GUILayout.Label("Assign level:", _label);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(220f));
            if (database != null && database.levels != null)
                foreach (var lv in database.levels)
                {
                    if (lv == null) continue;
                    string name = string.IsNullOrEmpty(lv.displayName) ? lv.levelId : lv.displayName;
                    if (GUILayout.Button(name, _btn)) { var t = Tag(); t.level = lv; t.isStart = false; }
                }
            GUILayout.EndScrollView();
        }

        GUILayout.FlexibleSpace();
        if (GUILayout.Button($"SAVE MAP  ({mapName})", _btn, GUILayout.Height(40f)))
            LevelMapIO.Save(LevelMapIO.CaptureCurrent(mapName), mapName);
        if (GUILayout.Button("LOAD MAP (edit)", _btn, GUILayout.Height(28f)))
            LoadForEdit();

        GUILayout.EndArea();
    }

    // Rebuild a saved map back into THIS scene via the real placement path, so you
    // can keep editing it across sessions. Load into a fresh grid (occupied cells
    // are skipped to avoid clashes).
    void LoadForEdit()
    {
        var data = LevelMapIO.Load(mapName);
        var pc   = PlacementController.Instance;
        var grid = GridSystem.instance;
        if (data == null || pc == null || grid == null) return;

        foreach (var node in data.nodes)
        {
            if (node.cells == null || node.cells.Length == 0) continue;

            bool clash = false;
            foreach (var c in node.cells) if (grid.IsOccupied(c)) { clash = true; break; }
            if (clash) continue;

            BlockData bd = null;
            if (System.Enum.TryParse(node.blockTypeName, out BlockType bt)) bd = pc.FindBlockData(bt);
            if (bd == null) { Debug.LogWarning($"[LevelMap] can't resolve block '{node.blockTypeName}', skipped."); continue; }

            var ins = pc.PlaceBlockDirect(bd, node.cells, Quaternion.identity, node.color);
            if (ins?.visualObject != null && (!string.IsNullOrEmpty(node.levelId) || node.isStart))
            {
                var tag = ins.visualObject.AddComponent<LevelNodeTag>();
                tag.level   = database != null ? database.Find(node.levelId) : null;
                tag.isStart = node.isStart;
            }
        }
    }

    string DescribeTag()
    {
        var t = _picked.GetComponent<LevelNodeTag>();
        if (t == null) return "untagged";
        if (t.isStart) return "START";
        return t.level != null ? t.level.levelId : "waypoint";
    }

    void EnsureStyles()
    {
        if (_btn != null) return;
        _btn   = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold };
        _label = new GUIStyle(GUI.skin.label)  { fontSize = 13, wordWrap = true };
        _label.normal.textColor = Color.white;
    }
}
