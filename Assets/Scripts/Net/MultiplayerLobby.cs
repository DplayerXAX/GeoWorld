using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The room. Connect here, agree on what to play, then everyone loads the match
// together.
//
// A separate scene rather than an overlay on the title, because connecting is a
// state you can be IN — you can be waiting for a third player for a while, and a
// screen you are parked on should look like a place, not a dialog someone left open.
//
// The whole thing is built in code and auto-spawns on the Lobby scene, like every
// other screen in this project: the scene file stays a camera and a light, and the
// layout is reviewable as a diff instead of as a prefab.
//
// The host owns the room. Clients display what they are told and send exactly one
// thing back — their ready flag. That asymmetry is why there is no "who is right"
// question to answer when two people change something at once.
[DisallowMultipleComponent]
public class MultiplayerLobby : MonoBehaviour
{
    public const string SceneName = "Lobby";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Spawn()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TrySpawn();
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m) => TrySpawn();

    static void TrySpawn()
    {
        if (SceneManager.GetActiveScene().name != SceneName) return;
        if (FindFirstObjectByType<MultiplayerLobby>() != null) return;
        new GameObject("MultiplayerLobby").AddComponent<MultiplayerLobby>();
    }

    enum Face { Choose, Room }

    Face   _face = Face.Choose;
    string _address = "127.0.0.1";
    string _name    = "";
    int    _levelIndex;                       // 0 = endless, then database order
    readonly List<LevelDefinition> _levels = new();

    Canvas       _canvas;
    RectTransform _choosePanel, _roomPanel;
    TMP_Text     _statusText, _levelText, _hostAddrText, _startHint;
    TMP_InputField _addressField, _nameField, _roomNameField;
    bool _nameSent;
    Button       _startBtn, _readyBtn, _levelPrev, _levelNext;
    TMP_Text     _readyLabel, _startLabel;
    readonly TMP_Text[] _slotName  = new TMP_Text[MultiplayerSession.MaxPlayers];
    readonly TMP_Text[] _slotState = new TMP_Text[MultiplayerSession.MaxPlayers];
    readonly Image[]    _slotSwatch = new Image[MultiplayerSession.MaxPlayers];

    void Start()
    {
        _name = $"Player {Random.Range(100, 999)}";
        BuildLevelList();
        BuildUI();

        // Already connected — we came back from a match rather than in from the
        // title. Open straight into the room: asking someone to host or join a
        // session they are currently in the middle of is nonsense, and picking
        // either would tear down the one they have.
        if (NetBootstrap.Online)
        {
            _face = Face.Room;
            var me = MultiplayerSession.Get(MultiplayerSession.LocalId);
            if (me != null) _name = me.displayName;
            _roomNameField.SetTextWithoutNotify(_name);
        }

        MultiplayerSession.RosterChanged += Refresh;
        if (NetBootstrap.Net != null)
        {
            NetBootstrap.Net.LobbyUpdated  += Refresh;
            NetBootstrap.Net.MatchBeginning += BeginMatch;
        }
        Refresh();
    }

    void OnDestroy()
    {
        MultiplayerSession.RosterChanged -= Refresh;
        if (NetBootstrap.Net != null)
        {
            NetBootstrap.Net.LobbyUpdated   -= Refresh;
            NetBootstrap.Net.MatchBeginning -= BeginMatch;
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) Leave();
    }

    void BuildLevelList()
    {
        _levels.Clear();
        _levels.Add(null);                    // index 0 is endless
        var db = LevelRegistry.Db;
        if (db != null && db.levels != null)
            foreach (var l in db.levels) if (l != null) _levels.Add(l);
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    void HostRoom()
    {
        NetBootstrap.Host();
        MultiplayerSession.SetName(MultiplayerSession.LocalId, _name);
        ApplyLevelChoice();
        _face = Face.Room;
        _roomNameField.SetTextWithoutNotify(_name);
        Refresh();
    }

    void JoinRoom()
    {
        NetBootstrap.Join(_address);
        _nameSent = false;              // resent once the host tells us which slot we are
        _face = Face.Room;
        _roomNameField.SetTextWithoutNotify(_name);
        Refresh();
    }

    // Renaming from inside the room. The host applies its own; a client asks and waits
    // for the echo, exactly like ready — so the roster everyone sees always comes from
    // one machine, and there is no moment where your name reads differently to you
    // than to the other three.
    void SubmitName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        _name = value.Trim();

        if (MultiplayerSession.IsHost)
        {
            MultiplayerSession.SetName(MultiplayerSession.LocalId, _name);
            NetBootstrap.Net?.BroadcastLobby();
            Refresh();
        }
        else
        {
            NetBootstrap.Net?.SendNameToHost(_name);
        }
    }

    void Leave()
    {
        if (_face == Face.Room) { NetBootstrap.Shutdown(); _face = Face.Choose; Refresh(); return; }
        LoadingScreen.Go("Title");
    }

    void ToggleReady()
    {
        int me = MultiplayerSession.LocalId;
        bool now = !(MultiplayerSession.Get(me)?.ready ?? false);

        if (MultiplayerSession.IsHost)
        {
            MultiplayerSession.SetReady(me, now);
            NetBootstrap.Net?.BroadcastLobby();
        }
        else
        {
            // Not applied locally: the host's echo is what sets it, so the button can
            // never show ready while the host still has us as not. One source.
            NetBootstrap.Net?.SendReadyToHost(now);
        }
    }

    void StepLevel(int dir)
    {
        if (!MultiplayerSession.IsHost || _levels.Count == 0) return;
        _levelIndex = (_levelIndex + dir + _levels.Count) % _levels.Count;
        ApplyLevelChoice();
    }

    void ApplyLevelChoice()
    {
        var lv = _levels[Mathf.Clamp(_levelIndex, 0, _levels.Count - 1)];

        // Never zero. Zero means "roll one at run start", which each machine would do
        // from its own clock — the level generator is seeded, so an unagreed seed is
        // exactly as broken as no seed at all. Rolled once here, on the host, and
        // carried to everyone in the room message.
        ulong seed = lv != null && lv.runSeed != 0UL ? lv.runSeed : NewSeed();
        RoomConfig.Set(lv != null ? lv.levelId : "", seed);

        // Changing what everyone is about to play un-readies everyone: a player who
        // readied for one level has not agreed to a different one.
        MultiplayerSession.ClearReady();
        NetBootstrap.Net?.BroadcastLobby();
        Refresh();
    }

    static ulong NewSeed()
    {
        // Two 31-bit draws because Random.Range has no 64-bit overload; the exact
        // distribution does not matter, only that it is a definite number everyone
        // gets told about.
        ulong hi = (ulong)Random.Range(1, int.MaxValue);
        ulong lo = (ulong)Random.Range(1, int.MaxValue);
        return (hi << 32) | lo;
    }

    void StartMatch()
    {
        if (!MultiplayerSession.IsHost || !MultiplayerSession.AllReady) return;
        NetBootstrap.Net?.BroadcastBegin(RoomConfig.GameplayScene);
    }

    // Fires on every machine off the host's message, so all four resolve the level
    // from the same id at the same moment rather than from their own UI state.
    void BeginMatch(string scene)
    {
        RoomConfig.PushToRunConfig(LevelRegistry.Db);
        MultiplayerSession.ClearReady();
        LoadingScreen.Go(string.IsNullOrEmpty(scene) ? RoomConfig.GameplayScene : scene);
    }

    // ── Refresh ──────────────────────────────────────────────────────────────

    void Refresh()
    {
        if (_canvas == null) return;

        _choosePanel.gameObject.SetActive(_face == Face.Choose);
        _roomPanel.gameObject.SetActive(_face == Face.Room);
        if (_face != Face.Room) return;

        bool host   = MultiplayerSession.IsHost;
        bool online = NetBootstrap.Online;

        // Waits for the host's first roster message. `online` only means the socket is
        // up — StartClient returns before the connection completes — so sending on
        // that alone would drop the name into a connection that is not there yet. Our
        // slot reading back as connected is the host having actually answered.
        bool seated = MultiplayerSession.Get(MultiplayerSession.LocalId)?.connected ?? false;
        if (online && !host && !_nameSent && seated)
        {
            NetBootstrap.Net?.SendNameToHost(_name);
            _nameSent = true;
        }

        // Not while it is focused: the host's echo arriving mid-word would rewrite
        // what you are still typing.
        if (_roomNameField != null && !_roomNameField.isFocused)
        {
            var me = MultiplayerSession.Get(MultiplayerSession.LocalId);
            if (me != null && me.connected && me.displayName != _roomNameField.text)
                _roomNameField.SetTextWithoutNotify(me.displayName);
        }

        for (int i = 0; i < MultiplayerSession.MaxPlayers; i++)
        {
            var p = MultiplayerSession.Get(i);
            bool on = p != null && p.connected;

            _slotSwatch[i].color = on ? MultiplayerSession.ColorOf(i)
                                      : GeoPalette.WithAlpha(GeoPalette.Paper, 0.10f);
            _slotName[i].text  = on ? p.displayName : "open";
            _slotName[i].color = on ? GeoPalette.Paper : GeoPalette.WithAlpha(GeoPalette.Paper, 0.28f);

            string tag = !on ? "" : p.ready ? "READY" : "waiting";
            if (on && p.isLocal) tag += "  (you)";
            _slotState[i].text  = tag;
            _slotState[i].color = on && p.ready ? GeoPalette.Gold
                                                : GeoPalette.WithAlpha(GeoPalette.Paper, 0.45f);
        }

        var lv = _levels.Count > 0 ? _levels[Mathf.Clamp(_levelIndex, 0, _levels.Count - 1)] : null;
        _levelText.text = lv != null ? lv.displayName : "ENDLESS";
        _levelPrev.gameObject.SetActive(host);
        _levelNext.gameObject.SetActive(host);

        _hostAddrText.text = host
            ? $"others join at   <b>{NetBootstrap.LocalAddress()}</b>   ·   port {NetBootstrap.DefaultPort}"
            : (online ? $"connected to {_address}" : $"connecting to {_address}…");

        bool meReady = MultiplayerSession.Get(MultiplayerSession.LocalId)?.ready ?? false;
        _readyLabel.text = meReady ? "CANCEL" : "READY";

        _startBtn.gameObject.SetActive(host);
        bool canStart = host && MultiplayerSession.AllReady;
        _startBtn.interactable = canStart;
        _startLabel.color = canStart ? GeoPalette.Paper : GeoPalette.WithAlpha(GeoPalette.Paper, 0.35f);

        int ready = MultiplayerSession.ReadyCount, total = MultiplayerSession.ConnectedCount;
        _startHint.text = host
            ? (canStart ? "everyone is ready" : $"{ready} / {total} ready — waiting")
            : $"{ready} / {total} ready — the host starts the match";

        _statusText.text = online ? "" : "not connected";
    }

    // ── Build ────────────────────────────────────────────────────────────────

    void BuildUI()
    {
        var go = new GameObject("LobbyCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        _canvas = go.GetComponent<Canvas>();
        _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 30;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        var bg = NewRect("Backdrop", _canvas.transform);
        Stretch(bg);
        bg.gameObject.AddComponent<Image>().color = new Color(0.055f, 0.055f, 0.065f, 1f);

        var title = NewText("Title", _canvas.transform, 68f, GeoPalette.Paper, FontStyles.Bold,
                            TextAlignmentOptions.Center);
        title.text = "MULTIPLAYER";
        Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(900f, 90f));

        BuildChoose();
        BuildRoom();
    }

    void BuildChoose()
    {
        _choosePanel = NewRect("Choose", _canvas.transform);
        Anchor(_choosePanel, new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(640f, 470f));

        var col = _choosePanel.gameObject.AddComponent<VerticalLayoutGroup>();
        col.spacing = 18f; col.childForceExpandHeight = false; col.childControlHeight = true;
        col.childAlignment = TextAnchor.UpperCenter;

        var nameLabel = NewText("NameLabel", _choosePanel, 20f, GeoPalette.WithAlpha(GeoPalette.Paper, 0.55f),
                                FontStyles.Normal, TextAlignmentOptions.Left);
        nameLabel.text = "YOUR NAME";
        Row(nameLabel.rectTransform, 26f);

        _nameField = NewInput(_choosePanel, _name, v => _name = v);

        var addrLabel = NewText("AddrLabel", _choosePanel, 20f, GeoPalette.WithAlpha(GeoPalette.Paper, 0.55f),
                                FontStyles.Normal, TextAlignmentOptions.Left);
        addrLabel.text = "HOST ADDRESS  (only needed to join)";
        Row(addrLabel.rectTransform, 26f);

        _addressField = NewInput(_choosePanel, _address, v => _address = v);

        var spacer = NewRect("Spacer", _choosePanel);
        Row(spacer, 20f);

        NewButton(_choosePanel, "HOST A ROOM", GeoPalette.Signal, out _, HostRoom);
        NewButton(_choosePanel, "JOIN A ROOM", GeoPalette.Blue,   out _, JoinRoom);

        var back = NewText("Back", _choosePanel, 18f, GeoPalette.WithAlpha(GeoPalette.Paper, 0.4f),
                           FontStyles.Normal, TextAlignmentOptions.Center);
        back.text = "Esc  ·  back to title";
        Row(back.rectTransform, 40f);
    }

    // ── Room layout ──────────────────────────────────────────────────────────
    //
    // Three stacked bands with real gaps between them, instead of one column of
    // evenly spaced rows. Everything in here used to be positioned by hand-summed
    // offsets (-330, -406, -470, -540…), which is why it read as a pile of boxes:
    // nothing was grouped, so nothing said what belonged with what.
    //
    // Now the grouping IS the design:
    //   IDENTITY   who you are and how the others reach you
    //   ROSTER     the four seats — the largest band, because it is what you sit
    //              here looking at while you wait
    //   AGREEMENT  what you are about to play, and the two buttons that commit
    //
    // Constants below are the band geometry. They are named and derived from each
    // other so moving one band cannot silently overlap the next, which is exactly
    // what the loose offsets were doing.
    const float PanelW    = 900f;
    // Tall enough that the three bands and the footer never meet: bands consume
    // Pad + 72 + gap + 290 + gap + 190 = 638 from the top, and the footer needs ~80
    // from the bottom. At 700 they overlapped by exactly the amount that is easy to
    // miss on one screen resolution and obvious on another.
    const float PanelH    = 760f;
    const float Pad       = 34f;
    const float BandGap   = 26f;
    const float SlotH     = 58f;
    const float SlotGap   = 8f;

    void BuildRoom()
    {
        _roomPanel = NewRect("Room", _canvas.transform);
        Anchor(_roomPanel, new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(PanelW, PanelH));

        float inner = PanelW - Pad * 2f;
        float y     = -Pad;   // running top edge, so a band never has to know its own offset

        // ── IDENTITY ─────────────────────────────────────────────────────────
        var idBand = Band("Identity", ref y, 72f, inner, 0.05f);

        var youLabel = NewText("YouLabel", idBand, 15f, GeoPalette.WithAlpha(GeoPalette.Paper, 0.45f),
                               FontStyles.Bold, TextAlignmentOptions.Left);
        youLabel.text = "YOU";
        Anchor(youLabel.rectTransform, new Vector2(0f, 1f), new Vector2(120f, -18f), new Vector2(200f, 18f));

        // Rename without leaving the room. Committed on enter or on losing focus
        // rather than per keystroke — a message per character would flood the host
        // and make the other three watch your name being typed.
        _roomNameField = NewInput(idBand, _name, _ => { });
        _roomNameField.onEndEdit.AddListener(SubmitName);
        Anchor((RectTransform)_roomNameField.transform, new Vector2(0f, 0f),
               new Vector2(160f, 18f), new Vector2(280f, 40f));

        // The address sits on the right of the same band because it answers the same
        // question the name does — "which of these is me, and how do the rest get in".
        _hostAddrText = NewText("Addr", idBand, 19f, GeoPalette.WithAlpha(GeoPalette.Paper, 0.72f),
                                FontStyles.Normal, TextAlignmentOptions.Right);
        Anchor(_hostAddrText.rectTransform, new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(400f, 48f));

        // ── ROSTER ───────────────────────────────────────────────────────────
        y -= BandGap;
        float rosterH = MultiplayerSession.MaxPlayers * SlotH + (MultiplayerSession.MaxPlayers - 1) * SlotGap + Pad;
        var seats = Band("Roster", ref y, rosterH, inner, 0.03f);

        var seatsLabel = NewText("SeatsLabel", seats, 15f, GeoPalette.WithAlpha(GeoPalette.Paper, 0.45f),
                                 FontStyles.Bold, TextAlignmentOptions.Left);
        seatsLabel.text = "PLAYERS";
        Anchor(seatsLabel.rectTransform, new Vector2(0f, 1f), new Vector2(120f, -16f), new Vector2(200f, 18f));

        for (int i = 0; i < MultiplayerSession.MaxPlayers; i++)
        {
            var row = NewRect($"Slot{i}", seats);
            Anchor(row, new Vector2(0.5f, 1f), new Vector2(0f, -(Pad + 4f) - i * (SlotH + SlotGap) - SlotH * 0.5f),
                   new Vector2(inner - 32f, SlotH));
            row.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f);

            // Full-height colour bar rather than a small square: at four seats the
            // colour is how you find your own row at a glance, so it gets an edge to
            // itself instead of a dot competing with the text.
            var sw = NewRect("Swatch", row);
            Anchor(sw, new Vector2(0f, 0.5f), new Vector2(7f, 0f), new Vector2(6f, SlotH - 12f));
            _slotSwatch[i] = sw.gameObject.AddComponent<Image>();

            _slotName[i] = NewText("Name", row, 24f, GeoPalette.Paper, FontStyles.Bold,
                                   TextAlignmentOptions.MidlineLeft);
            Anchor(_slotName[i].rectTransform, new Vector2(0f, 0.5f), new Vector2(250f, 0f), new Vector2(440f, 30f));

            _slotState[i] = NewText("State", row, 19f, GeoPalette.Paper, FontStyles.Normal,
                                    TextAlignmentOptions.MidlineRight);
            Anchor(_slotState[i].rectTransform, new Vector2(1f, 0.5f), new Vector2(-130f, 0f), new Vector2(240f, 30f));
        }

        // ── AGREEMENT ────────────────────────────────────────────────────────
        y -= BandGap;
        var deal = Band("Agreement", ref y, 190f, inner, 0.05f);

        var lvLabel = NewText("LvLabel", deal, 15f, GeoPalette.WithAlpha(GeoPalette.Paper, 0.45f),
                              FontStyles.Bold, TextAlignmentOptions.Left);
        lvLabel.text = "LEVEL";
        Anchor(lvLabel.rectTransform, new Vector2(0f, 1f), new Vector2(120f, -16f), new Vector2(200f, 18f));

        var lvRow = NewRect("Level", deal);
        Anchor(lvRow, new Vector2(0.5f, 1f), new Vector2(0f, -62f), new Vector2(inner - 32f, 56f));
        lvRow.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f);

        _levelPrev = SmallButton(lvRow, "<", new Vector2(0f, 0.5f), new Vector2(36f, 0f), () => StepLevel(-1));
        _levelNext = SmallButton(lvRow, ">", new Vector2(1f, 0.5f), new Vector2(-36f, 0f), () => StepLevel(+1));

        _levelText = NewText("LevelName", lvRow, 28f, GeoPalette.Gold, FontStyles.Bold,
                             TextAlignmentOptions.Center);
        Anchor(_levelText.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(inner - 180f, 36f));

        // The two buttons sit side by side, directly under what they commit to. Their
        // widths are derived from the panel so they always meet in the middle with one
        // gap, whatever PanelW becomes.
        float btnW = (inner - 32f - 20f) * 0.5f;
        _readyBtn = WideButton(deal, "READY", GeoPalette.Signal,
                               new Vector2(-(btnW + 20f) * 0.5f, -138f), btnW, out _readyLabel, ToggleReady);
        _startBtn = WideButton(deal, "START MATCH", GeoPalette.Blue,
                               new Vector2((btnW + 20f) * 0.5f, -138f), btnW, out _startLabel, StartMatch);

        // ── Footer ───────────────────────────────────────────────────────────
        // Outside the bands: these are about the screen, not about the room.
        _startHint = NewText("StartHint", _roomPanel, 19f, GeoPalette.WithAlpha(GeoPalette.Paper, 0.6f),
                             FontStyles.Normal, TextAlignmentOptions.Center);
        Anchor(_startHint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 52f), new Vector2(inner, 26f));

        _statusText = NewText("Status", _roomPanel, 18f, GeoPalette.Signal, FontStyles.Normal,
                              TextAlignmentOptions.Center);
        Anchor(_statusText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(inner, 24f));

        var back = NewText("BackRoom", _roomPanel, 17f, GeoPalette.WithAlpha(GeoPalette.Paper, 0.35f),
                           FontStyles.Normal, TextAlignmentOptions.Center);
        back.text = "Esc  ·  leave the room";
        Anchor(back.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(inner, 22f));
    }

    // A titled block of panel, stacked below whatever came before it. `y` is carried
    // by reference so each band advances the cursor itself — the caller never adds up
    // heights, which is the arithmetic that went wrong in the old layout.
    RectTransform Band(string name, ref float y, float height, float width, float tint)
    {
        var rt = NewRect(name, _roomPanel);
        Anchor(rt, new Vector2(0.5f, 1f), new Vector2(0f, y - height * 0.5f), new Vector2(width, height));
        rt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, tint);
        y -= height;
        return rt;
    }

    // ── UI primitives ────────────────────────────────────────────────────────

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static TMP_Text NewText(string name, Transform parent, float size, Color color,
                            FontStyles style, TextAlignmentOptions align)
    {
        var rt = NewRect(name, parent);
        var t  = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize = size; t.color = color; t.fontStyle = style; t.alignment = align;
        t.raycastTarget = false;
        return t;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static void Row(RectTransform rt, float height)
    {
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = height;
    }

    static Button NewButton(Transform parent, string label, Color color, out TMP_Text text,
                            UnityEngine.Events.UnityAction onClick)
    {
        var rt = NewRect("Btn_" + label, parent);
        Row(rt, 58f);

        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        text = NewText("Label", rt, 26f, GeoPalette.Paper, FontStyles.Bold, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        text.text = label;
        return btn;
    }

    Button WideButton(Transform parent, string label, Color color, Vector2 pos, float width,
                      out TMP_Text text, UnityEngine.Events.UnityAction onClick)
    {
        var btn = NewButton(parent, label, color, out text, onClick);
        // The LayoutElement NewButton adds is for the vertical column on the other
        // face; this one is anchored explicitly, so anchoring wins and it is inert.
        Anchor(btn.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), pos, new Vector2(width, 58f));
        return btn;
    }

    static Button SmallButton(Transform parent, string label, Vector2 anchor, Vector2 pos,
                              UnityEngine.Events.UnityAction onClick)
    {
        var rt = NewRect("Step_" + label, parent);
        Anchor(rt, anchor, pos, new Vector2(48f, 48f));

        var img = rt.gameObject.AddComponent<Image>();
        img.color = GeoPalette.WithAlpha(GeoPalette.Paper, 0.14f);

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        var t = NewText("Label", rt, 30f, GeoPalette.Paper, FontStyles.Bold, TextAlignmentOptions.Center);
        Stretch(t.rectTransform);
        t.text = label;
        return btn;
    }

    // TMP_InputField needs its text object, a viewport to clip against and a caret
    // target wired by hand — the prefab in the package does this for you, and
    // building one in code means doing it explicitly or getting an invisible field.
    TMP_InputField NewInput(Transform parent, string value, System.Action<string> onChanged)
    {
        var rt = NewRect("Input", parent);
        Row(rt, 54f);

        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.10f);

        var viewport = NewRect("Viewport", rt);
        viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(16f, 6f); viewport.offsetMax = new Vector2(-16f, -6f);
        viewport.gameObject.AddComponent<RectMask2D>();

        var text = NewText("Text", viewport, 26f, GeoPalette.Paper, FontStyles.Normal,
                           TextAlignmentOptions.Left);
        Stretch(text.rectTransform);
        text.raycastTarget = false;

        var field = rt.gameObject.AddComponent<TMP_InputField>();
        field.targetGraphic  = img;
        field.textViewport   = viewport;
        field.textComponent  = (TextMeshProUGUI)text;
        field.lineType       = TMP_InputField.LineType.SingleLine;
        field.caretColor     = GeoPalette.Gold;
        field.customCaretColor = true;
        field.text           = value;
        field.onValueChanged.AddListener(v => onChanged(v));
        return field;
    }
}
