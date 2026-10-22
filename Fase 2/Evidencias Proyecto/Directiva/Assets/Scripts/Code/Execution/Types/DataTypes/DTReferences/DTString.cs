using System;
using System.Globalization;
using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace DSExecution.DataTypes
{
    /// <summary>
    /// Immutable heap-backed Directiva string. Value equality is ordinal and Add concatenates
    /// into a newly allocated StringHeapObject.
    /// </summary>
    public sealed class DTString : DataType, IOpArithmetic, IOpReferenceEquality, IOpHash, IOpLength
    {
        [DScriptGetter("length")]
        public OperationResult GetLengthMember(OperationCall call) => Length(call);

        public static DTString Instance { get; } = new DTString();

        private DTString()
        {
        }

        /// <summary>
        /// Allocates an immutable Directiva string in the shared heap.
        /// Used by casts whose source type decides how it is represented as text.
        /// </summary>
        internal static CastResult CreateValue(string value, VMMemoryContext memory)
        {
            if (memory == null)
                throw new ArgumentNullException(nameof(memory));

            var heapObject = new StringHeapObject(value);
            if (!memory.TryAllocHeap(heapObject, out uint memoryReference))
                return CastResult.Failure(RuntimeError.OutOfMemory());

            return CastResult.Success(
                DataValue.FromStringReference(memoryReference)
            );
        }

        private static bool TryResolve(
            OperationCall opCall,
            DataValue value,
            out StringHeapObject stringObject,
            out OperationResult errorResult)
        {
            stringObject = null;
            errorResult = default;

            if (value.dataType != DataTypeId.String)
                return false;

            if (!value.TryGetHeapReference(out uint memoryReference))
            {
                errorResult = OperationResult.OpError(
                    RuntimeError.InvalidOperation("String contains an invalid heap reference.")
                );
                return false;
            }
            if (!opCall.Memory.TryGetHeap(memoryReference, out HeapObject heapObject) ||
                !(heapObject is StringHeapObject resolved))
            {
                errorResult = OperationResult.OpError(
                    RuntimeError.InvalidMemoryAccess(memoryReference)
                );
                return false;
            }

            stringObject = resolved;
            return true;
        }

        public OperationResult Add(OperationCall opCall)
        {
            if (opCall.Arguments.Length != 2 ||
                opCall.Arguments[0].dataType != DataTypeId.String ||
                opCall.Arguments[1].dataType != DataTypeId.String)
            {
                return OperationResult.NotImplemented();
            }

            if (!TryResolve(opCall, opCall.Arguments[0], out var left, out var leftError))
                return leftError.Status == OperationStatus.Error ? leftError : OperationResult.NotImplemented();

            if (!TryResolve(opCall, opCall.Arguments[1], out var right, out var rightError))
                return rightError.Status == OperationStatus.Error ? rightError : OperationResult.NotImplemented();

            string resultValue = string.Concat(left.Value, right.Value);
            var resultObject = new StringHeapObject(resultValue);

            if (!opCall.Memory.TryAllocHeap(resultObject, out uint resultReference))
                return OperationResult.OpError(RuntimeError.OutOfMemory());

            return OperationResult.Success(
                DataValue.FromStringReference(resultReference)
            );
        }

        /// <summary>
        /// Stable FNV-1a over UTF-16 code units (in fixed little-endian byte order).
        /// The first successful hash is cached on the immutable heap object; later calls are O(1).
        /// This matches ordinal string equality without relying on randomized .NET hashes.
        /// </summary>
        public OperationResult Hash(OperationCall opCall)
        {
            if (opCall.Arguments.Length != 1 ||
                opCall.Arguments[0].dataType != DataTypeId.String)
                return OperationResult.NotImplemented();

            if (!TryResolve(opCall, opCall.Arguments[0], out var text, out var error))
                return error.Status == OperationStatus.Error ? error : OperationResult.NotImplemented();

            if (text.TryGetCachedHash(out long cachedHash))
                return OperationResult.Success(DataValue.FromInt(cachedHash));

            unchecked
            {
                const ulong offset = 14695981039346656037UL;
                const ulong prime = 1099511628211UL;
                ulong hash = offset;
                foreach (char c in text.Value)
                {
                    hash ^= (byte)c;
                    hash *= prime;
                    hash ^= (byte)(c >> 8);
                    hash *= prime;
                }

                long result = (long)hash;
                text.CacheHash(result);
                return OperationResult.Success(DataValue.FromInt(result));
            }
        }

        /// <summary>
        /// Counts Unicode code points rather than UTF-16 storage units.
        /// The logical heap Size deliberately continues to use UTF-16 units.
        /// </summary>
        public OperationResult Length(OperationCall opCall)
        {
            if (opCall.Arguments.Length != 1 ||
                opCall.Arguments[0].dataType != DataTypeId.String)
                return OperationResult.NotImplemented();

            if (!TryResolve(opCall, opCall.Arguments[0], out var text, out var error))
                return error.Status == OperationStatus.Error ? error : OperationResult.NotImplemented();

            string value = text.Value;
            int length = 0;
            for (int i = 0; i < value.Length; i++)
            {
                // A valid surrogate pair is one Unicode code point.
                if (char.IsHighSurrogate(value[i]) &&
                    i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                    i++;
                length++;
            }

            return OperationResult.Success(DataValue.FromInt(length));
        }

        /// <summary>String emptiness has the same truthiness as other containers.</summary>
        public override OperationResult IsTruthy(OperationCall call)
        {
            if (call.Arguments.Length != 1)
                return OperationResult.NotImplemented();
            if (!TryResolve(call, call.Arguments[0], out var text, out var error))
                return error.Status == OperationStatus.Error ? error : OperationResult.NotImplemented();
            return OperationResult.Success(DataValue.FromBool(text.Value.Length != 0));
        }

        /// <summary>Culture-independent conversions; malformed text is a value error, not a missing cast.</summary>
        public override CastResult CastTo(DataTypeId targetType, DataValue value, VMMemoryContext memory)
        {
            if (targetType != DataTypeId.Int && targetType != DataTypeId.Decimal)
                return base.CastTo(targetType, value, memory);
            if (!value.TryGetHeapReference(out uint reference) ||
                !memory.TryGetHeap(reference, out HeapObject obj) || obj is not StringHeapObject text)
                return CastResult.Failure(RuntimeError.InvalidMemoryAccess((uint)value.value));
            string content = text.Value.Trim();
            if (targetType == DataTypeId.Int)
            {
                if (long.TryParse(content, NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out long integer))
                    return CastResult.Success(DataValue.FromInt(integer));
                return CastResult.Failure(RuntimeError.InvalidCastValue(DataTypeId.String,
                    DataTypeId.Int, "Expected a signed 64-bit integer."));
            }
            if (decimal.TryParse(content, NumberStyles.Float, CultureInfo.InvariantCulture,
                out decimal number))
            {
                // Compare before multiplying to avoid overflowing .NET decimal itself.
                if (number < (decimal)long.MinValue / DTDecimal.Scale ||
                    number > (decimal)long.MaxValue / DTDecimal.Scale)
                    return CastResult.Failure(RuntimeError.Overflow());
                decimal scaled = decimal.Truncate(number * DTDecimal.Scale);
                if (scaled >= long.MinValue && scaled <= long.MaxValue)
                    return CastResult.Success(DataValue.FromDecimalRaw((long)scaled));
                return CastResult.Failure(RuntimeError.Overflow());
            }
            return CastResult.Failure(RuntimeError.InvalidCastValue(DataTypeId.String,
                DataTypeId.Decimal, "Expected a finite decimal number."));
        }

        public OperationResult Subtract(OperationCall opCall) => OperationResult.NotImplemented();
        public OperationResult Multiply(OperationCall opCall) => OperationResult.NotImplemented();
        public OperationResult Divide(OperationCall opCall) => OperationResult.NotImplemented();
        public OperationResult FloorDivide(OperationCall opCall) => OperationResult.NotImplemented();
        public OperationResult Modulo(OperationCall opCall) => OperationResult.NotImplemented();
        public OperationResult Power(OperationCall opCall) => OperationResult.NotImplemented();

        public override OperationResult ValueEquals(OperationCall opCall)
        {
            if (opCall.Arguments.Length != 2 ||
                opCall.Arguments[0].dataType != DataTypeId.String ||
                opCall.Arguments[1].dataType != DataTypeId.String)
            {
                return OperationResult.NotImplemented();
            }

            if (opCall.Arguments[0].value == opCall.Arguments[1].value)
                return OperationResult.Success(DataValue.FromBool(true));

            if (!TryResolve(opCall, opCall.Arguments[0], out var left, out var leftError))
                return leftError.Status == OperationStatus.Error ? leftError : OperationResult.NotImplemented();

            if (!TryResolve(opCall, opCall.Arguments[1], out var right, out var rightError))
                return rightError.Status == OperationStatus.Error ? rightError : OperationResult.NotImplemented();

            return OperationResult.Success(
                DataValue.FromBool(
                    string.Equals(left.Value, right.Value, StringComparison.Ordinal)
                )
            );
        }

        public OperationResult ReferenceEquals(OperationCall opCall)
        {
            if (opCall.Arguments.Length != 2)
                return OperationResult.NotImplemented();

            DataValue left = opCall.Arguments[0];
            DataValue right = opCall.Arguments[1];

            return OperationResult.Success(
                DataValue.FromBool(
                    left.dataType == DataTypeId.String &&
                    right.dataType == DataTypeId.String &&
                    left.value == right.value
                )
            );
        }
    }
}
