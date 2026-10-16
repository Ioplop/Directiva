// Optional Unity Editor smoke tests for the Directiva tuple completion patch.
// Place in Assets/Editor/Directiva_TupleCompletionSmokeTests.cs (no Play mode required).
using System;
using System.Collections.Generic;
using DSCompilation.DIL;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;
using UnityEditor;
using UnityEngine;

public static class Directiva_TupleCompletionSmokeTests
{
    private sealed class ModuleSource : IDILSourceProvider
    {
        private readonly string source;

        public ModuleSource(string source) { this.source = source; }

        public bool TryReadModule(string name, out string text)
        {
            text = name == "tuple_tests" ? source : null;
            return text != null;
        }
    }

    [MenuItem("Directiva/Tests/Run Tuple Completion Smoke Tests")]
    public static void Run()
    {
        int passed = 0;
        int failed = 0;
        Check("DIL TUPLE, GET and order", TestDILGet, ref passed, ref failed);
        Check("DIL COPY and empty TUPLE", TestCopyAndEmpty, ref passed, ref failed);
        Check("Logical LENGTH", TestLength, ref passed, ref failed);
        Check("Unicode String LENGTH", TestUnicodeLength, ref passed, ref failed);
        Check("Tuple CAST STRING formatting", TestStringFormatting, ref passed, ref failed);
        Check("Stable string HASH", TestStringHash, ref passed, ref failed);
        Check("Compatible numeric tuple HASH", TestNumericTupleHash, ref passed, ref failed);
        Check("Tuple HASH order dependence", TestHashOrder, ref passed, ref failed);
        Check("None inside hashed tuple", TestNoneHash, ref passed, ref failed);
        Check("Nested unhashable element path", TestNestedUnhashable, ref passed, ref failed);
        Check("Parser rejects negative TUPLE", TestNegativeCount, ref passed, ref failed);
        Check("CAST STRING rooted through lazy GC", TestGCWhileCasting, ref passed, ref failed);
        Check("CAST STRING out of heap memory", TestCastOutOfMemory, ref passed, ref failed);
        if (failed == 0)
            Debug.Log($"Directiva tuple completion: all {passed} tests passed.");
        else
            Debug.LogError($"Directiva tuple completion: {passed} passed, {failed} FAILED. See errors above.");
    }

    private static void Check(string name, Action action, ref int passed, ref int failed)
    {
        try
        {
            action();
            passed++;
            Debug.Log($"[Tuple PASS] {name}");
        }
        catch (Exception ex)
        {
            failed++;
            Debug.LogError($"[Tuple FAIL] {name}: {ex}");
        }
    }

    private static CodeContext Parse(string body)
    {
        string source = "# FUNCTION : main\n# PARAMS : 0\n# LOCALS : 0\n" +
                        body.Trim() + "\n# END_FUNCTION\n";
        return new DILParser(new ModuleSource(source)).Parse("tuple_tests");
    }

    private static DirectivaVM NewVM(string body, ulong maxHeap = HeapMemory.DefaultMaxSize)
    {
        CodeContext code = Parse(body);
        var memory = new VMMemoryContext(code.GlobalCount, maxHeap);
        return new DirectivaVM(code, memory, null);
    }

    private static DataValue RunValue(string body)
    {
        DirectivaVM vm = NewVM(body);
        var result = vm.Run(1000);
        Require(result.Status == VMExecutionStatus.Completed,
            $"VM ended {result.Status}: {result.Error?.Message}");
        return result.ReturnValue;
    }

    private static long RunInt(string body)
    {
        DataValue result = RunValue(body + "\nRETURN");
        Require(result.dataType == DataTypeId.Int, $"Expected Int, got {result.dataType}.");
        return unchecked((long)result.value);
    }

    private static string PrintValue(string body, ulong maxHeap = HeapMemory.DefaultMaxSize)
    {
        DirectivaVM vm = NewVM(body + "\nPRINT\nPUSH NONE\nRETURN", maxHeap);
        string output = null;
        vm.Printed += s => output = s;
        var result = vm.Run(1000);
        Require(result.Status == VMExecutionStatus.Completed,
            $"VM ended {result.Status}: {result.Error?.Message}");
        Require(output != null, "PRINT event was not emitted.");
        return output;
    }

    private static void TestDILGet()
    {
        long result = RunInt("PUSH INT 1\nPUSH INT 2\nPUSH INT 3\nTUPLE 3\nPUSH INT -1\nGET");
        Require(result == 3, $"Expected last element 3, received {result}.");
    }

    private static void TestCopyAndEmpty()
    {
        long len = RunInt("TUPLE 0\nCOPY\nLENGTH");
        Require(len == 0, "COPY/empty tuple/length mismatch.");
    }

    private static void TestLength()
    {
        long len = RunInt("PUSH INT 8\nPUSH INT 9\nTUPLE 2\nLENGTH");
        Require(len == 2, $"Expected 2, received {len}.");
    }

    private static void TestUnicodeLength()
    {
        long len = RunInt("PUSH STRING \"\\uD83D\\uDC0C\"\nLENGTH");
        Require(len == 1, $"Snail emoji should have one code point, received {len}.");
        Require(new StringHeapObject("🐌").Size == 2, "UTF-16 heap size was unexpectedly changed.");
    }

    private static void TestStringFormatting()
    {
        string formatted = PrintValue(
            "PUSH INT 123\nPUSH STRING \"A\\n\\\"B\\\"\"\nTUPLE 2\nPUSH BOOL TRUE\nTUPLE 2");
        Require(formatted == "((123, \"A\\n\\\"B\\\"\"), True)", $"Unexpected tuple print: {formatted}");
        Require(PrintValue("TUPLE 0") == "()", "Empty tuple print mismatch.");
        Require(PrintValue("PUSH INT 3\nTUPLE 1") == "(3,)", "Singleton tuple print mismatch.");
        Require(PrintValue("PUSH STRING \"a\\\\b\"\nTUPLE 1") == "(\"a\\\\b\",)", "Backslash escaping mismatch.");
    }

    private static void TestStringHash()
    {
        long first = RunInt("PUSH STRING \"Chile\\uD83D\\uDC0C\"\nHASH");
        long second = RunInt("PUSH STRING \"Chile\\uD83D\\uDC0C\"\nHASH");
        Require(first == second, "Equal strings in independent heaps must hash identically.");
    }

    private static void TestNumericTupleHash()
    {
        long a = RunInt("PUSH INT 1\nPUSH BOOL TRUE\nTUPLE 2\nHASH");
        long b = RunInt("PUSH BOOL TRUE\nPUSH INT 1\nTUPLE 2\nHASH");
        long c = RunInt("PUSH DECIMAL 1.0\nPUSH INT 1\nTUPLE 2\nHASH");
        Require(a == b && a == c, "Numerically equal elements must yield identical tuple hashes.");
    }

    private static void TestHashOrder()
    {
        long a = RunInt("PUSH INT 1\nPUSH INT 2\nTUPLE 2\nHASH");
        long b = RunInt("PUSH INT 2\nPUSH INT 1\nTUPLE 2\nHASH");
        Require(a != b, "These differently ordered tuples should not collide with the selected mixer.");
    }

    private static void TestNoneHash()
    {
        DataValue result = RunValue("PUSH NONE\nTUPLE 1\nHASH\nRETURN");
        Require(result.dataType == DataTypeId.Int, "None-containing tuple should be hashable.");
    }

    private static void TestNestedUnhashable()
    {
        var memory = new VMMemoryContext(1);
        uint inner = memory.AllocHeap(new TupleHeapObject(new DataValue[]
        {
            new DataValue(DataTypeId.List, 123)
        }));
        uint outer = memory.AllocHeap(new TupleHeapObject(new DataValue[]
        {
            DataValue.FromInt(9), DataValue.FromTupleReference(inner)
        }));
        memory.SetGlobal(0, DataValue.FromTupleReference(outer));
        var call = new OperationCall(new[] { DataValue.FromTupleReference(outer) }, 0, memory, null);
        OperationResult result = DTTuple.Instance.Hash(call);
        Require(result.Status == OperationStatus.Error, "Expected unhashable error.");
        Require(result.Error != null && result.Error.Id == RuntimeErrorId.UnhashableType,
            $"Expected UnhashableType, got {result.Error?.Id}.");
        Require(result.Error.Message.Contains("[1][0]"),
            $"Expected nested path [1][0], got: {result.Error.Message}");
    }

    private static void TestNegativeCount()
    {
        try
        {
            Parse("TUPLE -1\nRETURN");
        }
        catch (DILParseException)
        {
            return;
        }
        throw new Exception("Parser should reject TUPLE -1.");
    }

    private static void TestGCWhileCasting()
    {
        var vm = NewVM(
            "PUSH STRING \"A\"\nPUSH INT 12\nTUPLE 2\nCAST STRING\nPRINT\nPUSH NONE\nRETURN", 9);
        string printed = null;
        vm.Printed += text => printed = text;
        var result = vm.Run(1000);
        Require(result.Status == VMExecutionStatus.Completed,
            $"CAST through GC should complete: {result.Error?.Message}");
        Require(printed == "(\"A\", 12)", $"Unexpected result: {printed}");
        Require(vm.Memory.GarbageCollectionCount > 0, "Expected lazy GC while creating final string.");
    }

    private static void TestCastOutOfMemory()
    {
        var vm = NewVM("PUSH INT 10\nPUSH INT 20\nTUPLE 2\nCAST STRING\nRETURN", 5);
        var result = vm.Run(1000);
        Require(result.Status == VMExecutionStatus.Faulted, "Expected out of memory to fault.");
        Require(result.Error != null && result.Error.Id == RuntimeErrorId.OutOfMemory,
            $"Expected OutOfMemory, got {result.Error?.Id}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}
