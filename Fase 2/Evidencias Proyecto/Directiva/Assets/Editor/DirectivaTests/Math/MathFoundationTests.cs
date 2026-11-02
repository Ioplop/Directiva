using System;
using System.Collections.Generic;
using DMath;

namespace Directiva.EditorTests.MathTests
{
    internal static class MathFoundationTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string group, string name, string description, Action<DirectivaTestContext> action)
                => tests.Add(new DirectivaTestCase("Math", group, name, description, action));

            Add("Fixed", "CeilExactIntegers", "CeilToLong preserves positive and negative whole values", t =>
            {
                t.Equal(10L, Fixed.CeilToLong(10), "ceil 10");
                t.Equal(-2L, Fixed.CeilToLong(-2), "ceil -2");
                t.Equal(0L, Fixed.CeilToLong(0), "ceil zero");
            });

            Add("Fixed", "CeilFractionalValues", "CeilToLong rounds toward positive infinity", t =>
            {
                t.Equal(2L, Fixed.CeilToLong(new Fixed(12500, raw: true)), "ceil 1.25");
                t.Equal(-1L, Fixed.CeilToLong(new Fixed(-12500, raw: true)), "ceil -1.25");
                t.Equal(0L, Fixed.CeilToLong(new Fixed(-2500, raw: true)), "ceil -0.25");
            });

            Add("Fixed", "FloorNegativeFraction", "FloorToLong handles negative fractions", t =>
            {
                t.Equal(-2L, Fixed.FloorToLong(new Fixed(-12500, raw: true)), "floor -1.25");
                t.Equal(-1L, Fixed.FloorToLong(new Fixed(-2500, raw: true)), "floor -0.25");
                t.Equal(2L, Fixed.FloorToLong(2), "floor exact 2");
            });

            Add("Fixed", "ScaleAndArithmetic", "Fixed math respects four decimal places", t =>
            {
                Fixed a = new Fixed(12500, raw: true);
                Fixed b = new Fixed(5000, raw: true);
                t.Equal(12500L, a.RawValue, "raw representation");
                t.Equal(new Fixed(17500, raw: true), a + b, "addition");
                t.Equal(new Fixed(7500, raw: true), a - b, "subtraction");
                t.Equal(new Fixed(6250, raw: true), a * b, "multiplication");
                t.Equal(new Fixed(25000, raw: true), a / b, "division");
            });

            Add("Fixed", "RoundTies", "RoundToLong ties round away from zero", t =>
            {
                t.Equal(2L, Fixed.RoundToLong(new Fixed(15000, raw: true)), "round +1.5");
                t.Equal(-2L, Fixed.RoundToLong(new Fixed(-15000, raw: true)), "round -1.5");
                t.Equal(0L, Fixed.RoundToLong(new Fixed(4999, raw: true)), "round 0.4999");
            });

            Add("Vectors", "CardinalDirections", "FixedVector2 exposes expected exact cardinal vectors", t =>
            {
                t.Equal(new FixedVector2(1, 0), FixedVector2.Right, "right");
                t.Equal(new FixedVector2(-1, 0), FixedVector2.Left, "left");
                t.Equal(new FixedVector2(0, 1), FixedVector2.Up, "up");
                t.Equal(new FixedVector2(0, -1), FixedVector2.Down, "down");
            });

            Add("Vectors", "MagnitudeAndNormalize", "(3,4) has length 5 and unit direction (0.6,0.8)", t =>
            {
                var vector = new FixedVector2(3, 4);
                t.Equal((Fixed)5, vector.Magnitude, "magnitude");
                t.Equal((Fixed)25, vector.SqrMagnitude, "squared magnitude");
                t.Equal(new FixedVector2(new Fixed(6000, raw: true), new Fixed(8000, raw: true)),
                    vector.Normalized, "normalized");
            });

            Add("Vectors", "ClampMagnitude", "Clamps a (6,8) vector to exact length 5", t =>
            {
                var vector = new FixedVector2(6, 8);
                t.Equal(new FixedVector2(3, 4), vector.ClampMagnitude(5), "clamped vector");
                t.Equal(vector, vector.ClampMagnitude(10), "unchanged vector");
            });

            Add("Vectors", "RotateExact", "Cardinal rotations preserve exact integer coordinates", t =>
            {
                var right = FixedVector2.Right;
                t.Equal(FixedVector2.Up, right.Rotate90(), "90 degrees");
                t.Equal(FixedVector2.Left, right.Rotate180(), "180 degrees");
                t.Equal(FixedVector2.Down, right.Rotate270(), "270 degrees");
            });

            Add("Random", "SeedReproducibility", "Same-seed PCG32 generators produce identical sequences", t =>
            {
                var a = new DeterministicRandom(123456);
                var b = new DeterministicRandom(123456);
                for (int i = 0; i < 64; i++)
                    t.Equal(a.NextUInt(), b.NextUInt(), "PCG value " + i);
            });

            Add("Random", "CopyContinuesSequence", "Copy preserves the PRNG's exact current state", t =>
            {
                var original = new DeterministicRandom(77);
                for (int i = 0; i < 13; i++)
                    original.NextUInt();
                var clone = original.Copy();
                for (int i = 0; i < 24; i++)
                    t.Equal(original.NextUInt(), clone.NextUInt(), "next value " + i);
            });

            Add("Random", "BoundsIntAndFixed", "Random values obey exclusive upper bounds", t =>
            {
                var random = new DeterministicRandom(3);
                var min = new Fixed(12500, raw: true);
                var max = new Fixed(27500, raw: true);
                for (int i = 0; i < 128; i++)
                {
                    int value = random.NextInt(-5, 7);
                    t.Check(value >= -5 && value < 7, "int value out of range");
                    Fixed fixedValue = random.NextFixed(min, max);
                    t.Check(fixedValue >= min && fixedValue < max, "Fixed value out of range");
                }
            });

            Add("Trigonometry", "CardinalAngles", "Trig lookup tables are exact on cardinal directions", t =>
            {
                t.Equal((Fixed)0, Trigonometry.Sin(0), "sin(0)");
                t.Equal((Fixed)1, Trigonometry.Sin(90), "sin(90)");
                t.Equal((Fixed)1, Trigonometry.Cos(0), "cos(0)");
                t.Equal((Fixed)0, Trigonometry.Cos(90), "cos(90)");
            });

            Add("Trigonometry", "Atan2Axes", "Atan2 uses the conventional (y,x) argument ordering", t =>
            {
                t.Equal((Fixed)0, Trigonometry.Atan2(0, 1), "positive X");
                t.Equal((Fixed)90, Trigonometry.Atan2(1, 0), "positive Y");
                t.Equal((Fixed)180, Trigonometry.Atan2(0, -1), "negative X");
                t.Equal((Fixed)270, Trigonometry.Atan2(-1, 0), "negative Y");
                t.Equal((Fixed)90, Trigonometry.Atan2(FixedVector2.Up), "vector overload");
            });

            Add("Trigonometry", "Atan2ZeroRejected", "Atan2 rejects a zero vector", t =>
            {
                bool rejected = false;
                try { Trigonometry.Atan2(FixedVector2.Zero); }
                catch (ArgumentException) { rejected = true; }
                t.Check(rejected, "atan2(0, 0) should throw");
            });
        }
    }
}
