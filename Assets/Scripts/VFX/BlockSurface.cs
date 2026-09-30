using System.Collections.Generic;
using UnityEngine;

// Keeps every board block's weathering overlay (GeoWorld/BlockWeather) in step with
// the board: which of each cube's 26 neighbouring cells are filled (the contact
// shadow) and how old its block is (the wear). Called after every board edit
// (GameFlowManager.EvaluateGrid) — cheap: a few dictionary lookups per cube.
//
// The overlay is an EXTRA material slot on each cube, added here, drawn over the
// block's own material. Only cubes built from the placement cube prefab get it —
// turret models and synergy decorations under a block are left alone.
public static class BlockSurface
{
    static Material _mat;
    static Mesh     _cubeMesh;
    static MaterialPropertyBlock _mpb;

    static readonly int Nbr0Id  = Shader.PropertyToID("_Nbr0");
    static readonly int Nbr1Id  = Shader.PropertyToID("_Nbr1");
    static readonly int AgeId   = Shader.PropertyToID("_Age01");
    static readonly int SeedId  = Shader.PropertyToID("_WearSeed");
    static readonly int CellId  = Shader.PropertyToID("_GeoCellSize");

    public static void Refresh(GridSystem grid)
    {
        if (grid == null) return;
        // Fog clearance, far-haze ring and rain splashes follow the board too.
        LevelEnvironmentDriver.NotifyBoard(grid);

        var mat = Mat();
        var cube = CubeMesh();
        if (mat == null || cube == null) return;

        Shader.SetGlobalFloat(CellId, grid.cellSize);
        _mpb ??= new MaterialPropertyBlock();
        var env = LevelEnvironmentDriver.Current != null ? LevelEnvironmentDriver.Current : LevelEnvironment.Default;

        foreach (var ins in grid.GetAllInstances())
        {
            if (ins?.visualObject == null) continue;
            float age = env.AgeToWear(ins.age);

            foreach (var mf in ins.visualObject.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh != cube) continue;
                var r = mf.GetComponent<MeshRenderer>();
                if (r == null) continue;
                if (!EnsureSlot(r, mat)) continue;

                var cell = grid.WorldToGrid(mf.transform.position);
                Mask(grid, cell, out float n0, out float n1);

                r.GetPropertyBlock(_mpb);
                _mpb.SetFloat(Nbr0Id, n0);
                _mpb.SetFloat(Nbr1Id, n1);
                _mpb.SetFloat(AgeId,  age);
                _mpb.SetFloat(SeedId, Hash(cell));
                r.SetPropertyBlock(_mpb);
            }
        }
    }

    // Adds the overlay as the last slot if it isn't there. Not while the cube wears
    // the hazard stripes (BoardValidity): the overlay would darken whatever shows
    // through their gaps.
    static bool EnsureSlot(MeshRenderer r, Material mat)
    {
        var mats = r.sharedMaterials;
        foreach (var m in mats)
        {
            if (m == mat) return true;
            if (m == null) continue;
            if (m.shader != null && m.shader.name == "GeoWorld/Hazard") return false;
            // A transparent synergy surface (CellMaterialVisuallizer): the overlay
            // would multiply onto whatever is behind the volume.
            if (m.renderQueue > 2500) return false;
        }
        var grown = new Material[mats.Length + 1];
        mats.CopyTo(grown, 0);
        grown[mats.Length] = mat;
        r.sharedMaterials = grown;
        return true;
    }

    // Occupancy of the 26 neighbours, 13 bits per float (exact), in the same
    // x,y,z nested order the shader walks.
    static void Mask(GridSystem grid, Vector3Int c, out float w0, out float w1)
    {
        int b0 = 0, b1 = 0, idx = 0;
        for (int x = -1; x <= 1; x++)
        for (int y = -1; y <= 1; y++)
        for (int z = -1; z <= 1; z++)
        {
            if (x == 0 && y == 0 && z == 0) continue;
            if (grid.IsOccupied(new Vector3Int(c.x + x, c.y + y, c.z + z)))
            {
                if (idx < 13) b0 |= 1 << idx; else b1 |= 1 << (idx - 13);
            }
            idx++;
        }
        w0 = b0; w1 = b1;
    }

    static float Hash(Vector3Int c)
    {
        unchecked
        {
            int h = c.x * 73856093 ^ c.y * 19349663 ^ c.z * 83492791;
            return (h & 0xFFFF) / 65535f;
        }
    }

    static Material Mat()
    {
        if (_mat != null) return _mat;
        var sh = Shader.Find("GeoWorld/BlockWeather");
        if (sh == null) { Debug.LogWarning("[BlockSurface] GeoWorld/BlockWeather shader not found."); return null; }
        _mat = new Material(sh) { name = "BlockWeather (runtime)" };
        return _mat;
    }

    static Mesh CubeMesh()
    {
        if (_cubeMesh != null) return _cubeMesh;
        var pc = PlacementController.Instance;
        var mf = pc != null && pc.cubePrefab != null ? pc.cubePrefab.GetComponentInChildren<MeshFilter>() : null;
        _cubeMesh = mf != null ? mf.sharedMesh : null;
        return _cubeMesh;
    }
}
