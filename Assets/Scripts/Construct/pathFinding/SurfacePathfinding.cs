using System.Collections.Generic;

public class SurfacePathfinding
{
    public static List<FaceNode> FindPath(List<FaceNode> startFaces, List<FaceNode> endFaces)
    {
        var endSet = new HashSet<FaceNode>(endFaces);

        Queue<FaceNode> open = new();
        Dictionary<FaceNode, FaceNode> cameFrom = new();
        HashSet<FaceNode> visited = new();

        foreach (var s in startFaces)
        {
            open.Enqueue(s);
            visited.Add(s);
        }

        FaceNode reachedEnd = null;

        while (open.Count > 0)
        {
            var current = open.Dequeue();

            if (endSet.Contains(current))
            {
                reachedEnd = current;
                break;
            }

            foreach (var n in current.neighbors)
            {
                if (visited.Contains(n)) continue;
                visited.Add(n);
                cameFrom[n] = current;
                open.Enqueue(n);
            }
        }

        if (reachedEnd == null) return null;

        List<FaceNode> path = new();
        var cur = reachedEnd;

        while (!startFaces.Contains(cur))
        {
            path.Add(cur);
            cur = cameFrom[cur];
        }

        path.Add(cur); 
        path.Reverse();
        return path;
    }
}