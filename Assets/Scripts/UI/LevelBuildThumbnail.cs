using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Renders a GridSnapshot (LevelRecord.buildSnapshot) to a small sprite for the
// level info panel. Same "photograph a parked instance" approach as
// BlockShapeThumbnail/EnemyThumbnail, shooting the whole board instead of one
// block. One tinted cube per occupied cell (colour from the snapshot, including
// turret cells' display tint) — no turret meshes, no live GridSystem needed.
public static class LevelBuildThumbnail
{
    const int Size = 384;
    const float CellSize = 1f;   // SnapshotManager always captures at cellSize 1
    static readonly Vector3 BoothOrigin = new Vector3(-24000f, 24000f, 24000f);

    static GameObject _boothRoot;
    static int _shotCount;
    static readonly Dictionary<string, Sprite> _cache = new();

    // `cacheKey` should be unique per (level, snapshot), e.g. levelId + timestamp.
    public static Sprite GetOrCreate(string cacheKey, GridSnapshot snap, GameObject cubePrefab)
    {
        if (string.IsNullOrEmpty(cacheKey) || snap?.blocks == null || snap.blocks.Count == 0 || cubePrefab == null)
            return null;

        if (_cache.TryGetValue(cacheKey, out var cached) && cached != null) return cached;

        var sprite = Shoot(snap, cubePrefab);
        if (sprite != null) _cache[cacheKey] = sprite;
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

    static Sprite Shoot(GridSnapshot snap, GameObject cubePrefab)
    {
        if (_boothRoot == null)
        {
            _boothRoot = new GameObject("LevelBuildThumbnailBooth");
            Object.DontDestroyOnLoad(_boothRoot);
        }

        Vector3 spot = BoothOrigin + Vector3.right * (_shotCount++ * 40f);
        var rig = new GameObject("Rig");
        rig.transform.SetParent(_boothRoot.transform, false);
        rig.transform.position = spot;

        // Centre on the whole board's occupied-cell footprint, not just one block.
        Vector3 centre = Vector3.zero;
        int cellCount = 0;
        foreach (var b in snap.blocks)
        {
            if (b?.occupiedCells == null) continue;
            foreach (var c in b.occupiedCells) { centre += (Vector3)c; cellCount++; }
        }
        if (cellCount == 0) { Object.Destroy(rig); return null; }
        centre /= cellCount;

        foreach (var b in snap.blocks)
        {
            if (b?.occupiedCells == null) continue;
            foreach (var c in b.occupiedCells)
            {
                var cube = Object.Instantiate(cubePrefab, rig.transform);
                cube.transform.localPosition = ((Vector3)c - centre) * CellSize;
                foreach (var col in cube.GetComponentsInChildren<Collider>()) col.enabled = false;
                foreach (var r in cube.GetComponentsInChildren<Renderer>()) MpbColor.Set(r, b.color);
            }
        }

        var camGo = new GameObject("ThumbCam");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags      = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.orthographic    = true;
        cam.nearClipPlane   = 0.05f;
        cam.farClipPlane    = 200f;
        cam.aspect          = 1f;      // square RT — matches the fit math below
        cam.enabled         = false;

        // Frame tightly: measure the board's extent IN CAMERA SPACE (project every
        // bounds corner) instead of a spherical radius, which over-pads a flat board.
        var bounds = MeasureBounds(rig.transform, spot);
        Vector3 dir = new Vector3(1f, 1f, -1f).normalized;   // isometric-ish, matches BlockShapeThumbnail
        float back = bounds.extents.magnitude + 10f;
        camGo.transform.position = bounds.center + dir * back;
        camGo.transform.LookAt(bounds.center);

        float halfH = 0.001f, halfW = 0.001f;
        Vector3 bCentre = bounds.center, bExt = bounds.extents;
        for (int i = 0; i < 8; i++)
        {
            var corner = bCentre + new Vector3(
                (i & 1) == 0 ? -bExt.x : bExt.x,
                (i & 2) == 0 ? -bExt.y : bExt.y,
                (i & 4) == 0 ? -bExt.z : bExt.z);
            var local = camGo.transform.InverseTransformPoint(corner);
            halfW = Mathf.Max(halfW, Mathf.Abs(local.x));
            halfH = Mathf.Max(halfH, Mathf.Abs(local.y));
        }
        // Margin scales with build size: a small build sits back with breathing room
        // (1.06), a big one crops in tighter (0.95) so it still fills the frame.
        float margin = Mathf.Lerp(1.06f, 0.95f, Mathf.InverseLerp(4f, 40f, cellCount));
        cam.orthographicSize = Mathf.Max(0.5f, Mathf.Max(halfH, halfW) * margin);

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

    // World-space AABB of everything under `root`; falls back to a unit box.
    static Bounds MeasureBounds(Transform root, Vector3 fallbackCentre)
    {
        var rends = root.GetComponentsInChildren<Renderer>();
        bool started = false;
        Bounds b = new Bounds(fallbackCentre, Vector3.one);
        foreach (var rend in rends)
        {
            if (rend == null) continue;
            if (!started) { b = rend.bounds; started = true; }
            else b.Encapsulate(rend.bounds);
        }
        if (!started || b.extents.magnitude < 0.01f) b = new Bounds(fallbackCentre, Vector3.one);
        return b;
    }
}
