using System.Collections.Generic;
using UnityEngine;

// The scaffolding every minigame overlay needs, independent of what the game IS.
//
// Minigames run INSIDE the scene that launched them rather than as their own
// scene, which buys a free return trip (pawn position, dialogue state and all
// survive untouched) but means the host is still there underneath: its canvases
// still draw, its music still plays, its skybox and fog are still the scene's.
// Each of those has to be taken over on entry and handed back on exit, and
// getting the hand-back wrong strands the map in a minigame's lighting.
//
// One instance per running minigame; call Capture* on entry and Restore() on exit.
public class MinigameStage
{
    // One place for the host to ask "is a minigame up?", so adding a third game
    // doesn't mean hunting down every `SomeGame.Active` check in the codebase.
    public static bool AnyActive => BlockTetris3D.Active || BalanceTower.Active || Clepsydra.Active;

    // ── Host UI ──────────────────────────────────────────────────────────────

    readonly List<Canvas> _suppressed = new();

    // Host UI is ScreenSpaceOverlay, so it ignores the minigame camera's depth and
    // would draw straight over it. `ours` is exempt.
    public void SuppressHostUI(Transform ours)
    {
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (c == null || !c.enabled) continue;
            if (ours != null && c.transform.IsChildOf(ours)) continue;
            c.enabled = false;
            _suppressed.Add(c);
        }
    }

    // ── Skybox + fog ─────────────────────────────────────────────────────────

    Material _hostSkybox;
    bool     _skyboxSwapped;
    bool     _hostFog;
    Color    _hostFogColor;
    float    _hostFogStart, _hostFogEnd;
    bool     _fogCaptured;

    public void SetSkybox(Material sky)
    {
        if (sky == null) return;
        if (!_skyboxSwapped) { _hostSkybox = RenderSettings.skybox; _skyboxSwapped = true; }
        RenderSettings.skybox = sky;
    }

    public void SetLinearFog(Color color, float start, float end)
    {
        if (!_fogCaptured)
        {
            _hostFog      = RenderSettings.fog;
            _hostFogColor = RenderSettings.fogColor;
            _hostFogStart = RenderSettings.fogStartDistance;
            _hostFogEnd   = RenderSettings.fogEndDistance;
            _fogCaptured  = true;
        }
        RenderSettings.fog              = true;
        RenderSettings.fogMode          = FogMode.Linear;
        RenderSettings.fogColor         = color;
        RenderSettings.fogStartDistance = start;
        RenderSettings.fogEndDistance   = end;
    }

    // ── Host music ───────────────────────────────────────────────────────────

    readonly List<uint> _pausedMusicIds = new();

    // Pauses rather than stops, so the host picks its track back up mid-phrase.
    // Targeted BY EVENT (AK_INVALID_GAME_OBJECT = "wherever it's playing") rather
    // than by ducking a bus RTPC: a minigame can be launched from more than one
    // scene, and a bus-wide duck has no way to tell the host's music apart from the
    // minigame's own if they ever share a bus.
    public void PauseHostMusic()
    {
        _pausedMusicIds.Clear();
        TryPause(AudioManager.Instance != null ? AudioManager.Instance.BGM : null);
        TryPause(AudioManager.Instance != null ? AudioManager.Instance.BGM_fight : null);
        TryPause(LevelMapController.Instance != null ? LevelMapController.Instance.ActiveLoop : null);
    }

    void TryPause(AK.Wwise.Event evt)
    {
        if (evt == null || !evt.IsValid()) return;
        AkUnitySoundEngine.ExecuteActionOnEvent(evt.Id, AkActionOnEventType.AkActionOnEventType_Pause,
            AkUnitySoundEngine.AK_INVALID_GAME_OBJECT, 200, AkCurveInterpolation.AkCurveInterpolation_Linear);
        _pausedMusicIds.Add(evt.Id);
    }

    // ── Hand everything back ─────────────────────────────────────────────────

    /// <summary>
    /// Hand the host's music back WITHOUT restoring anything else. Called when the
    /// stage is going away for a reason other than the player leaving the minigame —
    /// a scene change, or the object being destroyed. Music left paused across a
    /// scene load is an instance nobody owns any more, and whoever stops it next
    /// stops a paused segment, which is what trips the music engine's sequencer
    /// assert.
    /// </summary>
    public void ResumeHostMusic()
    {
        foreach (var id in _pausedMusicIds)
            AkUnitySoundEngine.ExecuteActionOnEvent(id, AkActionOnEventType.AkActionOnEventType_Resume,
                AkUnitySoundEngine.AK_INVALID_GAME_OBJECT, 0, AkCurveInterpolation.AkCurveInterpolation_Linear);
        _pausedMusicIds.Clear();
    }

    public void Restore()
    {
        foreach (var c in _suppressed) if (c != null) c.enabled = true;
        _suppressed.Clear();

        if (_skyboxSwapped) { RenderSettings.skybox = _hostSkybox; _skyboxSwapped = false; }

        if (_fogCaptured)
        {
            RenderSettings.fog              = _hostFog;
            RenderSettings.fogColor         = _hostFogColor;
            RenderSettings.fogStartDistance = _hostFogStart;
            RenderSettings.fogEndDistance   = _hostFogEnd;
            _fogCaptured = false;
        }

        // Resumes exactly what we paused — never more, never less.
        foreach (var id in _pausedMusicIds)
            AkUnitySoundEngine.ExecuteActionOnEvent(id, AkActionOnEventType.AkActionOnEventType_Resume,
                AkUnitySoundEngine.AK_INVALID_GAME_OBJECT, 400, AkCurveInterpolation.AkCurveInterpolation_Linear);
        _pausedMusicIds.Clear();
    }
}
