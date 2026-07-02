using UnityEngine;

// Floating diamond that sits above a turret block to distinguish it from
// normal blocks at a glance. Gentle spin + bob. Attached at spawn by
// PlacementController / ShopController.
public class TurretBeacon : MonoBehaviour
{
    public float minSpinSpeed = 80f;
    public float maxSpinSpeed = 140f;

    public float bobHeight = 0.08f;
    public float bobSpeed = 1.6f;

    Vector3 _basePos;
    float _phase;
    Vector3 _spin;

    void Start()
    {
        _basePos = transform.localPosition;
        _phase = Random.Range(0f, Mathf.PI * 2f);

        _spin = new Vector3(
            Random.Range(-1f, 1f),
            Random.Range(-1f, 1f),
            Random.Range(-1f, 1f)
        ).normalized * Random.Range(minSpinSpeed, maxSpinSpeed);
    }

    void Update()
    {
        transform.Rotate(_spin * Time.deltaTime, Space.Self);

        float bob = Mathf.Sin(Time.time * bobSpeed + _phase) * bobHeight;
        transform.localPosition = _basePos + Vector3.up * bob;
    }
}