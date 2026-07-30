using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Buff / debuff status particles for turrets and enemies.
//
//   DEBUFF → red arrows pointing DOWN, sinking through the unit.
//   BUFF   → green arrows pointing UP, rising through it.
//
// Prefab-free and zero scene wiring, same as SynergyBuffFx: the arrow mesh is
// built once at runtime and a small pool of billboarded quads is parented to
// each affected unit. Both banks can run at once — a turret that's inside a
// Harmony buff AND under an enemy suppressor shows both, which is exactly the
// composition the TurretController multiplier channels already model.
//
// Driven by StatusEffectWatcher (below), which polls the canonical multiplier
// channels — so any current or future effect that goes through them gets the
// feedback for free. One-off events that don't live in a channel (a heal tick,
// say) can call StatusArrowFx.Show directly.
public static class StatusArrowFx
{
    public enum Kind { Buff, Debuff }

    // How long a Show() call keeps the arrows alive. The watcher re-asserts every
    // frame, so this only decides how quickly they fade once the effect ends.
    public const float DefaultHold = 0.35f;

    public static readonly Color BuffColor   = new Color(0.30f, 0.95f, 0.45f);
    public static readonly Color DebuffColor = new Color(0.95f, 0.25f, 0.22f);

    static Material _mat;
    static Mesh     _arrow;

    static void Ensure()
    {
        if (_mat != null) return;
        _arrow = BuildArrowMesh();
        // Kept in standalone builds by Assets/Resources/GeoWorldShaderKeepalive/
        // StatusArrow_keep.mat — a Shader.Find-only shader is otherwise stripped.
        var sh = Shader.Find("GeoWorld/StatusArrow");
        _mat = new Material(sh != null ? sh : Shader.Find("Universal Render Pipeline/Unlit"))
        {
            name = "StatusArrowFx",
        };
    }

    // Shows (or refreshes) `kind` arrows on `target`. Safe to call every frame.
    // `radius` sizes the column the arrows travel in; pass <= 0 for the default
    // derived from the grid cell size.
    public static StatusArrows Show(Transform target, Kind kind,
                                    float hold = DefaultHold, float radius = 0f)
    {
        if (target == null) return null;
        Ensure();

        var arrows = target.GetComponentInChildren<StatusArrows>();
        if (arrows == null)
        {
            var go = new GameObject("StatusArrows") { layer = target.gameObject.layer };
            go.transform.SetParent(target, false);
            go.transform.localPosition = Vector3.zero;
            arrows = go.AddComponent<StatusArrows>();
            arrows.Bind(_arrow, _mat);
        }

        if (radius <= 0f)
            radius = GridSystem.instance != null ? GridSystem.instance.cellSize * 0.55f : 0.55f;

        arrows.Refresh(kind, hold, radius);
        return arrows;
    }

    public static void Buff(Transform target, float hold = DefaultHold, float radius = 0f)
        => Show(target, Kind.Buff, hold, radius);

    public static void Debuff(Transform target, float hold = DefaultHold, float radius = 0f)
        => Show(target, Kind.Debuff, hold, radius);

    // Arrow pointing +Y, roughly 1 unit tall and centred on the origin: a narrow
    // shaft with a wide head. uv.y runs 0 at the tail → 1 at the tip, which the
    // shader uses to fade the tail into a streak.
    static Mesh BuildArrowMesh()
    {
        const float halfShaft = 0.11f;
        const float halfHead  = 0.30f;
        const float neckY     = 0.06f;   // where the shaft ends and the head starts

        var verts = new[]
        {
            new Vector3(-halfShaft, -0.5f,  0f),   // 0 shaft tail-left
            new Vector3( halfShaft, -0.5f,  0f),   // 1 shaft tail-right
            new Vector3( halfShaft, neckY,  0f),   // 2 shaft neck-right
            new Vector3(-halfShaft, neckY,  0f),   // 3 shaft neck-left
            new Vector3(-halfHead,  neckY,  0f),   // 4 head-left
            new Vector3( halfHead,  neckY,  0f),   // 5 head-right
            new Vector3( 0f,         0.5f,  0f),   // 6 tip
        };

        var uvs = new Vector2[verts.Length];
        for (int i = 0; i < verts.Length; i++)
            uvs[i] = new Vector2(verts[i].x * 1.6f + 0.5f, verts[i].y + 0.5f);

        var m = new Mesh { name = "StatusArrow" };
        m.vertices  = verts;
        m.uv        = uvs;
        m.triangles = new[] { 0, 1, 2,  0, 2, 3,  4, 5, 6 };
        m.RecalculateBounds();
        return m;
    }
}

// The per-unit arrow pool. Owns up to two independent banks (buff / debuff) so a
// unit under both effects shows both at once; each bank auto-expires once
// nothing refreshes it, and the component removes itself when both are gone.
public class StatusArrows : MonoBehaviour
{
    const int   PerBank   = 5;      // arrows in flight per bank
    const float TravelSec = 1.15f;  // seconds for one arrow to cross the column
    const float ColumnTop = 0.85f;  // travel range in world units, ± around the unit

    struct Arrow
    {
        public Transform t;
        public Renderer  r;
        public float     phase;      // 0..1 position along the travel, offset per arrow
        public Vector3   offset;     // horizontal placement around the unit
        public float     scale;
    }

    class Bank
    {
        public Arrow[] arrows;
        public float   expire;
        public GameObject root;
    }

    Mesh     _mesh;
    Material _mat;
    Bank     _buff, _debuff;
    MaterialPropertyBlock _mpb;
    Transform _cam;
    float _radius = 0.55f;

    public void Bind(Mesh mesh, Material mat)
    {
        _mesh = mesh;
        _mat  = mat;
        _mpb  = new MaterialPropertyBlock();
    }

    public void Refresh(StatusArrowFx.Kind kind, float hold, float radius)
    {
        _radius = radius;
        float expire = Time.time + Mathf.Max(0.05f, hold);

        if (kind == StatusArrowFx.Kind.Buff)
        {
            _buff ??= BuildBank(true);
            _buff.expire = expire;
        }
        else
        {
            _debuff ??= BuildBank(false);
            _debuff.expire = expire;
        }
    }

    Bank BuildBank(bool buff)
    {
        var bank = new Bank { arrows = new Arrow[PerBank] };
        bank.root = new GameObject(buff ? "Buff" : "Debuff") { layer = gameObject.layer };
        bank.root.transform.SetParent(transform, false);
        bank.root.transform.localPosition = Vector3.zero;

        for (int i = 0; i < PerBank; i++)
        {
            var go = new GameObject($"Arrow_{i}") { layer = gameObject.layer };
            go.transform.SetParent(bank.root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial    = _mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows    = false;
            r.lightProbeUsage   = LightProbeUsage.Off;

            // Spread the arrows around the unit and stagger them along the travel
            // so they read as a stream rather than one synchronised block.
            float ang = (i / (float)PerBank) * Mathf.PI * 2f + Random.value * 0.6f;
            bank.arrows[i] = new Arrow
            {
                t      = go.transform,
                r      = r,
                phase  = i / (float)PerBank + Random.value * 0.1f,
                offset = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)),
                scale  = Random.Range(0.34f, 0.46f),
            };
        }
        return bank;
    }

    void LateUpdate()
    {
        if (_cam == null) { var c = Camera.main; if (c != null) _cam = c.transform; }

        // Unscaled: status feedback should still animate while the planning pause
        // has frozen timeScale, same as the rest of the build-phase UI.
        float dt = Time.unscaledDeltaTime;
        bool aliveBuff   = Step(_buff,   dt, rising: true);
        bool aliveDebuff = Step(_debuff, dt, rising: false);

        if (!aliveBuff && !aliveDebuff) Destroy(gameObject);
    }

    // Advances one bank; returns false once it has expired and been torn down.
    bool Step(Bank bank, float dt, bool rising)
    {
        if (bank == null) return false;

        if (Time.time >= bank.expire)
        {
            if (bank.root != null) Destroy(bank.root);
            if (rising) _buff = null; else _debuff = null;
            return false;
        }

        Color color = rising ? StatusArrowFx.BuffColor : StatusArrowFx.DebuffColor;
        float step  = dt / TravelSec;

        for (int i = 0; i < bank.arrows.Length; i++)
        {
            var a = bank.arrows[i];
            if (a.t == null) continue;

            a.phase += step;
            if (a.phase >= 1f) a.phase -= 1f;
            bank.arrows[i] = a;

            // Rising arrows travel bottom → top, sinking ones top → bottom.
            float y = Mathf.Lerp(-ColumnTop, ColumnTop, rising ? a.phase : 1f - a.phase);
            a.t.localPosition = a.offset * _radius + Vector3.up * y;

            // Billboard toward the camera; a sinking arrow is the same mesh spun
            // 180° in screen space so it points down.
            if (_cam != null)
            {
                Vector3 fwd = a.t.position - _cam.position;
                if (fwd.sqrMagnitude > 0.0001f)
                    a.t.rotation = Quaternion.LookRotation(fwd, Vector3.up)
                                 * (rising ? Quaternion.identity : Quaternion.Euler(0f, 0f, 180f));
            }

            a.t.localScale = Vector3.one * a.scale;

            // Fade in off the start of the travel and out at the end, so arrows
            // appear and vanish inside the column instead of popping at its edges.
            float fade = Mathf.Min(a.phase, 1f - a.phase) / 0.25f;
            float alpha = Mathf.Clamp01(fade) * 0.95f;

            if (a.r != null)
            {
                a.r.GetPropertyBlock(_mpb);
                _mpb.SetColor("_BaseColor", new Color(color.r, color.g, color.b, alpha));
                a.r.SetPropertyBlock(_mpb);
            }
        }
        return true;
    }
}

// Polls every live turret and enemy for an active buff / debuff and drives the
// arrows. Reading the canonical multiplier channels (rather than hooking each
// individual effect) means every source — synergy, shrine, suppressor, slow
// turret, Order synergy — is covered by one place, and stays covered when new
// ones are added.
//
// Auto-spawns once per gameplay scene, the same hook pattern used by
// TutorialDirector / ChaosBlockController — no scene wiring needed.
[DisallowMultipleComponent]
public class StatusEffectWatcher : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        TrySpawn();
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene s,
                              UnityEngine.SceneManagement.LoadSceneMode m) => TrySpawn();

    static void TrySpawn()
    {
        // PlacementController.Instance is Awake()-set, so it's already there by the
        // time this hook runs — unlike GameFlowManager.Instance, which is Start()-set.
        if (PlacementController.Instance == null) return;
        if (FindFirstObjectByType<StatusEffectWatcher>() != null) return;
        new GameObject("StatusEffectWatcher").AddComponent<StatusEffectWatcher>();
    }

    [Tooltip("Seconds between re-scans for newly spawned turrets / enemies. The cached list is polled every frame; only the scan itself is throttled.")]
    [Min(0.05f)] public float rescanInterval = 0.25f;

    readonly List<TurretController>  _turrets = new();
    readonly List<EnemySurfaceUnit>  _enemies = new();
    float _rescanTimer;

    void Update()
    {
        _rescanTimer -= Time.unscaledDeltaTime;
        if (_rescanTimer <= 0f)
        {
            _rescanTimer = rescanInterval;
            Rescan();
        }

        for (int i = 0; i < _turrets.Count; i++)
        {
            var t = _turrets[i];
            if (t == null) continue;

            // Debuff channel is the suppressor's; the synergy and shrine channels
            // are buffs. They compose, so both sets of arrows can show at once.
            if (t.DebuffFireRateMultiplier < 0.999f)
                StatusArrowFx.Debuff(t.transform);
            if (t.SynergyFireRateMultiplier > 1.001f || t.ShrineBuffActive)
                StatusArrowFx.Buff(t.transform);
        }

        for (int i = 0; i < _enemies.Count; i++)
        {
            var e = _enemies[i];
            if (e == null || e.CurrentHealth <= 0) continue;

            // Only the temporary channel counts — baseSpeedMultiplier is the
            // archetype's authored speed, not something being done to it.
            float m = e.TemporarySpeedMultiplier;
            if (m < 0.999f)      StatusArrowFx.Debuff(e.transform);
            else if (m > 1.001f) StatusArrowFx.Buff(e.transform);
        }
    }

    void Rescan()
    {
        _turrets.Clear();
        _turrets.AddRange(FindObjectsByType<TurretController>(FindObjectsSortMode.None));
        _enemies.Clear();
        _enemies.AddRange(FindObjectsByType<EnemySurfaceUnit>(FindObjectsSortMode.None));
    }
}
