using System;
using System.Collections.Generic;
using UnityEngine;

public enum ChapterWeather { None, RainMist, Puddles, BlockRain }

[CreateAssetMenu(menuName = "GeoWorld/Chapter Environment", fileName = "ChapterEnvironment")]
public class ChapterEnvironmentProfile : ScriptableObject
{
    public string themeId = "rain";
    public string displayName = "Rain";
    public ChapterWeather[] effects = { ChapterWeather.RainMist, ChapterWeather.Puddles, ChapterWeather.BlockRain };
    [Min(0)] public int mistCount = 3;
    [Min(0.1f)] public float mistRadius = 2.5f;
    [Range(0.1f, 1f)] public float mistRangeMultiplier = 0.75f;
    [Header("Rain mist presentation")]
    [Range(0f, 1f)] public float mistOpacity = 0.55f;
    [Min(0f)] public float rainRate = 85f;
    [Range(0f, 1f)] public float puddleFraction = 0.25f;
    [Min(0)] public int puddleLimit = 12;
    [Range(0.1f, 1f)] public float puddleSpeedMultiplier = 0.75f;
    [Min(0)] public int dropCount = 3;
    [Min(0f)] public float firstDropDelay = 1f;
    [Min(0.1f)] public float dropInterval = 6f;
    [Min(1f)] public float dropLifetime = 10f;

    public ChapterWeather Pick(Xoshiro256StarStar rng, ChapterWeather previous)
    {
        var available = new List<ChapterWeather>();
        if (effects != null)
            foreach (var effect in effects)
                if (effect != ChapterWeather.None && Enum.IsDefined(typeof(ChapterWeather), effect)
                    && !available.Contains(effect)) available.Add(effect);
        if (available.Count > 1) available.Remove(previous);
        return available.Count == 0 ? ChapterWeather.None : available[rng.NextInt(available.Count)];
    }
}

[Serializable]
public class EnvironmentDropPlan
{
    public string blockAssetName;
    public BlockColor color;
    public float viewportX;
}

[Serializable]
public class EnvironmentRunState
{
    public string themeId;
    public int wave;
    public ChapterWeather effect;
    public string[] randomState;
    public List<Vector3> mistCenters = new();
    public List<Vector3Int> puddleCells = new(); // Always the +Y face.
    public List<EnvironmentDropPlan> drops = new();
}
