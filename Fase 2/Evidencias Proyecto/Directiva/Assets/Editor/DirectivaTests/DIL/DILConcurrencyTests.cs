using System;
using System.Collections.Generic;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    internal static class DILConcurrencyTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string purpose, Action<DirectivaTestContext> action)
                => DirectivaTestCatalog.Add(tests, "Concurrency", id, purpose, action);

            Add("RoundRobinIsDeterministic", "Repeated schedules of 6 VMs produce identical global state", t =>
            {
                string logA = RunRoundRobin(t, 6, 4);
                string logB = RunRoundRobin(t, 6, 4);
                t.Equal(logA, logB, "round-robin determinism across independent executions");
                t.Note("Schedule result=" + logA);
            });
            Add("InterleavingLostUpdate", "A known interleaving race is reproducible (no threads)", t =>
            {
                var code = IncrementCode(t);
                var memory = new VMMemoryContext(code.GlobalCount);
                memory.SetGlobal(0, DataValue.FromInt(0));
                var vmA = DILTestHarness.MakeVM(code, memory: memory);
                var vmB = DILTestHarness.MakeVM(code, memory: memory);
                // Both LOAD_GLOBAL happen before either STORE_GLOBAL.
                for (int i = 0; i < 6; i++) { vmA.Advance(); vmB.Advance(); }
                t.Equal(1L, unchecked((long)memory.GetGlobal(0).value),
                    "both VMs must deterministically observe stale counter and write 1");
                t.Note("Deterministic data race intentionally yields counter=1 instead of 2");
            });
            Add("SequentialScheduleDifferentOutcome", "Sequential execution serializes two global increments", t =>
            {
                var code = IncrementCode(t);
                var mem = new VMMemoryContext(code.GlobalCount);
                mem.SetGlobal(0, DataValue.FromInt(0));
                var a = DILTestHarness.Run(t, DILTestHarness.MakeVM(code, memory: mem));
                var b = DILTestHarness.Run(t, DILTestHarness.MakeVM(code, memory: mem));
                t.Equal(VMExecutionStatus.Completed, a.Status, "first VM");
                t.Equal(VMExecutionStatus.Completed, b.Status, "second VM");
                t.Equal(2L, unchecked((long)mem.GetGlobal(0).value), "global increments");
            });
            Add("SharedHeapViaGlobal", "A tuple created by one VM is read by another sharing memory", t =>
            {
                var source = "# GLOBAL : value\n" +
                    DILTestHarness.Function("PUSH INT 41\nTUPLE 1\nSTORE_GLOBAL $value\nPUSH NONE\nRETURN") +
                    DILTestHarness.Function("LOAD_GLOBAL $value\nPUSH INT 0\nGET\nRETURN", functionName: "read");
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> { ["test"] = source });
                var mem = new VMMemoryContext(code.GlobalCount);
                var writer = DILTestHarness.Run(t, DILTestHarness.MakeVM(code, memory: mem));
                int readId = -1;
                foreach (var fn in code.Functions)
                    if (fn.Name.EndsWith(".read", StringComparison.Ordinal)) readId = fn.Id;
                t.Check(readId >= 0, "reader function missing");
                var reader = DILTestHarness.Run(t, DILTestHarness.MakeVM(code, memory: mem, entryFunctionId: readId));
                t.Equal(VMExecutionStatus.Completed, writer.Status, "writer status");
                t.Equal(VMExecutionStatus.Completed, reader.Status, "reader status");
                t.Equal(41L, unchecked((long)reader.Value.value), "shared heap tuple element");
            });
            Add("SharedHeapPreservesRunningVMRoots", "GC triggered by one VM preserves another VM's stack reference", t =>
            {
                var src = DILTestHarness.Function("PUSH STRING \"protected\"\nPRINT\nPUSH NONE\nRETURN") +
                          DILTestHarness.Function("PUSH STRING \"a\"\nPOP\nPUSH STRING \"b\"\nPOP\nPUSH NONE\nRETURN", functionName: "collector");
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> { ["test"] = src });
                int id = -1;
                foreach (var f in code.Functions)
                    if (f.Name.EndsWith(".collector", StringComparison.Ordinal)) id = f.Id;
                t.Check(id >= 0, "collector function missing");
                var mem = new VMMemoryContext(code.GlobalCount, 6);
                var keeper = DILTestHarness.MakeVM(code, memory: mem);
                keeper.Advance(); // PUSH STRING protected: stack root
                uint kept = 1;
                t.Check(mem.TryGetHeap(kept, out _), "keeper string missing before pressure");
                var collector = DILTestHarness.Run(t, DILTestHarness.MakeVM(code, memory: mem, entryFunctionId: id));
                t.Equal(VMExecutionStatus.Completed, collector.Status, "collector execution");
                t.Check(mem.GarbageCollectionCount > 0, "second VM did not pressure heap");
                t.Check(mem.TryGetHeap(kept, out _), "GC collected object owned by paused VM");
                var result = DILTestHarness.Run(t, keeper);
                t.Equal(VMExecutionStatus.Completed, result.Status, "keeper execution");
                t.Equal("protected", result.Output[0], "keeper text survived");
            });
            Add("SeparateLocalStacks", "VMs share globals/heap but not local and evaluation stacks", t =>
            {
                var code = DILTestHarness.Parse(t, "PUSH INT 8\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nRETURN", locals: 1);
                var mem = new VMMemoryContext(code.GlobalCount);
                var a = DILTestHarness.MakeVM(code, memory: mem);
                var b = DILTestHarness.MakeVM(code, memory: mem);
                a.Advance(); a.Advance(); // a's local now initialized
                t.Equal(DataTypeId.Uninitialized, b.CurrentLocals.Get(0).dataType,
                    "b's local should remain uninitialized");
                t.Equal(8L, unchecked((long)a.CurrentLocals.Get(0).value), "a's initialized local");
            });
            Add("FaultIsolation", "One faulted VM does not halt other VMs on same shared memory", t =>
            {
                var code = DILTestHarness.Parse(t, "POP\nPUSH INT 5\nRETURN");
                var good = DILTestHarness.Parse(t, "PUSH INT 7\nRETURN");
                var memory = new VMMemoryContext(0);
                var failing = DILTestHarness.Run(t, DILTestHarness.MakeVM(code, memory: memory));
                var success = DILTestHarness.Run(t, DILTestHarness.MakeVM(good, memory: memory));
                DILTestHarness.ExpectFault(t, failing, RuntimeErrorId.StackUnderflow);
                t.Equal(VMExecutionStatus.Completed, success.Status, "other VM unaffected");
                t.Equal(7L, unchecked((long)success.Value.value), "other VM result");
            });
            Add("SharedMemoryLayoutValidation", "VM constructor rejects mismatch between global slot counts", t =>
            {
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> {
                    ["test"] = "# GLOBAL : x\n" + DILTestHarness.Function("PUSH NONE\nRETURN")
                });
                try { DILTestHarness.MakeVM(code, memory: new VMMemoryContext(0)); }
                catch (ArgumentException ex) { t.Note("Expected mismatch: " + ex.Message); return; }
                throw new DirectivaTestAssertionException("Accepted mismatched shared memory layout");
            });
        }

        private static CodeContext IncrementCode(DirectivaTestContext t)
        {
            return DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> {
                ["test"] = "# GLOBAL : counter\n" + DILTestHarness.Function(
                    "LOAD_GLOBAL $counter\nPUSH INT 1\nADD\nSTORE_GLOBAL $counter\n" +
                    "LOAD_GLOBAL $counter\nRETURN")
            });
        }

        private static string RunRoundRobin(DirectivaTestContext t, int machines, int repeats)
        {
            var code = IncrementCode(t);
            var mem = new VMMemoryContext(code.GlobalCount);
            mem.SetGlobal(0, DataValue.FromInt(0));
            var vms = new DirectivaVM[machines];
            for (int i = 0; i < machines; i++) vms[i] = DILTestHarness.MakeVM(code, memory: mem);
            int steps = 0;
            for (int round = 0; round < repeats && steps < 5000; round++)
                for (int i = 0; i < machines; i++)
                    if (vms[i].Status == VMExecutionStatus.Running) { vms[i].Advance(); steps++; }
            while (steps < 5000)
            {
                bool any = false;
                foreach (var vm in vms)
                    if (vm.Status == VMExecutionStatus.Running) { vm.Advance(); steps++; any = true; }
                if (!any) break;
            }
            foreach (var vm in vms) t.Equal(VMExecutionStatus.Completed, vm.Status, "round-robin VM completion");
            t.Note("machines=" + machines + " scheduling-steps=" + steps + " counter=" +
                   unchecked((long)mem.GetGlobal(0).value));
            return unchecked((long)mem.GetGlobal(0).value) + "/" + steps;
        }
    }
}
