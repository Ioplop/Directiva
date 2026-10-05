namespace DSExecution.Errors
{
    // TODO: Localize error messages.

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
        InternalVmError = 13
    }

    public sealed class RuntimeError
    {
        public RuntimeErrorId Id { get; }
        public string Message { get; }

        private RuntimeError(RuntimeErrorId id, string message)
        {
            Id = id;
            Message = message;
        }

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

        public static RuntimeError InternalVmError(string details)
            => new(
                RuntimeErrorId.InternalVmError,
                $"Internal VM error. {details} TODO: Localize!"
            );
    }
}
