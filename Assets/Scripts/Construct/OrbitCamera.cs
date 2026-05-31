using UnityEngine;

public class OrbitCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 10f;
    public float speed = 120f;

    [Range(-89f, 0f)] public float minPitch = -80f;
    [Range(0f, 89f)] public float maxPitch = 80f;
    public bool useOrthographic = true;

    [Header("Ortho")]
    public float orthoSize = 5f;
    public float orthoZoomSpeed = 2f;

    public Vector3 orthoAngle = new Vector3(35f, 45f, 0f);

    [Header("Projection toggle")]
    public KeyCode projectionToggleKey = KeyCode.F8;
    [Tooltip("Perspective FOV applied when switching out of ortho.")]
    public float perspectiveFov = 50f;
    private float yaw;
    private float pitch = 20f;

    public float transitionSpeed = 6f;
    public Camera myCam;
    private Vector3 currentFocusPoint;
    private Transform desiredTarget;
    private float targetDistance;
    private float targetOrthoSize;

    public Vector3 FocusPoint => currentFocusPoint;
    public float   Yaw        => yaw;
    public float   Pitch      => pitch;

    // Hard-reset orbit state. Used by snapshot restore.
    public void ApplyState(Vector3 focus, float dist, float newYaw, float newPitch)
    {
        currentFocusPoint = focus;
        distance          = dist;
        yaw               = newYaw;
        pitch             = Mathf.Clamp(newPitch, minPitch, maxPitch);
        _panOffset        = Vector3.zero;
    }

    // Free pan offset added on top of the focus target's position.
    // Reset whenever SetFocus is called so selecting an object snaps the
    // camera back to it.
    private Vector3 _panOffset;

    void Start()
    {
        pitch = orthoAngle.x;
        if (myCam != null)
        {
            myCam.orthographic = useOrthographic;

            if (useOrthographic)
                myCam.orthographicSize = orthoSize;
        }

        targetDistance = distance;
        targetOrthoSize = orthoSize;

        if (target != null)
        {
            desiredTarget = target;
            currentFocusPoint = target.position;
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(projectionToggleKey))
            ToggleProjection();
    }

    public void ToggleProjection() => SetOrthographic(!useOrthographic);

    public void SetOrthographic(bool ortho)
    {
        if (ortho == useOrthographic) return;

        // LateUpdate adds orthoAngle.y to yaw in ortho mode. Bake / unbake that
        // offset so the visible camera direction stays continuous across the swap.
        if (ortho) yaw -= orthoAngle.y;
        else       yaw += orthoAngle.y;

        useOrthographic = ortho;

        if (myCam != null)
        {
            myCam.orthographic = ortho;
            if (ortho) myCam.orthographicSize = orthoSize;
            else       myCam.fieldOfView      = perspectiveFov;
        }

        // Re-clamp pitch to the active mode's range so the next mouse drag
        // doesn't suddenly snap.
        pitch = ortho
            ? Mathf.Clamp(pitch, 10f, 80f)
            : Mathf.Clamp(pitch, minPitch, maxPitch);
    }

    public void AddDistance(float delta)
    {
        if (useOrthographic)
        {
            targetOrthoSize = Mathf.Clamp(
                targetOrthoSize + delta * 0.5f,
                2f,
                12f
            );
        }
        else
        {
            targetDistance = Mathf.Clamp(
                targetDistance + delta,
                2f,
                40f
            );
        }
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
        distance = Mathf.Lerp(
    distance,
    targetDistance,
    1f - Mathf.Exp(-transitionSpeed * Time.deltaTime)
);

        orthoSize = Mathf.Lerp(
            orthoSize,
            targetOrthoSize,
            1f - Mathf.Exp(-transitionSpeed * Time.deltaTime)
        );
        if (myCam != null && useOrthographic)
        {
            myCam.orthographicSize = orthoSize;
        }
        if (desiredTarget == null) return;


        Vector3 focusGoal = desiredTarget.position + _panOffset;

        currentFocusPoint = Vector3.Lerp(
            currentFocusPoint,
            focusGoal,
            1f - Mathf.Exp(-transitionSpeed * Time.deltaTime)
        );

        if (!useOrthographic)
        {
            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * speed * Time.deltaTime;
                pitch -= Input.GetAxis("Mouse Y") * speed * Time.deltaTime;
                pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            }
        }
        else
        {
            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * speed * 0.3f * Time.deltaTime;

                pitch -= Input.GetAxis("Mouse Y") * speed * 0.2f * Time.deltaTime;
                pitch = Mathf.Clamp(pitch, 10f, 80f);
            }
        }
        Quaternion rot;

        if (useOrthographic)
        {
            rot = Quaternion.Euler(
     pitch,
     yaw + orthoAngle.y,
     0
 );
        }
        else
        {
            rot = Quaternion.Euler(pitch, yaw, 0);
        }

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
