using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public partial class ChapterEnvironmentController
{
    GameObject _markers;
    readonly Dictionary<Vector3Int, GameObject> _puddleMarkers = new();
    Material _markerMaterial;
    GUIStyle _titleStyle, _descriptionStyle;

    void RebuildMarkers()
    {
        ClearMarkers();
        _markerMaterial = Resources.Load<Material>("GeoWorldShaderKeepalive/EnvironmentMarker_keep");
        if (_markerMaterial == null || State == null || GridSystem.instance == null) return;
        _markers = new GameObject("Environment rule regions");
        _markers.transform.SetParent(transform, false);
        float size = GridSystem.instance.cellSize;
        foreach (var center in State.mistCenters)
        {
            float radius = Mathf.Max(0.1f, Profile.mistRadius) * size;
            var sphere = Primitive(PrimitiveType.Sphere, center, Vector3.one * radius * 2f,
                                   new Color(0.55f, 0.72f, 0.83f, 0.045f));
            sphere.name = "Rain mist range penalty";
            for (int axis = 0; axis < 3; axis++)
            {
                var ring = new GameObject("Boundary").AddComponent<LineRenderer>();
                ring.transform.SetParent(_markers.transform, false);
                ring.sharedMaterial = _markerMaterial;
                ring.startColor = ring.endColor = new Color(0.45f, 0.72f, 0.86f, 0.32f);
                ring.widthMultiplier = size * 0.018f;
                ring.positionCount = 65; ring.useWorldSpace = true;
                ring.shadowCastingMode = ShadowCastingMode.Off; ring.receiveShadows = false;
                for (int i = 0; i < 65; i++)
                {
                    float a = i * Mathf.PI * 2f / 64f;
                    float x = Mathf.Cos(a) * radius, y = Mathf.Sin(a) * radius;
                    ring.SetPosition(i, center + (axis == 0 ? new Vector3(x, 0, y) : axis == 1 ? new Vector3(x, y, 0) : new Vector3(0, x, y)));
                }
            }
        }
        foreach (var cell in State.puddleCells)
        {
            var pos = GridSystem.instance.GridToWorld(cell) + Vector3.up * size * 0.512f;
            var marker = Primitive(PrimitiveType.Cylinder, pos, new Vector3(size * 0.72f, size * 0.003f, size * 0.72f),
                                   new Color(0.26f, 0.66f, 0.84f, 0.33f));
            marker.name = "Puddle top face " + cell;
            _puddleMarkers[cell] = marker;
        }
    }

    GameObject Primitive(PrimitiveType type, Vector3 position, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        go.transform.SetParent(_markers.transform, false);
        go.transform.position = position; go.transform.localScale = scale;
        var collider = go.GetComponent<Collider>();
        collider.enabled = false; ReleaseObject(collider);
        var renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = _markerMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        var props = new MaterialPropertyBlock(); props.SetColor("_BaseColor", color); renderer.SetPropertyBlock(props);
        return go;
    }

    void RefreshPuddleMarkers()
    {
        foreach (var pair in _puddleMarkers)
            if (pair.Value != null) pair.Value.SetActive(_activePuddles.Contains(pair.Key));
    }

    void ClearMarkers()
    {
        if (_markers != null) { _markers.SetActive(false); ReleaseObject(_markers); }
        _markers = null;
        _puddleMarkers.Clear();
    }

    public string EffectName => Current switch {
        ChapterWeather.RainMist => "Rain Mist", ChapterWeather.Puddles => "Puddles",
        ChapterWeather.BlockRain => "Block Rain", _ => "Calm"
    };

    void OnGUI()
    {
        if (Profile == null || State == null || Current == ChapterWeather.None || IntroDirector.Playing
            || GameFlowManager.SettlementUp || PauseMenu.Paused) return;
        _titleStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        _descriptionStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, alignment = TextAnchor.MiddleCenter };
        _titleStyle.normal.textColor = GeoPalette.Paper;
        _descriptionStyle.normal.textColor = GeoPalette.Paper;
        var oldMatrix = GUI.matrix;
        float scale = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
        GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
        float x = Screen.width / scale * 0.5f - 255f;
        var oldColor = GUI.color;
        GUI.color = new Color(0.07f, 0.12f, 0.16f, 0.78f);
        GUI.DrawTexture(new Rect(x, 88f, 510f, 72f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(x + 10f, 91f, 490f, 25f), Profile.displayName + " / " + EffectName, _titleStyle);
        string description = Current switch {
            ChapterWeather.RainMist => $"Marked regions: turret range -{(1f - Profile.mistRangeMultiplier) * 100f:0}%.",
            ChapterWeather.Puddles => $"Wet top faces: enemy speed -{(1f - Profile.puddleSpeedMultiplier) * 100f:0}%.",
            ChapterWeather.BlockRain => $"Catch free building blocks during combat. {RemainingDrops} drops remaining.\nClick to catch / Tab to release / no sale refund.",
            _ => ""
        };
        GUI.Label(new Rect(x + 10f, 116f, 490f, 42f), description, _descriptionStyle);
        GUI.color = oldColor; GUI.matrix = oldMatrix;
    }
}
