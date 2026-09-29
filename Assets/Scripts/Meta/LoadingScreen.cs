using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Loading page with a persistent spinning ◇ cube (the game's signature element).
// Auto-spawns once and persists. Call LoadingScreen.Go("scene") to fade in the
// overlay, async-load the scene, then drop it — so every transition shows the
// same spinning-cube loading screen.
//
// A scene that keeps building after it has loaded (the level-select map, a
// level's far scenery) calls Hold(this) and Release(this) round the work: the
// page stays up until every hold is released, then fades out. A Hold taken in
// Awake of the FIRST scene (entering Play straight into it) puts the page up
// too, so a direct start gets it as well.
[DisallowMultipleComponent]
public class LoadingScreen : MonoBehaviour
{
    static LoadingScreen _inst;
    bool     _active;
    GUIStyle _label;
    float    _alpha = 1f;   // fades out once everything is built
    Coroutine _boot;
    static float _drawAlpha = 1f;

    static readonly HashSet<Object> _holds = new();
    const float HoldTimeout = 30f;   // a hold never released must not trap the player behind the page

    // Tips pool — Resources/LoadingTips.asset, edited directly in the Inspector
    // (see LoadingTipsData). Loaded once and cached; a new one is picked each
    // time a load starts, not every frame.
    static LoadingTipsData _tipsData;
    static bool            _tipsLoadAttempted;
    string   _currentTip;
    GUIStyle _tipCaptionStyle, _tipStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Spawn()
    {
        if (_inst != null) return;
        var go = new GameObject("LoadingScreen");
        DontDestroyOnLoad(go);
        _inst = go.AddComponent<LoadingScreen>();
        if (Held) _inst._boot = _inst.StartCoroutine(_inst.Boot());   // the first scene is still building
    }

    public static bool Active => _inst != null && _inst._active;

    // Keep the page up until Release(key). Keys are the objects doing the work,
    // so one destroyed mid-build (scene left early) stops holding by itself.
    public static void Hold(Object key)
    {
        if (key == null) return;
        _holds.Add(key);
        if (_inst != null && !_inst._active) _inst._boot = _inst.StartCoroutine(_inst.Boot());
    }

    public static void Release(Object key)
    {
        if (key != null) _holds.Remove(key);
    }

    static bool Held
    {
        get
        {
            _holds.RemoveWhere(k => k == null);
            return _holds.Count > 0;
        }
    }

    IEnumerator WaitForHolds()
    {
        float t = 0f;
        while (Held && t < HoldTimeout)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        if (Held)
        {
            Debug.LogWarning("[Loading] still held after " + HoldTimeout + "s — dropping the page anyway.");
            _holds.Clear();
        }
    }

    IEnumerator FadeOut()
    {
        const float dur = 0.3f;
        for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
        {
            _alpha = 1f - t / dur;
            yield return null;
        }
        _alpha  = 1f;
        _active = false;
    }

    // Entering Play straight into a scene that builds: up at once, down when done.
    IEnumerator Boot()
    {
        _active = true;
        _alpha  = 1f;
        PickTip();
        yield return null;
        yield return WaitForHolds();
        yield return FadeOut();
    }

    // Show the loading page, then async-load the scene.
    public static void Go(string scene)
    {
        if (string.IsNullOrEmpty(scene)) return;
        if (_inst == null || !Application.CanStreamedLevelBeLoaded(scene))
        {
            if (Application.CanStreamedLevelBeLoaded(scene)) SceneManager.LoadScene(scene);
            else Debug.LogWarning($"[Loading] scene '{scene}' not in Build Settings.");
            return;
        }
        _inst.StartCoroutine(_inst.Run(scene));
    }

    IEnumerator Run(string scene)
    {
        if (_boot != null) { StopCoroutine(_boot); _boot = null; }
        _active = true;
        _alpha  = 1f;
        PickTip();
        yield return null;                 // paint the overlay before the hitch
        var op = SceneManager.LoadSceneAsync(scene);
        while (op != null && !op.isDone) yield return null;
        yield return null;                 // the new scene's Start runs; whatever it builds over frames holds the page
        yield return WaitForHolds();
        float hold = 0.25f;                 // brief hold so the spinner reads on fast loads
        while (hold > 0f) { hold -= Time.unscaledDeltaTime; yield return null; }
        yield return FadeOut();
    }

    // Picks a fresh random tip for this load. Resources.Load only actually hits
    // disk once (guarded by _tipsLoadAttempted) — an empty/missing asset just
    // means no tip is shown, not an error.
    void PickTip()
    {
        if (!_tipsLoadAttempted)
        {
            _tipsLoadAttempted = true;
            _tipsData = Resources.Load<LoadingTipsData>("LoadingTips");
        }
        _currentTip = (_tipsData != null && _tipsData.tips != null && _tipsData.tips.Length > 0)
            ? _tipsData.tips[Random.Range(0, _tipsData.tips.Length)]
            : null;
    }

    void OnGUI()
    {
        if (!_active) return;
        GUI.depth = -2000;                 // above all other IMGUI (incl. settings)
        float s = UiScale.Get();
        _drawAlpha = _alpha;

        Fill(new Rect(0, 0, Screen.width, Screen.height), GeoPalette.Paper);
        Color p = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, _alpha);   // the labels fade with the page
        Fill(new Rect(0, 0, Screen.width, 5f * s), GeoPalette.Ink);            // top rule (thinner, like the panels)
        Fill(new Rect(0, 0, 6f * s, Screen.height), GeoPalette.Signal);        // left spine

        // Spinning cube, bottom-right.
        float cz = 52f * s;
        var cube = new Rect(Screen.width - 110f * s, Screen.height - 110f * s, cz, cz);
        DrawSpinningCube(cube, Time.unscaledTime * 140f);

        if (_label == null) _label = new GUIStyle { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
        _label.fontSize = Mathf.RoundToInt(20f * s);
        _label.normal.textColor = GeoPalette.Ink;
        GUI.Label(new Rect(Screen.width - 380f * s, cube.y, 240f * s, cz), "LOADING…", _label);

        // Tip, bottom-left — same silkscreen language as the panels elsewhere
        // (small bold signal-colored caption over an ink body line).
        if (!string.IsNullOrEmpty(_currentTip))
        {
            if (_tipCaptionStyle == null) _tipCaptionStyle = new GUIStyle { fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft };
            if (_tipStyle == null) _tipStyle = new GUIStyle { fontStyle = FontStyle.Italic, alignment = TextAnchor.UpperLeft, wordWrap = true };
            _tipCaptionStyle.fontSize = Mathf.RoundToInt(14f * s);
            _tipCaptionStyle.normal.textColor = GeoPalette.Signal;
            _tipStyle.fontSize = Mathf.RoundToInt(19f * s);
            _tipStyle.normal.textColor = GeoPalette.Ink;

            float tipX     = 40f * s;
            float tipY     = Screen.height - 130f * s;
            float tipWidth = Mathf.Min(700f * s, Screen.width - 80f * s);
            GUI.Label(new Rect(tipX, tipY, tipWidth, 22f * s), "TIP", _tipCaptionStyle);
            GUI.Label(new Rect(tipX, tipY + 22f * s, tipWidth, 90f * s), _currentTip, _tipStyle);
        }
        GUI.color = p;
    }

    // Muted toward paper so the cube reads like a printed mark, not four hard primaries.
    static Color Soft(Color c) => Color.Lerp(c, GeoPalette.Paper, 0.22f);

    static void DrawSpinningCube(Rect r, float angle)
    {
        Matrix4x4 m = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, r.center);
        // Four-tone square reads as a tumbling cube; paper gaps + ink seams suggest faces.
        Fill(new Rect(r.x,        r.y,        r.width * 0.5f, r.height * 0.5f), Soft(GeoPalette.Gold));
        Fill(new Rect(r.center.x, r.y,        r.width * 0.5f, r.height * 0.5f), Soft(GeoPalette.Signal));
        Fill(new Rect(r.x,        r.center.y, r.width * 0.5f, r.height * 0.5f), Soft(GeoPalette.Blue));
        Fill(new Rect(r.center.x, r.center.y, r.width * 0.5f, r.height * 0.5f), GeoPalette.Ink);
        Fill(new Rect(r.x, r.center.y - 2f, r.width, 4f), GeoPalette.Paper);   // horizontal gap (silkscreen)
        Fill(new Rect(r.center.x - 2f, r.y, 4f, r.height), GeoPalette.Paper);  // vertical gap
        Fill(new Rect(r.x, r.center.y - 1f, r.width, 2f), GeoPalette.Ink);     // thin ink seam
        Fill(new Rect(r.center.x - 1f, r.y, 2f, r.height), GeoPalette.Ink);
        GUI.matrix = m;
    }

    static void Fill(Rect r, Color c)
    {
        Color p = GUI.color; GUI.color = new Color(c.r, c.g, c.b, c.a * _drawAlpha);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = p;
    }
}
