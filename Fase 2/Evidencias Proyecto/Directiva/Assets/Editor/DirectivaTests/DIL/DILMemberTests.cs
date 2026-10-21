using System;
using System.Collections.Generic;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    /// <summary>
    /// End-to-end DIL member resolution and C# annotation inheritance. No Play Mode or scene.
    /// Failure evidence is captured by the existing per-test report system.
    /// </summary>
    internal static class DILMemberTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string name, string description, Action<DirectivaTestContext> test)
                => DirectivaTestCatalog.Add(tests, "Members", name, description, test);

            Add("StringLength", "GET_PROPERTY reads annotated String length", t =>
                DILTestHarness.ExpectInt(t, "PUSH STRING \"Hello\"\nGET_PROPERTY length\nRETURN", 5));
            Add("UnicodeLength", "Getter reports Unicode code points, not UTF-16 size", t =>
                DILTestHarness.ExpectInt(t, "PUSH STRING \"🐌X\"\nGET_PROPERTY length\nRETURN", 2));
            Add("TupleLength", "Getter works for immutable tuple", t =>
                DILTestHarness.ExpectInt(t, "PUSH INT 1\nPUSH INT 2\nTUPLE 2\nGET_PROPERTY length\nRETURN", 2));
            Add("EmptyTupleLength", "Getter works on empty tuple", t =>
                DILTestHarness.ExpectInt(t, "TUPLE 0\nGET_PROPERTY length\nRETURN", 0));
            Add("ListLength", "Getter works for mutable list", t =>
                DILTestHarness.ExpectInt(t, "PUSH INT 1\nLIST 1\nGET_PROPERTY length\nRETURN", 1));
            Add("SetLength", "Getter works on set with equal duplicate values", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 1\nPUSH BOOL TRUE\nSET 2\nGET_PROPERTY length\nRETURN", 1));
            Add("DictLength", "Getter counts unique dict entries", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 1\nPUSH INT 10\nPUSH BOOL TRUE\nPUSH INT 99\nDICT 2\nGET_PROPERTY length\nRETURN", 1));
            Add("CaseSensitive", "Member names preserve exact capitalization", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t, "PUSH STRING \"hello\"\nGET_PROPERTY Length\nRETURN"),
                    RuntimeErrorId.MemberNotFound));
            Add("MissingMember", "Unknown member produces structured script fault", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t, "PUSH STRING \"hello\"\nGET_PROPERTY nonexistent\nRETURN"),
                    RuntimeErrorId.MemberNotFound));
            Add("MissingMethod", "Unknown method produces structured script fault", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t, "LIST 0\nCALL_METHOD nonexistent 0\nRETURN"),
                    RuntimeErrorId.MemberNotFound));
            Add("WrongKind", "A method cannot be read as a property", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t, "DICT 0\nGET_PROPERTY keys\nRETURN"),
                    RuntimeErrorId.MemberNotFound));
            Add("ReadOnly", "Getter-only property cannot be assigned", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t, "PUSH STRING \"Hi\"\nPUSH INT 10\nSET_PROPERTY length\nPUSH NONE\nRETURN"),
                    RuntimeErrorId.ReadOnlyProperty));
            Add("WrongMethodArity", "A method validates its annotated parameter count", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t, "LIST 0\nCALL_METHOD append 0\nRETURN"),
                    RuntimeErrorId.InvalidMemberArgumentCount));
            Add("TooManyMethodArguments", "Extra method arguments are rejected", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t,
                        "LIST 0\nPUSH INT 1\nPUSH INT 2\nCALL_METHOD append 2\nRETURN"),
                    RuntimeErrorId.InvalidMemberArgumentCount));
            Add("WrongReceiverType", "A receiver without the member cannot invoke it", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t, "PUSH INT 1\nPUSH INT 2\nCALL_METHOD append 1\nRETURN"),
                    RuntimeErrorId.MemberNotFound));
            Add("StackUnderflowGetter", "Property read without receiver faults safely", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t, "GET_PROPERTY length\nRETURN"),
                    RuntimeErrorId.StackUnderflow));
            Add("StackUnderflowSetter", "Property write requires receiver and new value", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t, "PUSH INT 1\nSET_PROPERTY length\nRETURN"),
                    RuntimeErrorId.StackUnderflow));
            Add("StackUnderflowMethod", "A missing method parameter faults safely", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t, "LIST 0\nCALL_METHOD append 1\nRETURN"),
                    RuntimeErrorId.StackUnderflow));
            Add("MalformedPropertyName", "Parser rejects invalid property identifier", t =>
                DILTestHarness.ExpectParseFailure(t,
                    DILTestHarness.Function("PUSH NONE\nGET_PROPERTY 0bad\nRETURN"), "Invalid property name"));
            Add("MalformedMethodName", "Parser rejects invalid method identifier", t =>
                DILTestHarness.ExpectParseFailure(t,
                    DILTestHarness.Function("PUSH NONE\nCALL_METHOD bad.name 0\nRETURN"), "Invalid method name"));
            Add("NegativeArity", "Parser rejects negative method argument count", t =>
                DILTestHarness.ExpectParseFailure(t,
                    DILTestHarness.Function("PUSH NONE\nCALL_METHOD x -1\nRETURN"),
                    "non-negative argument count"));
            Add("ExcessiveArity", "Parser rejects an argument count that could overflow", t =>
                DILTestHarness.ExpectParseFailure(t,
                    DILTestHarness.Function("PUSH NONE\nCALL_METHOD x 2147483647\nRETURN"),
                    "below Int32.MaxValue"));
            Add("GetterExtraOperand", "Parser requires exactly one property identifier", t =>
                DILTestHarness.ExpectParseFailure(t,
                    DILTestHarness.Function("PUSH NONE\nGET_PROPERTY length 2\nRETURN"),
                    "expects 1 operand"));
            Add("SetterExtraOperand", "Parser requires exactly one property identifier", t =>
                DILTestHarness.ExpectParseFailure(t,
                    DILTestHarness.Function("PUSH NONE\nSET_PROPERTY length 1\nRETURN"),
                    "expects 1 operand"));

            Add("AppendMember", "List.append delegates to the existing allocation-aware operation", t =>
            {
                var result = DILTestHarness.ExpectCompleted(t,
                    "LIST 0\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nPUSH INT 12\nCALL_METHOD append 1\nPOP\n" +
                    "LOAD_LOCAL 0\nGET_PROPERTY length\nRETURN", locals: 1);
                t.Equal(1L, unchecked((long)result.Value.value), "length after append");
            });
            Add("AppendReturnsNone", "Void-style method calls push None", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t, "LIST 0\nPUSH INT 1\nCALL_METHOD append 1\nRETURN");
                t.Equal(DataTypeId.None, r.Value.dataType, "append returns None");
            });
            Add("AppendThenGet", "List mutations through a member are immediately visible", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "LIST 0\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nPUSH INT 13\nCALL_METHOD append 1\nPOP\n" +
                    "LOAD_LOCAL 0\nPUSH INT 0\nGET\nRETURN", locals: 1);
                t.Equal(13L, unchecked((long)r.Value.value), "appended element");
            });
            Add("ListContains", "List.contains retains continued-search behavior", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 11\nPUSH INT 22\nLIST 2\nPUSH INT 22\nCALL_METHOD contains 1\nRETURN", true));
            Add("ListIndexOf", "List.indexOf returns the searched index", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 11\nPUSH INT 22\nLIST 2\nPUSH INT 22\nCALL_METHOD indexOf 1\nRETURN", 1));
            Add("ListInsert", "List.insert reuses the continued insertion engine", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nPUSH INT 3\nLIST 2\nSTORE_LOCAL 0\nLOAD_LOCAL 0\n" +
                    "PUSH INT 1\nPUSH INT 2\nCALL_METHOD insert 2\nPOP\n" +
                    "LOAD_LOCAL 0\nPUSH INT 1\nGET\nRETURN", locals: 1);
                t.Equal(2L, unchecked((long)r.Value.value), "inserted element");
            });
            Add("ListExtendContinuation", "Member extend uses the VM continuation path and atomic swap", t =>
            {
                string many = string.Join("\n", System.Linq.Enumerable.Repeat("PUSH INT 1", 45));
                var r = DILTestHarness.Evaluate(t,
                    many + "\nLIST 45\nSTORE_LOCAL 0\n" +
                    many + "\nLIST 45\nSTORE_LOCAL 1\n" +
                    "LOAD_LOCAL 0\nLOAD_LOCAL 1\nCALL_METHOD extend 1\nPOP\n" +
                    "LOAD_LOCAL 0\nGET_PROPERTY length\nRETURN", locals: 2);
                t.Equal(VMExecutionStatus.Completed, r.Status, "continued operation completes");
                t.Equal(90L, unchecked((long)r.Value.value), "total list length");
                t.Check(r.Steps > 99, "operation required continuation steps");
            });
            Add("DictKeysMethod", "Dictionary.keys returns a list of unique keys", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 1\nPUSH INT 10\nPUSH INT 2\nPUSH INT 20\nDICT 2\n" +
                    "CALL_METHOD keys 0\nLENGTH\nRETURN", 2));
            Add("DictValuesMethod", "Dictionary.values returns a list of values", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 1\nPUSH INT 10\nDICT 1\nCALL_METHOD values 0\nPUSH INT 0\nGET\nRETURN", 10));
            Add("DictItemsMethod", "Dictionary.items returns a list of key/value tuples", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 1\nPUSH INT 10\nDICT 1\nCALL_METHOD items 0\n" +
                    "PUSH INT 0\nGET\nLENGTH\nRETURN", 2));
            Add("SetAddMethod", "Set.add delegates to its hash-aware mutation", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "SET 0\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nPUSH INT 8\nCALL_METHOD add 1\nPOP\n" +
                    "LOAD_LOCAL 0\nGET_PROPERTY length\nRETURN", locals: 1);
                t.Equal(1L, unchecked((long)r.Value.value), "set length");
            });
            Add("SetDiscardMethod", "Set.discard removes the key if present", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 8\nSET 1\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nPUSH INT 8\n" +
                    "CALL_METHOD discard 1\nPOP\nLOAD_LOCAL 0\nGET_PROPERTY length\nRETURN", locals: 1);
                t.Equal(0L, unchecked((long)r.Value.value), "set length after discard");
            });
            Add("StringMemberOOM", "A member receiver remains protected during garbage collection", t =>
            {
                var r = DILTestHarness.Evaluate(t,
                    "PUSH STRING \"held\"\nSTORE_LOCAL 0\n" +
                    "PUSH STRING \"temp-temp\"\nPOP\n" +
                    "PUSH STRING \"X\"\nPOP\n" +
                    "LOAD_LOCAL 0\nGET_PROPERTY length\nRETURN", locals: 1, heap: 6);
                t.Equal(VMExecutionStatus.Completed, r.Status, "getter survives collection");
                t.Equal(4L, unchecked((long)r.Value.value), "protected string length");
                t.Check(r.VM.Memory.GarbageCollectionCount > 0, "garbage collector ran");
            });

            Add("MemberInterleavingConflict", "A continued method must not overwrite a change by another VM", t =>
            {
                string module = "# GLOBAL : dst\n# GLOBAL : src\n" +
                    DILTestHarness.Function("LOAD_GLOBAL $dst\nLOAD_GLOBAL $src\nCALL_METHOD extend 1\nRETURN") +
                    DILTestHarness.Function("LOAD_GLOBAL $dst\nPUSH INT 8\nCALL_METHOD append 1\nRETURN",
                        functionName: "writer");
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> {
                    ["test"] = module
                });
                var memory = new VMMemoryContext(2, 50);
                var dst = new ListHeapObject(new[] { DataValue.FromInt(1) });
                var src = new ListHeapObject(new[] { DataValue.FromInt(2) });
                memory.SetGlobal(0, DataValue.FromListReference(memory.AllocHeap(dst)));
                memory.SetGlobal(1, DataValue.FromListReference(memory.AllocHeap(src)));
                var first = DILTestHarness.MakeVM(code, memory: memory);
                int writerId = -1;
                foreach (var function in code.Functions)
                    if (function.Name.EndsWith(".writer", StringComparison.Ordinal)) writerId = function.Id;
                t.Check(writerId >= 0, "writer function found");
                var second = DILTestHarness.MakeVM(code, memory: memory, entryFunctionId: writerId);
                first.Advance(); first.Advance(); first.Advance(); // EXTEND is staged.
                t.Equal(VMExecutionStatus.Running, first.Status, "first VM is continuing");
                t.Equal(VMExecutionStatus.Completed, DILTestHarness.Run(t, second).Status, "writer completes");
                DILTestHarness.ExpectFault(t, DILTestHarness.Run(t, first), RuntimeErrorId.InvalidOperation);
                t.Equal(2, dst.Count, "only the writer mutated the original");
                t.Equal(8L, unchecked((long)dst.GetElement(1).value), "writer value retained");
            });
            Add("MemberPendingRoots", "A pending member's arguments and auxiliary buffer are GC roots", t =>
            {
                string module = "# GLOBAL : dst\n# GLOBAL : src\n" +
                    DILTestHarness.Function("LOAD_GLOBAL $dst\nLOAD_GLOBAL $src\nCALL_METHOD extend 1\nRETURN");
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> {
                    ["test"] = module
                });
                var memory = new VMMemoryContext(2, 30);
                uint dst = memory.AllocHeap(new ListHeapObject(new[] { DataValue.FromInt(1) }));
                uint src = memory.AllocHeap(new ListHeapObject(new[] { DataValue.FromInt(2) }));
                memory.SetGlobal(0, DataValue.FromListReference(dst));
                memory.SetGlobal(1, DataValue.FromListReference(src));
                var vm = DILTestHarness.MakeVM(code, memory: memory);
                vm.Advance(); vm.Advance(); vm.Advance();
                t.Equal(VMExecutionStatus.Running, vm.Status, "method continued");
                var rooted = new List<uint>(vm.EnumerateHeapReferences());
                t.Check(rooted.Count >= 3, "arguments and auxiliary root visible to GC");
                foreach (uint reference in rooted)
                    t.Check(memory.TryGetHeap(reference, out _), "live pending root " + reference);
                memory.CollectGarbage();
                foreach (uint reference in rooted)
                    t.Check(memory.TryGetHeap(reference, out _), "root survived GC " + reference);
                t.Equal(VMExecutionStatus.Completed, DILTestHarness.Run(t, vm).Status, "continued method completes");
            });
            Add("MethodGCPressure", "Member append triggers lazy GC without collecting its string operands", t =>
            {
                var r = DILTestHarness.Evaluate(t,
                    "LIST 0\nSTORE_LOCAL 0\n" +
                    "PUSH STRING \"unused-text\"\nPOP\n" +
                    "LOAD_LOCAL 0\nPUSH STRING \"alive\"\nCALL_METHOD append 1\nPOP\n" +
                    "LOAD_LOCAL 0\nPUSH INT 0\nGET\nGET_PROPERTY length\nRETURN",
                    locals: 1, heap: 6);
                t.Equal(VMExecutionStatus.Completed, r.Status, "member survived GC");
                t.Equal(5L, unchecked((long)r.Value.value), "stored string content survived");
                t.Check(r.VM.Memory.GarbageCollectionCount > 0, "collection occurred under pressure");
            });

            Add("OverrideGetter", "Inherited getter dispatches to the derived virtual override", t =>
            {
                var child = new DerivedProbe();
                var answer = Invoke(child, DScriptMemberKind.Getter, "health");
                t.Equal(OperationStatus.Success, answer.Status, "getter succeeded");
                t.Equal(100L, unchecked((long)answer.Value.value), "derived getter");
            });
            Add("OverrideMethod", "Inherited method dispatches to a derived override without repeating annotation", t =>
            {
                var child = new DerivedProbe();
                var answer = Invoke(child, DScriptMemberKind.Method, "move", DataValue.FromInt(3));
                t.Equal(OperationStatus.Success, answer.Status, "method succeeded");
                t.Equal(103L, unchecked((long)answer.Value.value), "derived method");
            });
            Add("OverrideSetter", "Inherited setter dispatches to a derived override", t =>
            {
                var child = new DerivedProbe();
                var answer = Invoke(child, DScriptMemberKind.Setter, "health", DataValue.FromInt(12));
                t.Equal(OperationStatus.Success, answer.Status, "setter succeeded");
                t.Equal(24L, child.LastHealth, "derived setter modified its backing state");
            });
            Add("OverrideInheritedArity", "Method override preserves the base DScript arity", t =>
            {
                var child = new DerivedProbe();
                var answer = Invoke(child, DScriptMemberKind.Method, "move");
                t.Equal(OperationStatus.Error, answer.Status, "wrong arity status");
                t.Equal(RuntimeErrorId.InvalidMemberArgumentCount, answer.Error.Id, "arity error");
            });
            Add("BaseGetter", "Base implementation is used when it is not overridden", t =>
            {
                var child = new DerivedProbe();
                var answer = Invoke(child, DScriptMemberKind.Getter, "baseOnly");
                t.Equal(7L, unchecked((long)answer.Value.value), "inherited implementation");
            });
            Add("AbstractGetter", "Annotated abstract base methods are exposed by concrete implementations", t =>
            {
                var child = new ConcreteAbstractProbe();
                var answer = Invoke(child, DScriptMemberKind.Getter, "speed");
                t.Equal(45L, unchecked((long)answer.Value.value), "abstract annotation inheritance");
            });
            Add("CaseSensitiveRegistration", "Annotation names retain case", t =>
            {
                var child = new DerivedProbe();
                t.Equal(RuntimeErrorId.MemberNotFound,
                    Invoke(child, DScriptMemberKind.Getter, "Health").Error.Id, "case mismatch");
            });
            Add("WriteOnly", "Write-only property rejects reads with a dedicated error", t =>
            {
                var probe = new WriteOnlyProbe();
                t.Equal(RuntimeErrorId.WriteOnlyProperty,
                    Invoke(probe, DScriptMemberKind.Getter, "value").Error.Id, "read error");
                t.Equal(OperationStatus.Success,
                    Invoke(probe, DScriptMemberKind.Setter, "value", DataValue.FromInt(9)).Status,
                    "valid setter");
            });
            Add("SetterNoValue", "Setter requires one argument and returns None", t =>
            {
                var child = new DerivedProbe();
                t.Equal(RuntimeErrorId.InvalidMemberArgumentCount,
                    Invoke(child, DScriptMemberKind.Setter, "health").Error.Id, "setter arity");
                t.Equal(DataTypeId.None,
                    Invoke(child, DScriptMemberKind.Setter, "health", DataValue.FromInt(4)).Value.dataType,
                    "setter returns None");
            });
            Add("DuplicateBinding", "Two same-name annotated methods fail during registration", t =>
            {
                bool caught = false;
                try { new DuplicateProbe(); }
                catch (InvalidOperationException ex) { caught = true; t.Contains("more than once", ex.Message, "duplicate diagnostic"); }
                t.Check(caught, "duplicate members must fail registration");
            });
            Add("MixedPropertyMethod", "Property and method cannot share the same exposed name", t =>
            {
                bool caught = false;
                try { new ConflictingProbe(); }
                catch (InvalidOperationException ex) { caught = true; t.Contains("mixes property and method", ex.Message, "conflict diagnostic"); }
                t.Check(caught, "method/property ambiguity must fail registration");
            });
            Add("InvalidSignature", "Wrong C# handler signature fails registration", t =>
            {
                bool caught = false;
                try { new InvalidSignatureProbe(); }
                catch (InvalidOperationException ex) { caught = true; t.Contains("OperationCall", ex.Message, "signature diagnostic"); }
                t.Check(caught, "invalid C# signature must fail registration");
            });
            Add("VariadicMethod", "DScriptMethod without arity accepts variable argument counts", t =>
            {
                var probe = new VariadicProbe();
                t.Equal(0L, unchecked((long)Invoke(probe, DScriptMemberKind.Method, "count").Value.value), "zero args");
                t.Equal(2L, unchecked((long)Invoke(probe, DScriptMemberKind.Method, "count",
                    DataValue.None(), DataValue.None()).Value.value), "two args");
            });
        }

        private static OperationResult Invoke(DataType type, DScriptMemberKind kind,
            string name, params DataValue[] parameters)
        {
            var args = new DataValue[parameters.Length + 1];
            args[0] = DataValue.None(); // A direct type dispatch probe; the receiver is conceptually separate.
            Array.Copy(parameters, 0, args, 1, parameters.Length);
            return type.InvokeMember(kind, name,
                new OperationCall(args, 0, new VMMemoryContext(0), null));
        }

        private class BaseProbe : DataType
        {
            [DScriptGetter("health")]
            public virtual OperationResult GetHealth(OperationCall call)
                => OperationResult.Success(DataValue.FromInt(5));

            [DScriptSetter("health")]
            public virtual OperationResult SetHealth(OperationCall call)
                => OperationResult.Success(DataValue.None());

            [DScriptGetter("baseOnly")]
            public OperationResult GetBaseOnly(OperationCall call)
                => OperationResult.Success(DataValue.FromInt(7));

            [DScriptMethod("move", 1)]
            public virtual OperationResult Move(OperationCall call)
                => OperationResult.Success(DataValue.FromInt(1));
        }
        private sealed class DerivedProbe : BaseProbe
        {
            internal long LastHealth;
            public override OperationResult GetHealth(OperationCall call)
                => OperationResult.Success(DataValue.FromInt(100));
            public override OperationResult SetHealth(OperationCall call)
            {
                LastHealth = unchecked((long)call.Arguments[1].value) * 2;
                return OperationResult.Success(DataValue.None());
            }
            public override OperationResult Move(OperationCall call)
                => OperationResult.Success(DataValue.FromInt(100 + unchecked((long)call.Arguments[1].value)));
        }
        private abstract class AbstractProbe : DataType
        {
            [DScriptGetter("speed")]
            public abstract OperationResult GetSpeed(OperationCall call);
        }
        private sealed class ConcreteAbstractProbe : AbstractProbe
        {
            public override OperationResult GetSpeed(OperationCall call)
                => OperationResult.Success(DataValue.FromInt(45));
        }
        private sealed class WriteOnlyProbe : DataType
        {
            [DScriptSetter("value")]
            public OperationResult SetValue(OperationCall call) => OperationResult.Success(DataValue.None());
        }
        private sealed class DuplicateProbe : DataType
        {
            [DScriptMethod("foo")]
            public OperationResult First(OperationCall call) => OperationResult.Success(DataValue.None());
            [DScriptMethod("foo")]
            public OperationResult Second(OperationCall call) => OperationResult.Success(DataValue.None());
        }
        private sealed class ConflictingProbe : DataType
        {
            [DScriptGetter("foo")]
            public OperationResult First(OperationCall call) => OperationResult.Success(DataValue.None());
            [DScriptMethod("foo")]
            public OperationResult Second(OperationCall call) => OperationResult.Success(DataValue.None());
        }
        private sealed class InvalidSignatureProbe : DataType
        {
            [DScriptGetter("foo")]
            public void Invalid() { }
        }
        private sealed class VariadicProbe : DataType
        {
            [DScriptMethod("count")]
            public OperationResult Count(OperationCall call)
                => OperationResult.Success(DataValue.FromInt(call.Arguments.Length - 1));
        }
    }
}
