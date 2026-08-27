using UnityEngine;

// The two turret shots that aren't a flying bullet.
//
//   Slow — a beam, not a projectile. Its whole job is to say "that one is held",
//          and a travelling pellet says the opposite: by the time it lands the
//          player has stopped watching. A line drawn straight to the target reads
//          as contact, instantly.
//   AOE  — the blast the energy ball turns into on impact. Drawn at the turret's
//          actual aoeRadius, so the splash the player SEES is the splash that
//          damaged things — the number in the balance table stops being invisible.
//
// Both build their own geometry and material at runtime, like EnemyDeathFx.
public static class TurretShotFx
{
    static readonly Color IceColor   = new(0.55f, 0.85f, 1.00f);
    static readonly Color BlastColor = new(1.00f, 0.55f, 0.20f);

    // ── Slow: ice beam ───────────────────────────────────────────────────────

    const float BeamLife  = 0.16f;   // long enough to register, short enough not to smear
    const float BeamWidth = 0.10f;

    public static void Beam(Vector3 from, Vector3 to)
    {
        var go = new GameObject("SlowBeam");
        go.transform.position = from;

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace     = true;
        lr.positionCount     = 2;
        lr.SetPosition(0, from);
        lr.SetPosition(1, to);
        // Tapered: wide at the muzzle, needle-thin at the target, so it reads as
        // fired FROM the turret rather than as a static rod joining two points.
        lr.startWidth        = BeamWidth;
        lr.endWidth          = BeamWidth * 0.35f;
        lr.numCapVertices    = 4;
        lr.sharedMaterial    = Mat();
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows    = false;

        go.AddComponent<Fade>().Init(lr, BeamLife);

        // A knot of frost where it lands — the beam alone reads as a line drawn
        // past the enemy rather than as something that hit it.
        Blast(to, 0.35f, IceColor, 0.22f);
    }

    // ── AOE: impact bloom ────────────────────────────────────────────────────

    public static void Blast(Vector3 center, float radius) => Blast(center, radius, BlastColor, 0.34f);

    public static void Blast(Vector3 center, float radius, Color color, float life)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Blast";
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.position   = center;
        go.transform.localScale = Vector3.one * 0.05f;

        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial    = Mat();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows    = false;

        go.AddComponent<Expand>().Init(mr, radius * 2f, life, color);
    }

    // ── Shared material ──────────────────────────────────────────────────────

    static Material _mat;

    static Material Mat()
    {
        if (_mat != null) return _mat;
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        _mat = new Material(sh) { name = "TurretShotFx" };
        if (_mat.HasProperty("_Surface"))
        {
            _mat.SetFloat("_Surface", 1f);
            _mat.SetFloat("_ZWrite", 0f);
            _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);   // additive — these are light, not paint
            _mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        return _mat;
    }

    // ── Behaviours ───────────────────────────────────────────────────────────

    class Fade : MonoBehaviour
    {
        LineRenderer _lr;
        float _life, _t;

        public void Init(LineRenderer lr, float life)
        {
            _lr = lr; _life = Mathf.Max(0.01f, life);
            MpbColor.Set(lr, IceColor);
        }

        void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / _life);
            if (k >= 1f) { Destroy(gameObject); return; }

            var c = IceColor;
            MpbColor.Set(_lr, new Color(c.r, c.g, c.b, 1f - k));
            // Thins as it fades, so it retracts rather than just dimming in place.
            _lr.startWidth = BeamWidth * (1f - k * 0.6f);
            _lr.endWidth   = BeamWidth * 0.35f * (1f - k);
        }
    }

    class Expand : MonoBehaviour
    {
        MeshRenderer _mr;
        Color _color;
        float _target, _life, _t;

        public void Init(MeshRenderer mr, float targetDiameter, float life, Color color)
        {
            _mr = mr; _target = targetDiameter; _life = Mathf.Max(0.01f, life); _color = color;
        }

        void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / _life);
            if (k >= 1f) { Destroy(gameObject); return; }

            // Fast open, slow settle — a linear expansion reads as a growing ball,
            // not as something that detonated.
            float e = 1f - (1f - k) * (1f - k);
            transform.localScale = Vector3.one * Mathf.Lerp(0.05f, _target, e);
            MpbColor.Set(_mr, new Color(_color.r, _color.g, _color.b, (1f - k) * 0.55f));
        }
    }
}
