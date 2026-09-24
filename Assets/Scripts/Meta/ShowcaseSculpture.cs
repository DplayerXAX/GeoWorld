using System;
using System.Collections.Generic;
using UnityEngine;

// Builds large sculptures out of the game's own blocks, for screenshots.
//
//   PORTRAIT  any image, voxelised into a relief  — the board can DRAW
//   KNOT      a twisted ribbon on a torus knot    — the board can be KNOTTED
//   PENROSE   the impossible triangle             — the board can LIE
//   TOWER     a doubly-ruled hyperboloid          — the board's STRAIGHT pieces make curves
//   SPONGE    a Menger sponge                     — the board can be INFINITE
//
// Deliberately NOT routed through PlacementController.
//
// A real placement registers with the grid, rebuilds the surface graph, re-runs the
// synergy evaluator and re-paths every enemy. That is correct for a block a player
// puts down and ruinous for six thousand of them at once — the level would hang for
// a minute and then simulate a sculpture nobody is playing. These are set dressing:
// the same cube prefab, the same materials, the same lighting, spawned straight into
// the scene with nothing behind them.
//
// So the grid's 10x5x10 does not apply either. That bound belongs to the playable
// board; a sculpture standing beside it can be any size the frame rate tolerates.
//
// TWO RULES MAKE THESE ACTUALLY VISIBLE, and the first version had neither:
//
//   Every piece is CENTRED on `origin` and standing on it, so `origin` means "here",
//   not "the corner is here". An 84-cell portrait anchored at its corner walks 84
//   units off to one side of a 10-unit board and leaves the frame entirely.
//
//   Every piece is then SCALED to `fitSize`. The cell lattice is a drawing grid, not
//   a world measurement — an 84-cell-wide portrait next to a 10-unit board has to
//   come down to board scale or it is simply past the far clip plane. Resolution and
//   world size are separate decisions and this keeps them separate.
[DisallowMultipleComponent]
public class ShowcaseSculpture : MonoBehaviour
{
    public enum Piece { Portrait, Knot, Penrose, Tower, Sponge }

    [Header("What to build")]
    public Piece piece = Piece.Knot;
    public bool  buildOnStart = true;

    [Tooltip("F9 portrait / F10 knot / F11 penrose / F7 tower / F6 sponge, " +
             "F5 aim camera, F8 clear. Turn off for a clean capture.")]
    public bool hotkeys = true;

    [Header("Placement")]
    [Tooltip("Where the sculpture STANDS, in world units. It is centred on this and sits on it.")]
    public Vector3 origin = new(5f, 0.5f, 5f);

    [Tooltip("Largest world dimension the finished sculpture may occupy. " +
             "The cell lattice is scaled to fit — resolution and size are independent.")]
    [Range(2f, 120f)] public float fitSize = 16f;

    public Vector3 rotationEuler = Vector3.zero;

    [Tooltip("Hard ceiling. Every cube is its own draw call, so this is the frame-rate dial. " +
             "A piece that exceeds it is REBUILT coarser, never cut in half.")]
    [Range(200, 40000)] public int maxCubes = 9000;

    [Header("Portrait")]
    [Tooltip("Texture under a Resources folder, e.g. Resources/Showcase/cat.png -> \"Showcase/cat\".")]
    public string portraitResource = "Showcase/cat";
    [Range(16, 240)] public int portraitWidth = 96;
    [Tooltip("Depth relief: brighter pixels stand further out. 0 = a flat mural.")]
    [Range(0, 8)] public int portraitRelief = 3;
    [Tooltip("Quantise each channel to this many steps — the game prints with a limited set of inks.")]
    [Range(2, 32)] public int portraitLevels = 12;
    [Tooltip("Push saturation and contrast up. A photograph averaged into blocks always reads flat.")]
    [Range(0f, 2f)] public float portraitPunch = 1.35f;

    [Header("Knot — a ribbon that is tied AND twisted")]
    [Tooltip("p,q of the torus knot. 2,3 = trefoil. 1,0 = a plain circle, i.e. a classic Mobius band.")]
    public int knotP = 2;
    public int knotQ = 3;
    [Tooltip("Half-twists of the ribbon over one full loop. Odd = one-sided, like a Mobius.")]
    [Range(0, 12)] public int knotHalfTwists = 3;
    [Range(4f, 40f)] public float knotScale = 14f;
    [Range(1f, 12f)] public float knotWidth = 4.5f;
    [Range(0.5f, 5f)] public float knotThickness = 1.2f;

    [Header("Penrose — the impossible triangle")]
    [Range(8f, 60f)] public float penroseArm = 30f;
    [Range(2f, 14f)] public float penroseBar = 7f;
    [Tooltip("The illusion only closes from ONE viewpoint. Build snaps the camera to it.")]
    public bool penroseAimsCamera = true;
    [Tooltip("If the loop reads as open, flip this — it mirrors the closing axis.")]
    public bool penroseFlip = false;

    [Header("Tower — a curve made only of straight beams")]
    [Range(6f, 40f)]  public float towerRadius = 12f;
    [Range(10f, 90f)] public float towerHeight = 40f;
    [Range(8, 72)]    public int   towerBeams  = 32;
    [Tooltip("Twist of the two beam families. 0 = a plain cylinder; 90+ = a pinched waist.")]
    [Range(0f, 150f)] public float towerTwist = 104f;

    [Header("Sponge")]
    [Range(1, 4)] public int spongeLevel = 3;

    [Header("Colour — sculptures only, not the board's six")]
    [Tooltip("Sweeps of the full spectrum. These are exhibits, not positions, " +
             "so they are allowed hues the board never uses.")]
    [Range(0f, 3f)] public float hueSweeps = 1f;
    [Range(0f, 1f)] public float hueOffset = 0f;
    [Range(0f, 1f)] public float saturation = 0.85f;
    [Range(0f, 1f)] public float value = 0.95f;

    GameObject _root;
    GameObject _cube;
    Bounds     _bounds;
    bool       _hasBounds;

    void Start() { if (buildOnStart) Build(piece); }

    void Update()
    {
        if (!hotkeys) return;
        if (Input.GetKeyDown(KeyCode.F9))  Build(Piece.Portrait);
        if (Input.GetKeyDown(KeyCode.F10)) Build(Piece.Knot);
        if (Input.GetKeyDown(KeyCode.F11)) Build(Piece.Penrose);
        if (Input.GetKeyDown(KeyCode.F7))  Build(Piece.Tower);
        if (Input.GetKeyDown(KeyCode.F6))  Build(Piece.Sponge);
        if (Input.GetKeyDown(KeyCode.F5))  AimCamera();
        if (Input.GetKeyDown(KeyCode.F8))  Clear();
    }

    [ContextMenu("Build")] void BuildCurrent() => Build(piece);

    [ContextMenu("Clear")]
    public void Clear()
    {
        if (_root != null) DestroyImmediate(_root);
        _root = null;
        _hasBounds = false;
    }

    public void Build(Piece which)
    {
        Clear();
        piece = which;

        _cube = CubePrefab();
        if (_cube == null) { Debug.LogWarning("[Showcase] No cube prefab."); return; }

        // Build coarser until it fits the budget, rather than stopping halfway
        // through. A truncated sculpture is a broken sculpture; a coarser one is
        // still the same sculpture.
        var cells = Budgeted(which);
        if (cells.Count == 0) return;

        // Centre on the origin and stand it on the ground there.
        var min = new Vector3(int.MaxValue, int.MaxValue, int.MaxValue);
        var max = new Vector3(int.MinValue, int.MinValue, int.MinValue);
        foreach (var k in cells.Keys) { min = Vector3.Min(min, k); max = Vector3.Max(max, k); }
        var size   = max - min + Vector3.one;
        var anchor = new Vector3((min.x + max.x) * 0.5f, min.y, (min.z + max.z) * 0.5f);

        float scale = fitSize / Mathf.Max(size.x, Mathf.Max(size.y, size.z));

        _root = new GameObject($"Showcase_{which}");
        _root.transform.SetParent(transform, false);
        _root.transform.position   = origin;
        _root.transform.rotation   = Quaternion.Euler(rotationEuler);
        _root.transform.localScale = Vector3.one * scale;

        bool wasOn = _cube.activeSelf;
        _cube.SetActive(false);              // so the prefab's Awakes never fire
        foreach (var kv in cells) Spawn((Vector3)kv.Key - anchor, kv.Value);
        _cube.SetActive(wasOn);

        _bounds = new Bounds(origin + Vector3.up * (size.y * 0.5f * scale),
                             size * scale);
        _hasBounds = true;

        Debug.Log($"[Showcase] {which}: {cells.Count} cubes, " +
                  $"{size.x:0}x{size.y:0}x{size.z:0} cells, scaled {scale:0.000} " +
                  $"-> {_bounds.size.x:0.0}x{_bounds.size.y:0.0}x{_bounds.size.z:0.0} world units at {origin}.");

        if (which == Piece.Penrose && penroseAimsCamera) AimCamera();
    }

    Dictionary<Vector3Int, Color> Budgeted(Piece which)
    {
        float res = 1f;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var cells = Generate(which, res);
            if (cells.Count <= maxCubes || cells.Count == 0) return cells;

            // These are all surfaces, so cell count grows with the SQUARE of the
            // lattice resolution. Solve for the factor directly instead of creeping
            // down by tenths and rebuilding eight times.
            res *= Mathf.Sqrt(maxCubes / (float)cells.Count) * 0.97f;
        }
        return Generate(which, res);
    }

    Dictionary<Vector3Int, Color> Generate(Piece which, float res) => which switch
    {
        Piece.Portrait => Portrait(res),
        Piece.Knot     => Knot(res),
        Piece.Penrose  => Penrose(res),
        Piece.Tower    => Tower(res),
        _              => Sponge(),
    };

    GameObject CubePrefab()
    {
        var pc = PlacementController.Instance;
        if (pc != null && pc.cubePrefab != null) return pc.cubePrefab;
        return Resources.Load<GameObject>("cube_be");
    }

    void Spawn(Vector3 local, Color c)
    {
        var go = Instantiate(_cube, _root.transform);
        go.transform.localPosition = local;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale    = Vector3.one;

        // Colliders would put thousands of shapes into the physics scene for a thing
        // nothing can touch, and gameplay scripts expect a real placement behind them.
        // Strip both: this is scenery.
        foreach (var col in go.GetComponentsInChildren<Collider>(true)) DestroyImmediate(col);
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(mb);
        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) MpbColor.Set(r, c);

        go.SetActive(true);
    }

    Color Hue(float t) =>
        Color.HSVToRGB(Mathf.Repeat(hueOffset + t * hueSweeps, 1f), saturation, value);

    // ── CAMERA ───────────────────────────────────────────────────────────────

    // The single most useful button here. Half of "I built it and cannot see it" is
    // just a sculpture standing outside the gameplay camera's framing, and guessing
    // at a camera transform by hand for every piece is miserable.
    [ContextMenu("Aim camera at sculpture")]
    public void AimCamera()
    {
        var cam = Camera.main;
        if (cam == null || !_hasBounds) return;

        // A portrait is a picture: look at it square on, or the relief shears and the
        // face stops reading. Everything else is a solid and wants the isometric pose
        // the rest of the game is drawn in.
        bool flat = piece == Piece.Portrait;

        var rot = flat ? Quaternion.identity
                       : Quaternion.Euler(35.264f, -45f, 0f);   // true isometric

        float extent = Mathf.Max(_bounds.extents.x,
                       Mathf.Max(_bounds.extents.y, _bounds.extents.z));

        cam.orthographic     = true;
        cam.orthographicSize = extent * 1.25f;
        cam.transform.rotation = rot;
        cam.transform.position = _bounds.center - rot * Vector3.forward * (extent * 8f);
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane  = extent * 40f;

        Debug.Log($"[Showcase] Camera aimed: pos {cam.transform.position}, " +
                  $"euler {rot.eulerAngles}, ortho size {cam.orthographicSize:0.0}.");
    }

    // ── PORTRAIT ─────────────────────────────────────────────────────────────

    Dictionary<Vector3Int, Color> Portrait(float res)
    {
        var cells = new Dictionary<Vector3Int, Color>();

        var tex = Resources.Load<Texture2D>(portraitResource);
        if (tex == null)
        {
            Debug.LogWarning($"[Showcase] No texture at Resources/{portraitResource}.");
            return cells;
        }

        var px = ReadPixels(tex, out int w, out int h);
        if (px == null) return cells;

        int cols = Mathf.Max(8, Mathf.RoundToInt(portraitWidth * res));
        int rows = Mathf.Max(8, Mathf.RoundToInt(cols * (float)h / Mathf.Max(1, w)));

        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                // Box-averaged, not point-sampled. One pixel per block would pick up
                // every speck of photographic noise and the wall would read as static;
                // averaging the whole footprint is what a mosaic actually is.
                Color c = Punch(Average(px, w, h, x, y, cols, rows), portraitPunch);
                c = Quantise(c, portraitLevels);

                // Brightness pushes the block outward, so the wall is a RELIEF. Flat,
                // a photograph of a photograph reads as a texture; with depth it reads
                // as something that was built.
                int depth = portraitRelief <= 0 ? 0
                          : Mathf.RoundToInt(Luma(c) * portraitRelief);

                for (int d = 0; d <= depth; d++)
                    cells[new Vector3Int(x, rows - 1 - y, -d)] = c;
            }

        return cells;
    }

    // Through a RenderTexture rather than tex.GetPixels().
    //
    // GetPixels throws on any texture imported without Read/Write enabled, which is
    // every texture by default — and telling someone to go and tick a box before
    // their screenshot works is a tool that does not work. Blitting to an RT and
    // reading that back has no such requirement.
    static Color[] ReadPixels(Texture2D src, out int w, out int h)
    {
        w = src.width; h = src.height;
        RenderTexture rt = null;
        var prev = RenderTexture.active;
        try
        {
            rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32,
                                            RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;

            var flat = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            flat.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            flat.Apply(false, false);

            var px = flat.GetPixels();
            DestroyImmediate(flat);
            return px;                      // bottom-up; Average() flips it
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Showcase] Could not read '{src.name}': {e.Message}");
            return null;
        }
        finally
        {
            RenderTexture.active = prev;
            if (rt != null) RenderTexture.ReleaseTemporary(rt);
        }
    }

    static Color Average(Color[] px, int w, int h, int cx, int cy, int cols, int rows)
    {
        int x0 = cx * w / cols, x1 = Mathf.Max(x0 + 1, (cx + 1) * w / cols);
        int y0 = cy * h / rows, y1 = Mathf.Max(y0 + 1, (cy + 1) * h / rows);

        float r = 0f, g = 0f, b = 0f; int n = 0;
        for (int y = y0; y < y1; y++)
        {
            int row = (h - 1 - y) * w;      // GetPixels is bottom-up, the mosaic is not
            for (int x = x0; x < x1; x++)
            {
                var c = px[row + x];
                r += c.r; g += c.g; b += c.b; n++;
            }
        }
        return n == 0 ? Color.magenta : new Color(r / n, g / n, b / n, 1f);
    }

    // Averaging thousands of pixels into a few hundred blocks pulls everything toward
    // the image's mean — that is what averaging does. Saturation and contrast have to
    // be put back or the portrait comes out as grey porridge.
    static Color Punch(Color c, float k)
    {
        if (k <= 0f) return c;
        Color.RGBToHSV(c, out float hue, out float sat, out float val);
        sat = Mathf.Clamp01(sat * k);
        val = Mathf.Clamp01(0.5f + (val - 0.5f) * Mathf.Lerp(1f, k, 0.6f));
        return Color.HSVToRGB(hue, sat, val);
    }

    static Color Quantise(Color c, int levels)
    {
        float s = Mathf.Max(1, levels - 1);
        return new Color(Mathf.Round(c.r * s) / s,
                         Mathf.Round(c.g * s) / s,
                         Mathf.Round(c.b * s) / s, 1f);
    }

    static float Luma(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

    // ── KNOT ─────────────────────────────────────────────────────────────────

    // A ribbon that is knotted AND twisted: its spine is a (p,q) torus knot, and the
    // band rotates about that spine as it goes. An odd number of half-twists makes it
    // one-sided — a Mobius band that has also been tied in a trefoil. (p,q = 1,0 and
    // one half-twist degenerates to exactly the classic Mobius strip.)
    //
    // The frame is built from the torus centreline rather than a fixed world up. A
    // fixed reference flips over wherever the tangent passes near it, and the ribbon
    // tears itself apart at that point; the radial direction never does.
    Dictionary<Vector3Int, Color> Knot(float res)
    {
        var cells = new Dictionary<Vector3Int, Color>();

        float S = knotScale * res;
        float W = knotWidth * res;
        float T = knotThickness * res;
        int p = Mathf.Max(1, knotP), q = Mathf.Max(0, knotQ);

        int uSteps = Mathf.Clamp(Mathf.CeilToInt(S * 40f), 200, 6000);
        int vSteps = Mathf.Max(2, Mathf.CeilToInt(W * 2.4f));
        int tSteps = Mathf.Max(1, Mathf.CeilToInt(T * 2.0f));

        for (int iu = 0; iu < uSteps; iu++)
        {
            float f = iu / (float)uSteps;
            float t = f * 2f * Mathf.PI;

            Vector3 P = KnotPoint(t, p, q, S);
            Vector3 tangent = (KnotPoint(t + 0.002f, p, q, S) - KnotPoint(t - 0.002f, p, q, S)).normalized;

            Vector3 hub = new(2f * S / 3f * Mathf.Cos(p * t), 0f, 2f * S / 3f * Mathf.Sin(p * t));
            Vector3 u = Vector3.ProjectOnPlane(P - hub, tangent).normalized;
            if (u.sqrMagnitude < 1e-6f) u = Vector3.ProjectOnPlane(Vector3.up, tangent).normalized;
            Vector3 v = Vector3.Cross(tangent, u).normalized;

            float twist = knotHalfTwists * Mathf.PI * f;
            Vector3 wide = u * Mathf.Cos(twist) + v * Mathf.Sin(twist);
            Vector3 norm = -u * Mathf.Sin(twist) + v * Mathf.Cos(twist);

            Color c = Hue(f);

            for (int iv = 0; iv <= vSteps; iv++)
            {
                float s = Mathf.Lerp(-W, W, iv / (float)vSteps);
                for (int it = 0; it < tSteps; it++)
                {
                    float d = tSteps == 1 ? 0f
                            : Mathf.Lerp(-T * 0.5f, T * 0.5f, it / (float)(tSteps - 1));
                    cells[Snap(P + wide * s + norm * d)] = c;
                }
            }
        }
        return cells;
    }

    static Vector3 KnotPoint(float t, int p, int q, float S)
    {
        float r = Mathf.Cos(q * t) + 2f;
        return new Vector3(r * Mathf.Cos(p * t), -Mathf.Sin(q * t), r * Mathf.Sin(p * t)) * (S / 3f);
    }

    // ── PENROSE ──────────────────────────────────────────────────────────────

    // Three straight bars that meet at three right angles and close into a triangle.
    // They cannot; the closure is a coincidence of one projection.
    //
    // In the isometric pose this game already uses — yaw -45, pitch 35.264 — the
    // camera looks along (-1,-1,1). Any world displacement parallel to that direction
    // projects to NOTHING. So a run of +X, then +Y, then -Z, all of equal length,
    // ends at (L, L, -L): a vector along the view axis, which lands back on the exact
    // screen pixel it started from. The loop closes on screen and nowhere else.
    //
    // The last bar's far end sits nearer the camera than the first bar's start, so it
    // occludes it, and the seam disappears. That occlusion is the whole trick — and
    // it is why the piece snaps the camera when it builds: from any other angle this
    // is just three bars in a spiral.
    Dictionary<Vector3Int, Color> Penrose(float res)
    {
        var cells = new Dictionary<Vector3Int, Color>();

        int L = Mathf.Max(6, Mathf.RoundToInt(penroseArm * res));
        int s = Mathf.Clamp(Mathf.RoundToInt(penroseBar * res), 2, L / 2);
        int zs = penroseFlip ? 1 : -1;

        // Three bars of equal length, square in section, chained head to tail:
        // +X, then +Y, then along the view axis. Each starts inside the previous
        // one's end cap, so the corners are solid joints rather than butted faces.
        Box(cells, 0,         L, 0,         s - 1, 0, s - 1, Hue(0.00f), zs);
        Box(cells, L - s + 1, L, 0,         L,     0, s - 1, Hue(0.34f), zs);
        Box(cells, L - s + 1, L, L - s + 1, L,     0, L,     Hue(0.67f), zs);

        return cells;
    }

    static void Box(Dictionary<Vector3Int, Color> cells,
                    int x0, int x1, int y0, int y1, int z0, int z1, Color c, int zs)
    {
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                {
                    // Hollow: only the shell. A solid bar is thousands of cells that
                    // nothing can ever see.
                    bool shell = x == x0 || x == x1 || y == y0 || y == y1 || z == z0 || z == z1;
                    if (!shell) continue;
                    cells[new Vector3Int(x, y, z * zs)] = c;
                }
    }

    // ── TOWER ────────────────────────────────────────────────────────────────

    // A doubly-ruled surface: the waist curves, yet every beam in it is a straight
    // line from the bottom ring to the top ring. Two families twisted opposite ways,
    // crossing into a lattice — a game made of straight pieces building a curve, and
    // you can see that it did.
    Dictionary<Vector3Int, Color> Tower(float res)
    {
        var cells = new Dictionary<Vector3Int, Color>();
        float R = towerRadius * res, H = towerHeight * res;
        int beams = Mathf.Max(3, Mathf.RoundToInt(towerBeams * res));
        float twist = towerTwist * Mathf.Deg2Rad;

        for (int family = 0; family < 2; family++)
        {
            float sign = family == 0 ? 1f : -1f;
            for (int i = 0; i < beams; i++)
            {
                float a0 = i / (float)beams * 2f * Mathf.PI;
                float a1 = a0 + sign * twist;

                var p0 = new Vector3(R * Mathf.Cos(a0), 0f, R * Mathf.Sin(a0));
                var p1 = new Vector3(R * Mathf.Cos(a1), H,  R * Mathf.Sin(a1));

                int steps = Mathf.CeilToInt(Vector3.Distance(p0, p1) * 2.5f);
                for (int st = 0; st <= steps; st++)
                {
                    float t = st / (float)steps;
                    cells[Snap(Vector3.Lerp(p0, p1, t))] = Hue(family == 0 ? t : 1f - t);
                }
            }
        }

        for (int r = 0; r < 2; r++)
        {
            float y = r == 0 ? 0f : H;
            int steps = Mathf.CeilToInt(2f * Mathf.PI * R * 2.5f);
            for (int st = 0; st < steps; st++)
            {
                float a = st / (float)steps * 2f * Mathf.PI;
                cells[Snap(new Vector3(R * Mathf.Cos(a), y, R * Mathf.Sin(a)))] = Hue(r);
            }
        }
        return cells;
    }

    // ── SPONGE ───────────────────────────────────────────────────────────────

    // A Menger sponge: a solid whose surface area runs away to infinity while its
    // volume runs to zero. The rule is one line — in base 3, a cell survives unless
    // two or more of its digits are 1 — and it is the same rule at every scale, which
    // is the point. Coloured by the deepest level that carved it, so the recursion is
    // visible rather than merely present.
    Dictionary<Vector3Int, Color> Sponge()
    {
        var cells = new Dictionary<Vector3Int, Color>();

        // 20^level cubes. Drop a level rather than cut the shape in half.
        int level = Mathf.Clamp(spongeLevel, 1, 4);
        while (level > 1 && Mathf.Pow(20, level) > maxCubes) level--;

        int n = (int)Mathf.Pow(3, level);
        for (int x = 0; x < n; x++)
            for (int y = 0; y < n; y++)
                for (int z = 0; z < n; z++)
                {
                    int deepest = -1;
                    bool solid = true;
                    for (int d = 0, px = x, py = y, pz = z; d < level; d++, px /= 3, py /= 3, pz /= 3)
                    {
                        int ones = (px % 3 == 1 ? 1 : 0) + (py % 3 == 1 ? 1 : 0) + (pz % 3 == 1 ? 1 : 0);
                        if (ones >= 2) { solid = false; break; }
                        if (ones == 1) deepest = d;
                    }
                    if (!solid) continue;
                    cells[new Vector3Int(x, y, z)] =
                        Hue((deepest + 1) / (float)(level + 1));
                }
        return cells;
    }

    static Vector3Int Snap(Vector3 p) =>
        new(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z));
}
