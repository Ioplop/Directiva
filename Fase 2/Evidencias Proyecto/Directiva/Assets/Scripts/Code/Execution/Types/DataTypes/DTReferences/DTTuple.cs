using System;
using System.Collections.Generic;
using System.Text;
using System.Globalization;
using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace DSExecution.DataTypes
{
    /// <summary>
    /// Heap-backed immutable tuple. Supports indexing, copying by reference,
    /// structural equality, identity equality, concatenation, length, hashing,
    /// string casting and truthiness.
    /// </summary>
    public sealed class DTTuple : DataType, IOpContainer, IOpReferenceEquality, IOpArithmetic, IOpHash, IOpLength
    {
        public static DTTuple Instance { get; } = new DTTuple();

        private DTTuple() { }

        private static bool TryResolve(
            IMemContext memory,
            DataValue value,
            out TupleHeapObject tuple,
            out OperationResult failure)
        {
            tuple = null;
            failure = default;

            if (value.dataType != DataTypeId.Tuple)
            {
                failure = OperationResult.NotImplemented();
                return false;
            }

            if (!value.TryGetHeapReference(out uint reference))
            {
                failure = OperationResult.OpError(
                    RuntimeError.InvalidOperation("Tuple contains an invalid heap reference."));
                return false;
            }

            if (!memory.TryGetHeap(reference, out HeapObject heapObject) ||
                !(heapObject is TupleHeapObject resolved))
            {
                failure = OperationResult.OpError(RuntimeError.InvalidMemoryAccess(reference));
                return false;
            }

            tuple = resolved;
            return true;
        }

        /// <summary>
        /// Returns the element at an Int/Bool index; negative indices count from the end.
        /// </summary>
        public OperationResult Get(OperationCall opCall)
        {
            if (opCall.Arguments.Length != 2 ||
                opCall.Arguments[0].dataType != DataTypeId.Tuple)
                return OperationResult.NotImplemented();

            DataValue indexValue = opCall.Arguments[1];
            if (indexValue.dataType != DataTypeId.Int &&
                indexValue.dataType != DataTypeId.Bool)
                return OperationResult.NotImplemented();

            if (!TryResolve(opCall.Memory, opCall.Arguments[0], out var tuple, out var failure))
                return failure;

            long index = unchecked((long)indexValue.value);
            long resolvedIndex = index < 0 ? tuple.Count + index : index;

            if (resolvedIndex < 0 || resolvedIndex >= tuple.Count)
                return OperationResult.OpError(RuntimeError.IndexOutOfRange(index, tuple.Count, "Tuple"));

            DataValue item = tuple.GetElement((int)resolvedIndex);
            if (item.dataType == DataTypeId.Uninitialized)
                return OperationResult.OpError(RuntimeError.VariableUsedBeforeInitialization());

            return OperationResult.Success(item);
        }

        /// <summary>
        /// Immutable tuples can safely be copied by sharing their heap identity.
        /// </summary>
        public OperationResult Copy(OperationCall opCall)
        {
            if (opCall.Arguments.Length != 1 || opCall.Arguments[0].dataType != DataTypeId.Tuple)
                return OperationResult.NotImplemented();

            if (!TryResolve(opCall.Memory, opCall.Arguments[0], out _, out var failure))
                return failure;

            return OperationResult.Success(opCall.Arguments[0]);
        }

        public override OperationResult IsTruthy(OperationCall opCall)
        {
            if (opCall.Arguments.Length != 1 || opCall.Arguments[0].dataType != DataTypeId.Tuple)
                return OperationResult.NotImplemented();

            if (!TryResolve(opCall.Memory, opCall.Arguments[0], out var tuple, out var failure))
                return failure;

            return OperationResult.Success(DataValue.FromBool(tuple.Count != 0));
        }

        public OperationResult ReferenceEquals(OperationCall opCall)
        {
            if (opCall.Arguments.Length != 2)
                return OperationResult.NotImplemented();

            DataValue left = opCall.Arguments[0];
            DataValue right = opCall.Arguments[1];
            return OperationResult.Success(DataValue.FromBool(
                left.dataType == DataTypeId.Tuple &&
                right.dataType == DataTypeId.Tuple &&
                left.value == right.value));
        }

        /// <summary>
        /// Structural equality is delegated to the same cycle-safe comparer used
        /// by lists, including tuple/list/tuple nesting.
        /// </summary>
        public override OperationResult ValueEquals(OperationCall opCall)
        {
            if (opCall.Arguments.Length != 2 ||
                opCall.Arguments[0].dataType != DataTypeId.Tuple ||
                opCall.Arguments[1].dataType != DataTypeId.Tuple)
                return OperationResult.NotImplemented();
            return SequenceEquality.Compare(opCall.Memory,
                opCall.Arguments[0], opCall.Arguments[1]);
        }

        /// <summary>
        /// Concatenation allocates a new tuple; the existing tuples are never mutated.
        /// VM operation arguments stay rooted during allocation and possible lazy GC.
        /// </summary>
        public OperationResult Add(OperationCall opCall)
        {
            if (opCall.Arguments.Length != 2 ||
                opCall.Arguments[0].dataType != DataTypeId.Tuple ||
                opCall.Arguments[1].dataType != DataTypeId.Tuple)
                return OperationResult.NotImplemented();

            if (!TryResolve(opCall.Memory, opCall.Arguments[0], out var left, out var leftFailure))
                return leftFailure;
            if (!TryResolve(opCall.Memory, opCall.Arguments[1], out var right, out var rightFailure))
                return rightFailure;

            long total = (long)left.Count + right.Count;
            if (total > int.MaxValue)
                return OperationResult.OpError(RuntimeError.OutOfMemory());

            var elements = new DataValue[(int)total];
            left.Items.CopyTo(elements.AsSpan());
            right.Items.CopyTo(elements.AsSpan(left.Count));

            if (!opCall.Memory.TryAllocHeap(new TupleHeapObject(elements), out uint reference))
                return OperationResult.OpError(RuntimeError.OutOfMemory());

            return OperationResult.Success(DataValue.FromTupleReference(reference));
        }

        /// <summary>
        /// Hashes each element in order. Successful tuple hashes (including nested
        /// tuples) are cached on their immutable heap objects. Repeated calls are O(1).
        /// Uncached tuples are traversed iteratively to avoid exhausting the host stack.
        /// Hashes use element semantics, preserving cross-type numeric equality.
        /// </summary>
        public OperationResult Hash(OperationCall opCall)
        {
            if (opCall.Arguments.Length != 1 ||
                opCall.Arguments[0].dataType != DataTypeId.Tuple)
                return OperationResult.NotImplemented();

            if (!TryResolve(opCall.Memory, opCall.Arguments[0], out var root, out var failure))
                return failure;

            if (root.TryGetCachedHash(out long rootHash))
                return OperationResult.Success(DataValue.FromInt(rootHash));

            uint rootReference = (uint)opCall.Arguments[0].value;
            var pending = new Stack<HashFrame>();
            var active = new HashSet<uint>();
            pending.Push(new HashFrame(rootReference, root));
            active.Add(rootReference);

            while (pending.Count > 0)
            {
                HashFrame current = pending.Peek();
                if (current.NextIndex == current.Tuple.Count)
                {
                    ulong completedHash = current.Finish();
                    // Cache only after this tuple's entire hash has succeeded.
                    current.Tuple.CacheHash(unchecked((long)completedHash));
                    pending.Pop();
                    active.Remove(current.Reference);

                    if (pending.Count == 0)
                        return OperationResult.Success(DataValue.FromInt(unchecked((long)completedHash)));

                    pending.Peek().Combine(completedHash);
                    continue;
                }

                DataValue child = current.Tuple.GetElement(current.NextIndex++);
                if (child.dataType == DataTypeId.Tuple)
                {
                    if (!TryResolve(opCall.Memory, child, out var nested, out var nestedFailure))
                        return nestedFailure;

                    uint nestedReference = (uint)child.value;
                    if (active.Contains(nestedReference))
                        return OperationResult.OpError(RuntimeError.InvalidOperation(
                            "Cyclic tuples cannot be hashed."));

                    if (nested.TryGetCachedHash(out long childHash))
                    {
                        current.Combine(unchecked((ulong)childHash));
                        continue;
                    }

                    active.Add(nestedReference);
                    pending.Push(new HashFrame(nestedReference, nested));
                    continue;
                }

                // Dispatch directly so a missing hash implementation can produce a useful
                // error containing the path to the offending tuple element.
                DataType dataType = GetDataType(child.dataType);
                if (dataType == null)
                    return OperationResult.OpError(RuntimeError.UnhashableType(
                        child.dataType, BuildElementPath(pending)));

                var arguments = new[] { child };
                var call = new OperationCall(arguments, 0, opCall.Memory, null);
                OperationResult childHash = dataType.Operate(OperationId.Hash, call);

                if (childHash.Status == OperationStatus.NotImplemented)
                    return OperationResult.OpError(RuntimeError.UnhashableType(
                        child.dataType, BuildElementPath(pending)));

                if (childHash.Status == OperationStatus.Error)
                    return childHash;

                if (childHash.Status != OperationStatus.Success ||
                    childHash.Value.dataType != DataTypeId.Int)
                {
                    return OperationResult.OpError(RuntimeError.InternalVmError(
                        $"Hash of {child.dataType} must return Int synchronously."));
                }

                current.Combine(childHash.Value.value);
            }

            return OperationResult.OpError(RuntimeError.InternalVmError(
                "Tuple hash traversal completed without a result."));
        }

        private sealed class HashFrame
        {
            private const ulong Offset = 14695981039346656037UL;
            private const ulong Prime = 1099511628211UL;

            public uint Reference { get; }
            public TupleHeapObject Tuple { get; }
            public int NextIndex { get; set; }
            private ulong accumulator = Offset;

            public HashFrame(uint reference, TupleHeapObject tuple)
            {
                Reference = reference;
                Tuple = tuple;
            }

            public void Combine(ulong elementHash)
            {
                unchecked
                {
                    accumulator ^= elementHash;
                    accumulator *= Prime;
                }
            }

            public ulong Finish()
            {
                unchecked
                {
                    ulong result = accumulator ^ (ulong)Tuple.Count;
                    return result * Prime;
                }
            }
        }

        private static string BuildElementPath(Stack<HashFrame> pending)
        {
            // Stack.ToArray returns frames deepest-first; reverse for root-first indexes.
            HashFrame[] frames = pending.ToArray();
            var path = new StringBuilder();
            for (int i = frames.Length - 1; i >= 0; i--)
                path.Append('[').Append(frames[i].NextIndex - 1).Append(']');
            return path.ToString();
        }

        public OperationResult Length(OperationCall opCall)
        {
            if (opCall.Arguments.Length != 1 ||
                opCall.Arguments[0].dataType != DataTypeId.Tuple)
                return OperationResult.NotImplemented();

            if (!TryResolve(opCall.Memory, opCall.Arguments[0], out var tuple, out var failure))
                return failure;

            return OperationResult.Success(DataValue.FromInt(tuple.Count));
        }

        /// <summary>
        /// CAST STRING shares the list/tuple formatter, including quoting and
        /// cyclic mixed structures. No separate repr() operation is exposed.
        /// </summary>
        public override CastResult CastTo(
            DataTypeId targetType, DataValue value, VMMemoryContext memory)
        {
            if (targetType != DataTypeId.String || value.dataType != DataTypeId.Tuple)
                return base.CastTo(targetType, value, memory);
            if (!SequenceFormatter.TryFormat(value, memory, out string text,
                    out RuntimeError error))
                return CastResult.Failure(error);
            return DTString.CreateValue(text, memory);
        }

        // Mutable container operations cannot change an immutable tuple.
        public OperationResult Append(OperationCall opCall) => OperationResult.NotImplemented();
        public OperationResult Insert(OperationCall opCall) => OperationResult.NotImplemented();
        public OperationResult Delete(OperationCall opCall) => OperationResult.NotImplemented();
        public OperationResult Pop(OperationCall opCall) => OperationResult.NotImplemented();
        public OperationResult Set(OperationCall opCall) => OperationResult.NotImplemented();

        public OperationResult Subtract(OperationCall opCall) => OperationResult.NotImplemented();
        public OperationResult Multiply(OperationCall opCall) => OperationResult.NotImplemented();
        public OperationResult Divide(OperationCall opCall) => OperationResult.NotImplemented();
        public OperationResult FloorDivide(OperationCall opCall) => OperationResult.NotImplemented();
        public OperationResult Modulo(OperationCall opCall) => OperationResult.NotImplemented();
        public OperationResult Power(OperationCall opCall) => OperationResult.NotImplemented();
    }
}
