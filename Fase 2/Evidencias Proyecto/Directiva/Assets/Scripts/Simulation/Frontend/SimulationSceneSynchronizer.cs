using System;
using System.Collections.Generic;
using System.IO;
using DMath;
using DOrchestration;
using DSimulation;
using DSCompilation.DScript;
using DSCompilation.DScript.Backend;
using DSCompilation.DScript.Modules;
using Directiva.SimulationUI;
using Directiva.MainMenu;
using Directiva.Levels;
using Directiva.Progress;
using Directiva.Localization;
using Directiva.CodeUI;
using DSExecution.VirtualMachine;
using UnityEngine;

namespace Directiva.SimulationFrontend
{
    /// <summary>
    /// Hosts a selected level and the player's explicitly selected saved DScript.
    /// No Inspector script may ever silently run as the player's program.
    /// </summary>
    public sealed class SimulationSceneSynchronizer : MonoBehaviour, ISimulationDebugSession
    {
        [Header("Scene connections")]
        [SerializeField] private SimulationUIController ui;
        [SerializeField] private SimulationSceneView view;

        [Header("World (50 x 50)")]
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

        [Header("Execution")]
        [SerializeField, Min(1)] private int vmStepsPerTick = 10;
        // Fallback for a scene without a selected level; levels override this setting.
        [SerializeField, Min(1)] private int matchDurationSeconds = 300;
        [SerializeField, Min(1)] private int maxTicksPerFrame = 250;
        [SerializeField] private bool mirrorVmMessagesToUnityConsole = true;

        // In-memory selection, retained for this scene's lifetime across Stop/Play.
        // This is deliberately NOT the code editor's working buffer.
        private string selectedPlayerScript;
        private string selectedPlayerFunction;
        // A selection made while stopped takes effect on the next Play.
        private bool pendingPlayerProgram;
        // A paused/finished match remains locked until the player presses Stop.
        private bool programSelectionLocked;
        private Orchestrator orchestrator;
        private SimulationWorldPointerController pointer;
        private DirectivaCodeUIController connectedEditor;
        private int selectedDebugEntityId;
        private readonly List<VMOutputMessage> pendingEditorMessages = new List<VMOutputMessage>();
        private bool running;
        private float speed = 1f;
        private double accumulatedTime;
        private readonly double simulationTickSeconds =
            (double)SConstants.deltaTime.RawValue / Constants.FixedScale;

        public Orchestrator CurrentMatch => orchestrator;
        public bool IsRunning => running;
        public bool IsProgramSelectionLocked => programSelectionLocked;
        public float PlaybackSpeed => speed;
        public string PlayerScriptsRoot => Path.Combine(Application.persistentDataPath, "DirectivaScripts");
        public string SelectedPlayerScript => selectedPlayerScript;
        public string SelectedPlayerFunction => selectedPlayerFunction;
        // A missing program is a normal, visible state until the player chooses one.
        public ScriptExecutionState State => orchestrator == null ||
            !programSelectionLocked || orchestrator.IsFinished ? ScriptExecutionState.Stopped :
            running ? ScriptExecutionState.Running : ScriptExecutionState.Paused;
        public bool HasSelectedVM => selectedDebugEntityId > 0 && orchestrator != null &&
            orchestrator.TryGetVMStatus(selectedDebugEntityId, out _);
        public long TotalLogicalSteps => orchestrator?.TotalVmSteps ?? 0L;
        public bool HasSelectedPlayerProgram =>
            !string.IsNullOrWhiteSpace(selectedPlayerScript) &&
            !string.IsNullOrWhiteSpace(selectedPlayerFunction);

        private void Awake()
        {
            // Add the selection system at runtime; existing scenes and prefabs do
            // not need new serialized component references or GUID changes.
            var selection = GetComponent<SimulationNexusProgramController>();
            if (selection == null) selection = gameObject.AddComponent<SimulationNexusProgramController>();
            selection.Initialize(this, view, ui);
            pointer = GetComponent<SimulationWorldPointerController>();
        }

        /// <summary>
        /// Changes the selected saved program only while stopped. The choice
        /// takes effect on Play; neither playback nor pause permits editing.
        /// Invalid code never replaces the previously selected program.
        /// </summary>
        public bool TryAssignPlayerNexusProgram(string relativeScript, string entryFunction, out string error)
        {
            return TryAssignPlayerNexusProgram(relativeScript, entryFunction, out error, out _);
        }

        /// <summary>
        /// Preserve structured source diagnostics even when compilation fails.
        /// Existing callers keep using the three-argument overload.
        /// </summary>
        public bool TryAssignPlayerNexusProgram(string relativeScript, string entryFunction,
            out string error, out IReadOnlyList<DScriptDiagnostic> diagnostics)
        {
            diagnostics = Array.Empty<DScriptDiagnostic>();
            // Enforce the same restriction at the backend boundary: disabling a
            // DropdownField alone would not prevent assignments from other callers.
            if (programSelectionLocked)
            {
                error = DirectivaLocalization.Get("Simulation", "nexus_locked_warning");
                return false;
            }

            try
            {
                if (string.Equals(selectedPlayerScript, relativeScript, StringComparison.Ordinal) &&
                    string.Equals(selectedPlayerFunction, entryFunction, StringComparison.Ordinal))
                {
                    error = null;
                    return true;
                }

                // Compile first: an invalid choice must not replace the current
                // selection or alter the stopped world preview.
                PlayerNexusScriptCatalog.Compile(PlayerScriptsRoot, relativeScript, entryFunction);
                selectedPlayerScript = relativeScript;
                selectedPlayerFunction = entryFunction;
                pendingPlayerProgram = true;

                error = null;
                return true;
            }
            catch (PlayerNexusCompilationException exception)
            {
                error = exception.Message;
                diagnostics = exception.Diagnostics;
                return false;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

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
            // Show the stopped level world before any player program has been assigned.
            ResetMatch();
            ConnectEditorDebugger();
        }

        private void ConnectEditorDebugger()
        {
            if (connectedEditor != null || ui == null || ui.CodeEditor == null)
                return;
            connectedEditor = ui.CodeEditor;
            connectedEditor.AttachSimulationDebugger(this);
            connectedEditor.SavedScriptsChanged += OnPlayerScriptsSaved;
            foreach (VMOutputMessage message in pendingEditorMessages)
                connectedEditor.PublishSimulationOutput(message);
            pendingEditorMessages.Clear();
            if (pointer == null) pointer = GetComponent<SimulationWorldPointerController>();
            if (pointer != null) pointer.SelectionChanged += OnEntitySelectedForDebug;
            if (orchestrator != null) ConfigureRuntimeDebugger(orchestrator);
        }

        /// <summary>
        /// A saved change to ANY player module can affect the selected Nexus
        /// program through imports. Recompile on the next fresh match; do not
        /// replace the VMs of a running or paused match mid-execution.
        /// </summary>
        private void OnPlayerScriptsSaved()
        {
            if (HasSelectedPlayerProgram)
                pendingPlayerProgram = true;
        }

        private void OnEntitySelectedForDebug(SimulationEntityIdentity identity)
        {
            // The enemy script lives in the level's private Resources module,
            // not in the player's editable workspace. Never display it as if editable.
            selectedDebugEntityId = identity != null && identity.SideId == 1 ? identity.EntityId : 0;
            connectedEditor?.RefreshSimulationDebugger(followExecution: true);
        }

        private void ConfigureRuntimeDebugger(Orchestrator match)
        {
            match.BreakpointPredicate = (side, id, source, line) =>
                side == 1 && connectedEditor != null &&
                connectedEditor.HasSimulationBreakpoint(source, line);
        }

        private void OnBreakpointHit(int entityId, VMDebugState debug)
        {
            selectedDebugEntityId = entityId;
            if (pointer != null && view != null &&
                view.TryGetVisual(entityId, out VisualEntity selectedVisual))
                pointer.SelectForDebug(selectedVisual.GetComponent<SimulationEntityIdentity>());
            running = false;
            accumulatedTime = 0;
            ApplyPlaybackState();
            if (connectedEditor != null && debug != null)
            {
                var source = debug.Line >= 0 && !string.IsNullOrWhiteSpace(debug.File)
                    ? new OutputSourceLocation(debug.File, debug.Line + 1) :
                        (OutputSourceLocation?)null;
                connectedEditor.Output.Write(DirectivaLocalization.Get("Simulation",
                    "debug_breakpoint_hit", entityId,
                    debug.File ?? "?", debug.Line + 1), source);
                connectedEditor.RefreshSimulationDebugger(followExecution: true);
            }
        }

        private void OnEntityFaulted(int entityId) => view?.SetEntityFaulted(entityId, true);
        private void OnEntityRecovered(int entityId) => view?.SetEntityFaulted(entityId, false);

        public bool TryGetSelectedSource(out string file, out int oneBasedLine)
        {
            if (HasSelectedVM && orchestrator.TryGetDebugState(selectedDebugEntityId,
                out VMDebugState debug) && debug.Line >= 0 &&
                !string.IsNullOrWhiteSpace(debug.File))
            {
                file = debug.File;
                oneBasedLine = debug.Line + 1;
                return true;
            }
            file = null;
            oneBasedLine = 0;
            return false;
        }

        /// <summary>Advance only the chosen VM while simulation time is frozen.</summary>
        public void Step()
        {
            if (orchestrator == null || !HasSelectedVM || !programSelectionLocked ||
                orchestrator.IsFinished) return;
            if (running) Pause();
            if (orchestrator.StepEntity(selectedDebugEntityId))
            {
                view?.ConsumeEvents();
                connectedEditor?.RefreshSimulationDebugger(followExecution: true);
            }
        }

        public void Continue() => Play();

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

        /// <summary>
        /// Recreate the level's initial world whether or not the player has picked
        /// a saved script. Never bind a Nexus VM without an explicit selection.
        /// Compilation failures also retain a visible stopped world.
        /// </summary>
        public void ResetMatch()
        {
            DSimulation.Simulation world = null;
            try
            {
                world = BuildWorld();

                // At level entry neither dropdown has a value. Keep the world
                // selectable, but do not construct a hidden fallback program.
                DirectivaLevel level = LevelLaunchContext.CurrentLevel;
                if (!HasSelectedPlayerProgram || level == null)
                {
                    AttachStoppedWorld(world);
                    return;
                }

                // Each side compiles from its own isolated source root.
                DScriptCompiledProgram player = PlayerNexusScriptCatalog.Compile(
                    PlayerScriptsRoot, selectedPlayerScript, selectedPlayerFunction);
                DScriptCompiledProgram enemy = CompileLevelEnemy(level);
                var next = new Orchestrator(world, player, enemy,
                    stepsPerTick: vmStepsPerTick, maxDurationSeconds: level.MaxMatchDurationSeconds);
                next.BindInitialEntity(world.GetSide(1).Nexus.id,
                    PlayerNexusScriptCatalog.ModuleFromPath(selectedPlayerScript) + "." +
                    selectedPlayerFunction);
                next.BindInitialEntity(world.GetSide(2).Nexus.id, level.QualifiedEnemyEntry);
                AttachOrchestrator(next);
                pendingPlayerProgram = false;
            }
            catch (Exception exception)
            {
                // A program can change on disk after it was selected. Make
                // failures during Play/Stop visible in the editor too.
                if (exception is PlayerNexusCompilationException compilationError)
                    ui?.CodeEditor?.PublishPlayerCompilationFailure(
                        compilationError.Message, compilationError.Diagnostics);
                else
                    ui?.CodeEditor?.PublishPlayerCompilationFailure(exception.Message, null);
                Debug.LogException(exception, this);
                running = false;
                accumulatedTime = 0;
                DetachOrchestrator();

                if (world != null && view != null)
                {
                    try { AttachStoppedWorld(world); }
                    catch (Exception viewException)
                    {
                        Debug.LogException(viewException, this);
                        view.Clear();
                    }
                }
                else if (view != null)
                {
                    view.Clear();
                }
                ApplyPlaybackState();
            }
        }

        private void AttachStoppedWorld(DSimulation.Simulation world)
        {
            if (view == null)
                throw new InvalidOperationException("SimulationSceneView is not assigned.");
            running = false;
            accumulatedTime = 0;
            DetachOrchestrator();
            world.Start(); // Idempotent if a partially built Orchestrator already started it.
            view.Attach(world, worldWidth, worldHeight,
                ui != null ? ui.GetComponent<UnityEngine.UIElements.UIDocument>() : null);
            pendingPlayerProgram = false;
            ApplyPlaybackState();
        }

        private DSimulation.Simulation BuildWorld()
        {
            if (worldWidth < 16 || worldHeight < 16)
                throw new InvalidOperationException("World must be at least 16 x 16.");
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
            orchestrator.BreakpointHit += OnBreakpointHit;
            orchestrator.EntityFaulted += OnEntityFaulted;
            orchestrator.EntityRecovered += OnEntityRecovered;
            ConfigureRuntimeDebugger(orchestrator);
            // Initializers emitted messages before the host could subscribe.
            // Replay them to BOTH output sinks, not only Unity's console.
            var initial = new List<VMOutputMessage>();
            initial.AddRange(orchestrator.GetOutput(1));
            initial.AddRange(orchestrator.GetOutput(2));
            initial.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
            foreach (VMOutputMessage message in initial) HandleOutput(message);
            accumulatedTime = 0;
            running = false;
            view.Attach(orchestrator.Simulation, worldWidth, worldHeight,
                ui != null ? ui.GetComponent<UnityEngine.UIElements.UIDocument>() : null);
            ApplyPlaybackState();
        }

        private void DetachOrchestrator()
        {
            selectedDebugEntityId = 0;
            pointer?.ClearSelection();
            if (orchestrator == null) return;
            orchestrator.OutputEmitted -= HandleOutput;
            orchestrator.MatchEnded -= HandleMatchEnded;
            orchestrator.BreakpointHit -= OnBreakpointHit;
            orchestrator.EntityFaulted -= OnEntityFaulted;
            orchestrator.EntityRecovered -= OnEntityRecovered;
            orchestrator.BreakpointPredicate = null;
            orchestrator = null;
        }

        public void Play()
        {
            if (!HasSelectedPlayerProgram)
            {
                WarnCannotPlay("nexus_select_before_play");
                return;
            }
            if (LevelLaunchContext.CurrentLevel == null)
            {
                WarnCannotPlay("nexus_level_required");
                return;
            }
            // Saving while paused must NOT restart the ongoing match. Stop
            // unlocks selection and rebuilds from the latest saved sources.
            if ((!programSelectionLocked && pendingPlayerProgram) ||
                orchestrator == null || orchestrator.IsFinished)
                ResetMatch();
            if (orchestrator == null) return;
            orchestrator.ResumeFromBreakpoint();
            running = true;
            programSelectionLocked = true;
            SyncProgramPickerLock();
            ApplyPlaybackState();
        }

        private void WarnCannotPlay(string localizationKey)
        {
            if (ui != null && ui.NexusPicker != null)
                ui.NexusPicker.SetLocalizedStatus(localizationKey, true);
            Debug.LogWarning("[Directiva] " +
                DirectivaLocalization.Get("Simulation", localizationKey), this);
            ui?.SetPlaybackFromRuntime(State);
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
            programSelectionLocked = false;
            SyncProgramPickerLock();
            // Freeze visuals immediately, even if recompilation fails.
            ApplyPlaybackState();
            ResetMatch();
        }

        private void SyncProgramPickerLock()
        {
            if (ui != null && ui.NexusPicker != null)
                ui.NexusPicker.SetSelectionLocked(programSelectionLocked);
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
            ui?.SetPlaybackFromRuntime(State);
        }

        private void Update()
        {
            if (connectedEditor == null) ConnectEditorDebugger();
            if (!running || orchestrator == null || orchestrator.IsFinished) return;
            accumulatedTime += Time.unscaledDeltaTime * speed;
            int steps = 0;
            try
            {
                // Keep any backlog rather than skipping physics steps on a slow frame.
                while ((accumulatedTime >= simulationTickSeconds || orchestrator.HasPendingTick) &&
                    steps < maxTicksPerFrame)
                {
                    // A debugger resumes the SAME partially executed tick. Its
                    // time budget was already removed before the breakpoint.
                    if (!orchestrator.HasPendingTick)
                        accumulatedTime -= simulationTickSeconds;
                    orchestrator.Update();
                    view.ConsumeEvents();
                    steps++;
                    if (orchestrator.IsPausedAtBreakpoint)
                    {
                        running = false;
                        accumulatedTime = 0;
                        ApplyPlaybackState();
                        break;
                    }
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

        private void LateUpdate()
        {
            if (ui != null && ui.MatchStats != null)
                ui.MatchStats.UpdateValues(orchestrator != null ? orchestrator.Simulation : view?.CurrentWorld,
                    orchestrator, orchestrator != null ? orchestrator.MaxDurationSeconds :
                        (LevelLaunchContext.CurrentLevel != null
                            ? LevelLaunchContext.CurrentLevel.MaxMatchDurationSeconds
                            : matchDurationSeconds));
        }

        private void HandleMatchEnded(MatchOutcome outcome)
        {
            // Only a genuine side-1 victory awards stars. Stop/Restart, draws and
            // losses do not write progress. The callback is invoked once per match.
            DirectivaLevel level = LevelLaunchContext.CurrentLevel;
            if (level != null && level.LevelNumber > 0 && orchestrator != null &&
                outcome == MatchOutcome.Side1Won)
            {
                SBNexus playerNexus = orchestrator.Simulation.GetSide(1).Nexus;
                LevelStarFlags achieved = LevelStarRules.Evaluate(outcome,
                    playerNexus == null || playerNexus.HasTakenHealthDamage,
                    orchestrator.Simulation.Time, level.FastVictorySeconds);
                if (LevelProgress.Current.TryAwardStars(level.LevelNumber, achieved,
                    out LevelStarFlags newlyEarned) && newlyEarned != LevelStarFlags.None)
                    Debug.Log("[Directiva] Level " + level.LevelNumber +
                        " earned new stars: " + newlyEarned +
                        " (total: " + LevelStarRules.Count(
                            LevelProgress.Current.GetStars(level.LevelNumber)) + "/3).", this);
            }
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
            // The embedded editor is the player's primary console. Unity's
            // console mirroring is only an optional developer diagnostic.
            if (connectedEditor == null) ConnectEditorDebugger();
            if (connectedEditor != null)
                connectedEditor.PublishSimulationOutput(output);
            else
                pendingEditorMessages.Add(output);
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
            if (pointer != null) pointer.SelectionChanged -= OnEntitySelectedForDebug;
            if (connectedEditor != null)
            {
                connectedEditor.SavedScriptsChanged -= OnPlayerScriptsSaved;
                connectedEditor.AttachSimulationDebugger(null);
            }
            DetachOrchestrator();
        }
    }
}
