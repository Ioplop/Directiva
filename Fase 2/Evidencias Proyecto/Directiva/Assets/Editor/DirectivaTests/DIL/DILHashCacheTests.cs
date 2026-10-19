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
    /// Cache invariants for immutable String and Tuple heap objects. Tests use real
    /// OperationId.Hash dispatch and inspect the cached state, without timing assumptions.
    /// </summary>
    internal static class DILHashCacheTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> action)
                => DirectivaTestCatalog.Add(tests, "Hashes", id, description, action);

            Add("StringCachesFirstHash", "String caches its first hash without changing the logical heap charge", t =>
            {
                var memory = new VMMemoryContext(0);
                var value = NewString(t, memory, "A🐌Z", out StringHeapObject obj);
                t.Check(!obj.TryGetCachedHash(out _), "new string is not cached");
                ulong originalCharge = memory.Heap.AllocatedSize;
                long expected = Hash(t, memory, value);
                t.Check(obj.TryGetCachedHash(out long cached), "successful hash is cached");
                t.Equal(expected, cached, "cached string value");
                t.Equal(expected, Hash(t, memory, value), "repeated string hash");
                t.Equal(originalCharge, memory.Heap.AllocatedSize, "hashing does not alter logical size");
            });

            Add("StringCachePerObject", "Equal strings allocated separately have independent equal hash caches", t =>
            {
                var memory = new VMMemoryContext(0);
                var left = NewString(t, memory, "same content", out StringHeapObject a);
                var right = NewString(t, memory, "same content", out StringHeapObject b);
                long hash = Hash(t, memory, left);
                t.Check(a.TryGetCachedHash(out _), "first string is cached");
                t.Check(!b.TryGetCachedHash(out _), "second string is initially uncached");
                t.Equal(hash, Hash(t, memory, right), "equal contents have equal hashes");
                t.Check(b.TryGetCachedHash(out _), "second object acquires its own cache");
            });

            Add("EmptyTupleCache", "An empty tuple caches its valid hash", t =>
            {
                var memory = new VMMemoryContext(0);
                var tupleValue = NewTuple(t, memory, Array.Empty<DataValue>(), out TupleHeapObject tuple);
                t.Check(!tuple.TryGetCachedHash(out _), "empty tuple initially uncached");
                long expected = Hash(t, memory, tupleValue);
                t.Check(tuple.TryGetCachedHash(out long stored), "empty tuple cached");
                t.Equal(expected, stored, "stored hash");
                t.Equal(expected, Hash(t, memory, tupleValue), "repeated empty tuple hash");
                t.Equal(1u, tuple.Size, "empty tuple logical size remains 1");
            });

            Add("NestedCachesAllCompletedObjects", "Nested hashing caches each finished tuple and its string leaves", t =>
            {
                var memory = new VMMemoryContext(0);
                DataValue stringValue = NewString(t, memory, "nested", out StringHeapObject str);
                DataValue childValue = NewTuple(t, memory,
                    new[] { stringValue, DataValue.FromInt(7) }, out TupleHeapObject child);
                DataValue parentValue = NewTuple(t, memory,
                    new[] { DataValue.FromBool(true), childValue }, out TupleHeapObject parent);
                t.Check(!str.TryGetCachedHash(out _), "string initially uncached");
                t.Check(!child.TryGetCachedHash(out _), "child initially uncached");
                t.Check(!parent.TryGetCachedHash(out _), "parent initially uncached");
                long computed = Hash(t, memory, parentValue);
                t.Check(str.TryGetCachedHash(out _), "nested string cache was populated");
                t.Check(child.TryGetCachedHash(out _), "nested tuple cache was populated");
                t.Check(parent.TryGetCachedHash(out long cached), "parent tuple cache was populated");
                t.Equal(computed, cached, "parent cache value");
                t.Equal(computed, Hash(t, memory, parentValue), "parent hash reuse");
            });

            Add("CachedChildPreservesStructuralHash", "A parent uses a previously cached child without changing its hash", t =>
            {
                var memory = new VMMemoryContext(0);
                DataValue childValue = NewTuple(t, memory,
                    new[] { DataValue.FromInt(3), DataValue.FromBool(false) }, out TupleHeapObject child);
                long childHash = Hash(t, memory, childValue);
                t.Check(child.TryGetCachedHash(out long cachedChild), "child hash cached first");
                t.Equal(childHash, cachedChild, "child cache value");
                DataValue cachedParent = NewTuple(t, memory,
                    new[] { childValue, DataValue.FromInt(5) }, out TupleHeapObject parent);
                long first = Hash(t, memory, cachedParent);
                t.Check(parent.TryGetCachedHash(out _), "parent cached");
                DataValue equivalentChild = NewTuple(t, memory,
                    new[] { DataValue.FromInt(3), DataValue.FromBool(false) }, out _);
                DataValue equivalentParent = NewTuple(t, memory,
                    new[] { equivalentChild, DataValue.FromInt(5) }, out _);
                t.Equal(first, Hash(t, memory, equivalentParent), "cached and fresh traversal match");
            });

            Add("UnhashableNestedTupleNeverCached", "Nested unhashable lists produce an error path and no partial tuple cache", t =>
            {
                var memory = new VMMemoryContext(0);
                t.Check(memory.TryAllocHeap(new ListHeapObject(new[] { DataValue.FromInt(9) }),
                    out uint listRef), "list allocated");
                DataValue badChild = NewTuple(t, memory,
                    new[] { DataValue.FromInt(7), DataValue.FromListReference(listRef) },
                    out TupleHeapObject child);
                DataValue parent = NewTuple(t, memory,
                    new[] { DataValue.FromBool(true), badChild }, out TupleHeapObject root);
                for (int i = 0; i < 2; i++)
                {
                    OperationResult result = Compute(memory, parent);
                    t.Equal(OperationStatus.Error, result.Status, "unhashable result");
                    t.Check(result.Error != null, "error details");
                    t.Equal(RuntimeErrorId.UnhashableType, result.Error.Id, "error ID");
                    t.Contains("[1][1]", result.Error.Message, "path to unhashable list");
                    t.Check(!root.TryGetCachedHash(out _), "root not cached after failed hash");
                    t.Check(!child.TryGetCachedHash(out _), "unhashable child not cached");
                }
            });

            Add("CompletedChildMayCacheBeforeParentFailure", "A successful child hash stays cached even if a later sibling is unhashable", t =>
            {
                var memory = new VMMemoryContext(0);
                DataValue good = NewTuple(t, memory,
                    new[] { DataValue.FromInt(12) }, out TupleHeapObject completed);
                t.Check(memory.TryAllocHeap(new ListHeapObject(Array.Empty<DataValue>()),
                    out uint listRef), "list allocated");
                DataValue parent = NewTuple(t, memory,
                    new[] { good, DataValue.FromListReference(listRef) }, out TupleHeapObject root);
                OperationResult result = Compute(memory, parent);
                t.Equal(OperationStatus.Error, result.Status, "parent unhashable");
                t.Check(completed.TryGetCachedHash(out long goodHash), "valid child cached");
                t.Equal(goodHash, Hash(t, memory, good), "valid child hash remains reusable");
                t.Check(!root.TryGetCachedHash(out _), "failed parent is never cached");
            });

            Add("ZeroCacheValueNotConfusedWithMissing", "Zero is a valid cache value, not a cache-miss sentinel", t =>
            {
                var str = new StringHeapObject("zero-slot");
                var tuple = new TupleHeapObject(Array.Empty<DataValue>());
                t.Check(!str.TryGetCachedHash(out _), "string cache initially empty");
                t.Check(!tuple.TryGetCachedHash(out _), "tuple cache initially empty");
                str.CacheHash(0);
                tuple.CacheHash(0);
                t.Check(str.TryGetCachedHash(out long s), "string cache has zero");
                t.Check(tuple.TryGetCachedHash(out long tp), "tuple cache has zero");
                t.Equal(0L, s, "string cached zero");
                t.Equal(0L, tp, "tuple cached zero");
            });

            Add("HashConsistencyAcrossNumericTypes", "Cached tuple hashes preserve Int/Bool/Decimal numeric equality", t =>
            {
                var memory = new VMMemoryContext(0);
                var a = NewTuple(t, memory, new[] { DataValue.FromInt(1), DataValue.FromBool(true) }, out _);
                var b = NewTuple(t, memory, new[] {
                    DataValue.FromDecimalRaw(DTDecimal.Scale), DataValue.FromInt(1)
                }, out _);
                var c = NewTuple(t, memory, new[] {
                    DataValue.FromBool(true), DataValue.FromInt(1)
                }, out _);
                long expected = Hash(t, memory, a);
                t.Equal(expected, Hash(t, memory, b), "Int and Decimal tuple hashes agree");
                t.Equal(expected, Hash(t, memory, c), "Bool and Int tuple hashes agree");
                t.Equal(expected, Hash(t, memory, b), "cached decimal tuple hash stays equal");
            });

            Add("CachesSurviveMarkAndSweep", "GC preserves cached hashes on reachable nested objects", t =>
            {
                var memory = new VMMemoryContext(1);
                DataValue str = NewString(t, memory, "retained", out StringHeapObject text);
                DataValue tuple = NewTuple(t, memory, new[] { str }, out TupleHeapObject root);
                memory.SetGlobal(0, tuple);
                long hash = Hash(t, memory, tuple);
                t.Check(memory.TryAllocHeap(new StringHeapObject("garbage"), out uint garbage),
                    "unreachable string allocated");
                int collected = memory.CollectGarbage();
                t.Check(collected >= 1, "unreachable object was collected");
                t.Check(!memory.TryGetHeap(garbage, out _), "garbage no longer in heap");
                t.Check(text.TryGetCachedHash(out _), "reachable string cache survives");
                t.Check(root.TryGetCachedHash(out _), "reachable tuple cache survives");
                t.Equal(hash, Hash(t, memory, tuple), "hash remains valid after sweep");
            });

            Add("DILRepeatedHashUsesCachedObject", "Repeated HASH through the DIL parser and VM keeps its result", t =>
            {
                DILTestHarness.ExpectBool(t,
                    "PUSH STRING \"repeat\"\nTUPLE 1\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nHASH\nSTORE_LOCAL 1\n" +
                    "LOAD_LOCAL 0\nHASH\nLOAD_LOCAL 1\nVALUE_EQUALS\nRETURN",
                    true, locals: 2);
            });

            Add("DeepNestingIterativeAndCached", "Hundreds of nested tuples hash iteratively and store intermediate caches", t =>
            {
                const int depth = 384;
                var memory = new VMMemoryContext(0);
                DataValue top = DataValue.FromInt(42);
                TupleHeapObject deepest = null;
                for (int i = 0; i < depth; i++)
                {
                    top = NewTuple(t, memory, new[] { top }, out TupleHeapObject obj);
                    if (i == 0) deepest = obj;
                }
                long hash = Hash(t, memory, top);
                var root = (TupleHeapObject)memory.GetHeap((uint)top.value);
                t.Check(root.TryGetCachedHash(out _), "root cached");
                t.Check(deepest.TryGetCachedHash(out _), "innermost cached");
                t.Equal(hash, Hash(t, memory, top), "deep hash is stable");
                t.Note("Nested tuple depth=" + depth + ", heap units=" + memory.Heap.AllocatedSize);
            });
        }

        private static DataValue NewString(DirectivaTestContext t, VMMemoryContext memory,
            string value, out StringHeapObject obj)
        {
            obj = new StringHeapObject(value);
            t.Check(memory.TryAllocHeap(obj, out uint reference), "allocate String");
            return DataValue.FromStringReference(reference);
        }

        private static DataValue NewTuple(DirectivaTestContext t, VMMemoryContext memory,
            DataValue[] items, out TupleHeapObject obj)
        {
            obj = new TupleHeapObject(items);
            t.Check(memory.TryAllocHeap(obj, out uint reference), "allocate Tuple");
            return DataValue.FromTupleReference(reference);
        }

        private static OperationResult Compute(VMMemoryContext memory, DataValue value)
        {
            DataType type = DataType.GetDataType(value.dataType);
            var operands = new[] { value };
            return type.Operate(OperationId.Hash, new OperationCall(operands, 0, memory, null));
        }

        private static long Hash(DirectivaTestContext t, VMMemoryContext memory, DataValue value)
        {
            OperationResult result = Compute(memory, value);
            t.Equal(OperationStatus.Success, result.Status, "HASH success");
            t.Equal(DataTypeId.Int, result.Value.dataType, "HASH result type");
            return unchecked((long)result.Value.value);
        }
    }
}
