using System.Collections.Generic;
using UnityEngine;

// A block wearing away (GeoWorld/Dissolve) — for a block the level takes from the
// player (Structural Instability), so it visibly goes rather than blinking out.
//
// Plays on a stripped COPY of the block's visual: the real block is removed from
// the board at once (grid, synergy, paths — nothing waits on the animation), and
// the copy carries only transforms and renderers, so nothing on it can fire, spin,
// be clicked or be counted. Each renderer keeps its own colour; the outline hull
// slot is dropped (dissolving an inverted hull reads as a second, fatter block).
public class BlockDissolveFx : MonoBehaviour
{
    static Material _mat;

    Renderer[] _rends;
    MaterialPropertyBlock _mpb;
    float _t, _duration, _sink;
    Vector3 _start;

    static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
    static readonly int SeedId     = Shader.PropertyToID("_Seed");
    static readonly int ColorId    = Shader.PropertyToID("_BaseColor");
    static readonly int EdgeId     = Shader.PropertyToID("_EdgeColor");

    /// <summary>Copy `visual` and dissolve the copy over `duration` seconds. Call BEFORE the original is destroyed.</summary>
    public static void Play(GameObject visual, float duration = 1.4f, float sinkCells = 0.15f)
    {
        if (visual == null) return;
        var mat = Mat();
        if (mat == null) return;

        // Built mesh by mesh rather than Instantiate'd: a copy of the real hierarchy
        // would wake every script on it (a turret's controller would start shooting
        // from a ghost) and drag along line and particle effects the dissolve can't
        // draw. Every visible MeshRenderer, with its world pose, mesh and colour.
        var root = new GameObject(visual.name + " (dissolving)");
        root.transform.position = visual.transform.position;
        var rends = new List<Renderer>();
        float seed = Random.Range(0f, 100f);

        foreach (var src in visual.GetComponentsInChildren<MeshRenderer>())
        {
            if (!src.enabled) continue;
            var mf = src.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;

            var go = new GameObject(src.name);
            go.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation);
            go.transform.localScale = src.transform.lossyScale;
            go.transform.SetParent(root.transform, worldPositionStays: true);
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;

            int slots = 0;
            foreach (var m in src.sharedMaterials) if (!IsOutline(m)) slots++;
            var arr = new Material[Mathf.Max(1, slots)];
            for (int k = 0; k < arr.Length; k++) arr[k] = mat;

            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials   = arr;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            var mpb = new MaterialPropertyBlock();
            mpb.SetColor(ColorId, MpbColor.Get(src));
            if (LevelEnvironmentDriver.Current != null)
                mpb.SetColor(EdgeId, LevelEnvironmentDriver.Current.dissolveEdge);
            mpb.SetFloat(SeedId, seed);
            mpb.SetFloat(DissolveId, 0f);
            r.SetPropertyBlock(mpb);
            rends.Add(r);
        }

        if (rends.Count == 0) { Destroy(root); return; }

        var fx = root.AddComponent<BlockDissolveFx>();
        float cs = GridSystem.instance != null ? GridSystem.instance.cellSize : 1f;
        fx._mpb      = new MaterialPropertyBlock();
        fx._rends    = rends.ToArray();
        fx._duration = Mathf.Max(0.05f, duration);
        fx._sink     = sinkCells * cs;
        fx._start    = root.transform.position;
    }

    static bool IsOutline(Material m)
    {
        if (m == null || m.shader == null) return false;
        var n = m.shader.name;
        // The weathering overlay (BlockSurface) is an extra slot too — not a surface.
        return n == "GeoWorld/BlockOutline" || n == "Custom/ObjectOutline" || n == "GeoWorld/BlockWeather";
    }

    static Material Mat()
    {
        if (_mat != null) return _mat;
        var sh = Shader.Find("GeoWorld/Dissolve");
        if (sh == null) { Debug.LogWarning("[BlockDissolveFx] GeoWorld/Dissolve shader not found."); return null; }
        _mat = new Material(sh) { name = "Dissolve (runtime)", enableInstancing = true };
        return _mat;
    }

    void Update()
    {
        _t += Time.deltaTime;
        float p = Mathf.Clamp01(_t / _duration);
        float d = p * p * (3f - 2f * p);   // smoothstep: eases in and out of the burn

        foreach (var r in _rends)
        {
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetFloat(DissolveId, d);
            r.SetPropertyBlock(_mpb);
        }
        transform.position = _start + Vector3.down * (_sink * d);

        if (p >= 1f) Destroy(gameObject);
    }
}
