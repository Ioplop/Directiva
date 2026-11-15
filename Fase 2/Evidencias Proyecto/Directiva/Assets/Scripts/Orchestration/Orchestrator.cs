using System;
using System.Collections.Generic;
using DSCompilation.DScript.Backend;
using DSExecution.Errors;
using DMath;
using DSExecution.VirtualMachine;
using DSimulation;

namespace DOrchestration
{
    /// <summary>A match ends when either nexus dies; both dead in the same step is a draw.</summary>
    public enum MatchOutcome
    {
        InProgress,
        Side1Won,
        Side2Won,
        Draw
    }

    /// <summary>Why the match finished; independent of who won.</summary>
    public enum MatchEndReason
    {
        None,
        NexusDestroyed,
        TimeLimit
    }

    /// <summary>
    /// Runs one deterministic simulation tick per Update. All live entity VMs take
    /// exactly one logical step per round in global entity-ID order. The simulation
    /// updates only after all rounds finish; newly built units run next tick.
    /// This is pure backend code and does not depend on Unity or the level editor.
    /// </summary>
    public sealed class Orchestrator
    {
        // Prevent malformed metadata-only loops from monopolizing a scheduler round.
        private const int MaxZeroCostAdvancesPerTurn = 16384;
        private const ulong SideSeedSalt = 0x9E3779B97F4A7C15UL;
        private readonly Simulation simulation;
        private readonly SideRuntime firstSide;
        private readonly SideRuntime secondSide;
        private readonly Queue<VMOutputMessage>[] outputBySide;
        private readonly long[] errorCounts = new long[3];
        private readonly int outputCapacityPerSide;
        private int stepsPerTick;
        private long outputSequence;
        private bool hasAdvanced;
        private readonly long startTick;
        private readonly long faultRestartDelayTicks;
        private List<EntityVMBinding> scheduledTick;
        private int nextRound;
        private int nextBinding;
        private int roundsInScheduledTick;
        private VMSourceStepPlan sourceStep;
        private int sourceStepEntityId;
        // Unlike the old StepEntity implementation, this is a REQUEST to the
        // normal scheduler, never an out-of-turn call to DirectivaVM.Advance().
        private int instructionStepEntityId;

        /// <summary>Debugging stops all VMs before the matching source instruction and
        /// leaves the simulation tick unfinished until Continue is requested.</summary>
        public bool IsPausedAtBreakpoint { get; private set; }
        public bool HasPendingTick => scheduledTick != null;
        public int BreakpointEntityId { get; private set; }
        public Func<int, int, string, int, bool> BreakpointPredicate { get; set; }
        public event Action<int, VMDebugState> BreakpointHit;
        public event Action<int> EntityFaulted;
        public event Action<int> EntityRecovered;
        public long FaultRestartDelayTicks => faultRestartDelayTicks;

        /// <summary>Maximum number of simulated ticks, counted from this match's start.</summary>
        public long MaxSimulationTicks { get; }
        /// <summary>Configured simulated duration (not wall-clock time), in whole seconds.</summary>
        public int MaxDurationSeconds { get; }
        public MatchEndReason EndReason { get; private set; } = MatchEndReason.None;

        public Simulation Simulation => simulation;
        public MatchOutcome Outcome { get; private set; } = MatchOutcome.InProgress;
        public bool IsFinished => Outcome != MatchOutcome.InProgress;
        public long? FinishedAtTick { get; private set; }
        public long TotalVmSteps { get; private set; }
        public int OutputCapacityPerSide => outputCapacityPerSide;

        /// <summary>Charged heap usage in logical Directiva units for one player side.</summary>
        public ulong GetHeapUsage(int sideId) => GetSide(sideId).Memory.Heap.AllocatedSize;

        /// <summary>Total execution errors since this match began, even after log eviction.</summary>
        public long GetErrorCount(int sideId) => errorCounts[GetSide(sideId).SideId];

        /// <summary>Effective VM instructions per entity per simulation tick (default 10).</summary>
        public int StepsPerTick
        {
            get => stepsPerTick;
            set
            {
                if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
                stepsPerTick = value;
            }
        }

        public event Action<VMOutputMessage> OutputEmitted;
        public event Action<MatchOutcome> MatchEnded;

        /// <summary>
        /// Start from an already designed world: enqueue the starting entities before
        /// constructing the orchestrator. Each side receives its own compiled program,
        /// heap, globals and random state. Attach entry functions with BindInitialEntity
        /// before the first Update; no file paths or level names are assumed here.
        /// </summary>
        public Orchestrator(Simulation simulation,
            DScriptCompiledProgram side1Program, DScriptCompiledProgram side2Program,
            int stepsPerTick = 10, int outputCapacityPerSide = 4096,
            VMOptions vmOptions = null, int initializerBudget = 100000,
            int maxDurationSeconds = 300, int faultRestartDelaySeconds = 2)
        {
            this.simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
            if (side1Program == null) throw new ArgumentNullException(nameof(side1Program));
            if (side2Program == null) throw new ArgumentNullException(nameof(side2Program));
            if (outputCapacityPerSide <= 0)
                throw new ArgumentOutOfRangeException(nameof(outputCapacityPerSide));
            if (initializerBudget <= 0)
                throw new ArgumentOutOfRangeException(nameof(initializerBudget));
            if (maxDurationSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxDurationSeconds));
            if (faultRestartDelaySeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(faultRestartDelaySeconds));
            if (simulation.Parameters.DeltaTime.RawValue <= 0)
                throw new InvalidOperationException("Simulation tick duration must be positive.");
            MaxDurationSeconds = maxDurationSeconds;
            faultRestartDelayTicks = checked((long)faultRestartDelaySeconds * Constants.FixedScale +
                simulation.Parameters.DeltaTime.RawValue - 1) / simulation.Parameters.DeltaTime.RawValue;
            // Round up instead of ending before the requested simulated duration.
            long durationRaw = checked((long)maxDurationSeconds * Constants.FixedScale);
            MaxSimulationTicks = checked(durationRaw + simulation.Parameters.DeltaTime.RawValue - 1) /
                simulation.Parameters.DeltaTime.RawValue;
            StepsPerTick = stepsPerTick;
            this.outputCapacityPerSide = outputCapacityPerSide;
            outputBySide = new Queue<VMOutputMessage>[3];
            outputBySide[1] = new Queue<VMOutputMessage>();
            outputBySide[2] = new Queue<VMOutputMessage>();

            simulation.Start();
            startTick = simulation.Tick;
            if (simulation.GetSide(1).Nexus == null || simulation.GetSide(2).Nexus == null ||
                !simulation.GetSide(1).Nexus.alive || !simulation.GetSide(2).Nexus.alive)
                throw new InvalidOperationException(
                    "Both player sides must have a living nexus before the match starts.");

            // Derive independent but reproducible streams from the match seed.
            firstSide = new SideRuntime(simulation, 1, side1Program,
                unchecked(simulation.seed ^ SideSeedSalt), vmOptions, initializerBudget,
                (entity, kind, text, debug, error, key, args) =>
                    RecordOutput(1, entity, kind, text, debug, error, key, args));
            secondSide = new SideRuntime(simulation, 2, side2Program,
                unchecked(simulation.seed ^ (SideSeedSalt * 2UL)), vmOptions, initializerBudget,
                (entity, kind, text, debug, error, key, args) =>
                    RecordOutput(2, entity, kind, text, debug, error, key, args));
        }

        /// <summary>
        /// Assign the entry function of a starting unit or nexus, using its qualified
        /// module.function name. This explicit bootstrap will later be supplied by a
        /// level definition, without changing simulation or orchestration code.
        /// </summary>
        public void BindInitialEntity(int entityId, string qualifiedFunction)
        {
            if (hasAdvanced || IsFinished)
                throw new InvalidOperationException("Starting entities must be bound before the first tick.");
            if (string.IsNullOrWhiteSpace(qualifiedFunction))
                throw new ArgumentException("A qualified function name is required.", nameof(qualifiedFunction));
            if (!simulation.TryGetEntity(entityId, out SBEntity entity))
                throw new ArgumentException("Entity is not registered in this simulation.", nameof(entityId));
            SideRuntime side = GetSide(entity.side.id);
            if (!side.Program.TryGetFunctionId(qualifiedFunction, out int entryFunctionId))
                throw new ArgumentException("Entry function not found: " + qualifiedFunction,
                    nameof(qualifiedFunction));
            side.BindEntity(entityId, entryFunctionId);
        }

        /// <summary>Number of entity VMs currently bound to one player side.</summary>
        public int BindingCount(int sideId)
        {
            int count = 0;
            foreach (EntityVMBinding _ in GetSide(sideId).Bindings) count++;
            return count;
        }

        /// <summary>Expose status for diagnostics without granting access to VM internals.</summary>
        public bool TryGetVMStatus(int entityId, out VMExecutionStatus status)
        {
            if (firstSide.TryGetBinding(entityId, out EntityVMBinding first))
            {
                status = first.VM.Status;
                return true;
            }
            if (secondSide.TryGetBinding(entityId, out EntityVMBinding second))
            {
                status = second.VM.Status;
                return true;
            }
            status = default;
            return false;
        }

        /// <summary>
        /// Snapshot a side's messages in deterministic order, optionally filtering by
        /// entity and/or severity. A null entity filter includes team bootstrap messages.
        /// Logs survive VM restarts and entity deaths until their bounded buffer expires.
        /// </summary>
        public IReadOnlyList<VMOutputMessage> GetOutput(int sideId,
            int? entityId = null, VMOutputKind? kind = null)
        {
            var result = new List<VMOutputMessage>();
            foreach (VMOutputMessage message in outputBySide[GetSide(sideId).SideId])
                if ((!entityId.HasValue || message.EntityId == entityId) &&
                    (!kind.HasValue || message.Kind == kind))
                    result.Add(message);
            return result;
        }

        /// <summary>
        /// Advance one 50 Hz tick. A breakpoint can suspend the scheduler midway
        /// through the tick without running physics or replaying earlier VM turns.
        /// Without breakpoints this retains exactly the original round-robin order.
        /// </summary>
        public void Update()
        {
            if (IsFinished || IsPausedAtBreakpoint) return;
            hasAdvanced = true;
            if (scheduledTick == null)
            {
                EvaluateOutcome();
                if (IsFinished) return;
                RecoverFaultedBindings(firstSide);
                RecoverFaultedBindings(secondSide);
                scheduledTick = new List<EntityVMBinding>();
                scheduledTick.AddRange(firstSide.Bindings);
                scheduledTick.AddRange(secondSide.Bindings);
                scheduledTick.Sort((a, b) => a.EntityId.CompareTo(b.EntityId));
                nextRound = 0;
                nextBinding = 0;
                roundsInScheduledTick = StepsPerTick;
            }

            for (; nextRound < roundsInScheduledTick; nextRound++)
            {
                while (nextBinding < scheduledTick.Count)
                {
                    EntityVMBinding binding = scheduledTick[nextBinding];
                    if (IsLivingBinding(binding) && binding.VM.Status != VMExecutionStatus.Faulted)
                    {
                        if (binding.VM.Status == VMExecutionStatus.Completed)
                        {
                            binding.VM.RestartCompleted();
                            binding.ResetSourceLine();
                        }
                        if (AdvanceOneLogicalStep(binding, checkBreakpoints: true,
                            consumeTrailingMetadata: false, consumedTurn: out bool consumedTurn))
                        {
                            // A source-level step stops AFTER its instruction. A
                            // normal breakpoint stops BEFORE it; only the latter
                            // must retain the selected scheduler turn on resume.
                            if (consumedTurn) nextBinding++;
                            return;
                        }
                    }
                    nextBinding++;
                }
                nextBinding = 0;
            }

            scheduledTick = null;
            simulation.Update();
            EvaluateOutcome();
            if (!IsFinished && simulation.Tick - startTick >= MaxSimulationTicks)
                Finish(MatchOutcome.Draw, MatchEndReason.TimeLimit);
            CompleteBuilds();
            RemoveDeadBindings();
            // Either kind of debug step can span multiple scheduler rounds and
            // physics ticks. A dying target must not leave the debugger running
            // forever while waiting for an instruction that can never execute.
            int steppingEntityId = sourceStep != null
                ? sourceStepEntityId : instructionStepEntityId;
            if (steppingEntityId != 0 &&
                (!TryGetBinding(steppingEntityId, out EntityVMBinding target) ||
                 !IsLivingBinding(target)))
            {
                CancelSourceStep();
                if (!IsFinished)
                {
                    IsPausedAtBreakpoint = true;
                    BreakpointEntityId = steppingEntityId;
                }
            }
        }

        /// <summary>Continue a paused source breakpoint without discarding its tick.</summary>
        public void ResumeFromBreakpoint()
        {
            CancelSourceStep();
            IsPausedAtBreakpoint = false;
            BreakpointEntityId = 0;
        }

        /// <summary>
        /// Schedule a line-level step without bypassing VM fairness or physics.
        /// The regular Update loop advances the entire match until this VM reaches
        /// the requested source boundary. No unbounded work occurs in the UI click.
        /// </summary>
        public bool BeginSourceStep(int entityId, VMSourceStepMode mode)
        {
            if (IsFinished || entityId <= 0 ||
                !TryGetBinding(entityId, out EntityVMBinding binding) ||
                !IsLivingBinding(binding) || binding.VM.Status == VMExecutionStatus.Faulted)
                return false;

            if (binding.VM.Status == VMExecutionStatus.Completed)
            {
                binding.VM.RestartCompleted();
                binding.ResetSourceLine();
            }

            ConsumeDebugMetadata(binding.VM);
            if (binding.VM.Status != VMExecutionStatus.Running)
                return false;
            CancelSourceStep();
            sourceStep = new VMSourceStepPlan(binding.VM, mode);
            sourceStepEntityId = entityId;
            IsPausedAtBreakpoint = false;
            BreakpointEntityId = 0;
            return true;
        }

        /// <summary>
        /// Cancel a pending source-line or single-instruction debug request.
        /// Pause, Stop, Continue and breakpoints must all discard these requests.
        /// </summary>
        public void CancelSourceStep()
        {
            sourceStep = null;
            sourceStepEntityId = 0;
            instructionStepEntityId = 0;
        }

        /// <summary>
        /// Request one charged instruction of the selected VM, WITHOUT executing it
        /// outside the deterministic scheduler. Update() runs all intervening VM
        /// turns and world ticks normally, then pauses just after the target VM's
        /// next scheduled instruction. Calling this method does no VM work.
        /// </summary>
        public bool BeginInstructionStep(int entityId)
        {
            if (IsFinished || entityId <= 0 ||
                !TryGetBinding(entityId, out EntityVMBinding binding) ||
                !IsLivingBinding(binding) || binding.VM.Status == VMExecutionStatus.Faulted)
                return false;

            // A completed VM is restarted at its scheduled turn by Update(), not
            // ahead of its neighbors merely because the debugger selected it.
            CancelSourceStep();
            instructionStepEntityId = entityId;
            IsPausedAtBreakpoint = false;
            BreakpointEntityId = 0;
            return true;
        }

        /// <summary>Compatibility entry point; stepping is now asynchronous and fair.</summary>
        public bool StepEntity(int entityId) => BeginInstructionStep(entityId);

        public bool TryGetDebugState(int entityId, out VMDebugState debug)
        {
            if (TryGetBinding(entityId, out EntityVMBinding binding))
            {
                debug = binding.VM.Debug.Snapshot();
                return true;
            }
            debug = null;
            return false;
        }

        /// <summary>
        /// Exposes the selected entity's exact compiled code and VM for read-only
        /// debugger inspection. Never call Advance() here: scheduling remains owned
        /// exclusively by Update().
        /// </summary>
        public bool TryGetDebugInspection(int entityId, out DirectivaVM vm,
            out DScriptCompiledProgram program)
        {
            if (TryGetBinding(entityId, out EntityVMBinding binding))
            {
                vm = binding.VM;
                program = GetSide(binding.SideId).Program;
                return true;
            }
            vm = null;
            program = null;
            return false;
        }

        private bool TryGetBinding(int entityId, out EntityVMBinding binding) =>
            firstSide.TryGetBinding(entityId, out binding) ||
            secondSide.TryGetBinding(entityId, out binding);

        private void RecoverFaultedBindings(SideRuntime side)
        {
            foreach (EntityVMBinding binding in side.Bindings)
            {
                if (binding.RecoverAtTick < 0 || simulation.Tick < binding.RecoverAtTick ||
                    !IsLivingBinding(binding)) continue;
                binding.VM.RestartFaulted();
                binding.RecoverAtTick = -1;
                binding.ResetSourceLine();
                EntityRecovered?.Invoke(binding.EntityId);
            }
        }

        private bool IsLivingBinding(EntityVMBinding binding) =>
            simulation.TryGetEntity(binding.EntityId, out SBEntity entity) &&
            entity.alive && entity.side.id == binding.SideId;

        /// <returns>True when the VM stopped at a breakpoint before execution.</returns>
        private bool AdvanceOneLogicalStep(EntityVMBinding binding, bool checkBreakpoints,
            bool consumeTrailingMetadata, out bool consumedTurn)
        {
            consumedTurn = false;
            DirectivaVM vm = binding.VM;
            ConsumeDebugMetadata(vm);
            if (vm.Status == VMExecutionStatus.Running)
            {
                bool enteredLine = binding.EnteredNewSourceLine();
                // Returning from a callee re-enters the caller's original source
                // line. During a source step this is still the same instruction,
                // not a new breakpoint: do not stop at the breakpoint we stepped
                // away from. All other breakpoints keep their usual behavior.
                bool isStepOrigin = sourceStep != null &&
                    sourceStepEntityId == binding.EntityId &&
                    sourceStep.IsStartingLocation(vm);
                if (checkBreakpoints && enteredLine && !isStepOrigin &&
                    BreakpointPredicate != null &&
                    BreakpointPredicate(binding.SideId, binding.EntityId, vm.Debug.File,
                        vm.Debug.Line + 1))
                {
                    CancelSourceStep(); // An explicit breakpoint preempts either step mode.
                    IsPausedAtBreakpoint = true;
                    BreakpointEntityId = binding.EntityId;
                    BreakpointHit?.Invoke(binding.EntityId, vm.Debug.Snapshot());
                    return true;
                }
                vm.Advance();
                TotalVmSteps++;
                consumedTurn = true;
                if (consumeTrailingMetadata ||
                    (sourceStep != null && sourceStepEntityId == binding.EntityId) ||
                    (instructionStepEntityId == binding.EntityId))
                    ConsumeDebugMetadata(vm);
            }
            if (vm.Status == VMExecutionStatus.Faulted && binding.RecoverAtTick < 0)
            {
                binding.RecoverAtTick = checked(simulation.Tick + faultRestartDelayTicks);
                GetSide(binding.SideId).ReportError(binding);
                EntityFaulted?.Invoke(binding.EntityId);
            }
            if (sourceStep != null && sourceStepEntityId == binding.EntityId &&
                (vm.Status != VMExecutionStatus.Running || sourceStep.ReachedTarget(vm)))
            {
                CancelSourceStep();
                IsPausedAtBreakpoint = true;
                BreakpointEntityId = binding.EntityId;
                return true;
            }
            if (instructionStepEntityId == binding.EntityId &&
                (consumedTurn || vm.Status != VMExecutionStatus.Running))
            {
                // Only a NORMAL scheduler turn can complete a processor step.
                // Update() advances its cursor before returning from this pause,
                // so Continue cannot repeat the selected instruction.
                CancelSourceStep();
                IsPausedAtBreakpoint = true;
                BreakpointEntityId = binding.EntityId;
                return true;
            }
            return false;
        }

        private static void ConsumeDebugMetadata(DirectivaVM vm)
        {
            int zeroCost = 0;
            while (vm.Status == VMExecutionStatus.Running && vm.NextInstructionCost == 0)
            {
                if (++zeroCost > MaxZeroCostAdvancesPerTurn)
                {
                    vm.Abort(RuntimeError.InvalidOperation("Too many consecutive zero-cost VM debug instructions.", "detail_too_many_consecutive_zero_cost_vm_debug_instructions"));
                    break;
                }
                vm.Advance();
            }
        }

        private void CompleteBuilds()
        {
            // Drain results even when the match ended; a unit built on the winning
            // tick must not start a new VM after the game is already over.
            while (simulation.TryConsumeCreatedUnit(out UnitCreationResult unit))
            {
                if (!simulation.TryGetEntity(unit.unitId, out SBEntity entity)) continue;
                if (entity.side.id != 1 && entity.side.id != 2) continue;
                SideRuntime side = GetSide(entity.side.id);
                if (!side.TryConsumeBuild(unit.requestId, out int functionId)) continue;
                if (!IsFinished && entity.alive)
                    side.BindEntity(unit.unitId, functionId);
            }
        }

        private void RemoveDeadBindings()
        {
            RemoveDeadBindings(firstSide);
            RemoveDeadBindings(secondSide);
        }

        private void RemoveDeadBindings(SideRuntime side)
        {
            var removed = new List<int>();
            foreach (EntityVMBinding binding in side.Bindings)
                if (!IsLivingBinding(binding)) removed.Add(binding.EntityId);
            foreach (int id in removed) side.UnbindEntity(id);
        }

        private void EvaluateOutcome()
        {
            bool firstAlive = simulation.GetSide(1).Nexus?.alive == true;
            bool secondAlive = simulation.GetSide(2).Nexus?.alive == true;
            if (firstAlive && secondAlive) return;
            Finish(firstAlive ? MatchOutcome.Side1Won :
                secondAlive ? MatchOutcome.Side2Won : MatchOutcome.Draw,
                MatchEndReason.NexusDestroyed);
        }

        private void Finish(MatchOutcome outcome, MatchEndReason reason)
        {
            if (IsFinished) return;
            CancelSourceStep();
            Outcome = outcome;
            EndReason = reason;
            FinishedAtTick = simulation.Tick;
            MatchEnded?.Invoke(outcome);
        }

        private SideRuntime GetSide(int sideId) => sideId switch
        {
            1 => firstSide,
            2 => secondSide,
            _ => throw new ArgumentOutOfRangeException(nameof(sideId),
                "Only player sides 1 and 2 may execute scripts.")
        };

        private void RecordOutput(int sideId, int? entityId, VMOutputKind kind,
            string text, VMDebugState debug, RuntimeError error,
            string localizationKey, object[] localizationArguments)
        {
            var message = new VMOutputMessage(++outputSequence, simulation.Tick,
                sideId, entityId, kind, text, debug,
                error, localizationKey, localizationArguments);
            if (kind == VMOutputKind.Error)
                errorCounts[sideId]++;
            Queue<VMOutputMessage> queue = outputBySide[sideId];
            if (queue.Count == outputCapacityPerSide) queue.Dequeue();
            queue.Enqueue(message);
            OutputEmitted?.Invoke(message);
        }
    }
}
