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
        /// <summary>Private heap root supplied by the VM for continued operations.</summary>
        public uint AuxiliaryReference { get; }

        public OperationCall(
            ReadOnlySpan<DataValue> arguments,
            int receiverIndex,
            IMemContext memory,
            OperationState? state,
            uint auxiliaryReference = 0)
        {
            Arguments = arguments;
            ReceiverIndex = receiverIndex;
            Memory = memory;
            State = state;
            AuxiliaryReference = auxiliaryReference;
        }
    }
}
