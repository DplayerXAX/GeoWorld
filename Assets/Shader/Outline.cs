using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Outline : MonoBehaviour
{
    public Color OutlineColor { get => outlineColor; set { outlineColor = value; ApplyProperties(); } }
    public float OutlineWidth { get => outlineWidth; set { outlineWidth = value; ApplyProperties(); } }

    [SerializeField] private Color outlineColor = Color.yellow;
    [SerializeField, Range(0f, 10f)] private float outlineWidth = 4f;

    private List<Renderer> _renderers;
    private Material _outlineMaterial;

    void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>().ToList();
        // 动态创建一个简单的描边Shader，防止紫色报错
        _outlineMaterial = new Material(Shader.Find("Hidden/Internal-Colored"));
        _outlineMaterial.SetInt("_ZWrite", 0);
        _outlineMaterial.SetInt("_ZTest", 8); // Always
    }

    void OnEnable() => AddOutline();
    void OnDisable() => RemoveOutline();

    private void AddOutline()
    {
        foreach (var r in _renderers)
        {
            var mats = r.sharedMaterials.ToList();
            // 这里使用简易的颜色叠加方案实现高亮效果
            Material highlightMat = new Material(Shader.Find("Unlit/Color"));
            highlightMat.color = outlineColor;
            mats.Add(highlightMat);
            r.materials = mats.ToArray();
        }
    }

    private void RemoveOutline()
    {
        foreach (var r in _renderers)
        {
            var mats = r.sharedMaterials.ToList();
            if (mats.Count > 1) mats.RemoveAt(mats.Count - 1);
            r.materials = mats.ToArray();
        }
    }

    private void ApplyProperties()
    {
        foreach (var r in _renderers)
        {
            if (r.materials.Length > 1)
                r.materials[r.materials.Length - 1].color = outlineColor;
        }
    }
}