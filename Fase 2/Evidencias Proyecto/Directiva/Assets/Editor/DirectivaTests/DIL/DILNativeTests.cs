using System;
using System.Collections.Generic;
using System.Linq;
using DSCompilation.DIL;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Native;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    /// <summary>Native call linking, builtins, seeded PRNG, math and GC regression coverage.</summary>
    internal static class DILNativeTests
    {
        private static string Entry(string code, string imports = "", int locals = 0)
            => imports + DILTestHarness.Function(code, locals);

        private static CodeContext Compile(DirectivaTestContext t, string code,
            string imports = "", int locals = 0)
            => DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> {
                ["test"] = Entry(code, imports, locals)
            });

        private static DILObservation Run(DirectivaTestContext t, string code,
            string imports = "", int locals = 0, ulong heap = HeapMemory.DefaultMaxSize,
            int budget = 3000)
            => DILTestHarness.Run(t, DILTestHarness.MakeVM(Compile(t, code, imports, locals), heap), budget);

        private static void Value(DirectivaTestContext t, string code, DataTypeId type,
            long raw, string imports = "")
        {
            var r = Run(t, code, imports);
            t.Equal(VMExecutionStatus.Completed, r.Status, "status: " + r.Error?.Message);
            t.Equal(type, r.Value.dataType, "type");
            t.Equal(raw, unchecked((long)r.Value.value), "raw value");
        }

        private static void Error(DirectivaTestContext t, string code, RuntimeErrorId id,
            string imports = "", ulong heap = HeapMemory.DefaultMaxSize)
            => DILTestHarness.ExpectFault(t, Run(t, code, imports, heap: heap), id);

        private static string Call(string name, int arguments, params string[] pushes)
            => "PUSH_FUNCTION " + name + "\n" + string.Join("\n", pushes) +
               "\nCALL_INDIRECT " + arguments + "\nRETURN";

        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string group, string id, string description, Action<DirectivaTestContext> action)
                => DirectivaTestCatalog.Add(tests, group, id, description, action);

            // Basic global functions, returned native function values and stack semantics.
            Add("NativeBuiltins", "PrintInt", "print(Int) raises Printed and returns None", t => {
                var r = Run(t, Call("print", 1, "PUSH INT 42"));
                t.Equal(VMExecutionStatus.Completed, r.Status, "status");
                t.Equal(DataTypeId.None, r.Value.dataType, "print return");
                t.Equal(1, r.Output.Count, "number of events");
                t.Equal("42", r.Output[0], "printed number");
            });
            Add("NativeBuiltins", "PrintString", "print(String) preserves exact text", t => {
                var r = Run(t, Call("print", 1, "PUSH STRING \"Hello Snail\""));
                t.Equal(VMExecutionStatus.Completed, r.Status, "status");
                t.Equal("Hello Snail", r.Output.Single(), "text");
            });
            Add("NativeBuiltins", "PrintNone", "print(None) outputs None", t => {
                var r = Run(t, Call("print", 1, "PUSH NONE"));
                t.Equal("None", r.Output.Single(), "text");
            });
            Add("NativeBuiltins", "PrintReturnsNone", "print does not leave input on stack", t =>
                Value(t, "PUSH_FUNCTION print\nPUSH INT 3\nCALL_INDIRECT 1\nIS_TRUTHY\nRETURN", DataTypeId.Bool, 0));
            Add("NativeBuiltins", "PrintZeroArgs", "print() emits exactly one empty line and returns None", t => {
                var r = Run(t, Call("print", 0));
                t.Equal(VMExecutionStatus.Completed, r.Status, "status");
                t.Equal(DataTypeId.None, r.Value.dataType, "return type");
                t.Equal(1, r.Output.Count, "event count");
                t.Equal("", r.Output[0], "empty line");
            });
            Add("NativeBuiltins", "PrintMixedArguments", "print() formats mixed values separated by one space", t => {
                var r = Run(t, Call("print", 4, "PUSH STRING \"Value:\"", "PUSH INT 42",
                    "PUSH BOOL TRUE", "PUSH NONE"));
                t.Equal(VMExecutionStatus.Completed, r.Status, "status");
                t.Equal(DataTypeId.None, r.Value.dataType, "return type");
                t.Equal(1, r.Output.Count, "one event, not one per argument");
                t.Equal("Value: 42 True None", r.Output[0], "printed values");
            });
            Add("NativeBuiltins", "PrintList", "print(label, List) reuses str(List) representation", t => {
                var r = Run(t, Call("print", 2, "PUSH STRING \"Primos:\"",
                    "PUSH INT 2\nPUSH INT 3\nPUSH INT 5\nLIST 3"));
                t.Equal(VMExecutionStatus.Completed, r.Status, "status");
                t.Equal("Primos: [2, 3, 5]", r.Output.Single(), "list format");
            });
            Add("NativeBuiltins", "PrintEmptyString", "empty argument still adds its surrounding separators", t => {
                var r = Run(t, Call("print", 3, "PUSH STRING \"a\"",
                    "PUSH STRING \"\"", "PUSH STRING \"b\""));
                t.Equal("a  b", r.Output.Single(), "empty middle field");
            });
            Add("NativeBuiltins", "PrintFromFunctionValue", "a stored print reference accepts multiple arguments", t => {
                var r = Run(t, "PUSH_FUNCTION print\nSTORE_LOCAL 0\nLOAD_LOCAL 0\n" +
                    "PUSH STRING \"Hola\"\nPUSH INT 17\nCALL_INDIRECT 2\nRETURN", locals: 1);
                t.Equal(VMExecutionStatus.Completed, r.Status, "status");
                t.Equal("Hola 17", r.Output.Single(), "indirect output");
            });
            Add("NativeBuiltins", "PrintCallerStack", "variadic print does not consume caller temporary values", t => {
                var r = Run(t, "PUSH INT 10\nPUSH_FUNCTION print\nPUSH INT 1\nPUSH INT 2\n" +
                    "CALL_INDIRECT 2\nPOP\nPUSH INT 7\nADD\nRETURN");
                t.Equal(VMExecutionStatus.Completed, r.Status, "status");
                t.Equal("1 2", r.Output.Single(), "one event");
                t.Equal(17L, unchecked((long)r.Value.value), "caller stack value retained");
            });
            Add("NativeBuiltins", "PrintNestedCall", "nested str(print()) preserves earlier expression values", t => {
                var r = Run(t, "PUSH STRING \"9\"\nPUSH_FUNCTION str\nPUSH_FUNCTION print\n" +
                    "PUSH STRING \"Hola\"\nCALL_INDIRECT 1\nCALL_INDIRECT 1\n" +
                    "ADD\nPUSH STRING \"5\"\nADD\nRETURN");
                t.Equal(VMExecutionStatus.Completed, r.Status, "status");
                t.Equal("Hola", r.Output.Single(), "inner print event");
                t.Check(r.Value.TryGetHeapReference(out uint id), "string return reference");
                t.Equal("9None5", ((StringHeapObject)r.VM.Memory.GetHeap(id)).Value, "nested return");
            });
            Add("NativeBuiltins", "PrintMaximumArguments", "print accepts the native limit of sixteen arguments", t => {
                string[] arguments = Enumerable.Range(0, 16).Select(i => "PUSH INT " + i).ToArray();
                var r = Run(t, Call("print", 16, arguments));
                t.Equal(VMExecutionStatus.Completed, r.Status, "status");
                t.Equal(string.Join(" ", Enumerable.Range(0, 16)), r.Output.Single(), "all arguments");
            });
            Add("NativeBuiltins", "PrintArity", "print rejects seventeen arguments (native limit is sixteen)", t =>
                Error(t, Call("print", 17, Enumerable.Range(0, 17).Select(i => "PUSH INT " + i).ToArray()),
                    RuntimeErrorId.InvalidOperation));
            Add("NativeBuiltins", "PrintDirectCallRejected", "variable-arity print requires CALL_INDIRECT n", t =>
                Error(t, "PUSH INT 3\nCALL $print\nRETURN", RuntimeErrorId.InvalidInstruction));
            Add("NativeBuiltins", "PrintAtomicFailure", "print never emits partial output if a later cast fails", t => {
                var r = Run(t, Call("print", 2, "PUSH INT 1", "PUSH INT 123456789"), heap: 2);
                DILTestHarness.ExpectFault(t, r, RuntimeErrorId.OutOfMemory);
                t.Equal(0, r.Output.Count, "no partial output");
            });
            Add("NativeBuiltins", "PrintOOM", "print conversion observes heap limits", t =>
                Error(t, Call("print", 1, "PUSH INT 123456789"), RuntimeErrorId.OutOfMemory, heap: 2));
            Add("NativeBuiltins", "NativeResultPreservesCallerTemporaries", "native call preserves caller's stack base", t =>
                Value(t, "PUSH INT 10\nPUSH_FUNCTION abs\nPUSH INT -7\nCALL_INDIRECT 1\nADD\nRETURN", DataTypeId.Int, 17));
            Add("NativeBuiltins", "NativeViaGlobal", "native function references survive globals", t => {
                string code = "# GLOBAL : action\n" + Entry(
                    "PUSH_FUNCTION abs\nSTORE_GLOBAL $action\nLOAD_GLOBAL $action\nPUSH INT -20\nCALL_INDIRECT 1\nRETURN");
                var program = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string>{ ["test"] = code });
                var r = DILTestHarness.Run(t, DILTestHarness.MakeVM(program));
                t.Equal(VMExecutionStatus.Completed, r.Status, "status");
                t.Equal(20L, unchecked((long)r.Value.value), "global native call");
            });
            Add("NativeBuiltins", "NativeViaList", "native function references survive collections", t =>
                Value(t, "PUSH_FUNCTION abs\nLIST 1\nPUSH INT 0\nGET\nPUSH INT -9\nCALL_INDIRECT 1\nRETURN", DataTypeId.Int, 9));
            Add("NativeBuiltins", "NativeViaDictionary", "native functions can be dictionary keys", t =>
                Value(t, "PUSH_FUNCTION abs\nPUSH INT 71\nDICT 1\nPUSH_FUNCTION abs\nGET\nRETURN", DataTypeId.Int, 71));
            Add("NativeBuiltins", "NativeEqualByIdentity", "native function id is stable across loads", t =>
                Value(t, "PUSH_FUNCTION abs\nPUSH_FUNCTION abs\nREFERENCE_EQUALS\nRETURN", DataTypeId.Bool, 1));
            Add("NativeBuiltins", "DifferentNativeIdentity", "distinct native functions compare unequal", t =>
                Value(t, "PUSH_FUNCTION abs\nPUSH_FUNCTION min\nVALUE_EQUALS\nRETURN", DataTypeId.Bool, 0));
            Add("NativeBuiltins", "DirectCall", "CALL $abs resolves native fixed-arity function", t =>
                Value(t, "PUSH INT -18\nCALL $abs\nRETURN", DataTypeId.Int, 18));
            Add("NativeBuiltins", "DirectRangeRejected", "variable-arity native requires CALL_INDIRECT", t =>
                Error(t, "PUSH INT 3\nCALL $range\nRETURN", RuntimeErrorId.InvalidInstruction));
            Add("NativeBuiltins", "AbsInt", "abs(-42) uses Int", t =>
                Value(t, Call("abs", 1, "PUSH INT -42"), DataTypeId.Int, 42));
            Add("NativeBuiltins", "AbsDecimal", "abs(-2.75) keeps Decimal", t =>
                Value(t, Call("abs", 1, "PUSH DECIMAL -2.75"), DataTypeId.Decimal, 27500));
            Add("NativeBuiltins", "AbsMinimumOverflow", "abs(Int64.MinValue) reports overflow", t =>
                Error(t, Call("abs", 1, "PUSH INT -9223372036854775808"), RuntimeErrorId.Overflow));
            Add("NativeBuiltins", "AbsTypeMismatch", "abs rejects strings", t =>
                Error(t, Call("abs", 1, "PUSH STRING \"x\""), RuntimeErrorId.InvalidOperation));
            Add("NativeBuiltins", "MinInt", "min(Int,Int)", t =>
                Value(t, Call("min", 2, "PUSH INT 8", "PUSH INT -3"), DataTypeId.Int, -3));
            Add("NativeBuiltins", "MaxInt", "max(Int,Int)", t =>
                Value(t, Call("max", 2, "PUSH INT 8", "PUSH INT -3"), DataTypeId.Int, 8));
            Add("NativeBuiltins", "MinMixed", "min compares Int and Decimal exactly", t =>
                Value(t, Call("min", 2, "PUSH INT 4", "PUSH DECIMAL 4.1"), DataTypeId.Int, 4));
            Add("NativeBuiltins", "MaxMixed", "max retains chosen numeric type", t =>
                Value(t, Call("max", 2, "PUSH INT 4", "PUSH DECIMAL 4.1"), DataTypeId.Decimal, 41000));
            Add("NativeBuiltins", "MaxTieKeepsFirstType", "max equal numeric values retains the first argument", t =>
                Value(t, Call("max", 2, "PUSH INT 4", "PUSH DECIMAL 4.0"), DataTypeId.Int, 4));
            Add("NativeBuiltins", "ExtremumWrongType", "min rejects nonnumeric arguments", t =>
                Error(t, Call("min", 2, "PUSH INT 1", "PUSH NONE"), RuntimeErrorId.InvalidOperation));
            Add("NativeBuiltins", "ExtremumLargeInt", "min Int64 comparison does not overflow", t =>
                Value(t, Call("min", 2, "PUSH INT 9223372036854775807", "PUSH INT -9223372036854775808"),
                    DataTypeId.Int, long.MinValue));
            Add("NativeBuiltins", "ClampLow", "clamp chooses lower bound", t =>
                Value(t, Call("clamp", 3, "PUSH INT -8", "PUSH INT 0", "PUSH INT 10"), DataTypeId.Int, 0));
            Add("NativeBuiltins", "ClampHigh", "clamp chooses upper bound", t =>
                Value(t, Call("clamp", 3, "PUSH INT 18", "PUSH INT 0", "PUSH INT 10"), DataTypeId.Int, 10));
            Add("NativeBuiltins", "ClampMiddle", "clamp leaves inside values unchanged", t =>
                Value(t, Call("clamp", 3, "PUSH DECIMAL 2.5", "PUSH INT 0", "PUSH INT 10"), DataTypeId.Decimal, 25000));
            Add("NativeBuiltins", "ClampBadBounds", "clamp rejects reversed bounds", t =>
                Error(t, Call("clamp", 3, "PUSH INT 4", "PUSH INT 10", "PUSH INT 0"), RuntimeErrorId.InvalidOperation));
            Add("NativeBuiltins", "BoolNone", "bool(None) is false", t =>
                Value(t, Call("bool", 1, "PUSH NONE"), DataTypeId.Bool, 0));
            Add("NativeBuiltins", "BoolEmptyList", "bool([]) is false", t =>
                Value(t, Call("bool", 1, "LIST 0"), DataTypeId.Bool, 0));
            Add("NativeBuiltins", "BoolEmptyString", "bool(empty string) is false", t =>
                Value(t, Call("bool", 1, "PUSH STRING \"\""), DataTypeId.Bool, 0));
            Add("NativeBuiltins", "BoolNonEmptyString", "bool(non-empty string) is true", t =>
                Value(t, Call("bool", 1, "PUSH STRING \"0\""), DataTypeId.Bool, 1));
            Add("NativeBuiltins", "BoolFunction", "bool(function) is true even for id 0", t =>
                Value(t, Call("bool", 1, "PUSH_FUNCTION abs"), DataTypeId.Bool, 1));
            Add("NativeBuiltins", "IntBool", "int(True) is 1", t =>
                Value(t, Call("int", 1, "PUSH BOOL TRUE"), DataTypeId.Int, 1));
            Add("NativeBuiltins", "DecimalBool", "decimal(True) is 1.0000", t =>
                Value(t, Call("decimal", 1, "PUSH BOOL TRUE"), DataTypeId.Decimal, 10000));
            Add("NativeBuiltins", "DecimalInt", "decimal(2) is 2.0000", t =>
                Value(t, Call("decimal", 1, "PUSH INT 2"), DataTypeId.Decimal, 20000));
            Add("NativeBuiltins", "IntDecimal", "int(-1.5) truncates toward zero", t =>
                Value(t, Call("int", 1, "PUSH DECIMAL -1.5"), DataTypeId.Int, -1));
            Add("NativeBuiltins", "IntText", "int(String) parses invariant integer", t =>
                Value(t, Call("int", 1, "PUSH STRING \" -123 \""), DataTypeId.Int, -123));
            Add("NativeBuiltins", "DecimalText", "decimal(String) parses invariant decimal", t =>
                Value(t, Call("decimal", 1, "PUSH STRING \"3.14\""), DataTypeId.Decimal, 31400));
            Add("NativeBuiltins", "IntMalformedText", "int(String) reports value-specific cast failure", t =>
                Error(t, Call("int", 1, "PUSH STRING \"abc\""), RuntimeErrorId.InvalidCastValue));
            Add("NativeBuiltins", "DecimalMalformedText", "decimal(String) reports value-specific cast failure", t =>
                Error(t, Call("decimal", 1, "PUSH STRING \"NaN\""), RuntimeErrorId.InvalidCastValue));
            Add("NativeBuiltins", "DecimalOverflow", "decimal(Int64.MaxValue) reports fixed-point overflow", t =>
                Error(t, Call("decimal", 1, "PUSH INT 9223372036854775807"), RuntimeErrorId.Overflow));
            Add("NativeBuiltins", "StrInt", "str(Int) allocates a Directiva String", t => {
                var r = Run(t, Call("str", 1, "PUSH INT 123"));
                t.Equal(VMExecutionStatus.Completed, r.Status, "status");
                t.Equal(DataTypeId.String, r.Value.dataType, "type");
                t.Check(r.Value.TryGetHeapReference(out uint reference), "reference");
                t.Equal("123", ((StringHeapObject)r.VM.Memory.GetHeap(reference)).Value, "string value");
            });
            Add("NativeBuiltins", "StrNoExtraAlloc", "str(String) is identity and creates no duplicate", t => {
                var r = Run(t, Call("str", 1, "PUSH STRING \"abc\""));
                t.Equal(VMExecutionStatus.Completed, r.Status, "status");
                t.Equal(1, r.VM.Memory.Heap.ObjectCount, "one String heap object");
            });
            Add("NativeBuiltins", "LengthList", "len(List) uses existing length operation", t =>
                Value(t, Call("len", 1, "PUSH INT 5\nPUSH INT 6\nLIST 2"), DataTypeId.Int, 2));
            Add("NativeBuiltins", "LengthTuple", "len(Tuple) uses existing length operation", t =>
                Value(t, Call("len", 1, "PUSH NONE\nTUPLE 1"), DataTypeId.Int, 1));
            Add("NativeBuiltins", "LengthRange", "len(Range) evaluates lazily", t =>
                Value(t, Call("len", 1, "PUSH INT 100\nRANGE 1"), DataTypeId.Int, 100));
            Add("NativeBuiltins", "LengthString", "len(String) counts code points", t =>
                Value(t, Call("len", 1, "PUSH STRING \"abc\""), DataTypeId.Int, 3));
            Add("NativeBuiltins", "LengthUnsupported", "len(None) rejects unsupported type", t =>
                Error(t, Call("len", 1, "PUSH NONE"), RuntimeErrorId.InvalidOperation));
            Add("NativeBuiltins", "RangeOne", "range(stop) creates a range", t => {
                var r = Run(t, Call("range", 1, "PUSH INT 1000000"), heap: 4);
                t.Equal(VMExecutionStatus.Completed, r.Status, "status");
                t.Equal(DataTypeId.Range, r.Value.dataType, "type");
                t.Equal(4UL, r.VM.Memory.Heap.AllocatedSize, "constant memory units");
            });
            Add("NativeBuiltins", "RangeTwo", "range(start,stop) length", t =>
                Value(t, "PUSH_FUNCTION range\nPUSH INT 3\nPUSH INT 8\nCALL_INDIRECT 2\nLENGTH\nRETURN", DataTypeId.Int, 5));
            Add("NativeBuiltins", "RangeThree", "range(start,stop,step) iteration", t => {
                var r = Run(t, "PUSH_FUNCTION range\nPUSH INT 1\nPUSH INT 7\nPUSH INT 2\n" +
                    "CALL_INDIRECT 3\nITER_INIT\nloop:\nITER_NEXT end\nPRINT\nJUMP loop\nend:\nPUSH NONE\nRETURN");
                t.Equal(VMExecutionStatus.Completed, r.Status, "status");
                t.Equal("1,3,5", string.Join(",", r.Output), "iterated values");
            });
            Add("NativeBuiltins", "RangeZeroStep", "range zero step faults", t =>
                Error(t, Call("range", 3, "PUSH INT 1", "PUSH INT 10", "PUSH INT 0"), RuntimeErrorId.InvalidOperation));
            Add("NativeBuiltins", "RangeWrongType", "range rejects Decimal inputs", t =>
                Error(t, Call("range", 1, "PUSH DECIMAL 2.5"), RuntimeErrorId.InvalidOperation));
            Add("NativeBuiltins", "RangeOutOfMemory", "range allocation observes logical heap limit", t =>
                Error(t, Call("range", 1, "PUSH INT 10"), RuntimeErrorId.OutOfMemory, heap: 3));
            Add("NativeBuiltins", "RangeBadArity", "range requires one to three arguments", t =>
                Error(t, Call("range", 0), RuntimeErrorId.InvalidOperation));
            Add("NativeBuiltins", "UninitializedArg", "uninitialized locals fault before a native call", t =>
                DILTestHarness.ExpectFault(t,
                    Run(t, "PUSH_FUNCTION abs\nLOAD_LOCAL 0\nCALL_INDIRECT 1\nRETURN", locals: 1),
                    RuntimeErrorId.VariableUsedBeforeInitialization));
            Add("NativeBuiltins", "NativeHeapGC", "native returned range survives actual GC", t => {
                var r = Run(t, "PUSH_FUNCTION range\nPUSH INT 10\nCALL_INDIRECT 1\n" +
                    "STORE_LOCAL 0\nPUSH STRING \"01234567890123456789\"\nPOP\n" +
                    "PUSH STRING \"abcdefghijabcdefghij\"\nPOP\n" +
                    "LOAD_LOCAL 0\nLENGTH\nRETURN", locals: 1, heap: 12);
                t.Equal(VMExecutionStatus.Completed, r.Status, "status");
                t.Check(r.VM.Memory.GarbageCollectionCount > 0, "GC actually executed");
                t.Equal(10L, unchecked((long)r.Value.value), "range survived collection");
            });

            // Import namespace resolution, call id stability, script/native interactions.
            Add("NativeLinking", "BuiltinsWithoutImports", "globals are available without # IMPORT", t => {
                var code = Compile(t, Call("abs", 1, "PUSH INT -1"));
                t.Check(code.NativeFunctions.Any(f => f.QualifiedName == "abs"), "builtins bound");
            });
            Add("NativeLinking", "MathNotImported", "math functions are not auto-imported", t =>
                DILTestHarness.ExpectParseFailure(t, Entry(Call("math.sin", 1, "PUSH INT 0")), "must be imported"));
            Add("NativeLinking", "MissingModuleName", "missing imports identify the missing module", t => {
                try
                {
                    Compile(t, "PUSH NONE\nRETURN", "# IMPORT : nonexistent\n");
                    throw new DirectivaTestAssertionException("Expected missing module parse error.");
                }
                catch (DILParseException ex)
                {
                    t.Equal("nonexistent", ex.ModuleName, "missing module name");
                    t.Check(ex.LineNumber >= 0, "parse location");
                    t.Contains("could not be found", ex.Message, "diagnostic");
                }
            });
            Add("NativeLinking", "ImportNativeWithoutSource", "# IMPORT math requires no .dil source", t => {
                var code = Compile(t, Call("math.sin", 1, "PUSH INT 0"), "# IMPORT : math\n");
                t.Equal(1, code.Functions.Count, "script function count");
                t.Check(code.NativeFunctions.Any(n => n.QualifiedName == "math.sin"), "native function bound");
            });
            Add("NativeLinking", "NativeRootReserved", "native root cannot shadow user module", t => {
                try {
                    new DILParser(new DILMemorySource(new Dictionary<string,string> { ["math"] = Entry("PUSH NONE\nRETURN") })).Parse("math");
                    throw new DirectivaTestAssertionException("Native root should have been rejected");
                } catch (DILParseException ex) { t.Contains("reserved", ex.Message, "diagnostic"); }
            });
            Add("NativeLinking", "NativeConstantRequiresImport", "PUSH_NATIVE constants require corresponding import", t =>
                DILTestHarness.ExpectParseFailure(t, Entry("PUSH_NATIVE math.pi\nRETURN"), "must be imported"));
            Add("NativeLinking", "UnknownNativeName", "PUSH_NATIVE rejects missing native members", t =>
                DILTestHarness.ExpectParseFailure(t, Entry("PUSH_NATIVE math.unknown\nRETURN", "# IMPORT : math\n"), "Unknown native symbol"));
            Add("NativeLinking", "NativeMathFunctionValue", "PUSH_NATIVE math.sin yields Function", t => {
                var r = Run(t, "PUSH_NATIVE math.sin\nRETURN", "# IMPORT : math\n");
                t.Equal(DataTypeId.Function, r.Value.dataType, "type");
                t.Check(r.Value.TryGetFunctionId(out int id) && r.VM.Code.TryGetNativeFunction(id, out _), "resolves native");
            });
            Add("NativeLinking", "BuiltinNativeValue", "PUSH_NATIVE abs works without import", t => {
                var r = Run(t, "PUSH_NATIVE abs\nPUSH INT -8\nCALL_INDIRECT 1\nRETURN");
                t.Equal(8L, unchecked((long)r.Value.value), "result");
            });
            Add("NativeLinking", "DirectMathCall", "CALL $math.sin is supported for fixed arity", t =>
                Value(t, "PUSH INT 0\nCALL $math.sin\nRETURN", DataTypeId.Decimal, 0, "# IMPORT : math\n"));
            Add("NativeLinking", "QualifiedFunctionIds", "Native function ids follow all script ids", t => {
                var code = Compile(t, "PUSH NONE\nRETURN", "# IMPORT : math\n");
                t.Check(code.NativeFunctions.Min(f => f.Id) >= code.Functions.Max(f => f.Id) + 1,
                    "native IDs appended");
            });
            Add("NativeLinking", "ProgramIdReproducible", "Same sources produce same native ids", t => {
                var a = Compile(t, "PUSH NONE\nRETURN", "# IMPORT : math\n# IMPORT : random\n");
                var b = Compile(t, "PUSH NONE\nRETURN", "# IMPORT : random\n# IMPORT : math\n");
                string aa = string.Join(",", a.NativeFunctions.OrderBy(f=>f.Id).Select(f => f.QualifiedName + ":" + f.Id));
                string bb = string.Join(",", b.NativeFunctions.OrderBy(f=>f.Id).Select(f => f.QualifiedName + ":" + f.Id));
                t.Equal(aa, bb, "native ids independent of import order");
            });
            Add("NativeLinking", "LibrarySymbols", "Module symbol catalog exposes math and random", t => {
                t.Check(NativeLibrary.GetFunctionNames("math").Contains("math.atan2"), "math.atan2 exported");
                t.Check(NativeLibrary.GetFunctionNames("random").Contains("random.randint"), "random.randint exported");
                t.Check(NativeLibrary.GetConstantNames("math").Contains("math.pi"), "math.pi exported");
            });
            Add("NativeLinking", "GlobalShadowsNativeName", "a global named abs retains its global slot despite native registration", t => {
                string code = "# GLOBAL : abs\n" + Entry(
                    "PUSH INT -25\nSTORE_GLOBAL $abs\nPUSH_FUNCTION abs\n" +
                    "LOAD_GLOBAL $abs\nCALL_INDIRECT 1\nRETURN");
                var program = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string>{ ["test"] = code });
                var result = DILTestHarness.Run(t, DILTestHarness.MakeVM(program));
                t.Equal(VMExecutionStatus.Completed, result.Status, "status");
                t.Equal(25L, unchecked((long)result.Value.value), "native called with shadowing global");
            });
            Add("NativeLinking", "ScriptFunctionShadowing", "local user function wins over builtin in PUSH_FUNCTION", t => {
                string source = DILTestHarness.Function("PUSH_FUNCTION abs\nCALL_INDIRECT 0\nRETURN") +
                    DILTestHarness.Function("PUSH INT 93\nRETURN", 0, 0, "abs");
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string,string>{ ["test"] = source });
                var r = DILTestHarness.Run(t, DILTestHarness.MakeVM(code));
                t.Equal(93L, unchecked((long)r.Value.value), "shadowed function");
            });
            Add("NativeLinking", "BuiltinExplicitShadow", "PUSH_NATIVE still selects builtin when local function has same name", t => {
                string source = DILTestHarness.Function("PUSH_NATIVE abs\nPUSH INT -3\nCALL_INDIRECT 1\nRETURN") +
                    DILTestHarness.Function("PUSH INT 93\nRETURN", 0, 0, "abs");
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string,string>{ ["test"] = source });
                var r = DILTestHarness.Run(t, DILTestHarness.MakeVM(code));
                t.Equal(3L, unchecked((long)r.Value.value), "builtin invoked");
            });
            Add("NativeLinking", "RuntimeBudgetNativeStep", "one native invocation costs one VM instruction step", t => {
                var r = Run(t, Call("abs", 1, "PUSH INT -4"));
                t.Equal(4, r.Steps, "push function, push value, native call, return");
            });
            Add("NativeLinking", "LegacyInstructionIds", "native functions do not renumber existing bytecode ids", t => {
                t.Equal(29, (int)InstructionId.CallIndirect, "indirect call opcode");
                t.Equal(32, (int)InstructionId.IterNext, "iteration opcode");
                t.Equal(13, (int)DataTypeId.Function, "function type id");
            });

            // Numeric math returns DTDecimal fixed-point (4 fractional digits).
            string m = "# IMPORT : math\n";
            Add("NativeMath", "SinZero", "sin(0) = 0", t => Value(t, Call("math.sin", 1, "PUSH INT 0"), DataTypeId.Decimal, 0, m));
            Add("NativeMath", "CosZero", "cos(0) = 1", t => Value(t, Call("math.cos", 1, "PUSH INT 0"), DataTypeId.Decimal, 10000, m));
            Add("NativeMath", "TanZero", "tan(0) = 0", t => Value(t, Call("math.tan", 1, "PUSH INT 0"), DataTypeId.Decimal, 0, m));
            Add("NativeMath", "AsinOne", "asin(1) approximates pi/2", t => Value(t, Call("math.asin", 1, "PUSH INT 1"), DataTypeId.Decimal, 15708, m));
            Add("NativeMath", "AcosOne", "acos(1) = 0", t => Value(t, Call("math.acos", 1, "PUSH INT 1"), DataTypeId.Decimal, 0, m));
            Add("NativeMath", "AtanOne", "atan(1) approximates pi/4", t => Value(t, Call("math.atan", 1, "PUSH INT 1"), DataTypeId.Decimal, 7854, m));
            Add("NativeMath", "Atan2", "atan2(y,x) uses correct argument order", t => Value(t, Call("math.atan2", 2, "PUSH INT 1", "PUSH INT 0"), DataTypeId.Decimal, 15708, m));
            Add("NativeMath", "SqrtNine", "sqrt(9) = 3", t => Value(t, Call("math.sqrt", 1, "PUSH INT 9"), DataTypeId.Decimal, 30000, m));
            Add("NativeMath", "Hypot", "hypot(3,4) = 5", t => Value(t, Call("math.hypot", 2, "PUSH INT 3", "PUSH INT 4"), DataTypeId.Decimal, 50000, m));
            Add("NativeMath", "Radians", "radians(180) approximates pi", t => Value(t, Call("math.radians", 1, "PUSH INT 180"), DataTypeId.Decimal, 31416, m));
            Add("NativeMath", "DegreesZero", "degrees(0) = 0", t => Value(t, Call("math.degrees", 1, "PUSH INT 0"), DataTypeId.Decimal, 0, m));
            Add("NativeMath", "FloorNegative", "floor(-1.2) = -2", t => Value(t, Call("math.floor", 1, "PUSH DECIMAL -1.2"), DataTypeId.Int, -2, m));
            Add("NativeMath", "CeilNegative", "ceil(-1.2) = -1", t => Value(t, Call("math.ceil", 1, "PUSH DECIMAL -1.2"), DataTypeId.Int, -1, m));
            Add("NativeMath", "FloorPositive", "floor(1.2) = 1", t => Value(t, Call("math.floor", 1, "PUSH DECIMAL 1.2"), DataTypeId.Int, 1, m));
            Add("NativeMath", "CeilPositive", "ceil(1.2) = 2", t => Value(t, Call("math.ceil", 1, "PUSH DECIMAL 1.2"), DataTypeId.Int, 2, m));
            Add("NativeMath", "Pi", "math.pi is a four-decimal constant", t => Value(t, "PUSH_NATIVE math.pi\nRETURN", DataTypeId.Decimal, 31416, m));
            Add("NativeMath", "Tau", "math.tau constant", t => Value(t, "PUSH_NATIVE math.tau\nRETURN", DataTypeId.Decimal, 62832, m));
            Add("NativeMath", "E", "math.e constant", t => Value(t, "PUSH_NATIVE math.e\nRETURN", DataTypeId.Decimal, 27183, m));
            Add("NativeMath", "AsinOutsideDomain", "asin(2) rejects domain violation", t => Error(t, Call("math.asin", 1, "PUSH INT 2"), RuntimeErrorId.InvalidOperation, m));
            Add("NativeMath", "SqrtNegative", "sqrt(-1) rejects domain violation", t => Error(t, Call("math.sqrt", 1, "PUSH INT -1"), RuntimeErrorId.InvalidOperation, m));
            Add("NativeMath", "SinWrongType", "sin(String) rejects type mismatch", t => Error(t, Call("math.sin", 1, "PUSH STRING \"x\""), RuntimeErrorId.InvalidOperation, m));
            Add("NativeMath", "SqrtLargeFinite", "sqrt(Int64.MaxValue) produces a finite decimal", t => {
                var result = Run(t, Call("math.sqrt", 1, "PUSH INT 9223372036854775807"), m);
                t.Equal(VMExecutionStatus.Completed, result.Status, "status");
                t.Equal(DataTypeId.Decimal, result.Value.dataType, "type");
                t.Check(result.Value.value > 0, "positive sqrt");
            });
            Add("NativeMath", "TrigFunctionValue", "math.sin can be returned as a first-class Function", t => {
                var r = Run(t, "PUSH_FUNCTION math.sin\nRETURN", m);
                t.Equal(VMExecutionStatus.Completed, r.Status, "status");
                t.Equal(DataTypeId.Function, r.Value.dataType, "type");
            });
            Add("NativeMath", "TrigWithDecimal", "sin uses decimal inputs", t => Value(t, Call("math.sin", 1, "PUSH DECIMAL 0.0"), DataTypeId.Decimal, 0, m));

            // Deterministic PRNG state is shared by VMs executing the same CodeContext.
            string rmod = "# IMPORT : random\n";
            Add("NativeRandom", "RandomBounds", "random() returns Decimal 0 <= x < 1", t => {
                var r = Run(t, Call("random.random", 0), rmod);
                t.Equal(VMExecutionStatus.Completed, r.Status, "status");
                t.Equal(DataTypeId.Decimal, r.Value.dataType, "type");
                t.Check(r.Value.value < 10000, "random bounds");
            });
            Add("NativeRandom", "RandIntSingle", "randint(a,a) returns a", t => Value(t,
                Call("random.randint", 2, "PUSH INT -5", "PUSH INT -5"), DataTypeId.Int, -5, rmod));
            Add("NativeRandom", "RandIntFullInt64", "randint supports full signed Int64 interval", t => {
                var r = Run(t, Call("random.randint", 2,
                    "PUSH INT -9223372036854775808", "PUSH INT 9223372036854775807"), rmod);
                t.Equal(VMExecutionStatus.Completed, r.Status, "status");
                t.Equal(DataTypeId.Int, r.Value.dataType, "type");
            });
            Add("NativeRandom", "RandIntBounds", "randint stays within inclusive interval", t => {
                var r = Run(t, Call("random.randint", 2, "PUSH INT 10", "PUSH INT 20"), rmod);
                long n = unchecked((long)r.Value.value);
                t.Check(n >= 10 && n <= 20, "bounds");
            });
            Add("NativeRandom", "RandIntReversed", "randint rejects inverted bounds", t =>
                Error(t, Call("random.randint", 2, "PUSH INT 10", "PUSH INT 9"), RuntimeErrorId.InvalidOperation, rmod));
            Add("NativeRandom", "RandIntWrongType", "randint only accepts integers", t =>
                Error(t, Call("random.randint", 2, "PUSH DECIMAL 0.1", "PUSH INT 10"), RuntimeErrorId.InvalidOperation, rmod));
            Add("NativeRandom", "UniformSingle", "uniform(a,a) yields exactly a", t =>
                Value(t, Call("random.uniform", 2, "PUSH DECIMAL 2.5", "PUSH DECIMAL 2.5"), DataTypeId.Decimal, 25000, rmod));
            Add("NativeRandom", "UniformBounds", "uniform returns fixed decimal in [low,high)", t => {
                var r = Run(t, Call("random.uniform", 2, "PUSH INT 0", "PUSH INT 1"), rmod);
                t.Equal(DataTypeId.Decimal, r.Value.dataType, "type");
                t.Check(r.Value.value < 10000UL, "uniform bound");
            });
            Add("NativeRandom", "UniformReversed", "uniform rejects inverted bounds", t =>
                Error(t, Call("random.uniform", 2, "PUSH INT 10", "PUSH INT 9"), RuntimeErrorId.InvalidOperation, rmod));
            Add("NativeRandom", "UniformOverflow", "uniform out-of-range fixed-point result reports overflow", t =>
                Error(t, Call("random.uniform", 2, "PUSH INT 9223372036854775807", "PUSH INT 9223372036854775807"), RuntimeErrorId.Overflow, rmod));
            Add("NativeRandom", "RandomReproducible", "same seed and schedule yield same results", t => {
                var code = Compile(t, Call("random.randint", 2, "PUSH INT 0", "PUSH INT 100000"), rmod);
                var a = DILTestHarness.Run(t, DILTestHarness.MakeVM(code,
                    memory: new VMMemoryContext(code.GlobalCount, HeapMemory.DefaultMaxSize, 1234UL)));
                var b = DILTestHarness.Run(t, DILTestHarness.MakeVM(code,
                    memory: new VMMemoryContext(code.GlobalCount, HeapMemory.DefaultMaxSize, 1234UL)));
                t.Equal(VMExecutionStatus.Completed, a.Status, "a status");
                t.Equal(VMExecutionStatus.Completed, b.Status, "b status");
                t.Equal(a.Value.value, b.Value.value, "seeded sequence");
            });
            Add("NativeRandom", "RandomSharedMemory", "VMs sharing memory consume one random stream", t => {
                var code = Compile(t, Call("random.randint", 2, "PUSH INT 0", "PUSH INT 10000000"), rmod);
                var memory = new VMMemoryContext(code.GlobalCount, HeapMemory.DefaultMaxSize, 42UL);
                var a = DILTestHarness.Run(t, DILTestHarness.MakeVM(code, memory: memory));
                var b = DILTestHarness.Run(t, DILTestHarness.MakeVM(code, memory: memory));
                memory.ReseedRandom(42UL);
                var c = DILTestHarness.Run(t, DILTestHarness.MakeVM(code, memory: memory));
                t.Equal(VMExecutionStatus.Completed, a.Status, "a status");
                t.Equal(VMExecutionStatus.Completed, b.Status, "b status");
                t.Equal(a.Value.value, c.Value.value, "reset seed repeats first draw");
                t.Check(a.Value.value != b.Value.value, "successive draws differ for the fixed test seed");
            });
            Add("NativeRandom", "RandomNoUnityGlobal", "NativeLibrary random module is separate from simulation scene", t => {
                var code = Compile(t, Call("random.random", 0), rmod);
                t.Check(code.NativeFunctions.Any(fn => fn.QualifiedName == "random.random"), "exported");
                t.Equal(1, code.Functions.Count, "single entry function");
            });
        }
    }
}
