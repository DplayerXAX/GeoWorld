using System.Collections.Generic;
using UnityEngine;

// The end block visibly failing as the run goes badly.
//
// Losing a life is the heaviest thing that happens in this game and the end block —
// the thing it happened TO — was the only object on screen that didn't react. Two
// separate signals, and keeping them separate is the point:
//
//   · The JOLT is the event. It fires once, when an enemy gets through, and it is
//     over in a fifth of a second.
//   · The STRESS is the state. It has no timing at all: it is a function of lives
//     remaining, so it is readable at any moment, including the moment you glance
//     back at the board after not watching it.
//
// An effect that only fires on the event tells you what just happened; one that only
// shows state tells you where you stand. You need both, and merging them into one
// escalating animation would give you neither reliably.
[DisallowMultipleComponent]
public class EndpointStress : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Spawn()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        TrySpawn();
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene s,
                              UnityEngine.SceneManagement.LoadSceneMode m) => TrySpawn();

    static void TrySpawn()
    {
        if (PlayerHealth.Instance == null) return;              // gameplay scene only
        if (FindFirstObjectByType<EndpointStress>() != null) return;
        new GameObject("EndpointStress").AddComponent<EndpointStress>();
    }

    // Below this fraction of lives the block starts looking damaged. Above it the
    // board should look fine — a game that starts out cracked has nowhere to go.
    const float StressBegins = 0.75f;

    // Cracks are drawn as thin dark bars on the block's faces. Built once at the
    // worst-case count and revealed progressively, because spawning geometry on the
    // frame you lose a life competes with the jolt for the player's attention.
    const int   MaxCracks    = 14;
    const float ShardDrift   = 0.16f;   // how far loose shards lean out at zero lives

    class Shard
    {
        public Transform t;
        public Vector3   restPos;
        public Vector3   outDir;
        public float     threshold;     // stress level at which it appears
    }

    readonly List<Shard> _shards = new();
    readonly List<Transform> _endpoints = new();

    float _stress;        // 0 = untouched, 1 = about to fail
    float _shown;         // eased, so a life lost is a slump rather than a snap
    float _jolt;

    void Start()
    {
        PlayerHealth.Instance.OnLivesChanged += OnLives;
        Rebuild();
        OnLives(PlayerHealth.Instance.CurrentLives);
        _shown = _stress;
    }

    void OnDestroy()
    {
        if (PlayerHealth.Instance != null) PlayerHealth.Instance.OnLivesChanged -= OnLives;
    }

    void OnLives(int lives)
    {
        var ph = PlayerHealth.Instance;
        float frac = ph != null && ph.maxLives > 0 ? lives / (float)ph.maxLives : 1f;

        // Remapped so the whole visible range of the effect is spent on the part of
        // the health bar the player is actually worried about.
        _stress = Mathf.Clamp01(Mathf.InverseLerp(StressBegins, 0f, frac));
        _jolt   = 1f;
    }

    // Collected by name, the same way EndpointVisual identifies itself. The endpoints
    // are respawned whenever a wave adds one, so this re-runs rather than caching.
    void Rebuild()
    {
        _endpoints.Clear();
        foreach (var v in FindObjectsByType<EndpointVisual>(FindObjectsSortMode.None))
        {
            if (v == null) continue;
            if (v.name.StartsWith("start", System.StringComparison.OrdinalIgnoreCase)) continue;
            _endpoints.Add(v.transform);
        }

        foreach (var s in _shards) if (s.t != null) Destroy(s.t.gameObject);
        _shards.Clear();

        foreach (var end in _endpoints) BuildCracks(end);
    }

    void BuildCracks(Transform end)
    {
        for (int i = 0; i < MaxCracks; i++)
        {
            // A face, a position on it, and a direction along it — deterministic per
            // index so the same block always fractures the same way. A crack pattern
            // that reshuffles itself between frames reads as noise, not as damage.
            float h1 = Hash(i * 3 + 1), h2 = Hash(i * 5 + 2), h3 = Hash(i * 7 + 3);

            int   face   = Mathf.FloorToInt(h1 * 5f);            // 4 sides + top
            Vector3 n    = face switch
            {
                0 => Vector3.forward, 1 => Vector3.back,
                2 => Vector3.right,   3 => Vector3.left,
                _ => Vector3.up,
            };
            Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.forward : Vector3.up;
            Vector3 v = Vector3.Cross(n, u).normalized;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Crack{i}";
            go.transform.SetParent(end, false);

            Vector3 onFace = n * 0.505f + u * ((h2 - 0.5f) * 0.7f) + v * ((h3 - 0.5f) * 0.7f);
            float   ang    = h1 * 180f;

            go.transform.localPosition = onFace;
            go.transform.localRotation = Quaternion.LookRotation(n, u) * Quaternion.Euler(0f, 0f, ang);
            go.transform.localScale    = new Vector3(0.035f, 0.16f + h2 * 0.28f, 0.02f);

            Destroy(go.GetComponent<Collider>());
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _shards.Add(new Shard
            {
                t         = go.transform,
                restPos   = onFace,
                outDir    = (n + u * (h2 - 0.5f) + v * (h3 - 0.5f)).normalized,
                // Spread thresholds across the range so cracks arrive one or two at a
                // time rather than all at the first hit.
                threshold = (i + 0.5f) / MaxCracks,
            });

            go.SetActive(false);
        }
    }

    void Update()
    {
        // Endpoints get added mid-run, so pick up any that appeared without cracks.
        if (_endpoints.Count > 0 && _endpoints[0] == null) Rebuild();

        _shown = Mathf.Lerp(_shown, _stress, 1f - Mathf.Exp(-6f * Time.deltaTime));
        _jolt  = Mathf.Max(0f, _jolt - Time.deltaTime * 5f);

        ApplyJolt();
        ApplyStress();
    }

    // The event. Sharp, short, and biggest at the moment it lands — it is competing
    // with the screen-wide damage shake, so it has to be legible in about six frames.
    void ApplyJolt()
    {
        if (_jolt <= 0f) return;

        // Amplitude scales with how bad things already are: the same hit at one life
        // should look worse than the first one.
        float amp = 0.10f * _jolt * _jolt * (0.6f + _shown);
        foreach (var end in _endpoints)
        {
            if (end == null) continue;
            var off = new Vector3(Mathf.Sin(Time.time * 71f), Mathf.Sin(Time.time * 53f), 0f) * amp;
            foreach (Transform child in end)
            {
                if (!child.name.StartsWith("Crack")) continue;
                child.localPosition += off * 0.15f;
            }
            end.localRotation = Quaternion.Euler(off.y * 40f, 0f, -off.x * 40f);
        }
    }

    // The state. No time term at all — read it any moment and it tells you where the
    // run stands.
    void ApplyStress()
    {
        foreach (var s in _shards)
        {
            if (s.t == null) continue;

            bool on = _shown >= s.threshold;
            if (s.t.gameObject.activeSelf != on) s.t.gameObject.SetActive(on);
            if (!on) continue;

            // How far past its own threshold this crack is — so each one opens on its
            // own, and the block comes apart gradually instead of in steps.
            float open = Mathf.Clamp01((_shown - s.threshold) / Mathf.Max(0.01f, 1f - s.threshold));

            s.t.localPosition = s.restPos + s.outDir * (ShardDrift * open);
            MpbColor.Set(s.t.GetComponent<Renderer>(),
                         Color.Lerp(new Color(0.10f, 0.10f, 0.12f),
                                    new Color(1.00f, 0.35f, 0.20f), open));
        }
    }

    static float Hash(int i) => Mathf.Abs(Mathf.Sin(i * 127.1f) * 43758.5453f) % 1f;
}
