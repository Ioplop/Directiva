using System;
using System.Collections.Generic;
using System.Text;
using DSCompilation.DIL;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    internal static class DILRobustnessTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string purpose, Action<DirectivaTestContext> action)
                => DirectivaTestCatalog.Add(tests, "Robustness", id, purpose, action);

            Add("SeededIntegerArithmetic", "500 bounded deterministic arithmetic cases with known oracle", t =>
            {
                const int seed = 20261015;
                var random = new Random(seed);
                t.Note("Seed=" + seed + ", samples=500, domain=[-100000,100000], no intentional overflow");
                for (int i = 0; i < 500; i++)
                {
                    long a = random.Next(-100000, 100001);
                    long b = random.Next(-100000, 100001);
                    OperationId op = i % 3 == 0 ? OperationId.Add :
                        i % 3 == 1 ? OperationId.Subtract : OperationId.Multiply;
                    long expected = op == OperationId.Add ? a + b :
                        op == OperationId.Subtract ? a - b : a * b;
                    var vm = DirectIntOperation(a, b, op);
                    var r = vm.Run(20);
                    t.Check(r.Status == VMExecutionStatus.Completed,
                        "sample " + i + " failed: " + a + " " + op + " " + b + " error=" + r.Error?.Message);
                    t.Equal(DataTypeId.Int, r.ReturnValue.dataType, "sample=" + i + " result type");
                    t.Equal(expected, unchecked((long)r.ReturnValue.value),
                        "sample=" + i + " operation=" + op + " a=" + a + " b=" + b);
                }
            });
            Add("SeededFloorDivAndModulo", "300 signed integer division/modulo invariants for b != 0", t =>
            {
                const int seed = 912733;
                var random = new Random(seed);
                t.Note("Seed=" + seed + ", samples=300, denominator != 0");
                for (int i = 0; i < 300; i++)
                {
                    long a = random.Next(-10000, 10001);
                    long b = random.Next(-100, 101);
                    if (b == 0) b = 1;
                    long q = Math.DivRem(a, b, out long rem);
                    if (rem != 0 && (rem < 0) != (b < 0)) q--;
                    long expectedRem = a - q * b;
                    var div = DirectIntOperation(a, b, OperationId.FloorDivide).Run(30);
                    var mod = DirectIntOperation(a, b, OperationId.Modulo).Run(30);
                    t.Equal(VMExecutionStatus.Completed, div.Status, "division status: " + i);
                    t.Equal(VMExecutionStatus.Completed, mod.Status, "modulo status: " + i);
                    t.Equal(q, unchecked((long)div.ReturnValue.value), "floor division: " + a + "/" + b);
                    t.Equal(expectedRem, unchecked((long)mod.ReturnValue.value), "modulo: " + a + "%" + b);
                }
            });
            Add("HashEqualityProperty", "Numerically equal scalars must have identical hashes", t =>
            {
                for (long n = 0; n <= 100; n++)
                {
                    long hashInt = HashScalar(DataValue.FromInt(n));
                    long hashDecimal = HashScalar(DataValue.FromDecimalRaw(n * DTDecimal.Scale));
                    t.Equal(hashInt, hashDecimal, "int-decimal hash equivalence n=" + n);
                    if (n <= 1)
                        t.Equal(hashInt, HashScalar(DataValue.FromBool(n != 0)), "int-bool equivalence n=" + n);
                }
                t.Note("Compared 101 Int/Decimal pairs and Boolean 0/1 hash equivalence");
            });
            Add("DeepNestedTupleNoHostRecursion", "200 nested tuples hash, stringify and mark without host stack overflow", t =>
            {
                const int depth = 200;
                var memory = new VMMemoryContext(1, 1200);
                DataValue child = DataValue.FromInt(7);
                for (int i = 0; i < depth; i++)
                    child = DataValue.FromTupleReference(memory.AllocHeap(new TupleHeapObject(new[] { child })));
                memory.SetGlobal(0, child);
                var type = DTTuple.Instance;
                var hash = type.Hash(new OperationCall(new[] { child }, 0, memory, null));
                t.Equal(OperationStatus.Success, hash.Status, "deep tuple hash");
                t.Equal(DataTypeId.Int, hash.Value.dataType, "deep hash result type");
                var cast = type.CastTo(DataTypeId.String, child, memory);
                t.Check(cast.IsSuccess, "deep tuple cast failed: " + cast.Error?.Message);
                int sweptTemporaries = memory.CollectGarbage();
                t.Check(child.TryGetHeapReference(out uint rootId) &&
                    memory.TryGetHeap(rootId, out _), "rooted deeply nested tuple collected");
                t.Equal(depth, memory.Heap.ObjectCount, "rooted nested tuple count after sweep");
                t.Note("depth=" + depth + ", hash=" + unchecked((long)hash.Value.value) +
                    ", temporary strings swept=" + sweptTemporaries);
            });
            Add("DeterministicFuzzMalformedInstructions", "200 bogus parser tokens must raise DILParseException, not exceptions from host", t =>
            {
                var random = new Random(61607);
                var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ_0123456789";
                for (int i = 0; i < 200; i++)
                {
                    var token = new StringBuilder("UNKNOWN_");
                    for (int n = 0; n < 5; n++) token.Append(chars[random.Next(chars.Length)]);
                    string source = DILTestHarness.Function(token + "\nPUSH NONE\nRETURN");
                    try
                    {
                        new DILParser(new DILMemorySource(new Dictionary<string, string> {
                            ["test"] = source
                        })).Parse("test");
                    }
                    catch (DILParseException) { t.Check(true, "proper parse failure"); continue; }
                    catch (Exception ex)
                    {
                        throw new DirectivaTestAssertionException("Malformed sample " + i +
                            " threw host exception for " + token + ": " + ex);
                    }
                    throw new DirectivaTestAssertionException("Parser accepted bogus mnemonic " + token);
                }
                t.Note("Deterministic fuzz seed=61607; 200 invalid opcode samples");
            });
            Add("RepeatedLazyGC", "400 small allocations in tight heap never exceed logical capacity", t =>
            {
                var memory = new VMMemoryContext(0, 11);
                for (int i = 0; i < 400; i++)
                {
                    t.Check(memory.TryAllocHeap(new StringHeapObject("x"), out _), "allocation " + i);
                    t.Check(memory.Heap.AllocatedSize <= 11, "heap size exceeded cap at " + i);
                }
                t.Check(memory.GarbageCollectionCount > 10, "stress did not trigger many GC cycles");
                t.Note("400 allocations; GC=" + memory.GarbageCollectionCount +
                    "; final allocated=" + memory.Heap.AllocatedSize);
            });
            Add("TupleEqualityAgainstHashes", "Structurally equal tuples across heaps hash the same", t =>
            {
                var a = DILTestHarness.ExpectCompleted(t, "PUSH INT 2\nPUSH BOOL TRUE\nTUPLE 2\nHASH\nRETURN");
                var b = DILTestHarness.ExpectCompleted(t, "PUSH DECIMAL 2.0\nPUSH INT 1\nTUPLE 2\nHASH\nRETURN");
                t.Equal(a.Value.value, b.Value.value, "hash equivalence of equal tuples");
            });
            Add("ScriptCanBeReparsed", "Same source yields stable function IDs and instruction sequence", t =>
            {
                string source = "# IMPORT : util\n" + DILTestHarness.Function(
                    "PUSH INT 4\nCALL $util.identity\nRETURN");
                var map = new Dictionary<string, string> {
                    ["test"] = source,
                    ["util"] = DILTestHarness.Function("LOAD_LOCAL 0\nRETURN", 1, 1, "identity")
                };
                var a = DILTestHarness.ParseModules(t, "test", map);
                var b = DILTestHarness.ParseModules(t, "test", map);
                t.Equal(a.EntryFunctionId, b.EntryFunctionId, "entry function ID");
                t.Equal(a.Functions.Count, b.Functions.Count, "function count");
                foreach (var f in a.Functions)
                {
                    var g = b.GetFunction(f.Id);
                    t.Equal(f.Name, g.Name, "function id -> symbol");
                    t.Equal(f.InstructionCount, g.InstructionCount, "instruction count");
                    for (int i = 0; i < f.InstructionCount; i++)
                    {
                        f.TryGetInstruction(i, out var insA);
                        g.TryGetInstruction(i, out var insB);
                        t.Equal(insA.ToString(), insB.ToString(), "instruction " + i);
                    }
                }
            });
            Add("OperationCatalogComplete", "Every declared operation has a stable arity and valid dispatch receivers", t =>
            {
                foreach (OperationId id in Enum.GetValues(typeof(OperationId)))
                {
                    OperationInfo info = OperationCatalog.GetInfo(id);
                    t.Check(info.ParameterCount >= 1 && info.ParameterCount <= 3,
                        "Unexpected arity for " + id + ": " + info.ParameterCount);
                    int[] dispatchIndices = info.DispatchIndices.ToArray();
                    t.Check(dispatchIndices.Length > 0, "No dispatch receiver for " + id);
                    foreach (int index in dispatchIndices)
                        t.Check(index >= 0 && index < info.ParameterCount,
                            "Bad dispatch index for " + id + ": " + index);
                }
                t.Note("Enumerated operation catalog, including reserved mutable-container operations.");
            });
            Add("ExceptionBoundaries", "Invalid options and references fail with host argument exceptions at constructor boundaries", t =>
            {
                bool vmOptionsFailed = false;
                try { new VMOptions(maxCallDepth: 0); }
                catch (ArgumentOutOfRangeException) { vmOptionsFailed = true; }
                t.Check(vmOptionsFailed, "VMOptions accepted zero call depth");
                bool heapRefFailed = false;
                try { DataValue.FromTupleReference(0); }
                catch (ArgumentOutOfRangeException) { heapRefFailed = true; }
                t.Check(heapRefFailed, "DataValue accepted zero heap reference");
            });
        }

        private static DirectivaVM DirectIntOperation(long a, long b, OperationId op)
        {
            var function = new FunctionContext(0, "property", 0, 0,
                Instruction.Push(DataValue.FromInt(a)), Instruction.Push(DataValue.FromInt(b)),
                Instruction.Operation(op), Instruction.Return());
            return new DirectivaVM(new CodeContext(0, 0, new[] { function }));
        }
        private static long HashScalar(DataValue value)
        {
            var function = new FunctionContext(0, "hash", 0, 0,
                Instruction.Push(value), Instruction.Operation(OperationId.Hash), Instruction.Return());
            var vm = new DirectivaVM(new CodeContext(0, 0, new[] { function }));
            var r = vm.Run(20);
            if (r.Status != VMExecutionStatus.Completed || r.ReturnValue.dataType != DataTypeId.Int)
                throw new DirectivaTestAssertionException("Scalar hash failed for " + value.dataType +
                    ": " + r.Error?.Message);
            return unchecked((long)r.ReturnValue.value);
        }
    }
}
