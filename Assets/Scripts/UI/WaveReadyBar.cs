using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Who has pressed Space, during a multiplayer build phase.
//
// Without it the wave gate is invisible: you press Space, nothing happens, and there
// is no way to tell whether the input was eaten or you are waiting on somebody. One
// pip per connected player, in their own colour, lit when they are ready.
//
// Hidden in single player. Not because the gate works differently there — it does not,
// the same command and the same AllReady check run — but because a solo player pressing
// Space starts the wave in the same frame, so the readout would never be on screen
// long enough to read and would only add furniture to the HUD.
[DisallowMultipleComponent]
public class WaveReadyBar : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Spawn()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TrySpawn();
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m) => TrySpawn();

    static void TrySpawn()
    {
        if (GameFlowManager.Instance == null) return;              // gameplay scene only
        if (FindFirstObjectByType<WaveReadyBar>() != null) return;
        new GameObject("WaveReadyBar").AddComponent<WaveReadyBar>();
    }

    const float PipSize = 22f;
    const float PipGap  = 12f;

    Canvas      _canvas;
    CanvasGroup _group;
    TMP_Text    _label;
    readonly Image[] _pips = new Image[MultiplayerSession.MaxPlayers];

    float _shown;

    void Start() => Build();

    void Update()
    {
        var flow = GameFlowManager.Instance;
        bool want = flow != null
                 && MultiplayerSession.ConnectedCount > 1
                 && (flow.phase == GamePhase.Build || flow.phase == GamePhase.ReadyToRun);

        _shown = Mathf.MoveTowards(_shown, want ? 1f : 0f, Time.unscaledDeltaTime * 5f);
        _group.alpha = _shown;
        if (_shown <= 0.001f) return;

        for (int i = 0; i < MultiplayerSession.MaxPlayers; i++)
        {
            var p = MultiplayerSession.Get(i);
            bool on = p != null && p.connected;
            _pips[i].gameObject.SetActive(on);
            if (!on) continue;

            var c = MultiplayerSession.ColorOf(i);
            // Unready reads as the same colour drained rather than as grey, so the pip
            // still says WHOSE it is while it is waiting.
            _pips[i].color = p.ready ? c : new Color(c.r, c.g, c.b, 0.22f);
        }

        int ready = MultiplayerSession.ReadyCount, total = MultiplayerSession.ConnectedCount;
        bool meReady = MultiplayerSession.Get(MultiplayerSession.LocalId)?.ready ?? false;
        _label.text = ready >= total
            ? "starting…"
            : meReady ? $"{ready} / {total} ready   ·   Space to cancel"
                      : $"{ready} / {total} ready   ·   Space when you are";
    }

    void Build()
    {
        var go = new GameObject("WaveReadyCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        _canvas = go.GetComponent<Canvas>();
        _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        // Above the wave progress bar (45) and below the shop bars (55), so it shares
        // the HUD's existing stacking rather than inventing a new top layer.
        _canvas.sortingOrder = 50;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        _group = go.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable   = false;

        var root = NewRect("ReadyRow", go.transform);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 1f);
        root.pivot = new Vector2(0.5f, 1f);
        root.anchoredPosition = new Vector2(0f, -118f);
        root.sizeDelta = new Vector2(520f, 62f);

        float total = MultiplayerSession.MaxPlayers * PipSize + (MultiplayerSession.MaxPlayers - 1) * PipGap;
        for (int i = 0; i < MultiplayerSession.MaxPlayers; i++)
        {
            var pip = NewRect($"Pip{i}", root);
            pip.anchorMin = pip.anchorMax = new Vector2(0.5f, 1f);
            pip.pivot = new Vector2(0.5f, 0.5f);
            pip.anchoredPosition = new Vector2(-total * 0.5f + PipSize * 0.5f + i * (PipSize + PipGap), -14f);
            pip.sizeDelta = new Vector2(PipSize, PipSize);
            _pips[i] = pip.gameObject.AddComponent<Image>();
            _pips[i].raycastTarget = false;
        }

        _label = NewText("Label", root, 20f, GeoPalette.WithAlpha(GeoPalette.Paper, 0.75f));
        _label.rectTransform.anchorMin = _label.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        _label.rectTransform.pivot = new Vector2(0.5f, 1f);
        _label.rectTransform.anchoredPosition = new Vector2(0f, -32f);
        _label.rectTransform.sizeDelta = new Vector2(520f, 28f);
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static TMP_Text NewText(string name, Transform parent, float size, Color color)
    {
        var rt = NewRect(name, parent);
        var t  = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize = size; t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        return t;
    }
}
