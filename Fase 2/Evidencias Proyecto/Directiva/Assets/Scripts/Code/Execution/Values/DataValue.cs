using DSExecution.DataTypes;

namespace DSExecution.Values
{
    public readonly struct DataValue
    {
        public readonly DataTypeId dataType;
        public readonly ulong value;

        public DataValue(DataTypeId dataType, ulong value)
        {
            this.dataType = dataType;
            this.value = value;
        }

        public static DataValue Uninitialized()
        {
            return new DataValue(
                DataTypeId.Uninitialized,
                0
            );
        }

        public static DataValue None()
        {
            return new DataValue(
                DataTypeId.None,
                0
            );
        }

        // Factories por cada data type id

        public static DataValue FromInt(long value)
        {
            return new DataValue(DataTypeId.Int, unchecked((ulong)value));
        }

        public static DataValue FromBool(bool value)
        {
            return new DataValue(DataTypeId.Bool, value ? 1UL : 0UL);
        }

        public static DataValue FromDecimalRaw(long value)
        {
            return new DataValue(DataTypeId.Decimal, unchecked((ulong)value));
        }

        public static DataValue FromStringReference(uint memoryReference)
        {
            if (memoryReference == 0)
                throw new System.ArgumentOutOfRangeException(nameof(memoryReference));

            return new DataValue(DataTypeId.String, memoryReference);
        }

        public static DataValue FromListReference(uint memoryReference)
        {
            if (memoryReference == 0)
                throw new System.ArgumentOutOfRangeException(nameof(memoryReference));
            return new DataValue(DataTypeId.List, memoryReference);
        }

        public static DataValue FromSetReference(uint memoryReference)
        {
            if (memoryReference == 0)
                throw new System.ArgumentOutOfRangeException(nameof(memoryReference));
            return new DataValue(DataTypeId.Set, memoryReference);
        }

        public static DataValue FromDictReference(uint memoryReference)
        {
            if (memoryReference == 0)
                throw new System.ArgumentOutOfRangeException(nameof(memoryReference));
            return new DataValue(DataTypeId.Dict, memoryReference);
        }

        /// <summary>References an already compiled function by its program-local id.</summary>
        public static DataValue FromFunctionId(int functionId)
        {
            if (functionId < 0)
                throw new System.ArgumentOutOfRangeException(nameof(functionId));

            return new DataValue(DataTypeId.Function, (ulong)functionId);
        }

        /// <summary>Decodes a function id without truncating invalid raw values.</summary>
        public bool TryGetFunctionId(out int functionId)
        {
            if (dataType == DataTypeId.Function && value <= int.MaxValue)
            {
                functionId = (int)value;
                return true;
            }

            functionId = -1;
            return false;
        }

        public static DataValue FromTupleReference(uint memoryReference)
        {
            if (memoryReference == 0)
                throw new System.ArgumentOutOfRangeException(nameof(memoryReference));

            return new DataValue(DataTypeId.Tuple, memoryReference);
        }

        /// <summary>
        /// Returns the heap reference carried by this value when its type is heap-backed.
        /// </summary>
        public bool TryGetHeapReference(out uint memoryReference)
        {
            switch (dataType)
            {
                case DataTypeId.String:
                case DataTypeId.Tuple:
                case DataTypeId.List:
                case DataTypeId.Set:
                case DataTypeId.Dict:
                    if (value == 0 || value > uint.MaxValue)
                        break;

                    memoryReference = (uint)value;
                    return true;
            }

            memoryReference = 0;
            return false;
        }

        // TODO: Implement other factories
    }
}