using System;
using DSExecution.Values;

namespace DSExecution.VirtualMachine
{
    /// <summary>
    /// Indexed local storage for one call frame. Every slot starts as Uninitialized.
    /// </summary>
    public sealed class LocalMemory
    {
        private readonly DataValue[] values;

        public int Count => values.Length;

        internal LocalMemory(int localCount)
        {
            if (localCount < 0)
                throw new ArgumentOutOfRangeException(nameof(localCount));

            values = new DataValue[localCount];
            ResetRange(0, localCount);
        }

        public DataValue Get(int localIndex)
        {
            if ((uint)localIndex >= (uint)values.Length)
                throw new ArgumentOutOfRangeException(nameof(localIndex));

            return values[localIndex];
        }

        internal bool TryGet(int localIndex, out DataValue value)
        {
            if ((uint)localIndex >= (uint)values.Length)
            {
                value = default;
                return false;
            }

            value = values[localIndex];
            return true;
        }

        internal bool TrySet(int localIndex, DataValue value)
        {
            if ((uint)localIndex >= (uint)values.Length)
                return false;

            values[localIndex] = value;
            return true;
        }

        internal bool IsValidRange(int firstLocal, int localCount)
        {
            if (firstLocal < 0 || localCount < 0)
                return false;

            long end = (long)firstLocal + localCount;
            return end <= values.Length;
        }

        internal void ResetRange(int firstLocal, int localCount)
        {
            if (!IsValidRange(firstLocal, localCount))
                throw new ArgumentOutOfRangeException(nameof(firstLocal));

            int end = firstLocal + localCount;
            for (int i = firstLocal; i < end; i++)
                values[i] = DataValue.Uninitialized();
        }
    }
}
