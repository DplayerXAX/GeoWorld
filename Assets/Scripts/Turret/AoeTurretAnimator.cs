using UnityEngine;

// AOE turret: its three parts move as one slow harmony —
//   * a wave runs round them: each rises and falls on the same beat, a third of a
//     cycle behind the one before;
//   * together they breathe — drift out from the middle and back in, in unison;
//   * each sways about its own vertical axis, neighbours opposite;
//   * and the trio as a whole turns slowly about its common centre.
// Every part keeps the same speed and amplitude; only the phase differs, which is
// what makes it read as one mechanism rather than three things jiggling.
//
// Attack (it lobs a blast): the three parts are flung apart from the middle and the
// whole trio drops, like a mortar taking its recoil, while its turn whips round —
// then they spring back into formation through a slight overshoot inward.
//
// Sizes are fractions of the model, so it looks the same at any fit scale.
public class AoeTurretAnimator : TurretAnimator
{
    [Tooltip("Radians per second of the shared beat.")]
    public float tempo = 1.3f;
    [Tooltip("Rise and fall of each part, as a fraction of the model's size.")]
    public float lift = 0.07f;
    [Tooltip("In-and-out drift from the middle, as a fraction of the model's size.")]
    public float breathe = 0.05f;
    [Tooltip("Degrees each part sways about its own vertical axis.")]
    public float sway = 14f;
    [Tooltip("Degrees per second the trio turns about its common centre.")]
    public float orbitSpeed = 20f;

    [Header("Attack")]
    [Tooltip("How far the parts are flung apart on a shot, as a fraction of the model's size.")]
    public float blast = 0.22f;
    [Tooltip("How far the whole trio drops on a shot (recoil), as a fraction of the model's size.")]
    public float dip = 0.12f;
    [Tooltip("Extra turn at the moment of the shot, degrees per second, fading out.")]
    public float orbitBurst = 540f;
    [Tooltip("How fast the kick dies away, and how fast it rings.")]
    public float kickDecay = 7f, kickFreq = 15f;

    float _orbit;        // integrated, so a burst leaves the trio further round

    Vector3[] _centre;   // the trio's centre, in each part's parent space
    Vector3[] _radial;   // unit direction from that centre out to each part (horizontal)

    protected override void Setup()
    {
        int n = Parts.Count;
        _centre = new Vector3[n];
        _radial = new Vector3[n];

        Vector3 c = Vector3.zero;
        foreach (var p in Parts) c += p.t.parent.TransformPoint(p.center);
        if (n > 0) c /= n;

        for (int i = 0; i < n; i++)
        {
            var p = Parts[i];
            _centre[i] = p.t.parent.InverseTransformPoint(c);
            var d = Vector3.ProjectOnPlane(p.center - _centre[i], p.up);
            _radial[i] = d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.zero;   // a part AT the centre only rises and sways
        }

        // Parts go round the centre in order, so the wave travels round instead of
        // hopping between them.
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        System.Array.Sort(order, (a, b) => Around(a).CompareTo(Around(b)));
        var parts = Parts.ToArray(); var cen = (Vector3[])_centre.Clone(); var rad = (Vector3[])_radial.Clone();
        for (int i = 0; i < n; i++) { Parts[i] = parts[order[i]]; _centre[i] = cen[order[i]]; _radial[i] = rad[order[i]]; }
    }

    float Around(int i)
    {
        var w = Parts[i].t.parent.TransformDirection(_radial[i]);
        return Mathf.Atan2(w.z, w.x);
    }

    protected override void Animate(float dt)
    {
        int n = Parts.Count;
        if (n == 0) return;

        float k     = Spring(FireAge, kickDecay, kickFreq);
        float w     = Clock * tempo;
        _orbit      = Mathf.Repeat(_orbit + (orbitSpeed + orbitBurst * Mathf.Exp(-kickDecay * FireAge)) * dt, 360f);
        float orbit = _orbit;
        float out_  = (Mathf.Sin(w * 0.5f) * breathe + blast * k) * Size;   // breath in unison, plus the blast
        float drop  = -dip * k * Size;

        for (int i = 0; i < n; i++)
        {
            var   p     = Parts[i];
            float phase = Mathf.PI * 2f * i / n;
            float up    = Mathf.Sin(w + phase) * lift * Size + drop;
            float twist = Mathf.Sin(w + phase + Mathf.PI * 0.5f) * sway * ((i & 1) == 0 ? 1f : -1f);

            // Own sway and the wave/breath first, then the whole lot turned about the
            // trio's centre — so the parts keep their formation while it revolves.
            Quaternion own = Quaternion.AngleAxis(twist, p.up);
            Vector3 offset = (p.up * up + _radial[i] * out_) * p.unit;

            Vector3    pos = p.center + own * (p.restPos - p.center) + offset;
            Quaternion rot = own * p.restRot;

            Quaternion turn = Quaternion.AngleAxis(orbit, p.up);
            p.t.localPosition = _centre[i] + turn * (pos - _centre[i]);
            p.t.localRotation = turn * rot;
        }
    }
}
