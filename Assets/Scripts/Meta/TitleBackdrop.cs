using UnityEngine;

// Full-screen constructivist shader behind the title. Spawns a camera-parented
// quad sized to fill the view and feeds it the cursor each frame so the geometric
// field tilts / parallaxes with the mouse.
[DisallowMultipleComponent]
public class TitleBackdrop : MonoBehaviour
{
    public Camera   cam;                 // null → Camera.main
    [Tooltip("GeoWorld/TitleConstructivist material. Null → created from the shader.")]
    public Material material;
    [Tooltip("How far in front of the camera the backdrop sits (behind the cube).")]
    public float distance = 30f;
    public float followLerp = 8f;

    Material  _mat;
    Transform _quad;
    Vector2   _mouse = new Vector2(0.5f, 0.5f);
    static readonly int MouseID = Shader.PropertyToID("_Mouse");

    void Start()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        _mat = material != null ? material
             : new Material(Shader.Find("GeoWorld/TitleConstructivist"));

        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = "TitleBackdrop";
        var col = q.GetComponent<Collider>();
        if (col != null) Destroy(col);
        q.GetComponent<Renderer>().sharedMaterial = _mat;

        _quad = q.transform;
        _quad.SetParent(cam.transform, false);
        _quad.localPosition = new Vector3(0f, 0f, distance);
        _quad.localRotation = Quaternion.identity;
        Fit();
    }

    void Update()
    {
        if (_mat == null) return;

        Vector2 m = new Vector2(
            Input.mousePosition.x / Mathf.Max(1f, Screen.width),
            Input.mousePosition.y / Mathf.Max(1f, Screen.height));
        _mouse = Vector2.Lerp(_mouse, m, 1f - Mathf.Exp(-followLerp * Time.deltaTime));
        _mat.SetVector(MouseID, new Vector4(_mouse.x, _mouse.y, 0f, 0f));

        Fit();
    }

    void Fit()
    {
        if (_quad == null || cam == null) return;
        float h = cam.orthographic
            ? cam.orthographicSize * 2f
            : 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        _quad.localScale = new Vector3(h * cam.aspect, h, 1f);
    }
}
