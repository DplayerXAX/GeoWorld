using UnityEngine;

// Animated through-line cube. Slides between centre / left (menu) / right (save)
// and scales up to "zoom into the left half". It ALWAYS spins slowly (every state),
// and the menu carousel feeds it an extra spin kick via AddScroll when you scroll.
public class TitleCube : MonoBehaviour
{
    [Header("Positions (world)")]
    public Vector3 centerPosition;
    public Vector3 menuPosition = new Vector3(-3f, -2f, 0f);   // LEFT; push .z toward camera for more zoom
    public Vector3 savePosition = new Vector3( 3f, -2f, 0f);   // RIGHT; save panel sits on the left

    [Header("Scale (the zoom)")]
    public float centerScale = 1f;
    public float menuScale   = 2.2f;
    public float saveScale   = 2.2f;

    [Header("Motion")]
    public float   moveSpeed  = 6f;
    public float   scaleSpeed = 7f;
    public Vector3 idleSpin   = new Vector3(6f, 14f, 0f);   // deg/sec — ALWAYS spinning (slow)
    public float   punchScale = 0.14f;
    public float   punchTime  = 0.3f;

    [Header("Scroll → extra spin")]
    public float   scrollKick  = 90f;                  // deg/sec spike per menu scroll step
    public float   scrollDecay = 4f;                   // how fast the extra spin settles back to idle
    [Tooltip("Axis the scroll spins the cube around. (1,0,0) = up/down tumble (matches a vertical menu). Flip sign to reverse.")]
    public Vector3 scrollAxis  = new Vector3(1f, 0f, 0f);

    Vector3 targetPos;
    float   targetScale, baseScale, punch, scrollSpin;

    void Start()
    {
        //centerPosition = transform.position;
        baseScale      = transform.localScale.x;
        targetPos      = centerPosition;
        targetScale    = centerScale;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        transform.position = Vector3.Lerp(transform.position, targetPos, 1f - Mathf.Exp(-moveSpeed * dt));

        baseScale = Mathf.Lerp(baseScale, targetScale, 1f - Mathf.Exp(-scaleSpeed * dt));
        punch     = Mathf.MoveTowards(punch, 0f, dt / Mathf.Max(0.05f, punchTime));
        transform.localScale = Vector3.one * baseScale * (1f + punch * punchScale);

        // Always rotating: slow idle spin + a decaying extra spin from scrolling.
        transform.Rotate(idleSpin * dt, Space.World);
        transform.Rotate(scrollAxis.normalized * (scrollSpin * dt), Space.World);
        scrollSpin = Mathf.Lerp(scrollSpin, 0f, 1f - Mathf.Exp(-scrollDecay * dt));
    }

    public void MoveToCenter()   { targetPos = centerPosition; targetScale = centerScale; punch = 1f; }
    public void MoveToMenuFace() { targetPos = menuPosition;   targetScale = menuScale;   punch = 1f; }
    public void MoveToSaveFace() { targetPos = savePosition;   targetScale = saveScale;   punch = 1f; }

    // Called by the carousel each scroll step (dir = +1 / -1) for an extra spin kick.
    public void AddScroll(int dir) { scrollSpin += dir * scrollKick; }
}
