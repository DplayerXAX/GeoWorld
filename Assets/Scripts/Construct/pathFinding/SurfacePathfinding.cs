using System.Collections.Generic;

public class SurfacePathfinding
{
    public static List<FaceNode> FindPath(FaceNode start, FaceNode end)
    {
        Queue<FaceNode> open = new();
        Dictionary<FaceNode, FaceNode> cameFrom = new();
        HashSet<FaceNode> visited = new();

        open.Enqueue(start);
        visited.Add(start);

        while (open.Count > 0)
        {
            var current = open.Dequeue();

            if (current == end)
                break;

            foreach (var n in current.neighbors)
            {
                if (visited.Contains(n)) continue;

                visited.Add(n);
                cameFrom[n] = current;
                open.Enqueue(n);
            }
        }

        if (!cameFrom.ContainsKey(end))
            return null;

        List<FaceNode> path = new();
        var cur = end;

        while (cur != start)
        {
            path.Add(cur);
            cur = cameFrom[cur];
        }

        path.Add(start);
        path.Reverse();

        return path;
    }
}