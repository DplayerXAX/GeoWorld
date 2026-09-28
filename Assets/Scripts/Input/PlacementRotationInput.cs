using UnityEngine;

// Shared by gameplay, batch moves and map building. Returns grid-aligned turns;
// each controller keeps its existing rotation, preview and placement logic.
public sealed class PlacementRotationInput
{
    const float DragPixelsPerStep = 60f;

    public bool Active { get; private set; }
    Vector2 _drag;
    Vector3 _right, _forward;
    float _wheel;

    public Quaternion Read(bool rotating, Vector2 mouseDelta, float scroll, Vector3 cameraRight,
                           bool canRotate = true)
    {
        if (!rotating)
        {
            Reset();
            return Quaternion.identity;
        }

        if (!Active)
        {
            Active = true;
            mouseDelta = Vector2.zero;   // ignore the cursor-lock transition
            _right = Mathf.Abs(cameraRight.x) >= Mathf.Abs(cameraRight.z)
                ? Vector3.right * Mathf.Sign(cameraRight.x)
                : Vector3.forward * Mathf.Sign(cameraRight.z);
            // Derive the second axis so a diagonal camera can never choose the
            // same grid axis for both gestures. Freeze both until Alt is released.
            _forward = Vector3.Cross(_right, Vector3.up);
        }

        _drag += mouseDelta;

        // Drop input while the previous turn finishes; never queue more turns.
        if (!canRotate)
        {
            _drag = Vector2.zero;
            _wheel = 0f;
            return Quaternion.identity;
        }

        if (scroll != 0f)
        {
            _drag = Vector2.zero;
            _wheel += scroll;
            if (Mathf.Abs(_wheel) < 1f) return Quaternion.identity;
            float direction = Mathf.Sign(_wheel);
            _wheel = 0f;
            return Quaternion.AngleAxis(90f * direction, Vector3.up);
        }

        bool horizontal = Mathf.Abs(_drag.x) >= Mathf.Abs(_drag.y);
        float distance = horizontal ? _drag.x : _drag.y;
        if (Mathf.Abs(distance) < DragPixelsPerStep) return Quaternion.identity;

        _drag = Vector2.zero;
        return Quaternion.AngleAxis(90f * Mathf.Sign(distance), horizontal ? -_forward : _right);
    }

    public void Reset()
    {
        Active = false;
        _drag = Vector2.zero;
        _wheel = 0f;
    }
}
