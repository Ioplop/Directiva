using System;
using System.Collections.Generic;
using System.Linq;
using Directiva.CodeUI;
using Directiva.EditorTests.SimulationTests;
using DSimulation;
using DSCompilation.DScript.Modules;
using DSExecution.DataTypes;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.CodeUITests
{
    /// <summary>Debugger must read compiled slots without changing VM or world execution.</summary>
    internal static class DebuggerVariableTests
    {
        private static DScriptEditorPreparedRun Prepare(DirectivaTestContext t, string script)
        {
            var run = DScriptEditorRunPipeline.Prepare("main", "main",
                new InMemoryDScriptSourceProvider(new Dictionary<string, string>
                { ["main"] = script }));
            t.Check(run.Success, "program compiled: " +
                string.Join("; ", run.Compilation.Diagnostics));
            return run;
        }

        private static DebuggerVariableSnapshot AdvanceUntilLocal(
            DirectivaTestContext t, DScriptEditorPreparedRun run,
            string name, string value)
        {
            DirectivaVM vm = run.VM;
            for (int i = 0; i < 500 && vm.Status == VMExecutionStatus.Running; i++)
            {
                var snapshot = DebuggerVariableSnapshot.Capture(vm, run.Program);
                if (snapshot.Locals.Any(row => row.Name == name && row.Value == value))
                    return snapshot;
                vm.Advance();
            }
            t.Check(false, "expected local " + name + " = " + value);
            return null;
        }

        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> run) =>
                tests.Add(new DirectivaTestCase("CodeUI", "DebuggerVariables", id, description, run));

            Add("CompiledSourceNamesAndSlots",
                "Compiler preserves exact source names/slots for parameters and locals", t =>
            {
                const string source = "def helper(first, second):\n    result = first + second\n    return result\ndef main():\n    helper(2, 3)\n";
                var run = Prepare(t, source);
                t.Check(run.Program.FunctionLocalSlots.TryGetValue("main.helper", out var slots),
                    "qualified function has debug locals");
                t.Equal(3, slots.Count, "two parameters and a local");
                t.Equal("first", slots[0].Name, "first parameter");
                t.Equal(0, slots[0].Slot, "first slot");
                t.Equal("second", slots[1].Name, "second parameter");
                t.Equal(1, slots[1].Slot, "second slot");
                t.Equal("result", slots[2].Name, "local variable");
                t.Equal(2, slots[2].Slot, "local slot");
            });

            Add("LocalVmGlobalAndSharedValues",
                "Live snapshot reads all three variable memories, not temporary DIL slots", t =>
            {
                const string source = "shared team = 11\nglobal private_count = 4\ndef main():\n    cargo = 7\n    print(cargo)\n";
                var run = Prepare(t, source);
                var shot = AdvanceUntilLocal(t, run, "cargo", "7");
                t.Check(shot.Globals.Any(x => x.Name == "private_count" && x.Value == "4"),
                    "private VM global is visible");
                t.Check(shot.Shared.Any(x => x.Name == "team" && x.Value == "11"),
                    "shared team global is visible");
                t.Equal(1, shot.Locals.Count, "temporary local slots are hidden");
                t.Equal(0, shot.Self.Count, "standalone execution has no self properties");
            });

            Add("CaptureIsReadOnly",
                "Inspecting locals, globals, shared slots and heap never advances execution", t =>
            {
                var run = Prepare(t, "shared s = 9\ndef main():\n    items = [1, 2, 3]\n    print(items)\n");
                var shot = AdvanceUntilLocal(t, run, "items", "[1, 2, 3]");
                int ip = run.VM.InstructionPointer;
                int depth = run.VM.CallDepth;
                ulong heap = run.Memory.Heap.AllocatedSize;
                for (int i = 0; i < 8; i++)
                {
                    var another = DebuggerVariableSnapshot.Capture(run.VM, run.Program);
                    t.Equal(shot.Locals[0].Value, another.Locals[0].Value, "stable read");
                }
                t.Equal(ip, run.VM.InstructionPointer, "instruction pointer unchanged");
                t.Equal(depth, run.VM.CallDepth, "call frames unchanged");
                t.Equal(heap, run.Memory.Heap.AllocatedSize, "no VM heap allocation");
            });

            Add("SelfUsesActualObservationProperties",
                "A living unit exposes its documented self properties without running DScript", t =>
            {
                var run = Prepare(t, "def main():\n    pass\n");
                var world = SimulationTestHarness.NewWorld();
                var unit = SimulationTestHarness.AddUnit(world, 1, 4, 5, 0);
                world.Start();
                long tick = world.Tick;
                var shot = DebuggerVariableSnapshot.Capture(run.VM, run.Program,
                    world.Observer, unit.id);
                t.Check(shot.Self.Any(x => x.Name == "self.id" && x.Value ==
                    unit.id.ToString()), "id observation");
                t.Check(shot.Self.Any(x => x.Name == "self.alive" && x.Value == "True"),
                    "alive observation");
                t.Check(shot.Self.Any(x => x.Name == "self.position"), "position observation");
                t.Check(shot.Self.Any(x => x.Name == "self.shields"), "shield observation");
                t.Check(!shot.Self.Any(x => x.Name == "self.build"), "orders excluded");
                t.Equal(tick, world.Tick, "inspecting self does not advance physics");
            });

            Add("ConsoleDebuggerTabsKeepMessages",
                "Tab switches content without discarding console messages or expanded state", t =>
            {
                var output = new OutputService();
                var panel = new OutputPanel(output, new LocalizationService("es"), 12f);
                output.Write("kept");
                panel.SetDebuggerMode(true);
                t.Check(panel.IsDebuggerMode, "debugger active");
                output.WriteWarning("still buffered");
                t.Equal(2, output.Entries.Count(), "console continues buffering");
                panel.SetDebuggerMode(false);
                t.Check(!panel.IsDebuggerMode, "console restored");
                t.Equal(2, output.Entries.Count(), "no output discarded on tab switch");
            });
        }
    }
}
