using System;
using System.Collections.Generic;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    /// <summary>Interleaved VMs, set continuation conflict detection and repeatable scheduling.</summary>
    internal static class DILSetConcurrencyTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string why, Action<DirectivaTestContext> action)
                => DirectivaTestCatalog.Add(tests, "SetConcurrency", id, why, action);

            Add("UpdateDestinationConflict", "A SET_ADD from another VM cancels staged SET_UPDATE", t =>
            {
                var (a, b, mem, destination, _) = Setup(t, "LOAD_GLOBAL $dst\nPUSH INT 90\nSET_ADD\nRETURN");
                a.Advance(); a.Advance(); a.Advance(); // stage SET_UPDATE with its aux root
                t.Equal(1, destination.Count, "destination unmodified during staging");
                t.Equal(VMExecutionStatus.Completed, DILTestHarness.Run(t, b).Status, "writer finished");
                DILTestHarness.ExpectFault(t, DILTestHarness.Run(t, a), RuntimeErrorId.InvalidOperation);
                t.Equal(2, destination.Count, "writer update remains visible");
                t.Check(destination.TryFind(DataValue.FromInt(90), 90, mem,
                    out int found, out _), "lookup after conflict");
                t.Check(found >= 0, "writer value not lost");
            });
            Add("UpdateSourceConflict", "Changing source while SET_UPDATE runs prevents stale commit", t =>
            {
                var (a, b, _, destination, source) = Setup(t,
                    "LOAD_GLOBAL $src\nPUSH INT 91\nSET_ADD\nRETURN");
                a.Advance(); a.Advance(); a.Advance();
                t.Equal(VMExecutionStatus.Completed, DILTestHarness.Run(t, b).Status, "writer status");
                DILTestHarness.ExpectFault(t, DILTestHarness.Run(t, a), RuntimeErrorId.InvalidOperation);
                t.Equal(1, destination.Count, "target unchanged");
                t.Equal(2, source.Count, "source writer retained");
            });
            Add("IntermediateInvisible", "Observer sees original set until buffered update commits", t =>
            {
                string source = "# GLOBAL : dst\n# GLOBAL : src\n" +
                    DILTestHarness.Function("LOAD_GLOBAL $dst\nLOAD_GLOBAL $src\nSET_UPDATE\nRETURN") +
                    DILTestHarness.Function("LOAD_GLOBAL $dst\nPUSH INT 2\nCONTAINS\nRETURN",
                        functionName: "observer");
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> {
                    ["test"] = source
                });
                var memory = new VMMemoryContext(2, 20);
                var dst = new SetHeapObject(); dst.AddAuthorized(DataValue.FromInt(1), 1);
                var src = new SetHeapObject(); src.AddAuthorized(DataValue.FromInt(2), 2);
                memory.SetGlobal(0, DataValue.FromSetReference(memory.AllocHeap(dst)));
                memory.SetGlobal(1, DataValue.FromSetReference(memory.AllocHeap(src)));
                var writer = DILTestHarness.MakeVM(code, memory: memory);
                writer.Advance(); writer.Advance(); writer.Advance();
                var observer = DILTestHarness.MakeVM(code, memory: memory,
                    entryFunctionId: FindId(code, ".observer"));
                var result = DILTestHarness.Run(t, observer);
                t.Equal(VMExecutionStatus.Completed, result.Status, "observer status");
                t.Equal(0UL, result.Value.value, "intermediate update invisible");
                t.Equal(VMExecutionStatus.Completed, DILTestHarness.Run(t, writer).Status, "writer completes");
                t.Equal(2, dst.Count, "update eventually published");
            });
            Add("DuplicateWriterNoConflict", "Adding an existing member does not invalidate another VM's continuation", t =>
            {
                var (a, b, _, destination, _) = Setup(t,
                    "LOAD_GLOBAL $dst\nPUSH INT 1\nSET_ADD\nRETURN");
                ulong version = destination.Version;
                a.Advance(); a.Advance(); a.Advance();
                t.Equal(VMExecutionStatus.Completed, DILTestHarness.Run(t, b).Status, "duplicate writer");
                t.Equal(version, destination.Version, "no mutation on duplicate");
                t.Equal(VMExecutionStatus.Completed, DILTestHarness.Run(t, a).Status, "pending update may finish");
                t.Equal(2, destination.Count, "union committed");
            });
            Add("GCProtectsPendingArguments", "GC while one VM is continued retains its source and target", t =>
            {
                var (a, b, mem, _, _) = Setup(t,
                    "PUSH STRING \"garbage-producing\"\nPOP\nPUSH NONE\nRETURN");
                a.Advance(); a.Advance(); a.Advance();
                var refs = new[] { (uint)mem.GetGlobal(0).value, (uint)mem.GetGlobal(1).value };
                mem.CollectGarbage();
                foreach (uint reference in refs)
                    t.Check(mem.TryGetHeap(reference, out _), "rooted input " + reference);
                t.Equal(VMExecutionStatus.Completed, DILTestHarness.Run(t, b).Status, "other VM status");
                t.Equal(VMExecutionStatus.Completed, DILTestHarness.Run(t, a).Status, "pending update completed");
            });
            Add("RoundRobinDeterministic", "Same scheduler order yields same Set and GC state", t =>
            {
                string first = Schedule(t);
                string second = Schedule(t);
                t.Equal(first, second, "deterministic concurrent run");
                t.Note("Schedule: " + first);
            });
            Add("IndependentSharedHeapSets", "VMs mutating different sets don't conflict", t =>
            {
                string source = "# GLOBAL : one\n# GLOBAL : two\n" +
                    DILTestHarness.Function("LOAD_GLOBAL $one\nPUSH INT 7\nSET_ADD\nRETURN") +
                    DILTestHarness.Function("LOAD_GLOBAL $two\nPUSH INT 8\nSET_ADD\nRETURN",
                        functionName: "second");
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> {
                    ["test"] = source
                });
                var mem = new VMMemoryContext(2, 20);
                var one = new SetHeapObject(); var two = new SetHeapObject();
                mem.SetGlobal(0, DataValue.FromSetReference(mem.AllocHeap(one)));
                mem.SetGlobal(1, DataValue.FromSetReference(mem.AllocHeap(two)));
                var a = DILTestHarness.MakeVM(code, memory: mem);
                var b = DILTestHarness.MakeVM(code, memory: mem,
                    entryFunctionId: FindId(code, ".second"));
                for (int i = 0; i < 12; i++)
                {
                    if (a.Status == VMExecutionStatus.Running) a.Advance();
                    if (b.Status == VMExecutionStatus.Running) b.Advance();
                }
                t.Equal(VMExecutionStatus.Completed, a.Status, "first VM");
                t.Equal(VMExecutionStatus.Completed, b.Status, "second VM");
                t.Equal(1, one.Count, "first set count");
                t.Equal(1, two.Count, "second set count");
            });
            Add("RepeatedSequentialMutations", "Fresh VMs share one set and preserve its heap ID", t =>
            {
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> {
                    ["test"] = "# GLOBAL : shared\n" +
                        DILTestHarness.Function("LOAD_GLOBAL $shared\nPUSH INT 10\nSET_ADD\nRETURN")
                });
                var memory = new VMMemoryContext(1);
                var set = new SetHeapObject();
                uint id = memory.AllocHeap(set);
                memory.SetGlobal(0, DataValue.FromSetReference(id));
                for (int i = 0; i < 8; i++)
                    t.Equal(VMExecutionStatus.Completed,
                        DILTestHarness.Run(t, DILTestHarness.MakeVM(code, memory: memory)).Status,
                        "repeat " + i);
                t.Equal(1, set.Count, "same value added only once");
                t.Equal(id, (uint)memory.GetGlobal(0).value, "identity stable");
            });
        }

        private static (DirectivaVM A, DirectivaVM B, VMMemoryContext Memory,
            SetHeapObject Destination, SetHeapObject Source) Setup(DirectivaTestContext t, string writerBody)
        {
            string source = "# GLOBAL : dst\n# GLOBAL : src\n" +
                DILTestHarness.Function("LOAD_GLOBAL $dst\nLOAD_GLOBAL $src\nSET_UPDATE\nRETURN") +
                DILTestHarness.Function(writerBody, functionName: "writer");
            var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> {
                ["test"] = source
            });
            var memory = new VMMemoryContext(2, 30);
            var dst = new SetHeapObject(); dst.AddAuthorized(DataValue.FromInt(1), 1);
            var src = new SetHeapObject(); src.AddAuthorized(DataValue.FromInt(2), 2);
            memory.SetGlobal(0, DataValue.FromSetReference(memory.AllocHeap(dst)));
            memory.SetGlobal(1, DataValue.FromSetReference(memory.AllocHeap(src)));
            var a = DILTestHarness.MakeVM(code, memory: memory);
            var b = DILTestHarness.MakeVM(code, memory: memory,
                entryFunctionId: FindId(code, ".writer"));
            return (a, b, memory, dst, src);
        }

        private static int FindId(CodeContext code, string suffix)
        {
            foreach (var f in code.Functions)
                if (f.Name.EndsWith(suffix, StringComparison.Ordinal)) return f.Id;
            throw new InvalidOperationException("Missing entry function " + suffix);
        }

        private static string Schedule(DirectivaTestContext t)
        {
            string source = "# GLOBAL : shared\n" +
                DILTestHarness.Function("LOAD_GLOBAL $shared\nPUSH INT 11\nSET_ADD\nRETURN");
            var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> {
                ["test"] = source
            });
            var memory = new VMMemoryContext(1, 20);
            var set = new SetHeapObject();
            uint id = memory.AllocHeap(set);
            memory.SetGlobal(0, DataValue.FromSetReference(id));
            var vms = new DirectivaVM[4];
            for (int i = 0; i < vms.Length; i++)
                vms[i] = DILTestHarness.MakeVM(code, memory: memory);
            for (int round = 0; round < 15; round++)
                foreach (DirectivaVM vm in vms)
                    if (vm.Status == VMExecutionStatus.Running) vm.Advance();
            foreach (DirectivaVM vm in vms)
                if (vm.Status != VMExecutionStatus.Completed)
                    throw new DirectivaTestAssertionException("Round robin left VM in " + vm.Status);
            return set.Count + ":" + set.Version + ":" + memory.Heap.AllocatedSize +
                ":" + memory.GarbageCollectionCount;
        }
    }
}
