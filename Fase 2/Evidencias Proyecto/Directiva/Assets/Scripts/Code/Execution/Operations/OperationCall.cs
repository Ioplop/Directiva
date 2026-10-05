# nullable enable
using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;
using System;

namespace DSExecution.Operations
{
    /// <summary>
    /// Contiene los datos que deben pasarse para poder realizar una operación.
    /// </summary>
    public readonly ref struct OperationCall
    {
        public ReadOnlySpan<DataValue> Arguments { get; }
        public int ReceiverIndex { get; }
        public IMemContext Memory { get; }
        public OperationState? State { get; }

        public OperationCall(
            ReadOnlySpan<DataValue> arguments,
            int receiverIndex,
            IMemContext memory,
            OperationState? state)
        {
            Arguments = arguments;
            ReceiverIndex = receiverIndex;
            Memory = memory;
            State = state;
        }
    }
}
