using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// Defeat sequence: a hard camera jolt, then the whole picture dissolving into
// blurred blue colour blocks, and only then the GAME OVER card.
//
// The blur grabs ONE screenshot at the moment of death and then repeatedly
// downsamples it: a 1920×1080 frame blitted through a 16×9 buffer and stretched
// back out IS a blur, and a progressively coarser one. No Renderer Feature, no
// post-process volume, no blit shader to author or to keep out of the build's
// shader stripper.
//
// It captures the finished frame rather than re-pointing the camera at a
// RenderTexture (the first attempt) because that depended on resolving the right
// camera and on URP honouring a mid-frame targetTexture swap — it silently did
// nothing. Freezing the frame is also truer to the moment: the run is over, so
// the world has no business still animating underneath.
[DisallowMultipleComponent]
public class GameOverScreen : MonoBehaviour
{
    public static bool Active { get; private set; }

    public static void Show()
    {
        // Guarded on a live instance rather than the static flag: statics survive
        // scene loads, so a flag left true by a teardown that didn't run cleanly
        // would silently suppress every future game-over for the whole session.
        if (FindFirstObjectByType<GameOverScreen>() != null) return;
        var go = new GameObject("GameOverScreen");
        go.AddComponent<GameOverScreen>().Begin();
    }

    // ── Timing ───────────────────────────────────────────────────────────────
    const float ShakeSettleDelay = 0.6f;   // > CameraShake.Death()'s 0.55s — freeze after it lands, not mid-jolt
    const float BlurDelay   = 0.6f;    // blur starts right as the frozen frame appears
    const float BlurRamp    = 1.2f;    // blur + tint reaching full strength
    const float CardDelay   = 2.0f;    // GAME OVER begins fading in
    const float CardFade    = 0.6f;
    const float RestartFade = 0.45f;   // blurred frame → black, before the scene reload

    const int   MinBlurRes  = 36;      // shortest edge at maximum blur

    // ── Blue ─────────────────────────────────────────────────────────────────
    // Three cooperating layers, because one flat overlay only ever greys the
    // picture out — it can't get dense without going opaque and flat:
    //   1. the frozen frame MULTIPLIED toward blue — dyes the image instead of
    //      painting over it, so its own light and dark survive the wash
    //   2. a flat wash on top for overall density
    //   3. a radial gradient, near-navy at the edges — the actual bleed, so the
    //      colour has somewhere to be deeper than somewhere else
    static readonly Color DyeColor  = new(0.30f, 0.50f, 1.00f);   // multiply, not paint
    static readonly Color TintColor = new(0.10f, 0.24f, 0.62f);   // flat wash
    static readonly Color EdgeColor = new(0.02f, 0.07f, 0.30f);   // vignette, near-navy

    const float TintPeak   = 0.44f;   // flat wash at full strength
    const float EdgePeak   = 0.88f;   // vignette at full strength, at the corners

    Texture2D     _shot;
    RenderTexture _rt;
    RawImage      _screen;
    Image         _tint;
    RawImage      _vignette;
    CanvasGroup   _cardGroup;
    Image         _fade;
    bool          _restarting;
    float         _t;
    int           _rtShort = -1;

    void Begin()
    {
        Active = true;
        CameraShake.Death();
        BuildUI();
        StartCoroutine(CaptureFrame());
    }

    System.Collections.IEnumerator CaptureFrame()
    {
        // Wait out the death shake first — capturing immediately would freeze a
        // jittering mid-shake frame instead of the settled view.
        yield return new WaitForSecondsRealtime(ShakeSettleDelay);

        // Must be end-of-frame: the screenshot reads the back buffer, and before
        // the frame finishes there's nothing complete in it to read.
        yield return new WaitForEndOfFrame();

        var raw = ScreenCapture.CaptureScreenshotAsTexture();
        if (raw == null)
        {
            Debug.LogWarning("[GameOverScreen] Screen capture failed — blue wash only, no blur.");
            yield break;
        }
        _shot = AsSrgb(raw);
        _shot.filterMode = FilterMode.Bilinear;
        _screen.texture  = _shot;
        _screen.enabled  = true;   // from here the frozen frame covers the live view
    }

    // CaptureScreenshotAsTexture hands back the already gamma-encoded backbuffer
    // in a texture flagged LINEAR. Drawn through UGUI in a linear-space project
    // that skips the sRGB decode on sample but still encodes on the way out, so
    // the frame comes back washed out and hue-shifted against the live view it
    // replaces. Re-wrapping the identical bytes in an sRGB-flagged texture fixes
    // the interpretation — no pixel is touched, only how it's read.
    static Texture2D AsSrgb(Texture2D src)
    {
        if (QualitySettings.activeColorSpace != ColorSpace.Linear) return src;

        var dst = new Texture2D(src.width, src.height, src.format, false, linear: false);
        try
        {
            dst.SetPixelData(src.GetRawTextureData<byte>(), 0);
            dst.Apply(false, false);
        }
        catch (System.Exception e)
        {
            // Only reachable if the capture format isn't raw-copyable. The
            // uncorrected frame still beats no frame at all.
            Debug.LogWarning($"[GameOverScreen] sRGB re-wrap failed ({e.Message}) — using the raw capture.");
            Destroy(dst);
            return src;
        }

        Destroy(src);
        return dst;
    }

    void Update()
    {
        if (_restarting) return;   // the wipe owns the card alpha from here

        _t += Time.unscaledDeltaTime;

        float k = Mathf.Clamp01((_t - BlurDelay) / BlurRamp);
        // Ease-in: stays nearly sharp for a beat, then falls apart quickly.
        float blur = k * k;

        UpdateBlur(blur);

        // Smoothstep rather than the blur's own quadratic: colour bleeding in
        // wants to ease out at the end, not slam into full strength.
        float dye = Mathf.SmoothStep(0f, 1f, blur);

        if (_screen != null)
            _screen.color = Color.Lerp(Color.white, DyeColor, dye);

        // Wash and vignette are their OWN overlays rather than tints on the
        // captured image, so the moment still reads if the capture fails — the
        // blue is what carries the beat, the blur is what can degrade.
        if (_tint != null)
            _tint.color = new Color(TintColor.r, TintColor.g, TintColor.b, dye * TintPeak);

        if (_vignette != null)
            _vignette.color = new Color(EdgeColor.r, EdgeColor.g, EdgeColor.b, dye * EdgePeak);

        if (_cardGroup != null)
            _cardGroup.alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((_t - CardDelay) / CardFade));
    }

    void UpdateBlur(float blur)
    {
        if (_shot == null || _screen == null) return;

        // Shortest edge of the downsample buffer, full res → MinBlurRes.
        int fullShort = Mathf.Max(1, Mathf.Min(_shot.width, _shot.height));
        int shortEdge = Mathf.Max(MinBlurRes, Mathf.RoundToInt(Mathf.Lerp(fullShort, MinBlurRes, blur)));

        // Only reallocate on a real change — resizing every frame would churn GPU
        // memory for nothing while the blur is still ramping smoothly.
        if (shortEdge == _rtShort && _rt != null) return;
        _rtShort = shortEdge;

        // At full resolution there's nothing to gain from a round trip — show the
        // capture directly, which also keeps frame one pixel-identical.
        if (shortEdge >= fullShort)
        {
            _screen.texture = _shot;
            return;
        }

        float aspect = (float)_shot.width / Mathf.Max(1, _shot.height);
        int h = shortEdge;
        int w = Mathf.Max(1, Mathf.RoundToInt(shortEdge * aspect));

        var old = _rt;
        _rt = new RenderTexture(w, h, 0) { filterMode = FilterMode.Bilinear };
        _rt.Create();

        // Default blit — bilinear minification into the small buffer, then the
        // RawImage stretches it back up. Both halves of that round trip blur.
        Graphics.Blit(_shot, _rt);

        _screen.texture = _rt;
        if (old != null) { old.Release(); Destroy(old); }
    }

    void BuildUI()
    {
        var canvasGo = new GameObject("GameOverCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above every other overlay canvas in the scene — the blurred frame has to
        // cover the HUD, not sit behind it.
        canvas.sortingOrder = 5000;

        var sc = canvasGo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;
        EnsureEventSystem();

        // The blurred frame itself. Disabled until UpdateBlur actually has a
        // texture — a RawImage with no texture draws opaque white, which would
        // flash the screen blank for a frame if the capture is a step behind.
        var screenRt = NewRect("Screen", canvasGo.transform);
        screenRt.anchorMin = Vector2.zero; screenRt.anchorMax = Vector2.one;
        screenRt.offsetMin = screenRt.offsetMax = Vector2.zero;
        _screen = screenRt.gameObject.AddComponent<RawImage>();
        _screen.color = Color.white;
        _screen.raycastTarget = false;
        _screen.enabled = false;

        // Blue wash, drawn over the captured frame.
        var tintRt = NewRect("Tint", canvasGo.transform);
        tintRt.anchorMin = Vector2.zero; tintRt.anchorMax = Vector2.one;
        tintRt.offsetMin = tintRt.offsetMax = Vector2.zero;
        _tint = tintRt.gameObject.AddComponent<Image>();
        _tint.color = new Color(TintColor.r, TintColor.g, TintColor.b, 0f);
        _tint.raycastTarget = false;

        // Radial bleed over the flat wash — clear in the middle, near-navy at the
        // corners. Stretching the square gradient to the screen's aspect is
        // deliberate: a vignette should follow the frame, not stay circular.
        var vigRt = NewRect("Vignette", canvasGo.transform);
        vigRt.anchorMin = Vector2.zero; vigRt.anchorMax = Vector2.one;
        vigRt.offsetMin = vigRt.offsetMax = Vector2.zero;
        _vignette = vigRt.gameObject.AddComponent<RawImage>();
        _vignette.texture = RadialFade();
        _vignette.color = new Color(EdgeColor.r, EdgeColor.g, EdgeColor.b, 0f);
        _vignette.raycastTarget = false;

        // Card (text + button), faded in as one group after the blur settles.
        var cardRt = NewRect("Card", canvasGo.transform);
        cardRt.anchorMin = Vector2.zero; cardRt.anchorMax = Vector2.one;
        cardRt.offsetMin = cardRt.offsetMax = Vector2.zero;
        _cardGroup = cardRt.gameObject.AddComponent<CanvasGroup>();
        _cardGroup.alpha = 0f;

        var title = NewText("Title", cardRt, 96f, GeoPalette.Paper, FontStyles.Bold);
        title.text = "GAME OVER";
        var trt = title.rectTransform;
        trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
        trt.anchoredPosition = new Vector2(0f, 70f);
        trt.sizeDelta = new Vector2(1200f, 160f);

        BuildRestartButton(cardRt);

        // Above everything, including the card — the restart wipe.
        var fadeRt = NewRect("Fade", canvasGo.transform);
        fadeRt.anchorMin = Vector2.zero; fadeRt.anchorMax = Vector2.one;
        fadeRt.offsetMin = fadeRt.offsetMax = Vector2.zero;
        _fade = fadeRt.gameObject.AddComponent<Image>();
        _fade.color = new Color(0f, 0f, 0f, 0f);
        _fade.raycastTarget = false;
    }

    void BuildRestartButton(RectTransform parent)
    {
        var go = new GameObject("Restart", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, -70f);
        rt.sizeDelta = new Vector2(320f, 76f);

        var img = go.AddComponent<Image>();
        img.sprite = UIRoundedRect.Get(14);
        img.type   = Image.Type.Sliced;
        img.color  = GeoPalette.Paper;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors; colors.highlightedColor = GeoPalette.Gold; btn.colors = colors;
        btn.onClick.AddListener(Restart);

        var label = NewText("Label", rt, 32f, GeoPalette.Ink, FontStyles.Bold);
        label.text = "RESTART";
        var lrt = label.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
    }

    void Restart()
    {
        if (_restarting) return;
        _restarting = true;
        StartCoroutine(RestartRoutine());
    }

    System.Collections.IEnumerator RestartRoutine()
    {
        // LoadingScreen snaps its overlay on with no transition of its own, so
        // without this the blurred blue frame cuts to the loading page in a single
        // frame. Fading to black first gives it something to arrive out of.
        float t = 0f;
        while (t < RestartFade)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(t / RestartFade);
            if (_fade != null) _fade.color = new Color(0f, 0f, 0f, a);
            // The card leaves ahead of the black rather than being buried under it
            // — text dimming behind a rising curtain reads as a bug, not a wipe.
            if (_cardGroup != null) _cardGroup.alpha = 1f - Mathf.Clamp01(a * 1.6f);
            yield return null;
        }

        Release();
        GameFlowManager.Instance?.RestartGame();
    }

    void Release()
    {
        Active = false;
        if (_rt != null)   { _rt.Release(); Destroy(_rt); _rt = null; }
        if (_shot != null) { Destroy(_shot); _shot = null; }
    }

    void OnDestroy() => Release();

    // ── Helpers ──────────────────────────────────────────────────────────────

    // White with a radial alpha ramp — transparent in the middle, solid at the
    // corners. Small and bilinear-filtered: at 64px stretched over a full screen
    // the interpolation IS the softness, and a bigger texture would only cost
    // memory for a gradient that has no detail to preserve.
    static Texture2D _radial;
    static Texture2D RadialFade()
    {
        if (_radial != null) return _radial;

        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
        {
            wrapMode   = TextureWrapMode.Clamp,   // clamped: edge pixels must not wrap back to the clear centre
            filterMode = FilterMode.Bilinear,
            hideFlags  = HideFlags.HideAndDontSave,
        };

        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float dx = (x + 0.5f) / N * 2f - 1f;
            float dy = (y + 0.5f) / N * 2f - 1f;
            float r  = Mathf.Sqrt(dx * dx + dy * dy) / 1.4142136f;   // 0 centre → 1 corner
            // Held clear out to 0.3 so the middle of the frame — where GAME OVER
            // and the button land — stays the readable part of the picture.
            px[y * N + x] = new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.30f, 1f, r)));
        }

        tex.SetPixels(px);
        tex.Apply(false, false);
        _radial = tex;
        return tex;
    }

    static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        new GameObject("EventSystem",
            typeof(EventSystem),
            typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static TMP_Text NewText(string name, Transform parent, float size, Color color, FontStyles style)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize = size; t.color = color; t.fontStyle = style;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        return t;
    }
}
