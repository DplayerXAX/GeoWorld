using UnityEngine;

// A camera-relative, pickable supply model. It never occupies a grid cell or
// participates in physics/line-of-sight until committed through PlacementController.
public class RainBlockPickup : MonoBehaviour
{
    public BlockData Data { get; private set; }
    public BlockColor Color { get; private set; }
    public Color Tint => Color != BlockColor.None ? BlockColorPalette.Get(Color) : GeoPalette.Paper;
    public bool Held { get; private set; }
    ChapterEnvironmentController _owner;
    Transform _model;
    float _x, _age, _lifetime, _extent;
    Vector2 _screen;
    bool _visible;

    public static RainBlockPickup Create(ChapterEnvironmentController owner, BlockData data,
                                        BlockColor color, float x, float lifetime)
    {
        var go = new GameObject("Rain supply - " + data.name);
        go.transform.SetParent(owner.transform, false);
        var pickup = go.AddComponent<RainBlockPickup>();
        pickup._owner = owner; pickup.Data = data; pickup.Color = color;
        pickup._x = x; pickup._lifetime = Mathf.Max(1f, lifetime);
        pickup.BuildModel();
        pickup.Position();
        return pickup;
    }

    void BuildModel()
    {
        _model = new GameObject("Model").transform;
        _model.SetParent(transform, false);
        Vector3 min = Data.cells[0], max = min;
        foreach (var cell in Data.cells) { min = Vector3.Min(min, cell); max = Vector3.Max(max, cell); }
        Vector3 center = (min + max) * 0.5f;
        _extent = Mathf.Max(1f, (max - min).magnitude + 1f);
        foreach (var cell in Data.cells)
        {
            var prefab = PlacementController.Instance?.cubePrefab;
            var cube = prefab != null ? Instantiate(prefab, _model) : GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(_model, false);
            cube.transform.localPosition = (Vector3)cell - center;
            cube.transform.localScale = Vector3.one * 0.9f;
            foreach (var collider in cube.GetComponentsInChildren<Collider>()) collider.enabled = false;
            foreach (var renderer in cube.GetComponentsInChildren<Renderer>())
            {
                MpbColor.Set(renderer, Tint);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }
    }

    public void SetHeld(bool held)
    {
        Held = held;
        if (_model != null) _model.gameObject.SetActive(!held);
    }

    public bool Hit(Vector2 screen) => !Held && _visible && (screen - _screen).sqrMagnitude <= Mathf.Pow(48f * Screen.height / 1080f, 2);

    void Update()
    {
        if (_owner == null || !_owner.InCombat) { Destroy(gameObject); return; }
        if (Held) return;
        _age += Time.deltaTime;
        if (_age >= _lifetime) { Destroy(gameObject); return; }
    }

    void LateUpdate() { if (!Held) Position(); }

    void Position()
    {
        var camera = PlacementController.Instance?.cam?.myCam ?? Camera.main;
        if (camera == null || _model == null) { _visible = false; return; }
        float depth = Mathf.Max(camera.nearClipPlane + 2f, 6f);
        float y = Mathf.Lerp(0.82f, 0.22f, Mathf.Clamp01(_age / _lifetime));
        transform.position = camera.ViewportToWorldPoint(new Vector3(_x, y, depth));
        transform.rotation = camera.transform.rotation * Quaternion.Euler(20f, _age * 14f + 25f, 8f);
        float height = camera.orthographic ? camera.orthographicSize * 2f : 2f * depth * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
        transform.localScale = Vector3.one * (height * 0.065f / _extent);
        _screen = camera.WorldToScreenPoint(transform.position);
        _visible = true;
    }
}
