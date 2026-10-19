using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DSCompilation.DIL;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    /// <summary>In-memory source provider: never reads/writes a player's DirectivaScripts directory.</summary>
    internal sealed class DILMemorySource : IDILSourceProvider
    {
        private readonly Dictionary<string, string> modules;
        internal DILMemorySource(IDictionary<string, string> sources)
        {
            modules = new Dictionary<string, string>(sources, StringComparer.Ordinal);
        }
        public bool TryReadModule(string moduleName, out string text)
        {
            return modules.TryGetValue(moduleName, out text);
        }
    }

    internal sealed class DILObservation
    {
        internal DirectivaVM VM;
        internal VMExecutionStatus Status;
        internal RuntimeError Error;
        internal DataValue Value;
        internal int Steps;
        internal readonly List<string> Output = new List<string>();
        internal readonly List<string> LastSteps = new List<string>();
        internal bool BudgetExhausted;
    }

    internal static class DILTestHarness
    {
        internal static string Function(string body, int locals = 0, int parameters = 0,
            string functionName = "main")
        {
            return "# FUNCTION : " + functionName + "\n" +
                   "# PARAMS : " + parameters.ToString(CultureInfo.InvariantCulture) + "\n" +
                   "# LOCALS : " + locals.ToString(CultureInfo.InvariantCulture) + "\n" +
                   body.Trim() + "\n# END_FUNCTION\n";
        }

        internal static CodeContext Parse(DirectivaTestContext t, string body, int locals = 0,
            int parameters = 0, DILParserOptions options = null)
        {
            return ParseModules(t, "test", new Dictionary<string, string> {
                ["test"] = Function(body, locals, parameters)
            }, options);
        }

        internal static CodeContext ParseModules(DirectivaTestContext t, string root,
            IDictionary<string, string> modules, DILParserOptions options = null)
        {
            foreach (var pair in modules.OrderBy(p => p.Key, StringComparer.Ordinal))
                t.Attach("DIL source: " + pair.Key + ".dil", pair.Value);
            t.Note("Parser root=" + root + ", DebugMetadata=" + (options != null && options.GenerateDebugMetadata));
            return new DILParser(new DILMemorySource(modules), options).Parse(root);
        }

        internal static DirectivaVM MakeVM(CodeContext code, ulong heap = HeapMemory.DefaultMaxSize,
            VMOptions options = null, VMMemoryContext memory = null,
            int? entryFunctionId = null, params DataValue[] args)
        {
            if (memory == null) memory = new VMMemoryContext(code.GlobalCount, heap);
            return entryFunctionId.HasValue
                ? new DirectivaVM(code, memory, options, entryFunctionId.Value, args)
                : new DirectivaVM(code, memory, options, args);
        }

        internal static DirectivaVM MakeVM(DirectivaTestContext t, string body, int locals = 0,
            int parameters = 0, ulong heap = HeapMemory.DefaultMaxSize, VMOptions options = null,
            DILParserOptions parserOptions = null, params DataValue[] args)
        {
            return MakeVM(Parse(t, body, locals, parameters, parserOptions), heap, options,
                null, null, args);
        }

        /// <summary>Hard cap avoids infinite loops locking up the Editor. Last 35 VM steps are retained.</summary>
        internal static DILObservation Run(DirectivaTestContext t, DirectivaVM vm, int budget = 2000)
        {
            if (budget < 1 || budget > 150000)
                throw new ArgumentOutOfRangeException(nameof(budget));
            var observed = new DILObservation { VM = vm };
            Action<string> listener = s => observed.Output.Add(s);
            vm.Printed += listener;
            var recent = new Queue<string>();
            try
            {
                while (vm.Status == VMExecutionStatus.Running && observed.Steps < budget)
                {
                    string line = StepText(vm);
                    if (recent.Count == 35) recent.Dequeue();
                    recent.Enqueue(line);
                    vm.Advance();
                    observed.Steps++;
                }
            }
            finally
            {
                vm.Printed -= listener;
            }
            observed.Status = vm.Status;
            observed.Error = vm.Error;
            observed.Value = vm.ReturnValue;
            observed.BudgetExhausted = vm.Status == VMExecutionStatus.Running && observed.Steps == budget;
            observed.LastSteps.AddRange(recent);
            t.Note("VM: status=" + observed.Status + ", steps=" + observed.Steps +
                   ", budget=" + budget + ", exhausted=" + observed.BudgetExhausted +
                   ", return=" + FormatValue(observed.Value) +
                   ", error=" + (vm.Error == null ? "<none>" : vm.Error.Id + ": " + vm.Error.Message) +
                   ", heap=" + vm.Memory.Heap.AllocatedSize + "/" + vm.Memory.Heap.MaxSize +
                   " units; objects=" + vm.Memory.Heap.ObjectCount +
                   "; GC=" + vm.Memory.GarbageCollectionCount +
                   "; stack=" + vm.EvaluationStackCount + "; callDepth=" + vm.CallDepth);
            if (observed.Output.Count > 0) t.Note("PRINT events: " + string.Join(" | ", observed.Output));
            t.Attach("Last VM steps (before execution)", string.Join("\n", recent));
            return observed;
        }

        internal static string StepText(DirectivaVM vm)
        {
            string instruction = "<invalid-ip>";
            if (vm.CurrentFunction != null && vm.CurrentFunction.TryGetInstruction(vm.InstructionPointer, out var op))
                instruction = op.ToString();
            return "function=" + (vm.CurrentFunction?.Name ?? "<none>") +
                   " ip=" + vm.InstructionPointer + " op=" + instruction +
                   " stack=" + vm.EvaluationStackCount + " depth=" + vm.CallDepth +
                   " dbg=" + (vm.Debug.File ?? "-") + ":" + vm.Debug.Line;
        }

        internal static string FormatValue(DataValue value)
        {
            if (value.dataType == DataTypeId.Int || value.dataType == DataTypeId.Decimal)
                return value.dataType + "(" + unchecked((long)value.value).ToString(CultureInfo.InvariantCulture) + ")";
            return value.dataType + "(" + value.value.ToString(CultureInfo.InvariantCulture) + ")";
        }

        internal static DILObservation Evaluate(DirectivaTestContext t, string body,
            int locals = 0, ulong heap = HeapMemory.DefaultMaxSize, VMOptions vmOptions = null,
            int budget = 2000)
        {
            return Run(t, MakeVM(t, body, locals, 0, heap, vmOptions), budget);
        }

        internal static DILObservation ExpectCompleted(DirectivaTestContext t, string body,
            int locals = 0, ulong heap = HeapMemory.DefaultMaxSize, int budget = 2000)
        {
            var r = Evaluate(t, body, locals, heap, null, budget);
            t.Equal(VMExecutionStatus.Completed, r.Status, "VM completion: " + (r.Error?.Message ?? ""));
            return r;
        }

        internal static void ExpectInt(DirectivaTestContext t, string body, long expected)
        {
            var r = ExpectCompleted(t, body);
            t.Equal(DataTypeId.Int, r.Value.dataType, "result type");
            t.Equal(expected, unchecked((long)r.Value.value), "result Int");
        }
        internal static void ExpectDecimal(DirectivaTestContext t, string body, long expectedRaw)
        {
            var r = ExpectCompleted(t, body);
            t.Equal(DataTypeId.Decimal, r.Value.dataType, "result type");
            t.Equal(expectedRaw, unchecked((long)r.Value.value), "result Decimal raw");
        }
        internal static void ExpectBool(DirectivaTestContext t, string body, bool expected, int locals = 0)
        {
            var r = ExpectCompleted(t, body, locals: locals);
            t.Equal(DataTypeId.Bool, r.Value.dataType, "result type");
            t.Equal(expected ? 1UL : 0UL, r.Value.value, "result Bool");
        }
        internal static void ExpectFault(DirectivaTestContext t, DILObservation r, RuntimeErrorId id)
        {
            t.Equal(VMExecutionStatus.Faulted, r.Status, "VM should fault with " + id);
            t.Check(r.Error != null, "Missing RuntimeError for " + id);
            t.Equal(id, r.Error.Id, "RuntimeErrorId");
            t.Check(r.Error.Id != RuntimeErrorId.InternalVmError || id == RuntimeErrorId.InternalVmError,
                "Unexpected InternalVmError instead of script fault");
        }
        internal static void ExpectParseFailure(DirectivaTestContext t, string source,
            string fragment = null, DILParserOptions options = null)
        {
            try
            {
                ParseModules(t, "test", new Dictionary<string, string> { ["test"] = source }, options);
            }
            catch (DILParseException ex)
            {
                t.Note("Expected parse failure at " + ex.ModuleName + ":" + ex.LineNumber + " — " + ex.Message);
                t.Equal("test", ex.ModuleName, "error module");
                t.Check(ex.LineNumber >= 0, "parse error must include valid line location");
                if (fragment != null) t.Contains(fragment, ex.Message, "parse diagnostic");
                return;
            }
            throw new DirectivaTestAssertionException("Expected DILParseException but parsing succeeded.");
        }
        internal static CodeContext Direct(DirectivaTestContext t, int globalCount,
            params Instruction[] instructions)
        {
            t.Attach("Direct VM instructions (bypasses DIL parser)",
                string.Join("\n", instructions.Select(i => i.ToString())));
            return new CodeContext(0, globalCount,
                new[] { new FunctionContext(0, "direct", 0, 0, instructions) });
        }
    }
}
