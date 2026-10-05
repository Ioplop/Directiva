using System.Numerics;

namespace DSExecution.Operations
{
    public sealed class IntPowerState : OperationState
    {
        public BigInteger Result { get; set; }
        public BigInteger Factor { get; set; }
        public ulong RemainingExponent { get; set; }
        public bool NegativeExponent { get; set; }

        public IntPowerState(
            long baseValue,
            ulong exponent,
            bool negativeExponent)
        {
            Result = BigInteger.One;
            Factor = baseValue;
            RemainingExponent = exponent;
            NegativeExponent = negativeExponent;
        }
    }
}