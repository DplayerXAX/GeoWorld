using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

// Scene-view layout for the level-select map. With the LevelMapController
// selected, it draws the whole world in edit mode: every level block in the map
// asset, and every decor plot's ground as the ground pass will raise it (its
// outline, its height, its colours). Nothing is built and Play is never entered.
//
// Click the dot by a plot's name to pick it (or pick it in the Inspector). The
// picked plot gets a move handle, snapped to whole cells with Y included, and a
// square on each edge to resize it. Where plots overlap, the later one gives way
// (as it does at runtime) and its label counts the cells it lost. Everything is
// undoable and saved with the scene. The grove is shown but not moved here: it
// wraps the farm and follows it.
[CustomEditor(typeof(LevelMapController))]
public class LevelMapControllerEditor : Editor
{
    const string PrefShow = "GeoWorld.MapLayout.Show";

    class PlotInfo { public MapDecorConfig cfg; public Vector3 label; public string text; }

    static readonly Vector2Int[] Side = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
    static readonly float[] SideShade = { 0.78f, 0.62f, 0.70f, 0.86f };   // a fixed light, so the steps read

    static int _picked = -1;   // index into LevelMapController.EditorPlots()
    static Material _mat;
    static Texture2D _labelBg;

    Mesh   _mesh;
    string _sig;
    Bounds _bounds;
    readonly List<PlotInfo> _plots = new();
    readonly List<(string text, Vector3 at)> _levelLabels = new();
    GUIStyle _plotStyle, _levelStyle;

    void OnDisable()
    {
        if (_mesh != null) DestroyImmediate(_mesh);
        _mesh = null;
        _sig  = null;
    }

    // ── Inspector ────────────────────────────────────────────────────────────

    public override void OnInspectorGUI()
    {
        var ctl = (LevelMapController)target;
        bool show = EditorPrefs.GetBool(PrefShow, true);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Map layout", EditorStyles.boldLabel);
        bool now = EditorGUILayout.ToggleLeft("Draw the whole map in the Scene view", show);
        if (now != show)
        {
            EditorPrefs.SetBool(PrefShow, now);
            SceneView.RepaintAll();
        }
        if (now)
        {
            EditorGUILayout.HelpBox(
                "Click the dot by a plot's name in the Scene view to pick it. Drag the arrows to move it " +
                "(whole cells, Y too); drag the squares on its edges to resize it. Where plots overlap, the later " +
                "one gives way. A raised plot needs a foundation to stand on. The grove follows the farm.", MessageType.None);

            var plots = ctl.EditorPlots();
            var names = new string[plots.Count + 1];
            names[0] = "None";
            for (int i = 0; i < plots.Count; i++) names[i + 1] = plots[i].RootName;
            int sel = EditorGUILayout.Popup("Picked plot", Mathf.Clamp(_picked + 1, 0, names.Length - 1), names) - 1;
            if (sel != _picked)
            {
                _picked = sel;
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("Frame the whole map")) Frame(ctl);
        }
        EditorGUILayout.EndVertical();

        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();
    }

    void Frame(LevelMapController ctl)
    {
        var grid = ctl.EditorGrid;
        if (grid == null) return;
        _sig = Signature(ctl, grid);
        Rebuild(ctl, grid);
        var sv = SceneView.lastActiveSceneView;
        if (sv != null) sv.Frame(_bounds, false);
    }

    // ── Scene view ───────────────────────────────────────────────────────────

    void OnSceneGUI()
    {
        if (!EditorPrefs.GetBool(PrefShow, true)) return;
        var ctl  = (LevelMapController)target;
        var grid = ctl.EditorGrid;
        if (grid == null) return;

        string sig = Signature(ctl, grid);
        if (_mesh == null || sig != _sig)
        {
            _sig = sig;
            Rebuild(ctl, grid);
        }

        if (Event.current.type == EventType.Repaint && _mesh != null && Mat() != null)
        {
            Mat().SetPass(0);
            Graphics.DrawMeshNow(_mesh, Matrix4x4.identity);
        }

        Styles();
        foreach (var (text, at) in _levelLabels) Handles.Label(at, text, _levelStyle);

        var plots = ctl.EditorPlots();
        foreach (var p in _plots)
        {
            int index = plots.IndexOf(p.cfg);
            float s = HandleUtility.GetHandleSize(p.label) * 0.07f;
            Handles.color = index == _picked ? Color.yellow : Color.white;
            if (Handles.Button(p.label, Quaternion.identity, s, s * 1.6f, Handles.DotHandleCap))
            {
                _picked = index;
                Repaint();
            }
            Handles.Label(p.label + Vector3.up * (s * 2f), p.text, _plotStyle);
        }

        if (_picked >= 0 && _picked < plots.Count && !(plots[_picked] is HarmonyGroveConfig) && plots[_picked].enabled)
            PlotHandles(ctl, plots[_picked], grid);
    }

    void PlotHandles(LevelMapController ctl, MapDecorConfig cfg, GridSystem grid)
    {
        float cs = grid.cellSize;
        var ext = cfg.Extent;
        // The footprint, at the top of its floor cells.
        Vector3 a = grid.GridToWorld(cfg.origin) + new Vector3(-cs * 0.5f, cs * 0.5f, -cs * 0.5f);
        Vector3 b = a + new Vector3(ext.x * cs, 0f, 0f);
        Vector3 c = a + new Vector3(ext.x * cs, 0f, ext.y * cs);
        Vector3 d = a + new Vector3(0f, 0f, ext.y * cs);

        Handles.color = Color.yellow;
        Handles.DrawAAPolyLine(4f, a, b, c, d, a);

        // Move, snapped to whole cells.
        var centre = (a + c) * 0.5f;
        EditorGUI.BeginChangeCheck();
        var moved = Handles.PositionHandle(centre, Quaternion.identity);
        if (EditorGUI.EndChangeCheck())
        {
            var delta = moved - centre;
            var step = new Vector3Int(Mathf.RoundToInt(delta.x / cs), Mathf.RoundToInt(delta.y / cs), Mathf.RoundToInt(delta.z / cs));
            if (step != Vector3Int.zero)
            {
                Undo.RecordObject(ctl, "Move map plot");
                cfg.origin += step;
                Changed(ctl);
            }
        }

        // Resize: a square on each edge.
        EdgeHandle(ctl, cfg, cs, (b + c) * 0.5f, Vector3.right,   0, false);
        EdgeHandle(ctl, cfg, cs, (a + d) * 0.5f, Vector3.left,    0, true);
        EdgeHandle(ctl, cfg, cs, (c + d) * 0.5f, Vector3.forward, 1, false);
        EdgeHandle(ctl, cfg, cs, (a + b) * 0.5f, Vector3.back,    1, true);
    }

    // Drag one edge out or in by whole cells. The low edges move the origin, so
    // the far edge stays put. Works on the rotated footprint and writes back
    // through rotationSteps, so a turned farm resizes the way it looks.
    void EdgeHandle(LevelMapController ctl, MapDecorConfig cfg, float cs, Vector3 at, Vector3 dir, int axis, bool lowSide)
    {
        float s = HandleUtility.GetHandleSize(at) * 0.1f;
        Handles.color = new Color(1f, 0.85f, 0.2f);
        EditorGUI.BeginChangeCheck();
        var np = Handles.Slider(at, dir, s, Handles.CubeHandleCap, 0f);
        if (!EditorGUI.EndChangeCheck()) return;

        int k = Mathf.RoundToInt(Vector3.Dot(np - at, dir) / cs);   // cells outward
        if (k == 0) return;
        var ext  = cfg.Extent;
        int cur  = axis == 0 ? ext.x : ext.y;
        int next = Mathf.Max(3, cur + k);
        k = next - cur;
        if (k == 0) return;

        Undo.RecordObject(ctl, "Resize map plot");
        if (axis == 0) { ext.x = next; if (lowSide) cfg.origin.x -= k; }
        else           { ext.y = next; if (lowSide) cfg.origin.z -= k; }
        cfg.size = (cfg.rotationSteps & 1) == 1 ? new Vector2Int(ext.y, ext.x) : ext;
        Changed(ctl);
    }

    void Changed(LevelMapController ctl)
    {
        EditorUtility.SetDirty(ctl);
        PrefabUtility.RecordPrefabInstancePropertyModifications(ctl);
        _sig = null;
        Repaint();
    }

    // ── Preview mesh ─────────────────────────────────────────────────────────

    // Everything the picture depends on. Rebuilt only when this changes.
    static string Signature(LevelMapController ctl, GridSystem grid)
    {
        var sb = new StringBuilder();
        sb.Append(grid.cellSize).Append('|').Append(grid.Origin).Append('|');
        sb.Append(ctl.mapAsset != null ? ctl.mapAsset.GetInstanceID() : 0).Append('|');
        var data = ctl.EditorMapData();
        sb.Append(data?.nodes != null ? data.nodes.Count : -1).Append('|');
        foreach (var p in ctl.EditorPlots()) sb.Append(JsonUtility.ToJson(p)).Append('|');
        return sb.ToString();
    }

    void Rebuild(LevelMapController ctl, GridSystem grid)
    {
        float cs = grid.cellSize;
        var v   = new List<Vector3>();
        var col = new List<Color>();
        var tri = new List<int>();
        _plots.Clear();
        _levelLabels.Clear();

        bool any = false;
        void Grow(Vector3 p)
        {
            if (!any) { _bounds = new Bounds(p, Vector3.one * cs); any = true; }
            else _bounds.Encapsulate(p);
        }

        // ── Level blocks ──
        var claimed = new HashSet<Vector2Int>();
        var data = ctl.EditorMapData();
        if (data?.nodes != null)
            foreach (var n in data.nodes)
            {
                if (n?.cells == null || n.cells.Length == 0) continue;
                var tint = n.synergyColor != BlockColor.None ? BlockColorPalette.Get(n.synergyColor) : BlockColorPalette.Snap(n.color);
                var sum = Vector3.zero;
                foreach (var cell in n.cells)
                {
                    Cell(v, col, tri, grid, cell, tint, true, 15);
                    claimed.Add(new Vector2Int(cell.x, cell.z));
                    sum += grid.GridToWorld(cell);
                    Grow(grid.GridToWorld(cell));
                }
                string text = !string.IsNullOrEmpty(n.levelId) ? n.levelId : n.isStart ? "start" : null;
                if (text != null) _levelLabels.Add((text, sum / n.cells.Length + Vector3.up * cs));
            }

        // ── Plots, the grove last (it wraps what's already there) ──
        // baseY: the floor the columns stand on. The grove's own origin is stale in
        // the scene (it is re-anchored to the farm at runtime), so it passes its
        // own lowest top instead.
        void AddPlot(MapDecorConfig p, List<Vector3Int> tops, int baseY, string note)
        {
            if (tops.Count == 0) return;
            var height = new Dictionary<Vector2Int, int>();
            int x0 = int.MaxValue, x1 = int.MinValue, z0 = int.MaxValue, z1 = int.MinValue;
            foreach (var t in tops)
            {
                height[new Vector2Int(t.x, t.z)] = t.y;
                x0 = Mathf.Min(x0, t.x); x1 = Mathf.Max(x1, t.x);
                z0 = Mathf.Min(z0, t.z); z1 = Mathf.Max(z1, t.z);
            }

            int maxTop = baseY, lost = 0;
            var mine = new List<Vector3Int>(tops.Count);
            foreach (var t in tops)
            {
                // Ground already standing wins, as in BuildDecor.
                if (claimed.Contains(new Vector2Int(t.x, t.z))) { lost++; height.Remove(new Vector2Int(t.x, t.z)); continue; }
                mine.Add(t);
            }
            foreach (var t in mine)
            {
                var c2 = new Vector2Int(t.x, t.z);
                claimed.Add(c2);
                for (int y = baseY; y <= t.y; y++)
                {
                    var cell = new Vector3Int(t.x, y, t.z);
                    int mask = 0;
                    for (int s = 0; s < 4; s++)
                        if (!height.TryGetValue(c2 + Side[s], out int nh) || nh < y) mask |= 1 << s;
                    var colour = y < p.origin.y ? Display(p.foundationColor) : Display(p.CellColor(cell));
                    Cell(v, col, tri, grid, cell, colour, y == t.y, mask);
                }
                maxTop = Mathf.Max(maxTop, t.y);
                Grow(grid.GridToWorld(t));
            }

            var centre = grid.GridToWorld(new Vector3Int((x0 + x1) / 2, maxTop, (z0 + z1) / 2));
            string gate = string.IsNullOrEmpty(p.gateLevelId) ? "always" : "after " + p.gateLevelId;
            _plots.Add(new PlotInfo
            {
                cfg   = p,
                label = centre + Vector3.up * (cs * 2.5f),
                text  = $"{p.RootName}  ({gate})\n" + (note ??
                        $"origin {p.origin.x}, {p.origin.y}, {p.origin.z}   size {p.size.x} x {p.size.y}")
                        + (lost > 0 ? $"\n{lost} cells give way to ground already there" : ""),
            });
        }

        var plots = ctl.EditorPlots();
        HarmonyGroveConfig grove = null;
        foreach (var p in plots)
        {
            if (p is HarmonyGroveConfig g) { grove = g; continue; }
            if (!p.enabled) continue;
            AddPlot(p, ctl.EditorPlotTops(p), p.origin.y - p.foundation, null);
        }
        if (grove != null && grove.enabled)
        {
            var tops = ctl.EditorGroveTops(grove, new HashSet<Vector2Int>(claimed), plots);
            int low = int.MaxValue;
            foreach (var t in tops) low = Mathf.Min(low, t.y);
            AddPlot(grove, tops, low, "wraps the farm");
        }

        if (_mesh == null) _mesh = new Mesh { name = "MapLayoutPreview", hideFlags = HideFlags.HideAndDontSave };
        _mesh.Clear();
        _mesh.indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        _mesh.SetVertices(v);
        _mesh.SetColors(col);
        _mesh.SetTriangles(tri, 0);
        _mesh.RecalculateBounds();
    }

    // One cell as a slightly inset box, so the seams between cells show: its top
    // (when it is the column's top) and whichever sides are open (mask bits: +x,
    // -x, +z, -z).
    static void Cell(List<Vector3> v, List<Color> c, List<int> t, GridSystem grid, Vector3Int cell,
                     Color colour, bool top, int sides)
    {
        float cs = grid.cellSize;
        var m = grid.GridToWorld(cell);
        float h = cs * 0.5f, w = cs * 0.47f;
        float y0 = m.y - h, y1 = m.y + h;
        float x0 = m.x - w, x1 = m.x + w, z0 = m.z - w, z1 = m.z + w;

        if (top) Quad(v, c, t, new(x0, y1, z0), new(x0, y1, z1), new(x1, y1, z1), new(x1, y1, z0), colour);
        if ((sides & 1) != 0) Quad(v, c, t, new(x1, y0, z0), new(x1, y1, z0), new(x1, y1, z1), new(x1, y0, z1), Shade(colour, SideShade[0]));
        if ((sides & 2) != 0) Quad(v, c, t, new(x0, y0, z1), new(x0, y1, z1), new(x0, y1, z0), new(x0, y0, z0), Shade(colour, SideShade[1]));
        if ((sides & 4) != 0) Quad(v, c, t, new(x1, y0, z1), new(x1, y1, z1), new(x0, y1, z1), new(x0, y0, z1), Shade(colour, SideShade[2]));
        if ((sides & 8) != 0) Quad(v, c, t, new(x0, y0, z0), new(x0, y1, z0), new(x1, y1, z0), new(x1, y0, z0), Shade(colour, SideShade[3]));
    }

    static void Quad(List<Vector3> v, List<Color> c, List<int> t, Vector3 a, Vector3 b, Vector3 d, Vector3 e, Color colour)
    {
        int s = v.Count;
        v.Add(a); v.Add(b); v.Add(d); v.Add(e);
        c.Add(colour); c.Add(colour); c.Add(colour); c.Add(colour);
        t.Add(s); t.Add(s + 1); t.Add(s + 2);
        t.Add(s); t.Add(s + 2); t.Add(s + 3);
    }

    static Color Shade(Color c, float k) => new(c.r * k, c.g * k, c.b * k, 1f);

    // Glowing colours (lava, lamps) squeezed back into range for the preview.
    static Color Display(Color c)
    {
        float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        if (m > 1f) { c.r /= m; c.g /= m; c.b /= m; }
        c.a = 1f;
        return c;
    }

    static Material Mat()
    {
        if (_mat != null) return _mat;
        var sh = Shader.Find("Hidden/Internal-Colored");
        if (sh == null) return null;
        _mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _mat.SetInt("_Cull",     (int)UnityEngine.Rendering.CullMode.Off);
        _mat.SetInt("_ZWrite",   1);
        _mat.SetInt("_ZTest",    (int)UnityEngine.Rendering.CompareFunction.LessEqual);
        return _mat;
    }

    void Styles()
    {
        if (_plotStyle != null) return;
        if (_labelBg == null)
        {
            _labelBg = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            _labelBg.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.65f));
            _labelBg.Apply();
        }
        _plotStyle = new GUIStyle(EditorStyles.label)
        {
            fontSize  = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            padding   = new RectOffset(6, 6, 3, 3),
        };
        _plotStyle.normal.background = _labelBg;
        _plotStyle.normal.textColor  = Color.white;

        _levelStyle = new GUIStyle(_plotStyle) { fontSize = 10, fontStyle = FontStyle.Normal };
        _levelStyle.normal.textColor = new Color(1f, 0.92f, 0.6f);
    }
}
