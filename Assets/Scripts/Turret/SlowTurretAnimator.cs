using UnityEngine;

// Slow turret: its sub-meshes turn about the vertical CONTINUOUSLY, bottom piece to
// top in sequence. The model already sets each piece a little further round than
// the one below; every piece is posed relative to that authored rest, so the twist
// is kept, and it all turns together at a steady speed — with a wave running up the
// stack on top: each piece swings ahead and back a beat after the one beneath it,
// so the turning reads as travelling up the pieces rather than one rigid spin.
//
// Attack (it fires a beam): the stack clenches — every piece is pulled in toward
// the middle of the stack — and a burst of spin races up it, bottom to top, a beat
// apart, as if winding the beam out of the top; then it all springs back open.
public class SlowTurretAnimator : TurretAnimator
{
    [Tooltip("Degrees per second every piece turns — the steady part.")]
    public float spinSpeed = 60f;
    [Tooltip("How far each piece swings ahead of / behind the steady spin, in degrees. 0 = one rigid spin.")]
    public float waveDegrees = 25f;
    [Tooltip("Radians per second of that swing.")]
    public float waveSpeed = 2.2f;
    [Tooltip("How far behind the piece below each piece's swing runs, in radians — what makes the wave travel up the stack.")]
    public float waveLag = 0.7f;
    [Tooltip("Neighbouring pieces spin opposite ways. Off = the whole stack turns the same way.")]
    public bool alternate = false;

    [Header("Attack")]
    [Tooltip("How far each piece is pulled toward the middle of the stack on a shot (0.35 = 35% of the way).")]
    [Range(0f, 0.8f)] public float clench = 0.35f;
    [Tooltip("Extra spin at the moment a piece's burst reaches it, degrees per second, fading out.")]
    public float burstSpeed = 900f;
    [Tooltip("How fast that burst fades (per second).")]
    public float burstDecay = 7f;
    [Tooltip("Seconds between the burst reaching one piece and the next one up.")]
    public float burstLag = 0.035f;
    [Tooltip("How fast the clench dies away, and how fast it rings.")]
    public float kickDecay = 8f, kickFreq = 18f;

    float[]   _angle;   // steady spin + bursts, integrated (a burst leaves the piece further round)
    Vector3[] _toMid;   // from each piece to the middle of the stack, vertical, in its parent's space

    protected override void Setup()
    {
        // Bottom to top; pieces at the same height go round the model's centre, so
        // the sequence reads as a sweep rather than a random pick.
        Parts.Sort((a, b) =>
        {
            float ya = a.t.parent.TransformPoint(a.center).y, yb = b.t.parent.TransformPoint(b.center).y;
            if (Mathf.Abs(ya - yb) > Size * 0.05f) return ya.CompareTo(yb);
            return Angle(a).CompareTo(Angle(b));
        });
        bobHeight *= 0.6f;   // it's the pieces that move; the whole model floats a little less

        int n = Parts.Count;
        _angle = new float[n];
        _toMid = new Vector3[n];
        float mid = 0f;
        foreach (var p in Parts) mid += p.t.parent.TransformPoint(p.center).y;
        if (n > 0) mid /= n;
        for (int i = 0; i < n; i++)
        {
            var p = Parts[i];
            float y = p.t.parent.TransformPoint(p.center).y;
            _toMid[i] = p.t.parent.InverseTransformVector(Vector3.up * (mid - y));
        }
    }

    float Angle(Part p)
    {
        var w = p.t.parent.TransformPoint(p.center) - transform.position;
        return Mathf.Atan2(w.z, w.x);
    }

    protected override void Animate(float dt)
    {
        for (int i = 0; i < Parts.Count; i++)
        {
            var   p   = Parts[i];
            float age = FireAge - i * burstLag;   // the shot reaches each piece a beat after the one below

            float boost = age >= 0f ? burstSpeed * Mathf.Exp(-burstDecay * age) : 0f;
            _angle[i] = Mathf.Repeat(_angle[i] + (spinSpeed + boost) * dt, 360f);

            float swing = Mathf.Sin(Clock * waveSpeed - i * waveLag) * waveDegrees;
            float dir   = alternate && (i & 1) == 1 ? -1f : 1f;
            float k     = Spring(age, kickDecay, kickFreq);

            p.Pose(Quaternion.AngleAxis((_angle[i] + swing) * dir, p.up), _toMid[i] * (clench * k));
        }
    }
}
