using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

// Builds + drives the title backdrop so an empty Title scene "just works":
//   • paper camera,
//   • a shader-cycling centre cube (TitleCubeShowcase) with a bold outline rim,
//   • constructivist "shards" that ASSEMBLE in on load, lean / parallax toward the
//     cursor, thump when the menu moves, SHATTER out on Play, and spawn bursts
//     when you click empty space.
// Pairs with TitleFlow (cube menu) + TitleBackdrop (full-screen shader).
[DisallowMultipleComponent]
public class TitleStage : MonoBehaviour
{
    [Header("Camera")]
    public Camera cam;                       // null → Camera.main
    public Color  background = GeoPalette.Paper;

    [Header("Centre cube")]
    public TitleCubeShowcase showcase;
    public List<Material> showcaseMaterials = new();
    public Material outlineMaterial;
    public Vector3 cubePosition = new Vector3(0f, 0.2f, 0f);   // centre; TitleFlow slides it left/right
    public float   cubeSize     = 3.2f;

    [Header("Constructivist shards")]
    public Material shardMaterial;
    public int     shardCount  = 11;
    public float   shardSpread = 9f;
    public float   shardDepth  = 6f;
    public Vector2 shardScale  = new Vector2(1.2f, 4.5f);
    public float   shardSpin   = 8f;
    public int     seed        = 7;

    [Header("Interaction")]
    public bool  mouseReactive = true;
    public float parallax   = 1.2f;
    public float tilt       = 12f;
    public float followLerp = 6f;

    [Header("Intro / shatter")]
    public float assembleTime = 0.9f;
    public float shatterTime  = 0.5f;

    public static TitleStage Instance;

    readonly List<Transform> _shards = new();
    readonly List<float>      _spin   = new();
    readonly List<Vector3>    _home   = new();
    readonly List<Vector3>    _dir    = new();

    enum Mode { Assemble, Idle, Shatter }
    Mode  _mode = Mode.Assemble;
    float _modeT;

    Vector3    _rigPos; Quaternion _rigRot; Vector3 _rigScale;
    Vector2    _mouse;
    float      _pulse;
    Material   _shardMat;

    void Awake() => Instance = this;

    void Start()
    {
        if (cam == null) cam = Camera.main;
        if (cam != null) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = background; }

        EnsureShowcase();
        BuildShards();

        _rigPos   = transform.localPosition;
        _rigRot   = transform.localRotation;
        _rigScale = transform.localScale;
    }

    void Update()
    {
        DriveRig();
        DriveShards();
        HandleBackgroundClick();
    }

    // ── Centre cube ─────────────────────────────────────────────────────────────

    void EnsureShowcase()
    {
        if (showcase == null)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "TitleCube";
            cube.transform.SetParent(transform, false);
            cube.transform.localPosition = cubePosition;
            cube.transform.localScale    = Vector3.one * cubeSize;
            showcase = cube.AddComponent<TitleCubeShowcase>();
        }

        if (showcaseMaterials != null && showcaseMaterials.Count > 0)
            showcase.materials = new List<Material>(showcaseMaterials);

        var rend = showcase.GetComponent<Renderer>();
        if (rend != null && showcase.materials != null && showcase.materials.Count > 0)
        {
            if (outlineMaterial != null)
                rend.sharedMaterials = new[] { showcase.materials[0], outlineMaterial };
            else
                rend.sharedMaterial = showcase.materials[0];
        }
    }

    // ── Shards ──────────────────────────────────────────────────────────────────

    void BuildShards()
    {
        _shardMat = shardMaterial != null ? shardMaterial : FallbackMat();
        var rng = new System.Random(seed);

        for (int i = 0; i < shardCount; i++)
        {
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "Shard_" + i;
            slab.transform.SetParent(transform, false);
            var col = slab.GetComponent<Collider>();
            if (col != null) Destroy(col);

            float x = Mathf.Lerp(-shardSpread, shardSpread, (float)rng.NextDouble());
            float y = Mathf.Lerp(-shardSpread * 0.55f, shardSpread * 0.55f, (float)rng.NextDouble());
            float z = Mathf.Lerp(2f, 2f + shardDepth, (float)rng.NextDouble());
            Vector3 home = new Vector3(x, y, z);

            float angle = Mathf.Round((float)rng.NextDouble() * 8f) * 15f;
            slab.transform.localRotation = Quaternion.Euler(0f, 0f, angle);

            float w = Mathf.Lerp(0.4f, shardScale.x, (float)rng.NextDouble());
            float h = Mathf.Lerp(shardScale.x, shardScale.y, (float)rng.NextDouble());
            slab.transform.localScale = new Vector3(w, h, 0.2f);

            var r = slab.GetComponent<Renderer>();
            r.sharedMaterial = _shardMat;
            MpbColor.Set(r, GeoPalette.Accents[i % GeoPalette.Accents.Length]);

            Vector3 dir = new Vector3(
                (float)rng.NextDouble() * 2f - 1f,
                (float)rng.NextDouble() * 2f - 1f,
                (float)rng.NextDouble() * 1.2f - 0.6f).normalized;

            // Start scattered (assembled in over the intro).
            slab.transform.localPosition = home + dir * shardSpread * 1.6f;

            _shards.Add(slab.transform);
            _spin.Add(Mathf.Lerp(-shardSpin, shardSpin, (float)rng.NextDouble()));
            _home.Add(home);
            _dir.Add(dir);
        }
    }

    void DriveShards()
    {
        if (_mode == Mode.Assemble)
        {
            _modeT += Time.deltaTime / Mathf.Max(0.01f, assembleTime);
            float f = EaseOutCubic(Mathf.Clamp01(_modeT));
            for (int i = 0; i < _shards.Count; i++)
                if (_shards[i] != null)
                    _shards[i].localPosition = Vector3.LerpUnclamped(
                        _home[i] + _dir[i] * shardSpread * 1.6f, _home[i], f);
            if (_modeT >= 1f) { _mode = Mode.Idle; _modeT = 0f; }
        }
        else if (_mode == Mode.Shatter)
        {
            _modeT += Time.deltaTime / Mathf.Max(0.01f, shatterTime);
            float f = EaseInCubic(Mathf.Clamp01(_modeT));
            for (int i = 0; i < _shards.Count; i++)
                if (_shards[i] != null)
                    _shards[i].localPosition = Vector3.LerpUnclamped(
                        _home[i], _home[i] + _dir[i] * shardSpread * 2.6f, f);
        }
        else
        {
            for (int i = 0; i < _shards.Count; i++)
                if (_shards[i] != null) _shards[i].localPosition = _home[i];
        }

        float boost = 1f + 2.5f * _pulse + (_mode == Mode.Shatter ? 4f : 0f);
        for (int i = 0; i < _shards.Count; i++)
            if (_shards[i] != null)
                _shards[i].Rotate(0f, 0f, _spin[i] * boost * Time.deltaTime, Space.Self);
    }

    // ── Interaction ─────────────────────────────────────────────────────────────

    void DriveRig()
    {
        if (mouseReactive)
        {
            Vector2 m = new Vector2(
                Input.mousePosition.x / Mathf.Max(1f, Screen.width),
                Input.mousePosition.y / Mathf.Max(1f, Screen.height)) * 2f - Vector2.one;
            m.x = Mathf.Clamp(m.x, -1f, 1f);
            m.y = Mathf.Clamp(m.y, -1f, 1f);
            _mouse = Vector2.Lerp(_mouse, m, 1f - Mathf.Exp(-followLerp * Time.deltaTime));

            transform.localPosition = _rigPos + new Vector3(_mouse.x, _mouse.y, 0f) * parallax;
            transform.localRotation = _rigRot * Quaternion.Euler(-_mouse.y * tilt, _mouse.x * tilt, 0f);
        }

        _pulse = Mathf.MoveTowards(_pulse, 0f, Time.deltaTime / 0.35f);
        transform.localScale = _rigScale * (1f + 0.05f * _pulse);
    }

    void HandleBackgroundClick()
    {
        if (cam == null || _mode == Mode.Shatter || SettingsScreen.Open) return;
        // Don't burst when the click is on a UGUI element (menu/buttons).
        if (!Input.GetMouseButtonDown(0)) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        var ray   = cam.ScreenPointToRay(Input.mousePosition);
        var plane = new Plane(-cam.transform.forward, transform.TransformPoint(cubePosition));
        if (plane.Raycast(ray, out float d)) Burst(ray.GetPoint(d));
    }

    // Reactive thump (menu navigation).
    public void Pulse() => _pulse = 1f;

    // Explode the whole composition outward (call before leaving the scene).
    public void Shatter() { _mode = Mode.Shatter; _modeT = 0f; }

    // Spray transient shards from a world point (click-to-shatter).
    public void Burst(Vector3 worldPos) => StartCoroutine(BurstCo(worldPos));

    IEnumerator BurstCo(Vector3 pos)
    {
        var mat = _shardMat != null ? _shardMat : FallbackMat();
        const int n = 7;
        var ts = new Transform[n]; var vel = new Vector3[n]; var spn = new float[n];

        for (int i = 0; i < n; i++)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var c = s.GetComponent<Collider>(); if (c != null) Destroy(c);
            s.transform.position    = pos;
            s.transform.localScale  = Vector3.one * Random.Range(0.25f, 0.6f);
            s.transform.rotation    = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            var r = s.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            MpbColor.Set(r, GeoPalette.Accents[i % GeoPalette.Accents.Length]);

            float a = (i / (float)n) * Mathf.PI * 2f + Random.Range(-0.3f, 0.3f);
            vel[i] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * Random.Range(3.5f, 7f);
            spn[i] = Random.Range(-360f, 360f);
            ts[i]  = s.transform;
        }

        float t = 0f;
        while (t < 0.6f)
        {
            t += Time.deltaTime;
            float k = 1f - t / 0.6f;
            for (int i = 0; i < n; i++)
            {
                if (ts[i] == null) continue;
                ts[i].position   += vel[i] * Time.deltaTime;
                ts[i].Rotate(0f, 0f, spn[i] * Time.deltaTime, Space.Self);
                ts[i].localScale  = Vector3.one * 0.6f * k;
            }
            yield return null;
        }
        for (int i = 0; i < n; i++) if (ts[i] != null) Destroy(ts[i].gameObject);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    static float EaseOutCubic(float x) { float p = 1f - x; return 1f - p * p * p; }
    static float EaseInCubic(float x)  => x * x * x;

    static Material FallbackMat()
    {
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        return new Material(sh);
    }
}
