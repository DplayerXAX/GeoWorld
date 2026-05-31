using System;
using System.Collections.Generic;
using UnityEngine;

// Dev-only preview panel — spawns a row of placeholder blocks, each fed
// through one SynergyRule's visualizer, so you can see what every theme
// looks like side-by-side without playing the actual game loop.
//
// Usage:
//   1. Add this component to any GameObject in the scene.
//   2. Fill `entries` with one row per theme: drag in the rule asset.
//   3. Play, press `toggleKey` (default F4) to toggle the preview row.
//
// The preview bypasses SynergyEvaluator — visualizers are called directly
// with a synthetic ActiveSynergy. Joker tint / connection lines / pulses
// from SynergyVisualFX won't show; this is purely the rule's own visualizer.
public class SynergyVisualizerPreview : MonoBehaviour
{
    [Serializable]
    public class Entry
    {
        public string      label;   // optional display name
        public BlockColor  color;   // theme color used for the placeholder cube
        public SynergyRule rule;    // rule whose visualizer.OnPieceClaimed is invoked
    }

    [Header("Themes to preview")]
    public List<Entry> entries = new();

    [Header("Layout")]
    [Tooltip("First cell position. Other entries are laid out at startCell + cellSpacing × index.")]
    public Vector3Int startCell   = Vector3Int.zero;
    public Vector3Int cellSpacing = new(3, 0, 0);

    [Header("Hotkey")]
    public KeyCode toggleKey = KeyCode.F4;

    [Header("Label overlay")]
    public bool   showLabels      = true;
    public float  labelYOffset    = 22f;
    public Color  labelColor      = new(1f, 1f, 1f, 0.95f);

    bool _visible;
    readonly List<GameObject> _spawned = new();
    readonly List<(Vector3 world, string text, Color col)> _labelData = new();

    void Update()
    {
        if (Input.GetKeyDown(toggleKey)) Toggle();
    }

    public void Toggle()
    {
        if (_visible) Teardown();
        else          Build();
    }

    public void Build()
    {
        Teardown();
        var grid = GridSystem.instance;
        if (grid == null)
        {
            Debug.LogWarning("[SynergyPreview] No GridSystem.instance in scene — can't build preview.");
            return;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e == null || e.rule == null) continue;
            var cell = startCell + cellSpacing * i;
            BuildOne(e, cell, grid);
        }
        _visible = true;
    }

    void BuildOne(Entry entry, Vector3Int cell, GridSystem grid)
    {
        // Placeholder cube standing in for a placed block.
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = $"SynergyPreview_{entry.color}";
        cube.transform.SetParent(transform, false);
        cube.transform.position   = grid.GridToWorld(cell);
        cube.transform.localScale = Vector3.one * grid.cellSize;
        var col = cube.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var rend = cube.GetComponent<Renderer>();
        if (rend != null) MpbColor.Set(rend, BlockColorPalette.Get(entry.color));

        // Synthetic instance + active to satisfy the visualizer's API.
        var ins = new PlacedBlockInstance
        {
            visualObject = cube,
            color        = entry.color,
        };
        ins.occupiedCells.Add(cell);

        var fakePiece  = new PlacedPiece(0, null, entry.color, new[] { cell });
        var fakeActive = new ActiveSynergy(
            entry.rule,
            new HashSet<PlacedPiece> { fakePiece },
            tier: 1);

        if (entry.rule.visualizer != null)
        {
            try { entry.rule.visualizer.OnPieceClaimed(ins, fakeActive); }
            catch (Exception ex) { Debug.LogError($"[SynergyPreview] {entry.color}: {ex}"); }
        }
        else
        {
            Debug.LogWarning($"[SynergyPreview] Rule '{entry.rule.name}' has no visualizer — placeholder cube only.");
        }

        _spawned.Add(cube);

        if (showLabels)
        {
            var labelText = !string.IsNullOrEmpty(entry.label) ? entry.label : entry.color.ToString();
            _labelData.Add((cube.transform.position, labelText, BlockColorPalette.Get(entry.color)));
        }
    }

    public void Teardown()
    {
        for (int i = 0; i < _spawned.Count; i++)
            if (_spawned[i] != null) Destroy(_spawned[i]);
        _spawned.Clear();
        _labelData.Clear();
        _visible = false;
    }

    void OnDisable() => Teardown();
    void OnDestroy() => Teardown();

    // ── Floating labels under each preview cube ────────────────────────

    GUIStyle _labelStyle;

    void OnGUI()
    {
        if (!_visible || !showLabels || _labelData.Count == 0) return;
        var cam = Camera.main;
        if (cam == null) return;

        if (_labelStyle == null)
        {
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
        }

        for (int i = 0; i < _labelData.Count; i++)
        {
            var (world, text, col) = _labelData[i];
            var screen = cam.WorldToScreenPoint(world);
            if (screen.z < 0f) continue;

            float x = screen.x - 80f;
            float y = Screen.height - screen.y + labelYOffset;

            _labelStyle.normal.textColor = col;
            GUI.Label(new Rect(x, y, 160f, 20f), text, _labelStyle);
        }
    }
}
