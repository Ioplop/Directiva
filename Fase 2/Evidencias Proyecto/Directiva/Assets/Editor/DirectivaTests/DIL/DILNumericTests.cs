using System;
using System.Collections.Generic;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    internal static class DILNumericTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string purpose, Action<DirectivaTestContext> action)
                => DirectivaTestCatalog.Add(tests, "Numeric", id, purpose, action);
            var intCases = new[]
            {
                new[] { "Add", "PUSH INT 20\nPUSH INT 22\nADD\nRETURN", "42" },
                new[] { "Subtract", "PUSH INT 20\nPUSH INT 22\nSUBTRACT\nRETURN", "-2" },
                new[] { "Multiply", "PUSH INT -5\nPUSH INT 9\nMULTIPLY\nRETURN", "-45" },
                new[] { "FloorDividePositive", "PUSH INT 8\nPUSH INT 3\nFLOOR_DIVIDE\nRETURN", "2" },
                new[] { "FloorDivideNegative", "PUSH INT -8\nPUSH INT 3\nFLOOR_DIVIDE\nRETURN", "-3" },
                new[] { "ModuloNegative", "PUSH INT -8\nPUSH INT 3\nMODULO\nRETURN", "1" },
                new[] { "Power", "PUSH INT 3\nPUSH INT 4\nPOWER\nRETURN", "81" },
                new[] { "HashInt", "PUSH INT -221\nHASH\nRETURN", "-221" },
                new[] { "CastIntIdentity", "PUSH INT -123\nCAST INT\nRETURN", "-123" }
            };
            foreach (var row in intCases)
            {
                string name = row[0], source = row[1];
                long value = long.Parse(row[2], System.Globalization.CultureInfo.InvariantCulture);
                Add(name, "Check integer operation " + name, t => DILTestHarness.ExpectInt(t, source, value));
            }
            Add("TrueDivision", "INT / INT produces a fixed-point Decimal", t =>
                DILTestHarness.ExpectDecimal(t, "PUSH INT 7\nPUSH INT 2\nDIVIDE\nRETURN", 35000));
            Add("NegativeExponent", "Exponentiation with a negative exponent returns Decimal", t =>
                DILTestHarness.ExpectDecimal(t, "PUSH INT 2\nPUSH INT -1\nPOWER\nRETURN", 5000));
            Add("DecimalAdd", "Exact fixed-point decimal arithmetic", t =>
                DILTestHarness.ExpectDecimal(t, "PUSH DECIMAL 1.125\nPUSH DECIMAL 2.375\nADD\nRETURN", 35000));
            Add("DecimalMultiply", "Fixed-point product with correct scale", t =>
                DILTestHarness.ExpectDecimal(t, "PUSH DECIMAL 1.5\nPUSH DECIMAL 2.0\nMULTIPLY\nRETURN", 30000));
            Add("MixedPromotion", "Both dispatch orders resolve Int + Decimal", t =>
            {
                DILTestHarness.ExpectDecimal(t, "PUSH INT 2\nPUSH DECIMAL 0.25\nADD\nRETURN", 22500);
                DILTestHarness.ExpectDecimal(t, "PUSH DECIMAL 0.25\nPUSH INT 2\nADD\nRETURN", 22500);
            });
            Add("BoolArithmetic", "Bool acts numerically as 0 or 1 when combined with Int", t =>
                DILTestHarness.ExpectInt(t, "PUSH BOOL TRUE\nPUSH INT 4\nADD\nRETURN", 5));
            Add("BoolIntDecimalEquality", "Numeric equality across Bool, Int and Decimal", t =>
            {
                DILTestHarness.ExpectBool(t, "PUSH BOOL TRUE\nPUSH INT 1\nVALUE_EQUALS\nRETURN", true);
                DILTestHarness.ExpectBool(t, "PUSH INT 1\nPUSH DECIMAL 1.0\nVALUE_EQUALS\nRETURN", true);
                DILTestHarness.ExpectBool(t, "PUSH BOOL FALSE\nPUSH DECIMAL 0.0\nVALUE_EQUALS\nRETURN", true);
            });
            Add("NumericHashCompatibility", "Hash matches when numeric values compare equal", t =>
            {
                var code = "PUSH BOOL TRUE\nHASH\nPRINT\nPUSH INT 1\nHASH\nPRINT\nPUSH DECIMAL 1.0\nHASH\nPRINT\nPUSH NONE\nRETURN";
                var r = DILTestHarness.ExpectCompleted(t, code);
                t.Equal(3, r.Output.Count, "number of hashes");
                t.Equal(r.Output[0], r.Output[1], "Bool-Int hashes");
                t.Equal(r.Output[1], r.Output[2], "Int-Decimal hashes");
            });
            Add("Comparisons", "Greater/Less and inclusive comparisons return Bool", t =>
            {
                DILTestHarness.ExpectBool(t, "PUSH INT 3\nPUSH INT 7\nLESS_THAN\nRETURN", true);
                DILTestHarness.ExpectBool(t, "PUSH INT 3\nPUSH INT 3\nLESS_OR_EQUAL\nRETURN", true);
                DILTestHarness.ExpectBool(t, "PUSH DECIMAL 3.5\nPUSH INT 2\nGREATER_THAN\nRETURN", true);
                DILTestHarness.ExpectBool(t, "PUSH INT 7\nPUSH INT 7\nGREATER_OR_EQUAL\nRETURN", true);
                DILTestHarness.ExpectBool(t, "PUSH INT 0\nPUSH INT 1\nGREATER_THAN\nRETURN", false);
            });
            Add("TruthinessAndNot", "None, zero, numbers and Booleans have expected truthiness", t =>
            {
                DILTestHarness.ExpectBool(t, "PUSH NONE\nIS_TRUTHY\nRETURN", false);
                DILTestHarness.ExpectBool(t, "PUSH INT 0\nIS_TRUTHY\nRETURN", false);
                DILTestHarness.ExpectBool(t, "PUSH DECIMAL -0.25\nIS_TRUTHY\nRETURN", true);
                DILTestHarness.ExpectBool(t, "PUSH BOOL FALSE\nNOT\nRETURN", true);
                DILTestHarness.ExpectBool(t, "PUSH INT 3\nNOT\nRETURN", false);
            });
            Add("CastToString", "Int, Decimal, Bool, None cast to string and print", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT -16\nCAST STRING\nPRINT\nPUSH DECIMAL 12.5\nCAST STRING\nPRINT\n" +
                    "PUSH BOOL TRUE\nCAST STRING\nPRINT\nPUSH NONE\nCAST STRING\nPRINT\n" +
                    "PUSH NONE\nRETURN");
                t.Equal("-16", r.Output[0], "int text");
                t.Equal("12.5", r.Output[1], "decimal text");
                t.Equal("True", r.Output[2], "bool text");
                t.Equal("None", r.Output[3], "none text");
            });
            Add("UnsupportedStringToInt", "Current DTString does not yet support parsing to Int", t =>
            {
                t.Note("String to numeric casts are currently not implemented in DTString; expect InvalidCast.");
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "PUSH STRING \"21\"\nCAST INT\nRETURN"), RuntimeErrorId.InvalidCast);
            });
            Add("CastDecimalToInt", "Decimal -> Int truncates fractional part toward zero", t =>
                DILTestHarness.ExpectInt(t, "PUSH DECIMAL -2.9\nCAST INT\nRETURN", -2));
            Add("CastIdentity", "Every type can cast to itself without extra heap allocation", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH STRING \"immutable\"\nCAST STRING\nCAST STRING\nRETURN");
                t.Equal(DataTypeId.String, r.Value.dataType, "identity type");
                t.Equal(1, r.VM.Memory.Heap.ObjectCount, "identity should reuse one StringHeapObject");
            });
            Add("PowerContinuation", "POWER is budgeted across deterministic operation continuations", t =>
            {
                var vm = DILTestHarness.MakeVM(t, "PUSH INT 2\nPUSH INT 25\nPOWER\nRETURN");
                var run = vm.Run(3);
                t.Equal(VMExecutionStatus.Running, run.Status, "power must continue after first operation step");
                t.Check(run.BudgetExhausted, "power must use budget");
                var r = DILTestHarness.Run(t, vm, 200);
                t.Equal(VMExecutionStatus.Completed, r.Status, "power completion");
                t.Equal(33554432L, unchecked((long)r.Value.value), "2^25");
            });
            Add("ValueVsReferenceIdentity", "Equal but distinct heap strings are not reference equal", t =>
            {
                DILTestHarness.ExpectBool(t,
                    "PUSH STRING \"abc\"\nPUSH STRING \"abc\"\nVALUE_EQUALS\nRETURN", true);
                DILTestHarness.ExpectBool(t,
                    "PUSH STRING \"abc\"\nPUSH STRING \"abc\"\nREFERENCE_EQUALS\nRETURN", false);
            });
        }
    }
}
