#nullable enable
using System;
using System.Collections.Generic;
using DSExecution.DataTypes;
using DSExecution.Native;
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
            public InstructionId Kind { get; }
            public string? MemberName { get; }
            public int MemberArgumentCount { get; }
            public DataValue[] Arguments { get; }
            public int ReceiverIndex { get; }
            public OperationState State { get; set; }

            public PendingOperation(
                OperationId operationId,
                DataValue[] arguments,
                int receiverIndex,
                OperationState state,
                InstructionId kind = InstructionId.Operation,
                string? memberName = null,
                int memberArgumentCount = 0)
            {
                OperationId = operationId;
                Kind = kind;
                MemberName = memberName;
                MemberArgumentCount = memberArgumentCount;
                Arguments = arguments;
                ReceiverIndex = receiverIndex;
                State = state;
            }
        }

        private readonly EvaluationStack evaluationStack;
        private readonly List<CallFrame> callStack = new();
        private readonly VMOptions options;

        private PendingOperation? pendingOperation;
        private DataValue[]? activeOperationArguments;
        private DataValue? activeCastValue;
        // This root is owned by the VM, not OperationState. Zero means absent.
        private uint operationAuxPointer;
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

        /// <summary>
        /// Logical cost of the next Advance. DEBUG_* metadata costs zero; ordinary
        /// and continued instructions cost one. This does not imply zero CPU time.
        /// </summary>
        public int NextInstructionCost
        {
            get
            {
                if (Status != VMExecutionStatus.Running || pendingOperation != null ||
                    CurrentFunction == null ||
                    !CurrentFunction.TryGetInstruction(InstructionPointer, out Instruction current))
                    return 1;
                return current.Id == InstructionId.DebugFile ||
                    current.Id == InstructionId.DebugLine ||
                    current.Id == InstructionId.DebugSpan ? 0 : 1;
            }
        }

        /// <summary>
        /// Emits text produced by the PRINT instruction. The VM itself is UI-agnostic;
        /// hosts decide where this output is displayed.
        /// </summary>
        public event Action<string>? Printed;

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
            Memory.RegisterVM(this);
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
            int advances = 0;
            VMAdvanceResult lastResult = Status switch
            {
                VMExecutionStatus.Completed => VMAdvanceResult.Completed(returnValue),
                VMExecutionStatus.Faulted => VMAdvanceResult.Faulted(fault!),
                _ => VMAdvanceResult.Running()
            };

            while (steps < instructionBudget && Status == VMExecutionStatus.Running)
            {
                int cost = NextInstructionCost;
                lastResult = Advance();
                advances++;
                steps += cost;
            }

            bool budgetExhausted =
                steps == instructionBudget &&
                Status == VMExecutionStatus.Running;

            return new VMRunResult(
                Status,
                steps,
                advances,
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
                PendingOperation pending = pendingOperation;
                if (pending.Kind != instruction.Id ||
                    (pending.Kind == InstructionId.Operation
                        ? instruction.OperationOperand != pending.OperationId
                        : instruction.TextOperand != pending.MemberName ||
                          (pending.Kind == InstructionId.CallMethod &&
                           instruction.OperandA != pending.MemberArgumentCount)))
                {
                    return Fault(RuntimeError.InternalVmError(
                        "Pending operation no longer matches the current instruction."));
                }

                return pending.Kind == InstructionId.Operation
                    ? ContinueOperation(frame) : ContinueMember(frame);
            }

            return instruction.Id switch
            {
                InstructionId.Nop => AdvanceInstruction(frame),
                InstructionId.Push => ExecutePush(frame, instruction.ValueOperand),
                InstructionId.PushString => ExecutePushString(frame, instruction.TextOperand),
                InstructionId.Pop => ExecutePop(frame),
                InstructionId.Cast => ExecuteCast(frame, instruction.DataTypeOperand),
                InstructionId.Print => ExecutePrint(frame),
                InstructionId.Tuple => ExecuteTuple(frame, instruction.OperandA),
                InstructionId.List => ExecuteList(frame, instruction.OperandA),
                InstructionId.Set => ExecuteSet(frame, instruction.OperandA),
                InstructionId.Dict => ExecuteDict(frame, instruction.OperandA),
                InstructionId.Range => ExecuteRange(frame, instruction.OperandA),
                InstructionId.IterInit => ExecuteIterInit(frame),
                InstructionId.IterNext => ExecuteIterNext(frame, instruction.OperandA),
                InstructionId.GetProperty => ExecuteMember(frame, InstructionId.GetProperty,
                    instruction.TextOperand, 0),
                InstructionId.SetProperty => ExecuteMember(frame, InstructionId.SetProperty,
                    instruction.TextOperand, 1),
                InstructionId.CallMethod => ExecuteMember(frame, InstructionId.CallMethod,
                    instruction.TextOperand, instruction.OperandA),
                InstructionId.LoadLocal => ExecuteLoadLocal(frame, instruction.OperandA),
                InstructionId.StoreLocal => ExecuteStoreLocal(frame, instruction.OperandA),
                InstructionId.LoadGlobal => ExecuteLoadGlobal(frame, instruction.OperandA),
                InstructionId.StoreGlobal => ExecuteStoreGlobal(frame, instruction.OperandA),
                InstructionId.Operation => ExecuteOperation(frame, instruction.OperationOperand),
                InstructionId.Jump => ExecuteJump(frame, instruction.OperandA),
                InstructionId.JumpIfTrue => ExecuteConditionalJump(frame, instruction.OperandA, true),
                InstructionId.JumpIfFalse => ExecuteConditionalJump(frame, instruction.OperandA, false),
                InstructionId.Call => ExecuteCall(frame, instruction.OperandA),
                InstructionId.PushFunction => ExecutePushFunction(frame, instruction.OperandA),
                InstructionId.CallIndirect => ExecuteCallIndirect(frame, instruction.OperandA),
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

        private VMAdvanceResult ExecutePushString(CallFrame frame, string? value)
        {
            if (value == null)
                return Fault(RuntimeError.InvalidInstruction("PushString requires a string literal."));

            var heapObject = new StringHeapObject(value);
            if (!Memory.TryAllocHeap(heapObject, out uint memoryReference))
                return Fault(RuntimeError.OutOfMemory());

            if (!evaluationStack.TryPush(DataValue.FromStringReference(memoryReference)))
                return Fault(RuntimeError.StackOverflow());

            return AdvanceInstruction(frame);
        }

        /// <summary>
        /// Builds a tuple from the top count stack values. All inputs remain GC roots
        /// until the new heap allocation succeeds; only then is the stack replaced.
        /// </summary>
        private VMAdvanceResult ExecuteTuple(CallFrame frame, int count)
        {
            if (count < 0)
                return Fault(RuntimeError.InvalidInstruction($"TUPLE requires a non-negative count ({count})."));

            if (!evaluationStack.HasValuesAbove(frame.EvalStackBase, count))
                return Fault(RuntimeError.StackUnderflow());

            if (!evaluationStack.CanReplaceTopWithOne(frame.EvalStackBase, count))
                return Fault(RuntimeError.StackOverflow());

            if (!evaluationStack.TryPeekArguments(frame.EvalStackBase, count, out DataValue[] elements))
                return Fault(RuntimeError.InternalVmError("TUPLE failed to peek validated stack values."));

            foreach (DataValue element in elements)
            {
                if (element.dataType == DataTypeId.Uninitialized)
                    return Fault(RuntimeError.VariableUsedBeforeInitialization());
            }

            var tuple = new TupleHeapObject(elements);
            if (!Memory.TryAllocHeap(tuple, out uint tupleReference))
                return Fault(RuntimeError.OutOfMemory());

            if (!evaluationStack.TryReplaceTopWithOne(
                    frame.EvalStackBase, count, DataValue.FromTupleReference(tupleReference)))
            {
                Memory.FreeHeap(tupleReference);
                return Fault(RuntimeError.InternalVmError("TUPLE could not commit the stack replacement."));
            }

            return AdvanceInstruction(frame);
        }

        /// <summary>Builds a list atomically with stack inputs rooted during lazy GC.</summary>
        private VMAdvanceResult ExecuteList(CallFrame frame, int count)
        {
            if (count < 0)
                return Fault(RuntimeError.InvalidInstruction("LIST requires a non-negative count."));
            if (!evaluationStack.HasValuesAbove(frame.EvalStackBase, count))
                return Fault(RuntimeError.StackUnderflow());
            if (!evaluationStack.CanReplaceTopWithOne(frame.EvalStackBase, count))
                return Fault(RuntimeError.StackOverflow());
            if (!evaluationStack.TryPeekArguments(frame.EvalStackBase, count, out DataValue[] elements))
                return Fault(RuntimeError.StackUnderflow());

            foreach (DataValue element in elements)
                if (element.dataType == DataTypeId.Uninitialized)
                    return Fault(RuntimeError.VariableUsedBeforeInitialization());

            if (!Memory.TryAllocHeap(new ListHeapObject(elements), out uint reference))
                return Fault(RuntimeError.OutOfMemory());
            if (!evaluationStack.TryReplaceTopWithOne(
                    frame.EvalStackBase, count, DataValue.FromListReference(reference)))
                return Fault(RuntimeError.InternalVmError("LIST stack replacement failed after validation."));
            return AdvanceInstruction(frame);
        }

        /// <summary>
        /// Builds a set from stack values, discarding equal duplicates. Validation and
        /// hashing happen before heap allocation; stack values stay rooted during GC.
        /// </summary>
        private VMAdvanceResult ExecuteSet(CallFrame frame, int count)
        {
            if (count < 0)
                return Fault(RuntimeError.InvalidInstruction("SET requires a non-negative count."));
            if (!evaluationStack.HasValuesAbove(frame.EvalStackBase, count))
                return Fault(RuntimeError.StackUnderflow());
            if (!evaluationStack.CanReplaceTopWithOne(frame.EvalStackBase, count))
                return Fault(RuntimeError.StackOverflow());
            if (!evaluationStack.TryPeekArguments(frame.EvalStackBase, count, out DataValue[] members))
                return Fault(RuntimeError.StackUnderflow());

            if (!DTSet.TryConstruct(members, Memory, out SetHeapObject set, out RuntimeError error))
                return Fault(error);
            if (!Memory.TryAllocHeap(set, out uint reference))
                return Fault(RuntimeError.OutOfMemory());
            if (!evaluationStack.TryReplaceTopWithOne(
                    frame.EvalStackBase, count, DataValue.FromSetReference(reference)))
            {
                Memory.FreeHeap(reference);
                return Fault(RuntimeError.InternalVmError("SET stack replacement failed after validation."));
            }
            return AdvanceInstruction(frame);
        }

        /// <summary>
        /// DICT n consumes n ordered key/value pairs; unique equivalent keys keep their
        /// first position while later values replace older values. Inputs remain stack
        /// GC roots until construction has succeeded and the resulting object is stored.
        /// </summary>
        private VMAdvanceResult ExecuteDict(CallFrame frame, int count)
        {
            if (count < 0 || count > int.MaxValue / 2)
                return Fault(RuntimeError.InvalidInstruction("DICT needs a valid non-negative pair count."));
            int stackCount = count * 2;
            if (!evaluationStack.HasValuesAbove(frame.EvalStackBase, stackCount))
                return Fault(RuntimeError.StackUnderflow());
            if (!evaluationStack.CanReplaceTopWithOne(frame.EvalStackBase, stackCount))
                return Fault(RuntimeError.StackOverflow());
            if (!evaluationStack.TryPeekArguments(frame.EvalStackBase, stackCount, out DataValue[] pairs))
                return Fault(RuntimeError.StackUnderflow());
            if (!DTDict.TryConstruct(pairs, Memory, out DictHeapObject dict, out RuntimeError error))
                return Fault(error);
            if (!Memory.TryAllocHeap(dict, out uint reference))
                return Fault(RuntimeError.OutOfMemory());
            if (!evaluationStack.TryReplaceTopWithOne(
                    frame.EvalStackBase, stackCount, DataValue.FromDictReference(reference)))
            {
                Memory.FreeHeap(reference);
                return Fault(RuntimeError.InternalVmError("DICT stack replacement failed after validation."));
            }
            return AdvanceInstruction(frame);
        }

        /// <summary>
        /// Constructs a lazy arithmetic progression. RANGE 1/2/3 accepts signed Int/Bool
        /// arguments; the input stack remains rooted until allocation succeeds.
        /// </summary>
        private VMAdvanceResult ExecuteRange(CallFrame frame, int count)
        {
            if (count < 1 || count > 3)
                return Fault(RuntimeError.InvalidInstruction("RANGE expects 1, 2 or 3 arguments."));
            if (!evaluationStack.HasValuesAbove(frame.EvalStackBase, count))
                return Fault(RuntimeError.StackUnderflow());
            if (!evaluationStack.TryPeekArguments(frame.EvalStackBase, count, out DataValue[] args))
                return Fault(RuntimeError.StackUnderflow());
            foreach (DataValue argument in args)
            {
                if (argument.dataType == DataTypeId.Uninitialized)
                    return Fault(RuntimeError.VariableUsedBeforeInitialization());
                if (argument.dataType != DataTypeId.Int && argument.dataType != DataTypeId.Bool)
                    return Fault(RuntimeError.InvalidOperation("RANGE requires Int/Bool arguments."));
            }
            long start = count > 1 ? unchecked((long)args[0].value) : 0L;
            long stop = count > 1 ? unchecked((long)args[1].value) : unchecked((long)args[0].value);
            long step = count == 3 ? unchecked((long)args[2].value) : 1L;
            if (step == 0)
                return Fault(RuntimeError.InvalidOperation("RANGE step cannot be zero."));
            if (!Memory.TryAllocHeap(new RangeHeapObject(start, stop, step), out uint reference))
                return Fault(RuntimeError.OutOfMemory());
            if (!evaluationStack.TryReplaceTopWithOne(
                    frame.EvalStackBase, count, DataValue.FromRangeReference(reference)))
            {
                Memory.FreeHeap(reference);
                return Fault(RuntimeError.InternalVmError("RANGE stack replacement failed."));
            }
            return AdvanceInstruction(frame);
        }

        /// <summary>
        /// ITER_INIT replaces one iterable with a cursor. An existing iterator is its
        /// own iterator, preserving the position rather than restarting traversal.
        /// </summary>
        private VMAdvanceResult ExecuteIterInit(CallFrame frame)
        {
            if (!evaluationStack.TryPeekArguments(frame.EvalStackBase, 1, out DataValue[] top))
                return Fault(RuntimeError.StackUnderflow());
            DataValue sourceValue = top[0];
            if (sourceValue.dataType == DataTypeId.Uninitialized)
                return Fault(RuntimeError.VariableUsedBeforeInitialization());
            if (sourceValue.dataType == DataTypeId.Iterator)
            {
                if (!sourceValue.TryGetHeapReference(out uint existing) ||
                    !Memory.TryGetHeap(existing, out HeapObject iteratorObject) ||
                    !(iteratorObject is IteratorHeapObject))
                    return Fault(RuntimeError.InvalidMemoryAccess((uint)sourceValue.value));
                return AdvanceInstruction(frame);
            }
            if (sourceValue.dataType != DataTypeId.Tuple &&
                sourceValue.dataType != DataTypeId.List &&
                sourceValue.dataType != DataTypeId.Set &&
                sourceValue.dataType != DataTypeId.Dict &&
                sourceValue.dataType != DataTypeId.Range)
                return Fault(RuntimeError.NotIterable(sourceValue.dataType));
            if (!sourceValue.TryGetHeapReference(out uint sourceReference) ||
                !Memory.TryGetHeap(sourceReference, out HeapObject source))
                return Fault(RuntimeError.InvalidMemoryAccess((uint)sourceValue.value));
            // Check the heap object's kind explicitly instead of accepting forged values.
            bool matching = sourceValue.dataType switch
            {
                DataTypeId.Tuple => source is TupleHeapObject,
                DataTypeId.List => source is ListHeapObject,
                DataTypeId.Set => source is SetHeapObject,
                DataTypeId.Dict => source is DictHeapObject,
                DataTypeId.Range => source is RangeHeapObject,
                _ => false
            };
            if (!matching)
                return Fault(RuntimeError.InvalidOperation("Iterable reference has the wrong heap object type."));
            var cursor = new IteratorHeapObject(sourceValue.dataType, sourceReference, source);
            if (!Memory.TryAllocHeap(cursor, out uint iteratorReference))
                return Fault(RuntimeError.OutOfMemory());
            if (!evaluationStack.TryReplaceTopWithOne(
                    frame.EvalStackBase, 1, DataValue.FromIteratorReference(iteratorReference)))
            {
                Memory.FreeHeap(iteratorReference);
                return Fault(RuntimeError.InternalVmError("ITER_INIT could not commit its cursor."));
            }
            return AdvanceInstruction(frame);
        }

        /// <summary>
        /// Success: [cursor] becomes [cursor, nextItem], even when nextItem is None.
        /// Exhaustion: consumes cursor and jumps to endTarget. Each check costs one
        /// VM step. Stack overflow is checked before moving the cursor.
        /// </summary>
        private VMAdvanceResult ExecuteIterNext(CallFrame frame, int endTarget)
        {
            if (!IsValidJumpTarget(frame, endTarget))
                return Fault(RuntimeError.InvalidJumpTarget(endTarget));
            if (!evaluationStack.TryPeekArguments(frame.EvalStackBase, 1, out DataValue[] top))
                return Fault(RuntimeError.StackUnderflow());
            DataValue iterator = top[0];
            if (iterator.dataType == DataTypeId.Uninitialized)
                return Fault(RuntimeError.VariableUsedBeforeInitialization());
            if (iterator.dataType != DataTypeId.Iterator)
                return Fault(RuntimeError.InvalidIterator(iterator.dataType));
            if (!iterator.TryGetHeapReference(out uint reference) ||
                !Memory.TryGetHeap(reference, out HeapObject heapObject) ||
                !(heapObject is IteratorHeapObject cursor))
                return Fault(RuntimeError.InvalidMemoryAccess((uint)iterator.value));
            // Detect exhaustion before checking stack capacity. An empty iterator may
            // jump with no free stack slots; a nonempty cursor must not advance if it
            // cannot push an item. These checks are uninterrupted in one VM step.
            if (!cursor.TryHasNext(Memory, out bool available, out RuntimeError peekError))
                return Fault(peekError);
            if (!available)
            {
                if (!evaluationStack.TryPop(frame.EvalStackBase, out _))
                    return Fault(RuntimeError.InternalVmError("ITER_NEXT exhausted but stack lost cursor."));
                frame.InstructionPointer = endTarget;
                return VMAdvanceResult.Running();
            }
            if (!evaluationStack.CanReplaceTopWithOne(frame.EvalStackBase, 0))
                return Fault(RuntimeError.StackOverflow());
            bool hasValue = cursor.TryGetNext(Memory, out DataValue item, out RuntimeError error);
            if (error != null)
                return Fault(error);
            if (!hasValue)
                return Fault(RuntimeError.InternalVmError("Iterator became exhausted during a single VM step."));
            if (!evaluationStack.TryPush(item))
                return Fault(RuntimeError.InternalVmError("ITER_NEXT failed after capacity check."));
            return AdvanceInstruction(frame);
        }

        private VMAdvanceResult ExecutePop(CallFrame frame)
        {
            if (!evaluationStack.TryPop(frame.EvalStackBase, out _))
                return Fault(RuntimeError.StackUnderflow());

            return AdvanceInstruction(frame);
        }

        private VMAdvanceResult ExecuteCast(CallFrame frame, DataTypeId targetType)
        {
            if (!evaluationStack.TryPop(frame.EvalStackBase, out DataValue value))
                return Fault(RuntimeError.StackUnderflow());

            CastResult cast = CastValue(value, targetType);
            if (!cast.IsSuccess)
            {
                return Fault(
                    cast.Error ?? RuntimeError.InternalVmError(
                        $"Cast from {value.dataType} to {targetType} failed without RuntimeError."
                    )
                );
            }

            if (!evaluationStack.TryPush(cast.Value))
                return Fault(RuntimeError.StackOverflow());

            return AdvanceInstruction(frame);
        }

        private VMAdvanceResult ExecutePrint(CallFrame frame)
        {
            if (!evaluationStack.TryPop(frame.EvalStackBase, out DataValue value))
                return Fault(RuntimeError.StackUnderflow());

            CastResult cast = CastValue(value, DataTypeId.String);
            if (!cast.IsSuccess)
            {
                return Fault(
                    cast.Error ?? RuntimeError.InternalVmError(
                        $"PRINT failed to cast {value.dataType} to String without RuntimeError."
                    )
                );
            }

            DataValue stringValue = cast.Value;
            activeCastValue = stringValue;
            try
            {
                if (!stringValue.TryGetHeapReference(out uint memoryReference))
                {
                    return Fault(
                        RuntimeError.InternalVmError(
                            "CAST STRING returned a non-heap-backed value."
                        )
                    );
                }

                if (!Memory.TryGetHeap(memoryReference, out HeapObject heapObject))
                    return Fault(RuntimeError.InvalidMemoryAccess(memoryReference));

                if (!(heapObject is StringHeapObject stringObject))
                {
                    return Fault(
                        RuntimeError.InternalVmError(
                            $"String reference {memoryReference} does not point to StringHeapObject."
                        )
                    );
                }

                Printed?.Invoke(stringObject.Value);
            }
            finally
            {
                activeCastValue = null;
            }

            return AdvanceInstruction(frame);
        }

        /// <summary>
        /// Performs a DataType-owned cast while keeping the source value visible to GC.
        /// This matters for future reference-type casts that may allocate and trigger collection.
        /// </summary>
        private CastResult CastValue(DataValue value, DataTypeId targetType)
        {
            if (value.dataType == DataTypeId.Uninitialized)
                return CastResult.Failure(RuntimeError.VariableUsedBeforeInitialization());

            DataType? sourceType = DataType.GetDataType(value.dataType);
            if (sourceType == null)
                return CastResult.Failure(RuntimeError.InvalidCast(value.dataType, targetType));

            activeCastValue = value;
            try
            {
                return sourceType.CastTo(targetType, value, Memory);
            }
            finally
            {
                activeCastValue = null;
            }
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

            activeOperationArguments = arguments;
            try
            {
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
            finally
            {
                activeOperationArguments = null;
            }
        }

        /// <summary>
        /// Member calls reuse OperationCall/OperationResult and the same VM-owned GC roots
        /// as ordinary operations; DIL never sees private continuation buffers.
        /// </summary>
        private VMAdvanceResult ExecuteMember(CallFrame frame, InstructionId kind,
            string? name, int argumentCount)
        {
            if (string.IsNullOrEmpty(name) || argumentCount < 0 || argumentCount == int.MaxValue)
                return Fault(RuntimeError.InvalidInstruction("Invalid member invocation operands."));

            int total = argumentCount + 1;
            if (!evaluationStack.TryPopArguments(frame.EvalStackBase, total, out DataValue[] arguments))
                return Fault(RuntimeError.StackUnderflow());
            foreach (DataValue value in arguments)
                if (value.dataType == DataTypeId.Uninitialized)
                    return Fault(RuntimeError.VariableUsedBeforeInitialization());

            activeOperationArguments = arguments;
            try
            {
                OperationResult result = DispatchMember(kind, name, arguments, null, 0);
                return HandleOperationResult(frame, default, arguments, 0, result,
                    kind, name, argumentCount);
            }
            finally
            {
                activeOperationArguments = null;
            }
        }

        private OperationResult DispatchMember(InstructionId kind, string name,
            DataValue[] arguments, OperationState? state, uint auxiliaryReference)
        {
            // Resolve the name against the receiver's runtime DataType. Never translate
            // it globally into an OperationId: List.pop, Set.pop and Dict.pop have
            // distinct signatures and/or primitive operations. Annotated handlers
            // decide whether to delegate to an existing native operation.
            DataType? type = DataType.GetDataType(arguments[0].dataType);
            if (type == null)
                return OperationResult.OpError(RuntimeError.MemberNotFound(
                    arguments[0].dataType.ToString(), name));
            DScriptMemberKind memberKind = kind switch
            {
                InstructionId.GetProperty => DScriptMemberKind.Getter,
                InstructionId.SetProperty => DScriptMemberKind.Setter,
                _ => DScriptMemberKind.Method
            };
            return type.InvokeMember(memberKind, name,
                new OperationCall(arguments, 0, Memory, state, auxiliaryReference));
        }

        private VMAdvanceResult ContinueMember(CallFrame frame)
        {
            PendingOperation pending = pendingOperation!;
            OperationResult result = DispatchMember(pending.Kind, pending.MemberName!,
                pending.Arguments, pending.State, operationAuxPointer);
            return HandleOperationResult(frame, default, pending.Arguments, 0, result,
                pending.Kind, pending.MemberName, pending.MemberArgumentCount);
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
                Memory,
                operationAuxPointer
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
            OperationResult result,
            InstructionId kind = InstructionId.Operation,
            string? memberName = null,
            int memberArgumentCount = 0)
        {
            string label = kind == InstructionId.Operation ? $"Operation {operationId}" :
                $"Member {kind} '{memberName}'";
            switch (result.Status)
            {
                case OperationStatus.Success:
                    pendingOperation = null;
                    operationAuxPointer = 0;

                    if (kind == InstructionId.SetProperty)
                    {
                        if (result.Value.dataType != DataTypeId.None)
                            return Fault(RuntimeError.InternalVmError(
                                $"{label} setter returned a value instead of None."));
                    }
                    else if (!evaluationStack.TryPush(result.Value))
                        return Fault(RuntimeError.StackOverflow());

                    return AdvanceInstruction(frame);

                case OperationStatus.Error:
                    pendingOperation = null;
                    operationAuxPointer = 0;
                    return Fault(
                        result.Error ?? RuntimeError.InternalVmError(
                            $"{label} returned Error without RuntimeError."
                        )
                    );

                case OperationStatus.Continued:
                    if (result.State == null)
                    {
                        pendingOperation = null;
                        return Fault(
                            RuntimeError.InternalVmError(
                                $"{label} returned Continued without state."
                            )
                        );
                    }

                    if (result.AuxiliaryReference != operationAuxPointer && pendingOperation != null)
                        return Fault(RuntimeError.InternalVmError(
                            $"{label} changed its auxiliary reference mid-continuation."));

                    if (result.AuxiliaryReference != 0 &&
                        !Memory.TryGetHeap(result.AuxiliaryReference, out _))
                        return Fault(RuntimeError.InternalVmError(
                            $"{label} returned a missing auxiliary heap reference."));

                    if (pendingOperation == null)
                    {
                        if (receiverIndex < 0)
                        {
                            return Fault(
                                RuntimeError.InternalVmError(
                                    $"{label} continued without a receiver."
                                )
                            );
                        }

                        pendingOperation = new PendingOperation(
                            operationId,
                            arguments,
                            receiverIndex,
                            result.State,
                            kind,
                            memberName,
                            memberArgumentCount
                        );
                        operationAuxPointer = result.AuxiliaryReference;
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
                            $"Member/operation dispatcher leaked status {result.Status} for {label}."
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

        /// <summary>Pushes a program-local compiled-function reference with no heap allocation.</summary>
        private VMAdvanceResult ExecutePushFunction(CallFrame frame, int functionId)
        {
            if (!Code.ContainsCallable(functionId))
                return Fault(RuntimeError.InvalidFunction(functionId));

            if (!Memory.TryBindFunctionProgram(Code))
                return Fault(RuntimeError.ForeignFunctionProgram());

            return ExecutePush(frame, DataValue.FromFunctionId(functionId));
        }

        /// <summary>
        /// Calls an already compiled function obtained from the stack. The stack layout
        /// is [function, argument0, ..., argumentN-1] and CALL_INDIRECT N consumes
        /// all N+1 values. No closures or runtime compilation are involved.
        /// </summary>
        private VMAdvanceResult ExecuteCallIndirect(CallFrame caller, int argumentCount)
        {
            if (argumentCount < 0 || argumentCount == int.MaxValue)
                return Fault(RuntimeError.InvalidInstruction(
                    "CALL_INDIRECT requires a non-negative argument count below Int32.MaxValue."));

            if (!evaluationStack.TryPeekArguments(
                    caller.EvalStackBase, argumentCount + 1, out DataValue[] operands))
                return Fault(RuntimeError.StackUnderflow());

            DataValue callee = operands[0];
            if (callee.dataType == DataTypeId.Uninitialized)
                return Fault(RuntimeError.VariableUsedBeforeInitialization());
            if (callee.dataType != DataTypeId.Function)
                return Fault(RuntimeError.InvalidCallTarget(callee.dataType));

            if (!callee.TryGetFunctionId(out int functionId) || !Code.ContainsCallable(functionId))
                return Fault(RuntimeError.InvalidFunction(functionId));

            if (!Memory.TryBindFunctionProgram(Code))
                return Fault(RuntimeError.ForeignFunctionProgram());

            if (Code.TryGetNativeFunction(functionId, out NativeFunction native))
                return ExecuteNativeCall(caller, native, argumentCount, true);

            if (!Code.TryGetFunction(functionId, out FunctionContext function))
                return Fault(RuntimeError.InvalidFunction(functionId));
            if (function.ParameterCount != argumentCount)
                return Fault(RuntimeError.InvalidFunctionArgumentCount(
                    functionId, function.ParameterCount, argumentCount));

            if (callStack.Count >= options.MaxCallDepth)
                return Fault(RuntimeError.CallStackOverflow());

            // All validation has succeeded. Committing a call is allocation-free in
            // the Directiva heap, and arguments move directly into the callee frame.
            if (!evaluationStack.TryPopArguments(
                    caller.EvalStackBase, argumentCount + 1, out DataValue[] popped))
                return Fault(RuntimeError.InternalVmError(
                    "Validated CALL_INDIRECT arguments disappeared from the stack."));

            DataValue[] arguments = new DataValue[argumentCount];
            Array.Copy(popped, 1, arguments, 0, argumentCount);
            caller.InstructionPointer++;
            callStack.Add(new CallFrame(function, evaluationStack.Count, arguments));
            return VMAdvanceResult.Running();
        }

        /// <summary>
        /// Creates a new call frame after consuming the callee arguments from the evaluation stack.
        /// </summary>
        private VMAdvanceResult ExecuteCall(CallFrame caller, int functionId)
        {
            if (Code.TryGetNativeFunction(functionId, out NativeFunction native))
            {
                if (native.MinArguments != native.MaxArguments)
                    return Fault(RuntimeError.InvalidInstruction(
                        $"Variadic native '{native.QualifiedName}' requires PUSH_FUNCTION + CALL_INDIRECT n."));
                return ExecuteNativeCall(caller, native, native.MinArguments, false);
            }
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
        /// Native functions are bounded, atomic instructions. Source operands remain GC roots
        /// while their handler executes; no native frame is pushed and no heap objects are
        /// silently kept alive after the single result replaces the call expression.
        /// </summary>
        private VMAdvanceResult ExecuteNativeCall(CallFrame frame, NativeFunction native,
            int argumentCount, bool indirect)
        {
            if (!native.Accepts(argumentCount))
                return Fault(RuntimeError.InvalidOperation(
                    $"Native '{native.QualifiedName}' expects {native.MinArguments}..{native.MaxArguments} " +
                    $"arguments, got {argumentCount}."));
            int consumed = argumentCount + (indirect ? 1 : 0);
            if (!evaluationStack.TryPeekArguments(frame.EvalStackBase, consumed, out DataValue[] operands))
                return Fault(RuntimeError.StackUnderflow());
            if (!evaluationStack.CanReplaceTopWithOne(frame.EvalStackBase, consumed))
                return Fault(RuntimeError.StackOverflow());
            var arguments = new DataValue[argumentCount];
            Array.Copy(operands, indirect ? 1 : 0, arguments, 0, argumentCount);
            foreach (DataValue value in arguments)
                if (value.dataType == DataTypeId.Uninitialized)
                    return Fault(RuntimeError.VariableUsedBeforeInitialization());
            NativeResult result;
            try
            {
                result = native.Invoke(new NativeCallContext(
                    Memory, arguments, text => Printed?.Invoke(text)));
            }
            catch (Exception ex)
            {
                // An unexpected exception in trusted C# is a VM integration defect,
                // never a script exception allowed to escape the simulation scheduler.
                return Fault(RuntimeError.InternalVmError(
                    $"Native '{native.QualifiedName}' raised {ex.GetType().Name}: {ex.Message}"));
            }
            if (!result.IsSuccess)
                return Fault(result.Error ?? RuntimeError.InternalVmError(
                    $"Native '{native.QualifiedName}' failed without an error."));
            if (!evaluationStack.TryReplaceTopWithOne(frame.EvalStackBase, consumed, result.Value))
                return Fault(RuntimeError.InternalVmError("Validated native stack replacement failed."));
            return AdvanceInstruction(frame);
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

        /// <summary>
        /// Enumerates every heap reference currently owned by this VM for garbage collection.
        /// OperationState is intentionally excluded by contract: continuation states must never own heap references.
        /// </summary>
        public IEnumerable<uint> EnumerateHeapReferences()
        {
            if (Status == VMExecutionStatus.Completed)
            {
                if (returnValue.TryGetHeapReference(out uint completedReference))
                    yield return completedReference;
                yield break;
            }

            if (Status != VMExecutionStatus.Running)
                yield break;

            foreach (DataValue value in evaluationStack.EnumerateValues())
            {
                if (value.TryGetHeapReference(out uint memoryReference))
                    yield return memoryReference;
            }

            foreach (CallFrame frame in callStack)
            {
                foreach (DataValue value in frame.Locals.EnumerateValues())
                {
                    if (value.TryGetHeapReference(out uint memoryReference))
                        yield return memoryReference;
                }
            }

            if (operationAuxPointer != 0)
                yield return operationAuxPointer;

            if (pendingOperation != null)
            {
                foreach (DataValue value in pendingOperation.Arguments)
                {
                    if (value.TryGetHeapReference(out uint memoryReference))
                        yield return memoryReference;
                }
            }

            if (activeOperationArguments != null)
            {
                foreach (DataValue value in activeOperationArguments)
                {
                    if (value.TryGetHeapReference(out uint memoryReference))
                        yield return memoryReference;
                }
            }

            if (activeCastValue.HasValue &&
                activeCastValue.Value.TryGetHeapReference(out uint activeCastReference))
            {
                yield return activeCastReference;
            }
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
        /// <summary>
        /// Stops an initializer that exhausted its logical execution budget, so
        /// unfinished frames/continuation roots do not remain reachable by the GC.
        /// </summary>
        internal void Abort(RuntimeError error)
        {
            if (Status == VMExecutionStatus.Running) Fault(error);
        }

        private VMAdvanceResult Fault(RuntimeError error)
        {
            pendingOperation = null;
            operationAuxPointer = 0;
            fault = error;
            Status = VMExecutionStatus.Faulted;
            return VMAdvanceResult.Faulted(error);
        }
    }
}
