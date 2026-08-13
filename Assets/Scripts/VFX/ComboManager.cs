using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Kill-combo tracking + feedback. Consecutive kills within `ComboWindow` seconds
// stack a combo count; each kill also pulses BackgroundReactor.TriggerKillReact
// (the skybox snapping back toward calm — mirror of TriggerDamageFlash's push
// toward chaos on damage taken), scaled by the current combo so bigger combos
// punch harder.
public static class ComboManager
{
    public static int Count { get; private set; }

    // Seconds since the last kill before the combo resets to 1.
    const float ComboWindow   = 2.2f;
    // Kept VERY low — this drives BackgroundReactor's skybox snap-back, which
    // subtracts from _CombatMode and so momentarily relaxes the ManifoldSkybox IFS
    // fold (the crystalline "space fractures" warp) as well as the calm/combat
    // colour blend. Because the fold is geometric, even a small `cm` dip visibly
    // twists the whole sky — which read as the sky lurching on every kill. Dropped
    // 5× from the previous 0.02/0.006/0.1 so a kill is a barely-there shimmer
    // instead of a distortion; raise gradually if it now reads as no feedback.
    const float BaseReact     = 0.004f;
    const float ReactPerStack = 0.0012f;
    const float MaxReact      = 0.02f;

    static float _lastKillTime = -999f;

    public static void RegisterKill()
    {
        float now = Time.unscaledTime;
        Count = (now - _lastKillTime <= ComboWindow) ? Count + 1 : 1;
        _lastKillTime = now;

        float strength = Mathf.Min(BaseReact + (Count - 1) * ReactPerStack, MaxReact);
        BackgroundReactor.Instance?.TriggerKillReact(strength);

        ComboHud.Show(Count);
    }
}

// "COMBO x N" counter parked in the empty top-right corner of the HUD (top-left is
// already taken by TopLeftHUD's currency counters + LevelObjectivesTracker). Pure
// screen-space overlay, runtime-built, no prefab — same pattern as CurrencyFlyFx.
// Pops/punches on every increment, then fades out on its own if no new kill lands
// before ComboManager's window elapses.
static class ComboHud
{
    static ComboHudRunner _runner;

    static void EnsureBuilt()
    {
        if (_runner != null) return;
        var go = new GameObject("ComboHud");
        Object.DontDestroyOnLoad(go);
        _runner = go.AddComponent<ComboHudRunner>();
    }

    public static void Show(int count)
    {
        EnsureBuilt();
        _runner.Show(count);
    }
}

class ComboHudRunner : MonoBehaviour
{
    const float HideDelay  = 2.2f;   // mirrors ComboManager's ComboWindow
    const float FadeSpeed  = 6f;
    const float PunchScale = 1.25f;
    const float PunchSpeed = 10f;

    CanvasGroup   _cg;
    RectTransform _rt;
    TMP_Text      _text;

    float _hideTimer;
    float _scale = 1f;

    void Awake() => BuildUI();

    public void Show(int count)
    {
        _text.text = $"COMBO x{count}";
        _hideTimer = HideDelay;
        _scale = PunchScale;   // snap out, then ease back to 1 in Update
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        _hideTimer -= dt;
        float target = _hideTimer > 0f ? 1f : 0f;
        _cg.alpha = Mathf.MoveTowards(_cg.alpha, target, FadeSpeed * dt);

        _scale = Mathf.Lerp(_scale, 1f, 1f - Mathf.Exp(-PunchSpeed * dt));
        _rt.localScale = Vector3.one * _scale;
    }

    void BuildUI()
    {
        var canvasGO = new GameObject("ComboCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 91;   // just above HUD (90), below currency-fly (95)
        var sc = canvasGO.GetComponent<CanvasScaler>();
        sc.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight  = 1f;

        var textGo = new GameObject("ComboText", typeof(RectTransform));
        textGo.transform.SetParent(canvasGO.transform, false);
        _rt = (RectTransform)textGo.transform;
        // Top-right, but BELOW the pause / fast-forward / settings row that lives in
        // that corner — at -40 the combo text drew straight through those buttons.
        // (TopLeftHUD + LevelObjectivesTracker both live top-left, so the right edge
        // is still the free side.)
        _rt.anchorMin = _rt.anchorMax = _rt.pivot = new Vector2(1f, 1f);
        _rt.anchoredPosition = new Vector2(-32f, -128f);
        _rt.sizeDelta = new Vector2(420f, 80f);

        _text = textGo.AddComponent<TextMeshProUGUI>();
        _text.alignment = TextAlignmentOptions.MidlineRight;
        _text.fontStyle = FontStyles.Bold;
        _text.fontSize  = 40f;
        _text.color     = new Color(1f, 0.85f, 0.35f);
        _text.raycastTarget = false;
        _text.text = "";

        _cg = textGo.AddComponent<CanvasGroup>();
        _cg.alpha = 0f;
    }
}
