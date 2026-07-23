using System.Collections.Generic;
using UnityEngine;

// The overworld's "is this thing on?" layer: a start monument (flat-plated,
// constructivist — GeoPalette, no spheres/gradients) and wind links (thin
// drifting filaments, Custom/WindFlow) traced along the actual walkable road
// from start to each activated level. Building a bridge makes the wind drift
// out to that level; picking it back up lets it die away.
public partial class LevelMapController : MonoBehaviour
{
    [Header("Start monument")]
    [Tooltip("Flat shader for the monument plates. Falls back to SilkscreenFlat → URP Lit, same lookup ShrineController uses.")]
    public Material monumentMaterial;
    [Tooltip("Overall size of the monument as a fraction of one cell.")]
    [Range(0.4f, 2f)] public float monumentScale = 1f;
    [Tooltip("Warm gold point light under the apex, matching the shrine's. 0 = off.")]
    [Range(0f, 4f)] public float monumentLight = 1.6f;
    [Tooltip("Sinks the monument below the block's surface top (fraction of a cell) so it sits IN the block, not on top.")]
    [Range(0f, 0.6f)] public float monumentSink = 0.25f;

    [Header("Wind links (start → activated levels)")]
    [Tooltip("Optional override. Left empty, the Custom/WindFlow shader is used — that's the intended look.")]
    public Material windMaterial;
    public Color windColor = new(0.910f, 0.698f, 0.227f, 1f);   // GeoPalette.Gold
    [Range(0.02f, 0.8f)] public float windWidth = 0.30f;   // bundle diameter, not a line thickness
    [Tooltip("Height above the road surface the ribbon floats at.")]
    public float windLift = 0.08f;
    [Tooltip("How fast a link fades in when its level powers up (and out when it goes dark).")]
    public float windFadeSpeed = 2.5f;

    // Filled by MarkConnectivity: surface cell -> cell the flood arrived from.
    // Walking it back from any lit cell gives the actual road home.
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

    // Rebuilds every link from scratch. Called from RefreshNodes (once per map
    // change), never per frame.
    void RebuildWindLinks()
    {
        EnsureWindRoot();

        int used = 0;
        foreach (var n in _nodes)
        {
            if (n == null) continue;

            // Badges track power for EVERY level, lit or not.
            if (_markers.TryGetValue(n, out var marker) && marker != null)
            {
                marker.SetPowered(n.connectedToStart && n.NodeState != LevelNode.State.Locked);

                // Keys off the save record's `cleared` flag, not NodeState — clearing
                // is permanent even if the road later gets picked up.
                if (n.level != null)
                {
                    var rec = SaveSystem.Profile.GetRecord(n.level.levelId);
                    if (rec != null && rec.cleared) marker.SetCleared(true, rec.clearSynergyColor);
                }
            }

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

        // Fade leftovers out (picking up a bridge should read as the wind dying,
        // not a cut wire) — UpdateWind disables them once they hit zero.
        for (int i = used; i < _links.Count; i++) _linkTarget[i] = 0f;

        UpdateMonument();
    }

    // Walks _reachedFrom back to a start seed, then hands the cell path to
    // BuildWorldPath (same wall-hugging conversion as the pawn's walk/trail) so
    // the ribbon crawls the block's silhouette instead of cutting through it.
    List<Vector3> TraceRoadHome(LevelNode node)
    {
        if (node.cells == null) return null;

        // Enter at whichever cell the flood reached; prefer the highest.
        Vector3Int entry = default;
        bool found = false;
        foreach (var c in node.cells)
            if (_reachedFrom.ContainsKey(c) && (!found || c.y > entry.y)) { entry = c; found = true; }
        if (!found) return null;

        var cells = new List<Vector3Int>();
        var seen  = new HashSet<Vector3Int>();
        var cur   = entry;
        while (seen.Add(cur))
        {
            cells.Add(cur);
            if (!_reachedFrom.TryGetValue(cur, out var prev) || prev == cur) break;   // seed maps to itself
            cur = prev;
        }
        cells.Reverse();
        if (cells.Count < 2) return null;   // BuildWorldPath needs at least a start + one step

        var pts = BuildWorldPath(cells);
        for (int i = 0; i < pts.Count; i++) pts[i] += Vector3.up * windLift;
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
            // Stretch (not Tile): UV.x runs 0→1 over the whole ribbon so the
            // shader's end-fade lands on the real endpoints, not every world unit.
            lr.textureMode       = LineTextureMode.Stretch;
            lr.alignment         = LineAlignment.View;   // always faces camera
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
                // Fall back to the trail's unlit material — links still draw, no drift.
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

    // Eases each link's alpha toward its target; the drift itself is the shader's job.
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

    // Flat plates, hard edges — same constructivist grammar as the title screen
    // and the level badges.
    //
    //   apex diamond  ◆   same tilted cube as a level badge, Blue, floating free
    //   frame B       ▱   tilted square outline, counter-rotating
    //   frame A       ▭   larger square outline, rotating
    //   plinth        ▬   flat ink slab
    //
    // Blue not Signal red — Exploration's vermilion (BlockColorPalette) sits too
    // close to GeoPalette.Signal, so a red start would misread as an Exploration clear.
    void BuildStartMonument()
    {
        if (_monument != null || _startNode == null) return;

        float cs = (gridSystem != null ? gridSystem.cellSize : 1f) * monumentScale;

        var root = new GameObject("StartMonument");
        root.transform.SetParent(transform, false);
        _monument = root.transform;

        var plinth = MakePlate(root.transform, "Plinth",
                               new Vector3(0.78f, 0.07f, 0.78f) * cs,
                               Vector3.up * (cs * 0.035f),
                               Quaternion.identity, GeoPalette.Ink);

        // Counter-rotating outlines sell "powered" without any glow.
        var frameA = MakeSquareFrame(root.transform, "FrameA", cs * 0.62f, cs * 0.045f, GeoPalette.Blue);
        frameA.localPosition = Vector3.up * (cs * 0.30f);

        var frameB = MakeSquareFrame(root.transform, "FrameB", cs * 0.42f, cs * 0.038f, GeoPalette.Gold);
        frameB.localPosition = Vector3.up * (cs * 0.56f);
        frameB.localRotation = Quaternion.Euler(28f, 45f, 0f);

        // Same shape as a level badge, floating free — one family of markers.
        var apex = MakePlate(root.transform, "Apex",
                             Vector3.one * (cs * 0.26f),
                             Vector3.up * (cs * 0.72f),
                             Quaternion.Euler(45f, 0f, 45f), GeoPalette.Blue);

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

    // One flat box plate — everything in the monument is one of these.
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

    // Four plates as a square OUTLINE — a filled square would just look like a lid.
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
        // Same lookup chain as ShrineController, so surfaces stay consistent.
        var sh = Shader.Find("GeoWorld/SilkscreenFlat")
              ?? Shader.Find("Universal Render Pipeline/Lit")
              ?? Shader.Find("Standard");
        _monumentMat = new Material(sh) { hideFlags = HideFlags.DontSave };
        return _monumentMat;
    }

    // Re-run on every rebuild — the start block's surface height can change.
    void UpdateMonument()
    {
        if (_startNode == null) return;
        BuildStartMonument();
        if (_monument == null) return;
        float cs = gridSystem != null ? gridSystem.cellSize : 1f;
        _monument.position = SurfaceTop(TopCellOf(_startNode)) - Vector3.up * (cs * monumentSink);
    }

    // Differential rotation between the frames + apex bob on the same sine as
    // the level badges/pawn, so the whole screen breathes together.
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
