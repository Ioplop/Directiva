namespace DSExecution.Errors
{
    // TODO: Localize error messages.

    public enum RuntimeErrorId
    {
        DivisionByZero,
        Overflow,
        InvalidOperation,
        InvalidMemoryAccess,
        StackOverflow,
        StackUnderflow,
        VariableUsedBeforeInitialization
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

        public static RuntimeError VariableUsedBeforeInitialization()
            => new(
                RuntimeErrorId.VariableUsedBeforeInitialization,
                "Variable used before initialization. TODO: Localize!"
            );
    }
}