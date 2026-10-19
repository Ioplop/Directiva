using System;
using System.Collections.Generic;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    /// <summary>Round-robin shared-list interleavings and deterministic conflict handling.</summary>
    internal static class DILListConcurrencyTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string why, Action<DirectivaTestContext> action)
                => DirectivaTestCatalog.Add(tests, "ListConcurrency", id, why, action);

            Add("VersionConflictInsert", "An interleaved APPEND must not be erased by a long INSERT", t =>
            {
                string source = "# GLOBAL : shared\n" +
                    DILTestHarness.Function("LOAD_GLOBAL $shared\nPUSH INT 0\nPUSH INT 99\n" +
                        "INSERT\nRETURN") +
                    DILTestHarness.Function("LOAD_GLOBAL $shared\nPUSH INT 77\nAPPEND\nRETURN",
                        functionName: "writer");
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> {
                    ["test"] = source
                });
                var mem = new VMMemoryContext(1, 30);
                var initial = new ListHeapObject(new[] {
                    DataValue.FromInt(1), DataValue.FromInt(2), DataValue.FromInt(3) });
                uint id = mem.AllocHeap(initial);
                mem.SetGlobal(0, DataValue.FromListReference(id));
                var a = DILTestHarness.MakeVM(code, memory: mem);
                var b = DILTestHarness.MakeVM(code, memory: mem, entryFunctionId: FindId(code, ".writer"));
                for (int i = 0; i < 4; i++) a.Advance(); // Begin INSERT; now has an aux root.
                t.Equal(VMExecutionStatus.Running, a.Status, "VM A still running");
                var resultB = DILTestHarness.Run(t, b);
                t.Equal(VMExecutionStatus.Completed, resultB.Status, "append succeeded in VM B");
                var resultA = DILTestHarness.Run(t, a);
                DILTestHarness.ExpectFault(t, resultA, RuntimeErrorId.InvalidOperation);
                t.Equal(4, initial.Count, "writer update retained");
                t.Equal(77L, unchecked((long)initial.GetElement(3).value), "writer tail survived");
                t.Equal(1L, unchecked((long)initial.GetElement(0).value), "unfinished INSERT not published");
            });
            Add("VersionConflictExtendSource", "Mutating EXTEND source during continuation fails safely", t =>
            {
                string source = "# GLOBAL : target\n# GLOBAL : source\n" +
                    DILTestHarness.Function("LOAD_GLOBAL $target\nLOAD_GLOBAL $source\nEXTEND\nRETURN") +
                    DILTestHarness.Function("LOAD_GLOBAL $source\nPUSH INT 88\nAPPEND\nRETURN",
                        functionName: "writer");
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> {
                    ["test"] = source
                });
                var memory = new VMMemoryContext(2, 30);
                var target = new ListHeapObject(new[] { DataValue.FromInt(1) });
                var sourceList = new ListHeapObject(new[] { DataValue.FromInt(2) });
                uint dest = memory.AllocHeap(target);
                uint src = memory.AllocHeap(sourceList);
                memory.SetGlobal(0, DataValue.FromListReference(dest));
                memory.SetGlobal(1, DataValue.FromListReference(src));
                var a = DILTestHarness.MakeVM(code, memory: memory);
                var b = DILTestHarness.MakeVM(code, memory: memory, entryFunctionId: FindId(code, ".writer"));
                a.Advance(); a.Advance(); a.Advance(); // Stage EXTEND.
                var bResult = DILTestHarness.Run(t, b);
                t.Equal(VMExecutionStatus.Completed, bResult.Status, "writer status");
                var aResult = DILTestHarness.Run(t, a);
                DILTestHarness.ExpectFault(t, aResult, RuntimeErrorId.InvalidOperation);
                t.Equal(1, target.Count, "destination unchanged");
                t.Equal(2, sourceList.Count, "writer added source value");
            });
            Add("IntermediateNotVisible", "Another VM observes original list until commit", t =>
            {
                string source = "# GLOBAL : shared\n" +
                    DILTestHarness.Function("LOAD_GLOBAL $shared\nPUSH INT 0\nPUSH INT 99\nINSERT\nRETURN") +
                    DILTestHarness.Function("LOAD_GLOBAL $shared\nLENGTH\nRETURN",
                        functionName: "observer");
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> {
                    ["test"] = source
                });
                var memory = new VMMemoryContext(1, 50);
                var list = new ListHeapObject(new[] { DataValue.FromInt(1), DataValue.FromInt(2) });
                uint id = memory.AllocHeap(list);
                memory.SetGlobal(0, DataValue.FromListReference(id));
                var writer = DILTestHarness.MakeVM(code, memory: memory);
                for (int i = 0; i < 4; i++) writer.Advance(); // Stage operation.
                t.Equal(2, list.Count, "original count during stage");
                var observer = DILTestHarness.Run(t,
                    DILTestHarness.MakeVM(code, memory: memory, entryFunctionId: FindId(code, ".observer")));
                t.Equal(VMExecutionStatus.Completed, observer.Status, "observer ran");
                t.Equal(2L, unchecked((long)observer.Value.value), "observer sees old count");
                var final = DILTestHarness.Run(t, writer);
                t.Equal(VMExecutionStatus.Completed, final.Status, "insert committed");
                t.Equal(3, list.Count, "new contents become visible together");
            });
            Add("TwoSequentialExtends", "Independent sequential mutations reuse the same stable list reference", t =>
            {
                string source = "# GLOBAL : shared\n" +
                    DILTestHarness.Function("LOAD_GLOBAL $shared\nPUSH INT 5\nAPPEND\nRETURN");
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> {
                    ["test"] = source
                });
                var memory = new VMMemoryContext(1);
                var list = new ListHeapObject(Array.Empty<DataValue>());
                uint id = memory.AllocHeap(list);
                memory.SetGlobal(0, DataValue.FromListReference(id));
                for (int i = 0; i < 5; i++)
                {
                    var run = DILTestHarness.Run(t, DILTestHarness.MakeVM(code, memory: memory));
                    t.Equal(VMExecutionStatus.Completed, run.Status, "mutation instance " + i);
                }
                t.Equal(5, list.Count, "five updates visible");
                t.Equal(id, (uint)memory.GetGlobal(0).value, "heap identity stable");
            });
            Add("RoundRobinRepeatable", "Identical schedules produce the same list contents and GC counts", t =>
            {
                string one = RunSchedule(t);
                string two = RunSchedule(t);
                t.Equal(one, two, "repeatable schedule");
                t.Note("Schedule: " + one);
            });
        }

        private static int FindId(CodeContext code, string suffix)
        {
            foreach (var function in code.Functions)
                if (function.Name.EndsWith(suffix, StringComparison.Ordinal)) return function.Id;
            throw new InvalidOperationException("Missing test entry " + suffix);
        }

        private static string RunSchedule(DirectivaTestContext t)
        {
            string source = "# GLOBAL : shared\n" +
                DILTestHarness.Function("LOAD_GLOBAL $shared\nPUSH INT 1\nAPPEND\nRETURN");
            var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> {
                ["test"] = source
            });
            var memory = new VMMemoryContext(1, 20);
            var list = new ListHeapObject(Array.Empty<DataValue>());
            uint id = memory.AllocHeap(list);
            memory.SetGlobal(0, DataValue.FromListReference(id));
            var vms = new DirectivaVM[4];
            for (int i = 0; i < vms.Length; i++)
                vms[i] = DILTestHarness.MakeVM(code, memory: memory);
            for (int round = 0; round < 20; round++)
                for (int i = 0; i < vms.Length; i++)
                    if (vms[i].Status == VMExecutionStatus.Running) vms[i].Advance();
            foreach (var vm in vms)
                if (vm.Status != VMExecutionStatus.Completed)
                    throw new DirectivaTestAssertionException("Unexpected VM state " + vm.Status);
            return list.Count + ":" + memory.GarbageCollectionCount + ":" + memory.Heap.AllocatedSize;
        }
    }
}
