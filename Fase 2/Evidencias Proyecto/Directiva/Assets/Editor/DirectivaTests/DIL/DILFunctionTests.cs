using System;
using System.Collections.Generic;
using System.Linq;
using DSCompilation.DIL;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    /// <summary>Compiled functions as values, name binding, indirect invocation and error contracts.</summary>
    internal static class DILFunctionTests
    {
        internal static CodeContext Program(DirectivaTestContext t, string main,
            int locals = 0, string otherFunctions = "", string header = "")
        {
            return DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> {
                ["test"] = header + DILTestHarness.Function(main, locals) + otherFunctions
            });
        }

        internal static string Fn(string name, string body, int parameters = 0, int locals = 0)
            => DILTestHarness.Function(body, locals, parameters, name);

        internal static int Id(CodeContext code, string suffix)
        {
            return code.Functions.Single(f => f.Name == "test." + suffix).Id;
        }

        internal static DILObservation Run(DirectivaTestContext t, string main,
            int locals = 0, string others = "", string header = "", int budget = 3000)
        {
            var code = Program(t, main, locals, others, header);
            return DILTestHarness.Run(t, DILTestHarness.MakeVM(code), budget);
        }

        internal static void IntResult(DirectivaTestContext t, string main, long expected,
            int locals = 0, string others = "", string header = "")
        {
            var result = Run(t, main, locals, others, header);
            t.Equal(VMExecutionStatus.Completed, result.Status, "completed: " + result.Error?.Message);
            t.Equal(DataTypeId.Int, result.Value.dataType, "result type");
            t.Equal(expected, unchecked((long)result.Value.value), "result value");
        }

        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> test)
                => DirectivaTestCatalog.Add(tests, "Functions", id, description, test);

            Add("ReferenceIsValue", "PUSH_FUNCTION yields non-heap Function DataValue", t =>
            {
                var code = Program(t, "PUSH_FUNCTION worker\nRETURN", otherFunctions: Fn("worker", "PUSH NONE\nRETURN"));
                var r = DILTestHarness.Run(t, DILTestHarness.MakeVM(code));
                t.Equal(VMExecutionStatus.Completed, r.Status, "status");
                t.Equal(DataTypeId.Function, r.Value.dataType, "type");
                t.Equal((ulong)Id(code, "worker"), r.Value.value, "function id");
                t.Check(!r.Value.TryGetHeapReference(out _), "function is not a heap reference");
                t.Equal(0, r.VM.Memory.Heap.ObjectCount, "no heap objects");
            });
            Add("ForwardFunction", "Compiler resolves a function declared later", t =>
                IntResult(t, "PUSH_FUNCTION later\nCALL_INDIRECT 0\nRETURN", 33,
                    others: Fn("later", "PUSH INT 33\nRETURN")));
            Add("FullyQualifiedFunction", "Fully qualified function names resolve correctly", t =>
                IntResult(t, "PUSH_FUNCTION test.later\nCALL_INDIRECT 0\nRETURN", 4,
                    others: Fn("later", "PUSH INT 4\nRETURN")));
            Add("LegacyQualifiedDollar", "Optional qualified $-prefixed function names remain valid", t =>
                IntResult(t, "PUSH_FUNCTION $test.later\nCALL_INDIRECT 0\nRETURN", 44,
                    others: Fn("later", "PUSH INT 44\nRETURN")));
            Add("BasicCall", "Indirect call with zero arguments returns correct value", t =>
                IntResult(t, "PUSH_FUNCTION target\nCALL_INDIRECT 0\nRETURN", 19,
                    others: Fn("target", "PUSH INT 19\nRETURN")));
            Add("OneParameter", "One argument is passed to the callee's local 0", t =>
                IntResult(t, "PUSH_FUNCTION inc\nPUSH INT 9\nCALL_INDIRECT 1\nRETURN", 10,
                    others: Fn("inc", "LOAD_LOCAL 0\nPUSH INT 1\nADD\nRETURN", 1, 1)));
            Add("ArgumentOrder", "Multiple arguments preserve their left-to-right order", t =>
                IntResult(t, "PUSH_FUNCTION subtract\nPUSH INT 5\nPUSH INT 13\nCALL_INDIRECT 2\nRETURN", -8,
                    others: Fn("subtract", "LOAD_LOCAL 0\nLOAD_LOCAL 1\nSUBTRACT\nRETURN", 2, 2)));
            Add("ThreeParameters", "CALL_INDIRECT accepts arbitrary n-ary calls", t =>
                IntResult(t, "PUSH_FUNCTION combine\nPUSH INT 2\nPUSH INT 3\nPUSH INT 4\nCALL_INDIRECT 3\nRETURN", 9,
                    others: Fn("combine", "LOAD_LOCAL 0\nLOAD_LOCAL 1\nADD\nLOAD_LOCAL 2\nADD\nRETURN", 3, 3)));
            Add("LocalFunctionVariable", "Function references can be stored and read from locals", t =>
                IntResult(t, "PUSH_FUNCTION worker\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nCALL_INDIRECT 0\nRETURN", 47,
                    1, Fn("worker", "PUSH INT 47\nRETURN")));
            Add("GlobalFunctionVariable", "Function references can be stored in global slots", t =>
                IntResult(t, "PUSH_FUNCTION worker\nSTORE_GLOBAL $action\nLOAD_GLOBAL $action\nCALL_INDIRECT 0\nRETURN", 48,
                    others: Fn("worker", "PUSH INT 48\nRETURN"), header: "# GLOBAL : action\n"));
            Add("FunctionParameter", "Functions may be passed as parameters of other functions", t =>
                IntResult(t, "PUSH_FUNCTION double\nPUSH INT 21\nCALL $test.apply\nRETURN", 42,
                    others: Fn("double", "LOAD_LOCAL 0\nPUSH INT 2\nMULTIPLY\nRETURN", 1, 1) +
                            Fn("apply", "LOAD_LOCAL 0\nLOAD_LOCAL 1\nCALL_INDIRECT 1\nRETURN", 2, 2)));
            Add("ReturnFunction", "A function can return a different function", t =>
                IntResult(t, "CALL $test.choose\nCALL_INDIRECT 0\nRETURN", 202,
                    others: Fn("choose", "PUSH_FUNCTION chosen\nRETURN") +
                            Fn("chosen", "PUSH INT 202\nRETURN")));
            Add("FunctionInTuple", "Tuple contains and returns a callable value", t =>
                IntResult(t, "PUSH_FUNCTION work\nTUPLE 1\nPUSH INT 0\nGET\nCALL_INDIRECT 0\nRETURN", 31,
                    others: Fn("work", "PUSH INT 31\nRETURN")));
            Add("FunctionInList", "A mutable list can store a callable", t =>
                IntResult(t, "PUSH_FUNCTION work\nLIST 1\nPUSH INT 0\nGET\nCALL_INDIRECT 0\nRETURN", 52,
                    others: Fn("work", "PUSH INT 52\nRETURN")));
            Add("FunctionInDictionaryValue", "Dictionary values can be functions", t =>
                IntResult(t, "PUSH INT 1\nPUSH_FUNCTION work\nDICT 1\nPUSH INT 1\nGET\nCALL_INDIRECT 0\nRETURN", 61,
                    others: Fn("work", "PUSH INT 61\nRETURN")));
            Add("FunctionInDictionaryKey", "Dictionary keys can be function values with stable hash", t =>
                IntResult(t, "PUSH_FUNCTION work\nPUSH INT 63\nDICT 1\nPUSH_FUNCTION work\nGET\nRETURN", 63,
                    others: Fn("work", "PUSH NONE\nRETURN")));
            Add("FunctionInSet", "Set accepts Function members and deduplicates them", t =>
                IntResult(t, "PUSH_FUNCTION work\nPUSH_FUNCTION work\nSET 2\nLENGTH\nRETURN", 1,
                    others: Fn("work", "PUSH NONE\nRETURN")));
            Add("FunctionIdentityEqual", "Same compiled function always compares equal", t =>
            {
                var r = Run(t, "PUSH_FUNCTION work\nPUSH_FUNCTION work\nVALUE_EQUALS\nRETURN",
                    others: Fn("work", "PUSH NONE\nRETURN"));
                t.Equal(VMExecutionStatus.Completed, r.Status, "completed");
                t.Equal(1UL, r.Value.value, "identity equality");
            });
            Add("FunctionIdentityDifferent", "Different functions are not equal", t =>
            {
                var r = Run(t, "PUSH_FUNCTION a\nPUSH_FUNCTION b\nVALUE_EQUALS\nRETURN",
                    others: Fn("a", "PUSH NONE\nRETURN") + Fn("b", "PUSH NONE\nRETURN"));
                t.Equal(VMExecutionStatus.Completed, r.Status, "completed");
                t.Equal(0UL, r.Value.value, "different functions");
            });
            Add("SameFunctionReferenceEquals", "Reference equality uses the identity of a compiled function", t =>
            {
                var r = Run(t, "PUSH_FUNCTION a\nPUSH_FUNCTION a\nREFERENCE_EQUALS\nRETURN",
                    others: Fn("a", "PUSH NONE\nRETURN"));
                t.Equal(VMExecutionStatus.Completed, r.Status, "completed");
                t.Equal(1UL, r.Value.value, "same function identity");
            });
            Add("DistinctFunctionReferenceEquals", "Different functions do not share reference identity", t =>
            {
                var r = Run(t, "PUSH_FUNCTION a\nPUSH_FUNCTION b\nREFERENCE_EQUALS\nRETURN",
                    others: Fn("a", "PUSH NONE\nRETURN") + Fn("b", "PUSH NONE\nRETURN"));
                t.Equal(VMExecutionStatus.Completed, r.Status, "completed");
                t.Equal(0UL, r.Value.value, "different function identity");
            });
            Add("FunctionVsInteger", "Function and integer with same payload aren't equal", t =>
            {
                var r = Run(t, "PUSH_FUNCTION main\nPUSH INT 0\nVALUE_EQUALS\nRETURN");
                t.Equal(VMExecutionStatus.Completed, r.Status, "completed");
                t.Equal(0UL, r.Value.value, "different types");
            });
            Add("FunctionHashStable", "Identical Function values have matching hashes", t =>
            {
                var r = Run(t, "PUSH_FUNCTION work\nHASH\nPUSH_FUNCTION work\nHASH\nVALUE_EQUALS\nRETURN",
                    others: Fn("work", "PUSH NONE\nRETURN"));
                t.Equal(VMExecutionStatus.Completed, r.Status, "completed");
                t.Equal(1UL, r.Value.value, "hash equality");
            });
            Add("FunctionZeroTruthy", "Function id zero is truthy", t =>
            {
                var r = Run(t, "PUSH_FUNCTION main\nIS_TRUTHY\nRETURN");
                t.Equal(VMExecutionStatus.Completed, r.Status, "completed");
                t.Equal(DataTypeId.Bool, r.Value.dataType, "bool");
                t.Equal(1UL, r.Value.value, "function zero is true");
            });
            Add("FunctionCastIdentity", "CAST FUNCTION preserves function reference", t =>
            {
                var r = Run(t, "PUSH_FUNCTION main\nCAST FUNCTION\nRETURN");
                t.Equal(VMExecutionStatus.Completed, r.Status, "completed");
                t.Equal(DataTypeId.Function, r.Value.dataType, "function");
            });
            Add("FunctionCastToString", "Function stringification displays stable program-local id", t =>
            {
                var code = Program(t, "PUSH_FUNCTION main\nCAST STRING\nRETURN");
                var r = DILTestHarness.Run(t, DILTestHarness.MakeVM(code));
                t.Equal(VMExecutionStatus.Completed, r.Status, "completed");
                t.Equal(DataTypeId.String, r.Value.dataType, "string");
                t.Check(r.VM.Memory.TryGetHeap((uint)r.Value.value, out HeapObject obj), "valid heap reference");
                t.Equal("<function #0>", ((StringHeapObject)obj).Value, "string format");
            });
            Add("FunctionPrint", "PRINT stringifies a function and continues", t =>
            {
                var r = Run(t, "PUSH_FUNCTION main\nPRINT\nPUSH INT 7\nRETURN");
                t.Equal(VMExecutionStatus.Completed, r.Status, "completed");
                t.Equal(1, r.Output.Count, "event count");
                t.Equal("<function #0>", r.Output[0], "print event");
            });
            Add("FunctionInsideStringCollection", "Tuple formatting can stringify function elements", t =>
            {
                var code = Program(t, "PUSH_FUNCTION main\nTUPLE 1\nCAST STRING\nRETURN");
                var r = DILTestHarness.Run(t, DILTestHarness.MakeVM(code));
                t.Equal(VMExecutionStatus.Completed, r.Status, "completed");
                t.Check(r.VM.Memory.TryGetHeap((uint)r.Value.value, out HeapObject obj), "tuple text heap");
                t.Equal("(<function #0>,)", ((StringHeapObject)obj).Value, "tuple text");
            });
            Add("NestedIndirectCalls", "Indirect calls nest over three function frames", t =>
                IntResult(t, "PUSH_FUNCTION outer\nCALL_INDIRECT 0\nRETURN", 8,
                    others: Fn("outer", "PUSH_FUNCTION middle\nCALL_INDIRECT 0\nRETURN") +
                            Fn("middle", "PUSH_FUNCTION inner\nCALL_INDIRECT 0\nRETURN") +
                            Fn("inner", "PUSH INT 8\nRETURN")));
            Add("RecursiveIndirect", "Indirect self recursion works with the existing frame stack", t =>
                IntResult(t, "PUSH_FUNCTION countdown\nPUSH INT 5\nCALL_INDIRECT 1\nRETURN", 0,
                    others: Fn("countdown", "LOAD_LOCAL 0\nPUSH INT 0\nVALUE_EQUALS\n" +
                        "JUMP_IF_TRUE done\nPUSH_FUNCTION countdown\nLOAD_LOCAL 0\n" +
                        "PUSH INT 1\nSUBTRACT\nCALL_INDIRECT 1\nRETURN\ndone:\n" +
                        "LOAD_LOCAL 0\nRETURN", 1, 1)));
            Add("CallByReferenceToDirect", "CALL_INDIRECT and classic CALL can interoperate", t =>
                IntResult(t, "PUSH_FUNCTION helper\nCALL_INDIRECT 0\nRETURN", 81,
                    others: Fn("helper", "CALL $test.leaf\nRETURN") +
                            Fn("leaf", "PUSH INT 81\nRETURN")));
            Add("GlobalReassignment", "A global function selector can change at runtime", t =>
                IntResult(t, "PUSH_FUNCTION miner\nSTORE_GLOBAL $action\n" +
                    "LOAD_GLOBAL $action\nCALL_INDIRECT 0\nSTORE_LOCAL 0\n" +
                    "PUSH_FUNCTION attacker\nSTORE_GLOBAL $action\n" +
                    "LOAD_GLOBAL $action\nCALL_INDIRECT 0\nLOAD_LOCAL 0\nADD\nRETURN", 17,
                    1, Fn("miner", "PUSH INT 7\nRETURN") +
                       Fn("attacker", "PUSH INT 10\nRETURN"), "# GLOBAL : action\n"));
            Add("CallRetainsCallerTemporaries", "Indirect call restores the evaluation stack prefix", t =>
                IntResult(t, "PUSH INT 10\nPUSH_FUNCTION work\nPUSH INT 7\nCALL_INDIRECT 1\nADD\nRETURN", 17,
                    others: Fn("work", "LOAD_LOCAL 0\nRETURN", 1, 1)));
            Add("BoundedExecution", "CALL_INDIRECT consumes a normal VM budget step", t =>
            {
                var code = Program(t, "PUSH_FUNCTION work\nCALL_INDIRECT 0\nRETURN",
                    otherFunctions: Fn("work", "PUSH INT 9\nRETURN"));
                var vm = DILTestHarness.MakeVM(code);
                t.Equal(VMExecutionStatus.Running, vm.Run(2).Status, "paused in callee");
                t.Equal(2, vm.CallDepth, "call depth");
                t.Equal(VMExecutionStatus.Completed, vm.Run(10).Status, "finished");
                t.Equal(9L, unchecked((long)vm.ReturnValue.value), "result");
            });
            Add("FunctionIdFactory", "DataValue validates and decodes compiled function ids", t =>
            {
                var value = DataValue.FromFunctionId(2);
                t.Check(value.TryGetFunctionId(out int id), "recognized function value");
                t.Equal(2, id, "function id");
                t.Equal(DataTypeId.Function, value.dataType, "function type");
                t.Check(!DataValue.FromInt(2).TryGetFunctionId(out _), "reject integer");
                try { DataValue.FromFunctionId(-1); throw new DirectivaTestAssertionException("negative id accepted"); }
                catch (ArgumentOutOfRangeException) { t.Check(true, "negative id rejected"); }
            });
            Add("ImportedFunction", "PUSH_FUNCTION resolves imported module function id", t =>
            {
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> {
                    ["test"] = "# IMPORT : lib\n" + DILTestHarness.Function(
                        "PUSH_FUNCTION lib.answer\nCALL_INDIRECT 0\nRETURN"),
                    ["lib"] = DILTestHarness.Function("PUSH INT 73\nRETURN", functionName: "answer")
                });
                var r = DILTestHarness.Run(t, DILTestHarness.MakeVM(code));
                t.Equal(VMExecutionStatus.Completed, r.Status, "imported invocation");
                t.Equal(73L, unchecked((long)r.Value.value), "import result");
            });
            Add("DebugMetadata", "Debug metadata injection keeps PUSH_FUNCTION targets stable", t =>
            {
                string src = DILTestHarness.Function("PUSH_FUNCTION worker\nCALL_INDIRECT 0\nRETURN") +
                             Fn("worker", "PUSH INT 66\nRETURN");
                var code = DILTestHarness.ParseModules(t, "test",
                    new Dictionary<string, string> { ["test"] = src },
                    new DILParserOptions(generateDebugMetadata: true));
                var r = DILTestHarness.Run(t, DILTestHarness.MakeVM(code));
                t.Equal(VMExecutionStatus.Completed, r.Status, "debug invocation");
                t.Equal(66L, unchecked((long)r.Value.value), "result");
            });
            Add("FunctionTypeRegistered", "Function type has registered operations and identity casting", t =>
            {
                t.Check(DataType.GetDataType(DataTypeId.Function) is DTFunction, "registered type");
                var code = Program(t, "PUSH_FUNCTION main\nHASH\nRETURN");
                var r = DILTestHarness.Run(t, DILTestHarness.MakeVM(code));
                t.Equal(VMExecutionStatus.Completed, r.Status, "hash successful");
                t.Equal(DataTypeId.Int, r.Value.dataType, "hash type");
            });
            Add("CaseSensitiveFunctionName", "Function names preserve original case", t =>
                IntResult(t, "PUSH_FUNCTION Worker\nCALL_INDIRECT 0\nRETURN", 88,
                    others: Fn("Worker", "PUSH INT 88\nRETURN") +
                            Fn("worker", "PUSH INT 99\nRETURN")));

            // Parse errors: these fail before the code can be executed.
            Add("UnknownFunction", "Unresolved PUSH_FUNCTION fails during link", t =>
                DILTestHarness.ExpectParseFailure(t, DILTestHarness.Function(
                    "PUSH_FUNCTION missing\nRETURN"), "Unknown function"));
            Add("InvalidFunctionIdentifier", "Invalid function identifier fails parsing", t =>
                DILTestHarness.ExpectParseFailure(t, DILTestHarness.Function(
                    "PUSH_FUNCTION 1bad\nRETURN"), "Invalid function name"));
            Add("UnknownImportedModuleFunction", "Cannot load a function from a module outside the import graph", t =>
                DILTestHarness.ExpectParseFailure(t, DILTestHarness.Function(
                    "PUSH_FUNCTION foreign.handler\nRETURN"), "Unknown function"));
            Add("PushFunctionMissingArgument", "PUSH_FUNCTION requires exactly one name", t =>
                DILTestHarness.ExpectParseFailure(t, DILTestHarness.Function(
                    "PUSH_FUNCTION\nRETURN"), "expects 1 operand"));
            Add("PushFunctionExtraArgument", "PUSH_FUNCTION refuses extra operands", t =>
                DILTestHarness.ExpectParseFailure(t, DILTestHarness.Function(
                    "PUSH_FUNCTION main extra\nRETURN"), "expects 1 operand"));
            Add("IndirectNegativeArity", "Negative indirect argument counts rejected by parser", t =>
                DILTestHarness.ExpectParseFailure(t, DILTestHarness.Function(
                    "CALL_INDIRECT -1\nRETURN"), "non-negative argument count"));
            Add("IndirectExcessiveArity", "Int32.MaxValue indirect argument count is rejected", t =>
                DILTestHarness.ExpectParseFailure(t, DILTestHarness.Function(
                    "CALL_INDIRECT 2147483647\nRETURN"), "below Int32.MaxValue"));
            Add("IndirectMissingArity", "CALL_INDIRECT requires a count", t =>
                DILTestHarness.ExpectParseFailure(t, DILTestHarness.Function(
                    "CALL_INDIRECT\nRETURN"), "expects 1 operand"));
            Add("IndirectExtraArgument", "CALL_INDIRECT refuses extra operands", t =>
                DILTestHarness.ExpectParseFailure(t, DILTestHarness.Function(
                    "CALL_INDIRECT 0 extra\nRETURN"), "expects 1 operand"));

            Add("InvalidTargetInt", "Indirect calls cannot target an integer", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "PUSH INT 0\nCALL_INDIRECT 0\nRETURN"), RuntimeErrorId.InvalidCallTarget));
            Add("InvalidTargetNone", "Indirect calls cannot target None", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "PUSH NONE\nCALL_INDIRECT 0\nRETURN"), RuntimeErrorId.InvalidCallTarget));
            Add("InvalidTargetString", "Indirect calls cannot target a String", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "PUSH STRING \"hello\"\nCALL_INDIRECT 0\nRETURN"), RuntimeErrorId.InvalidCallTarget));
            Add("UninitializedTarget", "An uninitialized function variable is a script error", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "LOAD_LOCAL 0\nCALL_INDIRECT 0\nRETURN", locals: 1),
                    RuntimeErrorId.VariableUsedBeforeInitialization));
            Add("WrongArityTooFew", "Wrong indirect argument count has its own runtime error", t =>
                DILTestHarness.ExpectFault(t, Run(t,
                    "PUSH_FUNCTION work\nCALL_INDIRECT 0\nRETURN",
                    others: Fn("work", "LOAD_LOCAL 0\nRETURN", 1, 1)),
                    RuntimeErrorId.InvalidFunctionArgumentCount));
            Add("WrongArityTooMany", "Extra function arguments are not silently consumed", t =>
                DILTestHarness.ExpectFault(t, Run(t,
                    "PUSH_FUNCTION work\nPUSH INT 1\nCALL_INDIRECT 1\nRETURN",
                    others: Fn("work", "PUSH NONE\nRETURN")),
                    RuntimeErrorId.InvalidFunctionArgumentCount));
            Add("StackUnderflow", "Indirect invocation needs the function and all arguments", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "PUSH_FUNCTION main\nCALL_INDIRECT 1\nRETURN"), RuntimeErrorId.StackUnderflow));
            Add("DirectInvalidFunctionId", "Invalid PUSH_FUNCTION id from untrusted bytecode is rejected", t =>
            {
                var vm = DILTestHarness.MakeVM(DILTestHarness.Direct(t, 0,
                    Instruction.PushFunction(123456), Instruction.Return()));
                DILTestHarness.ExpectFault(t, DILTestHarness.Run(t, vm), RuntimeErrorId.InvalidFunction);
            });
            Add("ForgedFunctionRef", "Invalid Function value from bytecode cannot be invoked", t =>
            {
                var vm = DILTestHarness.MakeVM(DILTestHarness.Direct(t, 0,
                    Instruction.Push(DataValue.FromFunctionId(123456)),
                    Instruction.CallIndirect(0), Instruction.Return()));
                DILTestHarness.ExpectFault(t, DILTestHarness.Run(t, vm), RuntimeErrorId.InvalidFunction);
            });
            Add("OverflowFunctionId", "Function references cannot silently truncate ulong to int", t =>
            {
                var vm = DILTestHarness.MakeVM(DILTestHarness.Direct(t, 0,
                    Instruction.Push(new DataValue(DataTypeId.Function, ulong.MaxValue)),
                    Instruction.CallIndirect(0), Instruction.Return()));
                DILTestHarness.ExpectFault(t, DILTestHarness.Run(t, vm), RuntimeErrorId.InvalidFunction);
            });
            Add("DirectInvalidNegativeArity", "Bad raw CALL_INDIRECT -1 cannot trigger internal VM error", t =>
            {
                var vm = DILTestHarness.MakeVM(DILTestHarness.Direct(t, 0,
                    Instruction.CallIndirect(-1), Instruction.Return()));
                DILTestHarness.ExpectFault(t, DILTestHarness.Run(t, vm), RuntimeErrorId.InvalidInstruction);
            });
            Add("DirectInvalidExcessiveArity", "Bad raw CALL_INDIRECT Int32.MaxValue cannot overflow", t =>
            {
                var vm = DILTestHarness.MakeVM(DILTestHarness.Direct(t, 0,
                    Instruction.CallIndirect(int.MaxValue), Instruction.Return()));
                DILTestHarness.ExpectFault(t, DILTestHarness.Run(t, vm), RuntimeErrorId.InvalidInstruction);
            });
            Add("IndirectCallDepthLimit", "Recursive indirect calls respect the VM call depth budget", t =>
            {
                var code = Program(t, "PUSH_FUNCTION main\nCALL_INDIRECT 0\nRETURN");
                var vm = DILTestHarness.MakeVM(code, options: new VMOptions(maxCallDepth: 4));
                DILTestHarness.ExpectFault(t, DILTestHarness.Run(t, vm), RuntimeErrorId.CallStackOverflow);
            });
            Add("ImmutableFunctionValue", "Function cannot be cast from an integer id", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "PUSH INT 0\nCAST FUNCTION\nRETURN"), RuntimeErrorId.InvalidCast));
            Add("NoHeapGrowthWhenStored", "Creating and storing many function refs consumes no heap units", t =>
            {
                string many = string.Join("\n", Enumerable.Repeat("PUSH_FUNCTION main", 200));
                var r = Run(t, many + "\nLIST 200\nLENGTH\nRETURN");
                t.Equal(VMExecutionStatus.Completed, r.Status, "completed");
                t.Equal(200L, unchecked((long)r.Value.value), "list count");
                t.Equal((ulong)201, r.VM.Memory.Heap.AllocatedSize, "only list slots consume heap");
            });
        }
    }
}
