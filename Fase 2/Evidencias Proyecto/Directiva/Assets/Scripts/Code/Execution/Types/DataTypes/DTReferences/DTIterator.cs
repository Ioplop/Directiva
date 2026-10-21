using System;
using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace DSExecution.DataTypes
{
    /// <summary>Opaque stateful iterator; use ITER_NEXT to distinguish None from completion.</summary>
    public sealed class DTIterator : DataType
    {
        public static DTIterator Instance { get; } = new DTIterator();
        private DTIterator() { }

        public override OperationResult IsTruthy(OperationCall call)
            => OperationResult.Success(DataValue.FromBool(true));

        public override CastResult CastTo(DataTypeId targetType, DataValue value, VMMemoryContext memory)
        {
            if (targetType != DataTypeId.String)
                return base.CastTo(targetType, value, memory);
            if (!value.TryGetHeapReference(out uint reference) ||
                !memory.TryGetHeap(reference, out HeapObject obj) ||
                !(obj is IteratorHeapObject cursor))
                return CastResult.Failure(RuntimeError.InvalidMemoryAccess((uint)value.value));
            return DTString.CreateValue("<iterator " + cursor.SourceType + ">", memory);
        }
    }
}
