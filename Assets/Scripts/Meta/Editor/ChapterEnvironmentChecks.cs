using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Runs in an isolated preview scene; never opens a player profile or writes a save.
// CLI: unity command eval --code "return ChapterEnvironmentChecks.Run();"
public static class ChapterEnvironmentChecks
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static void Check(bool ok, string message) { if (!ok) throw new Exception("Environment check failed: " + message); }
    static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Private).Invoke(obj, args);

    [MenuItem("GeoWorld/Validation/Chapter Environment")]
    static void MenuRun() => Debug.Log(Run());

    public static string Run()
    {
        Check(!EditorApplication.isPlaying, "Run checks outside Play mode");
        var oldGrid = GridSystem.instance;
        var oldFlow = GameFlowManager.Instance;
        var oldPlacement = PlacementController.Instance;
        var oldResources = ResourceManager.Instance;
        var oldEnvironment = ChapterEnvironmentController.Instance;
        var oldMode = RunConfig.Mode; var oldLevel = RunConfig.Level; var oldSeed = RunConfig.Seed;
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Environment checks");
        SceneManager.MoveGameObjectToScene(root, scene);
        root.SetActive(false); // No lifecycle callbacks from unrelated gameplay systems.
        var assets = new List<UnityEngine.Object>();
        var passed = new List<string>();
        ChapterEnvironmentController env = null;
        try
        {
            var profile = ScriptableObject.CreateInstance<ChapterEnvironmentProfile>(); assets.Add(profile);
            var rng = new Xoshiro256StarStar(123);
            ChapterWeather previous = ChapterWeather.None;
            var seen = new HashSet<ChapterWeather>();
            for (int i = 0; i < 300; i++)
            {
                var next = profile.Pick(rng, previous);
                Check(next != previous && next != ChapterWeather.None, "no consecutive repeats");
                previous = next; seen.Add(next);
            }
            Check(seen.Count == 3, "all three weather effects reachable");
            profile.effects = Array.Empty<ChapterWeather>();
            Check(profile.Pick(rng, previous) == ChapterWeather.None, "empty pool");
            profile.effects = new[] { ChapterWeather.Puddles, ChapterWeather.Puddles };
            Check(profile.Pick(rng, ChapterWeather.Puddles) == ChapterWeather.Puddles, "one distinct effect");
            passed.Add("random selection: diversity, no repeats, empty/single pool");

            var grid = root.AddComponent<GridSystem>(); grid.Init(); GridSystem.instance = grid;
            var flow = root.AddComponent<GameFlowManager>(); flow.enabled = false; flow.gridSystem = grid;
            flow.runSeed = 71; GameFlowManager.Instance = flow;
            var pc = root.AddComponent<PlacementController>(); pc.enabled = false; pc.grid = grid;
            PlacementController.Instance = pc;
            pc.previewParent = new GameObject("Preview").transform; pc.previewParent.SetParent(root.transform);
            var rm = root.AddComponent<ResourceManager>(); rm.enabled = false; ResourceManager.Instance = rm;
            var ground = ScriptableObject.CreateInstance<BlockData>(); assets.Add(ground);
            ground.name = "EnvironmentTestGround"; ground.blockType = BlockType.Home; ground.cells = new[] { Vector3Int.zero };
            var gun = ScriptableObject.CreateInstance<BlockData>(); assets.Add(gun);
            gun.name = "EnvironmentTestGun"; gun.blockType = BlockType.Turret; gun.cells = new[] { Vector3Int.zero };
            pc.blocks = new[] { ground, gun };
            var level = ScriptableObject.CreateInstance<LevelDefinition>(); assets.Add(level);
            RunConfig.Mode = GameMode.Level; RunConfig.Level = level;
            flow.endlessEnvironment = profile;
            Check(ChapterEnvironmentController.Ensure(flow) == null, "formal levels excluded even with an Endless profile");
            RunConfig.SetEndless();
            flow.endlessEnvironment = null;
            Check(ChapterEnvironmentController.Ensure(flow) == null, "empty Endless profile disables weather");
            flow.endlessEnvironment = profile;
            env = ChapterEnvironmentController.Ensure(flow);
            Check(env != null && env.Profile == profile, "single player Endless controller uses scene profile");
            passed.Add("Endless routing, optional profile, formal levels excluded");
            Call(env, "Awake");

            PlacedBlockInstance Place(BlockData data, Vector3Int cell, bool turret = false)
            {
                var go = new GameObject("Test piece"); go.transform.SetParent(root.transform);
                go.transform.position = grid.GridToWorld(cell);
                if (turret) { var t = go.AddComponent<TurretController>(); t.enabled = false; t.attackRange = 8f; }
                var ins = new PlacedBlockInstance { data = data, visualObject = go };
                ins.occupiedCells.Add(cell); grid.RegisterInstance(ins); return ins;
            }
            var a = Place(ground, Vector3Int.zero);
            var b = Place(ground, new Vector3Int(4, 0, 0));
            var tower = Place(gun, new Vector3Int(1, 0, 0), true);
            var turret = tower.visualObject.GetComponent<TurretController>();
            object shrine = new object(); turret.Mods.Set(Stat.TurretRange, shrine, mul: 1.5f);
            var mainState = string.Join(",", flow.Rng.SaveState());

            EnvironmentRunState Saved(ChapterWeather effect, int wave) => new EnvironmentRunState {
                themeId = profile.themeId, wave = wave, effect = effect,
                randomState = Array.ConvertAll(new Xoshiro256StarStar(456).SaveState(), n => n.ToString())
            };
            var mist = Saved(ChapterWeather.RainMist, 1);
            mist.mistCenters.Add(grid.GridToWorld(Vector3Int.zero));
            mist.mistCenters.Add(grid.GridToWorld(Vector3Int.zero));
            env.Restore(mist, 1);
            var markers = (GameObject)typeof(ChapterEnvironmentController).GetField("_markers", Private).GetValue(env);
            Check(markers != null && markers.GetComponentsInChildren<ParticleSystem>(true).Length == 2,
                "each mist region has visible rain");
            foreach (var rain in markers.GetComponentsInChildren<ParticleSystem>(true))
                Check(rain.main.maxParticles <= 256 && rain.emission.rateOverTime.constant > 0f,
                    "rain is enabled with a bounded particle budget");
            foreach (var collider in markers.GetComponentsInChildren<Collider>(true))
                Check(!collider.enabled, "weather visuals never intercept gameplay input");
            Check(Mathf.Approximately(turret.EffectiveRange, 9f), "mist stacks with existing modifiers, not overlapping mist");
            Check(env.IsInMist(turret), "mist selection readout");
            tower.occupiedCells[0] = new Vector3Int(8, 0, 0); env.RefreshBoard();
            Check(Mathf.Approximately(turret.EffectiveRange, 12f), "moving out removes only environment modifier");
            tower.occupiedCells[0] = new Vector3Int(1, 0, 0); env.RefreshBoard();
            env.EndCombat(); env.RefreshBoard();
            Check((GameObject)typeof(ChapterEnvironmentController).GetField("_markers", Private).GetValue(env) == null,
                "mist and rain cleaned up together");
            Check(Mathf.Approximately(turret.EffectiveRange, 12f), "ended effects cannot reappear on board refresh");
            passed.Add("mist overlap, range composition, moving out, cleanup");

            var puddles = Saved(ChapterWeather.Puddles, 2); puddles.puddleCells.Add(Vector3Int.zero);
            env.Restore(puddles, 2);
            Check(Mathf.Approximately(env.SpeedAt(Vector3Int.zero, Vector3.up), 0.75f), "wet top slows");
            Check(env.SpeedAt(Vector3Int.zero, Vector3.right) == 1f && env.SpeedAt(Vector3Int.zero, Vector3.down) == 1f, "side/bottom exempt");
            grid.SetOccupied(new Vector3Int(0, 9, 0)); env.RefreshBoard();
            Check(env.SpeedAt(Vector3Int.zero, Vector3.up) == 1f, "distant overhead roof shelters");
            grid.ClearOccupied(new Vector3Int(0, 9, 0)); env.RefreshBoard();
            Check(env.SpeedAt(Vector3Int.zero, Vector3.up) < 1f, "uncover restores same puddle");
            var enemy = new GameObject("Enemy"); enemy.transform.SetParent(root.transform);
            var unit = enemy.AddComponent<EnemySurfaceUnit>(); unit.enabled = false;
            unit.SetSpeedMultiplier(0.5f);
            // Effective movement uses an independent environment factor rather than overwriting slow.
            Check(Mathf.Approximately(unit.TemporarySpeedMultiplier, 0.5f), "existing slow channel preserved before entering face");
            Place(ground, Vector3Int.zero); env.RefreshBoard();
            Check(env.State.puddleCells.Count == 0, "replacement does not inherit old surface water");
            passed.Add("puddles: top only, overhead shelter, uncover, replacement, slow channel");

            profile.effects = new[] { ChapterWeather.RainMist, ChapterWeather.Puddles, ChapterWeather.BlockRain };
            foreach (var effect in profile.effects)
            {
                profile.effects = new[] { effect };
                env.BeginWave(env.State.wave + 1);
                string initial = JsonUtility.ToJson(env.Capture());
                env.BeginWave(env.State.wave);
                Check(initial == JsonUtility.ToJson(env.Capture()), "begin same wave is idempotent");
                var saved = env.Capture();
                env.BeginWave(saved.wave + 1);
                string future = JsonUtility.ToJson(env.Capture());
                env.Restore(saved, saved.wave);
                Check(initial == JsonUtility.ToJson(env.Capture()), "current state restores exactly");
                env.BeginWave(saved.wave + 1);
                Check(future == JsonUtility.ToJson(env.Capture()), "future RNG state restores exactly");
            }
            Check(mainState == string.Join(",", flow.Rng.SaveState()), "main run RNG unchanged");
            passed.Add("all effects: idempotence, saved regions/drop plan, deterministic continuation, isolated RNG");

            profile.effects = new[] { ChapterWeather.BlockRain };
            env.BeginWave(env.State.wave + 1);
            Check(env.State.drops.Count == 3, "bounded drop plan");
            foreach (var drop in env.State.drops) Check(drop.blockAssetName == ground.name, "no turret drops");
            env.BeginCombat();
            var pickup = RainBlockPickup.Create(env, ground, BlockColor.None, 0.5f, 10f);
            Check(pc.TryGrabRainBlock(pickup) && pickup.Held && pc.currentBlock == ground, "free pickup enters placement");
            var other = RainBlockPickup.Create(env, ground, BlockColor.None, 0.6f, 10f);
            Check(!pc.TryGrabRainBlock(other) && pc.currentBlock == ground, "held item cannot be overwritten");
            Call(pc, "CancelEditMode");
            Check(!pickup.Held && pc.currentBlock == null, "cancel resumes falling without consuming supply");
            Check(pc.TryGrabRainBlock(pickup), "can catch again");
            pc.ClearHeldRainBlock();
            Check(pc.currentBlock == null, "wave cleanup clears hand");
            passed.Add("block rain: terrain-only cap, pickup, no overwrite, cancel, clear hand");

            a.rainGranted = true;
            Check((int)Call(pc, "ComputeSellRefund", a) == 0, "free block has no sale refund");
            b.rainGranted = false;
            Check((int)Call(pc, "ComputeSellRefund", b) > 0, "purchased block retains refund");
            b.rainGranted = true;
            var snapshot = JsonUtility.FromJson<GridSnapshot>(JsonUtility.ToJson(SnapshotManager.Capture()));
            Check(snapshot.blocks.Exists(x => x.rainGranted), "snapshot preserves free provenance");
            var oldSave = JsonUtility.FromJson<LevelRunSave>("{\"version\":1,\"levelId\":\"1-1\"}");
            // Unity's serializer may materialize a missing nested class as an empty
            // object, rather than null. Either must take the legacy initialization path.
            Check(oldSave.environment == null || string.IsNullOrEmpty(oldSave.environment.themeId), "legacy save defaults");
            env.Restore(oldSave.environment, 40);
            Check(env.State.wave == 40, "legacy save initializes environment once");
            passed.Add("refund provenance, snapshot roundtrip, legacy save initialization");

            foreach (int n in new[] { 1, 2, 3 })
            {
                var authored = AssetDatabase.LoadAssetAtPath<LevelDefinition>($"Assets/scriptableObject/Level/Level_{n}.asset");
                Check(authored != null, "formal level asset retained " + n);
                RunConfig.SetLevel(authored);
                Check(ChapterEnvironmentController.Ensure(flow) == null, "formal level excludes Endless weather " + n);
            }
            var material = Resources.Load<Material>("GeoWorldShaderKeepalive/EnvironmentMarker_keep");
            Check(material != null && !ShaderUtil.ShaderHasError(material.shader), "marker shader retained and valid");
            var mistMaterial = Resources.Load<Material>("GeoWorldShaderKeepalive/RainMist_keep");
            Check(mistMaterial != null && !ShaderUtil.ShaderHasError(mistMaterial.shader), "rain mist shader retained and valid");
            passed.Add("formal chapter bindings removed and shader resources valid");
            return "PASS: " + string.Join("; ", passed);
        }
        finally
        {
            if (env != null) UnityEngine.Object.DestroyImmediate(env);
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (var asset in assets) UnityEngine.Object.DestroyImmediate(asset);
            GridSystem.instance = oldGrid; GameFlowManager.Instance = oldFlow;
            PlacementController.Instance = oldPlacement; ResourceManager.Instance = oldResources;
            typeof(ChapterEnvironmentController).GetProperty("Instance").GetSetMethod(true).Invoke(null, new object[] { oldEnvironment });
            RunConfig.Mode = oldMode; RunConfig.Level = oldLevel; RunConfig.Seed = oldSeed;
        }
    }
}
