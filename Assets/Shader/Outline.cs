using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Outline : MonoBehaviour
{
    public Color OutlineColor { get => outlineColor; set { outlineColor = value; ApplyProperties(); } }
    public float OutlineWidth { get => outlineWidth; set { outlineWidth = value; ApplyProperties(); } }

    [SerializeField] private Color outlineColor = Color.yellow;
    // Object-space normal extrusion (in mesh local units). Hugs the geometry.
    [SerializeField, Range(0f, 0.15f)] private float outlineWidth = 0.05f;

    private List<Renderer> _renderers;
    private List<Material[]> _originals = new();
    private Material _outlineMat;

    void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>().ToList();
        _outlineMat = new Material(Shader.Find("Custom/ObjectOutline"));
        ApplyProperties();
    }

    void OnEnable()  => AddOutline();
    void OnDisable() => RemoveOutline();
    void OnDestroy() { if (_outlineMat != null) Destroy(_outlineMat); }

    private void AddOutline()
    {
        _originals.Clear();
        foreach (var r in _renderers)
        {
            // Use r.materials (instances) not r.sharedMaterials (assets),
            // so any per-instance color changes (e.g. random block colors) are preserved.
            _originals.Add(r.materials);
            var mats = r.materials.ToList();
            mats.Add(_outlineMat);
            r.materials = mats.ToArray();
        }
    }

    private void RemoveOutline()
    {
        for (int i = 0; i < _renderers.Count && i < _originals.Count; i++)
        {
            if (_renderers[i] != null)
                _renderers[i].materials = _originals[i];
        }
        _originals.Clear();
    }

    private void ApplyProperties()
    {
        if (_outlineMat == null) return;
        _outlineMat.SetColor("_OutlineColor", outlineColor);
        _outlineMat.SetFloat("_OutlineWidth", outlineWidth);
    }
}
