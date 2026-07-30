using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Renders a BlockData's ACTUAL cell shape (using the real cube prefab) to a small
// sprite, for UI lists that want to show "this is what the block looks like"
// instead of just a name — same "photograph a parked instance" approach as
// EnemyThumbnail, adapted for a static block shape instead of a procedural enemy
// (no build-delay wait needed: the cubes exist the instant they're instantiated).
//
// Results are cached per (BlockData, tint) for the session.
public static class BlockShapeThumbnail
{
    const int Size = 128;
    static readonly Vector3 BoothOrigin = new Vector3(-12000f, 12000f, 12000f);

    static GameObject _boothRoot;
    static int _shotCount;
    static readonly Dictionary<(BlockData, Color32), Sprite> _cache = new();

    // Synchronous — block shapes need no frame delay to "assemble" like the
    // procedural enemy visuals do, so this can return the sprite immediately
    // (still cached, so repeat calls for the same tray refresh are free).
    public static Sprite GetOrCreate(BlockData data, GameObject cubePrefab, Color tint, float cellSize = 1f)
    {
        if (data?.cells == null || data.cells.Length == 0 || cubePrefab == null) return null;

        var key = (data, (Color32)tint);
        if (_cache.TryGetValue(key, out var cached) && cached != null) return cached;

        var sprite = Shoot(data, cubePrefab, tint, cellSize);
        if (sprite != null) _cache[key] = sprite;
        return sprite;
    }

    public static void Apply(Image target, Sprite s)
    {
        if (target == null) return;
        target.sprite         = s;
        target.color          = Color.white;
        target.preserveAspect = true;
        target.enabled        = s != null;
    }

    static Sprite Shoot(BlockData data, GameObject cubePrefab, Color tint, float cellSize)
    {
        if (_boothRoot == null)
        {
            _boothRoot = new GameObject("BlockShapeThumbnailBooth");
            Object.DontDestroyOnLoad(_boothRoot);
        }

        Vector3 spot = BoothOrigin + Vector3.right * (_shotCount++ * 20f);
        var rig = new GameObject("Rig");
        rig.transform.SetParent(_boothRoot.transform, false);
        rig.transform.position = spot;

        var centre = Vector3.zero;
        foreach (var c in data.cells) centre += (Vector3)c;
        centre /= data.cells.Length;

        foreach (var c in data.cells)
        {
            var cube = Object.Instantiate(cubePrefab, rig.transform);
            cube.transform.localPosition = ((Vector3)c - centre) * cellSize;
            foreach (var col in cube.GetComponentsInChildren<Collider>()) col.enabled = false;
            foreach (var r in cube.GetComponentsInChildren<Renderer>()) MpbColor.Set(r, tint);
        }

        var camGo = new GameObject("ThumbCam");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags      = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.orthographic    = true;
        cam.nearClipPlane   = 0.05f;
        cam.farClipPlane    = 20f;
        cam.enabled         = false;

        float radius = MeasureRadius(rig.transform, spot);
        cam.orthographicSize = Mathf.Max(0.6f, radius * 1.35f);
        // Isometric-ish angle so multi-cell shapes read as 3D, not a flat silhouette.
        camGo.transform.position = spot + new Vector3(radius * 1.6f, radius * 1.6f, -radius * 1.6f - 3f);
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
        Object.Destroy(rt);
        Object.Destroy(camGo);
        Object.Destroy(rig);

        var sprite = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        sprite.hideFlags = HideFlags.DontSave;
        return sprite;
    }

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
