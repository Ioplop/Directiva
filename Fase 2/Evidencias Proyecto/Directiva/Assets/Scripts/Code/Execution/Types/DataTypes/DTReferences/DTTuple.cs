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
        /// Compares tuple contents iteratively to avoid host call-stack overflow for
        /// deeply nested tuples. Non-tuple elements use standard operation dispatch.
        /// </summary>
        public override OperationResult ValueEquals(OperationCall opCall)
        {
            if (opCall.Arguments.Length != 2 ||
                opCall.Arguments[0].dataType != DataTypeId.Tuple ||
                opCall.Arguments[1].dataType != DataTypeId.Tuple)
                return OperationResult.NotImplemented();

            var pending = new Stack<(DataValue Left, DataValue Right)>();
            pending.Push((opCall.Arguments[0], opCall.Arguments[1]));

            while (pending.Count > 0)
            {
                var pair = pending.Pop();
                if (!TryResolve(opCall.Memory, pair.Left, out var left, out var leftFailure))
                    return leftFailure;
                if (!TryResolve(opCall.Memory, pair.Right, out var right, out var rightFailure))
                    return rightFailure;

                if (left.Count != right.Count)
                    return OperationResult.Success(DataValue.FromBool(false));

                for (int i = left.Count - 1; i >= 0; i--)
                {
                    DataValue a = left.GetElement(i);
                    DataValue b = right.GetElement(i);

                    if (a.dataType == DataTypeId.Tuple && b.dataType == DataTypeId.Tuple)
                    {
                        pending.Push((a, b));
                        continue;
                    }

                    var result = OperationDispatcher.Start(
                        OperationId.ValueEquals, new[] { a, b }, opCall.Memory).Result;
                    if (result.Status != OperationStatus.Success)
                    {
                        return result.Status == OperationStatus.Continued
                            ? OperationResult.OpError(RuntimeError.InternalVmError(
                                "A continued equality operation cannot be nested in tuple equality."))
                            : result;
                    }
                    if (result.Value.dataType != DataTypeId.Bool)
                        return OperationResult.OpError(RuntimeError.InternalVmError(
                            "ValueEquals did not return Bool while comparing tuple elements."));
                    if (result.Value.value == 0)
                        return OperationResult.Success(DataValue.FromBool(false));
                }
            }

            return OperationResult.Success(DataValue.FromBool(true));
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
        /// Hashes each element in order. Nested tuples are traversed iteratively so deep
        /// nesting cannot exhaust the host C# call stack. Hashes use the element's own
        /// semantics (not its DataTypeId), preserving cross-type numeric equality.
        /// </summary>
        public OperationResult Hash(OperationCall opCall)
        {
            if (opCall.Arguments.Length != 1 ||
                opCall.Arguments[0].dataType != DataTypeId.Tuple)
                return OperationResult.NotImplemented();

            if (!TryResolve(opCall.Memory, opCall.Arguments[0], out var root, out var failure))
                return failure;

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
                    if (!active.Add(nestedReference))
                        return OperationResult.OpError(RuntimeError.InvalidOperation(
                            "Cyclic tuples cannot be hashed."));

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
        /// Uses the existing CAST instruction. The tuple assembles a single final
        /// string; contained strings are quoted and nested tuples are traversed
        /// iteratively. Scalar CastTo(String) calls may allocate temporary strings.
        /// </summary>
        public override CastResult CastTo(
            DataTypeId targetType,
            DataValue value,
            VMMemoryContext memory)
        {
            if (targetType != DataTypeId.String || value.dataType != DataTypeId.Tuple)
                return base.CastTo(targetType, value, memory);

            if (!TryResolve(memory, value, out var root, out var rootFailure))
                return CastResult.Failure(rootFailure.Error ?? RuntimeError.InvalidCastValue(
                    value.dataType, targetType, "Invalid tuple reference."));

            uint rootReference = (uint)value.value;
            var builder = new StringBuilder();
            var pending = new Stack<FormatFrame>();
            var active = new HashSet<uint>();
            pending.Push(new FormatFrame(rootReference, root));
            active.Add(rootReference);
            builder.Append('(');

            while (pending.Count > 0)
            {
                FormatFrame current = pending.Peek();
                if (current.NextIndex == current.Tuple.Count)
                {
                    if (current.Tuple.Count == 1)
                        builder.Append(',');
                    builder.Append(')');
                    pending.Pop();
                    active.Remove(current.Reference);
                    continue;
                }

                if (current.NextIndex > 0)
                    builder.Append(", ");

                DataValue child = current.Tuple.GetElement(current.NextIndex++);
                if (child.dataType == DataTypeId.Tuple)
                {
                    if (!TryResolve(memory, child, out var nested, out var nestedFailure))
                        return CastResult.Failure(nestedFailure.Error ?? RuntimeError.InvalidCastValue(
                            child.dataType, targetType, "Invalid nested tuple reference."));

                    uint nestedReference = (uint)child.value;
                    if (!active.Add(nestedReference))
                        return CastResult.Failure(RuntimeError.InvalidOperation(
                            "Cyclic tuples cannot be formatted."));

                    builder.Append('(');
                    pending.Push(new FormatFrame(nestedReference, nested));
                    continue;
                }

                if (child.dataType == DataTypeId.String)
                {
                    if (!TryReadString(memory, child, out string stringValue, out var stringError))
                        return CastResult.Failure(stringError);

                    AppendQuoted(builder, stringValue);
                    continue;
                }

                if (child.dataType == DataTypeId.Uninitialized)
                    return CastResult.Failure(RuntimeError.VariableUsedBeforeInitialization());

                DataType sourceType = GetDataType(child.dataType);
                if (sourceType == null)
                    return CastResult.Failure(RuntimeError.InvalidCast(child.dataType, targetType));

                CastResult cast = sourceType.CastTo(DataTypeId.String, child, memory);
                if (!cast.IsSuccess)
                    return cast;

                if (cast.Value.dataType != DataTypeId.String)
                    return CastResult.Failure(RuntimeError.InternalVmError(
                        $"Cast from {child.dataType} to String returned {cast.Value.dataType}."));

                if (!TryReadString(memory, cast.Value, out string leafText, out var leafError))
                    return CastResult.Failure(leafError);

                builder.Append(leafText);
            }

            return DTString.CreateValue(builder.ToString(), memory);
        }

        private sealed class FormatFrame
        {
            public uint Reference { get; }
            public TupleHeapObject Tuple { get; }
            public int NextIndex { get; set; }

            public FormatFrame(uint reference, TupleHeapObject tuple)
            {
                Reference = reference;
                Tuple = tuple;
            }
        }

        private static bool TryReadString(
            IMemContext memory,
            DataValue value,
            out string text,
            out RuntimeError error)
        {
            text = string.Empty;
            error = null;
            if (!value.TryGetHeapReference(out uint reference))
            {
                error = RuntimeError.InvalidOperation("Invalid string heap reference.");
                return false;
            }

            if (!memory.TryGetHeap(reference, out HeapObject obj) ||
                !(obj is StringHeapObject stringObject))
            {
                error = RuntimeError.InvalidMemoryAccess(reference);
                return false;
            }

            text = stringObject.Value;
            return true;
        }

        private static void AppendQuoted(StringBuilder builder, string text)
        {
            builder.Append('"');
            foreach (char c in text)
            {
                switch (c)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (char.IsControl(c))
                        {
                            builder.Append("\\u");
                            builder.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }
            builder.Append('"');
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
