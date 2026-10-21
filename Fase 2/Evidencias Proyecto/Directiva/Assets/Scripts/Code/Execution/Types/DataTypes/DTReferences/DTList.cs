using System;
using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace DSExecution.DataTypes
{
    /// <summary>
    /// Mutable heap list. Constant-time operations mutate after TryResizeHeap, while
    /// costly shifts/copies are incremental and publish via an O(1) content swap.
    /// Pending operands and a VM-owned auxiliary pointer protect all heap references.
    /// </summary>
    public sealed class DTList : DataType, IOpContainer, IOpExtend, IOpSearch,
        IOpArithmetic, IOpReferenceEquality, IOpLength
    {
        private const int ItemsPerStep = 16;
        [DScriptGetter("length")]
        public OperationResult GetLengthMember(OperationCall call) => Length(call);

        [DScriptMethod("append", 1)]
        public OperationResult AppendMember(OperationCall call) => Append(call);

        [DScriptMethod("insert", 2)]
        public OperationResult InsertMember(OperationCall call) => Insert(call);

        [DScriptMethod("extend", 1)]
        public OperationResult ExtendMember(OperationCall call) => Extend(call);

        [DScriptMethod("contains", 1)]
        public OperationResult ContainsMember(OperationCall call) => Contains(call);

        [DScriptMethod("indexOf", 1)]
        public OperationResult IndexOfMember(OperationCall call) => IndexOf(call);

        public static DTList Instance { get; } = new DTList();
        private DTList() { }

        private static bool TryResolve(IMemContext memory, DataValue value,
            out ListHeapObject list, out OperationResult error)
        {
            list = null;
            error = default;
            if (value.dataType != DataTypeId.List)
            {
                error = OperationResult.NotImplemented();
                return false;
            }
            if (!value.TryGetHeapReference(out uint reference))
            {
                error = OperationResult.OpError(RuntimeError.InvalidOperation(
                    "List contains an invalid heap reference."));
                return false;
            }
            if (!memory.TryGetHeap(reference, out HeapObject heapObject) ||
                !(heapObject is ListHeapObject resolved))
            {
                error = OperationResult.OpError(RuntimeError.InvalidMemoryAccess(reference));
                return false;
            }
            list = resolved;
            return true;
        }

        private static bool TryIndex(DataValue index, int count, bool insertion,
            out int position)
        {
            position = -1;
            if (index.dataType != DataTypeId.Int && index.dataType != DataTypeId.Bool)
                return false;
            long raw = unchecked((long)index.value);
            long resolved = raw < 0 ? raw + count : raw;
            if (insertion)
            {
                // Python-like list.insert: clamp beyond either end.
                position = (int)Math.Max(0L, Math.Min(count, resolved));
                return true;
            }
            if (resolved >= 0 && resolved < count)
                position = (int)resolved;
            return true;
        }

        private static OperationResult IndexFailure(DataValue index, int count)
            => OperationResult.OpError(RuntimeError.IndexOutOfRange(
                unchecked((long)index.value), count, "List"));

        public OperationResult Get(OperationCall call)
        {
            if (call.Arguments.Length != 2 || call.Arguments[0].dataType != DataTypeId.List ||
                !TryIndex(call.Arguments[1], 0, false, out _))
                return OperationResult.NotImplemented();
            if (!TryResolve(call.Memory, call.Arguments[0], out var list, out var failure))
                return failure;
            TryIndex(call.Arguments[1], list.Count, false, out int index);
            if ((uint)index >= (uint)list.Count)
                return IndexFailure(call.Arguments[1], list.Count);
            DataValue value = list.GetElement(index);
            return value.dataType == DataTypeId.Uninitialized
                ? OperationResult.OpError(RuntimeError.VariableUsedBeforeInitialization())
                : OperationResult.Success(value);
        }

        public OperationResult Set(OperationCall call)
        {
            if (call.Arguments.Length != 3 || call.Arguments[0].dataType != DataTypeId.List ||
                !TryIndex(call.Arguments[1], 0, false, out _))
                return OperationResult.NotImplemented();
            if (!TryResolve(call.Memory, call.Arguments[0], out var list, out var failure))
                return failure;
            TryIndex(call.Arguments[1], list.Count, false, out int index);
            if ((uint)index >= (uint)list.Count)
                return IndexFailure(call.Arguments[1], list.Count);
            if (call.Arguments[2].dataType == DataTypeId.Uninitialized)
                return OperationResult.OpError(RuntimeError.VariableUsedBeforeInitialization());
            list.SetElement(index, call.Arguments[2]);
            return OperationResult.Success(DataValue.None());
        }

        public OperationResult Append(OperationCall call)
        {
            if (call.Arguments.Length != 2 || call.Arguments[0].dataType != DataTypeId.List)
                return OperationResult.NotImplemented();
            if (!TryResolve(call.Memory, call.Arguments[0], out var list, out var failure))
                return failure;
            if (call.Arguments[1].dataType == DataTypeId.Uninitialized)
                return OperationResult.OpError(RuntimeError.VariableUsedBeforeInitialization());
            if (list.Count == int.MaxValue ||
                !call.Memory.TryResizeHeap((uint)call.Arguments[0].value, list.Size + 1u))
                return OperationResult.OpError(RuntimeError.OutOfMemory());
            list.AppendAuthorized(call.Arguments[1]);
            return OperationResult.Success(DataValue.None());
        }

        public OperationResult Insert(OperationCall call)
        {
            if (call.Arguments.Length != 3 || call.Arguments[0].dataType != DataTypeId.List ||
                !TryIndex(call.Arguments[1], 0, true, out _))
                return OperationResult.NotImplemented();
            if (call.State != null)
                return Build(call, OperationId.Insert);
            if (!TryResolve(call.Memory, call.Arguments[0], out var list, out var failure))
                return failure;
            if (call.Arguments[2].dataType == DataTypeId.Uninitialized)
                return OperationResult.OpError(RuntimeError.VariableUsedBeforeInitialization());
            if (list.Count == int.MaxValue)
                return OperationResult.OpError(RuntimeError.OutOfMemory());
            TryIndex(call.Arguments[1], list.Count, true, out int index);
            if (index == list.Count)
            {
                if (!call.Memory.TryResizeHeap((uint)call.Arguments[0].value, list.Size + 1u))
                    return OperationResult.OpError(RuntimeError.OutOfMemory());
                list.AppendAuthorized(call.Arguments[2]);
                return OperationResult.Success(DataValue.None());
            }
            return Build(call, OperationId.Insert);
        }

        public OperationResult Delete(OperationCall call) => Remove(call, OperationId.Delete);
        public OperationResult Pop(OperationCall call) => Remove(call, OperationId.Pop);

        private static OperationResult Remove(OperationCall call, OperationId kind)
        {
            if (call.Arguments.Length != 2 || call.Arguments[0].dataType != DataTypeId.List ||
                !TryIndex(call.Arguments[1], 0, false, out _))
                return OperationResult.NotImplemented();
            if (call.State != null)
                return Build(call, kind);
            if (!TryResolve(call.Memory, call.Arguments[0], out var list, out var failure))
                return failure;
            TryIndex(call.Arguments[1], list.Count, false, out int index);
            if ((uint)index >= (uint)list.Count)
                return IndexFailure(call.Arguments[1], list.Count);
            if (index == list.Count - 1)
            {
                // Shrink first; no GC occurs for a reduction and the removed value
                // remains inside the list until it can be returned.
                if (!call.Memory.TryResizeHeap((uint)call.Arguments[0].value, list.Size - 1u))
                    return OperationResult.OpError(RuntimeError.InternalVmError("List shrink failed."));
                DataValue removed = list.RemoveLastAuthorized();
                return OperationResult.Success(kind == OperationId.Pop ? removed : DataValue.None());
            }
            return Build(call, kind);
        }

        public OperationResult Extend(OperationCall call) => Build(call, OperationId.Extend);
        public OperationResult Copy(OperationCall call) => Build(call, OperationId.Copy);
        public OperationResult Add(OperationCall call) => Build(call, OperationId.Add);

        private static OperationResult Build(OperationCall call, OperationId kind)
        {
            int arity = kind switch
            {
                OperationId.Copy => 1,
                OperationId.Insert => 3,
                _ => 2
            };
            if (call.Arguments.Length != arity || call.Arguments[0].dataType != DataTypeId.List)
                return OperationResult.NotImplemented();
            if (!TryResolve(call.Memory, call.Arguments[0], out var left, out var failure))
                return failure;

            ListHeapObject right = null;
            if (kind == OperationId.Add || kind == OperationId.Extend)
            {
                if (call.Arguments[1].dataType != DataTypeId.List)
                    return OperationResult.NotImplemented();
                if (!TryResolve(call.Memory, call.Arguments[1], out right, out failure))
                    return failure;
            }

            if (call.State == null)
            {
                int atIndex = 0;
                if (kind == OperationId.Insert || kind == OperationId.Delete || kind == OperationId.Pop)
                {
                    if (!TryIndex(call.Arguments[1], left.Count, kind == OperationId.Insert, out atIndex))
                        return OperationResult.NotImplemented();
                    if (kind != OperationId.Insert && (uint)atIndex >= (uint)left.Count)
                        return IndexFailure(call.Arguments[1], left.Count);
                }
                int rightCount = right?.Count ?? 0;
                long finalCount = kind switch
                {
                    OperationId.Insert => (long)left.Count + 1,
                    OperationId.Delete => left.Count - 1L,
                    OperationId.Pop => left.Count - 1L,
                    OperationId.Add => (long)left.Count + rightCount,
                    OperationId.Extend => (long)left.Count + rightCount,
                    _ => left.Count
                };
                if (finalCount < 0 || finalCount > int.MaxValue)
                    return OperationResult.OpError(RuntimeError.OutOfMemory());
                if (kind == OperationId.Extend && rightCount == 0)
                    return OperationResult.Success(DataValue.None());

                // The first step allocates one empty heap object; no GC occurs between
                // allocation and the return that registers this VM's auxiliary root.
                if (!call.Memory.TryAllocHeap(new ListHeapObject(Array.Empty<DataValue>()),
                        out uint auxReference))
                    return OperationResult.OpError(RuntimeError.OutOfMemory());
                var state = new ListBuildState(kind, left.Version, right?.Version ?? 0,
                    left.Count, rightCount, (int)finalCount, atIndex);
                return OperationResult.Continued(state, auxReference);
            }

            if (!(call.State is ListBuildState progress) || progress.Kind != kind ||
                call.AuxiliaryReference == 0)
                return OperationResult.OpError(RuntimeError.InternalVmError(
                    $"Invalid continuation state for list operation {kind}."));
            if (left.Version != progress.LeftVersion || left.Count != progress.LeftCount ||
                (right != null && (right.Version != progress.RightVersion || right.Count != progress.RightCount)))
                return OperationResult.OpError(RuntimeError.InvalidOperation(
                    $"List changed during continued {kind}; refusing to overwrite concurrent changes."));
            if (!call.Memory.TryGetHeap(call.AuxiliaryReference, out HeapObject obj) ||
                !(obj is ListHeapObject aux) || aux.Count != progress.NextIndex)
                return OperationResult.OpError(RuntimeError.InternalVmError(
                    $"Auxiliary list for {kind} was lost or unexpectedly modified."));

            for (int count = 0; count < ItemsPerStep && progress.NextIndex < progress.FinalCount; count++)
            {
                int i = progress.NextIndex;
                DataValue value = kind switch
                {
                    OperationId.Add => i < progress.LeftCount ? left.GetElement(i) :
                        right.GetElement(i - progress.LeftCount),
                    OperationId.Extend => i < progress.LeftCount ? left.GetElement(i) :
                        right.GetElement(i - progress.LeftCount),
                    OperationId.Insert => i == progress.AtIndex ? call.Arguments[2] :
                        left.GetElement(i < progress.AtIndex ? i : i - 1),
                    OperationId.Delete => left.GetElement(i < progress.AtIndex ? i : i + 1),
                    OperationId.Pop => left.GetElement(i < progress.AtIndex ? i : i + 1),
                    _ => left.GetElement(i)
                };
                if (value.dataType == DataTypeId.Uninitialized)
                    return OperationResult.OpError(RuntimeError.VariableUsedBeforeInitialization());
                if (!call.Memory.TryResizeHeap(call.AuxiliaryReference, aux.Size + 1u))
                    return OperationResult.OpError(RuntimeError.OutOfMemory());
                aux.AppendAuthorized(value);
                progress.NextIndex++;
            }

            if (progress.NextIndex < progress.FinalCount)
                return OperationResult.Continued(progress, call.AuxiliaryReference);

            if (kind == OperationId.Copy || kind == OperationId.Add)
                return OperationResult.Success(DataValue.FromListReference(call.AuxiliaryReference));

            // No VM can observe half of this swap. The original object keeps its ID;
            // heap charges are exchanged with the contents, so no extra growth needed.
            DataValue popped = kind == OperationId.Pop
                ? left.GetElement(progress.AtIndex) : DataValue.None();
            uint originalReference = (uint)call.Arguments[0].value;
            if (!call.Memory.TrySwapHeapContents(originalReference, call.AuxiliaryReference))
                return OperationResult.OpError(RuntimeError.InternalVmError(
                    $"Could not commit continued {kind}."));
            if (!call.Memory.FreeHeap(call.AuxiliaryReference))
                return OperationResult.OpError(RuntimeError.InternalVmError(
                    $"Could not release auxiliary list for {kind}."));
            return OperationResult.Success(kind == OperationId.Pop ? popped : DataValue.None());
        }

        public OperationResult Contains(OperationCall call) => Search(call, OperationId.Contains);
        public OperationResult IndexOf(OperationCall call) => Search(call, OperationId.IndexOf);

        private static OperationResult Search(OperationCall call, OperationId kind)
        {
            if (call.Arguments.Length != 2 || call.Arguments[0].dataType != DataTypeId.List)
                return OperationResult.NotImplemented();
            if (!TryResolve(call.Memory, call.Arguments[0], out var list, out var failure))
                return failure;
            var state = call.State as ListSearchState;
            if (state == null)
                state = new ListSearchState(kind, list.Version);
            if (state.Kind != kind || state.Version != list.Version)
                return OperationResult.OpError(RuntimeError.InvalidOperation(
                    $"List changed during continued {kind}."));

            for (int checkedCount = 0; checkedCount < ItemsPerStep && state.NextIndex < list.Count;
                checkedCount++, state.NextIndex++)
            {
                DataValue item = list.GetElement(state.NextIndex);
                OperationResult equality = OperationDispatcher.Start(OperationId.ValueEquals,
                    new[] { item, call.Arguments[1] }, call.Memory).Result;
                if (equality.Status != OperationStatus.Success)
                    return equality.Status == OperationStatus.Continued
                        ? OperationResult.OpError(RuntimeError.InternalVmError(
                            "Nested continued equality is not currently supported in list search."))
                        : equality;
                if (equality.Value.dataType != DataTypeId.Bool)
                    return OperationResult.OpError(RuntimeError.InternalVmError(
                        "ValueEquals returned a non-Bool result during list search."));
                if (equality.Value.value != 0)
                    return OperationResult.Success(kind == OperationId.Contains
                        ? DataValue.FromBool(true) : DataValue.FromInt(state.NextIndex));
            }
            if (state.NextIndex < list.Count)
                return OperationResult.Continued(state);
            return OperationResult.Success(kind == OperationId.Contains
                ? DataValue.FromBool(false) : DataValue.FromInt(-1));
        }

        public OperationResult Length(OperationCall call)
        {
            if (call.Arguments.Length != 1 || call.Arguments[0].dataType != DataTypeId.List)
                return OperationResult.NotImplemented();
            if (!TryResolve(call.Memory, call.Arguments[0], out var list, out var failure))
                return failure;
            return OperationResult.Success(DataValue.FromInt(list.Count));
        }

        public override OperationResult IsTruthy(OperationCall call)
        {
            if (call.Arguments.Length != 1 || call.Arguments[0].dataType != DataTypeId.List)
                return OperationResult.NotImplemented();
            if (!TryResolve(call.Memory, call.Arguments[0], out var list, out var failure))
                return failure;
            return OperationResult.Success(DataValue.FromBool(list.Count > 0));
        }

        public OperationResult ReferenceEquals(OperationCall call)
        {
            if (call.Arguments.Length != 2)
                return OperationResult.NotImplemented();
            return OperationResult.Success(DataValue.FromBool(
                call.Arguments[0].dataType == DataTypeId.List &&
                call.Arguments[1].dataType == DataTypeId.List &&
                call.Arguments[0].value == call.Arguments[1].value));
        }

        public override OperationResult ValueEquals(OperationCall call)
        {
            if (call.Arguments.Length != 2 || call.Arguments[0].dataType != DataTypeId.List ||
                call.Arguments[1].dataType != DataTypeId.List)
                return OperationResult.NotImplemented();
            return SequenceEquality.Compare(call.Memory, call.Arguments[0], call.Arguments[1]);
        }

        public override CastResult CastTo(DataTypeId targetType, DataValue value,
            VMMemoryContext memory)
        {
            if (targetType != DataTypeId.String || value.dataType != DataTypeId.List)
                return base.CastTo(targetType, value, memory);
            if (!SequenceFormatter.TryFormat(value, memory, out string text,
                    out RuntimeError error))
                return CastResult.Failure(error);
            return DTString.CreateValue(text, memory);
        }

        public OperationResult Subtract(OperationCall call) => OperationResult.NotImplemented();
        public OperationResult Multiply(OperationCall call) => OperationResult.NotImplemented();
        public OperationResult Divide(OperationCall call) => OperationResult.NotImplemented();
        public OperationResult FloorDivide(OperationCall call) => OperationResult.NotImplemented();
        public OperationResult Modulo(OperationCall call) => OperationResult.NotImplemented();
        public OperationResult Power(OperationCall call) => OperationResult.NotImplemented();
    }
}
