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

        /// <summary>
        /// Returns the heap reference carried by this value when its type is heap-backed.
        /// </summary>
        public bool TryGetHeapReference(out uint memoryReference)
        {
            switch (dataType)
            {
                case DataTypeId.String:
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