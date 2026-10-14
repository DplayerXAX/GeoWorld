using System;
using System.Collections.Generic;
using UnityEngine;

// How much of the enemies' route the turrets can actually hit.
//
// Walks every route the board forms, node by node, and counts the damaging turrets
// that could hit an enemy standing there (TurretController.Covers: range AND line of
// sight for the shooting types, the beam's own line for the laser). The answer is
// what the player is really optimising when they lay a route and place turrets, and
// until now it was something to be felt out by running the wave.
//
// Shown two ways: the live route line is coloured by it while building (red = nothing
// can hit an enemy here, amber = one turret, green = two or more), and the wave panel
// reads the summary. Recomputed whenever the board changes (GameFlowManager.EvaluateGrid).
public static class RouteCoverage
{
    public struct Result
    {
        public bool  valid;        // there is at least one route
        public int   nodes;        // route nodes, over all routes
        public int   covered;      // of those, how many some turret can hit
        public int   turrets;      // damaging turrets standing and supported
        public float avgDepth;     // mean turrets covering a node
        public int   longestGap;   // longest run of uncovered nodes, in one route
        public int   gapRoute;     // which route it is in
        public int   gapEnd;       // index of the last node of that run
        public int   gapRouteLength;

        public float Coverage => nodes > 0 ? (float)covered / nodes : 0f;
    }

    public static Result Last;
    public static int    Version;
    public static event Action Changed;

    // Per route, per node: how many turrets cover it. Null when there are no turrets
    // to cover anything at all — the lines then keep their plain look, instead of the
    // whole route glowing red before the player has had a chance to place one.
    public static List<int[]> Compute(List<List<FaceNode>> routes)
    {
        var res = new Result();
        List<int[]> counts = null;

        var grid = GridSystem.instance;
        var turrets = new List<TurretController>();
        foreach (var t in UnityEngine.Object.FindObjectsByType<TurretController>(FindObjectsSortMode.None))
            if (t != null && t.isActiveAndEnabled && t.Supported && t.mode != TurretController.Mode.Debuff)
                turrets.Add(t);
        res.turrets = turrets.Count;

        if (grid != null && routes != null && routes.Count > 0)
        {
            res.valid = true;
            counts = new List<int[]>(routes.Count);
            float cs = grid.cellSize;
            long depthSum = 0;

            for (int r = 0; r < routes.Count; r++)
            {
                var route = routes[r];
                var c = new int[route != null ? route.Count : 0];
                int run = 0;
                for (int i = 0; i < c.Length; i++)
                {
                    // Where an enemy's body is: just off the face it walks on.
                    Vector3 p = grid.GridToWorld(route[i].cell) + route[i].normal * (cs * 0.65f);
                    int n = 0;
                    for (int t = 0; t < turrets.Count; t++)
                        if (turrets[t].Covers(p)) n++;
                    c[i] = n;

                    res.nodes++;
                    depthSum += n;
                    if (n > 0) { res.covered++; run = 0; }
                    else
                    {
                        run++;
                        if (run > res.longestGap)
                        {
                            res.longestGap = run; res.gapRoute = r; res.gapEnd = i; res.gapRouteLength = c.Length;
                        }
                    }
                }
                counts.Add(c);
            }
            res.avgDepth = res.nodes > 0 ? (float)depthSum / res.nodes : 0f;
            if (turrets.Count == 0) counts = null;
        }

        Last = res;
        Version++;
        Changed?.Invoke();
        return counts;
    }

    // The three looks of a route node: nothing can hit it, one turret can, several can.
    public static int Class(int turretsCovering) => turretsCovering <= 0 ? 0 : (turretsCovering == 1 ? 1 : 2);

    public static Color ColorOf(int cls) => cls switch
    {
        0 => new Color(1.00f, 0.34f, 0.28f, 0.95f),
        1 => new Color(1.00f, 0.82f, 0.30f, 0.95f),
        _ => new Color(0.38f, 1.00f, 0.55f, 0.95f),
    };
}
