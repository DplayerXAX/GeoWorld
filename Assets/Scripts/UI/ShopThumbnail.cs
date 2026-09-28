using System.Collections.Generic;
using UnityEngine;

// Shop icons: a photograph of the real piece — the block's cubes, or the turret's
// model — taken once from a fixed angle (from above, 45° round to one side) with an
// orthographic camera, so the shape never distorts and every icon is lit and framed
// the same way. The shot is fitted to the piece's projected outline, so a one-cell
// block and a six-cell one both fill their icon.
//
// Cached per (asset, colour, angle) for the session — a refreshed shop reuses them.
public static class ShopThumbnail
{
    public const int Size = 256;
    static readonly Vector3 BoothOrigin = new Vector3(-14000f, 14000f, 14000f);

    static GameObject _booth;
    static int _shots;
    static readonly Dictionary<(Object, Color32, int), Sprite> _cache = new();

    /// <summary>A block's cell shape in its colour.</summary>
    public static Sprite Block(BlockData data, GameObject cubePrefab, Color tint, float cellSize,
                               float yaw, float pitch, float padding = 1.12f)
    {
        if (data?.cells == null || data.cells.Length == 0 || cubePrefab == null) return null;
        var key = (data as Object, (Color32)tint, AngleKey(yaw, pitch));
        if (_cache.TryGetValue(key, out var s) && s != null) return s;

        var rig = NewRig();
        var centre = Vector3.zero;
        foreach (var c in data.cells) centre += (Vector3)c;
        centre /= data.cells.Length;
        foreach (var c in data.cells)
        {
            var cube = Object.Instantiate(cubePrefab, rig.transform);
            cube.transform.localPosition = ((Vector3)c - centre) * cellSize;
            cube.transform.localRotation = Quaternion.identity;
            Strip(cube);
            foreach (var r in cube.GetComponentsInChildren<Renderer>()) MpbColor.Set(r, tint);
        }

        s = Shoot(rig, yaw, pitch, padding);
        if (s != null) _cache[key] = s;
        return s;
    }

    /// <summary>A turret's model, fitted to one cell, in its type colour.</summary>
    public static Sprite Turret(BlockData data, float cellSize, float yaw, float pitch, float padding = 1.04f)
    {
        if (data == null || data.turretPrefab == null) return null;
        var tint = TurretTypes.DisplayColor(data.blockType);
        var key = (data as Object, (Color32)tint, AngleKey(yaw, pitch));
        if (_cache.TryGetValue(key, out var s) && s != null) return s;

        var rig = NewRig();
        var visual = Object.Instantiate(data.turretPrefab, rig.transform);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale    = Vector3.one;
        // No behaviours running on a prop that lives for one frame.
        foreach (var b in visual.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled = false;
        Strip(visual);
        if (!TurretVisualFit.Fit(visual, cellSize, out _, out _))
            visual.transform.localScale = Vector3.one * 50f;
        foreach (var r in visual.GetComponentsInChildren<Renderer>()) MpbColor.Set(r, tint);

        s = Shoot(rig, yaw, pitch, padding);
        if (s != null) _cache[key] = s;
        return s;
    }

    static int AngleKey(float yaw, float pitch) => Mathf.RoundToInt(yaw) * 1000 + Mathf.RoundToInt(pitch);

    static GameObject NewRig()
    {
        if (_booth == null)
        {
            _booth = new GameObject("ShopThumbnailBooth");
            Object.DontDestroyOnLoad(_booth);
        }
        var rig = new GameObject("Rig");
        rig.transform.SetParent(_booth.transform, false);
        rig.transform.position = BoothOrigin + Vector3.right * (_shots++ % 64 * 30f);
        return rig;
    }

    static void Strip(GameObject go)
    {
        foreach (var col in go.GetComponentsInChildren<Collider>(true)) col.enabled = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static Sprite Shoot(GameObject rig, float yaw, float pitch, float padding)
    {
        // Everything the piece draws, as one box.
        var rends = rig.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) { Object.Destroy(rig); return null; }
        var b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);

        var camGo = new GameObject("ShopThumbCam");
        var cam = camGo.AddComponent<Camera>();
        cam.enabled         = false;
        cam.clearFlags      = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.orthographic    = true;
        cam.allowHDR        = false;
        cam.allowMSAA       = false;

        // Looking down at `pitch`, from `yaw` round the piece.
        var rot = Quaternion.Euler(pitch, yaw, 0f);
        float reach = b.extents.magnitude + 1f;
        camGo.transform.SetPositionAndRotation(b.center - rot * Vector3.forward * (reach + 1f), rot);
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane  = reach * 2f + 2f;

        // Fit the box's projected outline (square frame, so the larger half-extent).
        float half = 0.1f;
        for (int i = 0; i < 8; i++)
        {
            var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            var local  = camGo.transform.InverseTransformPoint(corner);
            half = Mathf.Max(half, Mathf.Abs(local.x), Mathf.Abs(local.y));
        }
        cam.orthographicSize = half * padding;

        var rt = new RenderTexture(Size, Size, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        { antiAliasing = 1, useMipMap = false };
        cam.targetTexture = rt;
        cam.Render();

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, filterMode = FilterMode.Bilinear };
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
}
