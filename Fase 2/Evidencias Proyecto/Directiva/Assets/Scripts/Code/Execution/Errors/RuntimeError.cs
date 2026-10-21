using DSExecution.DataTypes;

namespace DSExecution.Errors
{
    // TODO: Localize error messages.

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
        InvalidMemberArgumentCount = 22
    }

    /// <summary>
    /// Structured runtime failure returned by script execution instead of throwing through the game.
    /// </summary>
    public sealed class RuntimeError
    {
        public RuntimeErrorId Id { get; }
        public string Message { get; }

        private RuntimeError(RuntimeErrorId id, string message)
        {
            Id = id;
            Message = message;
        }

        public static RuntimeError MemberNotFound(string type, string name)
            => new(RuntimeErrorId.MemberNotFound,
                $"Member '{name}' is not available on type {type}. TODO: Localize!");

        public static RuntimeError ReadOnlyProperty(string type, string name)
            => new(RuntimeErrorId.ReadOnlyProperty,
                $"Property '{name}' on {type} is read-only. TODO: Localize!");

        public static RuntimeError WriteOnlyProperty(string type, string name)
            => new(RuntimeErrorId.WriteOnlyProperty,
                $"Property '{name}' on {type} is write-only. TODO: Localize!");

        public static RuntimeError InvalidMemberArgumentCount(string type, string name, int expected, int actual)
            => new(RuntimeErrorId.InvalidMemberArgumentCount,
                $"Member '{name}' on {type} expected " +
                (expected < 0 ? "a valid argument count" : expected.ToString() + " argument(s)") +
                $", got {actual}. TODO: Localize!");

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
                $"Invalid operation. {details} TODO: Localize!"
            );

        public static RuntimeError InvalidMemoryAccess(uint memoryReference)
            => new(
                RuntimeErrorId.InvalidMemoryAccess,
                $"Invalid heap reference {memoryReference}. TODO: Localize!"
            );

        public static RuntimeError IndexOutOfRange(long index, int count, string containerType)
            => new(
                RuntimeErrorId.IndexOutOfRange,
                $"{containerType} index {index} is out of range for {count} element(s). TODO: Localize!"
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
                " TODO: Localize!"
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
                $"Invalid instruction. {details} TODO: Localize!"
            );

        public static RuntimeError InvalidFunction(int functionId)
            => new(
                RuntimeErrorId.InvalidFunction,
                $"Function id {functionId} does not exist. TODO: Localize!"
            );

        public static RuntimeError InvalidLocalAccess(int localIndex)
            => new(
                RuntimeErrorId.InvalidLocalAccess,
                $"Invalid local index {localIndex}. TODO: Localize!"
            );

        public static RuntimeError InvalidGlobalAccess(int globalIndex)
            => new(
                RuntimeErrorId.InvalidGlobalAccess,
                $"Invalid global index {globalIndex}. TODO: Localize!"
            );

        public static RuntimeError InvalidJumpTarget(int target)
            => new(
                RuntimeErrorId.InvalidJumpTarget,
                $"Invalid jump target {target}. TODO: Localize!"
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
                $"Cannot cast {sourceType} to {targetType}. TODO: Localize!"
            );

        public static RuntimeError InvalidCastValue(
            DataTypeId sourceType,
            DataTypeId targetType,
            string details)
            => new(
                RuntimeErrorId.InvalidCastValue,
                $"Cannot cast value from {sourceType} to {targetType}. {details} TODO: Localize!"
            );

        public static RuntimeError InternalVmError(string details)
            => new(
                RuntimeErrorId.InternalVmError,
                $"Internal VM error. {details} TODO: Localize!"
            );
    }
}
