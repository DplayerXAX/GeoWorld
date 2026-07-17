using System.Collections.Generic;

public class SurfacePathfinding
{
    // Faces whose normal points up are "top" faces (walking on top of a block).
    static bool IsTop(FaceNode f) => f != null && f.normal.y > 0.5f;

    // Faces whose normal points down — the UNDERSIDE of a block. The player is
    // looking at the board from above and rarely orbits underneath it, so a route
    // that crawls along here is effectively invisible: enemies vanish and reappear
    // with no explanation of where they went.
    static bool IsUnderside(FaceNode f) => f != null && f.normal.y < -0.5f;

    // Cost per step onto `n`, in hop units. Distance is still the primary term,
    // but VISIBILITY is now a real cost rather than a tie-break:
    //   top       → free
    //   side      → a nudge (only decides near-ties; sides read fine on screen)
    //   underside → worth a multi-hop detour to avoid
    //
    // These are penalties, not bans: if the only way through is underneath, the
    // path still goes there rather than failing.
    static long StepCost(FaceNode n, long hop)
    {
        if (IsTop(n))       return hop;
        if (IsUnderside(n)) return hop + hop * 3L;   // take up to a 3-hop detour to stay visible
        return hop + hop / 8L;                       // side: 8 side-steps == 1 extra hop
    }

    // Cheapest path from start to end, weighted so the route the enemies actually
    // take is one the player can SEE (see StepCost).
    public static List<FaceNode> FindPath(List<FaceNode> startFaces, List<FaceNode> endFaces)
    {
        if (startFaces == null || endFaces == null || startFaces.Count == 0 || endFaces.Count == 0)
            return null;

        const long HOP = 1_000_000L;   // far larger than any accumulated face penalty

        var endSet   = new HashSet<FaceNode>(endFaces);
        var startSet = new HashSet<FaceNode>(startFaces);
        var dist     = new Dictionary<FaceNode, long>();
        var cameFrom = new Dictionary<FaceNode, FaceNode>();
        var open     = new List<FaceNode>();
        var inOpen   = new HashSet<FaceNode>();

        foreach (var s in startFaces)
            if (s != null && !dist.ContainsKey(s)) { dist[s] = 0L; open.Add(s); inOpen.Add(s); }

        FaceNode reachedEnd = null;

        while (open.Count > 0)
        {
            // Extract the lowest-cost open node (linear min — graphs here are small).
            int best = 0;
            for (int i = 1; i < open.Count; i++)
                if (dist[open[i]] < dist[open[best]]) best = i;
            var current = open[best];
            open.RemoveAt(best);
            inOpen.Remove(current);

            if (endSet.Contains(current)) { reachedEnd = current; break; }

            long cd = dist[current];
            foreach (var n in current.neighbors)
            {
                if (n == null) continue;
                long nd = cd + StepCost(n, HOP);
                if (!dist.TryGetValue(n, out long old) || nd < old)
                {
                    dist[n]     = nd;
                    cameFrom[n] = current;
                    if (!inOpen.Contains(n)) { open.Add(n); inOpen.Add(n); }
                }
            }
        }

        if (reachedEnd == null) return null;

        var path = new List<FaceNode>();
        var cur  = reachedEnd;
        while (!startSet.Contains(cur))
        {
            path.Add(cur);
            cur = cameFrom[cur];
        }
        path.Add(cur);
        path.Reverse();
        return path;
    }
}
