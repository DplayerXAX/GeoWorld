using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class EnergyFlowManager : MonoBehaviour
{
    public static EnergyFlowManager Instance;

    LineRenderer line;

    [Header("Flow")]
    public float flowSpeed = 1.5f;
    public float width = 0.2f;

    float flowOffset = 0f;

    void Awake()
    {
        Instance = this;
        line = GetComponent<LineRenderer>();
        line.positionCount = 0;
        line.widthMultiplier = width;
    }

    public void SetPath(List<FaceNode> path)
    {
        if (path == null || path.Count == 0) return;

        line.positionCount = path.Count;

        for (int i = 0; i < path.Count; i++)
        {
            line.SetPosition(i, path[i].worldPos + Vector3.up * 0.1f);
        }
    }

    void Update()
    {
        if (line.positionCount == 0) return;

        flowOffset += Time.deltaTime * flowSpeed;

        line.material.SetTextureOffset("_BaseMap", new Vector2(flowOffset, 0));
    }

    public void Clear()
    {
        line.positionCount = 0;
    }
}