#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

// Editor only: what each decor plot's ground will be, without building it. It
// gives the columns BuildDecor's ground pass would raise and how tall each one
// stands. LevelMapControllerEditor draws them, with the map asset's level
// blocks, so the whole world can be laid out in the Scene view without entering
// Play. EditorPlotTops mirrors the ground loop in BuildDecor, so keep the two in
// step.
public partial class LevelMapController
{
    public GridSystem EditorGrid => gridSystem;

    public LevelMapData EditorMapData() => mapAsset != null ? mapAsset.data : null;

    public List<MapDecorConfig> EditorPlots()
    {
        var list = new List<MapDecorConfig>();
        foreach (var c in AllDecorConfigs()) if (c != null) list.Add(c);
        return list;
    }

    // The top cell of every column the plot would cover.
    public List<Vector3Int> EditorPlotTops(MapDecorConfig cfg)
    {
        var tops = new List<Vector3Int>();
        if (cfg == null) return tops;

        var prev = _building;   // RotateLocal / RotatedExtent read it
        _building = cfg;
        try
        {
            int w = Mathf.Max(1, cfg.size.x);
            int d = Mathf.Max(1, cfg.size.y);
            var farm    = cfg as AbundanceFarmConfig;
            var ext     = RotatedExtent(w, d);
            var npcCol  = NpcColumn(cfg, ext);
            var gameCol = GameColumn(cfg, ext);

            for (int ix = 0; ix < w; ix++)
            for (int iz = 0; iz < d; iz++)
            {
                var rot = RotateLocal(ix, iz, w, d);
                var col = new Vector2Int(cfg.origin.x + rot.x, cfg.origin.z + rot.y);
                bool npcPedestal  = cfg.npc != null      && col == npcCol;
                bool gamePedestal = cfg.minigame != null && col == gameCol;

                var kind = farm != null ? RowKindAt(iz) : RowKind.Flower;
                float coverage = farm == null ? cfg.CoverageAt(col)
                    : !farm.InOutline(col) ? 0f
                    : kind == RowKind.Lane ? Mathf.Clamp01(farm.coverage + farm.pathRowExtraCoverage)
                    : farm.coverage;

                int hash = DecorHash(ix, iz);
                if (!npcPedestal && !gamePedestal && Hash01(hash) > coverage) continue;

                int lift = farm != null && farm.terrace > 0
                    ? Mathf.FloorToInt(Hash01(hash ^ unchecked((int)0x9e3779b9)) * (farm.terrace + 1))
                    : 0;
                lift = Mathf.Max(lift, cfg.HeightAt(col));
                if (npcPedestal)  lift = Mathf.Max(lift, 1);
                if (gamePedestal) lift = Mathf.Max(lift, cfg.minigamePedestalLift);

                tops.Add(new Vector3Int(col.x, cfg.origin.y + lift, col.y));
            }
        }
        finally { _building = prev; }
        return tops;
    }

    // The wood, anchored the way AnchorGrove does it at runtime, but against the
    // map asset's blocks and the other plots' previews (`ground`) rather than
    // built ground. The anchor rewrites the grove's origin and size. They are
    // derived, not authored, and are put back afterwards so a preview never
    // changes the scene.
    public List<Vector3Int> EditorGroveTops(HarmonyGroveConfig g, HashSet<Vector2Int> ground, List<MapDecorConfig> plots)
    {
        var tops = new List<Vector3Int>();
        if (g == null || decor == null) return tops;

        var keepOrigin = g.origin;
        var keepSize   = g.size;
        int keepRot    = g.rotationSteps;
        try
        {
            var f  = decor;
            var fe = f.Extent;
            var fo = new Vector2Int(f.origin.x, f.origin.z);

            g.HasFarm     = f.enabled;
            g.FarmCentre  = new Vector2(fo.x + (fe.x - 1) * 0.5f, fo.y + (fe.y - 1) * 0.5f);
            g.FarmHalf    = new Vector2(fe.x * 0.5f, fe.y * 0.5f);
            g.FarmOutline = f.InOutline;

            var blocked = new HashSet<Vector2Int>(ground);
            foreach (var p in plots)
            {
                if (p == null || p == g || !p.enabled) continue;
                var e = p.Extent;
                for (int x = 1; x < e.x - 1; x++)
                    for (int z = 1; z < e.y - 1; z++)
                    {
                        var col = new Vector2Int(p.origin.x + x, p.origin.z + z);
                        if (p.CoverageAt(col) > 0f) blocked.Add(col);
                    }
            }
            g.Blocked = blocked;

            Vector2 rest = Vector2.zero;
            int n = 0;
            foreach (var c in ground)
            {
                bool inFarm = c.x >= fo.x && c.x < fo.x + fe.x && c.y >= fo.y && c.y < fo.y + fe.y;
                if (inFarm) continue;
                rest += new Vector2(c.x, c.y);
                n++;
            }
            Vector2 outward = n > 0 ? g.FarmCentre - rest / n : Vector2.up;
            g.Outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : Vector2.up;

            int pad = Mathf.CeilToInt(g.depth * (1f + g.irregularity)) + 2;
            g.rotationSteps = 0;
            g.origin = new Vector3Int(fo.x - pad, f.origin.y, fo.y - pad);
            g.size   = new Vector2Int(fe.x + pad * 2, fe.y + pad * 2);
            g.Invalidate();

            tops = EditorPlotTops(g);
        }
        finally
        {
            g.origin        = keepOrigin;
            g.size          = keepSize;
            g.rotationSteps = keepRot;
            g.Invalidate();
        }
        return tops;
    }
}
#endif
