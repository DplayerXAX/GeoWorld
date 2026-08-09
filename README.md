# GeoWorld

A tower-defense game played on the **surface of a 3D block sculpture**.

You don't build a path — you build a *solid*, and the path is whatever route emerges across its exposed faces. Enemies walk that surface from the spawn portals to your endpoints; turrets placed on the same solid shoot them. Where the blocks sit determines the route, the firing angles, the economy, and the music all at once.

On top of that sits a **synergy layer**: blocks carry colours, and certain arrangements (a closed loop, a solid cube, a long straight run, one connected mass) activate faction bonuses. Deciding whether to place a block for *pathing* or for *synergy* is the game's central tension.

---

## Requirements

| | |
|---|---|
| **Unity** | `6000.3.6f1` (Unity 6, URP) |
| **Audio** | Wwise — the Unity integration is committed, but SoundBanks must be generated |
| **Git LFS** | **Required.** `.bnk` SoundBanks are stored in LFS |

### First-time setup

```bash
git lfs install
```

Run this **before** cloning, or run it after and then `git lfs pull`. Without it you'll get ~130-byte text pointer files where the SoundBanks should be, and Unity will fail to load any audio.

Then, in Wwise, open `Geo world/` and **Generate SoundBanks** before entering Play mode.

---

## Scenes

Build order matters — `LoadingScreen.Go()` looks scenes up by name against Build Settings.

| # | Scene | Role |
|---|-------|------|
| 0 | `Title` | Save-slot select, main menu, settings, gallery entry |
| 1 | `LevelSelect` | The world map — a walkable block surface with level nodes, NPCs and minigame entrances on it |
| 2 | `gamePlay` | The actual tower-defense level |
| 3 | `Gallery` | Unlocked-art viewer |

`mapMaker` and `material_Test` are editor-only tools and are deliberately **not** in Build Settings.

Scenes hand off through **`RunConfig`** — a plain static class, no `DontDestroyOnLoad`, no serialization. `LevelSelect` writes the chosen level into it, loads `gamePlay`, and `GameFlowManager.Start()` reads it back to decide how to open.

---

## Core loop

```
LevelSelect: walk the pawn to a level node → Enter
        ↓
Build phase — spend currency, place blocks and turrets on the grid
        ↓          (synergies evaluate live on every placement)
Space — the wave runs; enemies path across the surface, turrets fire
        ↓
Wave cleared → pick 1 of N upgrade cards → next wave
        ↓
All waves cleared → level clear screen → back to the map
   or lives hit 0 → the sky collapses, GAME OVER
```

---

## Architecture

Roughly 200 scripts under `Assets/Scripts/`, grouped by responsibility rather than by Unity type.

| Folder | Owns |
|--------|------|
| `Construct/` | The grid, block placement, camera, and `GameFlowManager` (the phase state machine: `Init → Build → ReadyToRun → Running → GameOver`) |
| `Construct/pathFinding/` | Surface graph + A*. `SurfaceGraphBuilder` turns placed blocks into a graph of exposed-face nodes; `SurfacePathfinding` searches it |
| `Construct/Synergy/` | Rule detection, effect application, and the per-faction visualisers |
| `Construct/Waves/` | Wave definitions and the budget-driven `WaveGenerator` |
| `Construct/Upgrades/` | The end-of-wave "pick 1 of N" card system |
| `Enemy/` | Enemy units, spawning, and the per-level mechanics (chaos blocks, shrines, random destruction) |
| `Turret/` | Turret firing, bullets, and status effects (slow, burn, gravity well, suppression) |
| `Audio/` | Wwise hub, the procedural arpeggiator, ambient loop layers, and the skybox reactor |
| `Meta/` | Everything outside a run: save system, level database, the LevelSelect map, tutorials, settings, loading screen |
| `Dialogue/` | A self-contained visual-novel runner — builds its own UGUI, zero scene setup |
| `Minigame/` | Standalone diversions reachable from the map (currently *Stack Well*, a 3D Tetris) |
| `UI/` | HUD, shop, panels, pause and game-over screens |
| `VFX/` | Camera shake, death explosions, outlines, combo and currency effects, URP renderer features |
| `Balance/` | `BalanceTable` — one read-only ScriptableObject holding every tunable number |
| `Input/` | Gamepad support and the virtual cursor |

### Key subsystems

**Surface pathfinding.** `FaceBuilder` enumerates each block's six faces and keeps the exposed ones. `SurfaceGraphBuilder` links adjacent coplanar faces *and* faces meeting across an edge, so units can walk over corners. Everything downstream — enemy routing, the live path preview, turret line-of-sight — reads that one graph, so it's rebuilt on every placement via `GameFlowManager.EvaluateGrid()`.

**Synergies.** Six factions, each a `SynergyRule` over the current `BoardSnapshot`:

| Faction | Shape required |
|---------|----------------|
| **Abundance** | A closed loop of same-colour pieces |
| **Enlightenment** | A filled axis-aligned cube — 2³, 3³, 4³ for tiers 1–3 |
| **Exploration** | A straight run of same-colour cells along any axis |
| **Harmony** | Every piece of that colour in one face-connected component |
| **Order** | N same-colour pieces connected face-to-face |
| **Heresy** | Placeholder — never fires |

`SynergyEvaluator` re-runs after every placement and removal. The important rule is **first-locked**: once a synergy claims a set of pieces, no other rule — and no second instance of the same rule — can see them. Active claims survive re-evaluation and can *grow* into newly placed pieces rather than being torn down and rebuilt. Read the header comment in `SynergyEvaluator.cs` before touching any of this; the ordering is load-bearing.

**Blocks.** `BlockData` is a ScriptableObject: a `BlockType` (`Home` / `Lift` / `Pull` / `Shadow` / `Turret` / `SlowTurret` / `AoeTurret`), a multi-cell shape, and a Wwise step event. The four terrain types each map to a chord, so the board's composition drives the music:

| Type | Chord | Character |
|------|-------|-----------|
| Home | Cm | Stable root |
| Lift | F | Bright, upward |
| Pull | Gm | Tension, forward motion |
| Shadow | Bb | Dark, unstable |

**Turrets.** Three modes, colour-coded to stay legible on a busy board: Basic (steel white), Slow (ice blue), AOE (hot orange). Stats live in `BalanceTable`, not on the prefabs.

**Reactive sky.** `BackgroundReactor` clones the skybox material at runtime and drives `ManifoldSkybox.shader` from live game state — beat pulses, chord colour, combat intensity, kill reactions, damage flashes, the level-clear crystallisation, and a **health-driven collapse** that tears the sky into slipped bands and drops cells to the engine's missing-material checker as you lose lives.

**Saves.** Three independent slots, each a `profile_<n>.json`. Mutators persist immediately, so a crash never costs a clear or a purchase. `SaveSystem.PeekSlot` reads a slot without selecting it, for the title screen.

---

## Controls

| Input | Build phase | Editing a held block |
|-------|-------------|----------------------|
| Left-click | Select block / place | Place |
| Double-click | Pick up a placed block for re-editing | — |
| WASD / QE | Move camera | Move the block |
| Right-drag | Rotate view | Rotate view |
| Scroll | Zoom | Set placement distance |
| 1 / 2 / 3 | — | Rotate on X / Y / Z |
| Tab | Enter / exit edit mode | |
| Space | Start the wave | |
| Shift (hold) | Peek past any open panel at the world behind it | |
| Delete | Remove the selected block | |
| G | Toggle grid overlay | |
| Esc | Pause menu | |

Gamepad is supported through `GamepadInputDriver` and a virtual cursor.

---

## Conventions

Things that aren't obvious from reading a single file, and that will bite you if you guess:

- **No namespaces.** Nothing in this codebase is namespaced. Don't introduce one.
- **UI is built in code, not in scenes.** Panels, HUDs, overlays and whole screens construct their own `Canvas` + `CanvasScaler` + `GraphicRaycaster` at runtime, usually auto-spawned via `[RuntimeInitializeOnLoadMethod]`. There is almost nothing to wire in the Inspector, and that's deliberate — it survives scene merges.
- **Wwise events cannot be created from code.** An `AK.Wwise.Event` field only works when it was assigned through a real Inspector reference; there is no `Find`-by-name equivalent. A pure-code system that needs audio must load a Resources ScriptableObject carrying the Inspector-assigned field (see `MinigameAudio`).
- **`new Material(shader)` loses your tuning.** It only picks up the shader's Properties-block *defaults*, not what you set on the `.mat` asset. Any runtime system that should respect a tuned material must `Resources.Load<Material>()` the real asset.
- **Shaders used only from code get stripped at build time.** They need an entry in Always Included Shaders *and* a keepalive material under `Assets/Resources/GeoWorldShaderKeepalive/`.
- **`BalanceTable` is read-only at runtime.** Query it; never write back into it.
- **`OrbitCamera` owns the camera transform.** It rewrites position and rotation every `LateUpdate`. Any effect that moves the camera has to live inside it (as camera shake does) or it will simply be overwritten.

---

## Repo notes

`.bnk` SoundBanks are tracked with Git LFS. Two consequences worth knowing:

- The three platform banks (Mac / WebGL / Windows) are byte-identical, so LFS deduplicates them into one object.
- LFS stores whole files, not deltas — every regeneration of the bank costs its full size again against the account's LFS quota. Regenerate deliberately, not habitually.

`Geo world/` is the Wwise project. `Assets/StreamingAssets/Audio/` holds the banks Unity actually loads.
