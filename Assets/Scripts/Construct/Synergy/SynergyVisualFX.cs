using System;
using System.Collections.Generic;
using UnityEngine;

// Routes per-piece claim/release events to the correct SynergyVisualizer.
//
// Architecture:
//   • Subscribe to SynergyEvaluator.OnClaimChanged.
//   • Each event = "something about the actives changed". Walk all current
//     actives, build piece→active map, diff against previous state.
//   • For each piece transition:
//       - piece newly claimed     → call visualizer.OnPieceClaimed
//       - piece moved synergies   → release old, claim new
//       - piece released entirely → call visualizer.OnPieceReleased
//   • Track the spawned GameObject per piece so release can clean up.
//
// One MonoBehaviour instance per scene. No inspector wiring needed — uses
// SynergyEvaluator.Instance and GridSystem.instance.
//
// In-world visuals live on the piece's visualObject (parented), so they
// automatically follow the block and die with it. The FX manager still
// gets a chance to run OnPieceReleased before the block is destroyed
// (via OnPieceRemoved → ReEvaluate → OnClaimChanged), but if the block's
// destruction is what triggered release, the spawned GameObject is already
// dead — Destroy on a fake-null target is safe.
public class SynergyVisualFX : MonoBehaviour
{
    public static SynergyVisualFX Instance;

    [Header("Joker tint")]
    [Tooltip("If true, Universal (grey) pieces that get claimed by a synergy are tinted to that synergy's theme color via MaterialPropertyBlock. Original colour is restored on release.")]
    public bool tintJokerPieces = true;

    sealed class PieceDecoration
    {
        public PlacedBlockInstance instance;
        public SynergyVisualizer   visualizer;
        public ActiveSynergy       active;
        public GameObject          spawned;

        // Snapshot of active.claimedPieces.Count at decoration time. If the
        // current count differs (absorb grew the claim, or a piece was
        // removed and the rule still satisfies), the decoration is rebuilt
        // — important for ICellHighlightFilter rules where the highlight
        // region can move/grow without the piece reference changing.
        public int snapshotClaimCount;

        // Original (pre-tint) MPB colors for each renderer of the piece.
        // Only populated for Universal pieces when tintJokerPieces is on.
        public List<(Renderer r, Color original)> jokerRestore;
    }

    // piece.id → live decoration record. Pieces without a visualizer in
    // their active rule are absent here entirely.
    readonly Dictionary<int, PieceDecoration> _decos = new();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnEnable()  => TryHook();
    void Start()     => TryHook();
    void OnDisable()
    {
        if (SynergyEvaluator.Instance != null)
            SynergyEvaluator.Instance.OnClaimChanged -= HandleClaimChanged;
    }

    void TryHook()
    {
        if (SynergyEvaluator.Instance == null) return;
        SynergyEvaluator.Instance.OnClaimChanged -= HandleClaimChanged;
        SynergyEvaluator.Instance.OnClaimChanged += HandleClaimChanged;
    }

    // ── Diff + dispatch ──────────────────────────────────────────────────

    void HandleClaimChanged(SynergyRule changedRule, ActiveSynergy changedActive)
    {
        var evaluator = SynergyEvaluator.Instance;
        if (evaluator == null) return;

        // Build current piece→active map from ALL actives (not just the
        // changed one), so we naturally handle cross-rule re-claims.
        var current = new Dictionary<int, ActiveSynergy>();
        var actives = evaluator.Actives;
        for (int i = 0; i < actives.Count; i++)
        {
            var a = actives[i];
            foreach (var p in a.claimedPieces) current[p.id] = a;
        }

        // 1) Release pieces no longer in any active claim.
        List<int> stale = null;
        foreach (var kv in _decos)
        {
            if (!current.ContainsKey(kv.Key))
            {
                (stale ??= new List<int>()).Add(kv.Key);
            }
        }
        if (stale != null)
        {
            for (int i = 0; i < stale.Count; i++)
            {
                var d = _decos[stale[i]];
                SafeRelease(d);
                _decos.Remove(stale[i]);
            }
        }

        // 2) For pieces in the current map, either spawn new or hand off
        //    between synergies if the owning rule changed.
        var grid = GridSystem.instance;
        foreach (var kv in current)
        {
            int  pieceId  = kv.Key;
            var  active   = kv.Value;
            var  newVis   = active.rule != null ? active.rule.visualizer : null;

            var piece = evaluator.GetPieceById(pieceId);
            if (piece == null || piece.cells.Length == 0) continue;

            bool wantsJokerTint = tintJokerPieces && piece.color == BlockColor.Universal;

            // Skip entirely if neither effect applies to this piece.
            if (newVis == null && !wantsJokerTint) continue;

            if (_decos.TryGetValue(pieceId, out var existing))
            {
                bool ruleSame  = existing.active.rule == active.rule;
                bool claimSame = existing.active == active
                              && existing.snapshotClaimCount == active.claimedPieces.Count;

                // Same rule AND same claim signature → decoration is current.
                // Rule changes (different synergy) OR claim grew/shrank (absorb,
                // ICellHighlightFilter zone may have moved) → rebuild.
                if (ruleSame && claimSame) continue;

                SafeRelease(existing);
                _decos.Remove(pieceId);
            }

            var ins = grid != null ? grid.GetInstanceAt(piece.cells[0]) : null;
            if (ins == null) continue;

            GameObject spawned = null;
            if (newVis != null)
            {
                try { spawned = newVis.OnPieceClaimed(ins, active); }
                catch (Exception e) { Debug.LogError($"[SynergyFX] {newVis.name}.OnPieceClaimed: {e}"); }
            }

            List<(Renderer r, Color original)> jokerRestore = null;
            if (wantsJokerTint && ins.visualObject != null)
            {
                jokerRestore = ApplyJokerTint(ins, active);
            }

            _decos[pieceId] = new PieceDecoration
            {
                instance           = ins,
                visualizer         = newVis,
                active             = active,
                spawned            = spawned,
                jokerRestore       = jokerRestore,
                snapshotClaimCount = active.claimedPieces.Count,
            };
        }
    }

    // Save current MPB colors on every renderer of the piece, then re-tint
    // them all to the active rule's theme color. Returned list is the
    // snapshot we'll replay on release to put the grey back.
    static List<(Renderer r, Color original)> ApplyJokerTint(PlacedBlockInstance ins, ActiveSynergy active)
    {
        var tint = BlockColorPalette.Get(active.rule.color);
        var snap = new List<(Renderer r, Color original)>();

        var rends = ins.visualObject.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < rends.Length; i++)
        {
            var r = rends[i];
            if (r == null) continue;
            var orig = MpbColor.Get(r);
            snap.Add((r, orig));
            MpbColor.Set(r, tint);
        }
        return snap;
    }

    static void RestoreJokerTint(PieceDecoration d)
    {
        if (d?.jokerRestore == null) return;
        for (int i = 0; i < d.jokerRestore.Count; i++)
        {
            var (r, orig) = d.jokerRestore[i];
            if (r != null) MpbColor.Set(r, orig);
        }
    }

    static void SafeRelease(PieceDecoration d)
    {
        if (d == null) return;

        // 1. Visualizer teardown (if any).
        if (d.visualizer != null)
        {
            try { d.visualizer.OnPieceReleased(d.instance, d.active, d.spawned); }
            catch (Exception e) { Debug.LogError($"[SynergyFX] {d.visualizer.name}.OnPieceReleased: {e}"); }
        }

        // 2. Joker tint restore (independent of visualizer).
        RestoreJokerTint(d);
    }
}
