using System;
using System.IO;
using System.Linq;
using UnityEngine;

// Capture / restore the full build-state of the grid.
// Dev-only entry now; same data format will back the player-facing keepsake.
public static class SnapshotManager
{
    static string Dir => Path.Combine(Application.persistentDataPath, "snapshots");

    // ── Capture ──────────────────────────────────────────────────────────────
    public static GridSnapshot Capture()
    {
        var snap = new GridSnapshot
        {
            version   = 2,
            timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"),
        };
        snap.name = $"snapshot_{snap.timestamp}";

        var gfm  = GameFlowManager.Instance;
        var grid = gfm?.gridSystem;
        if (grid == null) return snap;

        snap.roundIndex            = gfm.RoundIndex;
        snap.runsSinceLastEndpoint = gfm.RunsSinceLastEndpoint;

        foreach (var ins in grid.GetAllInstances())
        {
            if (ins?.visualObject == null || ins.data == null) continue;

            var firstRend = ins.visualObject.GetComponentInChildren<Renderer>();
            snap.blocks.Add(new BlockSnapshot {
                blockTypeName  = ins.data.blockType.ToString(),
                color          = MpbColor.Get(firstRend),
                rotation       = ins.visualObject.transform.rotation,
                occupiedCells  = ins.occupiedCells.ToArray(),
                blockAssetName = ins.data.name,
                synergyColor   = ins.color,
                upBasicPower   = ins.basicPowerUpgradeLevel,
                upBasicBurst   = ins.basicBurstUpgradeLevel,
                upAoeFire      = ins.aoeFireUpgradeLevel,
                upAoeGravity   = ins.aoeGravityUpgradeLevel,
            });
        }

        foreach (var c in gfm.AllStarts)
            snap.endpoints.Add(new EndpointSnapshot { cell = c, isStart = true });
        foreach (var c in gfm.AllEnds)
            snap.endpoints.Add(new EndpointSnapshot { cell = c, isStart = false });

        var cam = UnityEngine.Object.FindFirstObjectByType<OrbitCamera>();
        if (cam != null)
        {
            snap.camera.focusPoint = cam.FocusPoint;
            snap.camera.distance   = cam.distance;
            snap.camera.yaw        = cam.Yaw;
            snap.camera.pitch      = cam.Pitch;
        }

        return snap;
    }

    // ── Restore ──────────────────────────────────────────────────────────────
    public static void Restore(GridSnapshot snap)
    {
        if (snap == null) { Debug.LogWarning("[Snapshot] null snapshot"); return; }

        var gfm   = GameFlowManager.Instance;
        var pc    = PlacementController.Instance;
        if (gfm == null || pc == null)
        {
            Debug.LogError("[Snapshot] missing manager singleton");
            return;
        }

        gfm.WipeAll();

        var starts = snap.endpoints.Where(e => e.isStart).Select(e => e.cell).ToList();
        var ends   = snap.endpoints.Where(e => !e.isStart).Select(e => e.cell).ToList();
        foreach (var c in starts) gfm.endpoints.SpawnEndpointAt(c, true);
        foreach (var c in ends)   gfm.endpoints.SpawnEndpointAt(c, false);

        PlaceBlocks(snap, inherited: false, withUpgrades: true);

        gfm.RestoreRoundState(snap.roundIndex, snap.runsSinceLastEndpoint, starts, ends);

        var cam = UnityEngine.Object.FindFirstObjectByType<OrbitCamera>();
        if (cam != null && snap.camera != null)
            cam.ApplyState(snap.camera.focusPoint, snap.camera.distance, snap.camera.yaw, snap.camera.pitch);

        gfm.EvaluateGrid();
    }

    // Place a snapshot's BLOCKS — not its endpoints — and return what was placed.
    //
    // Shared by the dev restore above and by chapter inheritance, so there is one
    // way to turn a snapshot back into a board. Two paths that each resolve blocks
    // their own way is how a restore and an inheritance end up disagreeing about
    // what the same snapshot contains.
    //
    // Cells already taken are skipped rather than overwritten, so this can run onto
    // a board that already has something on it.
    public static System.Collections.Generic.List<PlacedBlockInstance> PlaceBlocks(
        GridSnapshot snap, bool inherited, bool withUpgrades)
    {
        var placed = new System.Collections.Generic.List<PlacedBlockInstance>();
        var pc   = PlacementController.Instance;
        var grid = GameFlowManager.Instance?.gridSystem;
        if (snap?.blocks == null || pc == null || grid == null) return placed;

        foreach (var b in snap.blocks)
        {
            if (b?.occupiedCells == null || b.occupiedCells.Length == 0) continue;

            bool clash = false;
            foreach (var c in b.occupiedCells) if (grid.IsOccupied(c)) { clash = true; break; }
            if (clash) continue;

            var data = Resolve(pc, b);
            if (data == null)
            {
                Debug.LogWarning($"[Snapshot] can't resolve block '{b.blockAssetName}'/'{b.blockTypeName}', skipped.");
                continue;
            }

            var ins = pc.PlaceBlockDirect(data, b.occupiedCells, b.rotation, b.color, SynergyOf(snap, b),
                                          -1,
                                          withUpgrades ? b.upBasicPower : 0,
                                          withUpgrades ? b.upBasicBurst : 0,
                                          withUpgrades ? b.upAoeFire    : 0,
                                          withUpgrades ? b.upAoeGravity : 0);
            if (ins == null) continue;

            ins.inherited = inherited;
            placed.Add(ins);
        }
        return placed;
    }

    // The exact asset when the snapshot names one (v2); by type otherwise (v1).
    static BlockData Resolve(PlacementController pc, BlockSnapshot b)
    {
        var data = !string.IsNullOrEmpty(b.blockAssetName) ? pc.FindBlockDataByName(b.blockAssetName) : null;
        if (data == null && Enum.TryParse<BlockType>(b.blockTypeName, out var bt)) data = pc.FindBlockData(bt);
        return data;
    }

    // v2 stores the tag. v1 only has the display colour, which IS the palette colour
    // of the piece's tag — so snap it back onto the palette. The one thing that
    // cannot be recovered that way is Universal: an untagged block is drawn grey,
    // and grey snaps to Universal, which would turn every plain block into a joker.
    // So a grey v1 block comes back untagged, and a genuine v1 Universal piece loses
    // its tag. That is the smaller of the two errors.
    static BlockColor SynergyOf(GridSnapshot snap, BlockSnapshot b)
    {
        if (snap.version >= 2) return b.synergyColor;
        var t = BlockColorPalette.Nearest(b.color);
        return t == BlockColor.Universal ? BlockColor.None : t;
    }

    // ── File IO ──────────────────────────────────────────────────────────────
    public static string SaveToFile(GridSnapshot snap, string nameOverride = null)
    {
        if (snap == null) return null;
        if (!string.IsNullOrEmpty(nameOverride)) snap.name = nameOverride;

        Directory.CreateDirectory(Dir);
        var path = Path.Combine(Dir, snap.name + ".json");
        var json = JsonUtility.ToJson(snap, prettyPrint: true);
        File.WriteAllText(path, json);
        Debug.Log($"[Snapshot] saved → {path}");
        return path;
    }

    public static GridSnapshot LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[Snapshot] file not found: {path}");
            return null;
        }
        var json = File.ReadAllText(path);
        var snap = JsonUtility.FromJson<GridSnapshot>(json);
        Debug.Log($"[Snapshot] loaded ← {path}");
        return snap;
    }

    public static string LatestPath()
    {
        if (!Directory.Exists(Dir)) return null;
        return Directory.EnumerateFiles(Dir, "*.json")
            .OrderByDescending(File.GetLastWriteTime)
            .FirstOrDefault();
    }

    // Quick save / load helpers used by hotkeys.
    public static string QuickSave()
    {
        var snap = Capture();
        var path = SaveToFile(snap);
        SaveThumbnail(snap.name);
        return path;
    }

    public static bool QuickLoad()
    {
        var path = LatestPath();
        if (path == null) { Debug.LogWarning("[Snapshot] no snapshots to load"); return false; }
        var snap = LoadFromFile(path);
        if (snap == null) return false;
        Restore(snap);
        return true;
    }

    // Renders the main camera off-screen into a PNG next to the JSON.
    // OnGUI panels aren't captured (they don't render through cameras).
    public static void SaveThumbnail(string snapshotName, int width = 256, int height = 256, Camera cam = null)
    {
        cam = cam != null ? cam : Camera.main;
        if (cam == null) { Debug.LogWarning("[Snapshot] no Camera.main for thumbnail"); return; }

        var rt = RenderTexture.GetTemporary(width, height, 24);
        var prevTarget = cam.targetTexture;
        var prevActive = RenderTexture.active;

        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prevTarget;

        RenderTexture.active = rt;
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();
        RenderTexture.active = prevActive;
        RenderTexture.ReleaseTemporary(rt);

        var bytes = tex.EncodeToPNG();
        UnityEngine.Object.Destroy(tex);

        Directory.CreateDirectory(Dir);
        File.WriteAllBytes(Path.Combine(Dir, snapshotName + ".png"), bytes);
    }

    public static void Delete(string jsonPath)
    {
        if (string.IsNullOrEmpty(jsonPath)) return;
        try
        {
            if (File.Exists(jsonPath)) File.Delete(jsonPath);
            var png = Path.ChangeExtension(jsonPath, ".png");
            if (File.Exists(png)) File.Delete(png);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Snapshot] delete failed: {e.Message}");
        }
    }

    // Enumerate all snapshots, newest first.
    public static System.Collections.Generic.List<string> ListAll()
    {
        var list = new System.Collections.Generic.List<string>();
        if (!Directory.Exists(Dir)) return list;
        list.AddRange(Directory.EnumerateFiles(Dir, "*.json")
            .OrderByDescending(File.GetLastWriteTime));
        return list;
    }
}
