using System;

namespace DSExecution.VirtualMachine
{
    /// <summary>
    /// Immutable code and local layout for one compiled function.
    /// Local slots [0, ParameterCount) are initialized from call arguments.
    /// </summary>
    public sealed class FunctionContext
    {
        private readonly Instruction[] instructions;

        public int Id { get; }
        public string Name { get; }
        public int ParameterCount { get; }
        public int LocalCount { get; }
        public int InstructionCount => instructions.Length;
        public ReadOnlySpan<Instruction> Instructions => instructions;

        public FunctionContext(
            int id,
            string name,
            int parameterCount,
            int localCount,
            params Instruction[] instructions)
        {
            if (id < 0)
                throw new ArgumentOutOfRangeException(nameof(id));

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Function name cannot be empty.", nameof(name));

            if (parameterCount < 0)
                throw new ArgumentOutOfRangeException(nameof(parameterCount));

            if (localCount < parameterCount)
                throw new ArgumentOutOfRangeException(
                    nameof(localCount),
                    "Local count cannot be smaller than parameter count."
                );

            Id = id;
            Name = name;
            ParameterCount = parameterCount;
            LocalCount = localCount;
            this.instructions = instructions != null
                ? (Instruction[])instructions.Clone()
                : throw new ArgumentNullException(nameof(instructions));
        }

        /// <summary>
        /// Reads an instruction without throwing when the instruction pointer is out of range.
        /// </summary>
        public bool TryGetInstruction(int instructionIndex, out Instruction instruction)
        {
            if ((uint)instructionIndex >= (uint)instructions.Length)
            {
                instruction = default;
                return false;
            }

            instruction = instructions[instructionIndex];
            return true;
        }
    }
}
