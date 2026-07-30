using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Renders an enemy prefab to a small sprite for UI rosters (level-select intel).
//
// Has to be done at RUNTIME rather than with a stored icon or an editor asset
// preview: several of these prefabs carry no mesh at all — EnemyChaoticVisual
// builds their shards procedurally on Start, so a prefab preview would come out
// empty. The enemy only exists once instantiated and given a frame to assemble.
//
// Works by parking an instance in an empty pocket of the world far from the map
// and photographing it with a throwaway camera, so no dedicated layer or scene
// setup is needed. Results are cached per prefab for the session.
public class EnemyThumbnail : MonoBehaviour
{
    const int   Size      = 128;
    static readonly Vector3 BoothOrigin = new Vector3(12000f, 12000f, 12000f);

    static EnemyThumbnail _inst;
    static readonly Dictionary<EnemySurfaceUnit, Sprite> _cache = new();
    // Booth slots are handed out when a shoot STARTS, not when it finishes.
    // Deriving the spot from _cache.Count instead meant every request issued in
    // the same frame (a whole roster is requested at once) read the same count,
    // parked every subject on the identical spot, and photographed the pile —
    // so all portraits came out showing whichever enemy was rendered in front.
    static int _nextSlot;
    // In-flight shoots, so two requests for the same prefab share one booth run
    // instead of racing each other.
    static readonly Dictionary<EnemySurfaceUnit, List<System.Action<Sprite>>> _pending = new();

    static void Ensure()
    {
        if (_inst != null) return;
        var go = new GameObject("EnemyThumbnailBooth");
        DontDestroyOnLoad(go);
        _inst = go.AddComponent<EnemyThumbnail>();
    }

    // Fills `target` with the prefab's portrait as soon as it's rendered (same
    // frame if it's already cached). Safe to call for many rows at once.
    public static void Request(EnemySurfaceUnit prefab, Image target)
    {
        if (target == null) return;
        Request(prefab, s => Apply(target, s));
    }

    // Sprite-callback overload — for world-space SpriteRenderers or anything that
    // isn't a UGUI Image. `onReady` fires immediately if cached, else once rendered.
    public static void Request(EnemySurfaceUnit prefab, System.Action<Sprite> onReady)
    {
        if (prefab == null || onReady == null) return;

        if (_cache.TryGetValue(prefab, out var cached) && cached != null)
        {
            onReady(cached);
            return;
        }

        // Already being photographed — ride along on that shoot.
        if (_pending.TryGetValue(prefab, out var waiting))
        {
            waiting.Add(onReady);
            return;
        }
        _pending[prefab] = new List<System.Action<Sprite>> { onReady };

        Ensure();
        _inst.StartCoroutine(_inst.Shoot(prefab));
    }

    static void Apply(Image target, Sprite s)
    {
        if (target == null) return;
        target.sprite         = s;
        target.color          = Color.white;
        target.preserveAspect = true;
        target.enabled        = true;
    }

    IEnumerator Shoot(EnemySurfaceUnit prefab)
    {
        // Park each subject at its own spot so two concurrent requests can't
        // photobomb each other. The slot is claimed up front — a counter that
        // only moves once the sprite is cached would hand the same spot to every
        // request made before the first one finished.
        Vector3 spot = BoothOrigin + Vector3.right * (_nextSlot++ * 50f);

        var go = Instantiate(prefab.gameObject, spot, Quaternion.identity);
        // Visual only: no pathing, no physics, no gameplay behaviours.
        foreach (var c in go.GetComponentsInChildren<EnemySurfaceUnit>(true))      c.enabled = false;
        foreach (var c in go.GetComponentsInChildren<EnemyHealerAura>(true))       c.enabled = false;
        foreach (var c in go.GetComponentsInChildren<EnemyBlockSealer>(true))      c.enabled = false;
        foreach (var c in go.GetComponentsInChildren<EnemyTurretSuppressor>(true)) c.enabled = false;
        foreach (var c in go.GetComponentsInChildren<EnemySplitOnAlive>(true))     c.enabled = false;
        foreach (var c in go.GetComponentsInChildren<Collider>(true))              c.enabled = false;
        foreach (var c in go.GetComponentsInChildren<Rigidbody>(true))             c.isKinematic = true;

        // Let EnemyChaoticVisual actually build its shards before we photograph.
        yield return null;
        yield return null;

        var camGo = new GameObject("ThumbCam");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags      = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);   // transparent — the panel shows through
        cam.orthographic    = true;
        cam.nearClipPlane   = 0.05f;
        cam.farClipPlane    = 20f;                          // nothing else is out here anyway
        cam.enabled         = false;                        // manual Render() only

        // Frame whatever the prefab actually built, so big and small enemies each
        // fill the portrait instead of being sized by a guessed constant.
        float radius = MeasureRadius(go.transform, spot);
        cam.orthographicSize = Mathf.Max(0.15f, radius * 0.95f);   // tighter crop — fills more of the frame
        camGo.transform.position = spot + new Vector3(0f, radius * 0.35f, -5f);
        camGo.transform.LookAt(spot);

        var rt = new RenderTexture(Size, Size, 16, RenderTextureFormat.ARGB32) { useMipMap = false };
        cam.targetTexture = rt;
        cam.Render();

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
        tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;

        cam.targetTexture = null;
        rt.Release();
        Destroy(rt);
        Destroy(camGo);
        Destroy(go);

        var sprite = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        sprite.hideFlags = HideFlags.DontSave;
        _cache[prefab] = sprite;

        if (_pending.TryGetValue(prefab, out var waiting))
        {
            _pending.Remove(prefab);
            for (int i = 0; i < waiting.Count; i++) waiting[i]?.Invoke(sprite);
        }
    }

    // Radius of the built visual around `centre`, from renderer bounds.
    static float MeasureRadius(Transform root, Vector3 centre)
    {
        float r = 0f;
        foreach (var rend in root.GetComponentsInChildren<Renderer>())
        {
            if (rend == null) continue;
            var b = rend.bounds;
            r = Mathf.Max(r, Vector3.Distance(centre, b.center) + b.extents.magnitude);
        }
        return r > 0.01f ? r : 0.5f;
    }
}
