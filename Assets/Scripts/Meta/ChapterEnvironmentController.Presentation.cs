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
                                   new Color(0.65f, 0.81f, 0.9f, 0.12f));
            sphere.name = "Rain mist range penalty";
            var mistMaterial = Resources.Load<Material>("GeoWorldShaderKeepalive/RainMist_keep");
            if (mistMaterial != null)
            {
                MistBank.EnsureDepthTexture();
                var renderer = sphere.GetComponent<Renderer>();
                renderer.sharedMaterial = mistMaterial;
                var props = new MaterialPropertyBlock();
                props.SetColor("_BaseColor", new Color(0.65f, 0.81f, 0.9f, Mathf.Clamp01(Profile.mistOpacity)));
                props.SetVector("_MistCenter", center);
                props.SetFloat("_MistRadius", radius);
                renderer.SetPropertyBlock(props);
            }
            CreateRain(center, radius, size);
            for (int axis = 0; axis < 3; axis++)
            {
                var ring = new GameObject("Boundary").AddComponent<LineRenderer>();
                ring.transform.SetParent(_markers.transform, false);
                ring.sharedMaterial = _markerMaterial;
                ring.startColor = ring.endColor = new Color(0.48f, 0.8f, 0.96f, 0.65f);
                ring.widthMultiplier = size * 0.035f;
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

    void CreateRain(Vector3 center, float radius, float size)
    {
        if (Profile.rainRate <= 0f) return;
        var go = new GameObject("Rain mist streaks");
        go.transform.SetParent(_markers.transform, false);
        go.transform.position = center + Vector3.up * radius;
        var rain = go.AddComponent<ParticleSystem>();
        rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = rain.main;
        main.loop = true;
        main.duration = 2f;
        main.startLifetime = radius * 2f / (size * 9f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.025f, size * 0.045f);
        main.startColor = new Color(0.68f, 0.86f, 1f, 0.8f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 256;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var emission = rain.emission;
        emission.rateOverTime = Mathf.Min(Profile.rainRate, 240f);
        var shape = rain.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius * 0.85f;
        shape.rotation = new Vector3(90f, 0f, 0f);
        var velocity = rain.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = size * 0.35f;
        velocity.y = -size * 9f;
        velocity.z = size * 0.15f;
        var fade = rain.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f),
                    new GradientAlphaKey(0.75f, 0.75f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        var renderer = rain.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = _markerMaterial;
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.025f;
        renderer.lengthScale = 5f;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        // Cosmetic randomness is independent of the saved gameplay RNG.
        rain.useAutoRandomSeed = false;
        rain.randomSeed = (uint)(State.mistCenters.IndexOf(center) + 1);
        if (Application.isPlaying)
        {
            rain.Simulate(main.startLifetime.constantMax, true, true);
            rain.Play();
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
        _titleStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        _descriptionStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true, alignment = TextAnchor.MiddleCenter };
        _titleStyle.normal.textColor = GeoPalette.Paper;
        _descriptionStyle.normal.textColor = GeoPalette.Paper;
        var oldMatrix = GUI.matrix;
        float scale = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
        GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
        float x = Screen.width / scale * 0.5f - 310f;
        var hud = TopLeftHUD.Instance;
        float y = hud != null ? (hud.topMargin + hud.topPanelSize.y) * (Screen.height / 1080f) / scale + 12f : 88f;
        var oldColor = GUI.color;
        GUI.color = new Color(0.07f, 0.12f, 0.16f, 0.78f);
        GUI.DrawTexture(new Rect(x, y, 620f, 90f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(x + 10f, y + 3f, 600f, 29f), Profile.displayName + " / " + EffectName, _titleStyle);
        string description = Current switch {
            ChapterWeather.RainMist => $"Marked regions: turret range -{(1f - Profile.mistRangeMultiplier) * 100f:0}%.",
            ChapterWeather.Puddles => $"Wet top faces: enemy speed -{(1f - Profile.puddleSpeedMultiplier) * 100f:0}%.",
            ChapterWeather.BlockRain => $"Catch free building blocks during combat. {RemainingDrops} drops remaining.\nClick to catch / Tab to release / no sale refund.",
            _ => ""
        };
        GUI.Label(new Rect(x + 10f, y + 33f, 600f, 54f), description, _descriptionStyle);
        GUI.color = oldColor; GUI.matrix = oldMatrix;
    }
}
