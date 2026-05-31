using UnityEngine;

// Tiny utility for visualizer decorations that need to spin.
// Attach via `obj.AddComponent<SimpleRotator>().axis = Vector3.up; .speedDeg = 30f;`
public class SimpleRotator : MonoBehaviour
{
    public Vector3 axis     = Vector3.up;
    public float   speedDeg = 30f;

    void Update()
    {
        transform.Rotate(axis, speedDeg * Time.deltaTime, Space.Self);
    }
}
