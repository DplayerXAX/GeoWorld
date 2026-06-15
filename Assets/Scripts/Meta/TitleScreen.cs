using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Persona / constructivist title menu (IMGUI, matches the game's other HUD).
// Pairs with TitleStage (white camera + shader-cycling cube). Sits on the LEFT
// third so the 3D cube showcase shows through on the right.
//
// Menu: PLAY → LevelSelect · ENDLESS → gameplay (endless) · TECH → toast (stub)
//       · QUIT. Mouse hover or W/S + Enter to drive it.
[DisallowMultipleComponent]
public class TitleScreen : MonoBehaviour
{
    [Header("Scenes (must be in Build Settings)")]
    public string levelSelectScene = "LevelSelect";
    public string gameplayScene    = "gamePlay";

    [Header("Branding")]
    public string titleTop    = "GEO";
    public string titleBottom = "WORLD";
    public string tagline     = "CONSTRUCT · DEFEND · RESONATE";
    public string version     = "v0.1 · seed engine";
    [Tooltip("Optional heavy display font for the logo/menu. Null = built-in font.")]
    public Font font;

    [Header("Motion")]
    public float introStagger = 0.09f;   // delay between cascading elements
    public float introDur     = 0.55f;

    static readonly string[] Items = { "PLAY", "ENDLESS", "TECH TREE", "SETTINGS", "QUIT" };

    // True while the cursor is over a menu row — TitleStage reads this so a
    // background click (shatter burst) doesn't fire when you click a menu item.
    public static bool PointerOverMenu;

    float  _t0;
    int    _sel;
    int    _lastSel = -1;
    string _toast;
    float  _toastUntil;

    GUIStyle _logo, _item, _small, _mono;
    float    _builtScale = -1f;

    void OnEnable() => _t0 = Time.unscaledTime;

    void OnGUI()
    {
        float s = UiScale.Get();
        BuildStyles(s);
        HandleKeys();
        PointerOverMenu = false;

        float t = Time.unscaledTime - _t0;
        DrawAccents(s, t);
        DrawLogo(s, t);
        DrawMenu(s, t);
        DrawFooter(s, t);
        DrawToast(s);

        // Nudge the 3D geometry whenever the selection moves.
        if (_sel != _lastSel) { _lastSel = _sel; TitleStage.Instance?.Pulse(); }
    }

    // ── Composition ────────────────────────────────────────────────────────────

    void DrawAccents(float s, float t)
    {
        // Left signal bar — always present, the constructivist spine.
        Fill(new Rect(0, 0, 14f * s, Screen.height), GeoPalette.Signal);

        // Top-right ink + gold diagonals, sliding in.
        float a = Appear(t, 0);
        float w = Screen.width;
        RotatedFill(new Rect(w - 300f * s, -40f * s, 360f * s, 30f * s),
                    GeoPalette.WithAlpha(GeoPalette.Ink, a), 28f, new Vector2(w, 0f));
        RotatedFill(new Rect(w - 250f * s, 8f * s, 250f * s, 14f * s),
                    GeoPalette.WithAlpha(GeoPalette.Gold, a), 28f, new Vector2(w, 0f));

        // Bottom-left gold wedge.
        RotatedFill(new Rect(-30f * s, Screen.height - 24f * s, 220f * s, 24f * s),
                    GeoPalette.WithAlpha(GeoPalette.Gold, a), -32f, new Vector2(0f, Screen.height));

        // Cursor-tracking scan line — the composition reacts as you move.
        float my = Event.current.mousePosition.y;
        Fill(new Rect(0f, my - 1.5f * s, Screen.width, 3f * s),
             GeoPalette.WithAlpha(GeoPalette.Ink, 0.08f));
    }

    void DrawLogo(float s, float t)
    {
        float a = Appear(t, 0);

        // Cursor parallax — the wordmark drifts slightly against the geometry.
        Vector2 mm = Event.current.mousePosition;
        float px = (mm.x / Mathf.Max(1f, Screen.width)  - 0.5f) * 22f * s;
        float py = (mm.y / Mathf.Max(1f, Screen.height) - 0.5f) * 14f * s;

        float dx = (1f - EaseOutBack(a)) * -60f * s + px;
        float x  = 50f * s + dx;

        // Red slash behind the wordmark.
        RotatedFill(new Rect(34f * s + dx, 150f * s + py, 340f * s, 16f * s),
                    GeoPalette.WithAlpha(GeoPalette.Signal, a), -7f,
                    new Vector2(34f * s + dx, 158f * s + py));

        _logo.normal.textColor = GeoPalette.WithAlpha(GeoPalette.Ink, a);
        GUI.Label(new Rect(x, 18f * s + py, 600f * s, 90f * s), titleTop,    _logo);
        GUI.Label(new Rect(x, 88f * s + py, 600f * s, 90f * s), titleBottom, _logo);
    }

    void DrawMenu(float s, float t)
    {
        float x0 = 44f * s, w = 250f * s, h = 48f * s, gap = 12f * s;
        float y0 = Screen.height * 0.40f;
        Vector2 mouse = Event.current.mousePosition;

        for (int i = 0; i < Items.Length; i++)
        {
            float a  = Appear(t, i + 1);
            if (a <= 0f) continue;

            // Rows near the cursor push out — the menu re-composes around the mouse.
            float cy   = y0 + i * (h + gap) + h * 0.5f;
            float prox = 1f - Mathf.Clamp01(Mathf.Abs(cy - mouse.y) / (200f * s));
            float dx   = (1f - EaseOutBack(a)) * -50f * s + prox * 26f * s;
            var   r    = new Rect(x0 + dx, y0 + i * (h + gap), w, h);

            bool hover = !SettingsScreen.Open && r.Contains(mouse);
            if (hover) { _sel = i; PointerOverMenu = true; }
            bool on = i == _sel;

            // Active fill bar.
            if (on) Fill(r, GeoPalette.WithAlpha(GeoPalette.Signal, a));

            // Marker square.
            float mk = on ? 20f * s : 14f * s;
            Fill(new Rect(r.x + 12f * s, r.y + (h - mk) * 0.5f, mk, mk),
                 GeoPalette.WithAlpha(on ? GeoPalette.Gold : GeoPalette.Signal, a));

            // Label.
            _item.normal.textColor = GeoPalette.WithAlpha(on ? GeoPalette.Paper : GeoPalette.Ink, a);
            GUI.Label(new Rect(r.x + 42f * s, r.y, w - 42f * s, h), Items[i], _item);

            // Chevron on the active row.
            if (on)
            {
                _item.normal.textColor = GeoPalette.WithAlpha(GeoPalette.Paper, a);
                GUI.Label(new Rect(r.xMax - 30f * s, r.y, 24f * s, h), "›", _item);
            }

            if (!SettingsScreen.Open && GUI.Button(r, GUIContent.none, GUIStyle.none)) Activate(i);
        }
    }

    void DrawFooter(float s, float t)
    {
        float a = Appear(t, Items.Length + 1);
        _small.normal.textColor = GeoPalette.WithAlpha(new Color(0.37f, 0.37f, 0.35f), a);
        GUI.Label(new Rect(50f * s, Screen.height - 42f * s, 800f * s, 24f * s), tagline, _small);

        _mono.normal.textColor = GeoPalette.WithAlpha(new Color(0.37f, 0.37f, 0.35f), a);
        GUI.Label(new Rect(Screen.width - 340f * s, Screen.height - 42f * s, 320f * s, 24f * s),
                  version, _mono);
    }

    void DrawToast(float s)
    {
        if (Time.unscaledTime > _toastUntil || string.IsNullOrEmpty(_toast)) return;
        float w = 360f * s, h = 44f * s;
        var r = new Rect((Screen.width - w) * 0.5f, Screen.height - 120f * s, w, h);
        Fill(r, GeoPalette.Ink);
        Fill(new Rect(r.x, r.y, 6f * s, h), GeoPalette.Signal);
        _small.normal.textColor = GeoPalette.Paper;
        var prev = _small.alignment; _small.alignment = TextAnchor.MiddleCenter;
        GUI.Label(r, _toast, _small);
        _small.alignment = prev;
    }

    // ── Actions ────────────────────────────────────────────────────────────────

    void HandleKeys()
    {
        if (SettingsScreen.Open) return;   // settings overlay is modal
        var e = Event.current;
        if (e == null || e.type != EventType.KeyDown) return;
        switch (e.keyCode)
        {
            case KeyCode.DownArrow: case KeyCode.S: _sel = (_sel + 1) % Items.Length; e.Use(); break;
            case KeyCode.UpArrow:   case KeyCode.W: _sel = (_sel + Items.Length - 1) % Items.Length; e.Use(); break;
            case KeyCode.Return: case KeyCode.KeypadEnter: case KeyCode.Space:
                Activate(_sel); e.Use(); break;
        }
    }

    void Activate(int i)
    {
        switch (i)
        {
            case 0: StartCoroutine(ExitTo(levelSelectScene, false)); break;
            case 1: StartCoroutine(ExitTo(gameplayScene,    true));  break;
            case 2: Toast("TECH TREE — COMING SOON"); break;
            case 3: SettingsScreen.Open = true; break;
            case 4: Quit(); break;
        }
    }

    // Shatter the geometry, then load (a short beat so the burst reads).
    IEnumerator ExitTo(string scene, bool endless)
    {
        TitleStage.Instance?.Shatter();
        yield return new WaitForSecondsRealtime(0.45f);
        if (endless) RunConfig.SetEndless();
        LoadScene(scene);
    }

    void Toast(string msg) { _toast = msg; _toastUntil = Time.unscaledTime + 2f; }

    void LoadScene(string scene)
    {
        if (!string.IsNullOrEmpty(scene) && Application.CanStreamedLevelBeLoaded(scene))
            SceneManager.LoadScene(scene);
        else
            Toast($"SCENE '{scene}' NOT IN BUILD SETTINGS");
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ── Easing / draw helpers ───────────────────────────────────────────────────

    float Appear(float t, int index) =>
        Mathf.Clamp01((t - index * introStagger) / Mathf.Max(0.01f, introDur));

    static float EaseOutBack(float x)
    {
        x = Mathf.Clamp01(x);
        const float c1 = 1.70158f, c3 = 2.70158f;
        float p = x - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
    }

    static void Fill(Rect r, Color c)
    {
        Color p = GUI.color; GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = p;
    }

    static void RotatedFill(Rect r, Color c, float angle, Vector2 pivot)
    {
        Matrix4x4 m = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, pivot);
        Fill(r, c);
        GUI.matrix = m;
    }

    void BuildStyles(float s)
    {
        if (_builtScale == s && _logo != null) return;
        _builtScale = s;

        _logo = new GUIStyle { alignment = TextAnchor.UpperLeft, fontStyle = FontStyle.Bold,
                               fontSize = Mathf.RoundToInt(62f * s) };
        _item = new GUIStyle { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold,
                               fontSize = Mathf.RoundToInt(24f * s) };
        _small = new GUIStyle { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold,
                                fontSize = Mathf.RoundToInt(14f * s) };
        _mono = new GUIStyle(_small) { alignment = TextAnchor.MiddleRight };

        if (font != null) { _logo.font = font; _item.font = font; _small.font = font; _mono.font = font; }
    }
}
