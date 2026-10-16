using System.Collections.Generic;
using DSExecution.Values;

namespace DSExecution.VirtualMachine
{
    /// <summary>
    /// Evaluation stack shared by all call frames in one VM.
    /// Each frame owns the portion above its EvalStackBase.
    /// </summary>
    internal sealed class EvaluationStack
    {
        private readonly DataValue[] values;
        private int count;

        public int Count => count;

        public EvaluationStack(int capacity)
        {
            values = new DataValue[capacity];
        }

        public bool TryPush(DataValue value)
        {
            if (count >= values.Length)
                return false;

            values[count++] = value;
            return true;
        }

        public bool TryPop(int floor, out DataValue value)
        {
            if (count <= floor)
            {
                value = default;
                return false;
            }

            count--;
            value = values[count];
            values[count] = default;
            return true;
        }

        internal IEnumerable<DataValue> EnumerateValues()
        {
            for (int i = 0; i < count; i++)
                yield return values[i];
        }

        public bool HasValuesAbove(int floor, int amount)
            => floor >= 0 && floor <= count && amount >= 0 && count - floor >= amount;

        /// <summary>
        /// Whether amount values could be replaced atomically by one result value.
        /// For an empty tuple this also checks the stack capacity before heap allocation.
        /// </summary>
        public bool CanReplaceTopWithOne(int floor, int amount)
            => HasValuesAbove(floor, amount) && (amount > 0 || count < values.Length);

        /// <summary>
        /// Copies the top amount values in stack order without removing GC roots.
        /// </summary>
        public bool TryPeekArguments(int floor, int amount, out DataValue[] arguments)
        {
            if (!HasValuesAbove(floor, amount))
            {
                arguments = System.Array.Empty<DataValue>();
                return false;
            }

            arguments = new DataValue[amount];
            System.Array.Copy(values, count - amount, arguments, 0, amount);
            return true;
        }

        /// <summary>
        /// Commits an already prepared result by removing amount values and pushing one.
        /// Callers should use CanReplaceTopWithOne before allocations that could invoke GC.
        /// </summary>
        public bool TryReplaceTopWithOne(int floor, int amount, DataValue replacement)
        {
            if (!CanReplaceTopWithOne(floor, amount))
                return false;

            TrimTo(count - amount);
            return TryPush(replacement);
        }

        /// <summary>
        /// Pops a call/operation argument list while preserving the original left-to-right order.
        /// </summary>
        public bool TryPopArguments(int floor, int amount, out DataValue[] arguments)
        {
            if (!HasValuesAbove(floor, amount))
            {
                arguments = System.Array.Empty<DataValue>();
                return false;
            }

            arguments = new DataValue[amount];

            for (int i = amount - 1; i >= 0; i--)
            {
                count--;
                arguments[i] = values[count];
                values[count] = default;
            }

            return true;
        }

        /// <summary>
        /// Discards every temporary value above the requested stack size.
        /// </summary>
        public void TrimTo(int newCount)
        {
            if (newCount < 0 || newCount > count)
                throw new System.ArgumentOutOfRangeException(nameof(newCount));

            while (count > newCount)
            {
                count--;
                values[count] = default;
            }
        }
    }
}
