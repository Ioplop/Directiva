using System;
using System.Collections.Generic;
using DMath;
using DOrchestration;
using DSimulation;
using DSCompilation.DScript.Backend;
using DSCompilation.DScript.Modules;
using Directiva.SimulationUI;
using Directiva.MainMenu;
using Directiva.Levels;
using UnityEngine;

namespace Directiva.SimulationFrontend
{
    /// <summary>
    /// Scene-level playback host. Accepts an Orchestrator, consumes its event stream,
    /// and advances it at 50 simulated Hz using Unity unscaled frame time.
    /// Time.timeScale affects visuals only; never the authoritative tick accumulator.
    /// The Inspector programs are a temporary demo bootstrap, NOT the code editor.
    /// </summary>
    public sealed class SimulationSceneSynchronizer : MonoBehaviour
    {
        public const string DefaultPlayerScript =
            "shared built = 0\n" +
            "shared assault = False\n" +
            "\n" +
            "def nexus_main():\n" +
            "    if built < 5 and self.can_build:\n" +
            "        if build(worker):\n" +
            "            built += 1\n" +
            "    if built >= 5 and len(units()) >= 5 and self.resources >= 5 and not assault:\n" +
            "        assault = True\n" +
            "        print(\"Bando 1: ataque\")\n" +
            "\n" +
            "def worker():\n" +
            "    if assault:\n" +
            "        target = enemy_nexus()\n" +
            "        if target != None:\n" +
            "            move(target)\n" +
            "            attack(target)\n" +
            "    elif self.cargo >= 5:\n" +
            "        stop_action()\n" +
            "        move(self.nexus)\n" +
            "    else:\n" +
            "        deposit = closest_resource()\n" +
            "        if deposit != None:\n" +
            "            move(deposit)\n" +
            "            gather(deposit)\n";

        public const string DefaultEnemyScript =
            "shared built = 0\n" +
            "shared charge = False\n" +
            "\n" +
            "def nexus_main():\n" +
            "    if built < 5:\n" +
            "        if self.can_build:\n" +
            "            if build(worker):\n" +
            "                built += 1\n" +
            "    if built >= 5 and len(self.units()) >= 5 and self.resources >= 5 and not charge:\n" +
            "        charge = True\n" +
            "        print(\"Bando 2: ataque\")\n" +
            "\n" +
            "def worker():\n" +
            "    if charge:\n" +
            "        base = enemy_nexus()\n" +
            "        if base != None:\n" +
            "            attack(base)\n" +
            "            move(base)\n" +
            "    elif self.is_cargo_full:\n" +
            "        stop_action()\n" +
            "        move(self.nexus)\n" +
            "    else:\n" +
            "        vein = closest_resource()\n" +
            "        if vein != None:\n" +
            "            gather(vein)\n" +
            "            move(vein)\n";

        [Header("Scene connections")]
        [SerializeField] private SimulationUIController ui;
        [SerializeField] private SimulationSceneView view;

        [Header("Demo world (50 x 50)")]
        [SerializeField, Min(16)] private int worldWidth = 50;
        [SerializeField, Min(16)] private int worldHeight = 50;
        [SerializeField] private int seed = 20261111;
        [SerializeField, Min(0)] private int initialResourcesPerSide = 40;
        [SerializeField, Min(1)] private int resourcesPerDeposit = 100;
        [SerializeField, Min(1)] private int depositRichness = 1;
        [Tooltip("Side 1 deposit coordinates; side 2 gets 180-degree mirrored deposits.")]
        [SerializeField] private Vector2Int[] side1Deposits =
        {
            new Vector2Int(9, 20),
            new Vector2Int(14, 34),
            new Vector2Int(19, 16)
        };

        [Header("DScript for side 1 (player)")]
        [SerializeField] private string playerModule = "player";
        [SerializeField] private string playerNexusFunction = "nexus_main";
        [SerializeField] private TextAsset playerScriptAsset;
        [SerializeField, TextArea(12, 26)] private string playerSource = DefaultPlayerScript;

        [Header("DScript for side 2 (enemy)")]
        [SerializeField] private string enemyModule = "enemy";
        [SerializeField] private string enemyNexusFunction = "nexus_main";
        [SerializeField] private TextAsset enemyScriptAsset;
        [SerializeField, TextArea(12, 26)] private string enemySource = DefaultEnemyScript;

        [Header("Execution")]
        [SerializeField, Min(1)] private int vmStepsPerTick = 10;
        [SerializeField, Min(1)] private int matchDurationSeconds = 300;
        [SerializeField, Min(1)] private int maxTicksPerFrame = 250;
        [SerializeField] private bool mirrorVmMessagesToUnityConsole = true;

        private Orchestrator orchestrator;
        private bool running;
        private float speed = 1f;
        private double accumulatedTime;
        private readonly double simulationTickSeconds =
            (double)SConstants.deltaTime.RawValue / Constants.FixedScale;

        public Orchestrator CurrentMatch => orchestrator;
        public bool IsRunning => running;
        public float PlaybackSpeed => speed;

        private void OnEnable()
        {
            if (ui != null)
            {
                var controls = ui.Controls;
                controls.play.AddListener(Play);
                controls.pause.AddListener(Pause);
                controls.stop.AddListener(Stop);
                controls.slowMotion.AddListener(SlowMotion);
                controls.normalSpeed.AddListener(NormalSpeed);
                controls.doubleSpeed.AddListener(DoubleSpeed);
                controls.quintupleSpeed.AddListener(QuintupleSpeed);
            }

            // If the controller was temporarily disabled, restore its playback rate.
            if (orchestrator != null) ApplyPlaybackState();
        }

        private void OnDisable()
        {
            if (ui != null)
            {
                var controls = ui.Controls;
                controls.play.RemoveListener(Play);
                controls.pause.RemoveListener(Pause);
                controls.stop.RemoveListener(Stop);
                controls.slowMotion.RemoveListener(SlowMotion);
                controls.normalSpeed.RemoveListener(NormalSpeed);
                controls.doubleSpeed.RemoveListener(DoubleSpeed);
                controls.quintupleSpeed.RemoveListener(QuintupleSpeed);
            }

            // Time.timeScale is global: do not leave another scene or the editor paused/speeded up.
            Time.timeScale = 1f;
        }

        private void Start()
        {
            // Show the stopped demo world's nexuses/resources immediately in Play Mode.
            ResetDemo();
        }

        public static DScriptCompiledProgram CompileSide(string module, string source)
        {
            if (string.IsNullOrWhiteSpace(module) || string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("Each side requires a module name and nonempty DScript.");
            var provider = new InMemoryDScriptSourceProvider(
                new Dictionary<string, string> { [module] = source });
            DScriptCompilationResult compilation = DScriptCompiler.Compile(
                module, provider, SimulationDScriptProfile.Options);
            if (!compilation.Success)
                throw new InvalidOperationException("DScript '" + module + "' compilation failed:\n" +
                    string.Join("\n", compilation.Diagnostics));
            return compilation.Program;
        }

        private string PlayerSource => playerScriptAsset != null ? playerScriptAsset.text : playerSource;
        private string EnemySource => enemyScriptAsset != null ? enemyScriptAsset.text : enemySource;

        /// <summary>
        /// Compile the selected level's entry module and every reachable import using
        /// a provider rooted exclusively inside that level's Resources directory.
        /// This never reads modules from the player's workspace or other levels.
        /// </summary>
        public static DScriptCompiledProgram CompileLevelEnemy(DirectivaLevel level)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            if (string.IsNullOrWhiteSpace(level.EntryModule) ||
                string.IsNullOrWhiteSpace(level.EntryFunction))
                throw new InvalidOperationException(
                    "Level '" + level.LevelName + "' needs an entry script and function.");

            var provider = new ResourcesLevelDScriptSourceProvider(level.CodeFolder);
            DScriptCompilationResult result = DScriptCompiler.Compile(
                level.EntryModule, provider, SimulationDScriptProfile.Options);
            if (!result.Success)
                throw new InvalidOperationException("Enemy program for level '" +
                    level.LevelName + "' failed to compile:\n" +
                    string.Join("\n", result.Diagnostics));
            if (!result.Program.TryGetFunctionId(level.QualifiedEnemyEntry, out _))
                throw new InvalidOperationException("Enemy entry function '" +
                    level.QualifiedEnemyEntry + "' was not found in level '" +
                    level.LevelName + "'.");
            return result.Program;
        }

        /// <summary>Creates and attaches a symmetric demo match from Inspector settings.</summary>
        public void ResetDemo()
        {
            try
            {
                DScriptCompiledProgram first = CompileSide(playerModule, PlayerSource);
                DirectivaLevel selectedLevel = LevelLaunchContext.CurrentLevel;
                DScriptCompiledProgram second = selectedLevel != null
                    ? CompileLevelEnemy(selectedLevel)
                    : CompileSide(enemyModule, EnemySource);
                string enemyEntry = selectedLevel != null
                    ? selectedLevel.QualifiedEnemyEntry
                    : enemyModule + "." + enemyNexusFunction;
                DSimulation.Simulation world = BuildDemoWorld();
                var next = new Orchestrator(world, first, second,
                    stepsPerTick: vmStepsPerTick, maxDurationSeconds: matchDurationSeconds);
                next.BindInitialEntity(world.GetSide(1).Nexus.id,
                    playerModule + "." + playerNexusFunction);
                next.BindInitialEntity(world.GetSide(2).Nexus.id, enemyEntry);
                AttachOrchestrator(next);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                running = false;
                accumulatedTime = 0;
                if (orchestrator != null) DetachOrchestrator();
                if (view != null) view.Clear();
                ApplyPlaybackState();
            }
        }

        private DSimulation.Simulation BuildDemoWorld()
        {
            if (worldWidth < 16 || worldHeight < 16)
                throw new InvalidOperationException("Demo world must be at least 16 x 16.");
            if (initialResourcesPerSide < 0 || resourcesPerDeposit <= 0 || depositRichness <= 0)
                throw new InvalidOperationException("Invalid starting resource settings.");
            var world = new DSimulation.Simulation(unchecked((ulong)(uint)seed),
                new FixedVector2(worldWidth, worldHeight));
            world.GetSide(1).Resources = initialResourcesPerSide;
            world.GetSide(2).Resources = initialResourcesPerSide;
            world.EnqueueCreation(new SBNexus(world.GetSide(1),
                new FixedVector2(6, worldHeight / 2)));
            world.EnqueueCreation(new SBNexus(world.GetSide(2),
                new FixedVector2(worldWidth - 6, worldHeight / 2)));
            foreach (Vector2Int point in side1Deposits)
            {
                ValidateDeposit(point);
                Vector2Int mirrored = new Vector2Int(worldWidth - point.x, worldHeight - point.y);
                world.EnqueueCreation(new SBResource(world.GetSide(0),
                    new FixedVector2(point.x, point.y), 0,
                    resourcesPerDeposit, depositRichness));
                world.EnqueueCreation(new SBResource(world.GetSide(0),
                    new FixedVector2(mirrored.x, mirrored.y), 0,
                    resourcesPerDeposit, depositRichness));
            }
            return world;
        }

        private void ValidateDeposit(Vector2Int point)
        {
            if (point.x < 2 || point.y < 2 ||
                point.x > worldWidth - 2 || point.y > worldHeight - 2)
                throw new InvalidOperationException("Resource location is outside safe world bounds: " + point);
        }

        /// <summary>
        /// Replace the match with one supplied by another system (future level loader).
        /// Both nexus entry functions must already be bound by that system.
        /// </summary>
        public void AttachOrchestrator(Orchestrator match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            if (view == null) throw new InvalidOperationException("SimulationSceneView is not assigned.");
            DetachOrchestrator();
            orchestrator = match;
            orchestrator.OutputEmitted += HandleOutput;
            orchestrator.MatchEnded += HandleMatchEnded;
            // Shared initializers ran in the constructor before we could subscribe.
            // Preserve those early messages when mirroring output to Unity's console.
            if (mirrorVmMessagesToUnityConsole)
            {
                var initial = new List<VMOutputMessage>();
                initial.AddRange(orchestrator.GetOutput(1));
                initial.AddRange(orchestrator.GetOutput(2));
                initial.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
                foreach (VMOutputMessage message in initial) HandleOutput(message);
            }
            accumulatedTime = 0;
            running = false;
            view.Attach(orchestrator.Simulation, worldWidth, worldHeight);
            ApplyPlaybackState();
        }

        private void DetachOrchestrator()
        {
            if (orchestrator == null) return;
            orchestrator.OutputEmitted -= HandleOutput;
            orchestrator.MatchEnded -= HandleMatchEnded;
            orchestrator = null;
        }

        public void Play()
        {
            if (orchestrator == null || orchestrator.IsFinished)
                ResetDemo();
            if (orchestrator == null) return;
            running = true;
            ApplyPlaybackState();
        }

        public void Pause()
        {
            running = false;
            ApplyPlaybackState();
        }

        /// <summary>Stop resets the match and shows its initial state, without running scripts.</summary>
        public void Stop()
        {
            running = false;
            // Freeze visuals immediately, even if compiling/rebuilding the demo fails.
            ApplyPlaybackState();
            ResetDemo();
        }

        public void SlowMotion() => SetSpeed(0.25f);
        public void NormalSpeed() => SetSpeed(1f);
        public void DoubleSpeed() => SetSpeed(2f);
        public void QuintupleSpeed() => SetSpeed(5f);

        private void SetSpeed(float newSpeed)
        {
            speed = newSpeed;
            ApplyPlaybackState();
        }

        /// <summary>
        /// Update the visual clock and interpolation together. Backend ticks use
        /// unscaledDeltaTime * speed (below), so this must not scale them again.
        /// </summary>
        private void ApplyPlaybackState()
        {
            float rate = running ? speed : 0f;
            Time.timeScale = rate;
            if (view != null) view.SetPlaybackSpeed(rate);
        }

        private void Update()
        {
            if (!running || orchestrator == null || orchestrator.IsFinished) return;
            accumulatedTime += Time.unscaledDeltaTime * speed;
            int steps = 0;
            try
            {
                // Keep any backlog rather than skipping physics steps on a slow frame.
                while (accumulatedTime >= simulationTickSeconds && steps < maxTicksPerFrame)
                {
                    accumulatedTime -= simulationTickSeconds;
                    orchestrator.Update();
                    view.ConsumeEvents();
                    steps++;
                    if (orchestrator.IsFinished) break;
                }
            }
            catch (Exception exception)
            {
                running = false;
                ApplyPlaybackState();
                Debug.LogException(exception, this);
            }
        }

        private void HandleMatchEnded(MatchOutcome outcome)
        {
            running = false;
            // Stop backend ticks, but let the last attack/death particles finish.
            // ConsumeEvents() runs after this callback and may spawn the final effect.
            // Pause/Stop explicitly freeze the visual clock if requested.
            if (view != null) view.SetPlaybackSpeed(speed);
            Debug.Log("[Directiva] Match ended: " + outcome + " (" +
                orchestrator.EndReason + ") at tick " + orchestrator.Simulation.Tick, this);
        }

        private void HandleOutput(VMOutputMessage output)
        {
            if (!mirrorVmMessagesToUnityConsole) return;
            string line = "[Directiva Side " + output.SideId + " / Entity " +
                (output.EntityId.HasValue ? output.EntityId.Value.ToString() : "shared") +
                " / Tick " + output.Tick + "] " + output.Text;
            switch (output.Kind)
            {
                case VMOutputKind.Warning: Debug.LogWarning(line, this); break;
                case VMOutputKind.Error: Debug.LogError(line, this); break;
                default: Debug.Log(line, this); break;
            }
        }

        private void OnDestroy()
        {
            Time.timeScale = 1f;
            DetachOrchestrator();
        }
    }
}
