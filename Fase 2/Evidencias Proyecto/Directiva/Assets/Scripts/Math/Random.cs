using System;

namespace DMath
{
    public sealed class DeterministicRandom
    {
        private ulong state;
        private readonly ulong increment;

        public DeterministicRandom(ulong seed, ulong sequence = 54UL)
        {
            state = 0;
            increment = unchecked((sequence << 1) | 1UL);

            NextUInt();
            state = unchecked(state + seed);
            NextUInt();
        }

        // Genera un entero de 32 bits sin signo.
        public uint NextUInt()
        {
            ulong oldState = state;

            state = unchecked(
                oldState * 6364136223846793005UL + increment
            );

            uint shifted = unchecked(
                (uint)(((oldState >> 18) ^ oldState) >> 27)
            );

            int rotation = (int)(oldState >> 59);

            return (shifted >> rotation)
                | (shifted << ((-rotation) & 31));
        }

        // Entero en [minInclusive, maxExclusive).
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (minInclusive >= maxExclusive)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive));

            uint range = (uint)((long)maxExclusive - minInclusive);
            uint threshold = unchecked(0u - range) % range;

            uint value;

            do
            {
                value = NextUInt();
            }
            while (value < threshold);

            return (int)((long)minInclusive + (value % range));
        }

        // Fixed en [0, 1).
        public Fixed NextFixed()
        {
            long raw = NextInt(0, (int)Constants.FixedScale);
            return new Fixed(raw, raw: true);
        }

        // Fixed en [minInclusive, maxExclusive).
        public Fixed NextFixed(Fixed minInclusive, Fixed maxExclusive)
        {
            if (minInclusive >= maxExclusive)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive));

            ulong range = unchecked(
                (ulong)(maxExclusive.RawValue - minInclusive.RawValue)
            );

            ulong threshold = unchecked(0UL - range) % range;

            ulong value;

            do
            {
                value = ((ulong)NextUInt() << 32) | NextUInt();
            }
            while (value < threshold);

            long raw = unchecked(
                (long)((ulong)minInclusive.RawValue + value % range)
            );

            return new Fixed(raw, raw: true);
        }
    }
}