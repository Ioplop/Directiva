// Optional Unity Editor smoke tests for the Directiva tuple implementation.
// Runs through Directiva > Tests > DIL > Regression > TestEverything,
// or is selectable in Directiva > Tests > Open Test Runner and Reports.
#if UNITY_EDITOR
using System;
using System.Linq;
using System.Collections.Generic;
using Directiva.EditorTests;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;
using UnityEditor;
using UnityEngine;

internal static class DirectivaTupleSmokeTests
{
    // Previous regression cases are now part of DIL/Regression and receive individual reports.
    internal static void Register(List<DirectivaTestCase> tests)
    {
        void Add(string name, Action<DirectivaTestContext> body) =>
            DirectivaTestCatalog.Add(tests, "Regression", "TupleOriginal" + name,
                "Original tuple regression: " + name, body);
        Add("BasicTuple", t => BasicTuple());
        Add("EmptyTuple", t => EmptyTuple());
        Add("TupleIndexing", t => TupleIndexing());
        Add("StructuralEquality", t => StructuralEquality());
        Add("NestedGarbageCollection", t => NestedGarbageCollection());
        Add("GarbageCollectionDuringConstruction", t => GarbageCollectionDuringConstruction());
        Add("OutOfMemory", t => OutOfMemory());
        Add("UnderflowAndStackCapacity", t => UnderflowAndStackCapacity());
    }

    private static void RunAll()
    {
        BasicTuple();
        EmptyTuple();
        TupleIndexing();
        StructuralEquality();
        NestedGarbageCollection();
        GarbageCollectionDuringConstruction();
        OutOfMemory();
        UnderflowAndStackCapacity();
        Debug.Log("Directiva: all tuple smoke tests passed.");
    }

    private static DirectivaVM Run(VMMemoryContext memory, params Instruction[] code)
    {
        var function = new FunctionContext(0, "test", 0, 0, code);
        var program = new CodeContext(0, memory.GlobalCount, new[] { function });
        var vm = new DirectivaVM(program, memory, null);
        vm.Run(200);
        return vm;
    }

    private static TupleHeapObject Resolve(VMMemoryContext memory, DataValue value)
    {
        Check(value.dataType == DataTypeId.Tuple, "Result is not a tuple.");
        Check(value.TryGetHeapReference(out uint reference), "Missing tuple heap reference.");
        Check(memory.TryGetHeap(reference, out HeapObject obj) && obj is TupleHeapObject,
              "Tuple heap object is missing.");
        return (TupleHeapObject)obj;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void BasicTuple()
    {
        var mem = new VMMemoryContext(1);
        var vm = Run(mem,
            Instruction.Push(DataValue.FromInt(1)),
            Instruction.Push(DataValue.FromInt(2)),
            Instruction.Push(DataValue.FromInt(3)),
            Instruction.Push(DataValue.FromInt(4)),
            Instruction.Push(DataValue.FromInt(5)),
            Instruction.Tuple(3),
            Instruction.StoreGlobal(0),
            Instruction.Pop(),
            Instruction.Pop(),
            Instruction.LoadGlobal(0),
            Instruction.Return());

        Check(vm.Status == VMExecutionStatus.Completed, "Basic tuple program failed.");
        var tuple = Resolve(mem, vm.ReturnValue);
        Check(tuple.Count == 3 && tuple.Size == 4, "Wrong tuple size.");
        for (int i = 0; i < 3; i++)
            Check(tuple.GetElement(i).value == (ulong)(i + 3), "Tuple element order was changed.");
    }

    private static void EmptyTuple()
    {
        var mem = new VMMemoryContext(0);
        var vm = Run(mem, Instruction.Tuple(0), Instruction.Return());
        Check(vm.Status == VMExecutionStatus.Completed, "Empty tuple program failed.");
        Check(Resolve(mem, vm.ReturnValue).Count == 0, "Empty tuple is not empty.");
        Check(mem.Heap.AllocatedSize == 1, "Empty tuple needs exactly one logical heap unit.");
    }

    private static void TupleIndexing()
    {
        var mem = new VMMemoryContext(0);
        var vm = Run(mem,
            Instruction.Push(DataValue.FromInt(12)),
            Instruction.Push(DataValue.FromInt(34)),
            Instruction.Tuple(2),
            Instruction.Push(DataValue.FromInt(-1)),
            Instruction.Operation(OperationId.Get),
            Instruction.Return());
        Check(vm.Status == VMExecutionStatus.Completed, "Tuple indexing program failed.");
        Check(vm.ReturnValue.dataType == DataTypeId.Int && vm.ReturnValue.value == 34,
              "Negative tuple index did not return the final element.");
    }

    private static void StructuralEquality()
    {
        var mem = new VMMemoryContext(0);
        var vm = Run(mem,
            Instruction.Push(DataValue.FromInt(5)),
            Instruction.PushString("text"),
            Instruction.Tuple(2),
            Instruction.Push(DataValue.FromInt(5)),
            Instruction.PushString("text"),
            Instruction.Tuple(2),
            Instruction.Operation(OperationId.ValueEquals),
            Instruction.Return());
        Check(vm.Status == VMExecutionStatus.Completed, "Tuple equality program failed.");
        Check(vm.ReturnValue.dataType == DataTypeId.Bool && vm.ReturnValue.value == 1,
              "Tuples with equal contents were not structurally equal.");
    }

    private static void NestedGarbageCollection()
    {
        var mem = new VMMemoryContext(1, 7);
        var vm = Run(mem,
            Instruction.PushString("abcd"), // String = 2 units
            Instruction.Tuple(1),           // Inner tuple = 2 units
            Instruction.StoreGlobal(0),
            Instruction.LoadGlobal(0),
            Instruction.LoadGlobal(0),
            Instruction.Tuple(2),           // Outer tuple = 3 units
            Instruction.Return());
        Check(vm.Status == VMExecutionStatus.Completed, "Nested tuple program failed.");
        Check(mem.Heap.AllocatedSize == 7, "Nested objects have incorrect accounting.");
        Check(mem.CollectGarbage() == 0, "Live nested references were collected.");
        var outer = Resolve(mem, vm.ReturnValue);
        Check(outer.Count == 2 && outer.GetElement(0).value == outer.GetElement(1).value,
              "Shared tuple reference was not preserved.");
        var inner = Resolve(mem, outer.GetElement(0));
        Check(inner.Count == 1 && inner.GetElement(0).dataType == DataTypeId.String,
              "Nested string reference is missing.");
    }

    private static void GarbageCollectionDuringConstruction()
    {
        var mem = new VMMemoryContext(1, 6);
        var vm = Run(mem,
            Instruction.PushString("abcd"),
            Instruction.Tuple(1),
            Instruction.StoreGlobal(0),
            Instruction.PushString("junk"),
            Instruction.Pop(),
            Instruction.PushString("new!"), // Lazy GC must preserve the nested tuple and child.
            Instruction.Pop(),
            Instruction.LoadGlobal(0),
            Instruction.Return());
        Check(vm.Status == VMExecutionStatus.Completed, "Lazy collection program failed.");
        Check(mem.GarbageCollectionCount > 0, "Expected lazy collection did not run.");
        Check(mem.CollectGarbage() == 1, "Expected one unreferenced string after the run.");
        Check(mem.Heap.AllocatedSize == 4, "Garbage collection did not retain exact live size.");
        var tuple = Resolve(mem, vm.ReturnValue);
        Check(tuple.GetElement(0).TryGetHeapReference(out uint strRef) &&
              mem.TryGetHeap(strRef, out HeapObject strObj) && strObj is StringHeapObject,
              "Tuple child was collected even though reachable.");
    }

    private static void OutOfMemory()
    {
        var mem = new VMMemoryContext(0, 3);
        var vm = Run(mem,
            Instruction.PushString("abcd"), // 2 units; still a root during TUPLE
            Instruction.Tuple(1),           // 2 more units -> OOM
            Instruction.Return());
        Check(vm.Status == VMExecutionStatus.Faulted && vm.Error?.Id == RuntimeErrorId.OutOfMemory,
              "Expected OutOfMemory when tuple elements cannot be collected.");
        Check(mem.Heap.AllocatedSize == 2 && mem.GarbageCollectionCount > 0,
              "OutOfMemory occurred after deleting a still-rooted child.");
    }

    private static void UnderflowAndStackCapacity()
    {
        var mem = new VMMemoryContext(0);
        var underflow = Run(mem,
            Instruction.Push(DataValue.FromInt(1)),
            Instruction.Tuple(2),
            Instruction.Return());
        Check(underflow.Error?.Id == RuntimeErrorId.StackUnderflow,
              "TUPLE with insufficient stack values must fault with underflow.");

        var program = new CodeContext(0, 0, new[] {
            new FunctionContext(0, "full", 0, 0,
                Instruction.Push(DataValue.FromInt(1)),
                Instruction.Tuple(0),
                Instruction.Return())
        });
        var boundedMem = new VMMemoryContext(0);
        var full = new DirectivaVM(program, boundedMem, new VMOptions(maxEvaluationStackSize: 1));
        full.Run(10);
        Check(full.Error?.Id == RuntimeErrorId.StackOverflow,
              "TUPLE 0 must check result stack capacity before allocating.");
        Check(boundedMem.Heap.ObjectCount == 0, "A failed TUPLE 0 allocated an object.");
    }
}
#endif
