using UnityEngine;

// Puts a MapInteractable on a specific column of the LevelSelect map. Drop this
// on a GameObject in the scene (with whatever visual you want as its child — an
// NPC model, a marker cube) and set `cell`.
//
// Keyed by COLUMN (x,z), not the exact cell: the pawn always lands on a column's
// top-exposed cell, which moves whenever the map is rebuilt or the player stacks
// a block there. Matching on the column means the spot keeps working instead of
// silently detaching.
[DisallowMultipleComponent]
public class MapInteractableSpot : MonoBehaviour
{
    public MapInteractable data;

    [Tooltip("Map column this stands on. Y is ignored — the spot binds to whatever cell is on top.")]
    public Vector3Int cell;

    [Tooltip("Snap this object onto the column's surface at Start. Off = keep the transform you placed by hand.")]
    public bool snapToSurface = true;

    [Tooltip("Extra height above the block's top face, in world units.")]
    public float surfaceLift = 0f;

    [Header("Idle motion")]
    public float bobAmplitude = 0.08f;
    public float bobSpeed = 1.6f;
    public float spinDegreesPerSecond = 0f;

    public Vector2Int Column => new(cell.x, cell.z);

    Vector3 _restPos;
    bool    _placed;

    // Called by LevelMapController once the surface exists (it owns the grid, and
    // Start order between it and this component isn't guaranteed).
    public void PlaceOn(Vector3 surfaceTop)
    {
        _restPos = surfaceTop + Vector3.up * surfaceLift;
        if (snapToSurface) transform.position = _restPos;
        else               _restPos = transform.position;
        _placed = true;
    }

    void Update()
    {
        if (!_placed) return;
        if (bobAmplitude > 0f)
            transform.position = _restPos + Vector3.up * (Mathf.Sin(Time.time * bobSpeed) * bobAmplitude);
        if (spinDegreesPerSecond != 0f)
            transform.Rotate(0f, spinDegreesPerSecond * Time.deltaTime, 0f, Space.World);
    }
}
