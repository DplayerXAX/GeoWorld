using System.Collections.Generic;
using UnityEngine;

public partial class PlacementController
{
    // =========================
    // COMBAT RIPPLE
    // =========================

    /// <summary>
    /// Called by GameFlowManager when entering the Running phase.
    /// Grows cube-by-cube along the path, each cube extending from its entry
    /// edge in the direction of travel like a branch creeping forward.
    /// Off-path cubes bloom afterwards, rippling out from the nearest path cube.
    /// </summary>
    public void TriggerCombatRipple(List<FaceNode> path)
    {
        StartCoroutine(CombatRippleCoroutine(path));
    }

    System.Collections.IEnumerator CombatRippleCoroutine(List<FaceNode> path)
    {
        var all = grid.GetAllInstances();
        if (all.Count == 0) yield break;

        // Step 1: collect every cube (cell-sized child of a placed block) and
        // hide it. Children are created 1:1 with occupiedCells in render order.
        var cubes   = new List<(Transform t, Vector3Int cell, Vector3 worldPos, Vector3 origLocalPos)>();
        var beacons = new List<(Transform t, Vector3 origScale, Vector3 worldPos)>();
        foreach (var ins in all)
        {
            if (ins.visualObject == null) continue;
            // Parent stays at scale 1; we drive each child independently.
            ins.visualObject.transform.localScale = Vector3.one;

            int count = Mathf.Min(ins.visualObject.transform.childCount, ins.occupiedCells.Count);
            for (int i = 0; i < count; i++)
            {
                var t = ins.visualObject.transform.GetChild(i);
                cubes.Add((t, ins.occupiedCells[i], t.position, t.localPosition));
                t.localScale = Vector3.zero;
            }

            // Turret beacons aren't cube children (the ripple skips them) — collect
            // + hide them so they can pop back in with the wavefront too.
            var beacon = ins.visualObject.GetComponentInChildren<TurretBeacon>();
            if (beacon != null)
            {
                beacons.Add((beacon.transform, beacon.transform.localScale, beacon.transform.position));
                beacon.transform.localScale = Vector3.zero;
            }
        }
        if (cubes.Count == 0) yield break;
        yield return null;

        // Step 2: cell first index along path.
        var pathIdx = new Dictionary<Vector3Int, int>();
        if (path != null)
            for (int i = 0; i < path.Count; i++)
                if (!pathIdx.ContainsKey(path[i].cell))
                    pathIdx[path[i].cell] = i;

        // Step 3: split into on-path (with index + entry direction) and off-path.
        var onPath  = new List<(Transform t, int idx, Vector3 worldPos, Vector3 origLocalPos, Vector3 entryDirWorld)>();
        var offPath = new List<(Transform t, Vector3 worldPos, Vector3 origLocalPos)>();
        foreach (var c in cubes)
        {
            if (pathIdx.TryGetValue(c.cell, out int idx))
            {
                Vector3 entry = Vector3.zero;
                if (path != null)
                {
                    // Entry direction: from the previous path cell into this one.
                    // For idx 0, use the direction toward the next cell so the seed
                    // still grows outward instead of expanding from its center.
                    if (idx > 0)
                        entry = ((Vector3)(path[idx].cell - path[idx - 1].cell)).normalized;
                    else if (path.Count > 1)
                        entry = ((Vector3)(path[1].cell - path[0].cell)).normalized;
                }
                onPath.Add((c.t, idx, c.worldPos, c.origLocalPos, entry));
            }
            else
            {
                offPath.Add((c.t, c.worldPos, c.origLocalPos));
            }
        }
        onPath.Sort((a, b) => a.idx.CompareTo(b.idx));

        // Step 4: branch along the path, cube by cube.
        const float perCellStep = 0.07f;   // delay between consecutive path cells
        const float cubeDur     = 0.22f;   // per-cube growth time

        float pathSweepEnd = 0f;
        foreach (var c in onPath)
        {
            float delay = c.idx * perCellStep;
            StartCoroutine(BranchSproutCube(c.t, delay, cubeDur, c.entryDirWorld, c.origLocalPos));
            pathSweepEnd = Mathf.Max(pathSweepEnd, delay + cubeDur);
        }

        // Re-grow turret beacons + synergy FX in sync with the wavefront so they
        // don't sit static while the blocks sprout back. delayFor(worldPos) = the
        // nearest path cube's sweep time + a small distance falloff.
        System.Func<Vector3, float> delayFor = wp =>
        {
            if (onPath.Count == 0) return 0f;
            float bestSqr = float.MaxValue;
            int   bestIdx = 0;
            for (int i = 0; i < onPath.Count; i++)
            {
                float dd = (onPath[i].worldPos - wp).sqrMagnitude;
                if (dd < bestSqr) { bestSqr = dd; bestIdx = onPath[i].idx; }
            }
            return bestIdx * perCellStep + Mathf.Sqrt(bestSqr) * 0.06f;
        };

        for (int i = 0; i < beacons.Count; i++)
            StartCoroutine(BeaconPop(beacons[i].t, delayFor(beacons[i].worldPos), beacons[i].origScale));

        SynergyVisualFX.ReplayGrowIn(delayFor);

        // Step 5: off-path cubes bloom from their nearest path cube. Small
        // overlap with the path sweep so it doesn't feel halted.
        if (offPath.Count == 0) yield break;

        var anchors = new List<Vector3>(onPath.Count);
        foreach (var c in onPath) anchors.Add(c.worldPos);
        if (anchors.Count == 0)
        {
            Vector3 c = Vector3.zero;
            foreach (var o in offPath) c += o.worldPos;
            anchors.Add(c / offPath.Count);
        }

        float maxOffDist = 0f;
        var offDists = new float[offPath.Count];
        for (int i = 0; i < offPath.Count; i++)
        {
            float minD = float.MaxValue;
            foreach (var a in anchors)
            {
                float d = Vector3.Distance(offPath[i].worldPos, a);
                if (d < minD) minD = d;
            }
            offDists[i] = (minD == float.MaxValue) ? 0f : minD;
            if (offDists[i] > maxOffDist) maxOffDist = offDists[i];
        }
        if (maxOffDist < 0.001f) maxOffDist = 1f;

        float offStart = Mathf.Max(0f, pathSweepEnd - cubeDur * 0.5f);
        const float offSpread = 0.55f;
        for (int i = 0; i < offPath.Count; i++)
        {
            float delay = offStart + (offDists[i] / maxOffDist) * offSpread;
            // No entry direction cube does a uniform bloom from its centre.
            StartCoroutine(BranchSproutCube(offPath[i].t, delay, cubeDur, Vector3.zero, offPath[i].origLocalPos));
        }
    }

    // ── Per-cube branch growth ──────────────────────────────────────────────
    // Path cubes scale anisotropically along the entry axis, with a position
    // offset so the back edge stays glued to the previous cube visually the
    // cube "extends" outward like a branch tip.
    // Off-path cubes (entryDirWorld = 0) just bloom uniformly from their centre.
    static System.Collections.IEnumerator BranchSproutCube(
        Transform t, float delay, float dur, Vector3 entryDirWorld, Vector3 origLocalPos)
    {
        if (delay > 0.001f) yield return new WaitForSeconds(delay);
        if (t == null) yield break;

        var rends = t.GetComponentsInChildren<Renderer>();
        int rc    = rends.Length;
        var orig  = new Color[rc];
        for (int i = 0; i < rc; i++)
            if (rends[i]) orig[i] = MpbColor.Get(rends[i]);

        // Resolve entry direction in the cube's local frame and pick the
        // dominant axis. Block rotations are 90°-stepped, so this maps cleanly.
        int   axis = -1;
        float sign = 1f;
        if (entryDirWorld.sqrMagnitude > 0.001f && t.parent != null)
        {
            Vector3 local = t.parent.InverseTransformDirection(entryDirWorld);
            float ax = Mathf.Abs(local.x), ay = Mathf.Abs(local.y), az = Mathf.Abs(local.z);
            if (ax >= ay && ax >= az)      { axis = 0; sign = Mathf.Sign(local.x); }
            else if (ay >= az)             { axis = 1; sign = Mathf.Sign(local.y); }
            else                           { axis = 2; sign = Mathf.Sign(local.z); }
        }

        float elapsed = 0f;
        while (elapsed < dur)
        {
            if (t == null) yield break;
            elapsed += Time.deltaTime;
            float p = Mathf.Clamp01(elapsed / dur);
            float e = 1f - Mathf.Pow(1f - p, 4f);   // easeOutQuart

            if (axis >= 0)
            {
                // Perp axes start a bit thick so the tip looks like a bud, not a needle.
                float perp  = Mathf.Lerp(0.72f, 1f, e);
                float along = e;

                Vector3 scl = new Vector3(perp, perp, perp);
                scl[axis] = along;
                t.localScale = scl;

                Vector3 offset = Vector3.zero;
                offset[axis] = -sign * (1f - along) * 0.5f;
                t.localPosition = origLocalPos + offset;
            }
            else
            {
                t.localScale = Vector3.one * e;
            }

            // Brief brightness pulse wavefront passing through.
            float bright = (1f - p) * (1f - p) * 0.7f;
            for (int i = 0; i < rc; i++)
                if (rends[i]) MpbColor.Set(rends[i], Color.Lerp(orig[i], Color.white, bright));

            yield return null;
        }

        if (t != null)
        {
            t.localScale    = Vector3.one;
            t.localPosition = origLocalPos;
        }
        for (int i = 0; i < rc; i++)
            if (rends[i]) MpbColor.Set(rends[i], orig[i]);
    }

    // Turret beacons aren't cube children, so the ripple pops them separately:
    // hidden at scale 0, then a quick overshoot grow when the wavefront arrives.
    static System.Collections.IEnumerator BeaconPop(Transform t, float delay, Vector3 targetScale)
    {
        if (t == null) yield break;
        t.localScale = Vector3.zero;
        if (delay > 0.001f) yield return new WaitForSeconds(delay);

        const float dur = 0.28f;
        float e = 0f;
        while (e < dur)
        {
            if (t == null) yield break;
            e += Time.deltaTime;
            float p  = Mathf.Clamp01(e / dur);
            float xm = p - 1f;
            float s  = 1f + 2.70158f * xm * xm * xm + 1.70158f * xm * xm;   // EaseOutBack overshoot
            t.localScale = targetScale * s;
            yield return null;
        }
        if (t != null) t.localScale = targetScale;
    }

    // ── Growth animation: 0 1.12 1.0 with cubic ease-out ────────────────
    // Overshoot to 1.12 gives a satisfying "snap into place" feel.
    static System.Collections.IEnumerator GrowIn(GameObject obj)
    {
        if (obj == null) yield break;

        const float dur     = 0.22f;
        const float peak    = 1.12f;   // overshoot scale
        const float peakAt  = 0.55f;   // fraction of dur at which we hit peak
        float       elapsed = 0f;

        while (elapsed < dur)
        {
            if (obj == null) yield break;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / dur);

            float scale;
            if (t < peakAt)
            {
                // Phase 1: 0 peak  (ease-out cubic)
                float t1 = t / peakAt;
                float e  = 1f - (1f - t1) * (1f - t1) * (1f - t1);
                scale = e * peak;
            }
            else
            {
                // Phase 2: peak 1  (ease-in-out)
                float t2 = (t - peakAt) / (1f - peakAt);
                float e  = t2 * t2 * (3f - 2f * t2);
                scale = Mathf.Lerp(peak, 1f, e);
            }

            obj.transform.localScale = Vector3.one * scale;
            yield return null;
        }

        if (obj != null) obj.transform.localScale = Vector3.one;
    }

    

}
