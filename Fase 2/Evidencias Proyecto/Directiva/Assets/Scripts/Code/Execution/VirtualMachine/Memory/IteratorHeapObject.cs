using System;
using System.Collections.Generic;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Values;

namespace DSExecution.VirtualMachine
{
    /// <summary>
    /// A bounded-size heap cursor. It owns one reference to its iterable, which the
    /// normal mark-and-sweep GC follows. No references are hidden in continuation state.
    /// Mutable sources must not change while a cursor is being consumed.
    /// </summary>
    public sealed class IteratorHeapObject : HeapObject, IDirectivaIterator
    {
        private readonly DataTypeId sourceType;
        private readonly uint sourceReference;
        private readonly ulong version;
        private int index;
        private long nextRangeValue;
        private bool rangeExhausted;

        // Logical header + source reference + cursor + mutation snapshot.
        public override uint Size => 4;
        public DataTypeId SourceType => sourceType;
        public uint SourceReference => sourceReference;

        public IteratorHeapObject(DataTypeId sourceType, uint sourceReference, HeapObject source)
        {
            if (sourceReference == 0 || source == null)
                throw new ArgumentException("Iterator needs a valid heap-backed source.");
            this.sourceType = sourceType;
            this.sourceReference = sourceReference;
            switch (sourceType)
            {
                case DataTypeId.Tuple when source is TupleHeapObject:
                    break;
                case DataTypeId.List when source is ListHeapObject list:
                    version = list.Version;
                    break;
                case DataTypeId.Set when source is SetHeapObject set:
                    version = set.Version;
                    break;
                case DataTypeId.Dict when source is DictHeapObject dict:
                    version = dict.Version;
                    break;
                case DataTypeId.Range when source is RangeHeapObject range:
                    nextRangeValue = range.Start;
                    rangeExhausted = range.Step > 0
                        ? range.Start >= range.Stop : range.Start <= range.Stop;
                    break;
                default:
                    throw new ArgumentException("The source is not an iterable collection.", nameof(source));
            }
        }

        public override IEnumerable<uint> GetReferences()
        {
            yield return sourceReference;
        }

        /// <summary>
        /// Tests exhaustion without consuming a value, including mutation validation.
        /// This allows ITER_NEXT to finish on a full evaluation stack without mistakenly
        /// reporting StackOverflow, while also avoiding advancement if the stack is full.
        /// </summary>
        public bool TryHasNext(IMemContext memory, out bool hasValue, out RuntimeError error)
        {
            hasValue = false;
            error = null;
            if (!memory.TryGetHeap(sourceReference, out HeapObject source))
            {
                error = RuntimeError.InvalidMemoryAccess(sourceReference);
                return false;
            }
            switch (sourceType)
            {
                case DataTypeId.Tuple when source is TupleHeapObject tuple:
                    hasValue = index < tuple.Count;
                    break;
                case DataTypeId.List when source is ListHeapObject list:
                    if (list.Version != version) error = RuntimeError.IteratorModified(sourceType);
                    else hasValue = index < list.Count;
                    break;
                case DataTypeId.Set when source is SetHeapObject set:
                    if (set.Version != version) error = RuntimeError.IteratorModified(sourceType);
                    else hasValue = index < set.Count;
                    break;
                case DataTypeId.Dict when source is DictHeapObject dict:
                    if (dict.Version != version) error = RuntimeError.IteratorModified(sourceType);
                    else hasValue = index < dict.Count;
                    break;
                case DataTypeId.Range when source is RangeHeapObject:
                    hasValue = !rangeExhausted;
                    break;
                default:
                    error = RuntimeError.InvalidOperation("Iterator source has an unexpected heap object type.");
                    break;
            }
            return error == null;
        }

        /// <summary>Does not move the cursor on invalid references or version conflicts.</summary>
        public bool TryGetNext(IMemContext memory, out DataValue value, out RuntimeError error)
        {
            value = DataValue.None();
            error = null;
            if (!memory.TryGetHeap(sourceReference, out HeapObject source))
            {
                error = RuntimeError.InvalidMemoryAccess(sourceReference);
                return false;
            }

            switch (sourceType)
            {
                case DataTypeId.Tuple when source is TupleHeapObject tuple:
                    if (index == tuple.Count) return false;
                    value = tuple.GetElement(index++);
                    break;

                case DataTypeId.List when source is ListHeapObject list:
                    if (version != list.Version)
                    {
                        error = RuntimeError.IteratorModified(sourceType);
                        return false;
                    }
                    if (index == list.Count) return false;
                    value = list.GetElement(index++);
                    break;

                case DataTypeId.Set when source is SetHeapObject set:
                    if (version != set.Version)
                    {
                        error = RuntimeError.IteratorModified(sourceType);
                        return false;
                    }
                    if (index == set.Count) return false;
                    value = set.GetElement(index++);
                    break;

                case DataTypeId.Dict when source is DictHeapObject dict:
                    if (version != dict.Version)
                    {
                        error = RuntimeError.IteratorModified(sourceType);
                        return false;
                    }
                    if (index == dict.Count) return false;
                    value = dict.GetKey(index++);
                    break;

                case DataTypeId.Range when source is RangeHeapObject range:
                    if (rangeExhausted) return false;
                    value = DataValue.FromInt(nextRangeValue);
                    // A signed step may overflow after the last valid yielded element.
                    try
                    {
                        nextRangeValue = checked(nextRangeValue + range.Step);
                        rangeExhausted = range.Step > 0
                            ? nextRangeValue >= range.Stop : nextRangeValue <= range.Stop;
                    }
                    catch (OverflowException)
                    {
                        rangeExhausted = true;
                    }
                    break;

                default:
                    error = RuntimeError.InvalidOperation(
                        "Iterator source has an unexpected heap object type.");
                    return false;
            }

            if (value.dataType == DataTypeId.Uninitialized)
            {
                error = RuntimeError.VariableUsedBeforeInitialization();
                return false;
            }
            return true;
        }
    }
}
