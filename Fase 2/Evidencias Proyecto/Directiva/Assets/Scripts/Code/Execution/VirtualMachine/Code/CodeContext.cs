using System;
using System.Collections.Generic;

namespace DSExecution.VirtualMachine
{
    /// <summary>
    /// Immutable compiled-code context consumed by one or more VMs.
    /// It contains function definitions and the number of shared global slots required by the program.
    /// </summary>
    public sealed class CodeContext
    {
        private readonly Dictionary<int, FunctionContext> functions;

        public int EntryFunctionId { get; }
        public int GlobalCount { get; }
        public IReadOnlyCollection<FunctionContext> Functions => functions.Values;

        public CodeContext(
            int entryFunctionId,
            int globalCount,
            IEnumerable<FunctionContext> functions)
        {
            if (globalCount < 0)
                throw new ArgumentOutOfRangeException(nameof(globalCount));

            if (functions == null)
                throw new ArgumentNullException(nameof(functions));

            this.functions = new Dictionary<int, FunctionContext>();

            foreach (var function in functions)
            {
                if (function == null)
                    throw new ArgumentException("Function list cannot contain null values.", nameof(functions));

                if (!this.functions.TryAdd(function.Id, function))
                    throw new ArgumentException(
                        $"Function id {function.Id} is registered more than once.",
                        nameof(functions)
                    );
            }

            if (!this.functions.ContainsKey(entryFunctionId))
                throw new ArgumentException(
                    $"Entry function id {entryFunctionId} does not exist.",
                    nameof(entryFunctionId)
                );

            EntryFunctionId = entryFunctionId;
            GlobalCount = globalCount;
        }

        /// <summary>
        /// Resolves a compiled function by its stable function id.
        /// </summary>
        public bool TryGetFunction(int functionId, out FunctionContext function)
            => functions.TryGetValue(functionId, out function);

        public FunctionContext GetFunction(int functionId)
        {
            if (!functions.TryGetValue(functionId, out var function))
                throw new KeyNotFoundException($"Function id {functionId} does not exist.");

            return function;
        }
    }
}
