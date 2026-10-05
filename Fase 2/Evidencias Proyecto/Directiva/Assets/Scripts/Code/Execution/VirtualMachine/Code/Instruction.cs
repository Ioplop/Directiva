#nullable enable
using DSExecution.Operations;
using DSExecution.Values;

namespace DSExecution.VirtualMachine
{
    /// <summary>
    /// Compact tagged instruction used by the VM after DIL has been parsed/resolved.
    /// Only the operands relevant to <see cref="Id"/> are meaningful.
    /// </summary>
    public readonly struct Instruction
    {
        public InstructionId Id { get; }
        public int OperandA { get; }
        public int OperandB { get; }
        public DataValue ValueOperand { get; }
        public OperationId OperationOperand { get; }
        public string? TextOperand { get; }

        private Instruction(
            InstructionId id,
            int operandA = 0,
            int operandB = 0,
            DataValue valueOperand = default,
            OperationId operationOperand = default,
            string? textOperand = null)
        {
            Id = id;
            OperandA = operandA;
            OperandB = operandB;
            ValueOperand = valueOperand;
            OperationOperand = operationOperand;
            TextOperand = textOperand;
        }

        public static Instruction Nop()
            => new(InstructionId.Nop);

        public static Instruction Push(DataValue value)
            => new(InstructionId.Push, valueOperand: value);

        public static Instruction Pop()
            => new(InstructionId.Pop);

        public static Instruction LoadLocal(int localIndex)
            => new(InstructionId.LoadLocal, operandA: localIndex);

        public static Instruction StoreLocal(int localIndex)
            => new(InstructionId.StoreLocal, operandA: localIndex);

        public static Instruction LoadGlobal(int globalIndex)
            => new(InstructionId.LoadGlobal, operandA: globalIndex);

        public static Instruction StoreGlobal(int globalIndex)
            => new(InstructionId.StoreGlobal, operandA: globalIndex);

        public static Instruction Operation(OperationId operationId)
            => new(InstructionId.Operation, operationOperand: operationId);

        public static Instruction Jump(int targetInstruction)
            => new(InstructionId.Jump, operandA: targetInstruction);

        /// <summary>
        /// Pops a Bool and jumps when it is True. DIL/compiler code should emit IsTruthy first
        /// when branching on a value of arbitrary type.
        /// </summary>
        public static Instruction JumpIfTrue(int targetInstruction)
            => new(InstructionId.JumpIfTrue, operandA: targetInstruction);

        /// <summary>
        /// Pops a Bool and jumps when it is False. DIL/compiler code should emit IsTruthy first
        /// when branching on a value of arbitrary type.
        /// </summary>
        public static Instruction JumpIfFalse(int targetInstruction)
            => new(InstructionId.JumpIfFalse, operandA: targetInstruction);

        public static Instruction Call(int functionId)
            => new(InstructionId.Call, operandA: functionId);

        /// <summary>
        /// Returns the value currently on top of the evaluation stack.
        /// A function with no explicit return should push None before Return.
        /// </summary>
        public static Instruction Return()
            => new(InstructionId.Return);

        /// <summary>
        /// Starts a lexical context whose local slots occupy [firstLocal, firstLocal + localCount).
        /// The range is reset to Uninitialized on entry.
        /// </summary>
        public static Instruction ContextStart(int firstLocal, int localCount)
            => new(InstructionId.ContextStart, operandA: firstLocal, operandB: localCount);

        /// <summary>
        /// Ends the most recently entered lexical context and clears its local slots.
        /// </summary>
        public static Instruction ContextEnd()
            => new(InstructionId.ContextEnd);

        public static Instruction DebugFile(string fileName)
            => new(InstructionId.DebugFile, textOperand: fileName);

        /// <summary>
        /// Sets the current zero-based source line.
        /// </summary>
        public static Instruction DebugLine(int line)
            => new(InstructionId.DebugLine, operandA: line);

        /// <summary>
        /// Sets the current source span [start, end), using offsets in the current debug file.
        /// </summary>
        public static Instruction DebugSpan(int start, int end)
            => new(InstructionId.DebugSpan, operandA: start, operandB: end);

        public override string ToString()
        {
            return Id switch
            {
                InstructionId.Push => $"Push {ValueOperand.dataType}:{ValueOperand.value}",
                InstructionId.LoadLocal => $"LoadLocal {OperandA}",
                InstructionId.StoreLocal => $"StoreLocal {OperandA}",
                InstructionId.LoadGlobal => $"LoadGlobal {OperandA}",
                InstructionId.StoreGlobal => $"StoreGlobal {OperandA}",
                InstructionId.Operation => $"Operation {OperationOperand}",
                InstructionId.Jump => $"Jump {OperandA}",
                InstructionId.JumpIfTrue => $"JumpIfTrue {OperandA}",
                InstructionId.JumpIfFalse => $"JumpIfFalse {OperandA}",
                InstructionId.Call => $"Call {OperandA}",
                InstructionId.ContextStart => $"ContextStart {OperandA} {OperandB}",
                InstructionId.DebugFile => $"DebugFile {TextOperand}",
                InstructionId.DebugLine => $"DebugLine {OperandA}",
                InstructionId.DebugSpan => $"DebugSpan {OperandA} {OperandB}",
                _ => Id.ToString()
            };
        }
    }
}
