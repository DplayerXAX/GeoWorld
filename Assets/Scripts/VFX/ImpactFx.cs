using System.Collections.Generic;
using UnityEngine;

// The small deformations that make a hit feel like a hit.
//
// Everything here is SHAPE AND TIME — squash, press, ripple — and nothing here adds
// an object to the scene that wasn't already there, except one flat ring. That is
// deliberate: this game's art is flat, hard-edged and restrained, and a particle
// burst on every event would fight it. Juice made of timing reads as weight; juice
// made of sprites reads as clutter.
//
// One component per animated transform, self-destructing when it's done, so nothing
// needs a manager and nothing keeps ticking once the motion is over.
public static class ImpactFx
{
    // ── Hit squash ───────────────────────────────────────────────────────────

    /// <summary>
    /// A struck thing flinches: squashed toward its own up axis, then snapping back.
    /// Re-hitting restarts it rather than stacking, so a stream of fast shots reads
    /// as continuous pressure instead of a vibrating blur.
    /// </summary>
    public static void Hit(Transform t, float squash = 0.85f, float seconds = 0.08f)
    {
        if (t == null) return;
        var s = t.GetComponent<SquashPulse>();
        if (s == null) s = t.gameObject.AddComponent<SquashPulse>();
        s.Begin(squash, seconds);
    }

    /// <summary>Nudge a transform off its rest position and let it spring back.</summary>
    public static void Shove(Transform t, Vector3 worldDir, float distance = 0.12f,
                             float seconds = 0.12f)
    {
        if (t == null || worldDir.sqrMagnitude < 1e-6f) return;
        var s = t.GetComponent<ShovePulse>();
        if (s == null) s = t.gameObject.AddComponent<ShovePulse>();
        s.Begin(worldDir.normalized * distance, seconds);
    }

    // ── Landing impact ───────────────────────────────────────────────────────

    /// <summary>
    /// Everything within `radius` of `worldPos` gets pressed down and springs back,
    /// weakening with distance. The block that landed is excluded — it has its own
    /// grow-in, and pressing it too would fight that.
    /// </summary>
    public static void Land(Vector3 worldPos, float radius, GameObject exclude = null)
    {
        var grid = GridSystem.instance;
        if (grid == null) return;

        float r2 = radius * radius;
        foreach (var ins in grid.GetAllInstances())
        {
            var vis = ins?.visualObject;
            if (vis == null || vis == exclude) continue;

            float d2 = (vis.transform.position - worldPos).sqrMagnitude;
            if (d2 > r2) continue;

            // Linear falloff. The point is a wave passing through the neighbours, so
            // what matters is that near ones move MORE than far ones — the exact
            // curve is invisible at 0.15s.
            float k = 1f - Mathf.Sqrt(d2) / radius;

            var p = vis.GetComponent<PressPulse>();
            if (p == null) p = vis.AddComponent<PressPulse>();
            p.Begin(k);
        }
    }

    // ── Ground ripple ────────────────────────────────────────────────────────

    /// <summary>A single flat ring that expands and fades. One object, one frame of work.</summary>
    public static void Ripple(Vector3 worldPos, Color color, float maxRadius = 2.6f,
                              float seconds = 0.42f)
    {
        var go = new GameObject("Ripple");
        go.transform.position = worldPos;
        go.AddComponent<RippleRing>().Begin(color, maxRadius, seconds);
    }
}

// ── Components ───────────────────────────────────────────────────────────────

// Squash along local Y, stretch across it — volume-preserving-ish, which is what
// separates a flinch from a thing simply getting smaller.
public class SquashPulse : MonoBehaviour
{
    Vector3 _rest;
    float   _t, _dur, _amount;
    bool    _running;

    public void Begin(float squash, float seconds)
    {
        if (!_running) _rest = transform.localScale;   // capture only when idle, never mid-pulse
        _amount  = Mathf.Clamp(1f - squash, 0f, 0.9f);
        _dur     = Mathf.Max(0.01f, seconds);
        _t       = 0f;
        _running = true;
    }

    void LateUpdate()
    {
        if (!_running) return;

        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / _dur);

        // One half-sine: out and back with no discontinuity at either end. A linear
        // there-and-back has a corner at the peak that reads as a stutter.
        float e = Mathf.Sin(k * Mathf.PI) * _amount;
        transform.localScale = new Vector3(_rest.x * (1f + e * 0.6f),
                                           _rest.y * (1f - e),
                                           _rest.z * (1f + e * 0.6f));

        if (k < 1f) return;
        transform.localScale = _rest;
        _running = false;
        Destroy(this);
    }
}

// Offset from rest and spring back. Position only — the thing being shoved is
// usually mid-path, and moving its actual transform target would desync it from
// wherever the pathing thinks it is.
public class ShovePulse : MonoBehaviour
{
    Vector3 _offset;
    float   _t, _dur;
    Vector3 _applied;

    public void Begin(Vector3 offset, float seconds)
    {
        transform.position -= _applied;   // undo whatever the last pulse still had out
        _applied = Vector3.zero;
        _offset  = offset;
        _dur     = Mathf.Max(0.01f, seconds);
        _t       = 0f;
    }

    void LateUpdate()
    {
        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / _dur);

        transform.position -= _applied;
        _applied = _offset * Mathf.Sin(k * Mathf.PI);
        transform.position += _applied;

        if (k < 1f) return;
        transform.position -= _applied;
        Destroy(this);
    }
}

// A block dipping as the shock passes through it.
public class PressPulse : MonoBehaviour
{
    const float Depth   = 0.22f;   // cells, at full strength
    const float Seconds = 0.15f;

    Vector3 _rest;
    float   _t, _k;
    bool    _running;

    public void Begin(float strength)
    {
        if (!_running) _rest = transform.localPosition;
        _k       = Mathf.Max(_k * (1f - Mathf.Clamp01(_t / Seconds)), Mathf.Clamp01(strength));
        _t       = 0f;
        _running = true;
    }

    void LateUpdate()
    {
        if (!_running) return;

        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / Seconds);

        // Down fast, back slower — a dip that returns as fast as it left reads as a
        // vibration; weighting the return is what makes it read as recoil.
        float e = k < 0.35f ? k / 0.35f : 1f - (k - 0.35f) / 0.65f;
        transform.localPosition = _rest + Vector3.down * (Depth * _k * e);

        if (k < 1f) return;
        transform.localPosition = _rest;
        _running = false;
        Destroy(this);
    }
}

// An expanding flat ring, drawn with a LineRenderer so it needs no mesh and no
// texture. Lies just above the impact so it never z-fights the block it came from.
public class RippleRing : MonoBehaviour
{
    const int Segments = 40;

    LineRenderer _line;
    float _t, _dur, _max;
    Color _color;

    static Material _mat;

    static Material RingMat()
    {
        if (_mat != null) return _mat;
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        _mat = new Material(sh) { name = "ImpactRipple" };
        if (_mat.HasProperty("_Surface"))
        {
            _mat.SetFloat("_Surface", 1f);
            _mat.SetFloat("_ZWrite", 0f);
            _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);   // additive
            _mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
        _mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return _mat;
    }

    public void Begin(Color color, float maxRadius, float seconds)
    {
        _color = color;
        _max   = maxRadius;
        _dur   = Mathf.Max(0.05f, seconds);

        _line = gameObject.AddComponent<LineRenderer>();
        _line.useWorldSpace   = false;
        _line.loop            = true;
        _line.positionCount   = Segments;
        _line.material        = RingMat();
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _line.receiveShadows    = false;
    }

    void LateUpdate()
    {
        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / _dur);

        // Radius eases OUT and alpha falls off fast: the ring should have most of its
        // travel in the first few frames, which is where the eye is still looking.
        float r = _max * (1f - (1f - k) * (1f - k));
        for (int i = 0; i < Segments; i++)
        {
            float a = i / (float)Segments * Mathf.PI * 2f;
            _line.SetPosition(i, new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
        }

        var c = _color;
        c.a = (1f - k) * (1f - k);
        _line.startColor = _line.endColor = c;
        _line.widthMultiplier = Mathf.Lerp(0.14f, 0.03f, k);

        if (k >= 1f) Destroy(gameObject);
    }
}
