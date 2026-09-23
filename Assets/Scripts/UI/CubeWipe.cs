using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// The screen folding into the settings cube, and back out of it.
//
// One shared component for both entrances — LevelSelect's settings and gameplay's —
// because the whole point is that opening settings feels the same wherever you do it.
// Callers only say Close/Open and hand it a callback; nothing about the screen behind
// it is this class's business.
//
// The shape it folds into is the CUBE'S OWN SILHOUETTE, sampled from the very
// render the solid cube is drawn from — not a diamond, not a hexagon, not anything
// that merely resembles it. There is one cube in this game's menus and it has to be
// one object the whole way through: a shrinking outline that hands over to a
// separate solid reads as two shapes, however carefully the two are matched.
//
// It works on a CAPTURED FRAME rather than on the live scene. A live version would
// need a render feature, a second camera or a full-screen blit pass, all of which
// mean touching the render pipeline for what is a two-second flourish; a still frame
// is correct here because the game is paused underneath anyway. GameOverScreen already
// proved this path in this project, including the colour-space trap below.
[DisallowMultipleComponent]
public class CubeWipe : MonoBehaviour
{
    public const float CloseTime = 0.42f;
    public const float OpenTime  = 0.34f;

    static CubeWipe _inst;

    static CubeWipe Ensure()
    {
        if (_inst != null) return _inst;
        var go = new GameObject("CubeWipe");
        DontDestroyOnLoad(go);
        _inst = go.AddComponent<CubeWipe>();
        _inst.Build();
        return _inst;
    }

    /// <summary>True while a wipe is playing — callers gate input on this.</summary>
    public static bool Busy { get; private set; }

    /// <summary>
    /// Fold the screen away, then run `then`. The paper stays up afterwards, so
    /// whatever `then` opens is what the player sees on it.
    /// </summary>
    public static void Close(Action then) => Ensure().StartCoroutine(Ensure().CloseRoutine(then));

    /// <summary>Unfold back to the game. Call when the menu is dismissed.</summary>
    public static void Open(Action then = null) => Ensure().StartCoroutine(Ensure().OpenRoutine(then));

    /// <summary>
    /// Drop the paper immediately, no animation.
    ///
    /// For exits where the screen is about to be replaced anyway — a scene load, a
    /// restart. Unfolding into something already going away is a flourish nobody
    /// sees, and more importantly this canvas OUTLIVES THE SCENE: forgetting to clear
    /// it leaves a full-screen sheet over the next one.
    /// </summary>
    public static void Dismiss()
    {
        if (_inst == null) return;
        _inst.StopAllCoroutines();
        Busy = false;
        if (_inst._canvas != null) _inst._canvas.enabled = false;
        if (_inst._mat    != null) _inst._mat.SetFloat("_Progress", 0f);
        _inst._progress = 0f;
    }

    Canvas    _canvas;
    RawImage  _image;
    Material  _mat;
    Texture2D _shot;

    void Build()
    {
        var canvasGo = new GameObject("WipeCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);

        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        // BELOW the pause menu (800) and the settings page (850), above the gameplay
        // HUD. This started at 900 — above both — and that is why the menu options
        // never appeared: they were being built and activated correctly, underneath a
        // full sheet of paper.
        //
        // The paper is a BACKDROP once it has folded, not a curtain. It has to cover
        // the game and nothing else, because the whole design is that the menu is
        // sitting on it.
        _canvas.sortingOrder = 780;
        _canvas.enabled = false;

        var rt = new GameObject("Frame", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(canvasGo.transform, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        _image = rt.gameObject.AddComponent<RawImage>();
        // Not a raycast target: while the paper is up the MENU wants the clicks, and
        // a full-screen image that eats them is the classic invisible-blocker bug.
        _image.raycastTarget = false;

        var sh = Shader.Find("GeoWorld/CubeWipe");
        if (sh == null)
        {
            Debug.LogWarning("[CubeWipe] Shader missing — transitions will be a plain cut.");
            return;
        }
        _mat = new Material(sh) { name = "CubeWipe" };
        _image.material = _mat;
    }

    IEnumerator CloseRoutine(Action then)
    {
        if (_mat == null) { then?.Invoke(); yield break; }

        Busy = true;
        yield return CaptureFrame();

        _canvas.enabled = true;
        yield return Drive(0f, 1f, CloseTime);

        // The paper stays. Whatever `then` opens gets to appear on it, which is why
        // the menu never has to draw a background of its own.
        Busy = false;
        then?.Invoke();
    }

    IEnumerator OpenRoutine(Action then)
    {
        if (_mat == null || !_canvas.enabled) { then?.Invoke(); yield break; }

        Busy = true;

        // NO re-capture on the way out.
        //
        // It used to re-shoot here, on the theory that a setting might have changed
        // the board. That was wrong for a reason the theory could not see: this
        // canvas is still up and still covering the screen, so what the camera grabs
        // is the PAPER — and the diamond then opened to reveal more paper, with the
        // real game only snapping in at the end when the canvas switched off.
        //
        // The frame taken on the way in is the right one anyway: the game is frozen
        // underneath and has not moved since.
        yield return Drive(1f, 0f, OpenTime);

        _canvas.enabled = false;
        Busy = false;
        then?.Invoke();
    }

    // How much bigger than the longest screen edge the silhouette starts.
    //
    // It has to be large enough that the whole frame falls inside the HEXAGON at
    // progress 0 — not merely inside the square render that holds it — or the fold
    // begins with an outline already drawn across the picture. Working it through:
    // the screen corner sits diag/(1.88*F*max) into the render's own space, and the
    // hexagon reaches about 0.28 there, so F must clear ~2.2 for a wide screen and
    // ~2.7 for a square one. 3.4 covers both with room, and costs about a tenth of
    // the travel happening off screen.
    const float StartFactor = 3.4f;

    float _progress;

    // Eased toward the target rather than ramped along a clock.
    //
    // The difference is what the two ends look like. A linear ramp starts and stops
    // dead, so the paper snaps into motion and snaps out of it; an exponential
    // approach leaves at speed and arrives slowing down, which is how a sheet of
    // paper being folded actually behaves — and it is the idiom every other motion in
    // this project already uses.
    //
    // `seconds` is kept as the argument because it is what callers think in. The rate
    // is derived so the move still lands in about that long: e^-5.5 is the threshold
    // below, so k = 6/seconds arrives with a little to spare.
    IEnumerator Drive(float from, float to, float seconds)
    {
        float p = from;
        float k = 6f / Mathf.Max(0.05f, seconds);
        Apply(p);

        while (Mathf.Abs(p - to) > 0.004f)
        {
            // Unscaled: this plays while the game is frozen at timeScale 0, which is
            // exactly when a transition must still move.
            p = Mathf.Lerp(p, to, 1f - Mathf.Exp(-k * Time.unscaledDeltaTime));
            Apply(p);
            yield return null;
        }
        Apply(to);
    }

    // Where the silhouette is this frame.
    //
    // The size travels in LOG space, not linearly. This is a shrink of forty to one,
    // and a linear one spends almost all its time in the last stretch being small: the
    // eye reads rate of change as a proportion, so equal ratios per second is what
    // looks like an even movement.
    void Apply(float p)
    {
        _progress = p;
        _mat.SetFloat("_Progress", p);
        _mat.SetFloat("_Aspect", Screen.width / Mathf.Max(1f, (float)Screen.height));

        var mask = SettingsCube.SilhouetteTexture;
        if (mask == null || !SettingsCube.TryCubeScreenRect(out var slotPx, out var sizePx) || sizePx < 1f)
        {
            // No cube to cut to — fall back to the diamond rather than to nothing.
            _mat.SetFloat("_UseMask", 0f);
            return;
        }

        _mat.SetTexture("_MaskTex", mask);
        _mat.SetFloat("_UseMask", 1f);

        float startPx = StartFactor * Mathf.Max(Screen.width, Screen.height);
        float side    = Mathf.Exp(Mathf.Lerp(Mathf.Log(startPx), Mathf.Log(sizePx), Mathf.Clamp01(p)));

        var screenMid = new Vector2(Screen.width, Screen.height) * 0.5f;
        Vector2 centre = Vector2.Lerp(screenMid, slotPx, Mathf.Clamp01(p));

        // 0.47 rather than 0.5: the window sits a hair INSIDE the solid cube that
        // lands on it, so no sliver of game can show around the edge if the two are a
        // pixel out.
        _mat.SetVector("_MaskRect", new Vector4(
            centre.x / Mathf.Max(1f, Screen.width),
            centre.y / Mathf.Max(1f, Screen.height),
            side * 0.47f / Mathf.Max(1f, Screen.width),
            side * 0.47f / Mathf.Max(1f, Screen.height)));
    }

    // The window has to keep up with the cube after the fold has finished, because
    // the cube goes on moving: opening settings walks it over to the left-hand
    // column. Left where the fold put it, it would be a hole in the paper in the
    // middle of the screen with nothing over it.
    void Update()
    {
        if (_mat != null && _canvas != null && _canvas.enabled && !Busy) Apply(_progress);
    }

    IEnumerator CaptureFrame()
    {
        // Must be after everything has drawn, or the capture is a half-rendered frame.
        yield return new WaitForEndOfFrame();

        if (_shot != null) Destroy(_shot);
        var raw = ScreenCapture.CaptureScreenshotAsTexture();
        _shot = AsSrgb(raw);
        if (_shot != raw) Destroy(raw);

        _image.texture = _shot;
        _mat.SetTexture("_MainTex", _shot);
    }

    // CaptureScreenshotAsTexture hands back the already gamma-encoded backbuffer in a
    // texture flagged LINEAR, so a linear project converts it a second time and the
    // capture comes out washed out. Re-wrapping the identical bytes in an sRGB
    // texture fixes it without touching a pixel. (Same trap GameOverScreen hit.)
    static Texture2D AsSrgb(Texture2D src)
    {
        if (QualitySettings.activeColorSpace != ColorSpace.Linear) return src;

        var dst = new Texture2D(src.width, src.height, src.format, false, linear: false);
        try
        {
            dst.SetPixelData(src.GetRawTextureData<byte>(), 0);
            dst.Apply(false, false);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CubeWipe] sRGB re-wrap failed ({e.Message}) — using the raw capture.");
            Destroy(dst);
            return src;
        }
        return dst;
    }

    void OnDestroy()
    {
        if (_shot != null) Destroy(_shot);
        if (_mat  != null) Destroy(_mat);
        if (_inst == this) _inst = null;
    }
}
