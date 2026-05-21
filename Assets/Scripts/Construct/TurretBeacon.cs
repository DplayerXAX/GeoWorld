using UnityEngine;

// Floating diamond that sits above a turret block to distinguish it from
// normal blocks at a glance. Gentle spin + bob. Attached at spawn by
// PlacementController / ShopController.
public class TurretBeacon : MonoBehaviour
{
    public Vector3 spinDegPerSec = new Vector3(0f, 110f, 0f);
    public float   bobHeight     = 0.08f;
    public float   bobSpeed      = 1.6f;

    Vector3 _basePos;
    float   _phase;

    void Start()
    {
        _basePos = transform.localPosition;
        _phase   = Random.Range(0f, Mathf.PI * 2f);
    }

    void Update()
    {
        transform.Rotate(spinDegPerSec * Time.deltaTime, Space.Self);
        float bob = Mathf.Sin(Time.time * bobSpeed + _phase) * bobHeight;
        transform.localPosition = _basePos + Vector3.up * bob;
    }
}
