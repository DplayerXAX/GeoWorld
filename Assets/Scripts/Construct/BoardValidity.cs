using System;
using System.Collections.Generic;
using UnityEngine;

// Which pieces on the board are actually PART of the build.
//
// ── A STATE, NOT A GATE ──────────────────────────────────────────────────────
//
// Placement used to refuse anything that did not touch an existing block, and
// picking a block up was refused if it would leave a turret with nothing to rest
// on. Both rules protected the same invariant — everything traces back to the
// endpoints — by forbidding the moves that would break it.
//
// Now the moves are allowed and the invariant is REPORTED instead. Anything that
// does not connect back to an endpoint is flagged: it gets hazard stripes, a turret
// in that state holds its fire, and the player is told why. The moment it is
// reconnected — a block placed beside it, the lifted block put back — all of that
// lifts on its own. The player gets to build in any order, and still always knows
// which parts of the build are live.
//
// ── WHAT "CONNECTED" MEANS ───────────────────────────────────────────────────
//
// Reachable from an endpoint through occupied cells, using the same 18-cell
// neighbourhood placement always used (faces and edges; corner-only contact never
// counted), plus portal links. That is deliberately the SAME notion of adjacency
// the enemies' walk graph is built from — SurfaceGraphBuilder links faces across
// faces and edges, and adds portal pairs as extra edges — so a block this calls
// detached is also one no enemy route can reach. The two never disagree.
//
// A Chaos Block's cells (GridSystem.IsNoSupport) conduct nothing, which keeps the
// old rule that you cannot anchor a turret to one to attack it.
public static class BoardValidity
{
    // Every piece that went from attached to detached in one reconcile, together —
    // so lifting the one block bridging five others produces one message, not six.
    public static event Action<IReadOnlyList<PlacedBlockInstance>> BecameDetached;

    // Level setup reconciles too (the inherited board, the authored layout), and
    // should flag islands without shouting about them before the player has done
    // anything. GameFlowManager holds this on for the length of its setup.
    public static bool Quiet;

    static readonly HashSet<Vector3Int> _reached = new();
    static readonly Queue<Vector3Int>   _queue   = new();
    static readonly List<PlacedBlockInstance> _newlyDetached = new();
    static readonly Dictionary<Vector3Int, List<Vector3Int>> _portalEdges = new();

    public static void Reconcile(GridSystem grid, IReadOnlyList<Vector3Int> starts, IReadOnlyList<Vector3Int> ends)
    {
        if (grid == null) return;

        Flood(grid, starts, ends);

        _newlyDetached.Clear();
        foreach (var ins in grid.GetAllInstances())
        {
            if (ins?.data == null || ins.visualObject == null) continue;

            bool detached = !Attached(grid, ins);
            if (detached == ins.detached) continue;

            ins.detached = detached;
            Present(ins);
            if (detached) _newlyDetached.Add(ins);
        }

        if (!Quiet && _newlyDetached.Count > 0) BecameDetached?.Invoke(_newlyDetached);
    }

    // Would a piece dropped on these cells be part of the build? For the placement
    // ghost, so the player sees the warning BEFORE committing rather than after.
    //
    // Answered from the last reconcile's reached set, with no new flood: a new piece
    // is attached exactly when one of its cells sits in the 18-neighbourhood of a
    // cell that already is. That is O(cells), cheap enough to ask every frame.
    // While a block is being moved it has already been lifted off the grid (and the
    // board reconciled without it), so the answer is about the board as it will be.
    public static bool WouldAttach(IReadOnlyList<Vector3Int> worldCells)
    {
        if (worldCells == null) return false;
        for (int i = 0; i < worldCells.Count; i++)
        {
            var c = worldCells[i];
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dy == 0 && dz == 0) continue;
                if (GridSystem.IsCornerOffset(dx, dy, dz)) continue;
                if (_reached.Contains(new Vector3Int(c.x + dx, c.y + dy, c.z + dz))) return true;
            }
        }
        return false;
    }

    static void Flood(GridSystem grid, IReadOnlyList<Vector3Int> starts, IReadOnlyList<Vector3Int> ends)
    {
        _reached.Clear();
        _queue.Clear();

        _portalEdges.Clear();
        foreach (var (a, b, _) in DeviceRegistry.PortalLinks())
        {
            Link(a, b);
            Link(b, a);
        }

        Seed(starts);
        Seed(ends);

        while (_queue.Count > 0)
        {
            var c = _queue.Dequeue();

            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dy == 0 && dz == 0) continue;
                if (GridSystem.IsCornerOffset(dx, dy, dz)) continue;
                Visit(grid, new Vector3Int(c.x + dx, c.y + dy, c.z + dz));
            }

            if (_portalEdges.TryGetValue(c, out var far))
                for (int i = 0; i < far.Count; i++) Visit(grid, far[i]);
        }
    }

    static void Seed(IReadOnlyList<Vector3Int> cells)
    {
        if (cells == null) return;
        for (int i = 0; i < cells.Count; i++)
            if (_reached.Add(cells[i])) _queue.Enqueue(cells[i]);
    }

    static void Visit(GridSystem grid, Vector3Int n)
    {
        if (_reached.Contains(n)) return;
        if (!grid.IsOccupied(n) || grid.IsNoSupport(n)) return;
        _reached.Add(n);
        _queue.Enqueue(n);
    }

    static void Link(Vector3Int from, Vector3Int to)
    {
        if (!_portalEdges.TryGetValue(from, out var list)) _portalEdges[from] = list = new List<Vector3Int>();
        list.Add(to);
    }

    static bool Attached(GridSystem grid, PlacedBlockInstance ins)
    {
        // Level furniture is the level's business, not the player's: an authored
        // island is there on purpose, and flagging something the player cannot move
        // or remove would only be telling them about a problem they cannot fix.
        if (ins.locked) return true;

        // Something the ENEMY put down (a Chaos Block) is not part of anyone's build.
        bool anySupport = false;
        foreach (var c in ins.occupiedCells)
        {
            if (!grid.IsNoSupport(c)) anySupport = true;
            if (_reached.Contains(c)) return true;
        }
        return !anySupport;
    }

    // ── Presentation ─────────────────────────────────────────────────────────

    static Material _hazard;
    static Material Hazard
    {
        get
        {
            if (_hazard != null) return _hazard;
            var sh = Shader.Find("GeoWorld/Hazard");
            if (sh == null) { Debug.LogWarning("[BoardValidity] GeoWorld/Hazard shader not found."); return null; }
            _hazard = new Material(sh) { name = "Hazard (runtime, shared)" };
            return _hazard;
        }
    }

    // Swap the piece's OWN materials for the hazard stripes, or put them back.
    //
    // Only the renderers the piece was built with (PlacedBlockInstance.ownRenderers,
    // recorded at placement) — never everything under its visual. Synergy effects
    // hang their own geometry off a block's visual; restyling "all renderers below
    // it" would stripe a Harmony block's vines and leaves along with the block.
    //
    // The outline slot is never STRIPED — stripes on an inverse hull would just be a
    // fat striped shell around the block. Whether it is kept or dropped is
    // PlacementController.hazardKeepsOutline.
    //
    // The block's colour comes back with it. Blocks are tinted through a
    // MaterialPropertyBlock, which stays on the renderer whatever material it is
    // drawing, so restoring the original materials restores the tint untouched.
    static void Present(PlacedBlockInstance ins)
    {
        var tc = ins.visualObject.GetComponentInChildren<TurretController>();
        if (tc != null) tc.Supported = !ins.detached;

        var rends = ins.ownRenderers;
        if (rends == null || rends.Length == 0) return;

        if (ins.detached)
        {
            var mat = Hazard;
            if (mat == null) return;

            ins.savedMaterials ??= new Material[rends.Length][];
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (r == null) continue;

                var orig = r.sharedMaterials;
                ins.savedMaterials[i] = orig;

                // The outline is an EXTRA material slot re-drawing the mesh as an
                // inverse hull, so dropping the slot drops the outline — nothing to
                // hide, it simply is not drawn. A renderer that was nothing but a
                // hull ends up with no materials at all and draws nothing.
                bool keepOutline = PlacementController.Instance != null
                                && PlacementController.Instance.hazardKeepsOutline;
                var swapped = new List<Material>(orig.Length);
                for (int m = 0; m < orig.Length; m++)
                {
                    if (!IsOutline(orig[m])) swapped.Add(mat);
                    else if (keepOutline)     swapped.Add(orig[m]);
                }
                r.sharedMaterials = swapped.ToArray();
            }
        }
        else if (ins.savedMaterials != null)
        {
            for (int i = 0; i < rends.Length && i < ins.savedMaterials.Length; i++)
                if (rends[i] != null && ins.savedMaterials[i] != null)
                    rends[i].sharedMaterials = ins.savedMaterials[i];
            ins.savedMaterials = null;
        }
    }

    static bool IsOutline(Material m)
    {
        if (m == null || m.shader == null) return false;
        var n = m.shader.name;
        return n == "GeoWorld/BlockOutline" || n == "Custom/ObjectOutline";
    }
}
