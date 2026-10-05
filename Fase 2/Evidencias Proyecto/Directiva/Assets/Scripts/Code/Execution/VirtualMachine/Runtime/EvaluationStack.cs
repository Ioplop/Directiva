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

        public bool HasValuesAbove(int floor, int amount)
            => amount >= 0 && count - floor >= amount;

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
