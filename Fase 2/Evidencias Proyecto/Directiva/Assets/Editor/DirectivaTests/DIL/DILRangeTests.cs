using System;
using System.Collections.Generic;
using System.Globalization;
using DSCompilation.DIL;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    /// <summary>Lazy Range construction, arithmetic extremes, iteration and Length.</summary>
    internal static class DILRangeTests
    {
        private static void Add(List<DirectivaTestCase> tests, string name,
            string description, Action<DirectivaTestContext> action)
            => DirectivaTestCatalog.Add(tests, "Ranges", name, description, action);

        private static string Program(long start, long stop, long step)
        {
            return "PUSH INT " + start.ToString(CultureInfo.InvariantCulture) +
                   "\nPUSH INT " + stop.ToString(CultureInfo.InvariantCulture) +
                   "\nPUSH INT " + step.ToString(CultureInfo.InvariantCulture) +
                   "\nRANGE 3\nITER_INIT\nloop:\nITER_NEXT done\nPRINT\nJUMP loop\n" +
                   "done:\nPUSH NONE\nRETURN";
        }

        private static void Output(DirectivaTestContext t, string source, params long[] expected)
        {
            var r = DILTestHarness.ExpectCompleted(t, source, budget: 10000);
            t.Equal(expected.Length, r.Output.Count, "item count");
            for (int i = 0; i < expected.Length; i++)
                t.Equal(expected[i].ToString(CultureInfo.InvariantCulture), r.Output[i],
                    "item " + i);
        }

        internal static void Register(List<DirectivaTestCase> tests)
        {
            Add(tests, "SingleArgument", "RANGE 1 uses start=0,step=1", t =>
                Output(t, "PUSH INT 4\nRANGE 1\nITER_INIT\nloop:\nITER_NEXT end\n" +
                    "PRINT\nJUMP loop\nend:\nPUSH NONE\nRETURN", 0, 1, 2, 3));
            Add(tests, "TwoArguments", "RANGE 2 uses step=1", t =>
                Output(t, "PUSH INT 4\nPUSH INT 7\nRANGE 2\nITER_INIT\nloop:\nITER_NEXT end\n" +
                    "PRINT\nJUMP loop\nend:\nPUSH NONE\nRETURN", 4, 5, 6));
            Add(tests, "PositiveStep", "Range positive step visits only included values", t =>
                Output(t, Program(1, 9, 3), 1, 4, 7));
            Add(tests, "NegativeStep", "Range negative step decrements lazily", t =>
                Output(t, Program(10, 1, -3), 10, 7, 4));
            Add(tests, "ZeroStop", "range(0) contains nothing", t =>
                Output(t, Program(0, 0, 1)));
            Add(tests, "EmptyAscending", "Inverted positive range is empty", t =>
                Output(t, Program(5, 2, 1)));
            Add(tests, "EmptyDescending", "Inverted negative range is empty", t =>
                Output(t, Program(2, 5, -1)));
            Add(tests, "OneAscending", "Single ascending item", t =>
                Output(t, Program(5, 6, 1), 5));
            Add(tests, "OneDescending", "Single descending item", t =>
                Output(t, Program(5, 4, -1), 5));
            Add(tests, "BigStep", "Step exceeding distance produces one item", t =>
                Output(t, Program(2, 11, 99), 2));
            Add(tests, "BigNegativeStep", "Negative step magnitude exceeding distance produces one item", t =>
                Output(t, Program(11, 2, -99), 11));
            Add(tests, "MinMaxBoundaryPositive", "Last valid positive item near Int64.MaxValue", t =>
                Output(t, Program(long.MaxValue - 1, long.MaxValue, 2), long.MaxValue - 1));
            Add(tests, "MinMaxBoundaryNegative", "Last valid negative item near Int64.MinValue", t =>
                Output(t, Program(long.MinValue + 1, long.MinValue, -2), long.MinValue + 1));
            Add(tests, "VeryLargePositiveStep", "Next-step signed overflow terminates gracefully", t =>
                Output(t, Program(long.MaxValue - 1, long.MaxValue, long.MaxValue), long.MaxValue - 1));
            Add(tests, "VeryLargeNegativeStep", "Negative step overflow terminates gracefully", t =>
                Output(t, Program(long.MinValue + 1, long.MinValue, long.MinValue), long.MinValue + 1));
            Add(tests, "MinStepMagnitude", "Int64.MinValue is valid as a negative step", t =>
                Output(t, Program(5, -5, long.MinValue), 5));
            Add(tests, "LengthThreeArguments", "Range Length agrees with lazy iteration", t =>
                DILTestHarness.ExpectInt(t, "PUSH INT 2\nPUSH INT 19\nPUSH INT 4\nRANGE 3\nLENGTH\nRETURN", 5));
            Add(tests, "LengthEmpty", "Range Length is zero for empty sequences", t =>
                DILTestHarness.ExpectInt(t, "PUSH INT 3\nPUSH INT 1\nRANGE 2\nLENGTH\nRETURN", 0));
            Add(tests, "LengthNegative", "Length of descending sequence", t =>
                DILTestHarness.ExpectInt(t, "PUSH INT 10\nPUSH INT -1\nPUSH INT -4\nRANGE 3\nLENGTH\nRETURN", 3));
            Add(tests, "LengthMember", "Range length getter reuses registered operation", t =>
                DILTestHarness.ExpectInt(t, "PUSH INT 5\nRANGE 1\nGET_PROPERTY length\nRETURN", 5));
            Add(tests, "TruthinessEmpty", "Empty range is false", t =>
                DILTestHarness.ExpectBool(t, "PUSH INT 0\nRANGE 1\nIS_TRUTHY\nRETURN", false));
            Add(tests, "TruthinessNonEmpty", "Nonempty range is true", t =>
                DILTestHarness.ExpectBool(t, "PUSH INT 4\nRANGE 1\nIS_TRUTHY\nRETURN", true));
            Add(tests, "StringCast", "Range conversion is descriptive and deterministic", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 2\nPUSH INT 9\nPUSH INT 3\nRANGE 3\nPRINT\nPUSH NONE\nRETURN");
                t.Equal("range(2, 9, 3)", r.Output[0], "formatted range");
            });
            Add(tests, "ZeroStep", "RANGE 3 with step zero returns a script error", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t,
                        "PUSH INT 1\nPUSH INT 10\nPUSH INT 0\nRANGE 3\nRETURN"),
                    RuntimeErrorId.InvalidOperation));
            Add(tests, "WrongType", "String argument rejected by RANGE", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t,
                        "PUSH STRING \"6\"\nRANGE 1\nRETURN"), RuntimeErrorId.InvalidOperation));
            Add(tests, "NoOperands", "RANGE underflow error", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t, "RANGE 1\nRETURN"), RuntimeErrorId.StackUnderflow));
            Add(tests, "BadRANGEArityZero", "Parser rejects RANGE 0", t =>
                DILTestHarness.ExpectParseFailure(t, DILTestHarness.Function(
                    "RANGE 0\nPUSH NONE\nRETURN"), "1, 2, or 3"));
            Add(tests, "BadRANGEArityFour", "Parser rejects RANGE 4", t =>
                DILTestHarness.ExpectParseFailure(t, DILTestHarness.Function(
                    "RANGE 4\nPUSH NONE\nRETURN"), "1, 2, or 3"));
            Add(tests, "NoRANGEArity", "Parser rejects RANGE without arity", t =>
                DILTestHarness.ExpectParseFailure(t, DILTestHarness.Function(
                    "RANGE\nPUSH NONE\nRETURN")));
            Add(tests, "HugeLengthOverflow", "LENGTH on a full Int64 range detects unrepresentable count", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "PUSH INT -9223372036854775808\nPUSH INT 9223372036854775807\n" +
                    "RANGE 2\nLENGTH\nRETURN"), RuntimeErrorId.Overflow));
            Add(tests, "HighMagnitudeLength", "Step=Int64.MaxValue avoids overflow in distance computation", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH INT -9223372036854775808\nPUSH INT 9223372036854775807\n" +
                    "PUSH INT 9223372036854775807\nRANGE 3\nLENGTH\nRETURN", 3));
            Add(tests, "LowMagnitudeLength", "Step=Int64.MinValue avoids overflow in distance computation", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 9223372036854775807\nPUSH INT -9223372036854775808\n" +
                    "PUSH INT -9223372036854775808\nRANGE 3\nLENGTH\nRETURN", 2));
            Add(tests, "BooleanStop", "True is accepted as the stop argument", t =>
                Output(t, "PUSH BOOL TRUE\nRANGE 1\nITER_INIT\n" +
                    "loop:\nITER_NEXT done\nPRINT\nJUMP loop\ndone:\nPUSH NONE\nRETURN", 0));
            Add(tests, "RangeAllocOOM", "Small logical heap rejects a Range", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t,
                        "PUSH INT 10\nRANGE 1\nRETURN", heap: 3), RuntimeErrorId.OutOfMemory));
            Add(tests, "RangeObjectConstantSize", "Huge range is always 4 logical units", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 2000000000\nRANGE 1\nRETURN", heap: 4);
                t.Equal(4UL, r.VM.Memory.Heap.AllocatedSize, "lazy range memory charge");
                t.Equal(DataTypeId.Range, r.Value.dataType, "range type");
            });
            Add(tests, "RangeIterAllocOOM", "Iterator creation requires its own logical allocation", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t,
                        "PUSH INT 7\nRANGE 1\nITER_INIT\nRETURN", heap: 7),
                    RuntimeErrorId.OutOfMemory));
            Add(tests, "RangeIsNotHashable", "Range is not silently made a hashable key", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t,
                        "PUSH INT 3\nRANGE 1\nHASH\nRETURN"), RuntimeErrorId.UnhashableType));
        }
    }
}
