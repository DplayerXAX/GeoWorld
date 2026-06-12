using System.Collections.Generic;
using UnityEngine;

public partial class PlacementController
{
    // =========================
    // TURRET RANGE SPHERE
    // =========================

    // Driven every frame from Update. Shows a faint translucent range bubble at the
    // selected turret's MUZZLE (where line-of-sight is cast from) plus a RED
    // translucent "shadow volume" filling the parts of the range the turret can't
    // shoot into — blocks occlude the shot, like a light casting shadows. Hidden
    // when no turret is selected.
    void UpdateRangeIndicator()
    {
        if (mode == PlacementMode.Select
            && selectedInstance != null
            && selectedInstance.visualObject != null
            && selectedInstance.data != null
            && TurretTypes.Is(selectedInstance.data.blockType))
        {
            var turret = selectedInstance.visualObject.GetComponentInChildren<TurretController>();
            if (turret != null && turret.attackRange > 0f)
            {
                Vector3 muzzle = turret.MuzzleWorldPosition;

                // Pop-in: grow the bubble + shadow from the muzzle when a new turret
                // is selected. EaseOutCubic (no overshoot) keeps the range honest.
                if (selectedInstance != _rangeShownFor) { _rangeAnimStart = Time.time; _rangeShownFor = selectedInstance; }
                float rt  = selectionPopDuration > 1e-4f ? (Time.time - _rangeAnimStart) / selectionPopDuration : 1f;
                float pop = EaseOutCubic(Mathf.Clamp01(rt));

                ShowRangeSphere(muzzle, turret.attackRange, pop);
                UpdateShadowVolume(turret, muzzle, pop);
                return;
            }
        }
        HideRangeIndicator();
    }

    void ShowRangeSphere(Vector3 muzzle, float range, float pop)
    {
        EnsureRangeSphere();
        if (!_rangeSphere.activeSelf) _rangeSphere.SetActive(true);
        _rangeSphere.transform.position   = muzzle;
        _rangeSphere.transform.localScale = Vector3.one * (range * 2f * pop);   // primitive sphere ⌀1 → 2·r, grows from muzzle
    }

    // Rebuilt only when the selected turret changes — the board is static while
    // inspecting in Select mode, so once per selection is exact AND cheap (pure
    // cube geometry, no raycasts).
    void UpdateShadowVolume(TurretController turret, Vector3 muzzle, float pop)
    {
        if (selectedInstance != _shadowFor)
        {
            BuildShadowVolume(turret, muzzle);
            _shadowFor = selectedInstance;
        }
        if (_rangeShadow != null)
        {
            if (!_rangeShadow.activeSelf) _rangeShadow.SetActive(true);
            _rangeShadow.transform.localScale = Vector3.one * pop;   // grow from the muzzle with the bubble
        }
    }

    // Builds the shadow from the block CUBE GEOMETRY directly (no ray sampling, so
    // no stair-stepped edges): every occluding block cell within range projects its
    // silhouette — as seen from the muzzle — out to the range edge, giving a clean
    // hexagonal shadow frustum (silhouette side walls + a far cap). The union of all
    // cells' frustums is the unreachable region. Cube cells match the LOS test's
    // cube colliders, so it still mirrors what the turret can actually hit.
    // Vertices are local to the muzzle. Pure geometry → cheap, rebuilt per selection.
    static readonly Vector3Int[] _axisStep = { Vector3Int.right, Vector3Int.up, new Vector3Int(0, 0, 1) };

    void BuildShadowVolume(TurretController turret, Vector3 muzzle)
    {
        EnsureRangeShadow();
        float range = turret.attackRange;

        var verts = new List<Vector3>();
        var cols  = new List<Color>();
        var tris  = new List<int>();

        var grid = GridSystem.instance;
        if (grid != null)
        {
            float half = grid.cellSize * 0.5f;
            float r2   = range * range;

            // Solid cells that actually occlude line of sight = every placed block
            // EXCEPT the firing turret's own (the LOS test ignores its own block).
            var occ = new HashSet<Vector3Int>();
            foreach (var ins in grid.GetAllInstances())
            {
                if (ins == null || ins == selectedInstance || ins.occupiedCells == null) continue;
                foreach (var c in ins.occupiedCells) occ.Add(c);
            }

            foreach (var cell in occ)
            {
                Vector3 C = grid.GridToWorld(cell);
                // Skip cubes fully beyond range — they cast no shadow inside it.
                Vector3 cl = new Vector3(
                    Mathf.Clamp(muzzle.x, C.x - half, C.x + half),
                    Mathf.Clamp(muzzle.y, C.y - half, C.y + half),
                    Mathf.Clamp(muzzle.z, C.z - half, C.z + half));
                if ((cl - muzzle).sqrMagnitude > r2) continue;

                AddCubeShadow(cell, C, half, muzzle, range, rangeBlockedColor, occ, verts, cols, tris);
            }
        }

        if (_rangeShadowMesh == null) _rangeShadowMesh = new Mesh { name = "TurretRangeShadow" };
        _rangeShadowMesh.Clear();
        _rangeShadowMesh.indexFormat = verts.Count > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        _rangeShadowMesh.SetVertices(verts);
        _rangeShadowMesh.SetColors(cols);
        _rangeShadowMesh.SetTriangles(tris, 0);
        _rangeShadowMesh.bounds = new Bounds(Vector3.zero, Vector3.one * (range * 2.2f));

        _rangeShadow.GetComponent<MeshFilter>().sharedMesh = _rangeShadowMesh;
        _rangeShadow.transform.SetPositionAndRotation(muzzle, Quaternion.identity);
        _rangeShadow.transform.localScale = Vector3.one;
    }

    // Projects one cube's silhouette (from M) out to `range`: for every silhouette
    // edge (one adjacent face toward M, the other away) emit a side wall + a far-cap
    // triangle. Edges buried between two solid cells are skipped so adjacent cubes
    // merge into one clean shadow instead of growing interior walls. Local to M.
    static void AddCubeShadow(Vector3Int cell, Vector3 C, float half, Vector3 M, float range, Color col,
                              HashSet<Vector3Int> occ, List<Vector3> verts, List<Color> cols, List<int> tris)
    {
        bool Front(int axis, int sign)
        {
            float cAxis = axis == 0 ? C.x : axis == 1 ? C.y : C.z;
            float mAxis = axis == 0 ? M.x : axis == 1 ? M.y : M.z;
            return sign * (mAxis - cAxis) > half;   // muzzle beyond this face → it faces M
        }

        Vector3 Cfar = (C - M).normalized * range;
        int[] sgn = { -1, 1 };

        for (int a1 = 0; a1 < 3; a1++)
        for (int si = 0; si < 2; si++)
        {
            int  s1 = sgn[si];
            bool f1 = Front(a1, s1);
            bool covered1 = occ.Contains(cell + _axisStep[a1] * s1);

            for (int a2 = a1 + 1; a2 < 3; a2++)
            for (int sj = 0; sj < 2; sj++)
            {
                int s2 = sgn[sj];
                if (f1 == Front(a2, s2)) continue;                                  // not a silhouette edge
                if (covered1 && occ.Contains(cell + _axisStep[a2] * s2)) continue;  // buried inside solid → skip

                int a3 = 3 - a1 - a2;

                Vector3 o = Vector3.zero;
                o[a1] = s1 * half; o[a2] = s2 * half;
                o[a3] = -half; Vector3 v0 = (C + o) - M;
                o[a3] =  half; Vector3 v1 = (C + o) - M;

                Vector3 v0f = v0.normalized * range;
                Vector3 v1f = v1.normalized * range;

                AddQuad(verts, cols, tris, col, v0, v1, v1f, v0f);   // side wall (cube edge → range)
                AddTri (verts, cols, tris, col, Cfar, v0f, v1f);     // far-cap slice
            }
        }
    }

    static void AddQuad(List<Vector3> v, List<Color> c, List<int> t, Color col,
                        Vector3 a, Vector3 b, Vector3 cc, Vector3 d)
    {
        int s = v.Count;
        v.Add(a); v.Add(b); v.Add(cc); v.Add(d);
        c.Add(col); c.Add(col); c.Add(col); c.Add(col);
        t.Add(s); t.Add(s + 1); t.Add(s + 2);
        t.Add(s); t.Add(s + 2); t.Add(s + 3);
    }

    static void AddTri(List<Vector3> v, List<Color> c, List<int> t, Color col,
                       Vector3 a, Vector3 b, Vector3 cc)
    {
        int s = v.Count;
        v.Add(a); v.Add(b); v.Add(cc);
        c.Add(col); c.Add(col); c.Add(col);
        t.Add(s); t.Add(s + 1); t.Add(s + 2);
    }

    void HideRangeIndicator()
    {
        if (_rangeSphere != null && _rangeSphere.activeSelf) _rangeSphere.SetActive(false);
        if (_rangeShadow != null && _rangeShadow.activeSelf) _rangeShadow.SetActive(false);
        _shadowFor     = null;   // force a rebuild next time (blocks may have changed)
        _rangeShownFor = null;   // re-show pops in again
    }

    void EnsureRangeSphere()
    {
        if (_rangeSphere != null) return;

        _rangeSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _rangeSphere.name = "TurretRangeSphere";
        _rangeSphere.transform.SetParent(transform, worldPositionStays: false);

        var col = _rangeSphere.GetComponent<Collider>();
        if (col != null) Destroy(col);   // must not block selection / line-of-sight rays

        var rend = _rangeSphere.GetComponent<Renderer>();
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows    = false;
        rend.sharedMaterial    = BuildTransparentMaterial(rangeSphereColor);
    }

    void EnsureRangeShadow()
    {
        if (_rangeShadow != null) return;

        _rangeShadow = new GameObject("TurretRangeShadow");
        _rangeShadow.transform.SetParent(transform, worldPositionStays: false);
        _rangeShadow.AddComponent<MeshFilter>();

        // Sprites/Default: unlit, vertex-color, Cull Off, alpha-blended, ZWrite Off
        // — a translucent double-sided red volume (red + alpha come from the verts).
        var rend = _rangeShadow.AddComponent<MeshRenderer>();
        rend.shadowCastingMode    = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows       = false;
        rend.lightProbeUsage      = UnityEngine.Rendering.LightProbeUsage.Off;
        rend.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        var sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
        rend.sharedMaterial = new Material(sh);
    }

    // Builds an unlit, double-sided, alpha-blended material so the range bubble
    // reads cleanly from outside AND inside (camera may zoom into it). Mirrors
    // the URP transparent setup used by ConfigurePreviewMaterial.
    static Material BuildTransparentMaterial(Color color)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        var mat = new Material(sh);

        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f);   // 1 = Transparent
            mat.SetFloat("_ZWrite",  0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color", color);
        return mat;
    }

}
