using System;
using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace DSExecution.DataTypes
{
    /// <summary>
    /// Immutable heap-backed Directiva string. Value equality is ordinal and Add concatenates
    /// into a newly allocated StringHeapObject.
    /// </summary>
    public sealed class DTString : DataType, IOpArithmetic, IOpReferenceEquality
    {
        public static DTString Instance { get; } = new DTString();

        private DTString()
        {
        }

        /// <summary>
        /// Allocates an immutable Directiva string in the shared heap.
        /// Used by casts whose source type decides how it is represented as text.
        /// </summary>
        internal static CastResult CreateValue(string value, VMMemoryContext memory)
        {
            if (memory == null)
                throw new ArgumentNullException(nameof(memory));

            var heapObject = new StringHeapObject(value);
            if (!memory.TryAllocHeap(heapObject, out uint memoryReference))
                return CastResult.Failure(RuntimeError.OutOfMemory());

            return CastResult.Success(
                DataValue.FromStringReference(memoryReference)
            );
        }

        private static bool TryResolve(
            OperationCall opCall,
            DataValue value,
            out StringHeapObject stringObject,
            out OperationResult errorResult)
        {
            stringObject = null;
            errorResult = default;

            if (value.dataType != DataTypeId.String ||
                value.value == 0 ||
                value.value > uint.MaxValue)
            {
                return false;
            }

            uint memoryReference = (uint)value.value;
            if (!opCall.Memory.TryGetHeap(memoryReference, out HeapObject heapObject) ||
                !(heapObject is StringHeapObject resolved))
            {
                errorResult = OperationResult.OpError(
                    RuntimeError.InvalidMemoryAccess(memoryReference)
                );
                return false;
            }

            stringObject = resolved;
            return true;
        }

        public OperationResult Add(OperationCall opCall)
        {
            if (opCall.Arguments.Length != 2 ||
                opCall.Arguments[0].dataType != DataTypeId.String ||
                opCall.Arguments[1].dataType != DataTypeId.String)
            {
                return OperationResult.NotImplemented();
            }

            if (!TryResolve(opCall, opCall.Arguments[0], out var left, out var leftError))
                return leftError.Status == OperationStatus.Error ? leftError : OperationResult.NotImplemented();

            if (!TryResolve(opCall, opCall.Arguments[1], out var right, out var rightError))
                return rightError.Status == OperationStatus.Error ? rightError : OperationResult.NotImplemented();

            string resultValue = string.Concat(left.Value, right.Value);
            var resultObject = new StringHeapObject(resultValue);

            if (!opCall.Memory.TryAllocHeap(resultObject, out uint resultReference))
                return OperationResult.OpError(RuntimeError.OutOfMemory());

            return OperationResult.Success(
                DataValue.FromStringReference(resultReference)
            );
        }

        public OperationResult Subtract(OperationCall opCall) => OperationResult.NotImplemented();
        public OperationResult Multiply(OperationCall opCall) => OperationResult.NotImplemented();
        public OperationResult Divide(OperationCall opCall) => OperationResult.NotImplemented();
        public OperationResult FloorDivide(OperationCall opCall) => OperationResult.NotImplemented();
        public OperationResult Modulo(OperationCall opCall) => OperationResult.NotImplemented();
        public OperationResult Power(OperationCall opCall) => OperationResult.NotImplemented();

        public override OperationResult ValueEquals(OperationCall opCall)
        {
            if (opCall.Arguments.Length != 2 ||
                opCall.Arguments[0].dataType != DataTypeId.String ||
                opCall.Arguments[1].dataType != DataTypeId.String)
            {
                return OperationResult.NotImplemented();
            }

            if (opCall.Arguments[0].value == opCall.Arguments[1].value)
                return OperationResult.Success(DataValue.FromBool(true));

            if (!TryResolve(opCall, opCall.Arguments[0], out var left, out var leftError))
                return leftError.Status == OperationStatus.Error ? leftError : OperationResult.NotImplemented();

            if (!TryResolve(opCall, opCall.Arguments[1], out var right, out var rightError))
                return rightError.Status == OperationStatus.Error ? rightError : OperationResult.NotImplemented();

            return OperationResult.Success(
                DataValue.FromBool(
                    string.Equals(left.Value, right.Value, StringComparison.Ordinal)
                )
            );
        }

        public OperationResult ReferenceEquals(OperationCall opCall)
        {
            if (opCall.Arguments.Length != 2)
                return OperationResult.NotImplemented();

            DataValue left = opCall.Arguments[0];
            DataValue right = opCall.Arguments[1];

            return OperationResult.Success(
                DataValue.FromBool(
                    left.dataType == DataTypeId.String &&
                    right.dataType == DataTypeId.String &&
                    left.value == right.value
                )
            );
        }
    }
}
