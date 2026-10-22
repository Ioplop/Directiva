using System;
using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace DSExecution.DataTypes
{
    /// <summary>
    /// Deterministic mutable hash set: unique hashable members, collision-aware equality,
    /// atomic heap-accounted mutations and block-budgeted set algebra/relations.
    /// </summary>
    public sealed class DTSet : DataType, IOpSet, IOpSearch, IOpLength,
        IOpContainer, IOpReferenceEquality
    {
        private const int ItemsPerStep = 16;
        [DScriptGetter("length")]
        public OperationResult GetLengthMember(OperationCall call) => Length(call);

        [DScriptMethod("add", 1)]
        public OperationResult AddMember(OperationCall call) => Operate(OperationId.SetAdd, call);

        [DScriptMethod("discard", 1)]
        public OperationResult DiscardMember(OperationCall call) => Operate(OperationId.SetDiscard, call);

        [DScriptMethod("remove", 1)]
        public OperationResult RemoveMember(OperationCall call) => Operate(OperationId.SetRemove, call);

        [DScriptMethod("contains", 1)]
        public OperationResult ContainsMember(OperationCall call) => Operate(OperationId.Contains, call);

        // The public method name is the same as List.pop, but dispatches to SetPop.
        [DScriptMethod("pop", 0)]
        public OperationResult PopMember(OperationCall call) => Operate(OperationId.SetPop, call);

        public static DTSet Instance { get; } = new DTSet();
        private DTSet() { }

        internal static bool TryConstruct(ReadOnlySpan<DataValue> values, IMemContext memory,
            out SetHeapObject result, out RuntimeError error)
        {
            result = new SetHeapObject();
            error = null;
            foreach (DataValue value in values)
            {
                if (!TryHash(value, memory, out long hash, out error))
                    return false;
                if (!result.TryFind(value, hash, memory, out int found, out error))
                    return false;
                if (found < 0)
                    result.AddAuthorized(value, hash); // Not allocated yet: charge at final allocation.
            }
            return true;
        }

        internal static bool TryHash(DataValue value, IMemContext memory,
            out long hash, out RuntimeError error)
        {
            hash = 0;
            error = null;
            OperationResult answer = OperationDispatcher.Start(OperationId.Hash,
                new[] { value }, memory).Result;
            if (answer.Status == OperationStatus.Error)
            {
                error = answer.Error ?? RuntimeError.InternalVmError("Hash failed without an error.");
                return false;
            }
            if (answer.Status != OperationStatus.Success || answer.Value.dataType != DataTypeId.Int)
            {
                error = RuntimeError.InternalVmError("Hash must finish synchronously and return Int.");
                return false;
            }
            hash = unchecked((long)answer.Value.value);
            return true;
        }

        private static bool Resolve(IMemContext memory, DataValue value,
            out SetHeapObject set, out OperationResult failure)
        {
            set = null;
            failure = default;
            if (value.dataType != DataTypeId.Set)
            {
                failure = OperationResult.NotImplemented();
                return false;
            }
            if (!value.TryGetHeapReference(out uint reference))
            {
                failure = OperationResult.OpError(RuntimeError.InvalidOperation(
                    "Set contains an invalid heap reference."));
                return false;
            }
            if (!memory.TryGetHeap(reference, out HeapObject heapObject) ||
                !(heapObject is SetHeapObject actual))
            {
                failure = OperationResult.OpError(RuntimeError.InvalidMemoryAccess(reference));
                return false;
            }
            set = actual;
            return true;
        }

        private static OperationResult Fault(RuntimeError error) => OperationResult.OpError(error);

        public OperationResult SetAdd(OperationCall call)
        {
            if (call.Arguments.Length != 2 || call.Arguments[0].dataType != DataTypeId.Set)
                return OperationResult.NotImplemented();
            if (!Resolve(call.Memory, call.Arguments[0], out SetHeapObject set, out var failure))
                return failure;
            if (!TryHash(call.Arguments[1], call.Memory, out long hash, out RuntimeError error))
                return Fault(error);
            if (!set.TryFind(call.Arguments[1], hash, call.Memory, out int found, out error))
                return Fault(error);
            if (found >= 0)
                return OperationResult.Success(DataValue.None());
            if (set.Count == int.MaxValue ||
                !call.Memory.TryResizeHeap((uint)call.Arguments[0].value, set.Size + 1u))
                return Fault(RuntimeError.OutOfMemory());
            set.AddAuthorized(call.Arguments[1], hash);
            return OperationResult.Success(DataValue.None());
        }

        public OperationResult SetRemove(OperationCall call) => Remove(call, false);
        public OperationResult SetDiscard(OperationCall call) => Remove(call, true);

        private static OperationResult Remove(OperationCall call, bool discard)
        {
            if (call.Arguments.Length != 2 || call.Arguments[0].dataType != DataTypeId.Set)
                return OperationResult.NotImplemented();
            if (!Resolve(call.Memory, call.Arguments[0], out SetHeapObject set, out var failure))
                return failure;
            if (!TryHash(call.Arguments[1], call.Memory, out long hash, out RuntimeError error))
                return Fault(error);
            if (!set.TryFind(call.Arguments[1], hash, call.Memory, out int index, out error))
                return Fault(error);
            if (index < 0)
                return discard ? OperationResult.Success(DataValue.None()) :
                    Fault(RuntimeError.InvalidOperation("Set element not found."));
            if (!call.Memory.TryResizeHeap((uint)call.Arguments[0].value, set.Size - 1u))
                return Fault(RuntimeError.InternalVmError("Set shrink failed."));
            set.RemoveAuthorized(index);
            return OperationResult.Success(DataValue.None());
        }

        /// <summary>Remove the last stored member deterministically; unlike stack POP, returns it.</summary>
        public OperationResult SetPop(OperationCall call)
        {
            if (call.Arguments.Length != 1 || call.Arguments[0].dataType != DataTypeId.Set)
                return OperationResult.NotImplemented();
            if (!Resolve(call.Memory, call.Arguments[0], out SetHeapObject set, out var failure))
                return failure;
            if (set.Count == 0)
                return Fault(RuntimeError.InvalidOperation("Cannot pop from an empty Set."));
            if (!call.Memory.TryResizeHeap((uint)call.Arguments[0].value, set.Size - 1u))
                return Fault(RuntimeError.InternalVmError("Set shrink failed."));
            return OperationResult.Success(set.RemoveAuthorized(set.Count - 1));
        }

        public OperationResult Contains(OperationCall call)
        {
            if (call.Arguments.Length != 2 || call.Arguments[0].dataType != DataTypeId.Set)
                return OperationResult.NotImplemented();
            if (!Resolve(call.Memory, call.Arguments[0], out SetHeapObject set, out var failure))
                return failure;
            if (!TryHash(call.Arguments[1], call.Memory, out long hash, out RuntimeError error))
                return Fault(error);
            if (!set.TryFind(call.Arguments[1], hash, call.Memory, out int found, out error))
                return Fault(error);
            return OperationResult.Success(DataValue.FromBool(found >= 0));
        }

        // IndexOf has no meaning for an unordered set.
        public OperationResult IndexOf(OperationCall call) => OperationResult.NotImplemented();

        public OperationResult Length(OperationCall call)
        {
            if (call.Arguments.Length != 1 || call.Arguments[0].dataType != DataTypeId.Set)
                return OperationResult.NotImplemented();
            return Resolve(call.Memory, call.Arguments[0], out SetHeapObject set, out var failure)
                ? OperationResult.Success(DataValue.FromInt(set.Count)) : failure;
        }

        public override OperationResult IsTruthy(OperationCall call)
        {
            if (call.Arguments.Length != 1 || call.Arguments[0].dataType != DataTypeId.Set)
                return OperationResult.NotImplemented();
            return Resolve(call.Memory, call.Arguments[0], out SetHeapObject set, out var failure)
                ? OperationResult.Success(DataValue.FromBool(set.Count != 0)) : failure;
        }

        public OperationResult ReferenceEquals(OperationCall call)
        {
            if (call.Arguments.Length != 2)
                return OperationResult.NotImplemented();
            return OperationResult.Success(DataValue.FromBool(
                call.Arguments[0].dataType == DataTypeId.Set &&
                call.Arguments[1].dataType == DataTypeId.Set &&
                call.Arguments[0].value == call.Arguments[1].value));
        }

        /// <summary>
        /// Synchronous structural equality, like List/Tuple equality. Keeping equality
        /// synchronous also allows these sets to be compared inside nested sequences;
        /// the existing SequenceEquality helper cannot nest continued operations.
        /// </summary>
        public override OperationResult ValueEquals(OperationCall call)
        {
            if (call.Arguments.Length != 2 || call.Arguments[0].dataType != DataTypeId.Set ||
                call.Arguments[1].dataType != DataTypeId.Set)
                return OperationResult.NotImplemented();
            if (!Resolve(call.Memory, call.Arguments[0], out SetHeapObject left, out var failure) ||
                !Resolve(call.Memory, call.Arguments[1], out SetHeapObject right, out failure))
                return failure;
            if (left.Count != right.Count)
                return OperationResult.Success(DataValue.FromBool(false));
            if (ReferenceEquals(left, right))
                return OperationResult.Success(DataValue.FromBool(true));
            for (int i = 0; i < left.Count; i++)
            {
                if (!right.TryFind(left.GetElement(i), left.GetElementHash(i), call.Memory,
                        out int match, out RuntimeError error))
                    return Fault(error);
                if (match < 0)
                    return OperationResult.Success(DataValue.FromBool(false));
            }
            return OperationResult.Success(DataValue.FromBool(true));
        }
        public OperationResult SetIsSubset(OperationCall call) => Scan(call, OperationId.SetIsSubset);
        public OperationResult SetIsSuperset(OperationCall call) => Scan(call, OperationId.SetIsSuperset);
        public OperationResult SetIsDisjoint(OperationCall call) => Scan(call, OperationId.SetIsDisjoint);

        private static OperationResult Scan(OperationCall call, OperationId kind)
        {
            if (call.Arguments.Length != 2 || call.Arguments[0].dataType != DataTypeId.Set ||
                call.Arguments[1].dataType != DataTypeId.Set)
                return OperationResult.NotImplemented();
            if (!Resolve(call.Memory, call.Arguments[0], out SetHeapObject left, out var failure) ||
                !Resolve(call.Memory, call.Arguments[1], out SetHeapObject right, out failure))
                return failure;
            if (kind == OperationId.ValueEquals && left.Count != right.Count ||
                kind == OperationId.SetIsSubset && left.Count > right.Count ||
                kind == OperationId.SetIsSuperset && left.Count < right.Count)
                return OperationResult.Success(DataValue.FromBool(false));
            if (ReferenceEquals(left, right))
                return OperationResult.Success(DataValue.FromBool(kind != OperationId.SetIsDisjoint || left.Count == 0));
            SetOperationState state = call.State as SetOperationState;
            if (state == null)
                state = new SetOperationState(kind, left.Version, right.Version, left.Count, right.Count);
            if (state.Kind != kind || state.LeftVersion != left.Version ||
                state.RightVersion != right.Version || state.LeftCount != left.Count ||
                state.RightCount != right.Count)
                return Fault(RuntimeError.InvalidOperation("Set changed during continued relation."));

            SetHeapObject source = kind == OperationId.SetIsSuperset ? right : left;
            SetHeapObject target = kind == OperationId.SetIsSuperset ? left : right;
            for (int inspected = 0; inspected < ItemsPerStep && state.NextIndex < source.Count;
                inspected++, state.NextIndex++)
            {
                DataValue value = source.GetElement(state.NextIndex);
                long hash = source.GetElementHash(state.NextIndex);
                if (!target.TryFind(value, hash, call.Memory, out int located, out RuntimeError error))
                    return Fault(error);
                if (kind == OperationId.SetIsDisjoint ? located >= 0 : located < 0)
                    return OperationResult.Success(DataValue.FromBool(false));
            }
            if (state.NextIndex < source.Count)
                return OperationResult.Continued(state);
            return OperationResult.Success(DataValue.FromBool(true));
        }

        public OperationResult Copy(OperationCall call) => Build(call, OperationId.Copy);
        public OperationResult SetUnion(OperationCall call) => Build(call, OperationId.SetUnion);
        public OperationResult SetIntersection(OperationCall call) => Build(call, OperationId.SetIntersection);
        public OperationResult SetDifference(OperationCall call) => Build(call, OperationId.SetDifference);
        public OperationResult SetSymmetricDifference(OperationCall call)
            => Build(call, OperationId.SetSymmetricDifference);
        public OperationResult SetUpdate(OperationCall call) => Build(call, OperationId.SetUpdate);

        private static OperationResult Build(OperationCall call, OperationId kind)
        {
            int arity = kind == OperationId.Copy ? 1 : 2;
            if (call.Arguments.Length != arity || call.Arguments[0].dataType != DataTypeId.Set ||
                (arity == 2 && call.Arguments[1].dataType != DataTypeId.Set))
                return OperationResult.NotImplemented();
            if (!Resolve(call.Memory, call.Arguments[0], out SetHeapObject left, out var failure))
                return failure;
            SetHeapObject right = null;
            if (arity == 2 && !Resolve(call.Memory, call.Arguments[1], out right, out failure))
                return failure;
            if (kind == OperationId.SetUpdate && ReferenceEquals(left, right))
                return OperationResult.Success(DataValue.None());

            if (call.State == null)
            {
                if (!call.Memory.TryAllocHeap(new SetHeapObject(), out uint auxReference))
                    return Fault(RuntimeError.OutOfMemory());
                var start = new SetOperationState(kind, left.Version, right?.Version ?? 0,
                    left.Count, right?.Count ?? 0);
                return OperationResult.Continued(start, auxReference);
            }

            if (!(call.State is SetOperationState state) || state.Kind != kind ||
                call.AuxiliaryReference == 0)
                return Fault(RuntimeError.InternalVmError("Invalid continued Set operation state."));
            if (state.LeftVersion != left.Version || state.LeftCount != left.Count ||
                (right != null && (state.RightVersion != right.Version || state.RightCount != right.Count)))
                return Fault(RuntimeError.InvalidOperation("Set changed during continued operation " + kind + "."));
            if (!call.Memory.TryGetHeap(call.AuxiliaryReference, out HeapObject rawAux) ||
                !(rawAux is SetHeapObject aux))
                return Fault(RuntimeError.InternalVmError("Lost auxiliary set during " + kind + "."));

            bool needsRightPass = kind == OperationId.SetUnion ||
                kind == OperationId.SetUpdate || kind == OperationId.SetSymmetricDifference;
            long totalLong = (long)state.LeftCount + (needsRightPass ? state.RightCount : 0L);
            if (totalLong > int.MaxValue)
                return Fault(RuntimeError.OutOfMemory());
            int total = (int)totalLong;
            // Each continuation inspects <=16 inputs. The aux remains a VM GC root.
            for (int inspected = 0; inspected < ItemsPerStep && state.NextIndex < total;
                inspected++, state.NextIndex++)
            {
                bool fromRight = state.NextIndex >= state.LeftCount;
                int index = fromRight ? state.NextIndex - state.LeftCount : state.NextIndex;
                SetHeapObject source = fromRight ? right : left;
                DataValue value = source.GetElement(index);
                long hash = source.GetElementHash(index);
                bool include = true;
                if (kind == OperationId.SetIntersection || kind == OperationId.SetDifference ||
                    kind == OperationId.SetSymmetricDifference)
                {
                    SetHeapObject other = fromRight ? left : right;
                    if (!other.TryFind(value, hash, call.Memory, out int matching, out RuntimeError error))
                        return Fault(error);
                    include = kind == OperationId.SetIntersection ? matching >= 0 : matching < 0;
                }
                if (!include) continue;
                if (!aux.TryFind(value, hash, call.Memory, out int present, out RuntimeError equalityError))
                    return Fault(equalityError);
                if (present >= 0) continue;
                if (aux.Count == int.MaxValue ||
                    !call.Memory.TryResizeHeap(call.AuxiliaryReference, aux.Size + 1u))
                    return Fault(RuntimeError.OutOfMemory());
                aux.AddAuthorized(value, hash);
            }
            if (state.NextIndex < total)
                return OperationResult.Continued(state, call.AuxiliaryReference);

            if (kind != OperationId.SetUpdate)
                return OperationResult.Success(DataValue.FromSetReference(call.AuxiliaryReference));

            if (!call.Memory.TrySwapHeapContents((uint)call.Arguments[0].value, call.AuxiliaryReference))
                return Fault(RuntimeError.InternalVmError("Could not commit SetUpdate."));
            if (!call.Memory.FreeHeap(call.AuxiliaryReference))
                return Fault(RuntimeError.InternalVmError("Could not release SetUpdate buffer."));
            return OperationResult.Success(DataValue.None());
        }

        public override CastResult CastTo(DataTypeId target, DataValue value, VMMemoryContext memory)
        {
            if (target != DataTypeId.String || value.dataType != DataTypeId.Set)
                return base.CastTo(target, value, memory);
            if (!SequenceFormatter.TryFormat(value, memory, out string representation,
                    out RuntimeError error))
                return CastResult.Failure(error);
            return DTString.CreateValue(representation, memory);
        }

        // Set is not an indexed container, so only Copy is applicable from IOpContainer.
        public OperationResult Append(OperationCall call) => OperationResult.NotImplemented();
        public OperationResult Insert(OperationCall call) => OperationResult.NotImplemented();
        public OperationResult Delete(OperationCall call) => OperationResult.NotImplemented();
        public OperationResult Pop(OperationCall call) => OperationResult.NotImplemented();
        public OperationResult Get(OperationCall call) => OperationResult.NotImplemented();
        public OperationResult Set(OperationCall call) => OperationResult.NotImplemented();
    }
}
