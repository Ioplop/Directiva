using System;
using System.Collections.Generic;
using System.Linq;
using DSCompilation.DScript;
using DSCompilation.DScript.Backend;
using DSCompilation.DScript.Modules;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DScript
{
    /// <summary>Real DScript -> DIL -> CodeContext -> VM integration fixtures.</summary>
    internal static class DScriptBackendTests
    {
        private static DScriptCompiledProgram Compile(DirectivaTestContext t, string main,
            Dictionary<string, string> more = null, string entry = "test")
        {
            var sources = new Dictionary<string, string>(StringComparer.Ordinal) { ["test"] = main };
            if (more != null) foreach (var pair in more) sources.Add(pair.Key, pair.Value);
            foreach (var pair in sources) t.Attach(pair.Key + ".dscript", pair.Value);
            DScriptCompilationResult result = DScriptCompiler.Compile(entry,
                new InMemoryDScriptSourceProvider(sources));
            t.Attach("Compilation diagnostics", string.Join("\n", result.Diagnostics));
            t.Check(result.Success, "DScript compilation: " + string.Join(" | ", result.Diagnostics));
            foreach (var pair in result.Program.DILModules)
                t.Attach(pair.Key + ".dil", pair.Value);
            return result.Program;
        }

        private static VMMemoryContext Memory(DirectivaTestContext t, DScriptCompiledProgram program,
            out DScriptInitializationReport report, int budget = 10000)
        {
            var memory = new VMMemoryContext(program.Code.GlobalCount);
            report = DScriptBootstrapRunner.Initialize(program, memory, budget);
            foreach (DScriptInitializerOutcome step in report.Steps)
                t.Note("Initializer " + step.Initializer.Module + "." + step.Initializer.Variable +
                    ": " + step.Status + " steps=" + step.LogicalSteps + " error=" + step.Error?.Message);
            return memory;
        }

        private static DirectivaVM Execute(DirectivaTestContext t, DScriptCompiledProgram program,
            string function = "test.main", VMMemoryContext memory = null,
            params DataValue[] args)
        {
            t.Check(program.TryGetFunctionId(function, out int id), "Missing compiled function " + function);
            var vm = new DirectivaVM(program.Code, memory ?? new VMMemoryContext(program.Code.GlobalCount),
                null, id, args);
            VMRunResult run = vm.Run(50000);
            t.Note(function + ": status=" + run.Status + " cost=" + run.StepsExecuted +
                " result=" + vm.ReturnValue.dataType + ":" + vm.ReturnValue.value +
                " error=" + vm.Error?.Message);
            t.Equal(VMExecutionStatus.Completed, vm.Status, "VM should complete " + function);
            return vm;
        }

        private static void CheckInt(DirectivaTestContext t, string source, long expected)
        {
            var program = Compile(t, source);
            var memory = Memory(t, program, out DScriptInitializationReport report);
            t.Check(!report.HasErrors, "No initializer should fail.");
            var vm = Execute(t, program, "test.main", memory);
            t.Equal(DataTypeId.Int, vm.ReturnValue.dataType, "return type");
            t.Equal(expected, unchecked((long)vm.ReturnValue.value), "return value");
        }

        private static void CheckBool(DirectivaTestContext t, string source, bool expected)
        {
            var program = Compile(t, source);
            var vm = Execute(t, program);
            t.Equal(DataTypeId.Bool, vm.ReturnValue.dataType, "return type");
            t.Equal(expected, vm.ReturnValue.value != 0, "return value");
        }

        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> action)
                => DScriptTestHarness.Add(tests, "Backend", id, description, action);

            Add("ArithmeticPrecedence", "AST precedence survives DIL emission", t =>
                CheckInt(t, "def main():\n    return 2 + 3 * 4\n", 14));
            Add("IntegerMinimum", "Signed 64-bit minimum is generated without overflow", t =>
                CheckInt(t, "def main():\n    return -9223372036854775808\n", long.MinValue));
            Add("UnaryNegative", "Unary minus is evaluated by VM arithmetic", t =>
                CheckInt(t, "def main():\n    return -(2 + 5)\n", -7));
            Add("Exponent", "Power operator compiles to POWER", t =>
                CheckInt(t, "def main():\n    return 2 ** 3 ** 2\n", 512));
            Add("Decimal", "Decimal literals use the fixed-point VM representation", t =>
            {
                var p = Compile(t, "def main():\n    return 1.25 + 0.50\n");
                var vm = Execute(t, p);
                t.Equal(DataTypeId.Decimal, vm.ReturnValue.dataType, "decimal type");
                t.Equal(17500L, unchecked((long)vm.ReturnValue.value), "scaled decimal result");
            });
            Add("NotAndEquality", "not and != use Bool and VALUE_EQUALS", t =>
                CheckBool(t, "def main():\n    return not (5 != 5)\n", true));
            Add("Relational", "Numeric ordering operation", t =>
                CheckBool(t, "def main():\n    return 7 >= 3\n", true));
            Add("LogicalAndOperand", "and returns its right operand, not Bool", t =>
                CheckInt(t, "def main():\n    return \"hello\" and 7\n", 7));
            Add("LogicalOrOperand", "or returns its right operand, not Bool", t =>
                CheckInt(t, "def main():\n    return 0 or 9\n", 9));
            Add("LogicalAndStops", "and must not evaluate skipped function", t =>
            {
                string src = "shared hits = 0\ndef side():\n    hits += 1\n    return 99\ndef main():\n    return 0 and side()\n";
                var p = Compile(t, src);
                var m = Memory(t, p, out var r);
                var vm = Execute(t, p, "test.main", m);
                t.Equal(0L, unchecked((long)vm.ReturnValue.value), "and result");
                t.Equal(0L, unchecked((long)m.GetGlobal(0).value), "skipped side effect");
            });
            Add("LogicalOrStops", "or must not evaluate skipped function", t =>
            {
                string src = "shared hits = 0\ndef side():\n    hits += 1\n    return 99\ndef main():\n    return 12 or side()\n";
                var p = Compile(t, src);
                var m = Memory(t, p, out var r);
                var vm = Execute(t, p, "test.main", m);
                t.Equal(12L, unchecked((long)vm.ReturnValue.value), "or result");
                t.Equal(0L, unchecked((long)m.GetGlobal(0).value), "skipped side effect");
            });
            Add("Truthiness", "Empty string is falsy and preserves short-circuit semantics", t =>
                CheckInt(t, "def main():\n    return \"\" or 19\n", 19));
            Add("IfElifElse", "Conditional chain with fallthrough and correct jumps", t =>
                CheckInt(t, "def main():\n    n = 3\n    if n < 1:\n        return 0\n    elif n == 3:\n        return 77\n    else:\n        return 2\n", 77));
            Add("IfElse", "A false branch reaches else", t =>
                CheckInt(t, "def main():\n    if False:\n        return 1\n    else:\n        return 2\n", 2));
            Add("WhileLoop", "while increments and tests the condition", t =>
                CheckInt(t, "def main():\n    i = 0\n    while i < 5:\n        i += 1\n    return i\n", 5));
            Add("WhileBreak", "while break jumps to exit", t =>
                CheckInt(t, "def main():\n    i = 0\n    while True:\n        i += 1\n        if i == 4:\n            break\n    return i\n", 4));
            Add("WhileContinue", "continue resumes the while condition", t =>
                CheckInt(t, "def main():\n    i = 0\n    s = 0\n    while i < 5:\n        i += 1\n        if i == 3:\n            continue\n        s += i\n    return s\n", 12));
            Add("ForList", "for over a list keeps iterator on evaluation stack", t =>
                CheckInt(t, "def main():\n    s = 0\n    for n in [1, 2, 3]:\n        s += n\n    return s\n", 6));
            Add("ForRange", "for over range() and native CALL_INDIRECT", t =>
                CheckInt(t, "def main():\n    s = 0\n    for n in range(1, 5):\n        s += n\n    return s\n", 10));
            Add("ForBreak", "break cleans up the retained iterator", t =>
                CheckInt(t, "def main():\n    s = 0\n    for n in [1, 2, 3, 4]:\n        if n == 3:\n            break\n        s += n\n    return s\n", 3));
            Add("ForContinue", "continue preserves the retained iterator", t =>
                CheckInt(t, "def main():\n    s = 0\n    for n in [1, 2, 3]:\n        if n == 2:\n            continue\n        s += n\n    return s\n", 4));
            Add("NestedFor", "nested breaks/continues affect correct iterator only", t =>
                CheckInt(t, "def main():\n    s = 0\n    for x in [1, 2, 3]:\n        for y in [1, 2, 3]:\n            if y == 2:\n                continue\n            if x == 3:\n                break\n            s += x * y\n    return s\n", 12));
            Add("Functions", "Indirect references call compiled functions correctly", t =>
                CheckInt(t, "def twice(x):\n    return x * 2\ndef main():\n    return twice(6)\n", 12));
            Add("FunctionAsValue", "Functions can be assigned then called indirectly", t =>
                CheckInt(t, "def twice(x):\n    return x * 2\ndef main():\n    f = twice\n    return f(6)\n", 12));
            Add("Recursion", "CALL_INDIRECT supports recursion and separate local frames", t =>
                CheckInt(t, "def factorial(n):\n    if n <= 1:\n        return 1\n    return n * factorial(n - 1)\ndef main():\n    return factorial(5)\n", 120));
            Add("ModuleFunction", "Qualified imported function references", t =>
            {
                var p = Compile(t, "import helper as h\ndef main():\n    return h.twice(6)\n",
                    new Dictionary<string, string> { ["helper"] = "def twice(x):\n    return x * 2\n" });
                var vm = Execute(t, p);
                t.Equal(12L, unchecked((long)vm.ReturnValue.value), "imported function result");
            });
            Add("FromImportActual", "From-import links across module graph", t =>
            {
                var p = Compile(t, "from helper import twice\ndef main():\n    return twice(6)\n",
                    new Dictionary<string, string> { ["helper"] = "def twice(x):\n    return x * 2\n" });
                var vm = Execute(t, p);
                t.Equal(12L, unchecked((long)vm.ReturnValue.value), "from-import result");
            });
            Add("NativeBuiltin", "Unqualified builtins are linked as function values", t =>
                CheckInt(t, "def main():\n    return abs(-14)\n", 14));
            Add("NativeMath", "Native module symbol is linked after imports", t =>
                CheckInt(t, "import math\ndef main():\n    return math.floor(5)\n", 5));
            Add("NativeAsValue", "Native functions can be stored in a local slot", t =>
                CheckInt(t, "def main():\n    f = abs\n    return f(-8)\n", 8));
            Add("ListGetSet", "Subscript updates use GET/SET with stack cleanup", t =>
                CheckInt(t, "def main():\n    items = [2, 3]\n    items[1] = 8\n    return items[1]\n", 8));
            Add("ListAugment", "Augmented index assignment preserves original receiver", t =>
                CheckInt(t, "def main():\n    items = [2, 3]\n    items[0] += 7\n    return items[0]\n", 9));
            Add("DictionaryGetSet", "Dictionary literal and indexed writes", t =>
                CheckInt(t, "def main():\n    m = {1: 5}\n    m[1] = 9\n    return m[1]\n", 9));
            Add("TupleIndex", "Tuple literal compiles via TUPLE", t =>
                CheckInt(t, "def main():\n    values = (2, 7, 9)\n    return values[1]\n", 7));
            Add("SetMembership", "in reverses operands without changing evaluation order", t =>
                CheckBool(t, "def main():\n    return 2 in {1, 2, 3}\n", true));
            Add("NotIn", "not in compiles with CONTAINS/NOT", t =>
                CheckBool(t, "def main():\n    return 9 not in [1, 2]\n", true));
            Add("PropertyLength", "Value getters use GET_PROPERTY", t =>
                CheckInt(t, "def main():\n    return [3, 4, 5].length\n", 3));
            Add("CallMethodList", "Member call stays CALL_METHOD, not APPEND operation", t =>
            {
                var p = Compile(t, "def main():\n    a = [1]\n    a.append(5)\n    return a.pop()\n");
                t.Contains("CALL_METHOD append 1", p.DILModules["test"], "list dispatch");
                t.Check(!p.DILModules["test"].Split('\n').Any(line => line.Trim() == "APPEND"),
                    "No static opcode APPEND");
                var vm = Execute(t, p);
                t.Equal(5L, unchecked((long)vm.ReturnValue.value), "list pop");
            });
            Add("CallMethodSet", "Set uses its own pop implementation", t =>
            {
                var p = Compile(t, "def main():\n    a = {5}\n    return a.pop()\n");
                var vm = Execute(t, p);
                t.Equal(5L, unchecked((long)vm.ReturnValue.value), "set pop");
            });
            Add("CallMethodDict", "Dictionary pop(key) shares a name with set/list pop", t =>
            {
                var p = Compile(t, "def main():\n    a = {1: 7}\n    return a.pop(1)\n");
                var vm = Execute(t, p);
                t.Equal(7L, unchecked((long)vm.ReturnValue.value), "dictionary pop");
            });
            Add("DynamicReceiver", "Same call site can dispatch a runtime List or Set", t =>
            {
                var p = Compile(t, "def run(x):\n    return x.pop()\ndef from_list():\n    v = [12]\n    return run(v)\ndef from_set():\n    v = {17}\n    return run(v)\n");
                t.Contains("CALL_METHOD pop 0", p.DILModules["test"], "dynamic call site");
                var memory = Memory(t, p, out var report);
                t.Equal(12L, unchecked((long)Execute(t, p, "test.from_list", memory).ReturnValue.value), "list receiver");
                t.Equal(17L, unchecked((long)Execute(t, p, "test.from_set", memory).ReturnValue.value), "set receiver");
            });
            Add("InvalidMethod", "Missing members fail at runtime rather than in compiler", t =>
            {
                var p = Compile(t, "def main():\n    x = 5\n    return x.noSuchMethod()\n");
                var vm = new DirectivaVM(p.Code, p.FunctionIds["test.main"]);
                vm.Run(5000);
                t.Equal(VMExecutionStatus.Faulted, vm.Status, "fault status");
                t.Equal(RuntimeErrorId.MemberNotFound, vm.Error.Id, "missing runtime method");
            });
            Add("SharedSlots", "Same shared name in different modules uses one canonical slot", t =>
            {
                string src = "shared score = 2\nimport helper\ndef main():\n    return helper.inc() + score\n";
                var p = Compile(t, src, new Dictionary<string, string> {
                    ["helper"] = "shared score\ndef inc():\n    score += 4\n    return score\n" });
                t.Equal(1, p.Code.GlobalCount, "canonical shared count");
                var memory = Memory(t, p, out var report);
                var vm = Execute(t, p, "test.main", memory);
                t.Equal(12L, unchecked((long)vm.ReturnValue.value), "same shared slot");
            });
            Add("SharedInitializerOrder", "Module cycle executes initializer steps in import/source order", t =>
            {
                string src = "shared a = 10\nimport helper as mod\nshared c = b + 5\nshared b\ndef main():\n    return c\n";
                var p = Compile(t, src, new Dictionary<string, string> {
                    ["helper"] = "shared a\nshared b = a * 2\nimport test\n" });
                t.Equal("a,b,c", string.Join(",", p.Initializers.Select(step => step.Variable)), "init plan");
                var m = Memory(t, p, out var report);
                t.Check(!report.HasErrors, "All shared steps succeed.");
                var vm = Execute(t, p, "test.main", m);
                t.Equal(25L, unchecked((long)vm.ReturnValue.value), "c=a*2+5");
            });
            Add("InitFailureContinues", "A failed initializer cannot stop subsequent steps", t =>
            {
                var p = Compile(t, "shared bad = 1 / 0\nshared good = 7\ndef main():\n    return good\n");
                var m = Memory(t, p, out var report);
                t.Equal(2, report.Steps.Count, "number of initializers");
                t.Check(report.HasErrors, "First initializer should fail.");
                t.Equal(VMExecutionStatus.Faulted, report.Steps[0].Status, "failing init");
                t.Equal(VMExecutionStatus.Completed, report.Steps[1].Status, "recovering init");
                var vm = Execute(t, p, "test.main", m);
                t.Equal(7L, unchecked((long)vm.ReturnValue.value), "following initializer value");
                t.Equal(DataTypeId.Uninitialized, m.GetGlobal(p.SharedSlots.First(s => s.Name == "bad").Slot).dataType,
                    "failed shared remains uninitialized");
            });
            Add("InitBudget", "Nonterminating initializer is aborted and later steps proceed", t =>
            {
                var p = Compile(t, "shared a = forever()\nshared b = 4\ndef forever():\n    while True:\n        pass\ndef main():\n    return b\n");
                var m = Memory(t, p, out var report, 30);
                t.Check(report.HasErrors, "Must report budget exhaustion.");
                t.Equal(VMExecutionStatus.Faulted, report.Steps[0].Status, "bounded init");
                t.Equal(VMExecutionStatus.Completed, report.Steps[1].Status, "later init");
                t.Equal(4L, unchecked((long)Execute(t, p, "test.main", m).ReturnValue.value), "recovered value");
            });
            Add("NoInitDefault", "Uninitialized declaration reserves a slot but no init step", t =>
            {
                var p = Compile(t, "shared x\ndef main():\n    return 9\n");
                t.Equal(0, p.Initializers.Count, "initializers");
                var m = Memory(t, p, out var report);
                t.Equal(DataTypeId.Uninitialized, m.GetGlobal(0).dataType, "uninitialized shared");
                t.Equal(9L, unchecked((long)Execute(t, p, "test.main", m).ReturnValue.value), "entrypoint");
            });
            Add("MemoryIsolation", "Compiled code reused with separate memory for two teams", t =>
            {
                var p = Compile(t, "shared n = 1\ndef main():\n    n += 1\n    return n\n");
                var m1 = Memory(t, p, out var r1);
                var m2 = Memory(t, p, out var r2);
                t.Equal(2L, unchecked((long)Execute(t, p, "test.main", m1).ReturnValue.value), "team1 first");
                t.Equal(3L, unchecked((long)Execute(t, p, "test.main", m1).ReturnValue.value), "team1 second");
                t.Equal(2L, unchecked((long)Execute(t, p, "test.main", m2).ReturnValue.value), "team2 independent");
            });
            Add("BootstrapOnce", "Same memory refuses bootstrap twice", t =>
            {
                var p = Compile(t, "shared x = 1\ndef main():\n    return x\n");
                var m = Memory(t, p, out var r);
                bool refused = false;
                try { DScriptBootstrapRunner.Initialize(p, m); }
                catch (InvalidOperationException) { refused = true; }
                t.Check(refused, "Memory must refuse duplicate initialization.");
            });
            Add("DebugOrigin", "DEBUG_* always reflects the original DScript source", t =>
            {
                var p = Compile(t, "def main():\n    return 34\n");
                t.Contains("DEBUG_FILE \"test.dscript\"", p.DILModules["test"], "source path");
                t.Contains("DEBUG_LINE 1", p.DILModules["test"], "source line");
                var vm = Execute(t, p);
                t.Equal("test.dscript", vm.Debug.File, "debug file");
                t.Equal(1, vm.Debug.Line, "debug zero-based line");
            });
            Add("DebugNoDoubleInjection", "Auto-debug mode preserves explicit original spans", t =>
            {
                var p = Compile(t, "def main():\n    return 3\n");
                var code = new DSCompilation.DIL.DILParser(
                    new Directiva.EditorTests.DIL.DILMemorySource(p.DILModules.ToDictionary(k => k.Key, v => v.Value)),
                    new DSCompilation.DIL.DILParserOptions(generateDebugMetadata: true)).Parse(p.BootstrapModule);
                var f = code.Functions.Single(fn => fn.Name == "test.main");
                t.Equal(p.Code.GetFunction(p.FunctionIds["test.main"]).InstructionCount,
                    f.InstructionCount, "explicit instructions are not injected a second time");
                t.Check(f.Instructions.ToArray().Any(i => i.Id == InstructionId.DebugFile && i.TextOperand == "test.dscript"),
                    "debug points at original DScript, not generated DIL");
            });
            Add("LogicalCostZero", "All DEBUG instructions have zero VM budget cost", t =>
            {
                var p = Compile(t, "def main():\n    return 123\n");
                var vm = new DirectivaVM(p.Code, p.FunctionIds["test.main"]);
                var result = vm.Run(2); // PUSH + RETURN, despite inserted DEBUG_*.
                t.Equal(VMExecutionStatus.Completed, result.Status, "completion in two logical steps");
                t.Equal(2, result.StepsExecuted, "logical budget");
            });
            Add("CompilerRejectsInvalid", "Frontend errors never produce a partial program", t =>
            {
                var source = new Dictionary<string, string> { ["test"] = "def main():\n    return unknown\n" };
                var result = DScriptCompiler.Compile("test", new InMemoryDScriptSourceProvider(source));
                t.Check(!result.Success, "Compilation should fail.");
                t.Check(result.Program == null, "No partial code may escape.");
                t.Check(result.Diagnostics.Count > 0, "Source diagnostic must survive.");
            });
            Add("CompilerLocalLimit", "Backend reports an explicit error when temp limit is reached", t =>
            {
                var source = new Dictionary<string, string> { ["test"] =
                    "def main():\n    a = 0 and 5\n    return a\n" };
                var result = DScriptCompiler.Compile("test", new InMemoryDScriptSourceProvider(source),
                    maxLocalSlots: 1);
                t.Check(!result.Success, "Too few local slots should fail.");
                t.Check(result.Diagnostics.Any(d => d.Code == "DS5001"), "Backend limit diagnostic.");
            });
        }
    }
}
