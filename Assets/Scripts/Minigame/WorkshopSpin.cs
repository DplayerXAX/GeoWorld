using UnityEngine;

// Turns a scenery gear at a constant rate.
//
// Unscaled time, like everything else in a minigame overlay: the host scene's clock
// is not this stage's business, and a machine room that froze because the map behind
// it was paused would read as the game having hung.
public class WorkshopSpin : MonoBehaviour
{
    public float degreesPerSecond = 20f;

    void Update() => transform.Rotate(0f, 0f, degreesPerSecond * Time.unscaledDeltaTime, Space.Self);
}
