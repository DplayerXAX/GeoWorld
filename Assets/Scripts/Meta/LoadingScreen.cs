using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Loading page with a persistent spinning ◇ cube (the game's signature element).
// Auto-spawns once and persists. Call LoadingScreen.Go("scene") to fade in the
// overlay, async-load the scene, then drop it — so every transition shows the
// same spinning-cube loading screen.
[DisallowMultipleComponent]
public class LoadingScreen : MonoBehaviour
{
    static LoadingScreen _inst;
    bool     _active;
    GUIStyle _label;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Spawn()
    {
        if (_inst != null) return;
        var go = new GameObject("LoadingScreen");
        DontDestroyOnLoad(go);
        _inst = go.AddComponent<LoadingScreen>();
    }

    public static bool Active => _inst != null && _inst._active;

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
        _active = true;
        yield return null;                 // paint the overlay before the hitch
        var op = SceneManager.LoadSceneAsync(scene);
        while (op != null && !op.isDone) yield return null;
        float hold = 0.25f;                 // brief hold so the spinner reads on fast loads
        while (hold > 0f) { hold -= Time.unscaledDeltaTime; yield return null; }
        _active = false;
    }

    void OnGUI()
    {
        if (!_active) return;
        GUI.depth = -2000;                 // above all other IMGUI (incl. settings)
        float s = UiScale.Get();

        Color p = GUI.color;
        GUI.color = GeoPalette.Paper;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = p;
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
        Color p = GUI.color; GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = p;
    }
}
