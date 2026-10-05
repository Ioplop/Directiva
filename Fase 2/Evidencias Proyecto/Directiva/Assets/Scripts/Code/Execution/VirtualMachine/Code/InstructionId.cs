namespace DSExecution.VirtualMachine
{
    /// <summary>
    /// VM instruction identifiers. Numeric ids are explicit so serialized DIB code can remain stable.
    /// </summary>
    public enum InstructionId
    {
        Nop = 0,
        Push = 1,
        Pop = 2,

        LoadLocal = 3,
        StoreLocal = 4,
        LoadGlobal = 5,
        StoreGlobal = 6,

        Operation = 7,

        Jump = 8,
        JumpIfTrue = 9,
        JumpIfFalse = 10,

        Call = 11,
        Return = 12,

        ContextStart = 13,
        ContextEnd = 14,

        DebugFile = 15,
        DebugLine = 16,
        DebugSpan = 17
    }
}
