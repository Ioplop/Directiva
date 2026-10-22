using System;
using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace DSExecution.DataTypes
{
    /// <summary>
    /// Mutable insertion-ordered hash dictionary. Only keys must be hashable; values
    /// may reference arbitrary mutable or cyclic objects. Continued builds stage in
    /// a VM-rooted auxiliary heap object and publish DictUpdate in one content swap.
    /// </summary>
    public sealed class DTDict : DataType, IOpContainer, IOpSearch, IOpLength,
        IOpReferenceEquality, IOpDictionary
    {
        private const int ItemsPerStep = 16;
        [DScriptGetter("length")]
        public OperationResult GetLengthMember(OperationCall call) => Length(call);

        [DScriptMethod("keys", 0)]
        public OperationResult KeysMember(OperationCall call) => Operate(OperationId.DictKeys, call);

        [DScriptMethod("values", 0)]
        public OperationResult ValuesMember(OperationCall call) => Operate(OperationId.DictValues, call);

        [DScriptMethod("items", 0)]
        public OperationResult ItemsMember(OperationCall call) => Operate(OperationId.DictItems, call);

        [DScriptMethod("contains", 1)]
        public OperationResult ContainsMember(OperationCall call) => Operate(OperationId.Contains, call);

        // Dictionary.pop(key) removes a key and returns its value using native Pop.
        [DScriptMethod("pop", 1)]
        public OperationResult PopMember(OperationCall call) => Operate(OperationId.Pop, call);

        public static DTDict Instance { get; } = new DTDict();
        private DTDict() { }

        internal static bool TryConstruct(ReadOnlySpan<DataValue> stackValues, IMemContext memory,
            out DictHeapObject result, out RuntimeError error)
        {
            result = new DictHeapObject();
            error = null;
            if ((stackValues.Length & 1) != 0)
            {
                error = RuntimeError.InternalVmError("DICT construction requires complete pairs.");
                return false;
            }
            for (int i = 0; i < stackValues.Length; i += 2)
            {
                DataValue key = stackValues[i];
                DataValue value = stackValues[i + 1];
                if (value.dataType == DataTypeId.Uninitialized)
                {
                    error = RuntimeError.VariableUsedBeforeInitialization();
                    return false;
                }
                if (!DTSet.TryHash(key, memory, out long hash, out error) ||
                    !result.TryFind(key, hash, memory, out int found, out error))
                    return false;
                if (found >= 0) result.SetValueAuthorized(found, value);
                else result.AddAuthorized(key, value, hash);
            }
            return true;
        }

        private static bool Resolve(IMemContext memory, DataValue value,
            out DictHeapObject dict, out OperationResult failure)
        {
            dict = null;
            failure = default;
            if (value.dataType != DataTypeId.Dict)
            {
                failure = OperationResult.NotImplemented();
                return false;
            }
            if (!value.TryGetHeapReference(out uint id))
            {
                failure = Fault(RuntimeError.InvalidOperation("Invalid Dictionary reference."));
                return false;
            }
            if (!memory.TryGetHeap(id, out HeapObject obj) || !(obj is DictHeapObject found))
            {
                failure = Fault(RuntimeError.InvalidMemoryAccess(id));
                return false;
            }
            dict = found;
            return true;
        }

        private static OperationResult Fault(RuntimeError error) => OperationResult.OpError(error);
        private static OperationResult MissingKey() => Fault(RuntimeError.InvalidOperation("Dictionary key not found."));

        private static bool Find(DictHeapObject dict, DataValue key, IMemContext memory,
            out int index, out OperationResult failure)
        {
            index = -1;
            failure = default;
            if (!DTSet.TryHash(key, memory, out long hash, out RuntimeError error) ||
                !dict.TryFind(key, hash, memory, out index, out error))
            {
                failure = Fault(error);
                return false;
            }
            return true;
        }

        public OperationResult Get(OperationCall call)
        {
            if (call.Arguments.Length != 2 || call.Arguments[0].dataType != DataTypeId.Dict)
                return OperationResult.NotImplemented();
            if (!Resolve(call.Memory, call.Arguments[0], out var dict, out var failure)) return failure;
            if (!Find(dict, call.Arguments[1], call.Memory, out int index, out failure)) return failure;
            return index < 0 ? MissingKey() : OperationResult.Success(dict.GetValue(index));
        }

        public OperationResult DictGetDefault(OperationCall call)
        {
            if (call.Arguments.Length != 3 || call.Arguments[0].dataType != DataTypeId.Dict)
                return OperationResult.NotImplemented();
            if (call.Arguments[2].dataType == DataTypeId.Uninitialized)
                return Fault(RuntimeError.VariableUsedBeforeInitialization());
            if (!Resolve(call.Memory, call.Arguments[0], out var dict, out var failure)) return failure;
            if (!Find(dict, call.Arguments[1], call.Memory, out int index, out failure)) return failure;
            return OperationResult.Success(index < 0 ? call.Arguments[2] : dict.GetValue(index));
        }

        public OperationResult Set(OperationCall call)
        {
            if (call.Arguments.Length != 3 || call.Arguments[0].dataType != DataTypeId.Dict)
                return OperationResult.NotImplemented();
            if (call.Arguments[2].dataType == DataTypeId.Uninitialized)
                return Fault(RuntimeError.VariableUsedBeforeInitialization());
            if (!Resolve(call.Memory, call.Arguments[0], out var dict, out var failure)) return failure;
            if (!DTSet.TryHash(call.Arguments[1], call.Memory, out long hash, out RuntimeError error) ||
                !dict.TryFind(call.Arguments[1], hash, call.Memory, out int index, out error))
                return Fault(error);
            if (index >= 0)
            {
                dict.SetValueAuthorized(index, call.Arguments[2]);
                return OperationResult.Success(DataValue.None());
            }
            if (dict.Count == int.MaxValue ||
                !call.Memory.TryResizeHeap((uint)call.Arguments[0].value, dict.Size + 2u))
                return Fault(RuntimeError.OutOfMemory());
            dict.AddAuthorized(call.Arguments[1], call.Arguments[2], hash);
            return OperationResult.Success(DataValue.None());
        }

        public OperationResult Delete(OperationCall call) => Remove(call, false);
        public OperationResult Pop(OperationCall call) => Remove(call, true);

        private static OperationResult Remove(OperationCall call, bool returnValue)
        {
            if (call.Arguments.Length != 2 || call.Arguments[0].dataType != DataTypeId.Dict)
                return OperationResult.NotImplemented();
            if (!Resolve(call.Memory, call.Arguments[0], out var dict, out var failure)) return failure;
            if (!Find(dict, call.Arguments[1], call.Memory, out int index, out failure)) return failure;
            if (index < 0) return MissingKey();
            if (!call.Memory.TryResizeHeap((uint)call.Arguments[0].value, dict.Size - 2u))
                return Fault(RuntimeError.InternalVmError("Dictionary shrink failed."));
            DataValue removed = dict.RemoveAuthorized(index);
            return OperationResult.Success(returnValue ? removed : DataValue.None());
        }

        public OperationResult DictClear(OperationCall call)
        {
            if (call.Arguments.Length != 1 || call.Arguments[0].dataType != DataTypeId.Dict)
                return OperationResult.NotImplemented();
            if (!Resolve(call.Memory, call.Arguments[0], out var dict, out var failure)) return failure;
            if (dict.Count == 0) return OperationResult.Success(DataValue.None());
            if (!call.Memory.TryResizeHeap((uint)call.Arguments[0].value, 1))
                return Fault(RuntimeError.InternalVmError("Dictionary clear resize failed."));
            dict.ClearAuthorized();
            return OperationResult.Success(DataValue.None());
        }

        public OperationResult Contains(OperationCall call)
        {
            if (call.Arguments.Length != 2 || call.Arguments[0].dataType != DataTypeId.Dict)
                return OperationResult.NotImplemented();
            if (!Resolve(call.Memory, call.Arguments[0], out var dict, out var failure)) return failure;
            if (!Find(dict, call.Arguments[1], call.Memory, out int index, out failure)) return failure;
            return OperationResult.Success(DataValue.FromBool(index >= 0));
        }

        public OperationResult IndexOf(OperationCall call) => OperationResult.NotImplemented();

        public OperationResult Length(OperationCall call)
        {
            if (call.Arguments.Length != 1 || call.Arguments[0].dataType != DataTypeId.Dict)
                return OperationResult.NotImplemented();
            return Resolve(call.Memory, call.Arguments[0], out var dict, out var failure)
                ? OperationResult.Success(DataValue.FromInt(dict.Count)) : failure;
        }

        public override OperationResult IsTruthy(OperationCall call)
        {
            if (call.Arguments.Length != 1 || call.Arguments[0].dataType != DataTypeId.Dict)
                return OperationResult.NotImplemented();
            return Resolve(call.Memory, call.Arguments[0], out var dict, out var failure)
                ? OperationResult.Success(DataValue.FromBool(dict.Count != 0)) : failure;
        }

        public OperationResult ReferenceEquals(OperationCall call)
        {
            if (call.Arguments.Length != 2) return OperationResult.NotImplemented();
            return OperationResult.Success(DataValue.FromBool(
                call.Arguments[0].dataType == DataTypeId.Dict &&
                call.Arguments[1].dataType == DataTypeId.Dict &&
                call.Arguments[0].value == call.Arguments[1].value));
        }

        public override OperationResult ValueEquals(OperationCall call)
        {
            if (call.Arguments.Length != 2 || call.Arguments[0].dataType != DataTypeId.Dict ||
                call.Arguments[1].dataType != DataTypeId.Dict)
                return OperationResult.NotImplemented();
            return SequenceEquality.Compare(call.Memory, call.Arguments[0], call.Arguments[1]);
        }

        public OperationResult Copy(OperationCall call) => Build(call, OperationId.Copy);
        public OperationResult DictUpdate(OperationCall call) => Build(call, OperationId.DictUpdate);
        public OperationResult DictKeys(OperationCall call) => Build(call, OperationId.DictKeys);
        public OperationResult DictValues(OperationCall call) => Build(call, OperationId.DictValues);
        public OperationResult DictItems(OperationCall call) => Build(call, OperationId.DictItems);

        private static OperationResult Build(OperationCall call, OperationId kind)
        {
            bool update = kind == OperationId.DictUpdate;
            int arity = update ? 2 : 1;
            if (call.Arguments.Length != arity || call.Arguments[0].dataType != DataTypeId.Dict ||
                (update && call.Arguments[1].dataType != DataTypeId.Dict))
                return OperationResult.NotImplemented();
            if (!Resolve(call.Memory, call.Arguments[0], out var left, out var failure)) return failure;
            DictHeapObject right = null;
            if (update && !Resolve(call.Memory, call.Arguments[1], out right, out failure)) return failure;
            if (update && ReferenceEquals(left, right)) return OperationResult.Success(DataValue.None());
            bool listResult = kind == OperationId.DictKeys || kind == OperationId.DictValues ||
                kind == OperationId.DictItems;
            if (call.State == null)
            {
                HeapObject buffer = listResult
                    ? new ListHeapObject(Array.Empty<DataValue>()) : new DictHeapObject();
                if (!call.Memory.TryAllocHeap(buffer, out uint auxiliary))
                    return Fault(RuntimeError.OutOfMemory());
                var first = new DictOperationState(kind, left.Version, right?.Version ?? 0,
                    left.Count, right?.Count ?? 0);
                return OperationResult.Continued(first, auxiliary);
            }
            if (!(call.State is DictOperationState state) || state.Kind != kind ||
                call.AuxiliaryReference == 0)
                return Fault(RuntimeError.InternalVmError("Invalid dictionary continuation state."));
            if (left.Version != state.LeftVersion || left.Count != state.LeftCount ||
                (right != null && (right.Version != state.RightVersion || right.Count != state.RightCount)))
                return Fault(RuntimeError.InvalidOperation("Dictionary modified during continued " + kind + "."));
            if (!call.Memory.TryGetHeap(call.AuxiliaryReference, out HeapObject bufferObject))
                return Fault(RuntimeError.InternalVmError("Lost dictionary auxiliary buffer."));
            var auxDict = bufferObject as DictHeapObject;
            var auxList = bufferObject as ListHeapObject;
            if (listResult ? auxList == null || auxList.Count != state.NextIndex : auxDict == null)
                return Fault(RuntimeError.InternalVmError("Dictionary auxiliary buffer type or size mismatch."));
            long totalLong = (long)state.LeftCount + (update ? state.RightCount : 0L);
            if (totalLong > int.MaxValue) return Fault(RuntimeError.OutOfMemory());
            int total = (int)totalLong;

            for (int n = 0; n < ItemsPerStep && state.NextIndex < total; n++, state.NextIndex++)
            {
                bool fromRight = state.NextIndex >= state.LeftCount;
                int index = fromRight ? state.NextIndex - state.LeftCount : state.NextIndex;
                DictHeapObject source = fromRight ? right : left;
                DataValue key = source.GetKey(index);
                DataValue value = source.GetValue(index);
                if (listResult)
                {
                    if (!call.Memory.TryResizeHeap(call.AuxiliaryReference, auxList.Size + 1u))
                        return Fault(RuntimeError.OutOfMemory());
                    DataValue item;
                    if (kind == OperationId.DictItems)
                    {
                        // Reserve list capacity before allocating a tuple: a lazy GC may
                        // occur while creating it, but the still-pending dictionary roots
                        // both key and value, and the VM roots the auxiliary list.
                        if (!call.Memory.TryAllocHeap(new TupleHeapObject(new[] { key, value }),
                                out uint tupleRef))
                            return Fault(RuntimeError.OutOfMemory());
                        item = DataValue.FromTupleReference(tupleRef);
                    }
                    else item = kind == OperationId.DictKeys ? key : value;
                    auxList.AppendAuthorized(item);
                    continue;
                }
                long hash = source.GetKeyHash(index);
                if (!auxDict.TryFind(key, hash, call.Memory, out int found, out RuntimeError error))
                    return Fault(error);
                if (found >= 0)
                {
                    auxDict.SetValueAuthorized(found, value);
                    continue;
                }
                if (auxDict.Count == int.MaxValue ||
                    !call.Memory.TryResizeHeap(call.AuxiliaryReference, auxDict.Size + 2u))
                    return Fault(RuntimeError.OutOfMemory());
                auxDict.AddAuthorized(key, value, hash);
            }
            if (state.NextIndex < total)
                return OperationResult.Continued(state, call.AuxiliaryReference);
            if (listResult)
                return OperationResult.Success(DataValue.FromListReference(call.AuxiliaryReference));
            if (!update)
                return OperationResult.Success(DataValue.FromDictReference(call.AuxiliaryReference));
            if (!call.Memory.TrySwapHeapContents((uint)call.Arguments[0].value, call.AuxiliaryReference))
                return Fault(RuntimeError.InternalVmError("Cannot commit DictUpdate."));
            if (!call.Memory.FreeHeap(call.AuxiliaryReference))
                return Fault(RuntimeError.InternalVmError("Cannot release DictUpdate buffer."));
            return OperationResult.Success(DataValue.None());
        }

        public override CastResult CastTo(DataTypeId target, DataValue value, VMMemoryContext memory)
        {
            if (target != DataTypeId.String || value.dataType != DataTypeId.Dict)
                return base.CastTo(target, value, memory);
            if (!SequenceFormatter.TryFormat(value, memory, out string text, out RuntimeError error))
                return CastResult.Failure(error);
            return DTString.CreateValue(text, memory);
        }

        public OperationResult Append(OperationCall call) => OperationResult.NotImplemented();
        public OperationResult Insert(OperationCall call) => OperationResult.NotImplemented();
    }
}
