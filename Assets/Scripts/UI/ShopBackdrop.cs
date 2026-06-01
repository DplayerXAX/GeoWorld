using UnityEngine;

// Spawns a procedurally-tiled backdrop quad behind the shop items.
// Gives the rift interior an "other dimension" feel — a moving
// tessellation pattern visible through the rift opening.
//
// Setup: attach this component to the ShopController GameObject (or any
// persistent GO). It auto-locates ShopController.Instance and positions
// the backdrop on the far side of shopCenter.
public class ShopBackdrop : MonoBehaviour
{
    [Header("Enabled")]
    [Tooltip("Turn off entirely for a clean solid-colour shop interior. Named `backdropEnabled` to avoid shadowing MonoBehaviour.enabled.")]
    public bool   backdropEnabled = false;

    [Header("Placement")]
    [Tooltip("How far behind shopCenter (from camera POV) the backdrop sits.")]
    public float backDistance = 9f;
    [Tooltip("Quad scale in world units (X, Y).")]
    public Vector2 quadSize   = new Vector2(28f, 18f);

    [Header("Palette")]
    public Color paletteA = new Color(0.60f, 0.30f, 0.80f, 1f);
    public Color paletteB = new Color(0.92f, 0.45f, 0.20f, 1f);
    public Color paletteC = new Color(0.78f, 0.74f, 0.92f, 1f);
    public Color paletteD = new Color(0.30f, 0.20f, 0.55f, 1f);

    [Header("Pattern")]
    [Range(2f, 40f)] public float tileSize    = 14f;
    [Range(0f, 0.3f)] public float scrollSpeed = 0.035f;
    [Range(0f, 2f)]   public float breathSpeed = 0.45f;
    [Range(0f, 1f)]   public float edgeDimming = 0.35f;

    GameObject _quad;
    Material   _mat;
    Renderer   _rend;
    ShopController _shop;

    void Start()
    {
        if (!backdropEnabled) return;

        _shop = ShopController.Instance;
        if (_shop == null) { Debug.LogWarning("[ShopBackdrop] ShopController.Instance is null."); return; }

        var sh = Shader.Find("GeoWorld/ShopTessellation");
        if (sh == null) { Debug.LogError("[ShopBackdrop] Shader 'GeoWorld/ShopTessellation' not found."); return; }

        _mat = new Material(sh) { name = "ShopBackdrop (runtime)" };
        SyncMaterial();

        _quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _quad.name = "ShopBackdrop";
        // Quads ship with a collider — strip it so the player can't accidentally click it.
        var col = _quad.GetComponent<Collider>();
        if (col != null) Destroy(col);

        _rend = _quad.GetComponent<Renderer>();
        _rend.sharedMaterial = _mat;
        _rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _rend.receiveShadows    = false;

        PositionBackdrop();
    }

    void LateUpdate()
    {
        // Cheap: re-position if shop camera moved (it lerps its offset between
        // small/large). Backdrop stays glued to the far wall of the shop view.
        PositionBackdrop();
        SyncMaterial();
    }

    void PositionBackdrop()
    {
        if (_shop == null || _quad == null) return;

        Vector3 center = _shop.shopCenter;
        Vector3 fwd    = (_shop.shopCam != null)
                         ? _shop.shopCam.transform.forward
                         : Vector3.forward;

        _quad.transform.position   = center + fwd * backDistance;
        _quad.transform.rotation   = Quaternion.LookRotation(fwd);  // face away from camera
        _quad.transform.localScale = new Vector3(quadSize.x, quadSize.y, 1f);
    }

    void SyncMaterial()
    {
        if (_mat == null) return;
        _mat.SetColor("_ColorA", paletteA);
        _mat.SetColor("_ColorB", paletteB);
        _mat.SetColor("_ColorC", paletteC);
        _mat.SetColor("_ColorD", paletteD);
        _mat.SetFloat("_TileSize",    tileSize);
        _mat.SetFloat("_ScrollSpeed", scrollSpeed);
        _mat.SetFloat("_BreathSpeed", breathSpeed);
        _mat.SetFloat("_Dimming",     edgeDimming);
    }

    void OnDestroy()
    {
        if (_quad != null) Destroy(_quad);
        if (_mat  != null) Destroy(_mat);
    }
}
