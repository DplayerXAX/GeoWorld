using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// ═════════════════════════════════════════════════════════════════════════════
// Input gate
// ═════════════════════════════════════════════════════════════════════════════
//
// What the roguelite's overlays tell the rest of the game: "a click here is mine,
// not the board's", and "something modal is open — do not start the wave under it".
public static class RogueUiGate
{
    // A choice, or a targeting reticle, owns the player's attention.
    public static bool ModalOpen => UpgradeChoiceUI.Open || RogueTargeting.Active || RogueVictoryScreen.Open;

    public static bool BlocksWorld(Vector2 screenPos) =>
        ModalOpen || WaveIntelHud.IsPointerOver(screenPos) || RogueRelicBar.IsPointerOver(screenPos)
                  || BattleReportPanel.IsPointerOver(screenPos);

    public static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        new GameObject("EventSystem",
            typeof(EventSystem),
            typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
    }

    // ── Shared UI building blocks ────────────────────────────────────────────
    public static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    public static TMP_Text NewText(string name, Transform parent, float size, Color color,
                                   FontStyles style = FontStyles.Normal,
                                   TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize = size; t.color = color; t.fontStyle = style; t.alignment = align;
        t.raycastTarget = false; t.richText = true;
        t.textWrappingMode = TextWrappingModes.Normal;
        return t;
    }

    public static Canvas NewCanvas(string name, Transform parent, int order, out CanvasGroup group)
    {
        var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(parent, false);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        var sc = go.GetComponent<CanvasScaler>();
        sc.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight  = 0.5f;
        group = go.AddComponent<CanvasGroup>();
        EnsureEventSystem();
        return canvas;
    }

    public static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// Targeting (Nuke)
// ═════════════════════════════════════════════════════════════════════════════
//
// A reticle that follows the cursor over the board: a ring of the blast's size on
// the ground under it. Click to confirm, Esc or right-click to cancel. While it is up
// the board ignores clicks (RogueUiGate), and for the frame after, so the click that
// confirms is not also a placement.
public class RogueTargeting : MonoBehaviour
{
    static RogueTargeting _inst;
    static int _closedFrame = -10;

    public static bool Active => (_inst != null && _inst._open) || Time.frameCount <= _closedFrame;

    float _radius;
    string _hint;
    System.Action<Vector3Int> _onConfirm;
    bool _open;
    LineRenderer _ring;
    Vector3Int _cell;
    bool _have;
    int _openedFrame;

    public static void Begin(float radiusCells, string hint, System.Action<Vector3Int> onConfirm)
    {
        if (_inst == null)
        {
            var go = new GameObject("RogueTargeting");
            _inst = go.AddComponent<RogueTargeting>();
        }
        _inst.Open(radiusCells, hint, onConfirm);
    }

    void Open(float radiusCells, string hint, System.Action<Vector3Int> onConfirm)
    {
        _radius = radiusCells; _hint = hint; _onConfirm = onConfirm;
        _open = true; _have = false; _openedFrame = Time.frameCount;

        if (_ring == null)
        {
            var go = new GameObject("Reticle");
            go.transform.SetParent(transform, false);
            _ring = go.AddComponent<LineRenderer>();
            _ring.useWorldSpace = true;
            _ring.loop = true;
            _ring.positionCount = 48;
            _ring.startWidth = _ring.endWidth = 0.09f;
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            _ring.sharedMaterial = new Material(sh) { name = "Reticle" };
            if (_ring.sharedMaterial.HasProperty("_BaseColor")) _ring.sharedMaterial.SetColor("_BaseColor", new Color(1f, 0.35f, 0.15f, 1f));
            _ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        _ring.enabled = true;
    }

    void Close()
    {
        _open = false;
        _closedFrame = Time.frameCount;
        if (_ring != null) _ring.enabled = false;
    }

    void Update()
    {
        if (!_open) return;

        var grid = GridSystem.instance;
        var cam  = Camera.main;
        if (grid == null || cam == null) { Close(); return; }

        // Where the cursor lands: the first thing its ray hits, or the build's floor.
        Vector2 sp = VirtualCursor.Position;
        Ray ray = cam.ScreenPointToRay(sp);
        float cs = grid.cellSize;
        Vector3 point;
        if (Physics.Raycast(ray, out var hit, 500f, ~0, QueryTriggerInteraction.Ignore))
            point = hit.point - ray.direction * (cs * 0.05f);
        else
        {
            float floorY = grid.GridToWorld(Vector3Int.zero).y;
            float t = Mathf.Abs(ray.direction.y) > 1e-4f ? (floorY - ray.origin.y) / ray.direction.y : 0f;
            point = t > 0f ? ray.origin + ray.direction * t : ray.origin + ray.direction * 10f;
        }
        _cell = grid.WorldToGrid(point);
        _have = true;

        // The ring, flat at the cell's own height.
        Vector3 c = grid.GridToWorld(_cell);
        float r = _radius * cs;
        for (int i = 0; i < _ring.positionCount; i++)
        {
            float a = i / (float)_ring.positionCount * Mathf.PI * 2f;
            _ring.SetPosition(i, c + new Vector3(Mathf.Cos(a) * r, 0.05f, Mathf.Sin(a) * r));
        }
        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 8f);
        _ring.startWidth = _ring.endWidth = 0.06f + 0.05f * pulse;

        if (Time.frameCount == _openedFrame) return;   // the click that armed it is not the confirm

        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
        {
            Close();
            return;
        }
        if (Input.GetMouseButtonDown(0) && _have)
        {
            var cb = _onConfirm;
            var cell = _cell;
            Close();
            cb?.Invoke(cell);
        }
    }

    void OnGUI()
    {
        if (!_open) return;
        var style = new GUIStyle(GUI.skin.box) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
        style.normal.textColor = Color.white;
        var size = style.CalcSize(new GUIContent(_hint));
        GUI.Box(new Rect((Screen.width - size.x - 28f) * 0.5f, Screen.height * 0.12f, size.x + 28f, size.y + 14f), _hint, style);
    }

    void OnDestroy()
    {
        if (_inst == this) _inst = null;
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// Relic bar
// ═════════════════════════════════════════════════════════════════════════════
//
// The relics you hold, top-left, as small badges: rarity-coloured, with the relic's
// initials, and under it whatever it has to say (READY, 3/5, spent). Hover for the
// card; click an active one to use it.
[DisallowMultipleComponent]
public class RogueRelicBar : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        TrySpawn();
    }

    static void OnLoaded(Scene s, LoadSceneMode m) => TrySpawn();

    static void TrySpawn()
    {
        if (RunConfig.Mode != GameMode.Rogue) return;
        if (PlacementController.Instance == null) return;
        if (FindFirstObjectByType<RogueRelicBar>() != null) return;
        new GameObject("RogueRelicBar").AddComponent<RogueRelicBar>();
    }

    static RectTransform _gridRt;
    static RectTransform _tipRt;
    static bool _shown;

    public static bool IsPointerOver(Vector2 p) =>
        _shown && _gridRt != null && RectTransformUtility.RectangleContainsScreenPoint(_gridRt, p, null);

    class Badge
    {
        public ItemEffectInstance inst;
        public Image bg, ring;
        public TMP_Text status;
        public RectTransform rt;
    }

    readonly List<Badge> _badges = new();
    CanvasGroup _group;
    TMP_Text _tipName, _tipBody;
    Image _tipBg;
    float _refresh;
    bool _dirty = true;

    void Start()
    {
        var canvas = RogueUiGate.NewCanvas("RogueRelicCanvas", transform, 84, out _group);
        _group.alpha = 0f;

        _gridRt = RogueUiGate.NewRect("Relics", canvas.transform);
        _gridRt.anchorMin = _gridRt.anchorMax = _gridRt.pivot = new Vector2(0f, 1f);
        _gridRt.anchoredPosition = new Vector2(16f, -16f);
        var grid = _gridRt.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(46f, 56f);
        grid.spacing  = new Vector2(6f, 6f);
        grid.startAxis = GridLayoutGroup.Axis.Vertical;
        grid.constraint = GridLayoutGroup.Constraint.FixedRowCount;
        grid.constraintCount = 12;
        var fit = _gridRt.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // The tooltip, a child of the canvas rather than of a badge so it is never clipped.
        _tipRt = RogueUiGate.NewRect("Tip", canvas.transform);
        _tipRt.pivot = new Vector2(0f, 1f);
        _tipRt.sizeDelta = new Vector2(360f, 120f);
        _tipBg = _tipRt.gameObject.AddComponent<Image>();
        _tipBg.sprite = UIRoundedRect.Get(14);
        _tipBg.type = Image.Type.Sliced;
        _tipBg.color = new Color(0.949f, 0.937f, 0.902f, 0.97f);
        _tipBg.raycastTarget = false;

        _tipName = RogueUiGate.NewText("Name", _tipRt, 26f, GeoPalette.Ink, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        var nrt = _tipName.rectTransform;
        nrt.anchorMin = new Vector2(0f, 1f); nrt.anchorMax = new Vector2(1f, 1f); nrt.pivot = new Vector2(0.5f, 1f);
        nrt.offsetMin = new Vector2(16f, -48f); nrt.offsetMax = new Vector2(-16f, -12f);

        _tipBody = RogueUiGate.NewText("Body", _tipRt, 20f, GeoPalette.Ink, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        var brt = _tipBody.rectTransform;
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
        brt.offsetMin = new Vector2(16f, 12f); brt.offsetMax = new Vector2(-16f, -52f);
        _tipRt.gameObject.SetActive(false);

        RogueRun.RelicsChanged += MarkDirty;
    }

    void OnDestroy()
    {
        RogueRun.RelicsChanged -= MarkDirty;
        _shown = false;
        _gridRt = null; _tipRt = null;
    }

    void MarkDirty() => _dirty = true;

    void Update()
    {
        if (_group == null) return;

        bool show = RunConfig.Mode == GameMode.Rogue && !IntroDirector.Playing
                 && !SettingsScreen.Open && !GameFlowManager.SettlementUp;
        _group.alpha = Mathf.MoveTowards(_group.alpha, show ? 1f : 0f, Time.unscaledDeltaTime * 6f);
        _group.blocksRaycasts = show;
        _shown = show && _group.alpha > 0.5f;
        if (!show) { HideTip(); return; }

        if (_dirty) Rebuild();

        _refresh -= Time.unscaledDeltaTime;
        if (_refresh <= 0f) { _refresh = 0.2f; Refresh(); }
    }

    void Rebuild()
    {
        _dirty = false;
        HideTip();
        foreach (var b in _badges) if (b.rt != null) Destroy(b.rt.gameObject);
        _badges.Clear();

        var um = UpgradeManager.Instance;
        if (um == null) return;
        foreach (var inst in um.Instances)
        {
            if (inst == null || inst.Card == null) continue;
            _badges.Add(MakeBadge(inst));
        }
        Refresh();
    }

    Badge MakeBadge(ItemEffectInstance inst)
    {
        var card = inst.Card;
        var rt = RogueUiGate.NewRect("Relic", _gridRt);

        var ring = rt.gameObject.AddComponent<Image>();           // category-coloured frame
        ring.sprite = UIRoundedRect.Get(10); ring.type = Image.Type.Sliced;
        ring.color = RogueItems.ColorOf(card.category);

        var inner = RogueUiGate.NewRect("Inner", rt);
        inner.anchorMin = Vector2.zero; inner.anchorMax = Vector2.one;
        inner.offsetMin = new Vector2(3f, 3f); inner.offsetMax = new Vector2(-3f, -3f);
        var bg = inner.gameObject.AddComponent<Image>();
        bg.sprite = UIRoundedRect.Get(8); bg.type = Image.Type.Sliced;
        bg.color = Color.Lerp(RogueItems.ColorOf(card.rarity), Color.black, 0.15f);
        bg.raycastTarget = false;

        var init = RogueUiGate.NewText("Initials", inner, 22f, Color.white, FontStyles.Bold);
        init.text = Initials(card.displayName);
        var irt = init.rectTransform;
        irt.anchorMin = new Vector2(0f, 0.3f); irt.anchorMax = Vector2.one;
        irt.offsetMin = irt.offsetMax = Vector2.zero;

        var st = RogueUiGate.NewText("Status", inner, 11f, new Color(1f, 1f, 1f, 0.9f), FontStyles.Bold);
        var srt = st.rectTransform;
        srt.anchorMin = Vector2.zero; srt.anchorMax = new Vector2(1f, 0.32f);
        srt.offsetMin = srt.offsetMax = Vector2.zero;
        st.textWrappingMode = TextWrappingModes.NoWrap;

        var hover = rt.gameObject.AddComponent<RelicHover>();
        hover.bar = this; hover.inst = inst;
        ring.raycastTarget = true;

        return new Badge { inst = inst, bg = bg, ring = ring, status = st, rt = rt };
    }

    static string Initials(string name)
    {
        if (string.IsNullOrEmpty(name)) return "?";
        var parts = name.Split(' ');
        if (parts.Length >= 2) return ("" + char.ToUpper(parts[0][0]) + char.ToUpper(parts[1][0]));
        return name.Length >= 2 ? ("" + char.ToUpper(name[0]) + char.ToLower(name[1])) : name.ToUpper();
    }

    void Refresh()
    {
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
        foreach (var b in _badges)
        {
            if (b.inst == null) continue;
            b.status.text = b.inst.Status ?? "";
            bool usable = b.inst.HasActive && b.inst.CanUse;
            // A usable active relic breathes; a spent one goes dull.
            var col = Color.Lerp(RogueItems.ColorOf(b.inst.Card.rarity), Color.black, 0.15f);
            if (b.inst.HasActive && !b.inst.CanUse && b.inst.Status == "spent") col = Color.Lerp(col, new Color(0.2f, 0.2f, 0.2f), 0.7f);
            if (usable) col = Color.Lerp(col, Color.white, 0.25f + 0.25f * pulse);
            b.bg.color = col;
        }
    }

    // ── Tooltip ──────────────────────────────────────────────────────────────

    public void ShowTip(ItemEffectInstance inst, RectTransform at)
    {
        if (inst?.Card == null || _tipRt == null) return;
        var c = inst.Card;

        string rarity = c.rarity.ToString().ToUpper();
        string hex = ColorUtility.ToHtmlStringRGB(RogueItems.ColorOf(c.rarity));
        string cat = ColorUtility.ToHtmlStringRGB(RogueItems.ColorOf(c.category));
        _tipName.text = $"{c.displayName}   <size=60%><color=#{hex}>{rarity}</color> · <color=#{cat}>{RogueItems.NameOf(c.category)}</color></size>";
        string use = inst.HasActive ? (inst.CanUse ? "\n<color=#C2561B><b>Click to use.</b></color>" : "") : "";
        _tipBody.text = c.description + use;

        _tipBody.ForceMeshUpdate();
        float h = Mathf.Max(90f, _tipBody.preferredHeight + 70f);
        _tipRt.sizeDelta = new Vector2(360f, h);

        _tipRt.gameObject.SetActive(true);
        Vector3 p = at.position;
        _tipRt.position = new Vector3(p.x + 56f * (Screen.width / 1920f), p.y + 28f * (Screen.height / 1080f), 0f);
    }

    public void HideTip()
    {
        if (_tipRt != null) _tipRt.gameObject.SetActive(false);
    }

    public void Click(ItemEffectInstance inst)
    {
        if (inst == null || !inst.HasActive || !inst.CanUse) return;
        inst.Use(GameFlowManager.Instance);
        _dirty = true;
    }

    class RelicHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public RogueRelicBar bar;
        public ItemEffectInstance inst;
        public void OnPointerEnter(PointerEventData e) => bar.ShowTip(inst, (RectTransform)transform);
        public void OnPointerExit(PointerEventData e)  => bar.HideTip();
        public void OnPointerClick(PointerEventData e) => bar.Click(inst);
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// Victory
// ═════════════════════════════════════════════════════════════════════════════
public class RogueVictoryScreen : MonoBehaviour
{
    static RogueVictoryScreen _inst;
    public static bool Open => _inst != null;

    public static void Show(int wavesCleared, int bestBefore)
    {
        if (_inst != null) return;
        var go = new GameObject("RogueVictoryScreen");
        _inst = go.AddComponent<RogueVictoryScreen>();
        _inst.Build(wavesCleared, bestBefore);
    }

    void Build(int cleared, int bestBefore)
    {
        var canvas = RogueUiGate.NewCanvas("RogueVictoryCanvas", transform, 4500, out _);

        var dim = RogueUiGate.NewRect("Dim", canvas.transform);
        RogueUiGate.Stretch(dim);
        var di = dim.gameObject.AddComponent<Image>();
        di.color = new Color(0.04f, 0.05f, 0.09f, 0.82f);

        var title = RogueUiGate.NewText("Title", canvas.transform, 110f, GeoPalette.Gold, FontStyles.Bold);
        title.text = "RUN COMPLETE";
        Place(title.rectTransform, new Vector2(0f, 300f), new Vector2(1500f, 150f));

        var sub = RogueUiGate.NewText("Sub", canvas.transform, 32f, GeoPalette.Paper);
        sub.text = RogueRun.SummaryLine(cleared) + $"   ·   {RunStats.Kills} kills   ·   {RogueRun.BossesDefeated} boss{(RogueRun.BossesDefeated == 1 ? "" : "es")}";
        Place(sub.rectTransform, new Vector2(0f, 200f), new Vector2(1700f, 60f));

        // The relics you won with.
        var sb = new System.Text.StringBuilder();
        var um = UpgradeManager.Instance;
        if (um != null)
            foreach (var c in um.Owned)
            {
                if (c == null) continue;
                string hex = ColorUtility.ToHtmlStringRGB(RogueItems.ColorOf(c.rarity));
                sb.Append($"<color=#{hex}>{c.displayName}</color>   ");
            }
        var relics = RogueUiGate.NewText("Relics", canvas.transform, 26f, GeoPalette.Paper);
        relics.text = sb.Length > 0 ? sb.ToString() : "No relics.";
        Place(relics.rectTransform, new Vector2(0f, 60f), new Vector2(1500f, 220f));

        // New relics unlocked by this run's reach.
        var fresh = RogueItems.NewlyUnlocked(bestBefore, cleared);
        if (fresh.Count > 0)
        {
            var u = RogueUiGate.NewText("Unlocked", canvas.transform, 26f, GeoPalette.Gold, FontStyles.Bold);
            var names = new List<string>();
            foreach (var c in fresh) names.Add(c.displayName);
            u.text = "NEW RELICS UNLOCKED:  " + string.Join(", ", names);
            Place(u.rectTransform, new Vector2(0f, -130f), new Vector2(1600f, 70f));
        }

        Button("CONTINUE  (endless)", new Vector2(-190f, -260f), canvas.transform, GeoPalette.Paper, Close);
        Button("TITLE", new Vector2(190f, -260f), canvas.transform, GeoPalette.Gold, () =>
        {
            Time.timeScale = 1f;
            LoadingScreen.Go("Title");
        });
    }

    static void Place(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static void Button(string label, Vector2 pos, Transform parent, Color color, UnityEngine.Events.UnityAction onClick)
    {
        var rt = RogueUiGate.NewRect("Button", parent);
        Place(rt, pos, new Vector2(340f, 76f));
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = UIRoundedRect.Get(14); img.type = Image.Type.Sliced; img.color = color;
        var b = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
        b.targetGraphic = img;
        b.onClick.AddListener(onClick);
        var t = RogueUiGate.NewText("Label", rt, 28f, GeoPalette.Ink, FontStyles.Bold);
        t.text = label;
        RogueUiGate.Stretch(t.rectTransform);
    }

    void Close()
    {
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (_inst == this) _inst = null;
    }
}
