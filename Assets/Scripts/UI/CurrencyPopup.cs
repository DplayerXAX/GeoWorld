using System.Collections.Generic;
using UnityEngine;

// ── CurrencyPopup ─────────────────────────────────────────────────────────────
//
// Subscribes to ResourceManager currency events and shows animated
// "+N ¤" / "−N ¤" floating labels in screen space (OnGUI).
//
// Setup: attach this to any persistent GameObject (e.g. the one that holds
// DebugUI).  No other configuration required.
//
// Positions:
//   blockAnchorNorm  — normalised screen position for block ¤ popups.
//   turretAnchorNorm — normalised screen position for turret ¤ popups.
//   Adjust in the Inspector so they sit near wherever you eventually render
//   the permanent HUD currency display.
// ─────────────────────────────────────────────────────────────────────────────
public class CurrencyPopup : MonoBehaviour
{
    public static CurrencyPopup Instance;

    [Header("Anchors (normalised screen, y = 0 at top)")]
    public Vector2 blockAnchorNorm  = new Vector2(0.06f, 0.06f);
    public Vector2 turretAnchorNorm = new Vector2(0.06f, 0.12f);

    [Header("Animation")]
    public float riseSpeed = 55f;
    public float lifetime  = 1.6f;
    public int   fontSize  = 16;

    // Turret regen fires a +1 event every ~2 s during combat.
    // Suppress tiny increments to avoid visual noise.
    [Tooltip("Minimum absolute delta to show a turret ¤ popup.")]
    public int turretMinDelta = 2;

    // ── Internal ──────────────────────────────────────────────────────────────

    struct Pop
    {
        public string text;
        public Color  col;
        public float  x, y;    // screen pixels, y = 0 at top
        public float  age;
    }

    readonly List<Pop> _pops = new();
    GUIStyle           _style;
    bool               _styleReady;

    int _prevBlock;
    int _prevTurret;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake() => Instance = this;

    void Start()
    {
        var rm = ResourceManager.Instance;
        if (rm == null) { Debug.LogWarning("[CurrencyPopup] ResourceManager not found."); return; }

        // Snapshot starting values so the first real delta is correct.
        _prevBlock  = rm.BlockCurrency;
        _prevTurret = rm.TurretCurrency;

        rm.OnBlockCurrencyChanged  += OnBlock;
        rm.OnTurretCurrencyChanged += OnTurret;
    }

    void OnDestroy()
    {
        var rm = ResourceManager.Instance;
        if (rm == null) return;
        rm.OnBlockCurrencyChanged  -= OnBlock;
        rm.OnTurretCurrencyChanged -= OnTurret;
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    void OnBlock(int newVal)
    {
        int d = newVal - _prevBlock;
        _prevBlock = newVal;
        if (d == 0) return;

        // Gains: bright green. Spends: coral red.
        Color col = d > 0 ? new Color(0.45f, 1.00f, 0.50f)
                           : new Color(1.00f, 0.38f, 0.38f);
        Spawn(d, col, blockAnchorNorm);
    }

    void OnTurret(int newVal)
    {
        int d = newVal - _prevTurret;
        _prevTurret = newVal;
        if (d == 0 || Mathf.Abs(d) < turretMinDelta) return;

        Color col = d > 0 ? new Color(0.45f, 0.85f, 1.00f)   // sky-blue gain
                           : new Color(1.00f, 0.55f, 0.20f);  // amber spend
        Spawn(d, col, turretAnchorNorm);
    }

    // ── Spawn / update / draw ─────────────────────────────────────────────────

    void Spawn(int delta, Color col, Vector2 anchorNorm)
    {
        string sign = delta >= 0 ? "+" : "−";  // minus sign (−), not hyphen
        _pops.Add(new Pop
        {
            text = $"{sign}{Mathf.Abs(delta)} ¤",
            col  = col,
            x    = anchorNorm.x * Screen.width,
            y    = anchorNorm.y * Screen.height,
            age  = 0f,
        });
    }

    void Update()
    {
        float dt = Time.deltaTime;
        for (int i = _pops.Count - 1; i >= 0; i--)
        {
            var p = _pops[i];
            p.y  -= riseSpeed * dt;
            p.age += dt;
            _pops[i] = p;
            if (p.age >= lifetime) _pops.RemoveAt(i);
        }
    }

    void OnGUI()
    {
        if (_pops.Count == 0) return;
        BuildStyle();

        foreach (var p in _pops)
        {
            // Fade in quickly, hold, then fade out over the last 35% of lifetime.
            float t     = p.age / lifetime;
            float alpha = t < 0.12f ? t / 0.12f                    // quick fade-in
                        : t < 0.65f ? 1f                            // hold
                        :             1f - (t - 0.65f) / 0.35f;    // fade-out
            alpha = Mathf.Clamp01(alpha);

            _style.normal.textColor = new Color(p.col.r, p.col.g, p.col.b, alpha);
            GUI.Label(new Rect(p.x, p.y, 130f, 26f), p.text, _style);
        }
    }

    void BuildStyle()
    {
        if (_styleReady) return;
        _styleReady = true;
        _style = new GUIStyle(GUI.skin.label)
        {
            fontSize  = fontSize,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
        };
    }
}
