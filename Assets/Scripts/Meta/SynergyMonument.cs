using System.Collections.Generic;
using UnityEngine;

// Architecture built out of the game's OWN pieces, in the game's own six colours.
//
// The voxel sculptures next door (ShowcaseSculpture) prove a different thing: that a
// big enough pile of cubes can be any shape at all. That argument is about resolution,
// and it is made in a vocabulary the game does not use — nobody plays by placing
// single cubes one at a time.
//
// These buildings are the opposite claim. Every one of them is assembled from I4, L4,
// T4, S4, O2x2, Corner3D — the same handful of pieces a player draws — and the COLOUR
// SAYS WHICH PIECE IS WHICH. That is the whole point of colouring by shape rather
// than by building: without it a tiled structure reads as an undifferentiated mass of
// cubes no matter how honestly it was assembled, because nothing marks where one
// piece stops and the next begins.
//
// The mapping falls out of the game's own numbers. There are exactly six four-cell
// shapes and exactly six synergy colours, so they pair off one to one — and the
// smaller filler pieces get Universal grey, which is precisely what Universal means
// in BlockColor.cs: the joker, the piece that stands in for whatever is needed.
[DisallowMultipleComponent]
public class SynergyMonument : MonoBehaviour
{
    public enum Kind { Temple, Viaduct, Tower, Ziggurat, All }

    // How a piece gets its hue.
    public enum Tint
    {
        ByShape,    // hue = which of the nine shapes it is. The vocabulary, made legible.
        Contrast,   // no two touching pieces share a hue. Maximum piece separation.
        ByTier,     // hue = height band. Reads as strata; good on the ziggurat.
    }

    [Header("What to build")]
    public Kind kind = Kind.Temple;
    public Tint  tint  = Tint.ByShape;
    public bool  buildOnStart = true;

    [Tooltip("F1 temple / F2 viaduct / F3 tower / F4 ziggurat / F7 all, F5 aims, F8 clears.")]
    public bool hotkeys = true;

    [Header("Placement")]
    [Tooltip("One cell = one world unit, same as the board, so these read as the same " +
             "material as real gameplay standing next to them.")]
    public Vector3 origin = new(0f, 0.5f, 0f);
    public float cellSize = 1f;

    [Tooltip("Rerolls the tiling. The building is fixed; which pieces express it is not.")]
    public int seed = 7;

    [Header("Colour")]
    [Tooltip("Brightness step used to separate two touching pieces that landed on the " +
             "same hue. HUE IS NEVER TOUCHED — every piece is an exact " +
             "BlockColorPalette value, only louder or quieter.")]
    [Range(0f, 0.2f)] public float pieceShade = 0.09f;

    [Header("Dimensions")]
    [Range(1, 6)] public int viaductArches = 3;
    [Range(3, 9)] public int templeColumns = 5;
    [Range(2, 5)] public int zigguratTiers = 3;
    [Range(1, 4)] public int towerStages   = 2;

    GameObject _root, _cube;
    Bounds _bounds;
    bool   _hasBounds;

    struct Placed
    {
        public BlockShape   shape;
        public Vector3Int[] cells;    // already rotated and translated
    }

    void Start() { if (buildOnStart) Build(kind); }

    void Update()
    {
        if (!hotkeys) return;
        if (Input.GetKeyDown(KeyCode.F1)) Build(Kind.Temple);
        if (Input.GetKeyDown(KeyCode.F2)) Build(Kind.Viaduct);
        if (Input.GetKeyDown(KeyCode.F3)) Build(Kind.Tower);
        if (Input.GetKeyDown(KeyCode.F4)) Build(Kind.Ziggurat);
        if (Input.GetKeyDown(KeyCode.F7)) Build(Kind.All);
        if (Input.GetKeyDown(KeyCode.F5)) AimCamera();
        if (Input.GetKeyDown(KeyCode.F8)) Clear();
    }

    [ContextMenu("Build")] void BuildCurrent() => Build(kind);

    [ContextMenu("Clear")]
    public void Clear()
    {
        if (_root != null) DestroyImmediate(_root);
        _root = null;
        _hasBounds = false;
    }

    public void Build(Kind which)
    {
        Clear();
        kind = which;

        _cube = CubePrefab();
        if (_cube == null) { Debug.LogWarning("[Monument] No cube prefab."); return; }

        var form = new HashSet<Vector3Int>();
        if (which == Kind.All)
        {
            // Laid out along X on a shared ground line, so one screenshot holds
            // the whole set.
            int x = 0;
            for (int i = 0; i < 4; i++)
            {
                var f = Form((Kind)i);
                Span(f, out int lo, out int hi);
                foreach (var c in f) form.Add(new Vector3Int(c.x + x - lo, c.y, c.z));
                x += (hi - lo + 1) + 4;
            }
        }
        else form = Form(which);

        var rng    = new System.Random(seed);
        var pieces = Tile(form, rng);
        if (pieces.Count == 0) return;

        var tones = Palette(pieces);

        _root = new GameObject($"Monument_{which}");
        _root.transform.SetParent(transform, false);
        _root.transform.position = origin;

        // Centre on X/Z so `origin` means "here", and leave Y alone so the building
        // stands ON the origin instead of hovering with its middle at it.
        var min = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
        var max = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
        foreach (var c in form) { min = Vector3Int.Min(min, c); max = Vector3Int.Max(max, c); }
        var anchor = new Vector3((min.x + max.x) * 0.5f, min.y, (min.z + max.z) * 0.5f);

        bool wasOn = _cube.activeSelf;
        _cube.SetActive(false);              // so the prefab's Awakes never fire

        var histogram = new Dictionary<BlockShape, int>();
        int cubes = 0;
        for (int i = 0; i < pieces.Count; i++)
        {
            histogram.TryGetValue(pieces[i].shape, out int had);
            histogram[pieces[i].shape] = had + 1;

            foreach (var c in pieces[i].cells) { Spawn(((Vector3)c - anchor) * cellSize, tones[i]); cubes++; }
        }

        _cube.SetActive(wasOn);

        var size = (Vector3)(max - min + Vector3Int.one) * cellSize;
        _bounds = new Bounds(origin + Vector3.up * (size.y * 0.5f), size);
        _hasBounds = true;

        var report = new System.Text.StringBuilder();
        foreach (var kv in histogram) report.Append(kv.Key).Append(' ').Append(kv.Value).Append("  ");
        Debug.Log($"[Monument] {which}: {pieces.Count} pieces, {cubes} cubes, " +
                  $"{size.x:0}x{size.y:0}x{size.z:0} units.\n  {report}");
    }

    // ── THE BUILDINGS ────────────────────────────────────────────────────────
    //
    // Each is authored as a SET OF CELLS, not as a list of pieces.
    //
    // Authoring pieces directly means hand-solving a packing puzzle for every design,
    // and one wrong offset leaves a hole you only find in the editor. Author the form,
    // let the tiler express it in real pieces, and the design and the packing stop
    // fighting each other — reroll `seed` and the same building comes back assembled
    // out of a different set of blocks.

    HashSet<Vector3Int> Form(Kind which) => which switch
    {
        Kind.Temple   => Temple(),
        Kind.Viaduct  => Viaduct(),
        Kind.Tower    => Tower(),
        _              => Ziggurat(),
    };

    // A peristyle temple: stepped stylobate, a colonnade, architrave, stepped roof.
    //
    // The columns are one cell square and four tall on purpose — that is exactly an
    // I4 stood on its end, so every column in the building is a SINGLE PIECE. Nothing
    // else in the shape vocabulary makes a column that clean, and it is the clearest
    // possible demonstration of what these pieces can do.
    HashSet<Vector3Int> Temple()
    {
        var s = new HashSet<Vector3Int>();
        int n  = Mathf.Clamp(templeColumns, 3, 9);
        int W  = (n - 1) * 2 + 2;             // colonnade span
        int D  = 6;

        Slab(s, -1, W + 1, 0, 0, -1, D + 1);  // stylobate
        Slab(s,  0, W,     1, 1,  0, D);      // second step

        for (int i = 0; i < n; i++)           // the colonnade — one I4 apiece
        {
            int x = 1 + i * 2;
            for (int y = 2; y <= 5; y++) { s.Add(new(x, y, 1)); s.Add(new(x, y, D - 1)); }
        }

        Ring (s, 0, W, 6, 6, 0, D);           // architrave
        Slab (s, 0, W, 7, 7, 0, D);           // roof base
        Slab (s, 1, W - 1, 8, 8, 1, D - 1);   // first course of the gable
        Slab (s, 2, W - 2, 9, 9, 2, D - 2);
        return s;
    }

    // A Roman viaduct: piers, semicircular arches, a deck and parapets.
    //
    // The arches are CARVED, not assembled — build the solid arcade, then remove a
    // rectangle up to the springing line and a half-disc above it. Describing a curve
    // by what it takes away is far easier to get right than laying voussoirs by hand,
    // and it is how a real voxel arch is cut anyway.
    HashSet<Vector3Int> Viaduct()
    {
        var s = new HashSet<Vector3Int>();
        int arches = Mathf.Clamp(viaductArches, 1, 6);
        const int pier = 3, span = 5, spring = 6, top = 9;
        int L = pier + arches * (span + pier) - 1;

        Slab(s, 0, L, 0, top, 0, 1);                       // the arcade, solid

        for (int k = 0; k < arches; k++)
        {
            int x0 = pier + k * (span + pier);
            float cx = x0 + (span - 1) * 0.5f, r = span * 0.5f;

            for (int x = x0; x < x0 + span; x++)
                for (int y = 0; y <= top; y++)
                {
                    bool inside = y < spring
                                || (x - cx) * (x - cx) + (y - spring) * (y - spring) <= r * r;
                    if (!inside) continue;
                    s.Remove(new(x, y, 0));
                    s.Remove(new(x, y, 1));
                }
        }

        Slab(s, 0, L, top + 1, top + 1, -1, 2);            // deck
        for (int x = 0; x <= L; x++)                       // parapets
        { s.Add(new(x, top + 2, -1)); s.Add(new(x, top + 2, 2)); }
        return s;
    }

    // A tapering tower: hollow shells, cantilevered galleries where the section steps
    // in, and a finial. Hollow because a solid tower is thousands of cells nothing can
    // ever see, and because a shell is what a tower actually is.
    HashSet<Vector3Int> Tower()
    {
        var s = new HashSet<Vector3Int>();
        int stages = Mathf.Clamp(towerStages, 1, 4);
        int y = 0, half = 2 + stages;            // biggest section at the bottom

        for (int st = 0; st < stages; st++)
        {
            int lo = -half, hi = half;
            for (int h = 0; h < 6; h++, y++) Ring(s, lo, hi, y, y, lo, hi);

            // The gallery oversails the shell below it by one cell all round, so the
            // step is a shadow line rather than a seam.
            Ring(s, lo - 1, hi + 1, y, y, lo - 1, hi + 1);
            Ring(s, lo,     hi,     y, y, lo,     hi);
            y++;
            half--;
        }

        int f = half + 1;
        Slab(s, -f, f, y, y, -f, f);
        y++;
        for (int h = 0; h < 4; h++, y++) s.Add(new(0, y, 0));   // finial — one I4
        return s;
    }

    // A stepped ziggurat with a processional stair climbing the front.
    //
    // The terraces are one cell thick. Solid tiers would triple the cube count to
    // describe a mass nobody can see into, and from any angle that shows the building
    // at all you are looking at the outside faces regardless.
    HashSet<Vector3Int> Ziggurat()
    {
        var s = new HashSet<Vector3Int>();
        int tiers = Mathf.Clamp(zigguratTiers, 2, 5);
        int half = tiers * 2 + 2;
        int y = 0;

        for (int t = 0; t < tiers; t++)
        {
            for (int h = 0; h < 3; h++, y++) Ring(s, -half, half, y, y, -half, half);
            half -= 2;
        }

        Slab(s, -1, 1, y, y + 2, -1, 1);                   // the shrine
        for (int h = 0; h < 4; h++) s.Add(new(0, y + 3 + h, 0));

        // The stair descends away from the building as it drops, so it lands on the
        // ground clear of the base instead of running into the first terrace.
        int topY = y - 1;
        for (int step = 0; step <= topY; step++)
        {
            int z = -(tiers * 2 + 3) - (topY - step);
            for (int x = -1; x <= 1; x++) s.Add(new(x, step, z));
        }
        return s;
    }

    static void Slab(HashSet<Vector3Int> s, int x0, int x1, int y0, int y1, int z0, int z1)
    {
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++) s.Add(new(x, y, z));
    }

    static void Ring(HashSet<Vector3Int> s, int x0, int x1, int y0, int y1, int z0, int z1)
    {
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                    if (x == x0 || x == x1 || z == z0 || z == z1) s.Add(new(x, y, z));
    }

    static void Span(HashSet<Vector3Int> cells, out int lo, out int hi)
    {
        lo = int.MaxValue; hi = int.MinValue;
        foreach (var c in cells) { lo = Mathf.Min(lo, c.x); hi = Mathf.Max(hi, c.x); }
    }

    // ── COLOUR ───────────────────────────────────────────────────────────────

    // Six four-cell shapes, six synergy colours. Everything smaller is Universal grey,
    // which is what Universal already means here: the joker, the stand-in. The filler
    // pieces are literally the filler colour.
    static BlockColor ShapeColor(BlockShape s) => s switch
    {
        BlockShape.I4       => BlockColor.Exploration,    // the straight run
        BlockShape.O2x2     => BlockColor.Enlightenment,  // the square
        BlockShape.L4       => BlockColor.Order,
        BlockShape.T4       => BlockColor.Harmony,
        BlockShape.S4       => BlockColor.Abundance,
        BlockShape.Corner3D => BlockColor.Heresy,         // the only one that leaves the plane
        _                   => BlockColor.Universal,
    };

    // Hue says what a piece IS; brightness only ever separates two touching pieces
    // that happened to land on the same hue. Softening a hue and dimming it are
    // different operations — mixing toward paper would shift every piece off the
    // palette it is supposed to BE — so the value is scaled and the hue left alone.
    Color[] Palette(List<Placed> pieces)
    {
        var hue = new BlockColor[pieces.Count];
        var owner = new Dictionary<Vector3Int, int>();

        int topY = 1;
        foreach (var p in pieces) foreach (var c in p.cells) topY = Mathf.Max(topY, c.y);

        for (int i = 0; i < pieces.Count; i++)
        {
            foreach (var c in pieces[i].cells) owner[c] = i;

            hue[i] = tint switch
            {
                Tint.ByShape => ShapeColor(pieces[i].shape),
                Tint.ByTier  => BlockColorPalette.Themes[
                                    Mathf.Clamp(pieces[i].cells[0].y * 6 / (topY + 1), 0, 5)],
                _            => BlockColor.None,   // Contrast assigns below
            };
        }

        var dirs = new[]
        {
            Vector3Int.right, Vector3Int.left, Vector3Int.up,
            Vector3Int.down,  new Vector3Int(0,0,1), new Vector3Int(0,0,-1),
        };

        var shade = new int[pieces.Count];
        var taken = new HashSet<int>();

        for (int i = 0; i < pieces.Count; i++)
        {
            // Neighbours already coloured. Only earlier pieces count, which is what
            // makes one pass enough: every pair is resolved by whichever of the two
            // is decided second.
            var near = new List<int>();
            foreach (var c in pieces[i].cells)
                foreach (var d in dirs)
                    if (owner.TryGetValue(c + d, out int j) && j < i && !near.Contains(j))
                        near.Add(j);

            if (tint == Tint.Contrast)
            {
                taken.Clear();
                foreach (var j in near) taken.Add(System.Array.IndexOf(BlockColorPalette.Themes, hue[j]));
                int pick = 0;
                while (pick < 5 && taken.Contains(pick)) pick++;
                hue[i] = BlockColorPalette.Themes[pick];
            }

            taken.Clear();
            foreach (var j in near) if (hue[j] == hue[i]) taken.Add(shade[j]);
            int k = 0;
            while (taken.Contains(Level(k))) k++;
            shade[i] = Level(k);
        }

        var outp = new Color[pieces.Count];
        for (int i = 0; i < pieces.Count; i++)
        {
            var b = BlockColorPalette.Get(hue[i]);
            if (pieceShade <= 0f || shade[i] == 0) { outp[i] = b; continue; }
            Color.RGBToHSV(b, out float h, out float sa, out float v);
            outp[i] = Color.HSVToRGB(h, sa, Mathf.Clamp01(v * (1f + shade[i] * pieceShade)));
        }
        return outp;
    }

    // 0, +1, -1, +2, -2 … — walk outward from the true palette value so the majority
    // of pieces sit exactly on it and only crowded ones drift.
    static int Level(int k) => (k % 2 == 0 ? 1 : -1) * ((k + 1) / 2);

    // ── THE TILER ────────────────────────────────────────────────────────────

    // Express a cell set as real block shapes: biggest pieces first, every rotation
    // tried, Single as the last resort. Greedy rather than exhaustive — a perfect
    // packing is an NP-hard search and nobody is going to notice the difference
    // between "optimal" and "no holes", which greedy already guarantees because the
    // fallback covers exactly one cell.
    List<Placed> Tile(HashSet<Vector3Int> cells, System.Random rng)
    {
        var remaining = new HashSet<Vector3Int>(cells);

        var order = new List<Vector3Int>(cells);
        order.Sort((a, b) => a.y != b.y ? a.y - b.y
                           : a.z != b.z ? a.z - b.z
                           : a.x - b.x);

        var lib = Library(rng);
        var placed = new List<Placed>();

        foreach (var target in order)
        {
            if (!remaining.Contains(target)) continue;

            bool done = false;
            foreach (var cand in lib)
            {
                // Try every cell of the candidate as the one that lands on `target`,
                // so a piece can reach backwards over ground already covered rather
                // than only ever growing forward from its own first cell. Without
                // this the fallback fires constantly and the building really does
                // end up made of single cubes.
                foreach (var anchor in cand.cells)
                {
                    var off = target - anchor;
                    bool fits = true;
                    foreach (var c in cand.cells)
                        if (!remaining.Contains(c + off)) { fits = false; break; }
                    if (!fits) continue;

                    var final = new Vector3Int[cand.cells.Length];
                    for (int i = 0; i < final.Length; i++)
                    {
                        final[i] = cand.cells[i] + off;
                        remaining.Remove(final[i]);
                    }
                    placed.Add(new Placed { shape = cand.shape, cells = final });
                    done = true;
                    break;
                }
                if (done) break;
            }

            if (done) continue;

            remaining.Remove(target);
            placed.Add(new Placed { shape = BlockShape.Single, cells = new[] { target } });
        }
        return placed;
    }

    struct Candidate { public BlockShape shape; public Vector3Int[] cells; }

    // Every shape in every distinct orientation, largest first, ties shuffled — the
    // shuffle is what makes `seed` do anything, and why the same building can come
    // back assembled out of a different set of blocks.
    static List<Candidate> Library(System.Random rng)
    {
        var all = new List<Candidate>();
        var shapes = new[]
        {
            BlockShape.I4, BlockShape.O2x2, BlockShape.L4, BlockShape.T4,
            BlockShape.S4, BlockShape.Corner3D,
            BlockShape.I3, BlockShape.L3, BlockShape.I2,
        };

        foreach (var sh in shapes)
        {
            var seen = new HashSet<string>();
            foreach (var rot in Rotations(Cells(sh)))
                if (seen.Add(Key(rot))) all.Add(new Candidate { shape = sh, cells = rot });
        }

        all.Sort((a, b) =>
        {
            int d = b.cells.Length - a.cells.Length;
            return d != 0 ? d : rng.Next(-1, 2);
        });
        return all;
    }

    // The nine authored shapes, matching the BlockData assets in
    // scriptableObject/BlockData/New Setup one for one. Held here rather than loaded,
    // because those assets are not under a Resources folder and a decoration tool
    // should not need scene wiring to run.
    static Vector3Int[] Cells(BlockShape s) => s switch
    {
        BlockShape.I2       => new Vector3Int[] { new(0,0,0), new(1,0,0) },
        BlockShape.I3       => new Vector3Int[] { new(0,0,0), new(1,0,0), new(2,0,0) },
        BlockShape.I4       => new Vector3Int[] { new(0,0,0), new(1,0,0), new(2,0,0), new(3,0,0) },
        BlockShape.L3       => new Vector3Int[] { new(0,0,0), new(1,0,0), new(1,0,1) },
        BlockShape.L4       => new Vector3Int[] { new(0,0,0), new(1,0,0), new(2,0,0), new(2,0,1) },
        BlockShape.T4       => new Vector3Int[] { new(0,0,0), new(1,0,0), new(2,0,0), new(1,0,1) },
        BlockShape.S4       => new Vector3Int[] { new(0,0,0), new(1,0,0), new(1,0,1), new(2,0,1) },
        BlockShape.O2x2     => new Vector3Int[] { new(0,0,0), new(1,0,0), new(0,0,1), new(1,0,1) },
        BlockShape.Corner3D => new Vector3Int[] { new(0,0,0), new(1,0,0), new(0,0,1), new(0,1,0) },
        _                   => new Vector3Int[] { new(0,0,0) },
    };

    // All 16 turns about X and Y. The authored shapes are flat in XZ, so without the
    // X turns nothing could ever stand up — no column, no wall, no arch.
    static IEnumerable<Vector3Int[]> Rotations(Vector3Int[] cells)
    {
        for (int rx = 0; rx < 4; rx++)
            for (int ry = 0; ry < 4; ry++)
            {
                var outp = new Vector3Int[cells.Length];
                for (int i = 0; i < cells.Length; i++)
                {
                    var c = cells[i];
                    for (int k = 0; k < rx; k++) c = new Vector3Int(c.x, -c.z, c.y);
                    for (int k = 0; k < ry; k++) c = new Vector3Int(-c.z, c.y, c.x);
                    outp[i] = c;
                }
                yield return Normalise(outp);
            }
    }

    static Vector3Int[] Normalise(Vector3Int[] cells)
    {
        var min = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
        foreach (var c in cells) min = Vector3Int.Min(min, c);
        var outp = new Vector3Int[cells.Length];
        for (int i = 0; i < cells.Length; i++) outp[i] = cells[i] - min;
        System.Array.Sort(outp, (a, b) => a.y != b.y ? a.y - b.y
                                        : a.z != b.z ? a.z - b.z
                                        : a.x - b.x);
        return outp;
    }

    static string Key(Vector3Int[] cells)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in cells) sb.Append(c.x).Append(',').Append(c.y).Append(',').Append(c.z).Append(';');
        return sb.ToString();
    }

    // ── SPAWNING ─────────────────────────────────────────────────────────────

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

        // Colliders would put thousands of shapes into the physics scene for a thing
        // nothing can touch, and gameplay scripts expect a real placement behind them.
        foreach (var col in go.GetComponentsInChildren<Collider>(true)) DestroyImmediate(col);
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(mb);
        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) MpbColor.Set(r, c);

        go.SetActive(true);
    }

    [ContextMenu("Aim camera")]
    public void AimCamera()
    {
        var cam = Camera.main;
        if (cam == null || !_hasBounds) return;

        var rot = Quaternion.Euler(35.264f, -45f, 0f);       // the game's own isometric
        float extent = Mathf.Max(_bounds.extents.x,
                       Mathf.Max(_bounds.extents.y, _bounds.extents.z));

        cam.orthographic       = true;
        cam.orthographicSize   = extent * 1.2f;
        cam.transform.rotation = rot;
        cam.transform.position = _bounds.center - rot * Vector3.forward * (extent * 8f);
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane  = extent * 40f;
    }
}
