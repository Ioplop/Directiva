using System;
using System.Collections.Generic;
using DSCompilation.DScript.Backend;
using DSimulation;
using DSExecution.Errors;
using DSExecution.VirtualMachine;

namespace DOrchestration
{
    /// <summary>
    /// Owns the VMs, shared heap/globals and pending construction entry functions of
    /// one player side. The future Orchestrator chooses when each VM advances.
    /// </summary>
    internal sealed class SideRuntime
    {
        private readonly Simulation simulation;
        private readonly SimulationBridge bridge;
        private readonly VMOptions vmOptions;
        private readonly SortedDictionary<int, EntityVMBinding> bindings = new();
        private readonly Dictionary<int, int> buildEntries = new();
        private readonly Action<int?, VMOutputKind, string, VMDebugState> outputSink;

        internal int SideId { get; }
        internal DScriptCompiledProgram Program { get; }
        internal VMMemoryContext Memory { get; }
        internal DScriptInitializationReport Initialization { get; }
        internal IEnumerable<EntityVMBinding> Bindings => bindings.Values;
        internal int PendingBuildCount => buildEntries.Count;

        internal event Action<int, string> Printed;
        internal event Action<int, string> Warning;

        /// <summary>
        /// Creates exactly one shared-memory context and runs initialization once
        /// before any entity VM exists. Different sides must use different instances.
        /// </summary>
        internal SideRuntime(Simulation simulation, int sideId, DScriptCompiledProgram program,
            ulong randomSeed, VMOptions vmOptions = null, int initializerBudget = 100000,
            Action<int?, VMOutputKind, string, VMDebugState> outputSink = null)
        {
            this.simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
            if (sideId != 1 && sideId != 2)
                throw new ArgumentOutOfRangeException(nameof(sideId), "Only player sides may run DScript VMs.");
            Program = program ?? throw new ArgumentNullException(nameof(program));
            SideId = sideId;
            this.vmOptions = vmOptions ?? new VMOptions();
            this.outputSink = outputSink;
            bridge = new SimulationBridge(simulation);
            Memory = new VMMemoryContext(program.Code.GlobalCount, new HeapMemory(), randomSeed);
            Initialization = DScriptBootstrapRunner.Initialize(program, Memory,
                initializerBudget, this.vmOptions,
                onErrorWithDebug: (step, error, debug) =>
                    EmitOutput(null, VMOutputKind.Error,
                        "shared " + step.Module + "." + step.Variable + ": " + error.Message, debug),
                onOutputWithDebug: (text, debug) =>
                    EmitOutput(null, VMOutputKind.Print, text, debug));
        }

        internal bool IsValidUnitEntry(int functionId) =>
            Program.Code.TryGetFunction(functionId, out FunctionContext function) &&
            function.ParameterCount == 0;

        /// <summary>Bind only a living, registered, script-capable entity of this side.</summary>
        internal EntityVMBinding BindEntity(int entityId, int entryFunctionId)
        {
            if (!IsValidUnitEntry(entryFunctionId))
                throw new ArgumentException("Entry must be a compiled zero-argument DScript function.",
                    nameof(entryFunctionId));
            if (!simulation.TryGetEntity(entityId, out SBEntity entity) ||
                !entity.alive || entity is not SBVMEntity ||
                !ReferenceEquals(entity.side, simulation.GetSide(SideId)))
                throw new ArgumentException("Entity is not a living, script-capable member of this side.",
                    nameof(entityId));
            if (bindings.ContainsKey(entityId))
                throw new InvalidOperationException("Entity " + entityId + " already has a VM.");

            var host = new SimulationVMHostContext(this, bridge, entityId);
            var vm = new DirectivaVM(Program.Code, Memory, vmOptions, entryFunctionId, host);
            vm.Printed += text =>
            {
                Printed?.Invoke(entityId, text);
                EmitOutput(entityId, VMOutputKind.Print, text, vm.Debug.Snapshot());
            };
            var binding = new EntityVMBinding(entityId, SideId, entryFunctionId, vm);
            bindings.Add(entityId, binding);
            return binding;
        }

        internal bool TryGetBinding(int entityId, out EntityVMBinding binding) =>
            bindings.TryGetValue(entityId, out binding);

        /// <summary>Releases VM roots when its entity is destroyed or detached.</summary>
        internal bool UnbindEntity(int entityId)
        {
            if (!bindings.TryGetValue(entityId, out EntityVMBinding binding)) return false;
            if (binding.VM.Status == VMExecutionStatus.Running)
                binding.VM.Abort(RuntimeError.InvalidOperation("Bound entity is no longer active."));
            return bindings.Remove(entityId);
        }

        /// <summary>The function stays outside DSimulation until its requested unit exists.</summary>
        internal void RememberBuild(int requestId, int entryFunctionId)
        {
            if (requestId <= 0 || !IsValidUnitEntry(entryFunctionId) || buildEntries.ContainsKey(requestId))
                throw new InvalidOperationException("Invalid or duplicate construction request ID.");
            buildEntries.Add(requestId, entryFunctionId);
        }

        internal bool TryConsumeBuild(int requestId, out int entryFunctionId)
        {
            if (buildEntries.TryGetValue(requestId, out entryFunctionId))
            {
                buildEntries.Remove(requestId);
                return true;
            }
            entryFunctionId = -1;
            return false;
        }

        internal void ReportWarning(int entityId, string message)
        {
            Warning?.Invoke(entityId, message);
            VMDebugState debug = bindings.TryGetValue(entityId, out EntityVMBinding binding)
                ? binding.VM.Debug.Snapshot() : null;
            EmitOutput(entityId, VMOutputKind.Warning, message, debug);
        }

        internal void ReportError(EntityVMBinding binding)
        {
            if (binding.VM.Error == null) return;
            EmitOutput(binding.EntityId, VMOutputKind.Error,
                binding.VM.Error.Message, binding.VM.Debug.Snapshot());
        }

        private void EmitOutput(int? entityId, VMOutputKind kind, string text, VMDebugState debug) =>
            outputSink?.Invoke(entityId, kind, text, debug);
    }
}
