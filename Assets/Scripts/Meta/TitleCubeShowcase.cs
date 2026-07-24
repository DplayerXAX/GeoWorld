using System.Collections.Generic;
using UnityEngine;

// Title-screen centrepiece: one cube whose material cycles through the game's
// shaders, with a scale "punch" + slow drift on each swap. Drop on a cube in
// front of a white camera background and fill `materials` with your core/synergy
// shader materials (ChaosCore, HaloCore, Exploration, HarmonyFlow, OrderGrid…).
//
// Pure visual + engine-agnostic — works whether the surrounding menu ends up
// being UGUI or IMGUI. Add a second material slot using GeoWorld/BlockOutline
// for the bold silhouette rim if you want the Persona-style cut-out look.
[DisallowMultipleComponent]
public class TitleCubeShowcase : MonoBehaviour
{
    [Header("Showcase")]
    [Tooltip("Materials cycled on the cube (one per shader to show off).")]
    public List<Material> materials = new();

    [Tooltip("Seconds each material is shown before the next.")]
    [Min(0.1f)] public float swapInterval = 3.5f;

    [Tooltip("Random order instead of sequential (avoids immediate repeats).")]
    public bool shuffle;

    [Tooltip("When false, the cube stops auto-cycling materials (still spins). Set an exact material with ShowIndex().")]
    public bool autoAdvance = true;

    [Header("Motion")]
    [Tooltip("Constant slow spin, deg/sec (x, y, z).")]
    public Vector3 spinDegPerSec = new Vector3(8f, 18f, 0f);

    [Tooltip("Scale punch on each swap (0 = none).")]
    [Range(0f, 0.6f)] public float swapPunch = 0.18f;
    [Min(0.05f)] public float punchDuration = 0.35f;

    Renderer _rend;
    Vector3  _baseScale;
    int      _index = -1;
    float    _timer;
    float    _punchT;
    bool     _punching;

    // Which material is currently showing — read by GalleryScreen to label it.
    public int CurrentIndex => _index;

    void Awake()
    {
        _rend      = GetComponent<Renderer>();
        _baseScale = transform.localScale;
        Next();
    }

    void Update()
    {
        transform.Rotate(spinDegPerSec * Time.deltaTime, Space.Self);

        if (autoAdvance)
        {
            _timer += Time.deltaTime;
            if (_timer >= swapInterval) { _timer = 0f; Next(); }
        }

        if (_punching)
        {
            _punchT += Time.deltaTime;
            float p = Mathf.Clamp01(_punchT / punchDuration);
            // Peak at the swap, ease back to rest.
            transform.localScale = _baseScale * (1f + swapPunch * (1f - p) * (1f - p));
            if (p >= 1f) { _punching = false; transform.localScale = _baseScale; }
        }
    }

    void Next()
    {
        if (_rend == null || materials == null || materials.Count == 0) return;

        if (shuffle && materials.Count > 1)
        {
            int n;
            do { n = Random.Range(0, materials.Count); } while (n == _index);
            _index = n;
        }
        else _index = (_index + 1) % materials.Count;

        if (materials[_index] != null) _rend.sharedMaterial = materials[_index];
        _punchT   = 0f;
        _punching = swapPunch > 0f;
    }

    // Pin an exact material (used by the Gallery detail view's prev/next). Punches
    // on change, same as an auto-swap.
    public void ShowIndex(int i)
    {
        if (_rend == null || materials == null || materials.Count == 0) return;
        _index = Mathf.Clamp(i, 0, materials.Count - 1);
        if (materials[_index] != null) _rend.sharedMaterial = materials[_index];
        _timer    = 0f;
        _punchT   = 0f;
        _punching = swapPunch > 0f;
    }
}
