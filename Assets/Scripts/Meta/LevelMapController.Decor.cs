using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Overworld set-dressing: an Abundance flower FARM built out of real map blocks,
// filling the empty ground beside a level and switching on once that level has
// been cleared.
//
// Registered as a plain LevelNode (level = null, same as the map's other bare
// "Waypoint" stepping-stones) so it becomes real walkable ground: BuildSurface/
// SurfaceBfs read cells off _nodes, not off any notion of "is this a real
// level" — a node with no level attached is simply passable, never enterable.
//

// Bundled into one field (LevelMapController.decor) instead of ~30 flat fields,
// so the Abundance farm doesn't dominate the Inspector — collapses to one foldout.
[System.Serializable]
public class AbundanceFarmConfig
{
    [Header("Plot")]
    [Tooltip("Off = never build the farm, whatever the save says.")]
    public bool enabled = true;
    [Tooltip("The farm appears once THIS level has been cleared. Blank = always on.")]
    public string gateLevelId = "1-1";
    [Tooltip("Lowest-x / lowest-z corner of the plot, in grid cells.")]
    public Vector3Int origin = new Vector3Int(-11, 2, 0);
    [Tooltip("Plot footprint in cells (x, z) before rotationSteps.")]
    public Vector2Int size = new Vector2Int(12, 11);
    [Tooltip("Turns the whole plot 90° clockwise per step (0-3) — layout and props together.")]
    [Range(0, 3)] public int rotationSteps = 0;
    [Tooltip("Fraction of plot cells that actually get a block — below 1 the edge frays.")]
    [Range(0.3f, 1f)] public float coverage = 0.82f;
    [Tooltip("Cells of height variation the plot terraces over. 0 = perfectly flat.")]
    [Range(0, 3)] public int terrace = 1;
    public Color soilColor = new Color(0.34f, 0.26f, 0.18f);
    [Tooltip("±brightness jitter per soil block.")]
    [Range(0f, 0.3f)] public float soilJitter = 0.12f;

    [Header("Beds")]
    [Tooltip("Row cycle along Z: every Nth row is a lane; planted rows alternate flower/crop. 0 = uniform meadow.")]
    public int pathRowEvery = 3;
    [Tooltip("Extra coverage for walking lanes over the base coverage.")]
    [Range(0f, 0.4f)] public float pathRowExtraCoverage = 0.18f;
    [Tooltip("Chance a flower-bed cell actually blooms.")]
    [Range(0f, 1f)] public float bloomChance = 0.8f;
    [Tooltip("Hard cap on flowers — the main cost dial for the whole farm.")]
    public int maxFlowers = 170;

    [Header("Crops")]
    [Range(0, 6)] public int stalksPerCell = 4;
    [Range(0.2f, 1.5f)] public float stalkHeight = 0.75f;
    public Color cropColor = new Color(0.62f, 0.58f, 0.24f);

    [Header("Fence")]
    public bool fenceEnabled = true;
    [Tooltip("Fraction of boundary edges that get a picket — gaps read as entrances.")]
    [Range(0f, 1f)] public float fenceCoverage = 0.78f;
    [Range(0.2f, 1.2f)] public float fenceHeight = 0.55f;
    public Color fenceColor = new Color(0.55f, 0.40f, 0.26f);

    [Header("Landmark")]
    public bool windmillEnabled = true;
    [Range(1f, 5f)] public float windmillHeight = 2.6f;
    public float windmillSpin = 26f;
    public Color towerColor = new Color(0.88f, 0.85f, 0.78f);
    public Color accentColor = new Color(0.98f, 0.80f, 0.30f);
    [Range(0, 10)] public int signCount = 4;

    [Header("Grow-in cutscene")]
    [Tooltip("How far below its resting position the field starts, on the one grow-in play.")]
    public float growRiseHeight = 3f;
    public float growRiseDuration = 1.8f;
    [Tooltip("Extra camera hold after the field settles, for BloomPatch's own bloom-in.")]
    public float growHoldSeconds = 1.4f;
    [Tooltip("Orbit zoom during the reveal. 0 = keep whatever zoom the map had.")]
    public float growZoom = 0f;
    [Tooltip("Degrees added to the camera's current yaw for the reveal (positive = turns right).")]
    public float growYawOffset = 90f;
    [Tooltip("Fade to/from black for the hand-off out of the reveal.")]
    public float transitionFadeDuration = 0.6f;
    [Tooltip("Aside bubble shown once during the reveal. Blank = none.")]
    [TextArea] public string growAsideText =
        "As Abundance returned, a small farm blossomed where countless wishes had been sown.";
    [Tooltip("How long the aside stays up — the camera hold stretches to cover it.")]
    public float growAsideSeconds = 4.5f;

    [Header("Residents")]
    [Tooltip("Planted on the field once it exists. Null = don't place one.")]
    public MapInteractable npc;
    public MapInteractable minigame;
    [Tooltip("Cells the minigame's own pedestal is raised above the surrounding ground (the NPC's is fixed at 1).")]
    [Range(1, 4)] public int minigamePedestalLift = 2;
}

public partial class LevelMapController : MonoBehaviour
{
    [Header("Abundance farm")]
    public AbundanceFarmConfig decor = new();

    GameObject _decorRoot;
    readonly List<GameObject> _residents = new();   // NPC / minigame objects planted on the field
    Vector3    _decorCenter;
    Vector3    _decorRestPos;   // where _decorRoot sits when NOT mid grow-in — see BuildDecor
    bool       _decorCutscenePlaying;

    static Image _fadeImage;

    // Lazily builds a full-screen black overlay, above everything else in the
    // scene (LevelSelect's other UI tops out around sortingOrder 850).
    static Image EnsureFadeImage()
    {
        if (_fadeImage != null) return _fadeImage;

        var canvasGo = new GameObject("DecorTransitionFade", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Object.DontDestroyOnLoad(canvasGo);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 950;   // above LevelClearScreen (900), below gameplay's IntroDirector (1000, irrelevant here)

        var rt = new GameObject("Fade", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(canvasGo.transform, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        _fadeImage = rt.gameObject.AddComponent<Image>();
        _fadeImage.color = new Color(0f, 0f, 0f, 0f);
        _fadeImage.raycastTarget = true;   // eat clicks while the screen is black
        return _fadeImage;
    }

    static IEnumerator FadeScreen(float from, float to, float duration)
    {
        var img = EnsureFadeImage();
        img.gameObject.SetActive(true);
        if (duration <= 0f) { img.color = new Color(0f, 0f, 0f, to); }
        else
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                img.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, Mathf.Clamp01(t / duration)));
                yield return null;
            }
            img.color = new Color(0f, 0f, 0f, to);
        }
        if (to <= 0f) img.gameObject.SetActive(false);
    }

    // Row roles. Lanes are bare walkways; beds alternate so the plot stripes.
    enum RowKind { Flower, Crop, Lane }

    RowKind RowKindAt(int localZ)
    {
        if (decor.pathRowEvery <= 0) return RowKind.Flower;
        if (localZ % decor.pathRowEvery == decor.pathRowEvery - 1) return RowKind.Lane;
        // Band index between lanes — alternate the bands so beds stripe rather
        // than every planted row looking identical.
        return (localZ / decor.pathRowEvery) % 2 == 1 ? RowKind.Crop : RowKind.Flower;
    }

    // ── Plot rotation ────────────────────────────────────────────────────────
    // Farm content is authored in plot-local space (rows along local +X, stepping
    // along local +Z, windmill rotor facing local -Z). decor.rotationSteps turns
    // that whole frame — layout and props together — re-anchored to stay
    // non-negative so decor.origin keeps meaning "lowest-x/lowest-z corner".
    // Matches Quaternion.Euler(0, steps*90, 0): a +90° Y turn sends local +X to
    // world -Z and local +Z to world +X.
    Vector2Int RotateLocal(int ix, int iz, int w, int d) => (decor.rotationSteps & 3) switch
    {
        1 => new Vector2Int(iz, w - 1 - ix),
        2 => new Vector2Int(w - 1 - ix, d - 1 - iz),
        3 => new Vector2Int(d - 1 - iz, ix),
        _ => new Vector2Int(ix, iz),
    };

    // Footprint extent after rotation — odd steps swap width and depth.
    Vector2Int RotatedExtent(int w, int d) =>
        (decor.rotationSteps & 1) == 1 ? new Vector2Int(d, w) : new Vector2Int(w, d);

    float DecorRotationDegrees => (decor.rotationSteps & 3) * 90f;

    // Local +X (the row / sowing direction) expressed in world space.
    Vector3 DecorRowDirWorld => Quaternion.Euler(0f, DecorRotationDegrees, 0f) * Vector3.right;

    // No-op until the gate level is cleared. Returns true only on the one visit
    // where the grow-in cutscene should play.
    bool TryBuildDecor()
    {
        if (!decor.enabled || _decorRoot != null) return false;
        if (gridSystem == null || cubePrefab == null) return false;

        if (!string.IsNullOrEmpty(decor.gateLevelId))
        {
            var rec = SaveSystem.Profile.GetRecord(decor.gateLevelId);
            if (rec == null || !rec.cleared) return false;
        }

        // Only the visit right after first clear plays the cutscene; later revisits
        // just rebuild the field instantly.
        bool grow = RunConfig.PendingMapGrowthLevelId == decor.gateLevelId;
        if (grow) RunConfig.PendingMapGrowthLevelId = null;

        BuildDecor(grow);
        return grow;
    }

    // Camera-locked reveal played once, right after this field's gate level is
    // first cleared. Blocks player input (_decorCutscenePlaying) for its duration.
    IEnumerator PlayDecorGrowthCutscene()
    {
        _decorCutscenePlaying = true;

        if (_orbit != null)
        {
            _orbit.focusViewport = new Vector2(0.5f, 0.5f);
            _orbit.FocusOnPoint(_decorCenter, snap: false);
            if (decor.growZoom > 0f) _orbit.SetZoom(decor.growZoom);
            _orbit.AddYaw(decor.growYawOffset);   // position eases to the new angle, so this still reads as a swing
        }

        if (!string.IsNullOrEmpty(decor.growAsideText))
            AsideBubble.Show(defaultCharacter, "default", decor.growAsideText, decor.growAsideSeconds);

        Vector3 sunk = _decorRoot != null ? _decorRoot.transform.position : _decorRestPos;
        float t = 0f;
        while (t < decor.growRiseDuration)
        {
            t += Time.deltaTime;
            float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / decor.growRiseDuration), 3f);   // ease-out cubic, no overshoot
            if (_decorRoot != null) _decorRoot.transform.position = Vector3.Lerp(sunk, _decorRestPos, e);
            yield return null;
        }
        if (_decorRoot != null) _decorRoot.transform.position = _decorRestPos;
        foreach (var r in _residents) if (r != null) r.SetActive(true);   // the field has arrived — its people with it

        // Camera waits for the line to finish (its clock started at the rise, so
        // only what's left of it needs covering).
        float hold = decor.growHoldSeconds;
        if (!string.IsNullOrEmpty(decor.growAsideText))
            hold = Mathf.Max(hold, decor.growAsideSeconds + AsideBubble.SlideSeconds - decor.growRiseDuration);
        yield return new WaitForSeconds(hold);

        // Fade to black for the hand-off — focusViewport resets with no lerp of its
        // own right below, so an eased pan would still pop.
        yield return FadeScreen(0f, 1f, decor.transitionFadeDuration);

        if (_orbit != null)
        {
            _orbit.focusViewport = new Vector2(focusViewportX, focusViewportY);
            _orbit.FocusOnPoint(_camFocus, snap: true);
        }

        PlayEntryDialogueIfAny();   // may re-focus again (reward conversation) — still hidden

        yield return FadeScreen(1f, 0f, decor.transitionFadeDuration);
        _decorCutscenePlaying = false;
    }

    void BuildDecor(bool grow)
    {
        var cells       = new List<Vector3Int>();   // in BlockRenderer instantiation order
        var occupied    = new HashSet<Vector3Int>();
        var coveredCols = new HashSet<Vector2Int>();
        var colTop      = new Dictionary<Vector2Int, Vector3Int>();

        int w = Mathf.Max(1, decor.size.x);
        int d = Mathf.Max(1, decor.size.y);
        float cs = gridSystem.cellSize;

        // Row role per world column — recorded here since world z no longer maps
        // back to the local row index once the plot can be rotated.
        var colKind = new Dictionary<Vector2Int, RowKind>();

        // The residents stand on PEDESTALS — forced-covered columns raised at least
        // one cell. Decided up front (they're derived from origin/extent alone) so
        // the ground pass can build them: picking a raised column afterwards would
        // only work when coverage and the terrace roll happened to cooperate.
        var ext = RotatedExtent(w, d);
        var npcCol  = new Vector2Int(decor.origin.x + ext.x / 3,     decor.origin.z + ext.y * 2 / 3);
        var gameCol = new Vector2Int(decor.origin.x + ext.x * 2 / 3, decor.origin.z + ext.y / 3);
        bool wantNpc  = decor.npc != null;
        bool wantGame = decor.minigame != null;

        // ── Ground ───────────────────────────────────────────────────────────
        for (int ix = 0; ix < w; ix++)
        for (int iz = 0; iz < d; iz++)
        {
            var rot = RotateLocal(ix, iz, w, d);
            var worldCol = new Vector2Int(decor.origin.x + rot.x, decor.origin.z + rot.y);
            bool npcPedestal  = wantNpc  && worldCol == npcCol;
            bool gamePedestal = wantGame && worldCol == gameCol;
            bool pedestal = npcPedestal || gamePedestal;

            var kind = RowKindAt(iz);
            // Lanes are deliberately MORE likely to be covered — they're the
            // farm's walkways, and a frayed walkway just looks like a mistake.
            float coverageHere = kind == RowKind.Lane
                ? Mathf.Clamp01(decor.coverage + decor.pathRowExtraCoverage)
                : decor.coverage;

            int hash = DecorHash(ix, iz);
            if (!pedestal && Hash01(hash) > coverageHere) continue;

            // Every column is filled from decor.origin.y up to its own top, so a
            // terraced neighbour never leaves a floating tile.
            int lift = decor.terrace > 0
                ? Mathf.FloorToInt(Hash01(hash ^ unchecked((int)0x9e3779b9)) * (decor.terrace + 1))
                : 0;
            // The minigame's own pedestal stands taller than the NPC's — the well
            // reads better perched a bit above the rest of the field.
            if (npcPedestal)  lift = Mathf.Max(lift, 1);
            if (gamePedestal) lift = Mathf.Max(lift, decor.minigamePedestalLift);

            coveredCols.Add(worldCol);
            colKind[worldCol] = kind;
            for (int y = 0; y <= lift; y++)
            {
                var c = new Vector3Int(worldCol.x, decor.origin.y + y, worldCol.y);
                cells.Add(c);
                occupied.Add(c);
                colTop[worldCol] = c;   // last write (highest y) wins
            }
        }
        if (cells.Count == 0) return;

        // Centre of the plot's footprint (not its cell centroid, which skews toward
        // wherever coverage happened to roll) — what the grow-in cutscene's camera
        // frames, so it's stable regardless of decor.coverage/terrace jitter.
        _decorCenter = gridSystem.GridToWorld(new Vector3Int(
            decor.origin.x + ext.x / 2, decor.origin.y, decor.origin.z + ext.y / 2));

        // Visual root. Deliberately carries NO LevelNode: LevelNode.Refresh()
        // (run by every RefreshNodes(), i.e. whenever ANYTHING changes anywhere
        // on the map) re-tints EVERY renderer beneath it flat to themeColor. That
        // would stomp the per-cell soil jitter — and the crop/fence/windmill
        // colours — back to one colour the next time the player edits the map.
        _decorRoot = new GameObject("AbundanceFarm");
        _decorRoot.transform.SetParent(transform, false);

        var br = _decorRoot.AddComponent<BlockRenderer>();
        br.cubePrefab = cubePrefab;
        var cellsArr = cells.ToArray();
        br.Render(Vector3Int.zero, cellsArr, cs, gridSystem);

        // BlockRenderer instantiates one cube per cell in array order, so the Nth
        // renderer is cells[N]. Read the renderers BEFORE anything else is
        // parented under the root, so later props can't shift the mapping.
        var soilRenderers = _decorRoot.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < soilRenderers.Length && i < cellsArr.Length; i++)
        {
            var c = cellsArr[i];
            float k = Mathf.Lerp(1f - decor.soilJitter, 1f + decor.soilJitter,
                                  Hash01(DecorHash(c.x, c.z) ^ (c.y * 92821)));
            MpbColor.Set(soilRenderers[i], Tint(decor.soilColor, k));
        }

        // ── Walkability ──────────────────────────────────────────────────────
        // Lives on a SEPARATE, non-rendering proxy — see the _decorRoot comment.
        // BuildSurface/SurfaceBfs read only LevelNode.cells, never the object's
        // own children, so this is exactly as walkable as owning the cubes.
        // level = null (a plain waypoint) and sourceBlock unset, since this was
        // never a player reward and must not be liftable. Same runtime-add
        // sequence every other node-spawning call site uses (see SpawnMapBlockNode).
        var proxy = new GameObject("AbundanceFarm_Walkable");
        proxy.transform.SetParent(transform, false);
        var ln = proxy.AddComponent<LevelNode>();
        ln.cells      = cellsArr;
        ln.level      = null;
        ln.isStart    = false;
        ln.themeColor = decor.soilColor;   // never seen (no renderers here) — kept sane regardless
        _nodes.Add(ln);
        LinkAllNodes();
        BuildSurface();
        RefreshNodes();

        // ── Props, largest first ─────────────────────────────────────────────
        if (decor.windmillEnabled) BuildWindmill(coveredCols, colTop, cs);
        if (decor.fenceEnabled)    BuildFence(coveredCols, colTop, cs);
        PlantBeds(occupied, colKind, cs);
        PlantResidents(colTop, ext, cs);

        // Sink the WHOLE field below ground — soil, fence, windmill, crops, blooms
        // are all children of _decorRoot, so one offset on the root moves them all
        // together. PlayDecorGrowthCutscene animates this back up to _decorRestPos.
        //
        // The controller's own GameObject isn't at the world origin in LevelSelect,
        // so animating back to Vector3.zero (instead of this) left the farm displaced.
        _decorRestPos = _decorRoot.transform.position;
        if (grow)
        {
            _decorRoot.transform.position = _decorRestPos + Vector3.down * decor.growRiseHeight;
            foreach (var r in _residents) if (r != null) r.SetActive(false);
        }
    }

    // ── Residents ────────────────────────────────────────────────────────────
    // The NPC and the minigame entrance, on the pedestal columns the ground pass
    // raised for them (see BuildDecor) — opposite thirds of the plot, so the two
    // never crowd each other.
    void PlantResidents(Dictionary<Vector2Int, Vector3Int> colTop, Vector2Int ext, float cs)
    {
        var npcCol  = new Vector2Int(decor.origin.x + ext.x / 3,     decor.origin.z + ext.y * 2 / 3);
        var gameCol = new Vector2Int(decor.origin.x + ext.x * 2 / 3, decor.origin.z + ext.y / 3);

        if (decor.npc != null && colTop.TryGetValue(npcCol, out var npcTop))
            SpawnResident(decor.npc, npcTop, cs, BuildNpcFigure);
        if (decor.minigame != null && colTop.TryGetValue(gameCol, out var gameTop))
            SpawnResident(decor.minigame, gameTop, cs, BuildStackingWell);
    }

    void SpawnResident(MapInteractable data, Vector3Int topCell, float cs,
                       System.Action<Transform, float> buildVisual)
    {
        // NOT parented to _decorRoot: that root gets sunk underground and animated
        // back up by the grow-in cutscene, which would drag residents with it and
        // leave MapInteractableSpot's own bob fighting the rise. They're hidden
        // outright for the duration instead (see _residents / PlayDecorGrowthCutscene)
        // so they don't hover over an empty field while it's still underground.
        var go = new GameObject($"Resident_{data.name}");
        go.transform.SetParent(transform, false);
        _residents.Add(go);

        var spot = go.AddComponent<MapInteractableSpot>();
        spot.data = data;
        spot.cell = topCell;
        spot.PlaceOn(BlockTop(topCell));

        buildVisual(go.transform, cs);

        // Clicking the FIGURE has to work, not just the ground under it — the
        // figure is what the player is aiming at. Its own parts are collider-free
        // mesh props, so this is the hit target.
        //
        // Deliberately parented to the controller and centred on the CELL, not on
        // the (bobbing) resident root: HandleClick derives the grid cell from
        // hit.collider.transform.position, so a collider sitting anywhere other
        // than the cell's centre resolves to the wrong cell — or to empty air
        // above it.
        var click = new GameObject($"ResidentClick_{data.name}");
        click.transform.SetParent(transform, false);
        click.transform.position = gridSystem.GridToWorld(topCell);
        var box = click.AddComponent<BoxCollider>();
        box.size = new Vector3(cs * 0.9f, cs * 2.2f, cs * 0.9f);
        box.center = new Vector3(0f, cs * 0.6f, 0f);   // spans the block and the figure above it
        _residents.Add(click);
    }

    // A simple standing figure — cream body, ink head, gold hat brim. Reads as
    // "someone is here" from the map camera without needing an art asset.
    void BuildNpcFigure(Transform root, float cs)
    {
        MakeMeshProp(root, "Body", TowerMesh(), root.position + Vector3.up * (cs * 0.02f),
                     Quaternion.identity, new Vector3(0.34f * cs, 0.62f * cs, 0.34f * cs),
                     decor.towerColor);
        MakeMeshProp(root, "Head", RailMesh(), root.position + Vector3.up * (cs * 0.74f),
                     Quaternion.identity, new Vector3(0.26f * cs, 0.24f * cs, 0.26f * cs),
                     GeoPalette.Ink);
        MakeMeshProp(root, "Brim", RailMesh(), root.position + Vector3.up * (cs * 0.86f),
                     Quaternion.identity, new Vector3(0.46f * cs, 0.05f * cs, 0.46f * cs),
                     decor.accentColor);
    }

    // The minigame entrance: a short shaft with a few coloured blocks resting in
    // it, so it reads as the stacking well it opens.
    void BuildStackingWell(Transform root, float cs)
    {
        MakeMeshProp(root, "Rim", RailMesh(), root.position + Vector3.up * (cs * 0.06f),
                     Quaternion.identity, new Vector3(0.78f * cs, 0.12f * cs, 0.78f * cs),
                     GeoPalette.Ink);

        // Four corner posts suggest a shaft you drop into.
        for (int i = 0; i < 4; i++)
        {
            float sx = (i & 1) == 0 ? -1f : 1f;
            float sz = (i & 2) == 0 ? -1f : 1f;
            MakeMeshProp(root, $"Post{i}", RailMesh(),
                         root.position + new Vector3(sx * 0.34f * cs, cs * 0.34f, sz * 0.34f * cs),
                         Quaternion.identity, new Vector3(0.09f * cs, 0.58f * cs, 0.09f * cs),
                         GeoPalette.Ink);
        }

        Color[] stack = { GeoPalette.Signal, decor.accentColor, GeoPalette.Blue };
        for (int i = 0; i < stack.Length; i++)
            MakeMeshProp(root, $"Block{i}", RailMesh(),
                         root.position + new Vector3(((i % 2) - 0.5f) * 0.24f * cs,
                                                     cs * (0.20f + i * 0.19f),
                                                     ((i / 2) - 0.5f) * 0.24f * cs),
                         Quaternion.Euler(0f, i * 22f, 0f),
                         Vector3.one * (0.30f * cs), stack[i]);
    }

    // Flowers, crops and signposts, driven by each cell's row role. One pass so a
    // cell can only ever be one thing.
    void PlantBeds(HashSet<Vector3Int> occupied, Dictionary<Vector2Int, RowKind> colKind, float cs)
    {
        var flowerTops = new List<Vector3>();
        var cropTops   = new List<Vector3>();
        var signTops   = new List<Vector3>();

        foreach (var c in occupied)
        {
            if (occupied.Contains(c + Vector3Int.up)) continue;   // buried — same test BuildSurface uses

            Vector3 top = gridSystem.GridToWorld(c) + Vector3.up * (cs * 0.5f);
            if (!colKind.TryGetValue(new Vector2Int(c.x, c.z), out var rowKind)) rowKind = RowKind.Flower;
            switch (rowKind)
            {
                case RowKind.Lane:
                    // Walkways stay clear, except for the occasional signpost —
                    // which is exactly where a real farm's markers stand.
                    if (signTops.Count < decor.signCount
                        && Hash01(DecorHash(c.x, c.z) ^ 0x27d4eb2d) > 0.88f)
                        signTops.Add(top);
                    break;

                case RowKind.Crop:
                    cropTops.Add(top);
                    break;

                default:
                    if (Hash01(DecorHash(c.x, c.z) ^ 0x5bd1e995) <= decor.bloomChance)
                        flowerTops.Add(top);
                    break;
            }
        }

        if (cropTops.Count > 0)   BuildCrops(cropTops, cs);
        for (int i = 0; i < signTops.Count; i++) BuildFarmSign(signTops[i], cs, i);
        if (flowerTops.Count > 0) BuildBlooms(flowerTops, cs);
    }

    void BuildBlooms(List<Vector3> tops, float cs)
    {
        var patchGo = new GameObject("Blooms");
        patchGo.transform.SetParent(_decorRoot.transform, false);
        var patch = patchGo.AddComponent<BloomPatch>();
        // Slower and softer than the gameplay version: ambient landscape the
        // player pans across, not feedback for an action they just took.
        patch.bloomDuration  = 0.6f;
        patch.bloomStagger   = 0.04f;
        patch.spinSpeed      = 10f;
        patch.swaySpeed      = 1.1f;
        patch.swayAngleDeg   = 6f;
        patch.bobAmplitude   = 0.035f * cs;
        patch.bobSpeed       = 0.9f;
        patch.stemHeight     = 0.22f * cs;

        patch.Grow(tops.ToArray(), DecorPetalPalette(), decor.accentColor,
                   maxFlowersPerCell: 3, flowerSizeWorld: 0.30f * cs,
                   scatterWorld: 0.28f * cs, maxFlowers: decor.maxFlowers);
    }

    // Crop beds: wheat sown in a straight line ACROSS each cell (along X, the row
    // direction), which is what makes them read as planted rather than scattered —
    // the flowers already own "scattered". One combined mesh per stalk (stem +
    // leaves + kernels), and one sway driver for the whole bed rather than a
    // component per stalk.
    void BuildCrops(List<Vector3> tops, float cs)
    {
        if (decor.stalksPerCell <= 0) return;

        var root = new GameObject("CropBeds");
        root.transform.SetParent(_decorRoot.transform, false);
        var field = root.AddComponent<FarmCropField>();

        float h = decor.stalkHeight * cs;

        // The sowing line follows the plot's rotated row direction, not world X.
        Vector3 along  = DecorRowDirWorld;
        Vector3 across = Vector3.Cross(Vector3.up, along);

        for (int t = 0; t < tops.Count; t++)
        {
            Vector3 top = tops[t];
            for (int s = 0; s < decor.stalksPerCell; s++)
            {
                // Evenly spaced across the cell, with a hair of jitter so the
                // line is hand-sown, not machine-printed.
                float u  = (s + 0.5f) / decor.stalksPerCell - 0.5f;
                int   hs = DecorHash(t * 31 + s, s * 17);
                Vector3 pos = top
                            + along  * (u * cs * 0.82f + (Hash01(hs) - 0.5f) * 0.12f * cs)
                            + across * ((Hash01(hs ^ 0x51ed) - 0.5f) * 0.24f * cs);

                float sh = h * Mathf.Lerp(0.8f, 1.2f, Hash01(hs ^ 0x2f9d));
                var stalk = MakeMeshProp(root.transform, "Wheat", WheatMesh(), pos,
                                         Quaternion.Euler(0f, Hash01(hs ^ 0x77a1) * 360f, 0f),
                                         new Vector3(sh, sh, sh),
                                         Tint(decor.cropColor, Mathf.Lerp(0.88f, 1.12f, Hash01(hs ^ 0x11b3))));

                field.Add(stalk, pos, Hash01(hs ^ 0x1234) * Mathf.PI * 2f);
            }
        }
    }

    // ── Fence ────────────────────────────────────────────────────────────────
    // A picket per boundary edge (a covered column whose horizontal neighbour is
    // NOT covered), gated by decor.fenceCoverage so gaps read as entrances. Each
    // picket also gets a rail running along the boundary toward the next edge
    // cell — pickets alone read as scattered sticks; the rail says "enclosure".
    void BuildFence(HashSet<Vector2Int> coveredCols, Dictionary<Vector2Int, Vector3Int> colTop, float cs)
    {
        var root = new GameObject("Fence");
        root.transform.SetParent(_decorRoot.transform, false);

        Vector2Int[] dirs = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };
        float postH = decor.fenceHeight * cs;

        foreach (var col in coveredCols)
        {
            if (!colTop.TryGetValue(col, out var top)) continue;
            foreach (var dir in dirs)
            {
                var ncol = new Vector2Int(col.x + dir.x, col.y + dir.y);
                if (coveredCols.Contains(ncol)) continue;   // interior — no boundary here

                int gate = DecorHash(col.x * 131 + dir.x * 7, col.y * 131 + dir.y * 7);
                if (Hash01(gate ^ unchecked((int)0xb5297a4d)) > decor.fenceCoverage) continue;

                Vector3 edge = gridSystem.GridToWorld(top)
                             + new Vector3(dir.x, 0f, dir.y) * (cs * 0.5f)
                             + Vector3.up * (cs * 0.5f);

                float lean = (Hash01(gate ^ 0x6d2b) - 0.5f) * 7f;   // a touch of settle, so the line isn't machined
                MakeMeshProp(root.transform, "Picket", PicketMesh(), edge,
                             Quaternion.Euler(lean, Hash01(gate) * 360f, lean * 0.5f),
                             new Vector3(0.16f * cs, postH, 0.16f * cs),
                             Tint(decor.fenceColor, Mathf.Lerp(0.85f, 1.12f, Hash01(gate ^ 0x9f1a))));

                // Rail toward the neighbouring boundary cell along this same
                // edge. `along` is perpendicular to the outward normal, so it
                // runs WITH the fence line rather than across it. Only drawn if
                // that neighbour is also on the boundary, so rails never jut out
                // into empty space at a corner.
                var along   = new Vector2Int(-dir.y, dir.x);
                var nextCol = new Vector2Int(col.x + along.x, col.y + along.y);
                if (!coveredCols.Contains(nextCol)) continue;
                if (coveredCols.Contains(new Vector2Int(nextCol.x + dir.x, nextCol.y + dir.y))) continue;

                // Two rails, upper and lower — one lone bar reads as a barrier,
                // two read as a fence.
                Vector3 railMid = edge + new Vector3(along.x, 0f, along.y) * (cs * 0.5f);
                Vector3 railScale = new Vector3(Mathf.Abs(along.x) * 1.02f + 0.06f, 0.05f,
                                                Mathf.Abs(along.y) * 1.02f + 0.06f) * cs;
                for (int r = 0; r < 2; r++)
                    MakeMeshProp(root.transform, "Rail", RailMesh(), railMid + Vector3.up * (postH * (0.42f + r * 0.32f)),
                                 Quaternion.identity, railScale,
                                 Tint(decor.fenceColor, 0.92f));
            }
        }
    }

    // ── Windmill ─────────────────────────────────────────────────────────────
    // Placed on the covered column furthest from 1-1 (i.e. lowest x, then lowest
    // z) so the landmark anchors the plot's far corner and never sits between the
    // camera and the beds.
    void BuildWindmill(HashSet<Vector2Int> coveredCols, Dictionary<Vector2Int, Vector3Int> colTop, float cs)
    {
        bool found = false;
        Vector2Int best = default;
        foreach (var col in coveredCols)
            if (!found || col.x < best.x || (col.x == best.x && col.y < best.y)) { best = col; found = true; }
        if (!found || !colTop.TryGetValue(best, out var top)) return;

        Vector3 basePos = gridSystem.GridToWorld(top) + Vector3.up * (cs * 0.5f);

        var root = new GameObject("Windmill");
        root.transform.SetParent(_decorRoot.transform, false);
        root.transform.position = basePos;
        // Rotor faces the plot's rotated frame. Set before the parts below — they're
        // placed in world space on the vertical axis, so a Y turn doesn't shift them,
        // while the hub/sails (local space) DO ride it.
        root.transform.rotation = Quaternion.Euler(0f, DecorRotationDegrees, 0f);

        float h = decor.windmillHeight * cs;

        // Tapered cream tower — one built mesh rather than stacked boxes, so the
        // batter (the inward slope of the walls) is continuous the way a real
        // mill's is. Ink plinth under it and an ink cap above keep the
        // constructivist ink/cream contrast the map's other markers use.
        MakeMeshProp(root.transform, "Plinth", RailMesh(), basePos + Vector3.up * (h * 0.02f),
                     Quaternion.identity, new Vector3(0.62f * cs, h * 0.05f, 0.62f * cs), GeoPalette.Ink);
        MakeMeshProp(root.transform, "Tower", TowerMesh(), basePos + Vector3.up * (h * 0.04f),
                     Quaternion.identity, new Vector3(0.5f * cs, h * 0.72f, 0.5f * cs), decor.towerColor);
        MakeMeshProp(root.transform, "Cap", TowerCapMesh(), basePos + Vector3.up * (h * 0.76f),
                     Quaternion.identity, new Vector3(0.46f * cs, h * 0.2f, 0.46f * cs), GeoPalette.Ink);
        // A broad near-up-facing gold collar — the one element guaranteed to
        // print at full gold (≈0.98 shade) regardless of camera yaw, so the
        // landmark still reads gold when the sails happen to be edge-on.
        MakeMeshProp(root.transform, "Collar", RailMesh(), basePos + Vector3.up * (h * 0.745f),
                     Quaternion.identity, new Vector3(0.54f * cs, h * 0.035f, 0.54f * cs), decor.accentColor);

        // Rotor: four slatted sails on a hub, turning in the vertical plane.
        // Their normals sweep through the emulated light as they turn, so the
        // sails shimmer bright↔dark — motion the player reads before any shape.
        var hub = new GameObject("Rotor").transform;
        hub.SetParent(root.transform, false);
        hub.localPosition = new Vector3(0f, h * 0.82f, -0.34f * cs);

        for (int i = 0; i < 4; i++)
            MakeMeshProp(hub, $"Sail_{i}", SailMesh(), Vector3.zero,
                         Quaternion.Euler(0f, 0f, i * 90f),
                         new Vector3(0.9f * cs, 0.9f * cs, 0.9f * cs),
                         decor.accentColor, localSpace: true);

        MakeMeshProp(hub, "Hub", RailMesh(), Vector3.zero, Quaternion.identity,
                     new Vector3(0.16f * cs, 0.16f * cs, 0.1f * cs), GeoPalette.Ink, localSpace: true);

        root.AddComponent<FarmWindmillSpin>().Init(hub, decor.windmillSpin);
    }

    // ── Signpost ─────────────────────────────────────────────────────────────
    // The flag is a broad plate tilted only ~12°, so it keeps a bright up-facing
    // normal and actually reads gold from the map camera (a fully-tilted plate
    // prints olive — see the class comment).
    void BuildFarmSign(Vector3 basePos, float cs, int index)
    {
        var root = new GameObject($"Signpost_{index}");
        root.transform.SetParent(_decorRoot.transform, false);
        root.transform.position = basePos;

        float postH = 0.66f * cs;
        float yaw   = Hash01(DecorHash(index * 977, index * 31)) * 360f;

        MakeMeshProp(root.transform, "Post", PicketMesh(), basePos,
                     Quaternion.Euler(0f, yaw, 0f), new Vector3(0.13f * cs, postH, 0.13f * cs),
                     decor.fenceColor);
        // Thin ink lip under the flag — the silkscreen "printed twice, slightly
        // offset" trick the level badges use, so the gold never looks like it's
        // floating free of the post.
        MakeMeshProp(root.transform, "FlagShadow", RailMesh(), basePos + Vector3.up * (postH * 0.9f),
                     Quaternion.Euler(12f, yaw, 0f), new Vector3(0.52f * cs, 0.03f * cs, 0.36f * cs),
                     GeoPalette.Ink);
        MakeMeshProp(root.transform, "Flag", RailMesh(), basePos + Vector3.up * (postH * 0.94f),
                     Quaternion.Euler(12f, yaw, 0f), new Vector3(0.48f * cs, 0.05f * cs, 0.32f * cs),
                     decor.accentColor);
    }

    // Spawns one shared-mesh prop. Mirrors MakePlate's contract (collider-free,
    // MonumentMaterial + per-instance MPB colour, no shadow casting) but takes a
    // built mesh instead of a primitive cube.
    Transform MakeMeshProp(Transform parent, string name, Mesh mesh, Vector3 pos,
                           Quaternion rot, Vector3 scale, Color color, bool localSpace = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        if (localSpace) go.transform.localPosition = pos;
        else            go.transform.position      = pos;
        go.transform.localRotation = rot;
        go.transform.localScale    = scale;

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial    = MonumentMaterial();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows    = false;
        MpbColor.Set(mr, color);
        return go.transform;
    }

    // Analogous spread around Abundance's orange, matching how AbundanceVisualizer
    // builds its palette — a multi-colour field that still reads as one theme.
    Color[] DecorPetalPalette()
    {
        Color.RGBToHSV(Color.Lerp(BlockColorPalette.Get(BlockColor.Abundance), Color.white, 0.2f),
                       out float h, out float s, out float v);
        s = Mathf.Clamp(s, 0.45f, 0.92f);
        v = Mathf.Clamp(v, 0.70f, 1f);
        const float sp = 0.06f;
        return new[]
        {
            Color.HSVToRGB(Mathf.Repeat(h,           1f), s,                       v),
            Color.HSVToRGB(Mathf.Repeat(h + sp,      1f), Mathf.Clamp01(s * 0.88f), Mathf.Clamp01(v * 1.03f)),
            Color.HSVToRGB(Mathf.Repeat(h - sp,      1f), Mathf.Clamp01(s),         Mathf.Clamp01(v * 0.94f)),
            Color.HSVToRGB(Mathf.Repeat(h + sp * 2f, 1f), Mathf.Clamp01(s * 0.78f), Mathf.Clamp01(v)),
        };
    }

    static Color Tint(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

    static int DecorHash(int x, int z)
    {
        unchecked { return x * 73856093 ^ z * 83492791; }
    }

    static float Hash01(int h)
    {
        unchecked
        {
            h = (h ^ 61) ^ (h >> 16);
            h += h << 3;
            h ^= h >> 4;
            h *= 0x27d4eb2d;
            h ^= h >> 15;
        }
        return (h & 0x7fffffff) / (float)0x7fffffff;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Procedural prop meshes
    //
    // Built once and shared for the process (hideFlags.DontSave), exactly how
    // BloomPatch caches its flower archetypes. All are UNIT-space — 1 tall,
    // ±0.5 wide — so the caller's localScale sets the real size. Colour is
    // per-instance via MPB, so one mesh + one material still batches.
    // ═════════════════════════════════════════════════════════════════════════

    static Mesh _wheatMesh, _sailMesh, _picketMesh, _railMesh, _towerMesh, _towerCapMesh;

    static void Quad(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int s = v.Count;
        v.Add(a); v.Add(b); v.Add(c); v.Add(d);
        t.Add(s); t.Add(s + 1); t.Add(s + 2);
        t.Add(s); t.Add(s + 2); t.Add(s + 3);
    }

    static void Tri(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c)
    {
        int s = v.Count;
        v.Add(a); v.Add(b); v.Add(c);
        t.Add(s); t.Add(s + 1); t.Add(s + 2);
    }

    static Mesh Finish(string name, List<Vector3> v, List<int> t)
    {
        var m = new Mesh { name = name, hideFlags = HideFlags.DontSave };
        m.SetVertices(v);
        m.SetTriangles(t, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // Wheat: a tapered cross-quad stem, two arcing leaf blades near the base, and
    // a head of alternating kernels climbing a central spike. This is the piece
    // that has to hold up next to BloomPatch's layered petals, so it gets real
    // kernels rather than a capsule.
    static Mesh WheatMesh()
    {
        if (_wheatMesh != null) return _wheatMesh;
        var v = new List<Vector3>();
        var t = new List<int>();

        const float stemTop = 0.55f;
        // Stem — two crossed tapered strips so it reads from any angle.
        foreach (var axis in new[] { Vector3.right, Vector3.forward })
        {
            Quad(v, t,
                 -axis * 0.035f,
                  axis * 0.035f,
                  axis * 0.018f + Vector3.up * stemTop,
                 -axis * 0.018f + Vector3.up * stemTop);
        }

        // Leaf blades — long thin triangles arcing away from the stem.
        for (int i = 0; i < 2; i++)
        {
            Vector3 dir = i == 0 ? new Vector3(1f, 0f, 0.3f) : new Vector3(-0.85f, 0f, -0.45f);
            dir.Normalize();
            Vector3 root0 = Vector3.up * (0.12f + i * 0.1f);
            Vector3 mid   = root0 + dir * 0.16f + Vector3.up * 0.16f;
            Vector3 tip   = root0 + dir * 0.30f + Vector3.up * 0.06f;
            Vector3 side  = Vector3.Cross(dir, Vector3.up) * 0.035f;
            Quad(v, t, root0 - side, root0 + side, mid + side * 0.6f, mid - side * 0.6f);
            Tri (v, t, mid - side * 0.6f, mid + side * 0.6f, tip);
        }

        // Spike + kernels. Six pairs, alternating sides, tilted up ~40° and
        // shrinking toward the tip.
        Quad(v, t,
             new Vector3(-0.014f, stemTop, 0f), new Vector3(0.014f, stemTop, 0f),
             new Vector3(0.010f, 1f, 0f),       new Vector3(-0.010f, 1f, 0f));

        const int pairs = 6;
        for (int i = 0; i < pairs; i++)
        {
            float f  = i / (float)(pairs - 1);
            float y  = Mathf.Lerp(stemTop + 0.02f, 0.95f, f);
            float len = Mathf.Lerp(0.115f, 0.055f, f);
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                // Alternate the pair's axis so kernels spiral rather than forming
                // two flat rows — the read from a top-down camera is much denser.
                Vector3 outDir = (i % 2 == 0 ? Vector3.right : Vector3.forward) * sgn;
                Vector3 baseP  = Vector3.up * y;
                Vector3 tipP   = baseP + outDir * len + Vector3.up * (len * 0.85f);
                Vector3 midP   = baseP + outDir * (len * 0.45f) + Vector3.up * (len * 0.30f);
                Vector3 wide   = Vector3.Cross(outDir, Vector3.up) * 0.028f;
                Quad(v, t, baseP, midP + wide, tipP, midP - wide);
            }
        }

        _wheatMesh = Finish("FarmWheat", v, t);
        return _wheatMesh;
    }

    // Windmill sail: a central spine with slats crossing it, shortening toward
    // the tip — the classic lattice, and far more legible in silhouette than a
    // solid blade would be.
    static Mesh SailMesh()
    {
        if (_sailMesh != null) return _sailMesh;
        var v = new List<Vector3>();
        var t = new List<int>();

        const float len = 1f;
        // Spine, from hub (y=0) to tip.
        Quad(v, t,
             new Vector3(-0.045f, 0.06f, 0f), new Vector3(0.045f, 0.06f, 0f),
             new Vector3( 0.030f, len,   0f), new Vector3(-0.030f, len,  0f));

        // Leading-edge rail, offset to one side — gives the sail a direction
        // (which way it "catches"), so a spinning rotor doesn't look symmetric.
        Quad(v, t,
             new Vector3(0.045f, 0.10f, 0f), new Vector3(0.175f, 0.10f, 0f),
             new Vector3(0.105f, len * 0.96f, 0f), new Vector3(0.055f, len * 0.96f, 0f));

        const int slats = 7;
        for (int i = 0; i < slats; i++)
        {
            float f = (i + 0.5f) / slats;
            float y = Mathf.Lerp(0.14f, len * 0.94f, f);
            float half = Mathf.Lerp(0.20f, 0.085f, f);
            float th   = Mathf.Lerp(0.030f, 0.018f, f);
            Quad(v, t,
                 new Vector3(-half, y - th, 0f), new Vector3(half, y - th, 0f),
                 new Vector3( half, y + th, 0f), new Vector3(-half, y + th, 0f));
        }

        _sailMesh = Finish("FarmSail", v, t);
        return _sailMesh;
    }

    // Fence picket: a square post that tapers inward as it rises, capped with a
    // pyramid point. Unit height, ±0.5 footprint.
    static Mesh PicketMesh()
    {
        if (_picketMesh != null) return _picketMesh;
        var v = new List<Vector3>();
        var t = new List<int>();

        const float hb = 0.5f;    // half-width at the base
        const float ht = 0.36f;   // half-width where the cap starts
        const float capY = 0.84f;

        Vector3[] baseRing = { new(-hb, 0f, -hb), new(hb, 0f, -hb), new(hb, 0f, hb), new(-hb, 0f, hb) };
        Vector3[] topRing  = { new(-ht, capY, -ht), new(ht, capY, -ht), new(ht, capY, ht), new(-ht, capY, ht) };

        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            Quad(v, t, baseRing[i], baseRing[j], topRing[j], topRing[i]);
        }
        Vector3 apex = new(0f, 1f, 0f);
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            Tri(v, t, topRing[i], topRing[j], apex);
        }

        _picketMesh = Finish("FarmPicket", v, t);
        return _picketMesh;
    }

    // Plain unit box, centred — rails, plinths, flags. Kept as its own mesh
    // rather than a CreatePrimitive cube so every farm prop shares one code path
    // (and every prop is collider-free by construction).
    static Mesh RailMesh()
    {
        if (_railMesh != null) return _railMesh;
        var v = new List<Vector3>();
        var t = new List<int>();
        const float h = 0.5f;

        Quad(v, t, new(-h, -h,  h), new( h, -h,  h), new( h,  h,  h), new(-h,  h,  h));   // +Z
        Quad(v, t, new( h, -h, -h), new(-h, -h, -h), new(-h,  h, -h), new( h,  h, -h));   // -Z
        Quad(v, t, new( h, -h,  h), new( h, -h, -h), new( h,  h, -h), new( h,  h,  h));   // +X
        Quad(v, t, new(-h, -h, -h), new(-h, -h,  h), new(-h,  h,  h), new(-h,  h, -h));   // -X
        Quad(v, t, new(-h,  h,  h), new( h,  h,  h), new( h,  h, -h), new(-h,  h, -h));   // +Y
        Quad(v, t, new(-h, -h, -h), new( h, -h, -h), new( h, -h,  h), new(-h, -h,  h));   // -Y

        _railMesh = Finish("FarmRail", v, t);
        return _railMesh;
    }

    // Windmill tower: an octagonal drum with batter (walls sloping inward as they
    // rise), plus two banding rings. Octagonal rather than round — it keeps the
    // faceted, printed look the rest of the map uses.
    static Mesh TowerMesh()
    {
        if (_towerMesh != null) return _towerMesh;
        var v = new List<Vector3>();
        var t = new List<int>();

        const int sides = 8;
        // (height, half-radius) — the two repeated radii are the banding rings,
        // which catch the face shading as a visible horizontal stripe.
        float[,] profile =
        {
            { 0.00f, 0.50f }, { 0.30f, 0.44f }, { 0.34f, 0.46f },
            { 0.38f, 0.43f }, { 0.72f, 0.37f }, { 0.76f, 0.39f },
            { 0.80f, 0.36f }, { 1.00f, 0.32f },
        };

        for (int s = 0; s < profile.GetLength(0) - 1; s++)
        {
            float y0 = profile[s, 0],     r0 = profile[s, 1];
            float y1 = profile[s + 1, 0], r1 = profile[s + 1, 1];
            for (int i = 0; i < sides; i++)
            {
                float a0 = (i / (float)sides) * Mathf.PI * 2f;
                float a1 = ((i + 1) / (float)sides) * Mathf.PI * 2f;
                Quad(v, t,
                     new Vector3(Mathf.Cos(a0) * r0, y0, Mathf.Sin(a0) * r0),
                     new Vector3(Mathf.Cos(a1) * r0, y0, Mathf.Sin(a1) * r0),
                     new Vector3(Mathf.Cos(a1) * r1, y1, Mathf.Sin(a1) * r1),
                     new Vector3(Mathf.Cos(a0) * r1, y1, Mathf.Sin(a0) * r1));
            }
        }

        _towerMesh = Finish("FarmTower", v, t);
        return _towerMesh;
    }

    // Windmill cap: an octagonal dome-ish roof closing the tower.
    static Mesh TowerCapMesh()
    {
        if (_towerCapMesh != null) return _towerCapMesh;
        var v = new List<Vector3>();
        var t = new List<int>();

        const int sides = 8;
        float[,] profile = { { 0.00f, 0.50f }, { 0.55f, 0.42f }, { 0.85f, 0.24f } };

        for (int s = 0; s < profile.GetLength(0) - 1; s++)
        {
            float y0 = profile[s, 0],     r0 = profile[s, 1];
            float y1 = profile[s + 1, 0], r1 = profile[s + 1, 1];
            for (int i = 0; i < sides; i++)
            {
                float a0 = (i / (float)sides) * Mathf.PI * 2f;
                float a1 = ((i + 1) / (float)sides) * Mathf.PI * 2f;
                Quad(v, t,
                     new Vector3(Mathf.Cos(a0) * r0, y0, Mathf.Sin(a0) * r0),
                     new Vector3(Mathf.Cos(a1) * r0, y0, Mathf.Sin(a1) * r0),
                     new Vector3(Mathf.Cos(a1) * r1, y1, Mathf.Sin(a1) * r1),
                     new Vector3(Mathf.Cos(a0) * r1, y1, Mathf.Sin(a0) * r1));
            }
        }
        // Close the top.
        Vector3 apex = new(0f, 1f, 0f);
        float rTop = profile[profile.GetLength(0) - 1, 1];
        float yTop = profile[profile.GetLength(0) - 1, 0];
        for (int i = 0; i < sides; i++)
        {
            float a0 = (i / (float)sides) * Mathf.PI * 2f;
            float a1 = ((i + 1) / (float)sides) * Mathf.PI * 2f;
            Tri(v, t,
                new Vector3(Mathf.Cos(a0) * rTop, yTop, Mathf.Sin(a0) * rTop),
                new Vector3(Mathf.Cos(a1) * rTop, yTop, Mathf.Sin(a1) * rTop),
                apex);
        }

        _towerCapMesh = Finish("FarmTowerCap", v, t);
        return _towerCapMesh;
    }

    // One driver for a whole crop bed rather than a component per stalk: a few
    // hundred stalks would otherwise mean a few hundred Update() calls for what
    // is one wind field.
    class FarmCropField : MonoBehaviour
    {
        struct Stalk
        {
            public Transform  t;
            public Vector3    basePos;
            public Quaternion baseRot;
            public float      phase;
        }

        readonly List<Stalk> _stalks = new();

        public void Add(Transform t, Vector3 basePos, float phase)
        {
            if (t == null) return;
            _stalks.Add(new Stalk { t = t, basePos = basePos, baseRot = t.localRotation, phase = phase });
        }

        void Update()
        {
            float time = Time.time;
            for (int i = 0; i < _stalks.Count; i++)
            {
                var s = _stalks[i];
                if (s.t == null) continue;

                // One shared wind direction with a per-stalk phase, so the bed
                // ripples as a field instead of each stalk wobbling on its own.
                // The mesh's pivot is already at its base, so a plain rotation
                // hinges at the ground with no position correction needed.
                float lean = Mathf.Sin(time * 1.3f + s.phase) * 6f;
                s.t.localRotation = Quaternion.Euler(lean * 0.55f, 0f, lean) * s.baseRot;
            }
        }
    }

    // Sails turn at a constant rate; the tower stays put.
    class FarmWindmillSpin : MonoBehaviour
    {
        Transform _hub;
        float     _speed;

        public void Init(Transform hub, float speed) { _hub = hub; _speed = speed; }

        void Update()
        {
            if (_hub != null) _hub.Rotate(0f, 0f, _speed * Time.deltaTime, Space.Self);
        }
    }
}
