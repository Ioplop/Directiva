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
        private readonly int outputCapacityPerSide;
        private int stepsPerTick;
        private long outputSequence;
        private bool hasAdvanced;
        private readonly long startTick;

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
            int maxDurationSeconds = 300)
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
            if (SConstants.deltaTime.RawValue <= 0)
                throw new InvalidOperationException("Simulation tick duration must be positive.");
            MaxDurationSeconds = maxDurationSeconds;
            // Round up instead of ending before the requested simulated duration.
            long durationRaw = checked((long)maxDurationSeconds * Constants.FixedScale);
            MaxSimulationTicks = checked(durationRaw + SConstants.deltaTime.RawValue - 1) /
                SConstants.deltaTime.RawValue;
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
                (entity, kind, text, debug) => RecordOutput(1, entity, kind, text, debug));
            secondSide = new SideRuntime(simulation, 2, side2Program,
                unchecked(simulation.seed ^ (SideSeedSalt * 2UL)), vmOptions, initializerBudget,
                (entity, kind, text, debug) => RecordOutput(2, entity, kind, text, debug));
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

        /// <summary>Advance exactly one 50 Hz world step, including all VM rounds.</summary>
        public void Update()
        {
            if (IsFinished) return; // No scripts or physics after a victory/draw.
            hasAdvanced = true;
            // Handle a nexus killed outside Update as well, before running any scripts.
            EvaluateOutcome();
            if (IsFinished) return;

            // Snapshot once per tick so every round sees precisely the same participants.
            // IDs are globally monotonic across both sides, so this order is stable.
            var scheduled = new List<EntityVMBinding>();
            scheduled.AddRange(firstSide.Bindings);
            scheduled.AddRange(secondSide.Bindings);
            scheduled.Sort((a, b) => a.EntityId.CompareTo(b.EntityId));

            // Freeze the setting for this tick, even if a host changes it during an output callback.
            int roundsThisTick = StepsPerTick;
            for (int round = 0; round < roundsThisTick; round++)
                foreach (EntityVMBinding binding in scheduled)
                {
                    if (!IsLivingBinding(binding) ||
                        binding.VM.Status == VMExecutionStatus.Faulted)
                        continue;
                    if (binding.VM.Status == VMExecutionStatus.Completed)
                        binding.VM.RestartCompleted();
                    AdvanceOneLogicalStep(binding);
                }

            // Physics, attacks, build progression, collisions and removals happen once.
            simulation.Update();
            EvaluateOutcome(); // Nexus deaths win priority over reaching the time limit.
            if (!IsFinished && simulation.Tick - startTick >= MaxSimulationTicks)
                Finish(MatchOutcome.Draw, MatchEndReason.TimeLimit);
            CompleteBuilds();
            RemoveDeadBindings();
        }

        private bool IsLivingBinding(EntityVMBinding binding) =>
            simulation.TryGetEntity(binding.EntityId, out SBEntity entity) &&
            entity.alive && entity.side.id == binding.SideId;

        private void AdvanceOneLogicalStep(EntityVMBinding binding)
        {
            DirectivaVM vm = binding.VM;
            int zeroCost = 0;
            while (vm.Status == VMExecutionStatus.Running && vm.NextInstructionCost == 0)
            {
                if (++zeroCost > MaxZeroCostAdvancesPerTurn)
                {
                    vm.Abort(RuntimeError.InvalidOperation(
                        "Too many consecutive zero-cost VM debug instructions."));
                    break;
                }
                vm.Advance();
            }
            if (vm.Status == VMExecutionStatus.Running)
            {
                vm.Advance();
                TotalVmSteps++;
            }
            if (vm.Status == VMExecutionStatus.Faulted)
                GetSide(binding.SideId).ReportError(binding);
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
            string text, VMDebugState debug)
        {
            var message = new VMOutputMessage(++outputSequence, simulation.Tick,
                sideId, entityId, kind, text, debug);
            Queue<VMOutputMessage> queue = outputBySide[sideId];
            if (queue.Count == outputCapacityPerSide) queue.Dequeue();
            queue.Enqueue(message);
            OutputEmitted?.Invoke(message);
        }
    }
}
