using System.Collections.Generic;
using UnityEngine;

// See-through round the pawn. When a tree crown, a roof or a ledge stands
// between the camera and the pawn, it opens up — the way a third-person camera
// cuts away the wall between it and the player — so the pawn and the path round
// it can always be seen.
//
// The cut itself is in the shaders (Shader/Include/MapOcclusion.hlsl): a cone
// from the camera to the pawn, dithered at its rim, taking only what is in front
// of the pawn AND higher than its floor. This side just feeds it the pawn's
// position every frame, and gives the map's cubes a copy of URP Lit that knows
// the cut (GeoWorld/LitOccludable) — their own template uses URP's default Lit.
// Trees (Foliage), props (SilkscreenFlat) and the cube outlines (BlockOutline)
// have the cut built in.
public partial class LevelMapController
{
    [Header("See-through (pawn)")]
    [Tooltip("Cut away whatever stands between the camera and the pawn, so the pawn and the path round it are never hidden. The player can also turn it off in Settings (GameSettings.SeeThrough); both must be on.")]
    public bool seeThrough = true;
    [Tooltip("Radius of the fully clear window round the pawn, in cells (measured at the pawn — the window is the same size on screen at every depth).")]
    [Range(0.5f, 6f)] public float seeThroughRadius = 2f;
    [Tooltip("Extra cells beyond that over which things dither back in, so the window has no hard rim.")]
    [Range(0.2f, 4f)] public float seeThroughSoft = 1.4f;
    [Tooltip("How far in front of the pawn, in cells, the cut begins — nothing right beside it is eaten. Also the height above the pawn's floor over which the cut fades in.")]
    [Range(0.2f, 3f)] public float seeThroughMargin = 0.8f;
    [Tooltip("Only what stands this many cells above the pawn's floor is cut — the path under it and the ground in front of it stay.")]
    [Range(0f, 2f)] public float seeThroughFloor = 0.2f;
    [Tooltip("URP Lit with the see-through cut (GeoWorld/LitOccludable), put on the map's cubes at runtime. Referenced here so builds include it.")]
    public Shader occludableLitShader;

    static readonly int MapOccludeId      = Shader.PropertyToID("_MapOcclude");
    static readonly int MapOccludeShapeId = Shader.PropertyToID("_MapOccludeShape");
    float _seeThroughK;

    // The cube template is a scene object (parked at y = -999), so its material
    // can be swapped for this run without touching any asset. If it is ever a
    // prefab ASSET instead, it is left alone: editing it in play mode would stick.
    void SetupSeeThroughMaterial()
    {
        if (!Application.isPlaying || cubePrefab == null || !cubePrefab.scene.IsValid()) return;
        var sh = occludableLitShader != null ? occludableLitShader : Shader.Find("GeoWorld/LitOccludable");
        if (sh == null) return;

        var copies = new Dictionary<Material, Material>();
        foreach (var r in cubePrefab.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || m.shader == null || m.shader.name != "Universal Render Pipeline/Lit") continue;
                if (!copies.TryGetValue(m, out var copy))
                {
                    copy = new Material(sh) { name = m.name + " (see-through)", hideFlags = HideFlags.DontSave };
                    copy.CopyPropertiesFromMaterial(m);
                    copies[m] = copy;
                }
                mats[i] = copy;
                changed = true;
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    // Eased in and out: off while the reveal cutscene or a minigame has the
    // camera (the pawn isn't what's being looked at), on the rest of the time.
    void UpdateSeeThrough()
    {
        if (pawn == null || gridSystem == null)
        {
            ClearSeeThrough();
            return;
        }

        bool want = seeThrough && GameSettings.SeeThrough && !_decorCutscenePlaying && !MinigameStage.AnyActive && !BlockTetris3D.Active;
        _seeThroughK = Mathf.MoveTowards(_seeThroughK, want ? 1f : 0f, Time.unscaledDeltaTime * 3f);
        if (_seeThroughK <= 0f)
        {
            Shader.SetGlobalVector(MapOccludeId, Vector4.zero);
            return;
        }

        float cs    = gridSystem.cellSize;
        float floor = SurfaceTop(_currentCell).y;
        // The pawn's body, not its pivot, and without the idle bob — a window
        // that breathed with the bob would shimmer along its rim.
        var body = new Vector3(pawn.position.x, floor + cs * 0.45f, pawn.position.z);

        Shader.SetGlobalVector(MapOccludeId, new Vector4(body.x, body.y, body.z, _seeThroughK));
        Shader.SetGlobalVector(MapOccludeShapeId, new Vector4(
            seeThroughRadius * cs,
            (seeThroughRadius + seeThroughSoft) * cs,
            floor + seeThroughFloor * cs,
            seeThroughMargin * cs));
    }

    // Globals outlive the scene: clear them so no other scene inherits the cut.
    void ClearSeeThrough()
    {
        _seeThroughK = 0f;
        Shader.SetGlobalVector(MapOccludeId, Vector4.zero);
    }
}
