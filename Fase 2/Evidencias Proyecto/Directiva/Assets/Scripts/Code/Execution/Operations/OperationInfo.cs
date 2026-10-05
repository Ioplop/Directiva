using System;
using System.Linq;
using DSExecution.Values;

namespace DSExecution.Operations
{
    /// <summary>
    /// Información sobre la firma de cada operación: Cuantos valores toma y cuales pueden despachar la operación.
    /// </summary>
    public readonly struct OperationInfo
    {
        public int ParameterCount { get; }
        private readonly int[] dispatchIndices;
        public ReadOnlySpan<int> DispatchIndices => dispatchIndices;

        public OperationResult? FallbackResult { get; }

        public OperationInfo(int parameterCount, OperationResult? fallbackResult, params int[] dispatchIndices)
        {
            ParameterCount = parameterCount;
            FallbackResult = fallbackResult;
            this.dispatchIndices = dispatchIndices.ToArray();
        }
    }
}