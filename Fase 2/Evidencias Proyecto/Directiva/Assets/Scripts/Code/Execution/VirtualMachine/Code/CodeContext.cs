using System;
using System.Collections.Generic;
using DSExecution.Native;

namespace DSExecution.VirtualMachine
{
    /// <summary>
    /// Immutable compiled-code context consumed by one or more VMs.
    /// It contains function definitions and the number of shared global slots required by the program.
    /// </summary>
    public sealed class CodeContext
    {
        private readonly Dictionary<int, FunctionContext> functions;
        private readonly Dictionary<int, NativeFunction> nativeFunctions;

        public int EntryFunctionId { get; }
        public int GlobalCount { get; }
        /// <summary>Persistent slots that belong to each VM separately.</summary>
        public int VmGlobalCount { get; }
        /// <summary>Optional synthesized initialization entry run once per VM.</summary>
        public int VmInitializerFunctionId { get; }
        public IReadOnlyCollection<FunctionContext> Functions => functions.Values;
        public IReadOnlyCollection<NativeFunction> NativeFunctions => nativeFunctions.Values;

        public CodeContext(
            int entryFunctionId,
            int globalCount,
            IEnumerable<FunctionContext> functions,
            IEnumerable<NativeFunction> nativeFunctions = null,
            int vmGlobalCount = 0,
            int vmInitializerFunctionId = -1)
        {
            if (globalCount < 0)
                throw new ArgumentOutOfRangeException(nameof(globalCount));
            if (vmGlobalCount < 0)
                throw new ArgumentOutOfRangeException(nameof(vmGlobalCount));

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

            this.nativeFunctions = new Dictionary<int, NativeFunction>();
            if (nativeFunctions != null)
            {
                foreach (NativeFunction function in nativeFunctions)
                {
                    if (function == null ||
                        this.functions.ContainsKey(function.Id) ||
                        !this.nativeFunctions.TryAdd(function.Id, function))
                        throw new ArgumentException("Invalid or duplicate native function id.", nameof(nativeFunctions));
                }
            }

            if (!this.functions.ContainsKey(entryFunctionId))
                throw new ArgumentException(
                    $"Entry function id {entryFunctionId} does not exist.",
                    nameof(entryFunctionId)
                );

            if (vmInitializerFunctionId >= 0 &&
                (!this.functions.TryGetValue(vmInitializerFunctionId, out var vmInitializer) ||
                 vmInitializer.ParameterCount != 0))
                throw new ArgumentException("VM initializer must be a zero-argument compiled function.",
                    nameof(vmInitializerFunctionId));

            EntryFunctionId = entryFunctionId;
            GlobalCount = globalCount;
            VmGlobalCount = vmGlobalCount;
            VmInitializerFunctionId = vmInitializerFunctionId;
        }

        /// <summary>
        /// Resolves a compiled function by its stable function id.
        /// </summary>
        public bool TryGetFunction(int functionId, out FunctionContext function)
            => functions.TryGetValue(functionId, out function);

        public bool TryGetNativeFunction(int functionId, out NativeFunction function)
            => nativeFunctions.TryGetValue(functionId, out function);

        public bool ContainsCallable(int functionId)
            => functions.ContainsKey(functionId) || nativeFunctions.ContainsKey(functionId);

        public FunctionContext GetFunction(int functionId)
        {
            if (!functions.TryGetValue(functionId, out var function))
                throw new KeyNotFoundException($"Function id {functionId} does not exist.");

            return function;
        }
    }
}
