using System;
using System.Collections.Generic;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    /// <summary>Deterministic multi-VM interactions, version conflicts and atomic updates.</summary>
    internal static class DILDictConcurrencyTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> action)
                => DirectivaTestCatalog.Add(tests, "DictConcurrency", id, description, action);

            Add("DestinationInsertConflict", "Concurrent insertion cancels staged update", t =>
            {
                var (a, b, _, dst, _) = Setup(t, "LOAD_GLOBAL $dst\nPUSH INT 9\nPUSH INT 90\nSET\nRETURN");
                Stage(a);
                t.Equal(VMExecutionStatus.Completed, DILTestHarness.Run(t, b).Status, "writer completes");
                DILTestHarness.ExpectFault(t, DILTestHarness.Run(t, a), RuntimeErrorId.InvalidOperation);
                t.Equal(2, dst.Count, "destination retains writer insertion");
                t.Equal(90L, unchecked((long)dst.GetValue(1).value), "writer value retained");
            });
            Add("DestinationOverwriteConflict", "Overwriting a value invalidates staged update", t =>
            {
                var (a, b, _, dst, _) = Setup(t, "LOAD_GLOBAL $dst\nPUSH INT 1\nPUSH INT 91\nSET\nRETURN");
                Stage(a);
                t.Equal(VMExecutionStatus.Completed, DILTestHarness.Run(t, b).Status, "writer completes");
                DILTestHarness.ExpectFault(t, DILTestHarness.Run(t, a), RuntimeErrorId.InvalidOperation);
                t.Equal(91L, unchecked((long)dst.GetValue(0).value), "overwrite retained");
            });
            Add("SourceOverwriteConflict", "Changing source value cancels stale update", t =>
            {
                var (a, b, _, dst, src) = Setup(t, "LOAD_GLOBAL $src\nPUSH INT 2\nPUSH INT 81\nSET\nRETURN");
                Stage(a);
                t.Equal(VMExecutionStatus.Completed, DILTestHarness.Run(t, b).Status, "source writer");
                DILTestHarness.ExpectFault(t, DILTestHarness.Run(t, a), RuntimeErrorId.InvalidOperation);
                t.Equal(1, dst.Count, "destination unchanged");
                t.Equal(81L, unchecked((long)src.GetValue(0).value), "new source value retained");
            });
            Add("SourceDeleteConflict", "Deleting a source entry cancels buffered update", t =>
            {
                var (a, b, _, dst, src) = Setup(t, "LOAD_GLOBAL $src\nPUSH INT 2\nDELETE\nRETURN");
                Stage(a);
                t.Equal(VMExecutionStatus.Completed, DILTestHarness.Run(t, b).Status, "source writer");
                DILTestHarness.ExpectFault(t, DILTestHarness.Run(t, a), RuntimeErrorId.InvalidOperation);
                t.Equal(1, dst.Count, "destination untouched");
                t.Equal(0, src.Count, "source removal kept");
            });
            Add("IntermediateInvisible", "Observers see old map until DictUpdate commits", t =>
            {
                string src = "# GLOBAL : dst\n# GLOBAL : src\n" +
                    DILTestHarness.Function("LOAD_GLOBAL $dst\nLOAD_GLOBAL $src\nDICT_UPDATE\nRETURN") +
                    DILTestHarness.Function("LOAD_GLOBAL $dst\nPUSH INT 2\nCONTAINS\nRETURN", functionName: "observer");
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> { ["test"] = src });
                var mem = new VMMemoryContext(2, 60);
                var dst = New(1, 10); var source = New(2, 20);
                mem.SetGlobal(0, DataValue.FromDictReference(mem.AllocHeap(dst)));
                mem.SetGlobal(1, DataValue.FromDictReference(mem.AllocHeap(source)));
                var writer = DILTestHarness.MakeVM(code, memory: mem);
                Stage(writer);
                var observer = DILTestHarness.Run(t, DILTestHarness.MakeVM(code, memory: mem,
                    entryFunctionId: FindId(code, ".observer")));
                t.Equal(VMExecutionStatus.Completed, observer.Status, "observer status");
                t.Equal(0UL, observer.Value.value, "old contents visible");
                t.Equal(VMExecutionStatus.Completed, DILTestHarness.Run(t, writer).Status, "update completed");
                t.Equal(2, dst.Count, "new key published together");
            });
            Add("PendingRootSurvivesGC", "Source and auxiliary buffer stay alive through GC", t =>
            {
                var (a, _, memory, dst, _) = Setup(t, "PUSH NONE\nRETURN");
                Stage(a);
                int count = memory.Heap.ObjectCount;
                memory.CollectGarbage();
                t.Equal(count, memory.Heap.ObjectCount, "all live objects rooted");
                t.Equal(VMExecutionStatus.Completed, DILTestHarness.Run(t, a).Status, "continued update completes");
                t.Equal(2, dst.Count, "target remains accessible");
            });
            Add("IndependentDestinations", "Two VMs mutate distinct maps in shared heap without conflicts", t =>
            {
                string src = "# GLOBAL : first\n# GLOBAL : second\n" +
                    DILTestHarness.Function("LOAD_GLOBAL $first\nPUSH INT 7\nPUSH INT 70\nSET\nRETURN") +
                    DILTestHarness.Function("LOAD_GLOBAL $second\nPUSH INT 8\nPUSH INT 80\nSET\nRETURN", functionName: "second");
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> { ["test"] = src });
                var mem = new VMMemoryContext(2);
                var left = new DictHeapObject(); var right = new DictHeapObject();
                mem.SetGlobal(0, DataValue.FromDictReference(mem.AllocHeap(left)));
                mem.SetGlobal(1, DataValue.FromDictReference(mem.AllocHeap(right)));
                var a = DILTestHarness.MakeVM(code, memory: mem);
                var b = DILTestHarness.MakeVM(code, memory: mem, entryFunctionId: FindId(code, ".second"));
                for (int n = 0; n < 16; n++)
                {
                    if (a.Status == VMExecutionStatus.Running) a.Advance();
                    if (b.Status == VMExecutionStatus.Running) b.Advance();
                }
                t.Equal(VMExecutionStatus.Completed, a.Status, "first finished");
                t.Equal(VMExecutionStatus.Completed, b.Status, "second finished");
                t.Equal(1, left.Count, "first map");
                t.Equal(1, right.Count, "second map");
            });
            Add("SequentialSharedMap", "Repeated VMs share and preserve one map heap identity", t =>
            {
                string source = "# GLOBAL : d\n" + DILTestHarness.Function(
                    "LOAD_GLOBAL $d\nPUSH INT 1\nPUSH INT 10\nSET\nRETURN");
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> { ["test"] = source });
                var mem = new VMMemoryContext(1);
                var dict = new DictHeapObject();
                uint id = mem.AllocHeap(dict);
                mem.SetGlobal(0, DataValue.FromDictReference(id));
                for (int i = 0; i < 10; i++)
                    t.Equal(VMExecutionStatus.Completed,
                        DILTestHarness.Run(t, DILTestHarness.MakeVM(code, memory: mem)).Status, "run " + i);
                t.Equal(1, dict.Count, "same key updated");
                t.Equal(id, (uint)mem.GetGlobal(0).value, "heap identity stable");
                t.Equal(3UL, mem.Heap.AllocatedSize, "no excess charge");
            });
            Add("DeterministicRoundRobin", "Repeated schedules yield identical dictionary state", t =>
            {
                string a = Schedule(t);
                string b = Schedule(t);
                t.Equal(a, b, "same schedule same result");
                t.Note("Schedule: " + a);
            });
            Add("ContinuedUpdateVersions", "Atomic buffer commit increments the destination version", t =>
            {
                var (a, _, _, dst, _) = Setup(t, "PUSH NONE\nRETURN");
                ulong old = dst.Version;
                Stage(a);
                t.Equal(old, dst.Version, "no premature mutation");
                t.Equal(VMExecutionStatus.Completed, DILTestHarness.Run(t, a).Status, "update completes");
                t.Check(dst.Version != old, "committed version changed");
            });
            Add("AliasedDictUpdate", "Updating a dictionary with itself is a no-op", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nPUSH INT 2\nDICT 1\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nLOAD_LOCAL 0\n" +
                    "DICT_UPDATE\nPOP\nLOAD_LOCAL 0\nLENGTH\nRETURN", locals: 1);
                t.Equal(1L, unchecked((long)r.Value.value), "same map unchanged");
            });
            Add("UpdatingTwoVMsSerially", "Serial updates from distinct VMs preserve all keys", t =>
            {
                var (a, _, mem, dst, _) = Setup(t, "PUSH NONE\nRETURN");
                t.Equal(VMExecutionStatus.Completed, DILTestHarness.Run(t, a).Status, "first update");
                var b = DILTestHarness.MakeVM(a.Code, memory: mem);
                t.Equal(VMExecutionStatus.Completed, DILTestHarness.Run(t, b).Status, "second update");
                t.Equal(2, dst.Count, "unique keys remain");
            });
        }

        private static DictHeapObject New(long key, long value)
        {
            var result = new DictHeapObject();
            result.AddAuthorized(DataValue.FromInt(key), DataValue.FromInt(value), key);
            return result;
        }

        private static (DirectivaVM A, DirectivaVM B, VMMemoryContext Memory,
            DictHeapObject Destination, DictHeapObject Source) Setup(DirectivaTestContext t, string writerBody)
        {
            string source = "# GLOBAL : dst\n# GLOBAL : src\n" +
                DILTestHarness.Function("LOAD_GLOBAL $dst\nLOAD_GLOBAL $src\nDICT_UPDATE\nRETURN") +
                DILTestHarness.Function(writerBody, functionName: "writer");
            var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> { ["test"] = source });
            var mem = new VMMemoryContext(2, 50);
            var dst = New(1, 10); var src = New(2, 20);
            mem.SetGlobal(0, DataValue.FromDictReference(mem.AllocHeap(dst)));
            mem.SetGlobal(1, DataValue.FromDictReference(mem.AllocHeap(src)));
            return (DILTestHarness.MakeVM(code, memory: mem),
                DILTestHarness.MakeVM(code, memory: mem, entryFunctionId: FindId(code, ".writer")),
                mem, dst, src);
        }

        private static void Stage(DirectivaVM vm)
        {
            vm.Advance(); // dst
            vm.Advance(); // src
            vm.Advance(); // DICT_UPDATE stages auxiliary root
        }

        private static int FindId(CodeContext code, string suffix)
        {
            foreach (var function in code.Functions)
                if (function.Name.EndsWith(suffix, StringComparison.Ordinal)) return function.Id;
            throw new InvalidOperationException("Missing function " + suffix);
        }

        private static string Schedule(DirectivaTestContext t)
        {
            string source = "# GLOBAL : shared\n" +
                DILTestHarness.Function("LOAD_GLOBAL $shared\nPUSH INT 1\nPUSH INT 99\nSET\nRETURN");
            var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> { ["test"] = source });
            var mem = new VMMemoryContext(1);
            var dict = new DictHeapObject();
            mem.SetGlobal(0, DataValue.FromDictReference(mem.AllocHeap(dict)));
            var machines = new DirectivaVM[5];
            for (int i = 0; i < machines.Length; i++)
                machines[i] = DILTestHarness.MakeVM(code, memory: mem);
            for (int turn = 0; turn < 20; turn++)
                foreach (var vm in machines)
                    if (vm.Status == VMExecutionStatus.Running) vm.Advance();
            foreach (var vm in machines)
                if (vm.Status != VMExecutionStatus.Completed)
                    throw new DirectivaTestAssertionException("Unexpected VM status: " + vm.Status);
            return dict.Count + ":" + dict.Version + ":" + mem.Heap.AllocatedSize +
                ":" + mem.GarbageCollectionCount;
        }
    }
}
