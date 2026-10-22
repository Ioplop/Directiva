using System;
using System.Collections.Generic;
using System.Linq;
using DSCompilation.DIL;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    /// <summary>Iterator loop mechanics, supported collections, None and parser jumps.</summary>
    internal static class DILIteratorTests
    {
        private static void Add(List<DirectivaTestCase> tests, string name,
            string description, Action<DirectivaTestContext> action)
            => DirectivaTestCatalog.Add(tests, "Iterators", name, description, action);

        private static void Prints(DirectivaTestContext t, string source, params string[] expected)
        {
            var r = DILTestHarness.ExpectCompleted(t, source);
            t.Equal(expected.Length, r.Output.Count, "output line count");
            for (int i = 0; i < expected.Length; i++)
                t.Equal(expected[i], r.Output[i], "output line " + i);
        }

        private static string Loop(string construct)
            => construct + "\nITER_INIT\nstart:\nITER_NEXT done\nPRINT\nJUMP start\ndone:\nPUSH NONE\nRETURN";

        internal static void Register(List<DirectivaTestCase> tests)
        {
            Add(tests, "TupleAscending", "Tuple traversal preserves element order", t =>
                Prints(t, Loop("PUSH INT 4\nPUSH INT 7\nPUSH INT 9\nTUPLE 3"), "4", "7", "9"));
            Add(tests, "ListAscending", "List traversal preserves element order", t =>
                Prints(t, Loop("PUSH INT 4\nPUSH INT 7\nPUSH INT 9\nLIST 3"), "4", "7", "9"));
            Add(tests, "DictionaryKeys", "Dictionaries iterate insertion-ordered keys, not values", t =>
                Prints(t, Loop("PUSH STRING \"a\"\nPUSH INT 11\nPUSH STRING \"b\"\nPUSH INT 22\nDICT 2"), "a", "b"));
            Add(tests, "SetUniqueMembers", "Sets iterate unique values deterministically", t =>
                Prints(t, Loop("PUSH INT 4\nPUSH INT 5\nPUSH INT 4\nSET 3"), "4", "5"));
            Add(tests, "TupleEmpty", "Empty Tuple jumps immediately, without generating items", t =>
                Prints(t, Loop("TUPLE 0")));
            Add(tests, "ListEmpty", "Empty List jumps immediately", t =>
                Prints(t, Loop("LIST 0")));
            Add(tests, "SetEmpty", "Empty Set jumps immediately", t =>
                Prints(t, Loop("SET 0")));
            Add(tests, "DictionaryEmpty", "Empty Dictionary jumps immediately", t =>
                Prints(t, Loop("DICT 0")));
            Add(tests, "NoneInsideTuple", "None is a valid yielded element, not a termination sentinel", t =>
                Prints(t, Loop("PUSH INT 1\nPUSH NONE\nPUSH INT 2\nTUPLE 3"), "1", "None", "2"));
            Add(tests, "NoneInsideList", "None is valid inside mutable lists", t =>
                Prints(t, Loop("PUSH NONE\nPUSH BOOL TRUE\nLIST 2"), "None", "True"));
            Add(tests, "NoneInsideSet", "Set may iterate None as a normal element", t =>
                Prints(t, Loop("PUSH NONE\nPUSH INT 7\nSET 2"), "None", "7"));
            Add(tests, "NoneAsDictKey", "Dictionary None keys are not iteration termination", t =>
                Prints(t, Loop("PUSH NONE\nPUSH INT 8\nDICT 1"), "None"));
            Add(tests, "MixedTypes", "Tuples yield mixed types intact", t =>
                Prints(t, Loop("PUSH BOOL FALSE\nPUSH STRING \"hi\"\nPUSH NONE\nTUPLE 3"),
                    "False", "hi", "None"));
            Add(tests, "NestedTuple", "Nested heap references survive iteration and PRINT", t =>
                Prints(t, Loop("PUSH INT 1\nPUSH INT 2\nTUPLE 2\nTUPLE 1"), "(1, 2)"));
            Add(tests, "EarlyExit", "Explicit POP of cursor permits early loop exit", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 4\nPUSH INT 5\nLIST 2\nITER_INIT\nITER_NEXT ended\n" +
                    "STORE_LOCAL 0\nPOP\nLOAD_LOCAL 0\nRETURN\nended:\nPUSH INT -1\nRETURN",
                    locals: 1);
                t.Equal(4L, unchecked((long)r.Value.value), "early exit value");
            });
            Add(tests, "ReiteratingSameSequence", "ITER_INIT creates independent cursors", t =>
            {
                // The second loop starts only after the first cursor was exhausted.
                string source = "PUSH INT 3\nPUSH INT 6\nTUPLE 2\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nITER_INIT\nloop1:\nITER_NEXT done1\nPRINT\nJUMP loop1\n" +
                    "done1:\nLOAD_LOCAL 0\nITER_INIT\nloop2:\nITER_NEXT done2\nPRINT\nJUMP loop2\n" +
                    "done2:\nPUSH NONE\nRETURN";
                var result = DILTestHarness.ExpectCompleted(t, source, locals: 1);
                t.Equal("3,6,3,6", string.Join(",", result.Output), "independent cursors");
            });
            Add(tests, "IterationAdvancesOnlyOnNext", "Iterator holds state across unrelated instructions", t =>
                Prints(t, "PUSH INT 11\nPUSH INT 12\nTUPLE 2\nITER_INIT\n" +
                    "ITER_NEXT end\nPRINT\nPUSH INT 999\nPOP\nITER_NEXT end\nPRINT\n" +
                    "ITER_NEXT end\nend:\nPUSH NONE\nRETURN", "11", "12"));
            Add(tests, "IteratorIdentity", "Calling ITER_INIT on a cursor returns the same cursor", t =>
                Prints(t, "PUSH INT 11\nPUSH INT 12\nLIST 2\nITER_INIT\n" +
                    "ITER_NEXT done\nPRINT\nITER_INIT\nITER_NEXT done\nPRINT\n" +
                    "ITER_NEXT done\ndone:\nPUSH NONE\nRETURN", "11", "12"));
            Add(tests, "IteratorStoredLocal", "Iterator works as a value stored in local slots", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 15\nTUPLE 1\nITER_INIT\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nITER_NEXT end\nPRINT\nJUMP next\n" +
                    "end:\nPUSH INT -1\nPRINT\nnext:\nPUSH NONE\nRETURN", locals: 1);
                t.Equal("15", r.Output.Single(), "local cursor result");
            });
            Add(tests, "IteratorStoredInGlobal", "Iterator is a heap reference and can cross global slots", t =>
            {
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string,string> {
                    ["test"] = "# GLOBAL : cursor\n" + DILTestHarness.Function(
                        "PUSH INT 41\nLIST 1\nITER_INIT\nSTORE_GLOBAL $cursor\n" +
                        "LOAD_GLOBAL $cursor\nITER_NEXT end\nPRINT\n" +
                        "end:\nPUSH NONE\nRETURN")
                });
                var r = DILTestHarness.Run(t, DILTestHarness.MakeVM(code));
                t.Equal(VMExecutionStatus.Completed, r.Status, "global loop");
                t.Equal("41", r.Output.Single(), "global cursor item");
            });
            Add(tests, "NotIterableInt", "ITER_INIT refuses an integer", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t, "PUSH INT 5\nITER_INIT\nRETURN"),
                    RuntimeErrorId.NotIterable));
            Add(tests, "NotIterableNone", "ITER_INIT refuses None", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t, "PUSH NONE\nITER_INIT\nRETURN"),
                    RuntimeErrorId.NotIterable));
            Add(tests, "NotIterableFunction", "ITER_INIT refuses Function refs", t =>
            {
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> {
                    ["test"] = DILTestHarness.Function("PUSH_FUNCTION main\nITER_INIT\nRETURN")
                });
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Run(t, DILTestHarness.MakeVM(code)), RuntimeErrorId.NotIterable);
            });
            Add(tests, "InvalidIteratorValue", "ITER_NEXT on a non-cursor yields a typed failure", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t, "PUSH INT 7\nITER_NEXT end\nend:\nPUSH NONE\nRETURN"),
                    RuntimeErrorId.InvalidIterator));
            Add(tests, "InvalidIteratorNone", "ITER_NEXT cannot use None as its cursor", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t, "PUSH NONE\nITER_NEXT end\nend:\nPUSH NONE\nRETURN"),
                    RuntimeErrorId.InvalidIterator));
            Add(tests, "InitUnderflow", "ITER_INIT with empty stack errors deterministically", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t, "ITER_INIT\nPUSH NONE\nRETURN"),
                    RuntimeErrorId.StackUnderflow));
            Add(tests, "NextUnderflow", "ITER_NEXT with empty stack errors deterministically", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t, "ITER_NEXT end\nend:\nPUSH NONE\nRETURN"),
                    RuntimeErrorId.StackUnderflow));
            Add(tests, "InitUninitialized", "Uninitialized source is rejected", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t, "LOAD_LOCAL 0\nITER_INIT\nRETURN", locals: 1),
                    RuntimeErrorId.VariableUsedBeforeInitialization));
            Add(tests, "NextUninitialized", "Uninitialized cursor value is rejected", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t,
                        "LOAD_LOCAL 0\nITER_NEXT end\nend:\nPUSH NONE\nRETURN", locals: 1),
                    RuntimeErrorId.VariableUsedBeforeInitialization));
            Add(tests, "BadNextLabel", "Unknown exhaustion label rejected by DIL parser", t =>
                DILTestHarness.ExpectParseFailure(t, DILTestHarness.Function(
                    "TUPLE 0\nITER_INIT\nITER_NEXT missing\nPUSH NONE\nRETURN"), "Unknown label"));
            Add(tests, "BadNextTarget", "Out of bounds ITER_NEXT jump target is rejected", t =>
                DILTestHarness.ExpectParseFailure(t, DILTestHarness.Function(
                    "TUPLE 0\nITER_INIT\nITER_NEXT 999\nPUSH NONE\nRETURN"), "Jump target"));
            Add(tests, "BadInitArgument", "ITER_INIT takes no inline arguments", t =>
                DILTestHarness.ExpectParseFailure(t, DILTestHarness.Function(
                    "ITER_INIT extra\nPUSH NONE\nRETURN"), "expects"));
            Add(tests, "BadNextArgument", "ITER_NEXT takes exactly one jump operand", t =>
                DILTestHarness.ExpectParseFailure(t, DILTestHarness.Function(
                    "ITER_NEXT end too_much\nend:\nPUSH NONE\nRETURN")));
            Add(tests, "NextJumpDebugMetadata", "Debug metadata preserves ITER_NEXT jump targets", t =>
            {
                var code = DILTestHarness.Parse(t, "PUSH INT 7\nTUPLE 1\nITER_INIT\n" +
                    "loop:\nITER_NEXT done\nPOP\nJUMP loop\n" +
                    "done:\nPUSH INT 8\nRETURN", options: new DILParserOptions(generateDebugMetadata: true));
                var r = DILTestHarness.Run(t, DILTestHarness.MakeVM(code));
                t.Equal(VMExecutionStatus.Completed, r.Status, "debug jumps");
                t.Equal(8L, unchecked((long)r.Value.value), "after exhaustion");
            });
            Add(tests, "NextNumericTarget", "Numeric target is validated and resolved", t =>
            {
                DILTestHarness.ExpectInt(t,
                    "TUPLE 0\nITER_INIT\nITER_NEXT 4\nPUSH INT 55\nPUSH INT 9\nRETURN", 9);
            });
            Add(tests, "NextStackOverflow", "Full stack must not advance the cursor on ITER_NEXT", t =>
            {
                var vm = DILTestHarness.MakeVM(t,
                    "PUSH INT 5\nTUPLE 1\nITER_INIT\nITER_NEXT end\n" +
                    "end:\nPUSH NONE\nRETURN",
                    options: new VMOptions(maxEvaluationStackSize: 1));
                DILTestHarness.ExpectFault(t, DILTestHarness.Run(t, vm), RuntimeErrorId.StackOverflow);
            });
            Add(tests, "EmptyIteratorAtStackCapacity", "Exhausting a cursor needs no extra stack slot", t =>
            {
                var vm = DILTestHarness.MakeVM(t,
                    "TUPLE 0\nITER_INIT\nITER_NEXT end\nPUSH INT -1\n" +
                    "end:\nPUSH INT 42\nRETURN",
                    options: new VMOptions(maxEvaluationStackSize: 1));
                var r = DILTestHarness.Run(t, vm);
                t.Equal(VMExecutionStatus.Completed, r.Status, "empty cursor at full capacity");
                t.Equal(42L, unchecked((long)r.Value.value), "jump over value");
            });
            Add(tests, "IteratorErrorNoInternal", "Forged IteratorId rejects bad heap reference", t =>
            {
                var code = DILTestHarness.Direct(t, 0,
                    Instruction.Push(new DataValue(DataTypeId.Iterator, 99888UL)),
                    Instruction.IterNext(3), Instruction.Push(DataValue.None()), Instruction.Return());
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Run(t, DILTestHarness.MakeVM(code)), RuntimeErrorId.InvalidMemoryAccess);
            });
            Add(tests, "ForLoopSum", "Loop can maintain accumulator in local memory", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nPUSH INT 2\nPUSH INT 3\nTUPLE 3\nITER_INIT\n" +
                    "PUSH INT 0\nSTORE_LOCAL 0\n" +
                    "loop:\nITER_NEXT end\nLOAD_LOCAL 0\nADD\nSTORE_LOCAL 0\nJUMP loop\n" +
                    "end:\nLOAD_LOCAL 0\nRETURN", locals: 1);
                t.Equal(6L, unchecked((long)r.Value.value), "accumulated sum");
            });
        }
    }
}
