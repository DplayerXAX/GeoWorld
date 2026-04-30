using UnityEngine;

public class OrbitCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 10f;
    public float speed = 120f;

    [Range(-89f, 0f)] public float minPitch = -80f;
    [Range(0f, 89f)] public float maxPitch = 80f;

    private float yaw;
    private float pitch = 20f;

    public float transitionSpeed = 6f;
    public Camera myCam;
    private Vector3 currentFocusPoint;
    private Transform desiredTarget;

    // Free pan offset added on top of the focus target's position.
    // Reset whenever SetFocus is called so selecting an object snaps the
    // camera back to it.
    private Vector3 _panOffset;

    void Start()
    {
        if (target != null)
        {
            desiredTarget = target;
            currentFocusPoint = target.position;
        }
    }

    public void AddDistance(float delta)
    {
        distance = Mathf.Clamp(distance + delta, 2f, 40f);
    }

    public void SetFocus(Transform newTarget)
    {
        if (desiredTarget != newTarget)
            _panOffset = Vector3.zero; // new focus → cancel any free-pan
        desiredTarget = newTarget;
    }

    // Called by PlacementController on WASD/QE in Select mode.
    public void Pan(Vector3 worldDelta)
    {
        _panOffset += worldDelta;
    }

    void LateUpdate()
    {
        if (desiredTarget == null) return;

        Vector3 focusGoal = desiredTarget.position + _panOffset;

        currentFocusPoint = Vector3.Lerp(
            currentFocusPoint,
            focusGoal,
            1f - Mathf.Exp(-transitionSpeed * Time.deltaTime)
        );

        if (Input.GetMouseButton(1))
        {
            yaw += Input.GetAxis("Mouse X") * speed * Time.deltaTime;
            pitch -= Input.GetAxis("Mouse Y") * speed * Time.deltaTime;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0);
        Vector3 offset = rot * new Vector3(0, 0, -distance);
        Vector3 desiredPos = currentFocusPoint + offset;

        transform.position = Vector3.Lerp(
            transform.position,
            desiredPos,
            1f - Mathf.Exp(-transitionSpeed * Time.deltaTime)
        );

        transform.LookAt(currentFocusPoint);
    }
}
