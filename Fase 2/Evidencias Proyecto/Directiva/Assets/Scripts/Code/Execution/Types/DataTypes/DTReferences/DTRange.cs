using System;
using System.Globalization;
using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace DSExecution.DataTypes
{
    /// <summary>Lazy, immutable Int64 range; used by ITER_INIT and future native range().</summary>
    public sealed class DTRange : DataType, IOpLength
    {
        public static DTRange Instance { get; } = new DTRange();
        private DTRange() { }

        [DScriptGetter("length")]
        public OperationResult GetLengthMember(OperationCall call) => Length(call);

        public OperationResult Length(OperationCall call)
        {
            if (call.Arguments.Length != 1 || call.Arguments[0].dataType != DataTypeId.Range)
                return OperationResult.NotImplemented();
            if (!TryResolve(call.Memory, call.Arguments[0], out RangeHeapObject range,
                    out RuntimeError error))
                return OperationResult.OpError(error);
            ulong count = range.ElementCount;
            return count > long.MaxValue
                ? OperationResult.OpError(RuntimeError.Overflow())
                : OperationResult.Success(DataValue.FromInt((long)count));
        }

        public override OperationResult IsTruthy(OperationCall call)
        {
            if (!TryResolve(call.Memory, call.Arguments[0], out RangeHeapObject range,
                    out RuntimeError error))
                return OperationResult.OpError(error);
            return OperationResult.Success(DataValue.FromBool(range.ElementCount != 0));
        }

        public override CastResult CastTo(DataTypeId targetType, DataValue value, VMMemoryContext memory)
        {
            if (targetType != DataTypeId.String)
                return base.CastTo(targetType, value, memory);
            if (!TryResolve(memory, value, out RangeHeapObject range, out RuntimeError error))
                return CastResult.Failure(error);
            return DTString.CreateValue(
                "range(" + range.Start.ToString(CultureInfo.InvariantCulture) + ", " +
                range.Stop.ToString(CultureInfo.InvariantCulture) + ", " +
                range.Step.ToString(CultureInfo.InvariantCulture) + ")", memory);
        }

        private static bool TryResolve(IMemContext memory, DataValue value,
            out RangeHeapObject range, out RuntimeError error)
        {
            range = null;
            error = null;
            if (!value.TryGetHeapReference(out uint reference) ||
                !memory.TryGetHeap(reference, out HeapObject obj) ||
                !(obj is RangeHeapObject found))
            {
                error = RuntimeError.InvalidMemoryAccess((uint)value.value);
                return false;
            }
            range = found;
            return true;
        }
    }
}
