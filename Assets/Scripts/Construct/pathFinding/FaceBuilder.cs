using System.Collections.Generic;
using UnityEngine;

public class FaceNode
{
    public Vector3Int cell;    
    public Vector3 normal;      

    public Vector3 worldPos;

    public List<FaceNode> neighbors = new();
}
public class FaceBuilder
{
    public static List<FaceNode> BuildFaces(Vector3Int cell, float size)
    {
        List<FaceNode> faces = new();

        Vector3 basePos = new Vector3(cell.x, cell.y, cell.z) * size;

        Vector3[] normals =
        {
            Vector3.right, Vector3.left,
            Vector3.up, Vector3.down,
            Vector3.forward, Vector3.back
        };

        foreach (var n in normals)
        {
            faces.Add(new FaceNode
            {
                cell = cell,
                normal = n,
                worldPos = basePos + n * (size * 0.5f)
            });
        }

        return faces;
    }
}