#nullable enable
using System;
using System.Collections.Generic;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;

namespace DSExecution.VirtualMachine
{
    /// <summary>
    /// Deterministic stack VM for compiled Directiva code.
    /// Advance() executes exactly one VM step; continued operations remain on the same instruction
    /// and consume additional steps. This makes execution budgeting independent of machine speed.
    /// </summary>
    public sealed class DirectivaVM
    {
        /// <summary>
        /// Stores the continuation state of an operation that needs more than one VM step.
        /// </summary>
        private sealed class PendingOperation
        {
            public OperationId OperationId { get; }
            public DataValue[] Arguments { get; }
            public int ReceiverIndex { get; }
            public OperationState State { get; set; }

            public PendingOperation(
                OperationId operationId,
                DataValue[] arguments,
                int receiverIndex,
                OperationState state)
            {
                OperationId = operationId;
                Arguments = arguments;
                ReceiverIndex = receiverIndex;
                State = state;
            }
        }

        private readonly EvaluationStack evaluationStack;
        private readonly List<CallFrame> callStack = new();
        private readonly VMOptions options;

        private PendingOperation? pendingOperation;
        private RuntimeError? fault;
        private DataValue returnValue;

        public CodeContext Code { get; }
        public VMMemoryContext Memory { get; }
        public VMDebugState Debug { get; } = new();

        public VMExecutionStatus Status { get; private set; }
        public RuntimeError? Error => fault;
        public DataValue ReturnValue => returnValue;
        public int EntryFunctionId { get; }
        public int CallDepth => callStack.Count;
        public int EvaluationStackCount => evaluationStack.Count;

        public FunctionContext? CurrentFunction
            => callStack.Count > 0 ? callStack[^1].Function : null;

        public int InstructionPointer
            => callStack.Count > 0 ? callStack[^1].InstructionPointer : -1;

        public LocalMemory? CurrentLocals
            => callStack.Count > 0 ? callStack[^1].Locals : null;

        public DirectivaVM(CodeContext code)
            : this(code, null, null, 0, true, Array.Empty<DataValue>())
        {
        }

        /// <summary>
        /// Creates a VM that starts from an explicit function instead of the CodeContext default entry.
        /// </summary>
        public DirectivaVM(
            CodeContext code,
            int entryFunctionId,
            params DataValue[] entryArguments)
            : this(code, null, null, entryFunctionId, false, entryArguments)
        {
        }

        public DirectivaVM(
            CodeContext code,
            VMMemoryContext? memory,
            VMOptions? options,
            params DataValue[] entryArguments)
            : this(code, memory, options, 0, true, entryArguments)
        {
        }

        /// <summary>
        /// Creates a VM using shared memory/options and an explicit entry function.
        /// </summary>
        public DirectivaVM(
            CodeContext code,
            VMMemoryContext? memory,
            VMOptions? options,
            int entryFunctionId,
            params DataValue[] entryArguments)
            : this(code, memory, options, entryFunctionId, false, entryArguments)
        {
        }

        private DirectivaVM(
            CodeContext code,
            VMMemoryContext? memory,
            VMOptions? options,
            int entryFunctionId,
            bool useDefaultEntry,
            DataValue[] entryArguments)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            this.options = options ?? new VMOptions();
            Memory = memory ?? new VMMemoryContext(code.GlobalCount);

            if (Memory.GlobalCount != code.GlobalCount)
            {
                throw new ArgumentException(
                    "Memory global count must match the code context global count.",
                    nameof(memory)
                );
            }

            evaluationStack = new EvaluationStack(this.options.MaxEvaluationStackSize);

            int resolvedEntryFunctionId = useDefaultEntry
                ? code.EntryFunctionId
                : entryFunctionId;

            if (!code.TryGetFunction(resolvedEntryFunctionId, out var entryFunction))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(entryFunctionId),
                    $"Entry function id {resolvedEntryFunctionId} does not exist in the code context."
                );
            }

            EntryFunctionId = resolvedEntryFunctionId;
            entryArguments ??= Array.Empty<DataValue>();

            if (entryArguments.Length != entryFunction.ParameterCount)
            {
                throw new ArgumentException(
                    $"Entry function expects {entryFunction.ParameterCount} arguments, " +
                    $"but {entryArguments.Length} were provided.",
                    nameof(entryArguments)
                );
            }

            callStack.Add(
                new CallFrame(
                    entryFunction,
                    0,
                    entryArguments
                )
            );

            returnValue = DataValue.None();
            Status = VMExecutionStatus.Running;
        }

        /// <summary>
        /// Executes exactly one deterministic VM step.
        /// Any script/runtime failure is returned as Faulted instead of escaping as an exception.
        /// </summary>
        public VMAdvanceResult Advance()
        {
            if (Status == VMExecutionStatus.Completed)
                return VMAdvanceResult.Completed(returnValue);

            if (Status == VMExecutionStatus.Faulted)
                return VMAdvanceResult.Faulted(fault!);

            try
            {
                return AdvanceCore();
            }
            catch (Exception ex)
            {
                return Fault(
                    RuntimeError.InternalVmError(
                        $"{ex.GetType().Name}: {ex.Message}"
                    )
                );
            }
        }

        /// <summary>
        /// Executes at most instructionBudget VM steps.
        /// A continued operation consumes one budget unit per continuation step.
        /// </summary>
        public VMRunResult Run(int instructionBudget)
        {
            if (instructionBudget <= 0)
                throw new ArgumentOutOfRangeException(nameof(instructionBudget));

            int steps = 0;
            VMAdvanceResult lastResult = Status switch
            {
                VMExecutionStatus.Completed => VMAdvanceResult.Completed(returnValue),
                VMExecutionStatus.Faulted => VMAdvanceResult.Faulted(fault!),
                _ => VMAdvanceResult.Running()
            };

            while (steps < instructionBudget && Status == VMExecutionStatus.Running)
            {
                lastResult = Advance();
                steps++;
            }

            bool budgetExhausted =
                steps == instructionBudget &&
                Status == VMExecutionStatus.Running;

            return new VMRunResult(
                Status,
                steps,
                budgetExhausted,
                lastResult.Error,
                Status == VMExecutionStatus.Completed
                    ? returnValue
                    : DataValue.None()
            );
        }

        private VMAdvanceResult AdvanceCore()
        {
            if (callStack.Count == 0)
            {
                return Fault(
                    RuntimeError.InternalVmError(
                        "VM is Running without a call frame."
                    )
                );
            }

            var frame = callStack[^1];

            if (!frame.Function.TryGetInstruction(
                    frame.InstructionPointer,
                    out var instruction))
            {
                return Fault(
                    RuntimeError.InvalidInstruction(
                        $"Instruction pointer {frame.InstructionPointer} is outside function " +
                        $"'{frame.Function.Name}'. A function must terminate with Return."
                    )
                );
            }

            if (pendingOperation != null)
            {
                if (instruction.Id != InstructionId.Operation ||
                    instruction.OperationOperand != pendingOperation.OperationId)
                {
                    return Fault(
                        RuntimeError.InternalVmError(
                            "Pending operation no longer matches the current instruction."
                        )
                    );
                }

                return ContinueOperation(frame);
            }

            return instruction.Id switch
            {
                InstructionId.Nop => AdvanceInstruction(frame),
                InstructionId.Push => ExecutePush(frame, instruction.ValueOperand),
                InstructionId.Pop => ExecutePop(frame),
                InstructionId.LoadLocal => ExecuteLoadLocal(frame, instruction.OperandA),
                InstructionId.StoreLocal => ExecuteStoreLocal(frame, instruction.OperandA),
                InstructionId.LoadGlobal => ExecuteLoadGlobal(frame, instruction.OperandA),
                InstructionId.StoreGlobal => ExecuteStoreGlobal(frame, instruction.OperandA),
                InstructionId.Operation => ExecuteOperation(frame, instruction.OperationOperand),
                InstructionId.Jump => ExecuteJump(frame, instruction.OperandA),
                InstructionId.JumpIfTrue => ExecuteConditionalJump(frame, instruction.OperandA, true),
                InstructionId.JumpIfFalse => ExecuteConditionalJump(frame, instruction.OperandA, false),
                InstructionId.Call => ExecuteCall(frame, instruction.OperandA),
                InstructionId.Return => ExecuteReturn(frame),
                InstructionId.ContextStart => ExecuteContextStart(frame, instruction.OperandA, instruction.OperandB),
                InstructionId.ContextEnd => ExecuteContextEnd(frame),
                InstructionId.DebugFile => ExecuteDebugFile(frame, instruction.TextOperand),
                InstructionId.DebugLine => ExecuteDebugLine(frame, instruction.OperandA),
                InstructionId.DebugSpan => ExecuteDebugSpan(frame, instruction.OperandA, instruction.OperandB),
                _ => Fault(
                    RuntimeError.InvalidInstruction(
                        $"Unknown instruction id {(int)instruction.Id}."
                    )
                )
            };
        }

        private VMAdvanceResult ExecutePush(CallFrame frame, DataValue value)
        {
            if (!evaluationStack.TryPush(value))
                return Fault(RuntimeError.StackOverflow());

            return AdvanceInstruction(frame);
        }

        private VMAdvanceResult ExecutePop(CallFrame frame)
        {
            if (!evaluationStack.TryPop(frame.EvalStackBase, out _))
                return Fault(RuntimeError.StackUnderflow());

            return AdvanceInstruction(frame);
        }

        private VMAdvanceResult ExecuteLoadLocal(CallFrame frame, int localIndex)
        {
            if (!frame.Locals.TryGet(localIndex, out var value))
                return Fault(RuntimeError.InvalidLocalAccess(localIndex));

            if (!evaluationStack.TryPush(value))
                return Fault(RuntimeError.StackOverflow());

            return AdvanceInstruction(frame);
        }

        private VMAdvanceResult ExecuteStoreLocal(CallFrame frame, int localIndex)
        {
            if ((uint)localIndex >= (uint)frame.Locals.Count)
                return Fault(RuntimeError.InvalidLocalAccess(localIndex));

            if (!evaluationStack.TryPop(frame.EvalStackBase, out var value))
                return Fault(RuntimeError.StackUnderflow());

            frame.Locals.TrySet(localIndex, value);
            return AdvanceInstruction(frame);
        }

        private VMAdvanceResult ExecuteLoadGlobal(CallFrame frame, int globalIndex)
        {
            if (!Memory.TryGetGlobal(globalIndex, out var value))
                return Fault(RuntimeError.InvalidGlobalAccess(globalIndex));

            if (!evaluationStack.TryPush(value))
                return Fault(RuntimeError.StackOverflow());

            return AdvanceInstruction(frame);
        }

        private VMAdvanceResult ExecuteStoreGlobal(CallFrame frame, int globalIndex)
        {
            if ((uint)globalIndex >= (uint)Memory.GlobalCount)
                return Fault(RuntimeError.InvalidGlobalAccess(globalIndex));

            if (!evaluationStack.TryPop(frame.EvalStackBase, out var value))
                return Fault(RuntimeError.StackUnderflow());

            Memory.TrySetGlobal(globalIndex, value);
            return AdvanceInstruction(frame);
        }

        /// <summary>
        /// Pops the operation arguments, dispatches the operation and handles its first result.
        /// </summary>
        private VMAdvanceResult ExecuteOperation(
            CallFrame frame,
            OperationId operationId)
        {
            var info = OperationCatalog.GetInfo(operationId);

            if (!evaluationStack.TryPopArguments(
                    frame.EvalStackBase,
                    info.ParameterCount,
                    out var arguments))
            {
                return Fault(RuntimeError.StackUnderflow());
            }

            var dispatch = OperationDispatcher.Start(
                operationId,
                arguments,
                Memory
            );

            return HandleOperationResult(
                frame,
                operationId,
                arguments,
                dispatch.ReceiverIndex,
                dispatch.Result
            );
        }

        /// <summary>
        /// Advances an operation that previously returned Continued without moving the instruction pointer.
        /// </summary>
        private VMAdvanceResult ContinueOperation(CallFrame frame)
        {
            var pending = pendingOperation!;
            var result = OperationDispatcher.Continue(
                pending.OperationId,
                pending.Arguments,
                pending.ReceiverIndex,
                pending.State,
                Memory
            );

            return HandleOperationResult(
                frame,
                pending.OperationId,
                pending.Arguments,
                pending.ReceiverIndex,
                result
            );
        }

        /// <summary>
        /// Applies an operation result to VM state: push success, fault, or preserve continuation state.
        /// </summary>
        private VMAdvanceResult HandleOperationResult(
            CallFrame frame,
            OperationId operationId,
            DataValue[] arguments,
            int receiverIndex,
            OperationResult result)
        {
            switch (result.Status)
            {
                case OperationStatus.Success:
                    pendingOperation = null;

                    if (!evaluationStack.TryPush(result.Value))
                        return Fault(RuntimeError.StackOverflow());

                    return AdvanceInstruction(frame);

                case OperationStatus.Error:
                    pendingOperation = null;
                    return Fault(
                        result.Error ?? RuntimeError.InternalVmError(
                            $"Operation {operationId} returned Error without RuntimeError."
                        )
                    );

                case OperationStatus.Continued:
                    if (result.State == null)
                    {
                        pendingOperation = null;
                        return Fault(
                            RuntimeError.InternalVmError(
                                $"Operation {operationId} returned Continued without state."
                            )
                        );
                    }

                    if (pendingOperation == null)
                    {
                        if (receiverIndex < 0)
                        {
                            return Fault(
                                RuntimeError.InternalVmError(
                                    $"Operation {operationId} continued without a receiver."
                                )
                            );
                        }

                        pendingOperation = new PendingOperation(
                            operationId,
                            arguments,
                            receiverIndex,
                            result.State
                        );
                    }
                    else
                    {
                        pendingOperation.State = result.State;
                    }

                    return VMAdvanceResult.Running();

                case OperationStatus.NotImplemented:
                default:
                    pendingOperation = null;
                    return Fault(
                        RuntimeError.InternalVmError(
                            $"Operation dispatcher leaked status {result.Status} for {operationId}."
                        )
                    );
            }
        }

        private VMAdvanceResult ExecuteJump(CallFrame frame, int target)
        {
            if (!IsValidJumpTarget(frame, target))
                return Fault(RuntimeError.InvalidJumpTarget(target));

            frame.InstructionPointer = target;
            return VMAdvanceResult.Running();
        }

        private VMAdvanceResult ExecuteConditionalJump(
            CallFrame frame,
            int target,
            bool jumpWhenTrue)
        {
            if (!IsValidJumpTarget(frame, target))
                return Fault(RuntimeError.InvalidJumpTarget(target));

            if (!evaluationStack.TryPop(frame.EvalStackBase, out var condition))
                return Fault(RuntimeError.StackUnderflow());

            if (condition.dataType == DataTypeId.Uninitialized)
                return Fault(RuntimeError.VariableUsedBeforeInitialization());

            if (condition.dataType != DataTypeId.Bool)
            {
                return Fault(
                    RuntimeError.InvalidOperation(
                        $"Conditional jump requires Bool but received {condition.dataType}. " +
                        "Emit IsTruthy before the jump."
                    )
                );
            }

            bool conditionValue = condition.value != 0;

            if (conditionValue == jumpWhenTrue)
                frame.InstructionPointer = target;
            else
                frame.InstructionPointer++;

            return VMAdvanceResult.Running();
        }

        /// <summary>
        /// Creates a new call frame after consuming the callee arguments from the evaluation stack.
        /// </summary>
        private VMAdvanceResult ExecuteCall(CallFrame caller, int functionId)
        {
            if (!Code.TryGetFunction(functionId, out var function))
                return Fault(RuntimeError.InvalidFunction(functionId));

            if (callStack.Count >= options.MaxCallDepth)
                return Fault(RuntimeError.CallStackOverflow());

            if (!evaluationStack.TryPopArguments(
                    caller.EvalStackBase,
                    function.ParameterCount,
                    out var arguments))
            {
                return Fault(RuntimeError.StackUnderflow());
            }

            // The caller is already positioned after CALL. RETURN only has to restore its stack.
            caller.InstructionPointer++;

            callStack.Add(
                new CallFrame(
                    function,
                    evaluationStack.Count,
                    arguments
                )
            );

            return VMAdvanceResult.Running();
        }

        /// <summary>
        /// Removes the current call frame and forwards its return value to the caller or completes the VM.
        /// </summary>
        private VMAdvanceResult ExecuteReturn(CallFrame frame)
        {
            if (!evaluationStack.TryPop(frame.EvalStackBase, out var result))
                return Fault(RuntimeError.StackUnderflow());

            if (result.dataType == DataTypeId.Uninitialized)
                return Fault(RuntimeError.VariableUsedBeforeInitialization());

            evaluationStack.TrimTo(frame.EvalStackBase);
            callStack.RemoveAt(callStack.Count - 1);

            if (callStack.Count == 0)
            {
                returnValue = result;
                Status = VMExecutionStatus.Completed;
                return VMAdvanceResult.Completed(result);
            }

            if (!evaluationStack.TryPush(result))
                return Fault(RuntimeError.StackOverflow());

            return VMAdvanceResult.Running();
        }

        private VMAdvanceResult ExecuteContextStart(
            CallFrame frame,
            int firstLocal,
            int localCount)
        {
            if (!frame.TryEnterContext(firstLocal, localCount))
            {
                return Fault(
                    RuntimeError.InvalidInstruction(
                        $"Invalid local context range [{firstLocal}, {firstLocal + localCount})."
                    )
                );
            }

            return AdvanceInstruction(frame);
        }

        private VMAdvanceResult ExecuteContextEnd(CallFrame frame)
        {
            if (!frame.TryExitContext())
            {
                return Fault(
                    RuntimeError.InvalidInstruction(
                        "ContextEnd has no matching ContextStart."
                    )
                );
            }

            return AdvanceInstruction(frame);
        }

        private VMAdvanceResult ExecuteDebugFile(CallFrame frame, string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return Fault(
                    RuntimeError.InvalidInstruction(
                        "DebugFile requires a non-empty file name."
                    )
                );
            }

            Debug.File = fileName;
            return AdvanceInstruction(frame);
        }

        private VMAdvanceResult ExecuteDebugLine(CallFrame frame, int line)
        {
            if (line < 0)
            {
                return Fault(
                    RuntimeError.InvalidInstruction(
                        $"DebugLine cannot use negative line {line}."
                    )
                );
            }

            Debug.Line = line;
            return AdvanceInstruction(frame);
        }

        private VMAdvanceResult ExecuteDebugSpan(CallFrame frame, int start, int end)
        {
            if (start < 0 || end < start)
            {
                return Fault(
                    RuntimeError.InvalidInstruction(
                        $"Invalid debug span [{start}, {end})."
                    )
                );
            }

            Debug.SpanStart = start;
            Debug.SpanEnd = end;
            return AdvanceInstruction(frame);
        }

        private static bool IsValidJumpTarget(CallFrame frame, int target)
            => target >= 0 && target < frame.Function.InstructionCount;

        private static VMAdvanceResult AdvanceInstruction(CallFrame frame)
        {
            frame.InstructionPointer++;
            return VMAdvanceResult.Running();
        }

        /// <summary>
        /// Permanently faults this VM instance and exposes the runtime error to its caller.
        /// </summary>
        private VMAdvanceResult Fault(RuntimeError error)
        {
            pendingOperation = null;
            fault = error;
            Status = VMExecutionStatus.Faulted;
            return VMAdvanceResult.Faulted(error);
        }
    }
}
