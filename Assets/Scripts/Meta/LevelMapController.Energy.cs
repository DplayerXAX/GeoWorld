using System.Collections.Generic;
using UnityEngine;

// The overworld's "is this thing on?" layer.
//
// LevelSelect used to communicate a level's state with nothing but a block tint,
// which is close to invisible on a map already made of coloured blocks. Two
// additions, both in the house constructivist/silkscreen language rather than
// gameplay's sci-fi one — the map is the MENU layer, so it borrows gameplay's
// ideas but not its chrome:
//
//   • START MONUMENT — a flat-plated marker built from the same vocabulary as the
//     title screen and the level badges: hard geometry, GeoPalette flats, no
//     spheres and no gradients. Counter-rotating square frames around a gold
//     spire, capped with the same tilted diamond the level badges use.
//   • WIND LINES — a bundle of thin drifting filaments (Custom/WindFlow) traced
//     along the ACTUAL walkable road from the start to every activated level.
//     Build a bridge and the wind starts drifting out to the level; pick the
//     bridge back up and it dies away. The connectivity rule, made visible
//     instead of explained.
//
// The wind is a different SHAPE from gameplay's PathLaser, not just a dimmer one:
// laser = one solid beam of constant width (a route you must follow), wind = many
// tapered strands weaving past each other (a current that happens to run there).
public partial class LevelMapController : MonoBehaviour
{
    [Header("Start monument")]
    [Tooltip("Flat shader for the monument plates. Falls back to SilkscreenFlat → URP Lit, same lookup ShrineController uses.")]
    public Material monumentMaterial;
    [Tooltip("Overall size of the monument as a fraction of one cell.")]
    [Range(0.4f, 2f)] public float monumentScale = 1f;
    [Tooltip("Warm gold point light under the apex, matching the shrine's. 0 = off.")]
    [Range(0f, 4f)] public float monumentLight = 1.6f;
    [Tooltip("Sinks the whole monument this far below the block's surface top, as a fraction of one cell — settles it INTO the block instead of perching on top.")]
    [Range(0f, 0.6f)] public float monumentSink = 0.25f;

    [Header("Wind links (start → activated levels)")]
    [Tooltip("Optional override. Left empty, the Custom/WindFlow shader is used — that's the intended look.")]
    public Material windMaterial;
    public Color windColor = new(0.910f, 0.698f, 0.227f, 1f);   // GeoPalette.Gold
    // Wider than a laser would be on purpose: the strands spread across the ribbon's
    // width, so this is the bundle's diameter, not a line thickness.
    [Range(0.02f, 0.8f)] public float windWidth = 0.30f;
    [Tooltip("Height above the road surface the ribbon floats at.")]
    public float windLift = 0.08f;
    [Tooltip("How fast a link fades in when its level powers up (and out when it goes dark).")]
    public float windFadeSpeed = 2.5f;

    // Filled by MarkConnectivity: surface cell -> the cell the flood arrived FROM.
    // Walking this back from any lit cell yields the actual road home, which is what
    // the ribbons are drawn along.
    readonly Dictionary<Vector3Int, Vector3Int> _reachedFrom = new();
    LevelNode _startNode;

    readonly Dictionary<LevelNode, MapLevelMarker> _markers = new();

    Transform                   _windRoot;
    readonly List<LineRenderer> _links     = new();
    readonly List<float>        _linkAlpha = new();   // current eased alpha, parallel to _links
    readonly List<float>        _linkTarget = new();  // 1 while the link is in use, 0 once retired
    Material                    _windMat;             // one shared instance; the shader self-animates
    Transform                   _monument;

    // ── Wind links ───────────────────────────────────────────────────────────

    // Rebuild every link from scratch. Called from RefreshNodes, i.e. once per real
    // map change (load, place, pick up) — never per frame, so tracing paths here is
    // cheap regardless of map size.
    void RebuildWindLinks()
    {
        EnsureWindRoot();

        int used = 0;
        foreach (var n in _nodes)
        {
            if (n == null) continue;

            // Badges track power for EVERY level, lit or not.
            if (_markers.TryGetValue(n, out var marker) && marker != null)
                marker.SetPowered(n.connectedToStart && n.NodeState != LevelNode.State.Locked);

            if (n.level == null || n == _startNode || !n.connectedToStart) continue;

            var path = TraceRoadHome(n);
            if (path == null || path.Count < 2) continue;

            var lr = LinkAt(used);
            lr.positionCount = path.Count;
            for (int i = 0; i < path.Count; i++) lr.SetPosition(i, path[i]);
            lr.enabled       = true;
            _linkTarget[used] = 1f;
            used++;
        }

        // Retire the leftovers by fading them out rather than snapping them off —
        // picking a bridge up should look like the wind dying, not like a cut wire.
        // UpdateWind disables them once they've actually reached zero.
        for (int i = used; i < _links.Count; i++) _linkTarget[i] = 0f;

        UpdateMonument();
    }

    // Walk _reachedFrom back from the node's nearest lit cell to a start seed,
    // converting to world points on the way. Returns start→node order so the wind
    // reads as drifting OUT of the monument.
    List<Vector3> TraceRoadHome(LevelNode node)
    {
        if (node.cells == null) return null;

        // Enter at whichever of the node's cells the flood actually reached; prefer
        // the highest so the ribbon lands where the pawn would stand.
        Vector3Int entry = default;
        bool found = false;
        foreach (var c in node.cells)
            if (_reachedFrom.ContainsKey(c) && (!found || c.y > entry.y)) { entry = c; found = true; }
        if (!found) return null;

        var pts  = new List<Vector3>();
        var seen = new HashSet<Vector3Int>();
        var cur  = entry;
        while (seen.Add(cur))
        {
            pts.Add(SurfaceTop(cur) + Vector3.up * windLift);
            if (!_reachedFrom.TryGetValue(cur, out var prev) || prev == cur) break;   // seed maps to itself
            cur = prev;
        }
        pts.Reverse();
        return pts;
    }

    LineRenderer LinkAt(int i)
    {
        while (_links.Count <= i)
        {
            var go = new GameObject($"WindLink_{_links.Count}");
            go.transform.SetParent(_windRoot, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.material          = WindMaterial();
            lr.useWorldSpace     = true;
            lr.positionCount     = 0;
            lr.startWidth        = windWidth;
            lr.endWidth          = windWidth;
            lr.numCapVertices    = 4;
            lr.numCornerVertices = 4;
            // Stretch, not Tile: the shader wants UV.x to run 0→1 over the WHOLE
            // ribbon so its end-fade lands on the real endpoints. Tiling would
            // restart the fade at every world unit and read as a dashed line.
            lr.textureMode       = LineTextureMode.Stretch;
            lr.alignment         = LineAlignment.View;   // ribbon always faces the camera — no edge-on disappearing
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows    = false;
            lr.enabled           = false;
            _links.Add(lr);
            _linkAlpha.Add(0f);
            _linkTarget.Add(0f);
        }
        return _links[i];
    }

    Material WindMaterial()
    {
        if (_windMat != null) return _windMat;

        if (windMaterial != null) _windMat = new Material(windMaterial);
        else
        {
            var sh = Shader.Find("Custom/WindFlow");
            if (sh == null)
            {
                // Shader stripped from the build / not found — fall back to the trail's
                // unlit material so the links still draw, just without the drift.
                var fb = GetTrailFallbackMaterial();
                if (fb == null) return null;
                _windMat = new Material(fb);
            }
            else _windMat = new Material(sh);
        }

        _windMat.color = windColor;
        if (_windMat.HasProperty("_Color")) _windMat.SetColor("_Color", windColor);
        return _windMat;
    }

    void EnsureWindRoot()
    {
        if (_windRoot != null) return;
        var go = new GameObject("WindLinks");
        go.transform.SetParent(transform, false);
        _windRoot = go.transform;
    }

    // Ease each link's alpha toward its target. The DRIFT is the shader's job — all
    // C# does here is fade links in and out as roads light up and go dark. Vertex
    // colour is what carries it, which WindFlow multiplies into its output.
    void UpdateWind()
    {
        if (_links.Count == 0) return;
        float k = 1f - Mathf.Exp(-windFadeSpeed * Time.deltaTime);

        for (int i = 0; i < _links.Count; i++)
        {
            var lr = _links[i];
            if (lr == null) continue;

            float a = Mathf.Lerp(_linkAlpha[i], _linkTarget[i], k);
            _linkAlpha[i] = a;

            if (a < 0.01f) { if (lr.enabled && _linkTarget[i] <= 0f) lr.enabled = false; continue; }

            var c = windColor; c.a = a;
            lr.startColor = c;
            lr.endColor   = c;
        }
    }

    // ── Start monument ───────────────────────────────────────────────────────

    // Built from flat plates and hard edges — the same constructivist grammar as
    // the title screen and the level badges. Reads as a landmark you navigate BY,
    // which is what a start point on a map is for.
    //
    //   apex diamond  ◆   ← same tilted cube as every level badge, in Signal red
    //   spire         │   ← thin gold column
    //   frame B       ▱   ← tilted square outline, counter-rotating
    //   frame A       ▭   ← larger square outline, rotating
    //   plinth        ▬   ← flat ink slab grounding the whole thing
    void BuildStartMonument()
    {
        if (_monument != null || _startNode == null) return;

        float cs = (gridSystem != null ? gridSystem.cellSize : 1f) * monumentScale;

        var root = new GameObject("StartMonument");
        root.transform.SetParent(transform, false);
        _monument = root.transform;

        // Plinth — reads as "this is a built thing standing on the block".
        var plinth = MakePlate(root.transform, "Plinth",
                               new Vector3(0.78f, 0.07f, 0.78f) * cs,
                               Vector3.up * (cs * 0.035f),
                               Quaternion.identity, GeoPalette.Ink);

        // Two square outlines at different heights, sizes and tilts. Counter-rotation
        // is what sells "powered" without any glow — pure motion parallax.
        var frameA = MakeSquareFrame(root.transform, "FrameA", cs * 0.62f, cs * 0.045f, GeoPalette.Signal);
        frameA.localPosition = Vector3.up * (cs * 0.30f);

        var frameB = MakeSquareFrame(root.transform, "FrameB", cs * 0.42f, cs * 0.038f, GeoPalette.Gold);
        frameB.localPosition = Vector3.up * (cs * 0.56f);
        frameB.localRotation = Quaternion.Euler(28f, 45f, 0f);

        // Spire — the vertical anchor that makes it legible from across the map.
        MakePlate(root.transform, "Spire",
                  new Vector3(0.10f, 0.62f, 0.10f) * cs,
                  Vector3.up * (cs * 0.40f),
                  Quaternion.identity, GeoPalette.Gold);

        // Apex diamond — deliberately the SAME shape as a level badge. The map then
        // reads as one family of markers: "here is a place that matters".
        var apex = MakePlate(root.transform, "Apex",
                             Vector3.one * (cs * 0.26f),
                             Vector3.up * (cs * 0.86f),
                             Quaternion.Euler(45f, 0f, 45f), GeoPalette.Signal);

        if (monumentLight > 0.01f)
        {
            var lightGo = new GameObject("MonumentLight");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = Vector3.up * (cs * 0.6f);
            var light = lightGo.AddComponent<Light>();
            light.type      = LightType.Point;
            light.color     = GeoPalette.Gold;
            light.range     = cs * 4.5f;
            light.intensity = monumentLight;
        }

        root.AddComponent<MonumentSpin>().Init(frameA, frameB, apex, plinth);
    }

    // One flat box plate. Everything in the monument is one of these — keeping it to
    // a single primitive is what makes the silhouette read as designed rather than
    // assembled out of whatever primitives were handy.
    Transform MakePlate(Transform parent, string name, Vector3 scale, Vector3 localPos,
                        Quaternion localRot, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        if (go.TryGetComponent<Collider>(out var col)) Destroy(col);   // never blocks map picking
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        go.transform.localScale    = scale;

        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial    = MonumentMaterial();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        MpbColor.Set(mr, color);
        return go.transform;
    }

    // Four plates laid out as a square OUTLINE (not a filled quad) — the negative
    // space in the middle is the point; a solid square would just look like a lid.
    Transform MakeSquareFrame(Transform parent, string name, float half, float thick, Color color)
    {
        var frame = new GameObject(name);
        frame.transform.SetParent(parent, false);

        float len = half * 2f + thick;
        MakePlate(frame.transform, "N", new Vector3(len,   thick, thick), new Vector3(0f, 0f,  half), Quaternion.identity, color);
        MakePlate(frame.transform, "S", new Vector3(len,   thick, thick), new Vector3(0f, 0f, -half), Quaternion.identity, color);
        MakePlate(frame.transform, "E", new Vector3(thick, thick, len),   new Vector3( half, 0f, 0f), Quaternion.identity, color);
        MakePlate(frame.transform, "W", new Vector3(thick, thick, len),   new Vector3(-half, 0f, 0f), Quaternion.identity, color);
        return frame.transform;
    }

    static Material _monumentMat;
    Material MonumentMaterial()
    {
        if (monumentMaterial != null) return monumentMaterial;
        if (_monumentMat != null) return _monumentMat;
        // Same lookup chain ShrineController uses, so the map and the shrine share a
        // surface treatment instead of drifting apart.
        var sh = Shader.Find("GeoWorld/SilkscreenFlat")
              ?? Shader.Find("Universal Render Pipeline/Lit")
              ?? Shader.Find("Standard");
        _monumentMat = new Material(sh) { hideFlags = HideFlags.DontSave };
        return _monumentMat;
    }

    // Park the monument on top of the start block. Re-run on every rebuild because
    // the start block's surface height can change if the player stacks onto it.
    void UpdateMonument()
    {
        if (_startNode == null) return;
        BuildStartMonument();
        if (_monument == null) return;
        float cs = gridSystem != null ? gridSystem.cellSize : 1f;
        _monument.position = SurfaceTop(TopCellOf(_startNode)) - Vector3.up * (cs * monumentSink);
    }

    // All the life in the monument comes from differential rotation — two frames
    // turning against each other at unrelated speeds, plus the apex bobbing on the
    // same sine the level badges and the pawn use, so the whole screen breathes
    // together.
    class MonumentSpin : MonoBehaviour
    {
        Transform _a, _b, _apex, _plinth;
        Vector3   _apexBase;

        public void Init(Transform a, Transform b, Transform apex, Transform plinth)
        {
            _a = a; _b = b; _apex = apex; _plinth = plinth;
            if (_apex != null) _apexBase = _apex.localPosition;
        }

        void Update()
        {
            if (_a != null) _a.Rotate(0f,  24f * Time.deltaTime, 0f, Space.Self);
            if (_b != null) _b.Rotate(0f, -37f * Time.deltaTime, 0f, Space.Self);
            if (_plinth != null) _plinth.Rotate(0f, 8f * Time.deltaTime, 0f, Space.Self);
            if (_apex != null)
            {
                _apex.localPosition = _apexBase + Vector3.up * (Mathf.Sin(Time.time * 2f) * 0.06f);
                _apex.Rotate(0f, 55f * Time.deltaTime, 0f, Space.World);
            }
        }
    }
}
