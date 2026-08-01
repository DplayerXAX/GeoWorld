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

public partial class LevelMapController : MonoBehaviour
{
    [Header("Abundance farm — plot")]
    [Tooltip("Off = never build the farm, whatever the save says.")]
    public bool decorEnabled = true;
    [Tooltip("The farm appears once THIS level has been cleared. Blank = always on.")]
    public string decorGateLevelId = "1-1";
    [Tooltip("Lowest-x / lowest-z corner of the plot, in grid cells. Default puts the plot's right edge at x0, one cell clear of 1-1 (which occupies x1..3, y2, z5..6).")]
    public Vector3Int decorOrigin = new Vector3Int(-11, 2, 0);
    [Tooltip("Plot footprint in cells (x, z) BEFORE decorRotationSteps. Height comes from decorTerrace.")]
    public Vector2Int decorSize = new Vector2Int(12, 11);
    [Tooltip("Turns the whole plot 90° clockwise per step (0-3): crop/lane rows, the windmill's rotor facing and the wheat's sowing line all rotate with it. The rotated footprint is re-anchored so decorOrigin always stays its lowest-x/lowest-z corner — rotating never makes you go hunting for where the farm went.")]
    [Range(0, 3)] public int decorRotationSteps = 0;
    [Tooltip("Fraction of plot cells that actually get a block — below 1 the edge frays into the surrounding void instead of ending on a hard rectangle.")]
    [Range(0.3f, 1f)] public float decorCoverage = 0.82f;
    [Tooltip("Cells of height variation the plot terraces over. 0 = perfectly flat.")]
    [Range(0, 3)] public int decorTerrace = 1;
    [Tooltip("Tint of the soil blocks. A muted earth so the flowers, not the ground, carry the colour.")]
    public Color decorSoilColor = new Color(0.34f, 0.26f, 0.18f);
    [Tooltip("±brightness jitter per soil block, so the ground reads as tilled earth rather than one flat slab.")]
    [Range(0f, 0.3f)] public float decorSoilJitter = 0.12f;

    [Header("Abundance farm — beds")]
    [Tooltip("Row cycle along Z: every Nth row is a bare walking lane, and the planted rows between lanes alternate flower bed / crop bed. 0 disables row structure (uniform meadow).")]
    public int decorPathRowEvery = 3;
    [Tooltip("Walking lanes get this much MORE coverage than decorCoverage, so they read as deliberate solid ground rather than more frayed edge.")]
    [Range(0f, 0.4f)] public float decorPathRowExtraCoverage = 0.18f;
    [Tooltip("Chance a flower-bed cell actually blooms. The rest stay bare tilled dirt.")]
    [Range(0f, 1f)] public float decorBloomChance = 0.8f;
    [Tooltip("Hard cap on flowers. Each bloom is ~6 GameObjects, so this is the main cost dial for the whole farm.")]
    public int decorMaxFlowers = 170;

    [Header("Abundance farm — crops")]
    [Tooltip("Wheat stalks per crop-bed cell, sown in a line across the row.")]
    [Range(0, 6)] public int decorStalksPerCell = 4;
    [Tooltip("Stalk height as a fraction of one cell.")]
    [Range(0.2f, 1.5f)] public float decorStalkHeight = 0.75f;
    [Tooltip("Wheat stem/leaf green-ochre. The kernels use the gold accent on top of this.")]
    public Color decorCropColor = new Color(0.62f, 0.58f, 0.24f);

    [Header("Abundance farm — fence")]
    [Tooltip("Ring the plot's outer edge with pickets and connecting rails.")]
    public bool decorFenceEnabled = true;
    [Tooltip("Fraction of boundary edges that get a picket — the rest are gaps, so the fence reads as a working boundary with entrances rather than a solid wall.")]
    [Range(0f, 1f)] public float decorFenceCoverage = 0.78f;
    [Tooltip("Picket height as a fraction of one cell.")]
    [Range(0.2f, 1.2f)] public float decorFenceHeight = 0.55f;
    [Tooltip("Weathered timber. NOT ink — a black fence around dark soil turns the plot's whole outline into a dead band.")]
    public Color decorFenceColor = new Color(0.55f, 0.40f, 0.26f);

    [Header("Abundance farm — landmark")]
    [Tooltip("A windmill at the plot's far corner: the element that makes this read as a farm from across the map.")]
    public bool decorWindmillEnabled = true;
    [Tooltip("Windmill height in cells, tower base to hub.")]
    [Range(1f, 5f)] public float decorWindmillHeight = 2.6f;
    [Tooltip("Sail rotation, degrees/sec.")]
    public float decorWindmillSpin = 26f;
    [Tooltip("Windmill tower body. Cream rather than ink so the landmark reads bright against the dark soil.")]
    public Color decorTowerColor = new Color(0.88f, 0.85f, 0.78f);
    [Tooltip("Gold for the sails and the signpost flags — the farm's accent role.")]
    public Color decorAccentColor = new Color(0.98f, 0.80f, 0.30f);
    [Tooltip("Signpost flags planted beside the walking lanes, up to this many.")]
    [Range(0, 10)] public int decorSignCount = 4;

    [Header("Abundance farm — grow-in cutscene")]
    [Tooltip("How far below its resting position the whole field starts, in world units, the ONE time the grow-in cutscene plays.")]
    public float decorGrowRiseHeight = 3f;
    [Tooltip("Seconds for the field to rise from underground into its resting position.")]
    public float decorGrowRiseDuration = 1.8f;
    [Tooltip("Extra seconds the camera holds on the field after it settles — gives the flowers' own bloom-in (BloomPatch) time to finish before the camera lets go and dialogue/gameplay resumes.")]
    public float decorGrowHoldSeconds = 1.4f;
    [Tooltip("Orbit zoom while the cutscene camera is locked on the field (smaller = closer). 0 = keep whatever zoom the map already had.")]
    public float decorGrowZoom = 0f;
    [Tooltip("Degrees added to the camera's current yaw for the reveal shot (positive = turns right). The reveal otherwise just inherits whatever angle the player happened to be looking from.")]
    public float decorGrowYawOffset = 90f;
    [Tooltip("Seconds to fade to/from black for the hand-off from the grow-in reveal into whatever dialogue/tutorial plays next (see PlayDecorGrowthCutscene). Hides the camera's own snap-back — focusViewport resets instantly there, and the reward dialogue may immediately re-target the camera again, so trying to keep that cut on-screen always reads as a pop no matter how the camera itself eases.")]
    public float decorTransitionFadeDuration = 0.6f;
    [Tooltip("Aside bubble shown once, while the camera holds on the newly grown farm. Blank = no bubble.")]
    [TextArea] public string decorGrowAsideText =
        "As Abundance returned, a small farm blossomed where countless wishes had been sown.";
    [Tooltip("Seconds the aside above stays up. The camera's hold is stretched to cover it when this is longer than decorGrowHoldSeconds, so the line is never cut off mid-read by the fade to black.")]
    public float decorGrowAsideSeconds = 4.5f;

    GameObject _decorRoot;
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
        if (decorPathRowEvery <= 0) return RowKind.Flower;
        if (localZ % decorPathRowEvery == decorPathRowEvery - 1) return RowKind.Lane;
        // Band index between lanes — alternate the bands so beds stripe rather
        // than every planted row looking identical.
        return (localZ / decorPathRowEvery) % 2 == 1 ? RowKind.Crop : RowKind.Flower;
    }

    // ── Plot rotation ────────────────────────────────────────────────────────
    // Everything about the farm is authored in PLOT-LOCAL space: rows run along
    // local +X and step along local +Z (RowKindAt), wheat is sown in a line along
    // local +X, and the windmill's rotor faces local -Z. decorRotationSteps turns
    // that whole local frame, so one number rotates the layout AND the props
    // together — as opposed to swapping decorSize's components, which only
    // reshapes the footprint and leaves every directional prop facing its
    // original way.
    //
    // Each step maps local (ix, iz) so the result stays non-negative and anchored
    // at (0,0), which is what keeps decorOrigin meaning "lowest-x/lowest-z corner"
    // at every rotation.
    // Matches Quaternion.Euler(0, steps*90, 0) exactly — a +90° Y turn sends local
    // +X to world -Z and local +Z to world +X (clockwise seen from above), and the
    // "+(w-1)" / "+(d-1)" terms are just the re-anchoring back to non-negative.
    Vector2Int RotateLocal(int ix, int iz, int w, int d) => (decorRotationSteps & 3) switch
    {
        1 => new Vector2Int(iz, w - 1 - ix),
        2 => new Vector2Int(w - 1 - ix, d - 1 - iz),
        3 => new Vector2Int(d - 1 - iz, ix),
        _ => new Vector2Int(ix, iz),
    };

    // Footprint extent after rotation — odd steps swap width and depth.
    Vector2Int RotatedExtent(int w, int d) =>
        (decorRotationSteps & 1) == 1 ? new Vector2Int(d, w) : new Vector2Int(w, d);

    float DecorRotationDegrees => (decorRotationSteps & 3) * 90f;

    // Local +X (the row / sowing direction) expressed in world space.
    Vector3 DecorRowDirWorld => Quaternion.Euler(0f, DecorRotationDegrees, 0f) * Vector3.right;

    // Called from Start() once the map exists. Silent no-op (returns false) until
    // the gate level has actually been cleared, so a fresh save sees plain empty
    // ground and the farm is a visible reward for finishing the level. Returns
    // true only on the ONE visit where the grow-in cutscene should play.
    bool TryBuildDecor()
    {
        if (!decorEnabled || _decorRoot != null) return false;
        if (gridSystem == null || cubePrefab == null) return false;

        if (!string.IsNullOrEmpty(decorGateLevelId))
        {
            var rec = SaveSystem.Profile.GetRecord(decorGateLevelId);
            if (rec == null || !rec.cleared) return false;
        }

        // One-shot: only the visit RIGHT AFTER this level's first clear plays the
        // grow-in cutscene (see RunConfig.PendingMapGrowthLevelId). Every later
        // revisit — this session or a future one — just silently rebuilds the same
        // deterministic field instantly, exactly like before this feature existed.
        bool grow = RunConfig.PendingMapGrowthLevelId == decorGateLevelId;
        if (grow) RunConfig.PendingMapGrowthLevelId = null;   // consume once regardless of what BuildDecor does below

        BuildDecor(grow);
        return grow;
    }

    // Camera-locked reveal played exactly once, right after this field's gate
    // level is first cleared — BEFORE any first-visit/reward dialogue (see the
    // call site in Start()). Blocks player input for its duration (see the
    // _decorCutscenePlaying check in Update()) so the player can't start walking
    // or building mid-reveal. The field itself was already sunk below ground by
    // BuildDecor(grow: true); this just rises it back up while the camera holds.
    IEnumerator PlayDecorGrowthCutscene()
    {
        _decorCutscenePlaying = true;

        if (_orbit != null)
        {
            _orbit.focusViewport = new Vector2(0.5f, 0.5f);   // dead-centre for the reveal, not the usual left-biased map framing
            _orbit.FocusOnPoint(_decorCenter, snap: false);
            if (decorGrowZoom > 0f) _orbit.SetZoom(decorGrowZoom);
            // Rotates the shot away from whatever angle the player happened to be
            // looking from before the cutscene started — position eases toward the
            // new angle every frame regardless (see OrbitCamera.LateUpdate), so this
            // one-shot yaw bump still reads as a smooth swing, not a snap.
            _orbit.AddYaw(decorGrowYawOffset);
        }

        // Narration rides the reveal rather than following it: the bubble slides in
        // as the field starts pushing up, so the words and the thing they describe
        // are one beat instead of a sentence arriving after the show is over.
        if (!string.IsNullOrEmpty(decorGrowAsideText))
            AsideBubble.Show(defaultCharacter, "default", decorGrowAsideText, decorGrowAsideSeconds);

        Vector3 sunk = _decorRoot != null ? _decorRoot.transform.position : _decorRestPos;
        float t = 0f;
        while (t < decorGrowRiseDuration)
        {
            t += Time.deltaTime;
            float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / decorGrowRiseDuration), 3f);   // ease-out cubic, no overshoot
            if (_decorRoot != null) _decorRoot.transform.position = Vector3.Lerp(sunk, _decorRestPos, e);
            yield return null;
        }
        if (_decorRoot != null) _decorRoot.transform.position = _decorRestPos;

        // The camera still waits for the line to finish rather than the other way
        // round — the default hold (~1.4s) is tuned for watching the field settle,
        // which is shorter than this sentence takes to read, and the fade to black
        // would otherwise cut it off mid-read. The bubble's clock started back at
        // the rise, so only what's LEFT of it needs covering here.
        float hold = decorGrowHoldSeconds;
        if (!string.IsNullOrEmpty(decorGrowAsideText))
            hold = Mathf.Max(hold, decorGrowAsideSeconds + AsideBubble.SlideSeconds - decorGrowRiseDuration);
        yield return new WaitForSeconds(hold);

        // Fade to black for the hand-off instead of an eased pan — focusViewport
        // resets synchronously right below (OrbitCamera reads it every frame with
        // no lerp of its own), and PlayEntryDialogueIfAny() may immediately
        // re-target the camera AGAIN to the reward's granting level node, so any
        // attempt to keep this moment on-screen ends up reading as a pop no matter
        // how the camera position itself eases.
        yield return FadeScreen(0f, 1f, decorTransitionFadeDuration);

        // Hand the camera back to wherever the pawn actually is, with the map's
        // normal left-biased framing, before whatever dialogue/tutorial comes next.
        // Safe to snap now — it's happening behind the fade.
        if (_orbit != null)
        {
            _orbit.focusViewport = new Vector2(focusViewportX, focusViewportY);
            _orbit.FocusOnPoint(_camFocus, snap: true);
        }

        PlayEntryDialogueIfAny();   // may re-focus again (reward conversation) — still hidden

        yield return FadeScreen(1f, 0f, decorTransitionFadeDuration);
        _decorCutscenePlaying = false;
    }

    void BuildDecor(bool grow)
    {
        var cells       = new List<Vector3Int>();   // in BlockRenderer instantiation order
        var occupied    = new HashSet<Vector3Int>();
        var coveredCols = new HashSet<Vector2Int>();
        var colTop      = new Dictionary<Vector2Int, Vector3Int>();

        int w = Mathf.Max(1, decorSize.x);
        int d = Mathf.Max(1, decorSize.y);
        float cs = gridSystem.cellSize;

        // Which row role each world column ended up with. Recorded here rather than
        // recovered later from the cell's own z: once the plot can be rotated, world
        // z no longer maps back to the local row index the roles were authored in.
        var colKind = new Dictionary<Vector2Int, RowKind>();

        // ── Ground ───────────────────────────────────────────────────────────
        for (int ix = 0; ix < w; ix++)
        for (int iz = 0; iz < d; iz++)
        {
            var kind = RowKindAt(iz);
            // Lanes are deliberately MORE likely to be covered — they're the
            // farm's walkways, and a frayed walkway just looks like a mistake.
            float coverageHere = kind == RowKind.Lane
                ? Mathf.Clamp01(decorCoverage + decorPathRowExtraCoverage)
                : decorCoverage;

            int hash = DecorHash(ix, iz);
            if (Hash01(hash) > coverageHere) continue;

            // Every column is filled from decorOrigin.y up to its own top, so a
            // terraced neighbour never leaves a floating tile.
            int lift = decorTerrace > 0
                ? Mathf.FloorToInt(Hash01(hash ^ unchecked((int)0x9e3779b9)) * (decorTerrace + 1))
                : 0;

            var rot = RotateLocal(ix, iz, w, d);
            var worldCol = new Vector2Int(decorOrigin.x + rot.x, decorOrigin.z + rot.y);
            coveredCols.Add(worldCol);
            colKind[worldCol] = kind;
            for (int y = 0; y <= lift; y++)
            {
                var c = new Vector3Int(worldCol.x, decorOrigin.y + y, worldCol.y);
                cells.Add(c);
                occupied.Add(c);
                colTop[worldCol] = c;   // last write (highest y) wins
            }
        }
        if (cells.Count == 0) return;

        // Centre of the plot's footprint (not its cell centroid, which skews toward
        // wherever coverage happened to roll) — what the grow-in cutscene's camera
        // frames, so it's stable regardless of decorCoverage/terrace jitter.
        var ext = RotatedExtent(w, d);
        _decorCenter = gridSystem.GridToWorld(new Vector3Int(
            decorOrigin.x + ext.x / 2, decorOrigin.y, decorOrigin.z + ext.y / 2));

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
            float k = Mathf.Lerp(1f - decorSoilJitter, 1f + decorSoilJitter,
                                  Hash01(DecorHash(c.x, c.z) ^ (c.y * 92821)));
            MpbColor.Set(soilRenderers[i], Tint(decorSoilColor, k));
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
        ln.themeColor = decorSoilColor;   // never seen (no renderers here) — kept sane regardless
        _nodes.Add(ln);
        LinkAllNodes();
        BuildSurface();
        RefreshNodes();

        // ── Props, largest first ─────────────────────────────────────────────
        if (decorWindmillEnabled) BuildWindmill(coveredCols, colTop, cs);
        if (decorFenceEnabled)    BuildFence(coveredCols, colTop, cs);
        PlantBeds(occupied, colKind, cs);

        // Sink the WHOLE field below ground — soil, fence, windmill, crops, blooms
        // are all children of _decorRoot, so one offset on the root moves them all
        // together. PlayDecorGrowthCutscene animates this back up to _decorRestPos.
        //
        // _decorRestPos is the root's REAL resting position, not the world origin:
        // _decorRoot is parented to this controller, whose GameObject is NOT at the
        // origin in LevelSelect. Animating back to Vector3.zero (as this first did)
        // therefore left the whole farm permanently displaced by the controller's own
        // offset — which is exactly the fractional-cell gap that opened up between
        // the farm and the rest of the map, and only ever after the cutscene ran.
        _decorRestPos = _decorRoot.transform.position;
        if (grow) _decorRoot.transform.position = _decorRestPos + Vector3.down * decorGrowRiseHeight;
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
                    if (signTops.Count < decorSignCount
                        && Hash01(DecorHash(c.x, c.z) ^ 0x27d4eb2d) > 0.88f)
                        signTops.Add(top);
                    break;

                case RowKind.Crop:
                    cropTops.Add(top);
                    break;

                default:
                    if (Hash01(DecorHash(c.x, c.z) ^ 0x5bd1e995) <= decorBloomChance)
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

        patch.Grow(tops.ToArray(), DecorPetalPalette(), decorAccentColor,
                   maxFlowersPerCell: 3, flowerSizeWorld: 0.30f * cs,
                   scatterWorld: 0.28f * cs, maxFlowers: decorMaxFlowers);
    }

    // Crop beds: wheat sown in a straight line ACROSS each cell (along X, the row
    // direction), which is what makes them read as planted rather than scattered —
    // the flowers already own "scattered". One combined mesh per stalk (stem +
    // leaves + kernels), and one sway driver for the whole bed rather than a
    // component per stalk.
    void BuildCrops(List<Vector3> tops, float cs)
    {
        if (decorStalksPerCell <= 0) return;

        var root = new GameObject("CropBeds");
        root.transform.SetParent(_decorRoot.transform, false);
        var field = root.AddComponent<FarmCropField>();

        float h = decorStalkHeight * cs;

        // The sowing line follows the plot's rotated row direction, not world X.
        Vector3 along  = DecorRowDirWorld;
        Vector3 across = Vector3.Cross(Vector3.up, along);

        for (int t = 0; t < tops.Count; t++)
        {
            Vector3 top = tops[t];
            for (int s = 0; s < decorStalksPerCell; s++)
            {
                // Evenly spaced across the cell, with a hair of jitter so the
                // line is hand-sown, not machine-printed.
                float u  = (s + 0.5f) / decorStalksPerCell - 0.5f;
                int   hs = DecorHash(t * 31 + s, s * 17);
                Vector3 pos = top
                            + along  * (u * cs * 0.82f + (Hash01(hs) - 0.5f) * 0.12f * cs)
                            + across * ((Hash01(hs ^ 0x51ed) - 0.5f) * 0.24f * cs);

                float sh = h * Mathf.Lerp(0.8f, 1.2f, Hash01(hs ^ 0x2f9d));
                var stalk = MakeMeshProp(root.transform, "Wheat", WheatMesh(), pos,
                                         Quaternion.Euler(0f, Hash01(hs ^ 0x77a1) * 360f, 0f),
                                         new Vector3(sh, sh, sh),
                                         Tint(decorCropColor, Mathf.Lerp(0.88f, 1.12f, Hash01(hs ^ 0x11b3))));

                field.Add(stalk, pos, Hash01(hs ^ 0x1234) * Mathf.PI * 2f);
            }
        }
    }

    // ── Fence ────────────────────────────────────────────────────────────────
    // A picket per boundary edge (a covered column whose horizontal neighbour is
    // NOT covered), gated by decorFenceCoverage so gaps read as entrances. Each
    // picket also gets a rail running along the boundary toward the next edge
    // cell — pickets alone read as scattered sticks; the rail says "enclosure".
    void BuildFence(HashSet<Vector2Int> coveredCols, Dictionary<Vector2Int, Vector3Int> colTop, float cs)
    {
        var root = new GameObject("Fence");
        root.transform.SetParent(_decorRoot.transform, false);

        Vector2Int[] dirs = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };
        float postH = decorFenceHeight * cs;

        foreach (var col in coveredCols)
        {
            if (!colTop.TryGetValue(col, out var top)) continue;
            foreach (var dir in dirs)
            {
                var ncol = new Vector2Int(col.x + dir.x, col.y + dir.y);
                if (coveredCols.Contains(ncol)) continue;   // interior — no boundary here

                int gate = DecorHash(col.x * 131 + dir.x * 7, col.y * 131 + dir.y * 7);
                if (Hash01(gate ^ unchecked((int)0xb5297a4d)) > decorFenceCoverage) continue;

                Vector3 edge = gridSystem.GridToWorld(top)
                             + new Vector3(dir.x, 0f, dir.y) * (cs * 0.5f)
                             + Vector3.up * (cs * 0.5f);

                float lean = (Hash01(gate ^ 0x6d2b) - 0.5f) * 7f;   // a touch of settle, so the line isn't machined
                MakeMeshProp(root.transform, "Picket", PicketMesh(), edge,
                             Quaternion.Euler(lean, Hash01(gate) * 360f, lean * 0.5f),
                             new Vector3(0.16f * cs, postH, 0.16f * cs),
                             Tint(decorFenceColor, Mathf.Lerp(0.85f, 1.12f, Hash01(gate ^ 0x9f1a))));

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
                                 Tint(decorFenceColor, 0.92f));
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
        // Turns the rotor to face the plot's rotated frame. Set BEFORE the parts are
        // made: the tower/plinth/cap below are placed in WORLD space and all sit on
        // the vertical axis through basePos, so a Y turn can't shift them — while the
        // hub (localPosition) and its localSpace sails DO ride this rotation, which
        // is precisely the "windmill facing" that swapping decorSize never touched.
        root.transform.rotation = Quaternion.Euler(0f, DecorRotationDegrees, 0f);

        float h = decorWindmillHeight * cs;

        // Tapered cream tower — one built mesh rather than stacked boxes, so the
        // batter (the inward slope of the walls) is continuous the way a real
        // mill's is. Ink plinth under it and an ink cap above keep the
        // constructivist ink/cream contrast the map's other markers use.
        MakeMeshProp(root.transform, "Plinth", RailMesh(), basePos + Vector3.up * (h * 0.02f),
                     Quaternion.identity, new Vector3(0.62f * cs, h * 0.05f, 0.62f * cs), GeoPalette.Ink);
        MakeMeshProp(root.transform, "Tower", TowerMesh(), basePos + Vector3.up * (h * 0.04f),
                     Quaternion.identity, new Vector3(0.5f * cs, h * 0.72f, 0.5f * cs), decorTowerColor);
        MakeMeshProp(root.transform, "Cap", TowerCapMesh(), basePos + Vector3.up * (h * 0.76f),
                     Quaternion.identity, new Vector3(0.46f * cs, h * 0.2f, 0.46f * cs), GeoPalette.Ink);
        // A broad near-up-facing gold collar — the one element guaranteed to
        // print at full gold (≈0.98 shade) regardless of camera yaw, so the
        // landmark still reads gold when the sails happen to be edge-on.
        MakeMeshProp(root.transform, "Collar", RailMesh(), basePos + Vector3.up * (h * 0.745f),
                     Quaternion.identity, new Vector3(0.54f * cs, h * 0.035f, 0.54f * cs), decorAccentColor);

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
                         decorAccentColor, localSpace: true);

        MakeMeshProp(hub, "Hub", RailMesh(), Vector3.zero, Quaternion.identity,
                     new Vector3(0.16f * cs, 0.16f * cs, 0.1f * cs), GeoPalette.Ink, localSpace: true);

        root.AddComponent<FarmWindmillSpin>().Init(hub, decorWindmillSpin);
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
                     decorFenceColor);
        // Thin ink lip under the flag — the silkscreen "printed twice, slightly
        // offset" trick the level badges use, so the gold never looks like it's
        // floating free of the post.
        MakeMeshProp(root.transform, "FlagShadow", RailMesh(), basePos + Vector3.up * (postH * 0.9f),
                     Quaternion.Euler(12f, yaw, 0f), new Vector3(0.52f * cs, 0.03f * cs, 0.36f * cs),
                     GeoPalette.Ink);
        MakeMeshProp(root.transform, "Flag", RailMesh(), basePos + Vector3.up * (postH * 0.94f),
                     Quaternion.Euler(12f, yaw, 0f), new Vector3(0.48f * cs, 0.05f * cs, 0.32f * cs),
                     decorAccentColor);
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
