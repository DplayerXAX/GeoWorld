using System.Collections.Generic;

public class SurfacePathfinding
{
    // Faces whose normal points up are "top" faces (walking on top of a block).
    static bool IsTop(FaceNode f) => f != null && f.normal.y > 0.5f;

    // Shortest path by hop count, with a tie-break that PREFERS top-surface faces:
    // cost = hops * HOP + (number of non-top steps). HOP dominates so distance is
    // still primary; among equal-length paths the one that stays on top wins.
    public static List<FaceNode> FindPath(List<FaceNode> startFaces, List<FaceNode> endFaces)
    {
        if (startFaces == null || endFaces == null || startFaces.Count == 0 || endFaces.Count == 0)
            return null;

        const long HOP = 1_000_000L;   // far larger than any side-step tie-break total

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
                long step = HOP + (IsTop(n) ? 0L : 1L);
                long nd   = cd + step;
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
