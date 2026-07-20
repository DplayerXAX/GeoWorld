using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Full-screen white wash driven by the hold-R-to-restart gesture: alpha tracks
// how long the key has been held, so the player can see the restart coming and
// bail out by letting go. Runtime-built overlay, no scene setup — same pattern
// as CurrencyFlyFx.
//
// Everything runs on UNSCALED time: a restart is still meant to feel responsive
// while the game is paused or mid-settlement, where Time.timeScale may be 0.
public class HoldRestartFade : MonoBehaviour
{
    static HoldRestartFade _inst;

    Canvas  _canvas;
    Image   _img;
    Coroutine _fadeOut;

    static void Ensure()
    {
        if (_inst != null) return;

        var go = new GameObject("HoldRestartFade");
        DontDestroyOnLoad(go);
        _inst = go.AddComponent<HoldRestartFade>();
        _inst.Build();
    }

    void Build()
    {
        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above EVERYTHING that can be on screen when R is held — Pause (800),
        // Settings (850), LevelClear (900), Intro (1000). It's a scene
        // transition, so anything still poking through it reads as a bug.
        _canvas.sortingOrder = 1100;

        var imgGo = new GameObject("White", typeof(RectTransform));
        imgGo.transform.SetParent(canvasGo.transform, false);
        var rt = (RectTransform)imgGo.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        _img = imgGo.AddComponent<Image>();
        _img.color         = new Color(1f, 1f, 1f, 0f);
        _img.raycastTarget = false;   // never eat clicks, even at full white

        _canvas.enabled = false;
    }

    // This object is DontDestroyOnLoad so the wash survives the scene swap (a
    // seamless white through the load, rather than a hard cut). The flip side is
    // that nothing in the reloaded scene would ever clear it — so fade back in
    // here, once the new scene is up.
    void OnEnable()  => SceneManager.sceneLoaded += HandleSceneLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= HandleSceneLoaded;

    void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (_img != null && _img.color.a > 0.001f) FadeOut(0.45f);
    }

    // 0 = clear, 1 = full white. Cancels any in-flight fade-out.
    public static void SetAlpha(float a)
    {
        Ensure();
        if (_inst._fadeOut != null) { _inst.StopCoroutine(_inst._fadeOut); _inst._fadeOut = null; }
        _inst.Apply(a);
    }

    // Player let go before the threshold — ease the wash back out.
    public static void FadeOut(float duration = 0.25f)
    {
        Ensure();
        if (_inst._fadeOut != null) _inst.StopCoroutine(_inst._fadeOut);
        _inst._fadeOut = _inst.StartCoroutine(_inst.FadeOutRoutine(duration));
    }

    void Apply(float a)
    {
        a = Mathf.Clamp01(a);
        var c = _img.color;
        c.a = a;
        _img.color = c;
        _canvas.enabled = a > 0.001f;
    }

    IEnumerator FadeOutRoutine(float duration)
    {
        float from = _img.color.a;
        if (from <= 0.001f) { Apply(0f); _fadeOut = null; yield break; }

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            Apply(Mathf.Lerp(from, 0f, t / duration));
            yield return null;
        }
        Apply(0f);
        _fadeOut = null;
    }
}
