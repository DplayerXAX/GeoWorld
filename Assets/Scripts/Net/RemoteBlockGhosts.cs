using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// Broadcasts the local player's in-progress block, and draws everyone else's as a
// translucent ghost in their own colour.
//
// Sent on a timer rather than every frame: the held block moves continuously, but
// a ghost that updates 15 times a second is indistinguishable from one that updates
// 120 times while costing an eighth of the traffic — and this is the only per-frame
// message in the game, so it's the only one where that matters.
//
// Ghosts are cosmetic and never authoritative. Seeing someone hovering a cell tells
// you they're ABOUT to take it, not that they have — CellClaims decides that, and
// deliberately: a ghost that reserved ground would let a player lock the board just
// by holding a block over it.
[DisallowMultipleComponent]
public class RemoteBlockGhosts : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Spawn()
    {
        // Also on every later load. AfterSceneLoad fires once, on the FIRST scene —
        // which is the title, where PlacementController does not exist — so on its own
        // it bailed out and never ran again, and the ghosts never appeared at all.
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        TrySpawn();
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene s,
                              UnityEngine.SceneManagement.LoadSceneMode m) => TrySpawn();

    static void TrySpawn()
    {
        if (PlacementController.Instance == null) return;   // gameplay scene only
        if (FindFirstObjectByType<RemoteBlockGhosts>() != null) return;
        new GameObject("RemoteBlockGhosts").AddComponent<RemoteBlockGhosts>();
    }

    const float SendInterval = 1f / 15f;
    const float GhostAlpha   = 0.42f;
    // Dropped after this long without an update, so a player who alt-tabs or drops
    // doesn't leave a ghost parked on the board forever.
    const float StaleAfter   = 0.6f;

    float _sendTimer;

    class Ghost
    {
        public GameObject go;
        public string     blockId;
        public Vector3Int cell;
        public Quaternion rot;
        public float      lastSeen;
        public TMP_Text   tag;      // whose block this is
    }

    // The tag lives on a world-space canvas parented to the ghost, so it follows the
    // block without a per-frame screen-space projection — and it is billboarded in
    // LateUpdate rather than parented upright, because an unrotated label on a
    // rotated block reads as part of the block instead of a label on it.
    const float TagLift = 1.4f;

    readonly Dictionary<int, Ghost> _ghosts = new();

    void Update()
    {
        BroadcastLocal();
        SyncRemoteGhosts();
    }

    void BroadcastLocal()
    {
        var net = NgoNetwork.Instance;
        if (net == null) return;

        _sendTimer -= Time.unscaledDeltaTime;
        if (_sendTimer > 0f) return;
        _sendTimer = SendInterval;

        var pc = PlacementController.Instance;
        bool holding = pc != null && pc.mode == PlacementMode.Edit && pc.currentBlock != null;

        net.BroadcastPreview(new NgoNetwork.PreviewState
        {
            playerId   = MultiplayerSession.LocalId,
            active     = holding,
            cell       = holding ? pc.HeldGridPos    : default,
            rotation   = holding ? pc.CurrentRotation : Quaternion.identity,
            blockId    = holding ? BlockCatalog.IdOf(pc.currentBlock) : "",
        });
    }

    void SyncRemoteGhosts()
    {
        var net = NgoNetwork.Instance;
        if (net == null) { ClearAll(); return; }

        foreach (var kv in net.RemotePreviews)
        {
            var st = kv.Value;
            if (!st.active) continue;

            string id = st.blockId.ToString();
            var data = BlockCatalog.Resolve(id);
            if (data == null) continue;   // not catalogued — nothing we can draw

            if (!_ghosts.TryGetValue(st.playerId, out var g))
                _ghosts[st.playerId] = g = new Ghost();

            // Rebuilt only when the SHAPE or orientation changed; a ghost that just
            // moved is repositioned, not respawned, or every frame of someone else's
            // dragging would churn a few GameObjects.
            if (g.go == null || g.blockId != id)
            {
                if (g.go != null) Destroy(g.go);
                g.go      = BuildGhost(data, MultiplayerSession.ColorOf(st.playerId));
                g.blockId = id;
                if (g.go != null)
                {
                    int top = 0;
                    foreach (var c in data.cells) if (c.y > top) top = c.y;
                    g.tag = BuildTag(g.go, GridSystem.instance.cellSize, top);
                }
            }
            if (g.go != null) g.go.transform.rotation = st.rotation;
            g.rot = st.rotation;

            if (g.go != null && GridSystem.instance != null)
                g.go.transform.position = GridSystem.instance.GridToWorld(st.cell);

            g.cell     = st.cell;
            g.lastSeen = Time.unscaledTime;

            if (g.tag != null)
            {
                var p = MultiplayerSession.Get(st.playerId);
                g.tag.text  = p != null ? p.displayName : $"Player {st.playerId + 1}";
                g.tag.color = MultiplayerSession.ColorOf(st.playerId);
            }
        }

        // Reap ghosts nobody is refreshing.
        var stale = new List<int>();
        foreach (var kv in _ghosts)
            if (Time.unscaledTime - kv.Value.lastSeen > StaleAfter) stale.Add(kv.Key);
        foreach (var id in stale)
        {
            if (_ghosts[id].go != null) Destroy(_ghosts[id].go);
            _ghosts.Remove(id);
        }
    }

    // Cells are laid out UNROTATED and the root is turned instead, so a rotation
    // change is a transform write rather than a rebuild.
    GameObject BuildGhost(BlockData data, Color playerColor)
    {
        var grid = GridSystem.instance;
        if (grid == null || data.cells == null) return null;

        var root = new GameObject("RemoteGhost");

        var tint = new Color(playerColor.r, playerColor.g, playerColor.b, GhostAlpha);
        foreach (var c in data.cells)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(root.transform, false);
            cube.transform.localPosition = (Vector3)c * grid.cellSize;
            // Slightly under a cell, same reason the tutorial's suggestion tiles are:
            // at exactly one cell the ghost is coplanar with real blocks and the two
            // surfaces z-fight.
            cube.transform.localScale    = Vector3.one * (grid.cellSize * 0.94f);
            Destroy(cube.GetComponent<Collider>());   // ghosts must never be clickable

            var r = cube.GetComponent<Renderer>();
            r.sharedMaterial    = GhostMaterial();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows    = false;
            MpbColor.Set(r, tint);
        }
        return root;
    }

    // World-space label above the ghost. Built alongside it so a rebuild (shape
    // change) never leaves an orphaned tag behind.
    TMP_Text BuildTag(GameObject root, float cellSize, float topCell)
    {
        var canvasGo = new GameObject("NameTag", typeof(Canvas));
        canvasGo.transform.SetParent(root.transform, false);
        canvasGo.transform.localPosition = new Vector3(0f, (topCell + TagLift) * cellSize, 0f);

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var rt = (RectTransform)canvasGo.transform;
        rt.sizeDelta  = new Vector2(400f, 80f);
        // Canvas units are pixels; scaled down to world units or the label would be
        // the size of a building.
        rt.localScale = Vector3.one * 0.006f;

        var t = new GameObject("Label", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        t.transform.SetParent(canvasGo.transform, false);
        var trt = t.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;

        t.fontSize      = 48f;
        t.alignment     = TextAlignmentOptions.Center;
        t.fontStyle     = FontStyles.Bold;
        t.raycastTarget = false;
        // Drawn over the world rather than clipped by it: a tag half-buried in the
        // terrain it is hovering over says nothing.
        t.isOverlay     = true;
        return t;
    }

    // Billboarded after everything has moved, so the label never trails the block by
    // a frame.
    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null) return;
        foreach (var kv in _ghosts)
        {
            var t = kv.Value.tag;
            if (t == null) continue;
            t.transform.parent.rotation = Quaternion.LookRotation(
                t.transform.parent.position - cam.transform.position, Vector3.up);
        }
    }

    void ClearAll()
    {
        foreach (var kv in _ghosts) if (kv.Value.go != null) Destroy(kv.Value.go);
        _ghosts.Clear();
    }

    void OnDestroy() => ClearAll();

    // Same transparent-Unlit recipe the rest of the project's runtime ghosts use.
    static Material _ghostMat;
    static Material GhostMaterial()
    {
        if (_ghostMat != null) return _ghostMat;
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        _ghostMat = new Material(sh) { name = "RemotePlayerGhost" };
        if (_ghostMat.HasProperty("_Surface"))
        {
            _ghostMat.SetFloat("_Surface", 1f);
            _ghostMat.SetFloat("_ZWrite", 0f);
            _ghostMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _ghostMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _ghostMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _ghostMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        return _ghostMat;
    }
}
