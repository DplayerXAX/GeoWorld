# GeoWorld

A music-driven tower defense game built in Unity. Players construct paths on a 3D block grid — the path geometry directly shapes the procedural music that plays as units traverse it. Longer, more varied paths produce richer soundscapes and stronger defenses.

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Engine | Unity (URP) |
| Audio | Wwise |
| Language | C# / HLSL |
| Render | Universal Render Pipeline, custom additive shaders |

---

## Core Loop

```
Place blocks on the 3D grid
        ↓
Path auto-detects between start and end endpoints
        ↓
Press Space — unit traverses the path, generating live music
        ↓
Completed path becomes a persistent ambient loop layer
        ↓
New endpoint added every N runs → expanding network
```

---

## Script Architecture

```
Assets/Scripts/
├── Audio/
│   ├── ArpeggiatorManager.cs
│   ├── AudioManager.cs
│   ├── BackgroundReactor.cs
│   ├── LoopManager.cs
│   └── PathFlowManager.cs
│
├── Construct/
│   ├── BlockData.cs
│   ├── BlockRenderer.cs
│   ├── GameFlowManager.cs
│   ├── GridOverlay.cs
│   ├── GridSystem.cs
│   ├── OrbitCamera.cs
│   ├── PlacementController.cs
│   ├── SelectedBlock.cs
│   └── pathFinding/
│       ├── EndpointVisual.cs
│       ├── FaceBuilder.cs
│       ├── LevelEndpointGenerator.cs
│       ├── SurfaceGraphBuilder.cs
│       ├── SurfacePathfinding.cs
│       ├── SurfaceUnit.cs
│       ├── SurfaceUnitVisual.cs
│       └── SelectableBlock.cs
│
├── Resource/
│   └── ResourceManager.cs
│
└── Audio/ (Shaders at Assets/Shader/)
    ├── BlackHoleCore.shader
    └── UnitGlow.shader
```

---

## Script Reference

### Audio

#### `ArpeggiatorManager.cs`
The musical brain of the game. Generates melody notes in **C Dorian** (C D Eb F G A Bb) as a unit walks each path step. Note selection is driven by path geometry — horizontal direction, vertical contour, and zigzag frequency all bias the melodic line. Supports recording a traversal and replaying it as a low-register ambient loop.

Key methods:
- `PlayMelodyNote()` — called per path step; reads geometry to shape note choice
- `PlayBassRoot()` — triggers a bass note on block-type transitions
- `PlayAmbientNote()` — lightweight playback used by loop layers and path scans
- `StartRecording()` / `StopRecording()` — captures a traversal for loop replay

#### `AudioManager.cs`
Wwise integration hub. All Wwise `PostEvent` and `SetRTPCValue` calls go through here. Manages the BGM Switch Container (chord pad layer that changes with block type) and exposes `PlayArpNote(degree, octave, velocity, emitter)` — the `emitter` parameter positions the sound in 3D space for distance attenuation.

#### `BackgroundReactor.cs`
Listens to music events and drives background visual/environmental responses (intensity, chord colour shifts).

#### `LoopManager.cs`
Manages all active ambient loop layers. Each completed path traversal is registered here as a `LoopEntry` (note sequence + visual block list + grid cells). Loops play back at a lower octave register so they sit beneath the live melody. Exposes `RemoveLoopsOverlapping(cells)` — called when a block is lifted to stop loops whose path has been broken.

#### `PathFlowManager.cs`
Draws `LineRenderer`-based laser lines for paths.
- **Live line** — warm-white preview shown automatically whenever a valid path exists; updated on every block placement
- **Loop lines** — coloured lines added when a run commits; tracked by grid cell so they disappear when their blocks are removed
- Corner waypoints are calculated geometrically (face-plane intersection) so lines route around block edges rather than cutting through them

---

### Construct

#### `GameFlowManager.cs`
Top-level game state machine. Manages phases (`Build → ReadyToRun → Running`) and the roguelite endpoint accumulation loop.

Key responsibilities:
- `EvaluateGrid()` — rebuilds the surface graph, refreshes the live path preview, and immediately destroys the running unit if its path is broken. Called after every block placement or removal.
- `Run()` — spawns the `SurfaceUnit` and converts the live preview line into a tracked loop line
- `EndRunningPhase()` — promotes finished unit to ambient loop, retires the oldest loop if the layer limit is exceeded, adds a new endpoint every `runsPerEndpoint` completions

Inspector tunables: `blocksPerTurn`, `runsPerEndpoint`, `maxLoopLayers`, `scanBeats`

#### `PlacementController.cs`
Handles all player interaction with blocks.
- **Select mode** — click to select, double-click to pick up and re-edit, WASD to pan camera
- **Edit mode** — WASD/QE to move block, 1/2/3 to rotate on each axis, left-click to place
- **Tray system** — blocks offered each round are displayed as selectable tokens; clicking a token enters Edit mode
- Placing a **new** block from the tray deducts resources via `ResourceManager`; repositioning an already-placed block is free
- Calls `GameFlowManager.EvaluateGrid()` after every placement or block lift

#### `GridSystem.cs`
The 3D voxel grid. Stores `PlacedBlockInstance` records keyed by `Vector3Int` cell. Provides `GridToWorld()` (cell centre), `WorldToGrid()`, `GetInstanceAt()`, `RegisterInstance()`, `RemoveInstance()`.

#### `BlockData.cs`
`ScriptableObject` defining a block type. Fields: `blockType` (enum: Home / Lift / Pull / Shadow / Turret), `cells` (multi-cell shape offsets), `onStepEvent` (Wwise event fired when a unit steps on it).

Each type carries musical meaning:
| Type | Chord | Character |
|------|-------|-----------|
| Home | Cm | Stable root |
| Lift | F | Bright, upward |
| Pull | Gm | Tension, forward motion |
| Shadow | Bb | Dark, unstable |

#### `GridOverlay.cs`
Renders the dashed-line 3D grid using `GL.LINES` in `OnRenderObject`. Toggle with **G** key. Outer edges are brighter than interior lines. Also draws a cursor highlight box on the hovered cell.

#### `OrbitCamera.cs`
Orbit/pan/zoom camera. `SetFocus()` smoothly pivots to a target transform (used when double-clicking a block).

---

### Construct / pathFinding

#### `SurfaceGraphBuilder.cs`
Scans all placed blocks and builds a graph of `FaceNode` objects — one node per exposed block face. Connects adjacent coplanar faces and faces that share an edge across different normals (e.g., top face → side face for corner transitions).

#### `SurfacePathfinding.cs`
A* search over the `FaceNode` graph. Accepts lists of start and end nodes (supporting multiple endpoints) and returns the shortest surface path.

#### `FaceBuilder.cs`
Low-level helper that enumerates the six faces of a block, checks which are exposed (no adjacent block), and creates the corresponding `FaceNode` with world position and surface normal.

#### `SurfaceUnit.cs`
The entity that traverses a path. Moves beat-by-beat (tempo driven by `bpm`), interpolating between face centres using a quadratic Bezier arc at surface-normal transitions. Calls `ArpeggiatorManager.PlayMelodyNote()` at each step. On completion, hands off the recorded note sequence to `LoopManager` and signals `GameFlowManager.EndRunningPhase()`.

#### `SurfaceUnitVisual.cs`
Abstract geometric art visual attached to `SurfaceUnit`. Procedurally builds:
- A glowing core sphere (pulsing)
- Three gyroscope rings at different tilts, each spinning at a different speed and colour (cyan / violet / amber)
- Four orbiting cube nodes, each on a different tilted orbital plane

All primitives use the `Custom/UnitGlow` shader.

#### `EndpointVisual.cs`
Applied to start and end blocks. Modifies the block's material to a transparent aerogel haze, then adds a `BlackHoleCore` sphere at the block centre. Start = warm palette (orange/amber/magenta); End = cool palette (cyan/mint/violet). Start also spawns a thin antenna spire; End spawns a spinning ring.

#### `LevelEndpointGenerator.cs`
Places start and end endpoint blocks on the grid surface within a configurable distance range. Generates additional endpoints each `runsPerEndpoint` cycles, alternating between adding starts and ends. Distance window widens over rounds.

---

### Resource

#### `ResourceManager.cs`
Four-resource economy tied directly to block types (Home / Lift / Pull / Shadow). Placing a new block from the tray costs resources of its type; repositioning a block already on the grid is free.

Income API for the battle system:
```csharp
ResourceManager.Instance.OnEnemyPassedBlock(BlockType.Home); // +1 per block walked
ResourceManager.Instance.OnWaveComplete(pathBlockTypes);      // wave-end bonus
```

Subscribe to `OnResourceChanged(BlockType, int)` for UI updates. A temporary `OnGUI` overlay displays current amounts until proper UI is connected.

---

### Shaders

#### `UnitGlow.shader` (`Custom/UnitGlow`)
Simple additive-blend emissive shader. Works on any mesh. A Fresnel rim term brightens silhouette edges, giving flat primitives apparent depth without lighting. Animated with a sine-wave pulse. Properties: `_Color`, `_Intensity`, `_RimBoost`, `_RimPower`, `_PulseSpeed`, `_PulseDepth`.

#### `BlackHoleCore.shader` (`Custom/BlackHoleCore`)
Single additive pass plasma-sphere shader with four composited layers:
- **Inner core glow** — coloured centre falloff (`_DiskColor`)
- **Accretion disk ring** — sharp band at configurable radius
- **Flowing streams** — three overlapping directional sine waves on the world-space normal, producing seamless animated plasma without UV seams
- **Outer nebula haze** — soft rim colour (`_HazeColor`)

---

## Controls

| Key | Action |
|-----|--------|
| Left-click | Select block / place block |
| Double-click | Pick up placed block for re-editing |
| Tab | Enter / exit Edit mode |
| WASD | Pan camera (Select) / move block (Edit) |
| Q / E | Camera down/up (Select) / block down/up (Edit) |
| 1 / 2 / 3 | Rotate block on X / Y / Z axis (Edit) |
| Space | Run — spawn unit on current path |
| P | Force re-evaluate path preview |
| G | Toggle grid overlay |
| B | Cancel run, return to Build phase |

---

## Project Setup

1. Open in **Unity 2022.3+** with URP package installed
2. Open the Wwise project and generate SoundBanks before entering Play mode
3. Assign the `AkAudioListener` component to the main camera
4. In the scene, ensure these Manager GameObjects are present:
   - `AudioManager` — assign BGM, Note, and chord Wwise events
   - `PathFlowManager` — assign `PathLaser` material
   - `LoopManager`, `ArpeggiatorManager`, `ResourceManager`
   - `GameFlowManager` — assign `GridSystem`, `PlacementController`, `LevelEndpointGenerator`, `SurfaceUnit` prefab
5. Set `GridSystem.size` to at least **10 × 3 × 10** for a comfortable play area
