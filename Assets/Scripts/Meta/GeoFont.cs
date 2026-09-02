using TMPro;
using UnityEngine;

// The game's display face, fetched by name.
//
// Everything else in this project takes its font from a `public TMP_FontAsset font`
// the scene fills in, which is right for a component someone places by hand. It is no
// use to the screens that build themselves from a RuntimeInitializeOnLoadMethod: there
// is no inspector to fill in, and a menu that silently falls back to Liberation Sans
// is a menu in the wrong typeface with nothing to point at.
//
// The asset lives under Assets/font/Resources/ purely so it can be found this way.
public static class GeoFont
{
    const string StampAsset = "CsCesareStampRegularDemo-dr47x SDF";

    static TMP_FontAsset _stamp;
    static bool _looked;

    /// <summary>
    /// The stamped display face — the game's title lettering. Null if the asset has
    /// been moved out of a Resources folder, in which case callers should leave the
    /// text at whatever font TMP gave it.
    /// </summary>
    public static TMP_FontAsset Stamp
    {
        get
        {
            // Looked for ONCE, even when it is missing. Resources.Load on a name that
            // is not there is not free, and this is asked for per label.
            if (_looked) return _stamp;
            _looked = true;

            _stamp = Resources.Load<TMP_FontAsset>(StampAsset);
            if (_stamp == null)
                Debug.LogWarning($"[GeoFont] '{StampAsset}' is not under a Resources folder — " +
                                 "runtime-built screens will use the default face.");
            return _stamp;
        }
    }

    /// <summary>Applies the stamp face if it is there, and leaves the text alone if not.</summary>
    public static void ApplyStamp(TMP_Text t)
    {
        if (t != null && Stamp != null) t.font = Stamp;
    }
}
