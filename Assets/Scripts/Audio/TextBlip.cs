using UnityEngine;

// The typewriter blip, for anything that types text: the dialogue box, the tutorial
// hint box. One blip at a time, and it belongs to whoever started it.
//
// ── WHY AN OWNER ─────────────────────────────────────────────────────────────
// There is one blip slot. The tutorial's hint box stops the blip every frame it is
// hidden (a guard against a hidden box leaving the blip ringing) — and a step with
// a speaker hides the hint box because its line is being shown by the DIALOGUE box
// instead. So the dialogue started a blip and the hidden hint box stopped it on the
// same frame: only speaker-less hints were ever heard. A stop from anyone but the
// blip's own starter is now ignored; a stop with no owner (cleanup) still wins.
//
// ── WHY A FALLBACK ───────────────────────────────────────────────────────────
// AudioManager lives in the gameplay and title scenes only. LevelSelect has none,
// so dialogue on the map was silent however it was called. A scene without an
// AudioManager registers its own blip event here (LevelMapController.textBlip).
public static class TextBlip
{
    static AK.Wwise.Event _fallback;
    static GameObject     _fallbackEmitter;
    static uint           _id;
    static Object         _owner;

    public static void SetFallback(AK.Wwise.Event evt, GameObject emitter)
    {
        if (evt == null) Stop(null);
        _fallback        = evt;
        _fallbackEmitter = emitter;
    }

    public static void Start(Object owner)
    {
        var am = AudioManager.Instance;
        if (am != null) { am.StartTextBlip(owner); return; }

        Stop(null);   // a new line takes the slot, whoever held it
        if (_fallback == null || !_fallback.IsValid() || _fallbackEmitter == null) return;
        _id    = _fallback.Post(_fallbackEmitter);
        _owner = owner;
    }

    public static void Stop(Object owner)
    {
        var am = AudioManager.Instance;
        if (am != null) am.StopTextBlip(owner);

        if (_id == 0) return;
        if (owner != null && _owner != null && owner != _owner) return;   // someone else's line is typing
        AkUnitySoundEngine.StopPlayingID(_id, 150, AkCurveInterpolation.AkCurveInterpolation_Linear);
        _id    = 0;
        _owner = null;
    }
}
