using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

// Hold Shift during combat to see every enemy's health, or hover one to read just
// that enemy — its bar plus a card listing what it actually does.
//
// On-demand rather than always-on because permanent bars over a whole wave turn
// the board into a wall of floating UI and bury the thing they sit on top of.
// Shift is already the game's "show me what's underneath" key (see PeekWorld),
// so this reads as the same gesture pointed at the enemies.
//
// Bars are pooled and screen-space: a world-space canvas per enemy would fight
// the camera for scale and get occluded by the blocks the enemies walk behind,
// which is exactly when you most want to read them.
[DisallowMultipleComponent]
public class EnemyHealthBars : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TrySpawn();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TrySpawn();

    static void TrySpawn()
    {
        if (FindFirstObjectByType<EnemyBaseManager>() == null) return;   // gameplay scene only
        if (FindFirstObjectByType<EnemyHealthBars>() != null) return;
        new GameObject("EnemyHealthBars").AddComponent<EnemyHealthBars>();
    }

    const float FadeSpeed  = 12f;    // per second — snappy, this answers a held key
    const float BarWidth   = 46f;    // reference (1080p) pixels
    const float BarHeight  = 6f;
    // Above the enemy's origin, in world units. Low enough to read as belonging to
    // the unit rather than floating free of it — at 0.85 the bars sat well clear of
    // their enemies and it took a second to work out which was whose.
    const float WorldLift  = 0.55f;

    static readonly Color TrackColor = new(0.05f, 0.05f, 0.05f, 0.72f);
    static readonly Color FullColor  = new(0.42f, 0.88f, 0.45f);
    static readonly Color HurtColor  = new(0.95f, 0.78f, 0.22f);
    static readonly Color LowColor   = new(0.92f, 0.24f, 0.20f);

    Canvas      _canvas;
    CanvasGroup _group;
    Camera      _cam;
    float       _alpha;

    readonly List<Bar> _pool = new();

    class Bar
    {
        public RectTransform root;
        public RectTransform fill;
        public Image         fillImage;
    }

    void Awake()
    {
        var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(transform, false);
        _canvas = go.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Under the HUD and every panel — these are a transient read-out, they
        // should never cover something the player can actually click.
        _canvas.sortingOrder = 50;

        var sc = go.GetComponent<CanvasScaler>();
        sc.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight  = 0.5f;

        _group = go.AddComponent<CanvasGroup>();
        _group.alpha          = 0f;
        _group.blocksRaycasts = false;
        _group.interactable   = false;
    }

    void LateUpdate()
    {
        // LateUpdate, not Update: enemies move in Update, and reading their
        // positions in the same frame they were written keeps the bars from
        // trailing a frame behind the thing they label.
        EnsureCamera();

        bool combat  = InCombat();
        bool peek    = combat && PeekWorld.Held;
        var  hovered = combat ? EnemyUnderCursor() : null;

        // Two ways in: Shift shows the whole board at once, hovering shows exactly
        // one. Either is enough to fade the layer in.
        bool want = peek || hovered != null;
        _alpha = Mathf.MoveTowards(_alpha, want ? 1f : 0f, FadeSpeed * Time.unscaledDeltaTime);
        _group.alpha = _alpha;

        if (_alpha <= 0.001f) { HideFrom(0); ShowTraits(null); return; }

        var mgr = EnemyBaseManager.Instance;
        if (_cam == null || mgr == null) { HideFrom(0); ShowTraits(null); return; }

        int used = 0;
        foreach (var e in mgr.ActiveEnemies)
        {
            if (e == null || e.CurrentHealth <= 0 || e.maxHealth <= 0) continue;
            // Not peeking → only the one under the cursor gets a bar. Drawing them
            // all would make the hover indistinguishable from the Shift view.
            if (!peek && e != hovered) continue;

            Vector3 sp = _cam.WorldToScreenPoint(e.transform.position + Vector3.up * WorldLift);
            if (sp.z <= 0f) continue;   // behind the camera — projects to a mirrored ghost position

            var bar = GetBar(used++);
            bar.root.position = sp;

            float frac = Mathf.Clamp01((float)e.CurrentHealth / e.maxHealth);
            bar.fill.anchorMax  = new Vector2(frac, 1f);
            bar.fillImage.color = frac > 0.6f ? FullColor : frac > 0.3f ? HurtColor : LowColor;
        }

        HideFrom(used);
        ShowTraits(hovered);
    }

    // OrbitCamera's own camera first — it's the rig that actually frames the board,
    // and Camera.main only agrees with it while the MainCamera tag happens to be on
    // the same object.
    void EnsureCamera()
    {
        if (_cam != null) return;
        var orbit = FindFirstObjectByType<OrbitCamera>();
        _cam = orbit != null && orbit.myCam != null ? orbit.myCam : Camera.main;
    }

    static bool InCombat()
    {
        var flow = GameFlowManager.Instance;
        return flow != null && flow.phase == GamePhase.Running;
    }

    // How close (in 1080p-reference pixels) the cursor has to get to an enemy's
    // screen position to count as pointing at it.
    const float HoverGrabPx = 46f;

    // Enemy under the cursor, or null.
    //
    // Deliberately SCREEN-SPACE distance rather than a physics raycast. A raycast
    // needs a collider that actually covers the enemy, and these are small, fast,
    // frequently half-behind a block, and only get a collider at all when a bullet
    // first needs one (TurretBullet.EnsureEnemyCollider) — so hovering one was a
    // game of its own. Distance-to-projected-position doesn't care about colliders,
    // occlusion or size, which is the right trade for a read-only inspect.
    EnemySurfaceUnit EnemyUnderCursor()
    {
        if (_cam == null) return null;
        if (HudSidePanels.PointerOver) return null;   // reading a panel, not the board

        var mgr = EnemyBaseManager.Instance;
        if (mgr == null) return null;

        // The grab radius is authored at 1080p; scale it so it feels the same on
        // any window size.
        float grab  = HoverGrabPx * (Screen.height / 1080f);
        float grab2 = grab * grab;

        Vector2 mouse = VirtualCursor.Position;
        EnemySurfaceUnit best = null;
        float bestD2 = grab2;

        foreach (var e in mgr.ActiveEnemies)
        {
            if (e == null || e.CurrentHealth <= 0) continue;

            Vector3 sp = _cam.WorldToScreenPoint(e.transform.position + Vector3.up * WorldLift * 0.5f);
            if (sp.z <= 0f) continue;

            float d2 = ((Vector2)sp - mouse).sqrMagnitude;
            if (d2 >= bestD2) continue;   // nearest wins, so overlapping enemies resolve predictably
            best = e; bestD2 = d2;
        }
        return best;
    }

    // ── Trait card ───────────────────────────────────────────────────────────

    RectTransform _card;
    TMP_Text      _cardText;

    void ShowTraits(EnemySurfaceUnit e)
    {
        if (_card == null) BuildCard();

        if (e == null) { _card.gameObject.SetActive(false); return; }

        _card.gameObject.SetActive(true);
        _cardText.text = DescribeEnemy(e);
        // Follows the cursor with a small offset rather than pinning to the enemy:
        // the enemy is moving, and a card that walks with it is harder to read than
        // one that sits still under the hand that's pointing at it.
        _card.position = (Vector3)VirtualCursor.Position + new Vector3(18f, -18f, 0f);
        ClampCardOnScreen();
    }

    void ClampCardOnScreen()
    {
        Vector2 size = _card.rect.size * _card.lossyScale;
        float x = Mathf.Min(_card.position.x, Screen.width  - size.x - 8f);
        float y = Mathf.Max(_card.position.y, size.y + 8f);
        _card.position = new Vector3(x, y, 0f);
    }

    // Name, health, and one line per trait COMPONENT actually on the enemy — read
    // off the live object rather than a data table, so a modifier added by a level
    // mechanic at runtime still shows up.
    static string DescribeEnemy(EnemySurfaceUnit e)
    {
        var sb = new System.Text.StringBuilder();

        string name = e.gameObject.name.Replace("(Clone)", "").Trim();
        sb.Append($"<b>{name}</b>\n");
        sb.Append($"<size=90%>HP {e.CurrentHealth} / {e.maxHealth}</size>");

        float spd = e.TemporarySpeedMultiplier;
        if (spd < 0.995f) sb.Append($"\n<size=90%><color=#8CD9FF>Slowed — {Mathf.RoundToInt(spd * 100f)}% speed</color></size>");

        void Trait(bool has, string line) { if (has) sb.Append($"\n<size=88%>· {line}</size>"); }

        Trait(e.GetComponent<EnemyAccelerator>()      != null, "Builds speed the longer it lives");
        Trait(e.GetComponent<EnemyHealerAura>()       != null, "Heals nearby enemies");
        Trait(e.GetComponent<EnemySplitOnAlive>()     != null, "Splits into smaller enemies");
        Trait(e.GetComponent<EnemyBlockSealer>()      != null, "Seals the blocks it walks over");
        Trait(e.GetComponent<EnemySynergyJammer>()    != null, "Darkens the synergy it stands on");
        Trait(e.GetComponent<EnemyTurretSuppressor>() != null, "Slows nearby turrets' fire");

        return sb.ToString();
    }

    void BuildCard()
    {
        var go = new GameObject("EnemyTraits", typeof(RectTransform));
        go.transform.SetParent(_canvas.transform, false);
        _card = (RectTransform)go.transform;
        _card.pivot     = new Vector2(0f, 0f);   // grows up-right from the cursor
        _card.sizeDelta = new Vector2(300f, 130f);

        var bg = go.AddComponent<Image>();
        bg.sprite        = UIRoundedRect.Get(10);
        bg.type          = Image.Type.Sliced;
        bg.color         = new Color(0.06f, 0.06f, 0.06f, 0.92f);
        bg.raycastTarget = false;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(_card, false);
        var rt = (RectTransform)textGo.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(14f, 12f);
        rt.offsetMax = new Vector2(-14f, -12f);

        _cardText = textGo.AddComponent<TextMeshProUGUI>();
        _cardText.fontSize      = 17f;
        _cardText.color         = GeoPalette.Paper;
        _cardText.alignment     = TextAlignmentOptions.TopLeft;
        _cardText.raycastTarget = false;

        _card.gameObject.SetActive(false);
    }

    Bar GetBar(int i)
    {
        while (_pool.Count <= i) _pool.Add(BuildBar());
        var b = _pool[i];
        if (!b.root.gameObject.activeSelf) b.root.gameObject.SetActive(true);
        return b;
    }

    void HideFrom(int i)
    {
        for (; i < _pool.Count; i++)
            if (_pool[i].root.gameObject.activeSelf)
                _pool[i].root.gameObject.SetActive(false);
    }

    Bar BuildBar()
    {
        var trackGo = new GameObject("Bar", typeof(RectTransform));
        trackGo.transform.SetParent(_canvas.transform, false);
        var track = (RectTransform)trackGo.transform;
        track.sizeDelta = new Vector2(BarWidth, BarHeight);
        var trackImg = trackGo.AddComponent<Image>();
        trackImg.color         = TrackColor;
        trackImg.raycastTarget = false;

        var fillGo = new GameObject("Fill", typeof(RectTransform));
        fillGo.transform.SetParent(track, false);
        var fill = (RectTransform)fillGo.transform;
        // Anchored fill rather than Image.fillAmount: anchors resize without the
        // sprite slicing that fillAmount needs, so a plain white quad works.
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(1f, 1f);
        fill.offsetMin = new Vector2(1f, 1f);
        fill.offsetMax = new Vector2(-1f, -1f);
        var fillImg = fillGo.AddComponent<Image>();
        fillImg.raycastTarget = false;

        return new Bar { root = track, fill = fill, fillImage = fillImg };
    }
}
