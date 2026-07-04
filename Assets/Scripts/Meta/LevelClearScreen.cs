using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Level-clear settlement, built at runtime (no scene setup). On a level clear:
//   • slides the player's built blocks to the LEFT half of the screen,
//   • fires a calm "harmony" skybox reaction,
//   • lists the result on the RIGHT (stars, score, run stats) + a Continue button.
// Spawned by GameFlowManager; the Continue button runs the supplied callback
// (return to the map).
public class LevelClearScreen : MonoBehaviour
{
    Action _onContinue;

    // stars/score are precomputed by GameFlowManager via RunStats (single source of
    // truth for the formula, shared with the Endless settlement path). prevBest/isNewBest
    // are captured BEFORE SaveSystem.RecordClear runs, since that call already bumps the
    // saved best — reading it back here would always look like a tie, never "NEW!".
    public static void Show(LevelDefinition level, int wavesCleared, int lives, int maxLives,
                            int stars, int score, int prevBest, bool isNewBest,
                            bool objectivesMet, Action onContinue)
    {
        var go = new GameObject("LevelClearScreen");
        go.AddComponent<LevelClearScreen>().Begin(level, wavesCleared, lives, maxLives, stars, score, prevBest, isNewBest, objectivesMet, onContinue);
    }

    void Begin(LevelDefinition level, int wavesCleared, int lives, int maxLives,
               int stars, int score, int prevBest, bool isNewBest, bool objectivesMet, Action onContinue)
    {
        _onContinue = onContinue;

        Debug.Log($"[LevelClear] settlement shown — waves {wavesCleared}, lives {lives}/{maxLives}, stars {stars}, score {score}");

        // Build the panel FIRST so it always shows even if the camera/skybox steps
        // throw for any reason.
        BuildUI(level, wavesCleared, lives, stars, score, prevBest, isNewBest);
        try { FrameBlocksLeft(); } catch (Exception e) { Debug.LogWarning("[LevelClear] camera step skipped: " + e.Message); }
        try { HarmonyReaction(); } catch (Exception e) { Debug.LogWarning("[LevelClear] skybox step skipped: " + e.Message); }
    }

    // ── Camera: push the build to the left half ──────────────────────────────
    void FrameBlocksLeft()
    {
        var grid = GridSystem.instance;
        var orbit = FindFirstObjectByType<OrbitCamera>();
        if (grid == null || orbit == null) return;

        Vector3 sum = Vector3.zero; int n = 0;
        foreach (var ins in grid.GetAllInstances())
            if (ins?.visualObject != null) { sum += ins.visualObject.transform.position; n++; }
        Vector3 centre = n > 0 ? sum / n : Vector3.zero;

        orbit.focusViewport = new Vector2(0.28f, 0.5f);   // bias the build to the left
        orbit.FocusOnPoint(centre, snap: false);          // smooth glide (added earlier)
        orbit.SetZoom(orbit.useOrthographic ? 7.5f : 16f); // pull back a touch
    }

    // ── Skybox: calm green "harmony" wash ────────────────────────────────────
    void HarmonyReaction()
    {
        var bg = BackgroundReactor.Instance;
        if (bg == null) return;
        // The skybox reorganises into ordered crystalline geometry via _ClearReact,
        // which BackgroundReactor ramps up while the settlement (SettlementUp) is on.
        // Here we just settle the calm base.
        bg.SetCombatMode(false);
        bg.SetMusicIntensity(0.4f);
    }

    // ── UI ────────────────────────────────────────────────────────────────────
    void BuildUI(LevelDefinition level, int wavesCleared, int lives, int stars, int score, int prevBest, bool isNewBest)
    {
        var go = new GameObject("ClearCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 900;   // above all gameplay UGUI (below the intro at 1000)
        var sc = go.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;
        EnsureEventSystem();

        // Right-side panel.
        var panel = NewRect("Panel", go.transform);
        panel.anchorMin = new Vector2(1f, 0.5f); panel.anchorMax = new Vector2(1f, 0.5f);
        panel.pivot     = new Vector2(1f, 0.5f);
        panel.sizeDelta = new Vector2(640f, 900f);
        panel.anchoredPosition = new Vector2(-70f, 0f);
        var bg = panel.gameObject.AddComponent<Image>();
        bg.color  = new Color(0.949f, 0.937f, 0.902f, 0.96f);   // paper
        bg.sprite = UIRoundedRect.Get(28);
        bg.type   = Image.Type.Sliced;

        var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(48, 48, 44, 40);
        vlg.spacing = 18f;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperCenter;

        Title(panel, "LEVEL CLEAR", 52f, GeoPalette.Ink, FontStyles.Bold);
        Title(panel, level != null ? level.displayName : "", 26f, GeoPalette.Blue, FontStyles.Italic);

        BuildStars(panel, stars);

        Title(panel, $"<size=30>SCORE</size>\n<size=64><b>{score}</b></size>", 30f, GeoPalette.Signal, FontStyles.Normal);

        StatRow(panel, "Waves Cleared", wavesCleared.ToString());
        StatRow(panel, "Lives Left",    lives.ToString());
        StatRow(panel, "Enemies Killed", RunStats.Kills.ToString());
        StatRow(panel, "Time",          FormatTime(RunStats.ElapsedTime));
        StatRow(panel, "Stars",         stars + " / 3");

        if (isNewBest) StatRow(panel, "Best Score", "NEW!");
        else if (prevBest > 0) StatRow(panel, "Best Score", prevBest.ToString());

        // spacer
        var spacer = NewRect("Spacer", panel); spacer.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

        var continueBtn = BuildButton(panel, "Return to Map", () =>
        {
            var cb = _onContinue; _onContinue = null;
            Destroy(gameObject);
            cb?.Invoke();
        });

        // Gamepad: focus the only actionable button the instant the screen appears.
        UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(continueBtn.gameObject);
    }

    void BuildStars(RectTransform parent, int stars)
    {
        var row = NewRect("Stars", parent);
        var le = row.gameObject.AddComponent<LayoutElement>(); le.minHeight = 96f;
        var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 20f; hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

        for (int i = 0; i < 3; i++)
        {
            bool on = i < stars;
            var pip = NewRect("Star", row);
            var ple = pip.gameObject.AddComponent<LayoutElement>(); ple.preferredWidth = ple.preferredHeight = on ? 92f : 72f;
            var img = pip.gameObject.AddComponent<Image>();
            img.sprite = UIRoundedRect.Get(40);
            img.type   = Image.Type.Sliced;
            img.color  = on ? GeoPalette.Gold : new Color(0.7f, 0.68f, 0.63f, 0.5f);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────
    static string FormatTime(float seconds)
    {
        int s = Mathf.Max(0, Mathf.RoundToInt(seconds));
        return $"{s / 60}:{s % 60:00}";
    }

    TMP_Text Title(RectTransform parent, string text, float size, Color color, FontStyles style)
    {
        var t = NewText("Title", parent, size, color, style, TextAlignmentOptions.Center);
        t.text = text; t.richText = true; t.textWrappingMode = TextWrappingModes.Normal;
        return t;
    }

    void StatRow(RectTransform parent, string label, string value)
    {
        var row = NewRect("Row", parent);
        row.gameObject.AddComponent<LayoutElement>().minHeight = 44f;
        var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = false;

        var l = NewText("L", row, 26f, new Color(0.30f, 0.30f, 0.30f), FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        l.text = label;
        var v = NewText("V", row, 28f, GeoPalette.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        v.text = value;
    }

    Button BuildButton(RectTransform parent, string label, Action onClick)
    {
        var rt = NewRect("Button", parent);
        rt.gameObject.AddComponent<LayoutElement>().minHeight = 78f;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = UIRoundedRect.Get(22); img.type = Image.Type.Sliced;
        img.color  = GeoPalette.Signal;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors; colors.highlightedColor = GeoPalette.Gold; colors.pressedColor = GeoPalette.Blue; btn.colors = colors;
        btn.onClick.AddListener(() => onClick());

        var t = NewText("Label", rt, 30f, GeoPalette.Paper, FontStyles.Bold, TextAlignmentOptions.Center);
        t.text = label;
        var lrt = t.rectTransform; lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        return btn;
    }

    static void EnsureEventSystem()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null) return;
        new GameObject("EventSystem",
            typeof(UnityEngine.EventSystems.EventSystem),
            typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
    }

    RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    TMP_Text NewText(string name, Transform parent, float size, Color color, FontStyles style, TextAlignmentOptions align)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize = size; t.color = color; t.fontStyle = style; t.alignment = align;
        t.raycastTarget = false; t.richText = true;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }
}
