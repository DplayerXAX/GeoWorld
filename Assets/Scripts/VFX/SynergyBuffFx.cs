using UnityEngine;
using UnityEngine.Rendering;

// Lightweight, prefab-free feedback VFX for synergy BUFFS (distinct from the
// static structure decoration handled by SynergyVisualizer). Everything is built
// at runtime — a soft additive billboard glow:
//   • AttachAura  → a pulsing aura parented to a buffed target (turret / enemy).
//   • Mote        → a one-shot rising, fading glow (income drip / harvest).
public static class SynergyBuffFx
{
    static Material  _mat;
    static Mesh      _quad;

    static void Ensure()
    {
        if (_mat != null) return;

        _quad = BuildQuad();
        // Always-on-top additive glow (radial alpha computed in the shader), so the
        // aura reads as a halo even when the turret beacon / blocks occlude it.
        var sh = Shader.Find("GeoWorld/BuffGlow");
        _mat = new Material(sh != null ? sh : Shader.Find("Universal Render Pipeline/Unlit")) { name = "SynergyBuffFx" };
    }

    // A pulsing aura parented to `target`. Reuses the target's existing aura if it
    // already has one (so repeated calls just refresh colour/size).
    public static SynergyAura AttachAura(Transform target, Color color, float size)
    {
        if (target == null) return null;
        Ensure();

        var aura = target.GetComponentInChildren<SynergyAura>();
        if (aura == null)
        {
            var go = NewGlow("SynergyAura", target.gameObject.layer);
            go.transform.SetParent(target, false);
            go.transform.localPosition = Vector3.zero;
            aura = go.AddComponent<SynergyAura>();
            aura.Bind(go.GetComponent<Renderer>());
        }
        aura.Set(color, size);
        return aura;
    }

    // One-shot rising/fading glow at a world position (currency drip / harvest).
    public static void Mote(Vector3 worldPos, Color color, float size)
    {
        Ensure();
        var go = NewGlow("SynergyMote", 0);
        go.transform.position = worldPos;
        var mote = go.AddComponent<SynergyMote>();
        mote.Init(go.GetComponent<Renderer>(), color, size);
    }

    static GameObject NewGlow(string name, int layer)
    {
        var go = new GameObject(name) { layer = layer };
        go.AddComponent<MeshFilter>().sharedMesh = _quad;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial    = _mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows    = false;
        return go;
    }

    static Mesh BuildQuad()
    {
        var m = new Mesh { name = "SynergyQuad" };
        m.vertices  = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
        };
        m.uv        = new[] { new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1) };
        m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        m.RecalculateBounds();
        return m;
    }

}

// Pulsing billboard aura on a buffed target. Persistent until removed, or set a
// lifetime via Refresh() so it auto-expires (used for the auto-refreshing slow).
public class SynergyAura : MonoBehaviour
{
    Renderer _r;
    MaterialPropertyBlock _mpb;
    Color _color = Color.white;
    float _size  = 1f;
    float _phase;
    float _expire = -1f;        // <0 = persist
    Transform _cam;

    public void Bind(Renderer r) { _r = r; _mpb = new MaterialPropertyBlock(); _phase = Random.value * 6.2832f; }

    public void Set(Color color, float size)
    {
        _color = color; _size = size;
        transform.localScale = Vector3.one * size;
    }

    // Keep the aura alive for `duration` more seconds (auto-expires after).
    public void Refresh(float duration) => _expire = Time.time + Mathf.Max(0f, duration);
    public void Persist() => _expire = -1f;
    public void Remove()  { if (this != null) Destroy(gameObject); }

    void LateUpdate()
    {
        if (_cam == null) { var c = Camera.main; if (c != null) _cam = c.transform; }
        if (_cam != null)
            transform.rotation = Quaternion.LookRotation(transform.position - _cam.position, Vector3.up);

        float a = 0.55f * (0.6f + 0.4f * Mathf.Sin(Time.time * 3f + _phase));
        if (_r != null)
        {
            _r.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", new Color(_color.r, _color.g, _color.b, a));
            _r.SetPropertyBlock(_mpb);
        }

        if (_expire > 0f && Time.time >= _expire) Destroy(gameObject);
    }
}

// One-shot glow that rises and fades, then self-destructs.
public class SynergyMote : MonoBehaviour
{
    Renderer _r;
    MaterialPropertyBlock _mpb;
    Color _color;
    float _t;
    const float Life = 1.1f;
    const float Rise = 1.6f;

    public void Init(Renderer r, Color color, float size)
    {
        _r = r; _color = color; _mpb = new MaterialPropertyBlock();
        transform.localScale = Vector3.one * size;
    }

    void Update()
    {
        _t += Time.deltaTime;
        transform.position += Vector3.up * (Rise * Time.deltaTime);

        var cam = Camera.main;
        if (cam != null)
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, Vector3.up);

        float k = Mathf.Clamp01(_t / Life);
        float a = (1f - k) * 0.9f;
        if (_r != null)
        {
            _r.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", new Color(_color.r, _color.g, _color.b, a));
            _r.SetPropertyBlock(_mpb);
        }

        if (_t >= Life) Destroy(gameObject);
    }
}
