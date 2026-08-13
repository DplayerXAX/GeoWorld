using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 3-D Tetris played with the game's own flat block shapes. Traditional rules in a
// W×D×H well: a piece falls on a timer, you slide and rotate it, and a LAYER
// clears when its whole W×D floor is filled — the 3-D counterpart of a full row.
//
// Runs as an overlay inside whatever scene launched it rather than as its own
// scene: it builds its camera, well and UI at runtime (the same convention the
// rest of this project's UI uses), so there's no scene to author or add to Build
// Settings, and leaving restores the map exactly as it was — pawn position,
// dialogue state and all — with no save round-trip.
public class BlockTetris3D : MonoBehaviour
{
    public static bool Active { get; private set; }

    // Flat shapes only, matching BlockData.ShapeCells — the "single-face" blocks.
    // Kept as a local table rather than read off BlockData assets so the minigame
    // has no asset dependencies to wire.
    static readonly Vector3Int[][] Shapes =
    {
        new[] { V(0,0,0) },                                     // Single
        new[] { V(0,0,0), V(1,0,0) },                           // I2
        new[] { V(0,0,0), V(1,0,0), V(2,0,0) },                 // I3
        new[] { V(0,0,0), V(1,0,0), V(2,0,0), V(3,0,0) },       // I4
        new[] { V(0,0,0), V(1,0,0), V(1,0,1) },                 // L3
        new[] { V(0,0,0), V(1,0,0), V(2,0,0), V(2,0,1) },       // L4
        new[] { V(0,0,0), V(1,0,0), V(2,0,0), V(1,0,1) },       // T4
        new[] { V(0,0,0), V(1,0,0), V(1,0,1), V(2,0,1) },       // S4
        new[] { V(0,0,0), V(1,0,0), V(0,0,1), V(1,0,1) },       // O2x2
    };

    static Vector3Int V(int x, int y, int z) => new(x, y, z);

    static readonly Color[] Palette =
    {
        new(0.886f, 0.141f, 0.106f),   // signal red
        new(0.910f, 0.698f, 0.227f),   // gold
        new(0.169f, 0.424f, 0.690f),   // blue
        new(0.298f, 0.686f, 0.314f),   // green
        new(0.72f,  0.36f,  0.80f),    // violet
        new(0.20f,  0.72f,  0.72f),    // teal
    };

    const int W = 4, D = 4, H = 12;
    const float Cell = 1f;

    // ── Launch / teardown ────────────────────────────────────────────────────

    public static void Launch(GameObject cubePrefab, string scoreId = null)
    {
        if (Active) return;
        var go = new GameObject("BlockTetris3D");
        var g = go.AddComponent<BlockTetris3D>();
        g._cubePrefab = cubePrefab;
        g._scoreId    = scoreId;
        g.Begin();
    }

    GameObject _cubePrefab;
    string     _scoreId;   // ProfileData.minigameScores key; null = don't record

    // Canvases that were showing when we launched, hidden for the duration. The
    // host scene's UI is ScreenSpaceOverlay, so it ignores our camera's depth and
    // would otherwise draw straight over the minigame.
    readonly List<Canvas> _suppressed = new();

    Camera        _cam;
    Transform     _root, _deck;
    Canvas        _canvas;
    TMP_Text      _scoreText, _overLeft, _overRight;

    readonly bool[,,]      _filled = new bool[W, H, D];
    readonly Transform[,,] _cubes  = new Transform[W, H, D];

    Vector3Int[] _piece;
    Vector3Int   _piecePos;
    Color        _pieceColor;
    readonly List<Transform> _pieceCubes = new();
    readonly List<Transform> _ghostCubes = new();

    float _fallTimer, _fallInterval = 0.8f;
    int   _score, _layers, _level = 1, _piecesPlaced;
    bool  _gameOver, _newRecord;
    float _camYaw = 35f, _camPitch = 18f;   // flatter default — more of the sky in frame, less looking down at the well

    void Begin()
    {
        Active = true;
        BuildCamera();
        BuildWellFrame();
        BuildUI();
        SuppressHostUI();
        SpawnPiece();
        PauseHostMusic();
        PlayMusic();
    }

    void Quit()
    {
        Active = false;
        foreach (var c in _suppressed) if (c != null) c.enabled = true;
        _suppressed.Clear();
        if (_skyboxSwapped) { RenderSettings.skybox = _hostSkybox; _skyboxSwapped = false; }
        RenderSettings.fog             = _hostFog;
        RenderSettings.fogColor        = _hostFogColor;
        RenderSettings.fogStartDistance = _hostFogStart;
        RenderSettings.fogEndDistance   = _hostFogEnd;
        StopMusic();
        ResumeHostMusic();
        if (_cam != null) Destroy(_cam.gameObject);
        Destroy(gameObject);
    }

    uint _musicPlayingId;

    // Posted from a MinigameAudio Resources asset rather than through
    // AudioManager — this overlay launches from LevelSelect, which carries no
    // AudioManager, so AudioManager.Instance would be null there and this would
    // silently never play.
    void PlayMusic()
    {
        var cfg = MinigameAudio.Get();
        if (cfg == null || cfg.stackWellMusic == null || !cfg.stackWellMusic.IsValid())
        {
            Debug.LogWarning("[BlockTetris3D] stackWellMusic not assigned on MinigameAudio.asset — nothing to play.");
            return;
        }
        _musicPlayingId = cfg.stackWellMusic.Post(gameObject);
    }

    void StopMusic()
    {
        if (_musicPlayingId == 0) return;
        var cfg = MinigameAudio.Get();
        int fadeMs = cfg != null ? cfg.stackWellMusicFadeOutMs : 500;
        AkUnitySoundEngine.StopPlayingID(_musicPlayingId, fadeMs, AkCurveInterpolation.AkCurveInterpolation_Linear);
        _musicPlayingId = 0;
    }

    // Every BGM-carrying event id we successfully paused, so Quit() resumes
    // exactly what Begin() paused — never more, never less.
    readonly List<uint> _pausedMusicIds = new();

    // Pauses whatever music was already playing rather than stopping it, so
    // resuming picks the track back up mid-phrase instead of restarting it.
    // Targeted BY EVENT (with AK_INVALID_GAME_OBJECT, i.e. "wherever it's
    // playing") rather than by ducking a bus RTPC: this overlay can be reached
    // from more than one scene (LevelSelect's ambient timeLoop today, possibly
    // gameplay's AudioManager BGM/BGM_fight from a future entry point), and a
    // bus-wide duck would have no way to tell "the host's music" apart from
    // "Stack Well's own music" if they ever end up on the same bus.
    void PauseHostMusic()
    {
        _pausedMusicIds.Clear();
        TryPause(AudioManager.Instance != null ? AudioManager.Instance.BGM : null);
        TryPause(AudioManager.Instance != null ? AudioManager.Instance.BGM_fight : null);
        // ActiveLoop, not timeLoop — the map swaps its ambient track once the
        // tutorial is cleared, and pausing the wrong event pauses nothing.
        TryPause(LevelMapController.Instance != null ? LevelMapController.Instance.ActiveLoop : null);
    }

    void TryPause(AK.Wwise.Event evt)
    {
        if (evt == null || !evt.IsValid()) return;
        AkUnitySoundEngine.ExecuteActionOnEvent(evt.Id, AkActionOnEventType.AkActionOnEventType_Pause,
            AkUnitySoundEngine.AK_INVALID_GAME_OBJECT, 200, AkCurveInterpolation.AkCurveInterpolation_Linear);
        _pausedMusicIds.Add(evt.Id);
    }

    void ResumeHostMusic()
    {
        foreach (var id in _pausedMusicIds)
            AkUnitySoundEngine.ExecuteActionOnEvent(id, AkActionOnEventType.AkActionOnEventType_Resume,
                AkUnitySoundEngine.AK_INVALID_GAME_OBJECT, 400, AkCurveInterpolation.AkCurveInterpolation_Linear);
        _pausedMusicIds.Clear();
    }

    void SuppressHostUI()
    {
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (c == null || !c.enabled) continue;
            if (c.transform.IsChildOf(transform)) continue;   // ours
            c.enabled = false;
            _suppressed.Add(c);
        }
    }

    // ── Loop ─────────────────────────────────────────────────────────────────

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) { Quit(); return; }

        HandleCameraDrag();
        if (_gameOver)
        {
            if (Input.GetKeyDown(KeyCode.R)) Restart();
            return;
        }

        HandleInput();

        _fallTimer += Time.unscaledDeltaTime;
        if (_fallTimer >= _fallInterval)
        {
            _fallTimer = 0f;
            if (!TryMove(Vector3Int.down)) Lock();
        }
    }

    void HandleInput()
    {
        // Movement is CAMERA-RELATIVE: the well can be orbited, so a fixed
        // world-axis mapping would have "left" mean different things depending on
        // where you'd dragged the view to.
        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))  TryMove(-CamRight());
        if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) TryMove( CamRight());
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))    TryMove( CamForward());
        if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow))  TryMove(-CamForward());

        // 1/2/3 rotate around X/Y/Z — the same binding block editing uses.
        if (Input.GetKeyDown(KeyCode.Alpha1)) TryRotate(Quaternion.Euler(90f, 0f, 0f));
        if (Input.GetKeyDown(KeyCode.Alpha2)) TryRotate(Quaternion.Euler(0f, 90f, 0f));
        if (Input.GetKeyDown(KeyCode.Alpha3)) TryRotate(Quaternion.Euler(0f, 0f, 90f));

        if (Input.GetKey(KeyCode.E)) _fallTimer += Time.unscaledDeltaTime * 12f;   // soft drop
        if (Input.GetKeyDown(KeyCode.Space))                                        // hard drop
        {
            int fell = 0;
            while (TryMove(Vector3Int.down)) { _score += 1; fell++; }
            _diveCells = fell;
            Lock();
        }
    }

    // Nearest world axis to the camera's own right / forward, snapped to the grid.
    Vector3Int CamRight()   => SnapAxis(_cam.transform.right);
    Vector3Int CamForward() => SnapAxis(_cam.transform.forward);

    static Vector3Int SnapAxis(Vector3 v)
    {
        v.y = 0f;
        return Mathf.Abs(v.x) >= Mathf.Abs(v.z)
            ? new Vector3Int(v.x >= 0f ? 1 : -1, 0, 0)
            : new Vector3Int(0, 0, v.z >= 0f ? 1 : -1);
    }

    void HandleCameraDrag()
    {
        if (!Input.GetMouseButton(1)) return;
        _camYaw   += Input.GetAxis("Mouse X") * 180f * Time.unscaledDeltaTime;
        _camPitch = Mathf.Clamp(_camPitch - Input.GetAxis("Mouse Y") * 120f * Time.unscaledDeltaTime, 8f, 80f);
        PlaceCamera();
    }

    // ── Piece mechanics ──────────────────────────────────────────────────────

    void SpawnPiece()
    {
        var shape = Shapes[Random.Range(0, Shapes.Length)];
        _piece = Normalize(shape);
        _pieceColor = Palette[Random.Range(0, Palette.Length)];

        var ext = Extent(_piece);
        _piecePos = new Vector3Int((W - ext.x) / 2, H - 1 - (ext.y - 1), (D - ext.z) / 2);

        if (!Fits(_piece, _piecePos)) { GameOver(); return; }
        RebuildPieceCubes();
        UpdateGhost();
    }

    bool TryMove(Vector3Int delta)
    {
        if (!Fits(_piece, _piecePos + delta)) return false;
        _piecePos += delta;
        SyncPieceCubes();
        UpdateGhost();
        return true;
    }

    void TryRotate(Quaternion rot)
    {
        var rotated = Normalize(Rotate(_piece, rot));
        // Wall kick: a rotation that clips the wall retries nudged back inside
        // rather than being refused, which is what stops rotation from feeling
        // dead against the edges of a well only 4 cells wide.
        foreach (var kick in Kicks)
        {
            if (!Fits(rotated, _piecePos + kick)) continue;
            _piece    = rotated;
            _piecePos += kick;
            RebuildPieceCubes();
            UpdateGhost();
            return;
        }
    }

    static readonly Vector3Int[] Kicks =
    {
        new(0,0,0), new(1,0,0), new(-1,0,0), new(0,0,1), new(0,0,-1),
        new(2,0,0), new(-2,0,0), new(0,0,2), new(0,0,-2), new(0,1,0),
    };

    static Vector3Int[] Rotate(Vector3Int[] cells, Quaternion rot)
    {
        var r = new Vector3Int[cells.Length];
        for (int i = 0; i < cells.Length; i++) r[i] = Vector3Int.RoundToInt(rot * (Vector3)cells[i]);
        return r;
    }

    // Shifts a shape so its minimum corner sits at (0,0,0) — keeps rotation from
    // drifting the piece across the well.
    static Vector3Int[] Normalize(Vector3Int[] cells)
    {
        var min = cells[0];
        foreach (var c in cells) min = Vector3Int.Min(min, c);
        var r = new Vector3Int[cells.Length];
        for (int i = 0; i < cells.Length; i++) r[i] = cells[i] - min;
        return r;
    }

    static Vector3Int Extent(Vector3Int[] cells)
    {
        var max = cells[0];
        foreach (var c in cells) max = Vector3Int.Max(max, c);
        return max + Vector3Int.one;
    }

    bool Fits(Vector3Int[] cells, Vector3Int at)
    {
        foreach (var c in cells)
        {
            var p = at + c;
            if (p.x < 0 || p.x >= W || p.z < 0 || p.z >= D || p.y < 0) return false;
            if (p.y >= H) continue;   // above the well is legal — pieces enter from there
            if (_filled[p.x, p.y, p.z]) return false;
        }
        return true;
    }

    void Lock()
    {
        foreach (var c in _piece)
        {
            var p = _piecePos + c;
            if (p.y < 0 || p.y >= H) continue;
            _filled[p.x, p.y, p.z] = true;
            var cube = _cubes[p.x, p.y, p.z] = MakeCube(p, _pieceColor);
            if (_diveCells > 0)
                cube.gameObject.AddComponent<PiecePlummet>().Init(cube.localPosition, _diveCells * Cell);
        }
        _diveCells = 0;
        // The piece is part of the well now, not a thing in flight. Everything
        // below this line — layer clears, the garbage raise — has to reason about
        // it as grid content; leaving _piece pointing at it invites exactly the
        // kind of double-counting RaiseGarbage used to do.
        _piece = null;
        ClearPieceCubes();
        ClearGhostCubes();

        int cleared = ClearFullLayers();
        if (cleared > 0)
        {
            Shockwave(_lastClearY, cleared);
            StartleCrows(16 + cleared * 8);

            // Quadratic, like Tetris' line bonus — clearing several at once is the
            // whole reason to stack rather than dump every piece flat.
            _score  += 100 * cleared * cleared * _level;
            _layers += cleared;
            _level   = 1 + _layers / 5;
            _fallInterval = Mathf.Max(0.12f, 0.8f - (_level - 1) * 0.07f);
        }

        // Endless escalation. Speed alone plateaus — it bottoms out at the minimum
        // interval and the run can coast forever — so from level 3 the floor also
        // starts pushing junk up, and pushes it more often the deeper you get.
        _piecesPlaced++;
        if (_level >= 3 && _piecesPlaced >= GarbageInterval)
        {
            _piecesPlaced = 0;
            RaiseGarbage();
            if (_gameOver) return;
        }

        RefreshScore();
        SpawnPiece();
    }

    int GarbageInterval => Mathf.Max(5, 16 - _level * 2);

    // Shoves one partial layer in at the bottom, everything above shifted up. The
    // holes are what make it survivable: a solid layer could never be cleared, so
    // it would just be a countdown.
    void RaiseGarbage()
    {
        // Anything already in the top layer has nowhere to go — that's a loss.
        for (int x = 0; x < W; x++)
            for (int z = 0; z < D; z++)
                if (_filled[x, H - 1, z]) { GameOver(); return; }

        for (int y = H - 1; y > 0; y--)
            for (int x = 0; x < W; x++)
                for (int z = 0; z < D; z++)
                {
                    _filled[x, y, z] = _filled[x, y - 1, z];
                    var t = _cubes[x, y, z] = _cubes[x, y - 1, z];
                    _cubes[x, y - 1, z]  = null;
                    _filled[x, y - 1, z] = false;
                    MoveCube(t, new Vector3Int(x, y, z));   // same stale-home hazard as CollapseLayer
                }

        int holes = Random.Range(1, 4);
        var open  = new HashSet<int>();
        while (open.Count < holes) open.Add(Random.Range(0, W * D));

        // Soil, not grey — what rises into a well dug in a field is the field.
        var junk = new Color(0.34f, 0.26f, 0.18f);
        for (int x = 0; x < W; x++)
            for (int z = 0; z < D; z++)
            {
                if (open.Contains(x * D + z)) continue;
                _filled[x, 0, z] = true;
                _cubes[x, 0, z]  = MakeCube(new Vector3Int(x, 0, z), junk);
            }

        // No "does the piece in play still fit" check here. Garbage only ever rises
        // from Lock(), i.e. after the piece has been written into _filled — so that
        // test was comparing the piece against its own just-locked cells and failing
        // every single time, ending the run on the first raise no matter how empty
        // the well was. Whether the NEXT piece has room is SpawnPiece's job, and it
        // already checks.
    }

    // Height of the LOWEST layer cleared by the last ClearFullLayers call. The
    // shockwave rides this rather than the piece's landing height — the wave should
    // come off the line that vanished, which isn't always where the piece stopped.
    int _lastClearY;

    int ClearFullLayers()
    {
        int cleared = 0;
        _lastClearY = 0;
        for (int y = 0; y < H; y++)
        {
            bool full = true;
            for (int x = 0; x < W && full; x++)
                for (int z = 0; z < D; z++)
                    if (!_filled[x, y, z]) { full = false; break; }
            if (!full) continue;

            if (cleared == 0) _lastClearY = y;
            CollapseLayer(y);
            cleared++;
            y--;   // everything shifted down — re-test this height
        }
        return cleared;
    }

    void CollapseLayer(int y)
    {
        for (int x = 0; x < W; x++)
            for (int z = 0; z < D; z++)
            {
                if (_cubes[x, y, z] != null) Destroy(_cubes[x, y, z].gameObject);
                _cubes[x, y, z]  = null;
                _filled[x, y, z] = false;
            }

        for (int up = y + 1; up < H; up++)
            for (int x = 0; x < W; x++)
                for (int z = 0; z < D; z++)
                {
                    _filled[x, up - 1, z] = _filled[x, up, z];
                    var t = _cubes[x, up, z];
                    _cubes[x, up - 1, z] = t;
                    _cubes[x, up, z]     = null;
                    _filled[x, up, z]    = false;
                    MoveCube(t, new Vector3Int(x, up - 1, z));
                }
    }

    // The ONLY way a settled cube may be repositioned.
    //
    // A cube that just hard-dropped still carries a PiecePlummet, which drives its
    // localPosition toward a home captured when it locked. Writing localPosition
    // directly here worked for one frame and was then stomped back to that stale
    // home — so a two-layer piece whose bottom layer cleared left its top layer
    // hanging exactly one cell up, permanently.
    void MoveCube(Transform t, Vector3Int cell)
    {
        if (t == null) return;
        var pos = CellPos(cell);
        t.localPosition = pos;
        var plummet = t.GetComponent<PiecePlummet>();
        if (plummet != null) plummet.Rebase(pos);
    }

    // Both halves of the settlement banner. Null on either side hides it, so the
    // reset path doesn't need its own teardown.
    void SetOverColumns(string left, string right)
    {
        if (_overLeft != null)
        {
            _overLeft.gameObject.SetActive(left != null);
            if (left != null) _overLeft.text = left;
        }
        if (_overRight != null)
        {
            _overRight.gameObject.SetActive(right != null);
            if (right != null) _overRight.text = right;
        }
    }

    void GameOver()
    {
        _gameOver = true;
        ClearPieceCubes();
        ClearGhostCubes();

        _newRecord = SaveSystem.Profile.RecordMinigameScore(_scoreId, _score);
        if (_newRecord) SaveSystem.Save();
        int best = SaveSystem.Profile.GetMinigameBest(_scoreId);

        string line = _newRecord ? "NEW RECORD" : $"best {best}";
        SetOverColumns(
            left:  $"WELL\n<size=60%>score {_score}\n<size=80%>R to retry</size></size>",
            right: $"FULL\n<size=60%>{line}\n<size=80%>Esc to leave</size></size>");
    }

    void Restart()
    {
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                for (int z = 0; z < D; z++)
                {
                    if (_cubes[x, y, z] != null) Destroy(_cubes[x, y, z].gameObject);
                    _cubes[x, y, z]  = null;
                    _filled[x, y, z] = false;
                }
        _score = 0; _layers = 0; _level = 1; _piecesPlaced = 0;
        _fallInterval = 0.8f; _gameOver = false; _newRecord = false;
        SetOverColumns(null, null);   // null = hide both
        RefreshScore();
        SpawnPiece();
    }

    // ── Visuals ──────────────────────────────────────────────────────────────

    Vector3 CellPos(Vector3Int c) =>
        new((c.x - (W - 1) * 0.5f) * Cell, (c.y + 0.5f) * Cell, (c.z - (D - 1) * 0.5f) * Cell);

    Transform MakeCube(Vector3Int cell, Color color, bool ghost = false)
    {
        GameObject go = _cubePrefab != null ? Instantiate(_cubePrefab) : GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.transform.SetParent(_deck, false);   // rides the deck, so DeckLift can't separate blocks from their floor
        go.transform.localPosition = CellPos(cell);
        go.transform.localScale    = Vector3.one * Cell;
        foreach (var col in go.GetComponentsInChildren<Collider>()) col.enabled = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            // The block prefab's own material is opaque, so alpha on the MPB alone
            // does nothing — the ghost needs a genuinely transparent material.
            if (ghost)
            {
                r.sharedMaterial    = GhostMaterial();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            MpbColor.Set(r, color);
        }
        return go.transform;
    }

    // Same recipe as the gameplay/overworld placement hints (TutorialDirector's
    // suggestion box, LevelMapController.RewardSuggestMaterial), so a landing
    // preview reads as the same language of "this is where it goes".
    static Material _ghostMat;

    static Material GhostMaterial()
    {
        if (_ghostMat != null) return _ghostMat;
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        _ghostMat = new Material(sh) { name = "TetrisGhost" };
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

    void RebuildPieceCubes()
    {
        ClearPieceCubes();
        foreach (var c in _piece) _pieceCubes.Add(MakeCube(_piecePos + c, _pieceColor));
    }

    void SyncPieceCubes()
    {
        for (int i = 0; i < _pieceCubes.Count && i < _piece.Length; i++)
            if (_pieceCubes[i] != null) _pieceCubes[i].localPosition = CellPos(_piecePos + _piece[i]);
    }

    void ClearPieceCubes()
    {
        foreach (var t in _pieceCubes) if (t != null) Destroy(t.gameObject);
        _pieceCubes.Clear();
    }

    // Landing preview. In a 3-D well you genuinely cannot tell which column a
    // piece is over from a single viewpoint, so this isn't a convenience — it's
    // what makes the game playable at all.
    void UpdateGhost()
    {
        ClearGhostCubes();
        var at = _piecePos;
        while (Fits(_piece, at + Vector3Int.down)) at += Vector3Int.down;
        if (at == _piecePos) return;

        // Full size, translucent — a shrunken ghost reads as "a smaller block goes
        // here" rather than as this piece's own footprint.
        var faded = new Color(_pieceColor.r, _pieceColor.g, _pieceColor.b, 0.35f);
        foreach (var c in _piece) _ghostCubes.Add(MakeCube(at + c, faded, ghost: true));
    }

    void ClearGhostCubes()
    {
        foreach (var t in _ghostCubes) if (t != null) Destroy(t.gameObject);
        _ghostCubes.Clear();
    }

    // How far below the play deck the field lies. Large enough that fog fully
    // swallows the pillar before it reaches bottom — the platform has to look
    // unsupported-past-a-point, not like it's standing on a visible floor far away.
    const float FieldDrop = 240f;

    // Depth of the plinth under the playfield. Grows downward from y = 0 — raise it
    // to make the well look like it's perched on a taller pedestal.
    const float BaseHeight = 0.5f;

    // Lifts the whole play deck (plinth, posts and every block) inside the well
    // root. The CAMERA does not follow it — its focus stays anchored to the root —
    // so this is exactly "move the board up in the viewport", and it exposes more
    // of the pillar underneath at the same time.
    const float DeckLift = 2.8f;

    void BuildWellFrame()
    {
        _root = new GameObject("Well").transform;
        _root.SetParent(transform, false);
        // Far from the map so the host scene's geometry can't poke into frame.
        _root.position = new Vector3(0f, 5000f, 0f);

        // Everything that makes up the playfield hangs off this, so one offset
        // moves plinth, posts and blocks together and they can never drift apart.
        // The pillar and the far field stay on _root, which is what makes the lift
        // read as the board rising away from the ground rather than the whole
        // world sliding.
        _deck = new GameObject("Deck").transform;
        _deck.SetParent(_root, false);
        _deck.localPosition = new Vector3(0f, DeckLift, 0f);

        // The plinth the well stands on. Its TOP FACE is pinned at y = 0 because
        // that's where cell row 0 rests (CellPos puts row 0's centre at +0.5), so a
        // taller plinth has to grow DOWNWARD — centre at -BaseHeight/2, never
        // +BaseHeight/2, or the slab rises into the playfield and swallows the
        // bottom rows of blocks.
        var floor = MakeBox("Floor", _deck);
        floor.transform.localPosition = new Vector3(0f, -BaseHeight * 0.5f, 0f);
        floor.transform.localScale    = new Vector3(W * Cell, BaseHeight, D * Cell);
        MpbColor.Set(floor.GetComponent<Renderer>(), GeoPalette.Ink);

        // Corner posts the full height of the well — the only cue for how much
        // room is left above the stack.
        for (int i = 0; i < 4; i++)
        {
            float sx = (i & 1) == 0 ? -1f : 1f;
            float sz = (i & 2) == 0 ? -1f : 1f;
            var post = MakeBox($"Post{i}", _deck);
            post.transform.localPosition = new Vector3(sx * W * Cell * 0.5f, H * Cell * 0.5f, sz * D * Cell * 0.5f);
            post.transform.localScale    = new Vector3(0.09f, H * Cell, 0.09f);
            // Fence timber, like the farm's own posts — and mid-toned, so it reads
            // against the dark upper sky AND the lit wheat the lower half crosses.
            MpbColor.Set(post.GetComponent<Renderer>(), new Color(0.55f, 0.40f, 0.26f));
        }

        BuildFloatingSupport();
    }

    // Sells "high in the air" with actual geometry, which a skybox can't: a
    // tapering pillar dropping from the well's underside, and a patch of field far
    // below it — both real meshes, so they get genuine perspective/fog falloff as
    // the camera orbits, unlike the skybox's infinite, parallax-free backdrop.
    void BuildFloatingSupport()
    {
        // Hangs from the plinth's UNDERSIDE down to the field. Starting it at y = 0
        // instead put its top face exactly coplanar with the plinth's top face, so
        // the pillar's cross-section z-fought with the playfield the blocks land on.
        // Measured in ROOT space, so it stretches to meet the deck wherever
        // DeckLift puts it — the pillar grows as the board rises.
        float pillarTop = DeckLift - BaseHeight;
        float pillarLen = FieldDrop + pillarTop;
        var pillar = MakeBox("Pillar", _root);
        pillar.transform.localPosition = new Vector3(0f, pillarTop - pillarLen * 0.5f, 0f);
        // Slender rather than tapered — a real taper would need its own mesh, and
        // fog hides the lower two-thirds anyway.
        pillar.transform.localScale = new Vector3(Cell * 0.55f, pillarLen, Cell * 0.55f);
        MpbColor.Set(pillar.GetComponent<Renderer>(), new Color(0.55f, 0.40f, 0.26f));

        var field = MakeBox("FieldFarBelow", _root);
        field.transform.localPosition = new Vector3(0f, -FieldDrop - 1f, 0f);
        field.transform.localScale    = new Vector3(FieldDrop * 1.6f, 2f, FieldDrop * 1.6f);
        MpbColor.Set(field.GetComponent<Renderer>(), new Color(0.62f, 0.58f, 0.24f));   // decor.cropColor
    }

    // Every non-block piece of scenery goes through here, because the material a
    // primitive ships with can't be trusted in a player.
    //
    // GameObject.CreatePrimitive assigns Unity's built-in Default-Material, which
    // belongs to the BUILT-IN pipeline. It resolves fine in the editor, but a URP
    // build has no reason to include it, so the deck, posts and pillar came back
    // untextured while the blocks stayed correct — the blocks come from a real
    // prefab, whose material IS a shipped asset. Borrowing that same material is
    // what makes these survive the build, and it keeps MpbColor tinting working
    // since it's the exact shader the per-cube colouring already targets.
    GameObject MakeBox(string name, Transform parent)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        Destroy(go.GetComponent<Collider>());

        var mat = DeckMaterial();
        if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    Material _deckMat;

    Material DeckMaterial()
    {
        if (_deckMat != null) return _deckMat;

        if (_cubePrefab != null)
        {
            var r = _cubePrefab.GetComponentInChildren<Renderer>();
            if (r != null) _deckMat = r.sharedMaterial;
        }

        // Only reached on Launch's no-prefab fallback path. URP/Lit is in any URP
        // build by construction, unlike the primitive default.
        if (_deckMat == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh != null) _deckMat = new Material(sh) { name = "TetrisDeck" };
            else Debug.LogWarning("[StackWell] No cube prefab and no URP/Lit — scenery will use the primitive default.");
        }

        return _deckMat;
    }

    void BuildCamera()
    {
        var camGo = new GameObject("TetrisCamera");
        _cam = camGo.AddComponent<Camera>();
        _cam.depth       = 100f;   // over the host scene's camera
        _cam.fieldOfView = 45f;

        // RenderSettings.skybox is scene-global, so the host's own sky is stashed
        // and put back on exit — a minigame must not leave the map repainted.
        //
        // Loaded from the Resources keepalive asset, NOT built via `new
        // Material(shader)` — that would construct a fresh material carrying only
        // the shader's Properties-block DEFAULTS, silently ignoring any tuning
        // done on the actual .mat asset in the Project window (which is exactly
        // the bug this replaced: edits to the material had nowhere to go).
        var mat = Resources.Load<Material>("GeoWorldShaderKeepalive/MinigameSkybox_keep");
        if (mat != null)
        {
            _hostSkybox    = RenderSettings.skybox;
            _skyboxSwapped = true;
            RenderSettings.skybox = mat;
            _cam.clearFlags = CameraClearFlags.Skybox;
        }
        else
        {
            _cam.clearFlags      = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.42f, 0.24f, 0.44f);   // dusk, matching the skybox's upper band
        }

        // Distance haze. This is what actually reads as "high up" — it's what
        // swallows the pillar and the field patch before their edges show, the
        // same way real height hides the ground in mist rather than showing it
        // sharp and small. RenderSettings.fog is scene-global too, so it gets the
        // same stash/restore treatment as the skybox.
        _hostFog      = RenderSettings.fog;
        _hostFogColor = RenderSettings.fogColor;
        _hostFogStart = RenderSettings.fogStartDistance;
        _hostFogEnd   = RenderSettings.fogEndDistance;
        // Start pushed well past the well itself (fogStartDistance was H*1.2 ≈ 14.4
        // before — basically at the camera) so the well and a good long stretch of
        // the pillar stay crisp; lengthening the pillar alone did nothing visible
        // because the extra length only landed PAST the old fog end. The gap
        // between start and end is now wide enough to read as a real drop, not an
        // instant haze right at the rail.
        RenderSettings.fog      = true;
        RenderSettings.fogMode  = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.86f, 0.40f, 0.30f);   // skybox's mid-sky band
        RenderSettings.fogStartDistance = H * Cell * 3.2f;
        RenderSettings.fogEndDistance   = FieldDrop * 0.75f;

        PlaceCamera();
    }

    Material _hostSkybox;
    bool     _skyboxSwapped, _hostFog;
    Color    _hostFogColor;
    float    _hostFogStart, _hostFogEnd;

    // Camera ORBITS around `focus` (distance/pitch/yaw feel unaffected) but AIMS
    // ABOVE it by this much, in cells — that pushes the well DOWN in the
    // viewport and opens up headroom for the sky above it (sunset ramp, clouds,
    // sun, windmill). Was negative before (aiming below focus), which did the
    // opposite: crammed the sky into a sliver at the top so the shot favoured the
    // drop below the well instead of the scenery above it.
    const float FrameLift = -3.2f;

    void PlaceCamera()
    {
        if (_cam == null) return;
        Vector3 focus = new(0f, 5000f + H * Cell * 0.42f, 0f);
        var rot = Quaternion.Euler(_camPitch, _camYaw, 0f);
        _cam.transform.position = focus + rot * new Vector3(0f, 0f, -H * Cell * 1.45f);
        _cam.transform.LookAt(focus - Vector3.up * (FrameLift * Cell));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Juice
    // ═════════════════════════════════════════════════════════════════════════

    // ── Hard-drop plummet ────────────────────────────────────────────────────
    // The PIECE dives, not the camera. A hard drop is logically instant — the
    // cells are already written into the grid by the time anything renders — so
    // the blocks would otherwise just teleport, and a drop from the top of the
    // well looks exactly like a drop from one cell up.
    //
    // This replays that fall cosmetically: the cubes start where the piece was,
    // accelerate down to where they actually are, stretch along the way and squash
    // on arrival. Purely visual — the board state never waits for it.

    // Cells the piece fell on the current hard drop, read by Lock() when it builds
    // the locked cubes. 0 = it wasn't a hard drop, so no plummet.
    int _diveCells;

    // ── Layer-clear shockwave ────────────────────────────────────────────────
    // A flat ring thrown out from the layer that vanished, at that layer's height.
    // Flat and horizontal on purpose: it has to read as coming off the LINE, and a
    // sphere would just look like an explosion anywhere in the well.

    void Shockwave(int layerY, int layers)
    {
        if (_deck == null) return;

        var go = new GameObject("Shockwave");
        go.transform.SetParent(_deck, false);
        go.transform.localPosition = new Vector3(0f, (layerY + 0.5f) * Cell, 0f);
        go.transform.localRotation = Quaternion.identity;

        go.AddComponent<MeshFilter>().sharedMesh = ShockMesh();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial    = GhostMaterial();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows    = false;

        // Bigger multi-clears throw a wider, longer-lived wave — the payoff has to
        // scale with the play or clearing four reads the same as clearing one.
        go.AddComponent<ShockRing>().Init(mr,
            Mathf.Max(W, D) * Cell * (1.6f + layers * 0.55f),
            0.42f + layers * 0.08f);
    }

    // ── Startled crows ───────────────────────────────────────────────────────
    // Silhouettes that scatter out of the wheat when a layer goes. They exist to
    // make the world react to the play: without them the clear happens inside the
    // well and the enormous sky around it stays completely indifferent.

    // Distance band the flock lives in. Far enough to sit on the horizon rather
    // than beside the well, but inside the fog's useful range: fog runs
    // 38 → 180 units, so at 55-100 they're 12-45% faded into the sky — reading as
    // distant without dissolving into it.
    const float CrowNear = 55f;
    const float CrowFar  = 100f;

    void StartleCrows(int count)
    {
        if (_root == null) return;

        for (int i = 0; i < count; i++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            float r = Random.Range(CrowNear, CrowFar);
            // Well below eye level, so they come UP past the horizon rather than
            // appearing on it — a bird already at cruising height isn't startled.
            var start = new Vector3(Mathf.Cos(a) * r, Random.Range(-18f, -4f), Mathf.Sin(a) * r);

            var go = new GameObject("Crow");
            go.transform.SetParent(_root, false);
            go.transform.localPosition = start;
            // Big in world units purely because of the distance — at 70 units out,
            // the small silhouettes this started with were a couple of pixels.
            go.transform.localScale = Vector3.one * Random.Range(2.6f, 4.6f);

            go.AddComponent<MeshFilter>().sharedMesh = CrowMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial    = GhostMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows    = false;

            // CLIMBING, first and foremost — they've been startled off the ground,
            // and a flock that mostly slides sideways reads as migrating past. The
            // lateral component is only there to fan them out so they don't rise as
            // one rigid sheet.
            var out_  = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var side  = new Vector3(-out_.z, 0f, out_.x) * (Random.value < 0.5f ? -1f : 1f);
            var vel   = Vector3.up * Random.Range(14f, 23f)
                      + side * Random.Range(3f, 8f)
                      + out_ * Random.Range(1f, 5f);
            go.AddComponent<CrowFlight>().Init(mr, vel, Random.Range(0f, Mathf.PI * 2f));
        }
    }

    // ── Juice meshes ─────────────────────────────────────────────────────────

    static Mesh _shockMesh, _crowMesh;

    // Flat annulus in the XZ plane, unit outer radius 0.5, drawn double-sided so
    // it still reads when the camera drops below its plane mid-dive.
    static Mesh ShockMesh()
    {
        if (_shockMesh != null) return _shockMesh;
        var v = new List<Vector3>();
        var t = new List<int>();

        const int seg = 28;
        const float rOut = 0.5f, rIn = 0.36f;
        for (int i = 0; i < seg; i++)
        {
            float a0 = i / (float)seg * Mathf.PI * 2f;
            float a1 = (i + 1) / (float)seg * Mathf.PI * 2f;
            Vector3 o0 = new(Mathf.Cos(a0) * rOut, 0f, Mathf.Sin(a0) * rOut);
            Vector3 o1 = new(Mathf.Cos(a1) * rOut, 0f, Mathf.Sin(a1) * rOut);
            Vector3 i0 = new(Mathf.Cos(a0) * rIn,  0f, Mathf.Sin(a0) * rIn);
            Vector3 i1 = new(Mathf.Cos(a1) * rIn,  0f, Mathf.Sin(a1) * rIn);

            int s = v.Count;
            v.Add(i0); v.Add(i1); v.Add(o1); v.Add(o0);
            t.Add(s); t.Add(s + 1); t.Add(s + 2);
            t.Add(s); t.Add(s + 2); t.Add(s + 3);
            t.Add(s); t.Add(s + 2); t.Add(s + 1);   // back faces
            t.Add(s); t.Add(s + 3); t.Add(s + 2);
        }

        _shockMesh = new Mesh { name = "TetrisShock", hideFlags = HideFlags.DontSave };
        _shockMesh.SetVertices(v);
        _shockMesh.SetTriangles(t, 0);
        _shockMesh.RecalculateBounds();
        return _shockMesh;
    }

    // Crow: two swept wing quads meeting at a small body, lying in the XZ plane so
    // the flap can be a Y-scale on each wing root. A silhouette from any angle,
    // which is all it needs to be against a bright sky.
    static Mesh CrowMesh()
    {
        if (_crowMesh != null) return _crowMesh;
        var v = new List<Vector3>();
        var t = new List<int>();

        void Wing(float s)
        {
            int i = v.Count;
            v.Add(new Vector3(0f, 0f, -0.10f));            // body rear
            v.Add(new Vector3(0f, 0f, 0.22f));             // body front
            v.Add(new Vector3(s * 0.55f, 0.14f, 0.02f));   // wing tip, raised
            v.Add(new Vector3(s * 0.30f, 0.03f, -0.18f));  // trailing edge
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);
            t.Add(i); t.Add(i + 2); t.Add(i + 1);          // back faces
            t.Add(i); t.Add(i + 3); t.Add(i + 2);
        }
        Wing(1f);
        Wing(-1f);

        _crowMesh = new Mesh { name = "TetrisCrow", hideFlags = HideFlags.DontSave };
        _crowMesh.SetVertices(v);
        _crowMesh.SetTriangles(t, 0);
        _crowMesh.RecalculateBounds();
        return _crowMesh;
    }

    // ── Juice behaviours ─────────────────────────────────────────────────────

    // Replays a hard drop on one cube: rush down from where the piece was, stretched
    // along the fall, then squash flat on arrival and spring back.
    class PiecePlummet : MonoBehaviour
    {
        Vector3 _home, _scale;
        float   _height, _t;

        // Longer falls take a little longer, but nowhere near proportionally — a
        // true constant speed makes a full-height drop feel sluggish, and the whole
        // point of a hard drop is that it's decisive. Even the longest fall is under
        // four frames at 60fps; past that it stops reading as a slam.
        float Fall  => Mathf.Clamp(0.026f + _height * 0.005f, 0.028f, 0.075f);
        const float Land = 0.13f;   // squash + recover

        // Called when the board moves this cube out from under a running animation
        // (a layer clearing below it, garbage rising under it). Without this the
        // animation would keep driving it back to where it locked.
        public void Rebase(Vector3 home)
        {
            _home = home;
            if (_t >= Fall) transform.localPosition = _home;   // past the fall, the squash holds it in place
        }

        public void Init(Vector3 home, float height)
        {
            _home   = home;
            _height = Mathf.Max(0f, height);
            _scale  = transform.localScale;
            transform.localPosition = _home + Vector3.up * _height;
            enabled = _height > 0.001f;
            if (!enabled) Destroy(this);
        }

        void Update()
        {
            _t += Time.unscaledDeltaTime;

            if (_t < Fall)
            {
                float k = _t / Fall;
                float e = k * k;                      // accelerating — gravity, not a lerp
                transform.localPosition = _home + Vector3.up * (_height * (1f - e));
                // Stretch along the fall, thinned across it so the cube keeps its
                // volume. This is what turns a fast move into a visible streak.
                float s = 1f + 0.9f * k;
                transform.localScale = new Vector3(_scale.x / Mathf.Sqrt(s), _scale.y * s, _scale.z / Mathf.Sqrt(s));
                return;
            }

            transform.localPosition = _home;

            float lk = Mathf.Clamp01((_t - Fall) / Land);
            if (lk >= 1f)
            {
                transform.localScale = _scale;
                Destroy(this);
                return;
            }

            // Squash hard on contact, then overshoot slightly on the way back —
            // stopping dead at the rest scale reads as the animation being cut off.
            float squash = Mathf.Sin(lk * Mathf.PI) * (1f - lk) * 0.55f;
            transform.localScale = new Vector3(_scale.x * (1f + squash * 0.6f),
                                               _scale.y * (1f - squash),
                                               _scale.z * (1f + squash * 0.6f));
        }
    }

    class ShockRing : MonoBehaviour
    {
        MeshRenderer _mr;
        float _maxRadius, _life, _t;

        public void Init(MeshRenderer mr, float maxRadius, float life)
        {
            _mr = mr; _maxRadius = maxRadius; _life = Mathf.Max(0.05f, life);
        }

        void Update()
        {
            _t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(_t / _life);
            if (k >= 1f) { Destroy(gameObject); return; }

            // Fast out of the gate, easing as it goes — a linear expansion reads
            // as a growing circle, not as something that was thrown.
            float e = 1f - (1f - k) * (1f - k);
            float r = Mathf.Lerp(0.4f, _maxRadius, e);
            transform.localScale = new Vector3(r, 1f, r);

            var c = GeoPalette.Paper;
            MpbColor.Set(_mr, new Color(c.r, c.g, c.b, (1f - k) * 0.75f));
        }
    }

    class CrowFlight : MonoBehaviour
    {
        MeshRenderer _mr;
        Vector3 _vel;
        float   _phase, _t;
        // Long, because at horizon range they need real screen time to cross
        // anything. A 3-second life out there is a flicker at the edge of frame.
        const float Life = 6f;

        public void Init(MeshRenderer mr, Vector3 velocity, float phase)
        {
            _mr = mr; _vel = velocity; _phase = phase;
            MpbColor.Set(mr, new Color(0f, 0f, 0f, 0f));   // fades IN — they enter, they don't pop
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _t += dt;
            if (_t >= Life) { Destroy(gameObject); return; }

            // Gentle uniform drag, no gravity. They should still be climbing when
            // they fade — a ballistic arc would level them off and drop them back
            // into frame, which is the opposite of "startled".
            _vel *= Mathf.Exp(-0.25f * dt);
            transform.localPosition += _vel * dt;

            // Flap by squashing on Y — cheaper than skinning two wings and, at this
            // size, indistinguishable from it.
            float flap = Mathf.Sin(_t * 17f + _phase);
            var s = transform.localScale;
            transform.localScale = new Vector3(s.x, Mathf.Abs(s.x) * (0.55f + flap * 0.45f), s.z);
            if (_vel.sqrMagnitude > 0.001f)
                transform.localRotation = Quaternion.LookRotation(_vel.normalized, Vector3.up);

            // Pure black at full opacity — a silhouette, not a dark shape. Any alpha
            // under 1 lets the bright sky through and greys them out, which is what
            // the old 0.85 was doing.
            float k = Mathf.Clamp01(_t / Life);
            float a = Mathf.Min(Mathf.Clamp01(_t / 0.15f), 1f - Mathf.Clamp01((k - 0.65f) / 0.35f));
            MpbColor.Set(_mr, new Color(0f, 0f, 0f, a));
        }
    }

    void BuildUI()
    {
        var canvasGo = new GameObject("TetrisCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 1000;
        var sc = canvasGo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;

        // Both halves of the screen ended up BRIGHT once the skybox settled — pale
        // sky up top, lit wheat below — so everything here is ink, not paper. The
        // white these started as was left over from the dark-sky draft.
        _scoreText = NewText("Score", 40f, TextAlignmentOptions.TopLeft);
        _scoreText.color = GeoPalette.Ink;
        var srt = _scoreText.rectTransform;
        srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0f, 1f);
        srt.anchoredPosition = new Vector2(48f, -40f);
        srt.sizeDelta = new Vector2(520f, 200f);
        RefreshScore();

        var help = NewText("Help", 24f, TextAlignmentOptions.BottomLeft);
        // Paper, not ink: the bottom of the frame is the shadowed near edge of the
        // field, and the dark grey this used to be disappeared into it entirely.
        help.color = GeoPalette.WithAlpha(GeoPalette.Paper, 0.8f);
        // No "Esc leave" — the Leave button in the opposite corner already says it.
        help.text = "WASD move   ·   1/2/3 rotate   ·   E soft drop\n"
                  + "Space hard drop   ·   Right-drag orbit";
        var hrt = help.rectTransform;
        hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(0f, 0f);
        hrt.anchoredPosition = new Vector2(48f, 36f);
        hrt.sizeDelta = new Vector2(1100f, 120f);

        // Esc still works, but a visible button is the discoverable way out — and
        // the only one on a pad.
        BuildLeaveButton();

        // Game-over text in TWO columns with the middle left empty. Centred, it
        // landed straight on the stack — the one thing the player wants to look at
        // when the run ends is how their tower finished, and the text was covering
        // exactly that. Both columns carry the same tag sequence so their rows line
        // up across the gap.
        _overLeft  = BuildOverColumn("GameOverLeft",  right: false);
        _overRight = BuildOverColumn("GameOverRight", right: true);
    }

    // Half the screen's centre gap, in reference pixels — the clear channel the
    // well sits in.
    const float OverGap = 360f;

    TMP_Text BuildOverColumn(string name, bool right)
    {
        var t = NewText(name, 84f, right ? TextAlignmentOptions.Left : TextAlignmentOptions.Right);
        t.color = GeoPalette.Paper;

        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot     = new Vector2(right ? 0f : 1f, 0.5f);
        rt.anchoredPosition = new Vector2(right ? OverGap : -OverGap, 0f);
        // Wide enough for the longest row ("Esc to leave") at the bigger size, so
        // it can't wrap and break the row alignment across the gap.
        rt.sizeDelta = new Vector2(700f, 360f);

        t.gameObject.SetActive(false);
        return t;
    }

    void BuildLeaveButton()
    {
        var go = new GameObject("Leave", typeof(RectTransform));
        go.transform.SetParent(_canvas.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-40f, 36f);
        rt.sizeDelta = new Vector2(190f, 62f);

        var img = go.AddComponent<Image>();
        img.sprite = UIRoundedRect.Get(12);
        img.type   = Image.Type.Sliced;
        img.color  = GeoPalette.WithAlpha(GeoPalette.Ink, 0.12f);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors; colors.highlightedColor = GeoPalette.Gold; btn.colors = colors;
        btn.onClick.AddListener(Quit);

        var label = NewText("Label", 26f, TextAlignmentOptions.Center);
        label.transform.SetParent(rt, false);
        label.color = GeoPalette.Paper;   // same reason as the help line — dark ground behind it
        label.text = "Leave  (Esc)";
        var lrt = label.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
    }

    void RefreshScore()
    {
        if (_scoreText == null) return;
        int best = SaveSystem.Profile.GetMinigameBest(_scoreId);
        string bestLine = best > 0 ? $"   ·   best {best}" : "";
        _scoreText.text = $"{_score}\n<size=45%>layers {_layers}   ·   depth {_level}{bestLine}</size>";
    }

    // Ink, not white: the skybox is a paper field, so light text would disappear
    // into it.
    TMP_Text NewText(string name, float size, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(_canvas.transform, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = size; t.color = GeoPalette.Ink; t.fontStyle = FontStyles.Bold;
        t.alignment = align; t.raycastTarget = false; t.richText = true;
        return t;
    }
}
