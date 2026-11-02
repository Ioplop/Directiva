using DSExecution.DataTypes;

namespace DSExecution.Errors
{

    /// <summary>
    /// Stable identifiers for runtime failures that can be reported by the VM or data type operations.
    /// </summary>
    public enum RuntimeErrorId
    {
        DivisionByZero = 0,
        Overflow = 1,
        InvalidOperation = 2,
        InvalidMemoryAccess = 3,
        StackOverflow = 4,
        StackUnderflow = 5,
        VariableUsedBeforeInitialization = 6,
        InvalidInstruction = 7,
        InvalidFunction = 8,
        InvalidLocalAccess = 9,
        InvalidGlobalAccess = 10,
        InvalidJumpTarget = 11,
        CallStackOverflow = 12,
        InternalVmError = 13,
        OutOfMemory = 14,
        InvalidCast = 15,
        InvalidCastValue = 16,
        IndexOutOfRange = 17,
        UnhashableType = 18,
        MemberNotFound = 19,
        ReadOnlyProperty = 20,
        WriteOnlyProperty = 21,
        InvalidMemberArgumentCount = 22,
        InvalidCallTarget = 23,
        InvalidFunctionArgumentCount = 24,
        ForeignFunctionProgram = 25,
        NotIterable = 26,
        InvalidIterator = 27,
        IteratorModified = 28
    }

    /// <summary>
    /// Structured runtime failure returned by script execution instead of throwing through the game.
    /// </summary>
    public sealed class RuntimeError
    {
        public RuntimeErrorId Id { get; }
        // Message stays in its original technical language for regression tests and logs.
        // The UI uses MessageKey + MessageArguments to format a localized version.
        public string Message { get; }
        public string MessageKey => Id.ToString();
        public object[] MessageArguments { get; }

        private RuntimeError(RuntimeErrorId id, string message, params object[] arguments)
        {
            Id = id;
            Message = message;
            MessageArguments = arguments ?? System.Array.Empty<object>();
        }

        public static RuntimeError MemberNotFound(string type, string name)
            => new(RuntimeErrorId.MemberNotFound,
                $"Member '{name}' is not available on type {type}. TODO: Localize!", type, name);

        public static RuntimeError ReadOnlyProperty(string type, string name)
            => new(RuntimeErrorId.ReadOnlyProperty,
                $"Property '{name}' on {type} is read-only. TODO: Localize!", type, name);

        public static RuntimeError WriteOnlyProperty(string type, string name)
            => new(RuntimeErrorId.WriteOnlyProperty,
                $"Property '{name}' on {type} is write-only. TODO: Localize!", type, name);

        public static RuntimeError InvalidMemberArgumentCount(string type, string name, int expected, int actual)
            => InvalidMemberArgumentCount(type, name, expected, expected, actual);

        public static RuntimeError InvalidMemberArgumentCount(
            string type, string name, int minimum, int maximum, int actual)
        {
            string expected = maximum < 0
                ? (minimum == 0 ? "any non-negative number of arguments" :
                    $"at least {minimum} argument(s)")
                : minimum == maximum ? $"{minimum} argument(s)" :
                    $"between {minimum} and {maximum} argument(s)";
            return new RuntimeError(RuntimeErrorId.InvalidMemberArgumentCount,
                $"Member '{name}' on {type} expected {expected}, got {actual}. TODO: Localize!", type, name, minimum, maximum, actual);
        }

        public static RuntimeError NotIterable(DataTypeId sourceType)
            => new(RuntimeErrorId.NotIterable,
                $"Type {sourceType} does not support iteration. TODO: Localize!", sourceType);

        public static RuntimeError InvalidIterator(DataTypeId sourceType)
            => new(RuntimeErrorId.InvalidIterator,
                $"ITER_NEXT needs an Iterator, received {sourceType}. TODO: Localize!", sourceType);

        public static RuntimeError IteratorModified(DataTypeId sourceType)
            => new(RuntimeErrorId.IteratorModified,
                $"{sourceType} was modified during iteration. TODO: Localize!", sourceType);

        public static RuntimeError DivisionByZero()
            => new(
                RuntimeErrorId.DivisionByZero,
                "Division by zero. TODO: Localize!"
            );

        public static RuntimeError Overflow()
            => new(
                RuntimeErrorId.Overflow,
                "Numeric overflow. TODO: Localize!"
            );

        public static RuntimeError InvalidOperation(string details)
            => new(
                RuntimeErrorId.InvalidOperation,
                $"Invalid operation. {details} TODO: Localize!", details
            );

        public static RuntimeError InvalidMemoryAccess(uint memoryReference)
            => new(
                RuntimeErrorId.InvalidMemoryAccess,
                $"Invalid heap reference {memoryReference}. TODO: Localize!", memoryReference
            );

        public static RuntimeError IndexOutOfRange(long index, int count, string containerType)
            => new(
                RuntimeErrorId.IndexOutOfRange,
                $"{containerType} index {index} is out of range for {count} element(s). TODO: Localize!", containerType, index, count
            );

        /// <summary>
        /// A value cannot be used as a dictionary key or set member, either directly
        /// or because it appears inside a tuple. The path uses zero-based tuple indexes.
        /// </summary>
        public static RuntimeError UnhashableType(DataTypeId type, string tuplePath = "")
            => new(
                RuntimeErrorId.UnhashableType,
                $"Unhashable type {type}" +
                (string.IsNullOrEmpty(tuplePath) ? "." : $" at tuple element {tuplePath}.") +
                " TODO: Localize!", type, tuplePath
            );

        public static RuntimeError StackOverflow()
            => new(
                RuntimeErrorId.StackOverflow,
                "Evaluation stack overflow. TODO: Localize!"
            );

        public static RuntimeError StackUnderflow()
            => new(
                RuntimeErrorId.StackUnderflow,
                "Evaluation stack underflow. TODO: Localize!"
            );

        public static RuntimeError VariableUsedBeforeInitialization()
            => new(
                RuntimeErrorId.VariableUsedBeforeInitialization,
                "Variable used before initialization. TODO: Localize!"
            );

        public static RuntimeError InvalidInstruction(string details)
            => new(
                RuntimeErrorId.InvalidInstruction,
                $"Invalid instruction. {details} TODO: Localize!", details
            );

        public static RuntimeError InvalidCallTarget(DataTypeId actualType)
            => new(RuntimeErrorId.InvalidCallTarget,
                $"Indirect call requires a Function, got {actualType}. TODO: Localize!", actualType);

        public static RuntimeError InvalidFunctionArgumentCount(int functionId, int expected, int actual)
            => new(RuntimeErrorId.InvalidFunctionArgumentCount,
                $"Function {functionId} expects {expected} argument(s), got {actual}. TODO: Localize!", functionId, expected, actual);

        public static RuntimeError ForeignFunctionProgram()
            => new(RuntimeErrorId.ForeignFunctionProgram,
                "Function references in shared memory must belong to the same compiled CodeContext. TODO: Localize!");

        public static RuntimeError InvalidFunction(int functionId)
            => new(
                RuntimeErrorId.InvalidFunction,
                $"Function id {functionId} does not exist. TODO: Localize!", functionId
            );

        public static RuntimeError InvalidLocalAccess(int localIndex)
            => new(
                RuntimeErrorId.InvalidLocalAccess,
                $"Invalid local index {localIndex}. TODO: Localize!", localIndex
            );

        public static RuntimeError InvalidGlobalAccess(int globalIndex)
            => new(
                RuntimeErrorId.InvalidGlobalAccess,
                $"Invalid global index {globalIndex}. TODO: Localize!", globalIndex
            );

        public static RuntimeError InvalidJumpTarget(int target)
            => new(
                RuntimeErrorId.InvalidJumpTarget,
                $"Invalid jump target {target}. TODO: Localize!", target
            );

        public static RuntimeError CallStackOverflow()
            => new(
                RuntimeErrorId.CallStackOverflow,
                "Call stack overflow. TODO: Localize!"
            );

        public static RuntimeError OutOfMemory()
            => new(
                RuntimeErrorId.OutOfMemory,
                "Directiva heap is out of memory. TODO: Localize!"
            );

        public static RuntimeError InvalidCast(DataTypeId sourceType, DataTypeId targetType)
            => new(
                RuntimeErrorId.InvalidCast,
                $"Cannot cast {sourceType} to {targetType}. TODO: Localize!", sourceType, targetType
            );

        public static RuntimeError InvalidCastValue(
            DataTypeId sourceType,
            DataTypeId targetType,
            string details)
            => new(
                RuntimeErrorId.InvalidCastValue,
                $"Cannot cast value from {sourceType} to {targetType}. {details} TODO: Localize!", sourceType, targetType, details
            );

        public static RuntimeError InternalVmError(string details)
            => new(
                RuntimeErrorId.InternalVmError,
                $"Internal VM error. {details} TODO: Localize!", details
            );
    }
}
