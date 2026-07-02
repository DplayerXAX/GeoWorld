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

    [Header("Focus framing")]
    [Tooltip("Where the focus point sits on screen (0.5,0.5 = centre). e.g. LevelMapController sets x≈0.3 to bias the selected cell to the left.")]
    public Vector2 focusViewport = new Vector2(0.5f, 0.5f);

    [Header("Gamepad")]
    [Tooltip("Right-stick look sensitivity multiplier (mirrors mouse 'speed').")]
    public float gamepadLookScale = 1f;
    [Tooltip("Trigger/bumper zoom speed.")]
    public float gamepadZoomSpeed = 12f;

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

    // Absolute zoom target. In ortho this is the orthographicSize; in perspective
    // it's the orbit distance. SMALLER = more zoomed in. Clamped to each mode's
    // range and eased by LateUpdate's distance/orthoSize lerp.
    public void SetZoom(float zoom)
    {
        if (useOrthographic) targetOrthoSize = Mathf.Clamp(zoom, 2f, 12f);
        else                 targetDistance  = Mathf.Clamp(zoom, 2f, 40f);
    }

    public void SetFocus(Transform newTarget)
    {
        // Always cancel any accumulated free-pan so a focus request truly
        // re-centers on the target. Block focus reuses ONE shared anchor
        // (PlacementController.editFocusAnchor) that is just repositioned each
        // time, so comparing references here would skip the reset on every
        // focus after the first — the camera would then jump to
        // target + stalePan instead of centering, which feels like the camera
        // "flying off" / moving weirdly after a double-click focus.
        desiredTarget = newTarget;
        _panOffset    = Vector3.zero;
    }

    // Focus on a fixed world point (no scene Transform needed). Uses an internal
    // anchor and snaps currentFocusPoint so the very first frame is already
    // centred. Sets `target` too, so it survives whatever Start() order ran.
    Transform _focusAnchor;
    public void FocusOnPoint(Vector3 worldPoint, bool snap = true)
    {
        if (_focusAnchor == null) _focusAnchor = new GameObject("OrbitFocusAnchor").transform;
        _focusAnchor.position = worldPoint;
        target            = _focusAnchor;
        desiredTarget     = _focusAnchor;
        // snap=true → instant centre (initial framing). snap=false → leave
        // currentFocusPoint so LateUpdate glides both position and LookAt target.
        if (snap) currentFocusPoint = worldPoint;
        _panOffset        = Vector3.zero;
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

        Vector2 lookInput = GamepadInput.Look;   // right stick — orbits without a button hold

        if (!useOrthographic)
        {
            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * speed * Time.deltaTime;
                pitch -= Input.GetAxis("Mouse Y") * speed * Time.deltaTime;
                pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            }
            else if (lookInput.sqrMagnitude > 0.0001f)
            {
                yaw += lookInput.x * speed * gamepadLookScale * Time.deltaTime;
                pitch -= lookInput.y * speed * gamepadLookScale * Time.deltaTime;
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
            else if (lookInput.sqrMagnitude > 0.0001f)
            {
                yaw += lookInput.x * speed * 0.3f * gamepadLookScale * Time.deltaTime;
                pitch -= lookInput.y * speed * 0.2f * gamepadLookScale * Time.deltaTime;
                pitch = Mathf.Clamp(pitch, 10f, 80f);
            }
        }

        if (Mathf.Abs(GamepadInput.ZoomDelta) > 0.05f)
            AddDistance(-GamepadInput.ZoomDelta * gamepadZoomSpeed * Time.deltaTime);
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

        Vector3 shift  = ViewportBiasShift(rot);
        Vector3 offset = rot * new Vector3(0, 0, -distance);
        Vector3 desiredPos = currentFocusPoint + offset + shift;

        transform.position = Vector3.Lerp(
            transform.position,
            desiredPos,
            1f - Mathf.Exp(-transitionSpeed * Time.deltaTime)
        );

        // Smooth aim (eased rotation, not a per-frame snap). Looking at the SHIFTED
        // target keeps the focus point biased to `focusViewport` while staying smooth.
        transform.LookAt(currentFocusPoint + shift);
    }

    // World shift so the focus point lands at `focusViewport` instead of dead centre.
    Vector3 ViewportBiasShift(Quaternion rot)
    {
        if (myCam == null) return Vector3.zero;
        if (Mathf.Approximately(focusViewport.x, 0.5f) && Mathf.Approximately(focusViewport.y, 0.5f))
            return Vector3.zero;

        float halfH = myCam.orthographic
            ? orthoSize
            : distance * Mathf.Tan(myCam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfW = halfH * myCam.aspect;

        // Moving the camera right pushes the focus left, so use (0.5 - viewport).
        return rot * Vector3.right * ((0.5f - focusViewport.x) * 2f * halfW)
             + rot * Vector3.up    * ((0.5f - focusViewport.y) * 2f * halfH);
    }
}
